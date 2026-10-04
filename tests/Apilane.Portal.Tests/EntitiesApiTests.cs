using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class EntitiesApiTests
    {
        private readonly PortalFactory _portal;

        public EntitiesApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Application ----------

        [Fact]
        public async Task Application_Should_Count_Its_Custom_Endpoints_In_Get_And_In_The_List()
        {
            var scene = await CreateSceneAsync();

            await _portal.WithDbContextAsync(db =>
            {
                db.CustomEndpoints.Add(new DBWS_CustomEndpoint { AppID = scene.AppId, Name = "First", Query = "select 1", DateModified = DateTime.UtcNow });
                db.CustomEndpoints.Add(new DBWS_CustomEndpoint { AppID = scene.AppId, Name = "Second", Query = "select 2", DateModified = DateTime.UtcNow });
                return db.SaveChangesAsync();
            });

            var one = await (await scene.Owner.GetAsync(scene.AppUrl)).ReadJsonAsync<ApplicationResponse>();
            Assert.Equal(2, one.CustomEndpointCount);
            Assert.Equal(1, one.CollaboratorCount);

            var shared = await (await scene.Collaborator.GetAsync(scene.AppUrl)).ReadJsonAsync<ApplicationResponse>();
            Assert.Equal(2, shared.CustomEndpointCount);

            var list = await (await scene.Owner.GetAsync("/api/v1/applications")).ReadJsonAsync<ListResponse<ApplicationResponse>>();
            Assert.Equal(2, Assert.Single(list.Data, x => x.Token == scene.Token).CustomEndpointCount);
        }

        [Fact]
        public async Task Application_Without_Custom_Endpoints_Should_Count_Zero()
        {
            var scene = await CreateSceneAsync();

            var application = await (await scene.Owner.GetAsync(scene.AppUrl)).ReadJsonAsync<ApplicationResponse>();

            Assert.Equal(0, application.CustomEndpointCount);
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Should_Return_Custom_And_System_Entities_By_Name_Without_Properties()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.GetAsync(scene.EntitiesUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var list = await response.ReadJsonAsync<ListResponse<EntityResponse>>();

            // Sorted by name without regard to letter case: 'archive' before 'Customers', whatever order they were added in.
            Assert.Equal(new[] { "archive", "Customers", "Files", "Invoices", "Orders", "Users" }, list.Data.Select(x => x.Name));
            Assert.Equal(6, list.Total);
            Assert.All(list.Data, x => Assert.Null(x.Properties));
            Assert.Equal(new[] { "Files", "Users" }, list.Data.Where(x => x.IsSystem).Select(x => x.Name));

            // Reading does not involve the API server.
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task List_With_IncludeProperties_Should_Give_The_Properties_Of_Each_Entity_As_Get_Does()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.GetAsync($"{scene.EntitiesUrl}?IncludeProperties=true");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var list = (await response.ReadJsonAsync<ListResponse<EntityResponse>>()).Data;

            Assert.Equal(new[] { "archive", "Customers", "Files", "Invoices", "Orders", "Users" }, list.Select(x => x.Name));

            foreach (var entity in list)
            {
                var single = await (await scene.Owner.GetAsync($"{scene.EntitiesUrl}/{entity.Name}")).ReadJsonAsync<EntityResponse>();
                var listed = entity.Properties ?? throw new InvalidOperationException($"{entity.Name} has no Properties.");

                Assert.NotEmpty(listed);
                Assert.Equal((single.Properties ?? new List<PropertyResponse>()).Select(x => x.Name), listed.Select(x => x.Name));
            }

            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task List_Should_Describe_Entities_With_The_Flags_Of_The_Model()
        {
            var scene = await CreateSceneAsync();

            var list = (await (await scene.Owner.GetAsync(scene.EntitiesUrl)).ReadJsonAsync<ListResponse<EntityResponse>>()).Data;

            var orders = Assert.Single(list, x => x.Name == "Orders");
            Assert.Equal("Customer orders", orders.Description);
            Assert.False(orders.IsSystem);
            Assert.False(orders.IsReadOnly);
            Assert.True(orders.RequireChangeTracking);
            Assert.False(orders.HasDifferentiationProperty);
            Assert.True(orders.AllowPost);
            Assert.True(orders.AllowPut);
            Assert.True(orders.AllowDelete);
            Assert.True(orders.AllowAddProperties);

            // Users register: no POST. Files cannot be updated and take no new properties.
            var users = Assert.Single(list, x => x.Name == "Users");
            Assert.True(users.IsSystem);
            Assert.False(users.AllowPost);
            Assert.True(users.AllowPut);
            Assert.True(users.AllowAddProperties);

            var files = Assert.Single(list, x => x.Name == "Files");
            Assert.True(files.AllowPost);
            Assert.False(files.AllowPut);
            Assert.True(files.AllowDelete);
            Assert.False(files.AllowAddProperties);

            var archive = Assert.Single(list, x => x.Name == "archive");
            Assert.True(archive.IsReadOnly);
            Assert.False(archive.AllowPost);
            Assert.False(archive.AllowPut);
            Assert.False(archive.AllowDelete);
            Assert.False(archive.AllowAddProperties);
            Assert.Null(archive.Description);
        }

        [Fact]
        public async Task List_Should_Return_Constraints_As_A_Structured_List()
        {
            var scene = await CreateSceneAsync();

            var list = (await (await scene.Owner.GetAsync(scene.EntitiesUrl)).ReadJsonAsync<ListResponse<EntityResponse>>()).Data;

            var customers = Assert.Single(list, x => x.Name == "Customers").Constraints;
            Assert.Equal(2, customers.Count);

            Assert.Equal("Unique", customers[0].Type);
            Assert.False(customers[0].IsSystem);
            Assert.Equal(new[] { "Name", "Owner" }, customers[0].Properties);
            Assert.Null(customers[0].Property);
            Assert.Null(customers[0].ForeignEntity);
            Assert.Null(customers[0].OnDelete);

            Assert.Equal("ForeignKey", customers[1].Type);
            Assert.True(customers[1].IsSystem);
            Assert.Empty(customers[1].Properties);
            Assert.Equal("Owner", customers[1].Property);
            Assert.Equal("Users", customers[1].ForeignEntity);
            Assert.Equal("ON_DELETE_SET_NULL", customers[1].OnDelete);

            var order = Assert.Single(Assert.Single(list, x => x.Name == "Orders").Constraints);
            Assert.Equal("ForeignKey", order.Type);
            Assert.Equal("Customer_ID", order.Property);
            Assert.Equal("Customers", order.ForeignEntity);
            Assert.Equal("ON_DELETE_CASCADE", order.OnDelete);

            Assert.Empty(Assert.Single(list, x => x.Name == "Invoices").Constraints);
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData("null")]
        [InlineData("[null]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"  \"}]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":null}]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"OnlyOnePart\"}]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"A,B,C,D\"}]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Prop,Customers,ON_DELETE_SOMETHING\"}]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":7,\"Properties\":\"Prop\"}]")]
        public async Task Stored_Constraints_That_Cannot_Be_Read_Should_Be_Left_Out_And_Break_Nothing(string stored)
        {
            var scene = await CreateSceneAsync();
            await SetConstraintsAsync(scene, "Invoices", stored);
            ScriptApiServer();

            var list = await (await scene.Owner.GetAsync(scene.EntitiesUrl)).ReadJsonAsync<ListResponse<EntityResponse>>();
            Assert.Empty(Assert.Single(list.Data, x => x.Name == "Invoices").Constraints);

            var one = await (await scene.Owner.GetAsync(scene.EntityUrl("Invoices"))).ReadJsonAsync<EntityResponse>();
            Assert.Empty(one.Constraints);

            // Such a value does not get in the way: rename and delete of the other entities still work.
            // (The entity such a foreign key points to stays protected: see the 409 theory under Delete.)
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.EntityUrl("Orders"))).StatusCode);
        }

        [Fact]
        public async Task Foreign_Key_Stored_Without_An_On_Delete_Choice_Should_Read_As_No_Action()
        {
            var scene = await CreateSceneAsync();
            await SetConstraintsAsync(scene, "Invoices", "[{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers\"}]");

            var entity = await (await scene.Owner.GetAsync(scene.EntityUrl("Invoices"))).ReadJsonAsync<EntityResponse>();

            var constraint = Assert.Single(entity.Constraints);
            Assert.Equal("ForeignKey", constraint.Type);
            Assert.Equal("Customer_ID", constraint.Property);
            Assert.Equal("Customers", constraint.ForeignEntity);
            Assert.Equal("ON_DELETE_NO_ACTION", constraint.OnDelete);
        }

        [Fact]
        public async Task Stored_Constraint_With_Spaces_After_The_Commas_Should_Read_As_Clean_Names_And_Count_As_A_Reference()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetConstraintsAsync(scene, "Invoices",
                "[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Name, Owner\"}," +
                "{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID, Customers\"}]");

            var entity = await (await scene.Owner.GetAsync(scene.EntityUrl("Invoices"))).ReadJsonAsync<EntityResponse>();
            Assert.Equal(2, entity.Constraints.Count);
            Assert.Equal(new[] { "Name", "Owner" }, entity.Constraints[0].Properties);
            Assert.Equal("Customer_ID", entity.Constraints[1].Property);
            Assert.Equal("Customers", entity.Constraints[1].ForeignEntity);

            // Orders points to Customers too; with it gone, Invoices is the one that is named.
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.EntityUrl("Orders"))).StatusCode);
            await AssertConflictAsync(await scene.Owner.DeleteAsync(scene.EntityUrl("Customers")), "Cannot delete entity as it is referenced by 'Invoices'");
        }

        [Fact]
        public async Task List_Application_Without_Entities_Should_Return_An_Empty_List()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var seeded = await _portal.CreateApplicationAsync(server.ID, email, "no-entities");
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var list = await (await client.GetAsync($"/api/v1/applications/{seeded.Application.Token}/entities")).ReadJsonAsync<ListResponse<EntityResponse>>();

            Assert.Empty(list.Data);
            Assert.Equal(0, list.Total);
        }

        // ---------- Get one ----------

        [Fact]
        public async Task Get_Should_Return_The_Entity_With_Its_Key_Then_Its_Custom_Properties_Then_The_System_Ones()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.GetAsync(scene.EntityUrl("Orders"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var entity = await response.ReadJsonAsync<EntityResponse>();
            Assert.Equal("Orders", entity.Name);
            Assert.Equal("Customer orders", entity.Description);
            Assert.True(entity.RequireChangeTracking);
            Assert.Single(entity.Constraints);

            // The primary key, then the custom properties by name, then the system ones by name.
            var properties = entity.Properties ?? throw new InvalidOperationException("No Properties.");
            Assert.Equal(new[] { "ID", "Amount", "Customer_ID", "Paid", "Secret", "Created", "Owner" }, properties.Select(x => x.Name));

            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Get_Should_Describe_Properties_With_The_Flags_Of_The_Model()
        {
            var scene = await CreateSceneAsync();

            var entity = await (await scene.Owner.GetAsync(scene.EntityUrl("Orders"))).ReadJsonAsync<EntityResponse>();
            var properties = (entity.Properties ?? throw new InvalidOperationException("No Properties.")).ToDictionary(x => x.Name);

            var id = properties["ID"];
            Assert.Equal("Number", id.Type);
            Assert.True(id.IsPrimaryKey);
            Assert.True(id.IsSystem);
            Assert.False(id.AllowEdit);
            Assert.False(id.AllowMin);
            Assert.False(id.AllowMaxEdit);
            Assert.False(id.AllowValidationRegex);
            Assert.False(id.IsUtc);

            var amount = properties["Amount"];
            Assert.Equal("Number", amount.Type);
            Assert.Equal("The total", amount.Description);
            Assert.False(amount.IsPrimaryKey);
            Assert.False(amount.IsSystem);
            Assert.True(amount.Required);
            Assert.Equal(2, amount.DecimalPlaces);
            Assert.Equal(0, amount.Minimum);
            Assert.Equal(1000, amount.Maximum);
            Assert.True(amount.AllowEdit);
            Assert.True(amount.AllowMin);
            Assert.True(amount.AllowMaxEdit);
            Assert.False(amount.AllowValidationRegex);

            var secret = properties["Secret"];
            Assert.Equal("String", secret.Type);
            Assert.True(secret.Encrypted);
            Assert.Equal("^[A-Z]+$", secret.ValidationRegex);
            Assert.Equal(50, secret.Maximum);
            Assert.Null(secret.Minimum);
            Assert.Null(secret.DecimalPlaces);
            Assert.True(secret.AllowMin);
            Assert.False(secret.AllowMaxEdit);
            Assert.True(secret.AllowValidationRegex);

            var paid = properties["Paid"];
            Assert.Equal("Boolean", paid.Type);
            Assert.False(paid.Required);
            Assert.False(paid.AllowMin);
            Assert.False(paid.AllowMaxEdit);

            // Set by the API server, never by a client.
            var created = properties["Created"];
            Assert.Equal("Date", created.Type);
            Assert.True(created.IsSystem);
            Assert.True(created.IsUtc);
            Assert.False(created.AllowEdit);
            Assert.False(properties["Owner"].AllowEdit);
        }

        [Fact]
        public async Task Get_Differentiation_Property_Should_Not_Be_Editable()
        {
            var scene = await CreateSceneAsync(differentiationEntity: "Company");

            await _portal.WithDbContextAsync(async db =>
            {
                var orders = await db.Entities.SingleAsync(x => x.AppID == scene.AppId && x.Name == "Orders");
                orders.HasDifferentiationProperty = true;
                db.EntityProperties.Add(Property("Company_ID", PropertyType.Number, isSystem: true, entityId: orders.ID));
                return await db.SaveChangesAsync();
            });

            var application = await (await scene.Owner.GetAsync(scene.AppUrl)).ReadJsonAsync<ApplicationResponse>();
            Assert.Equal("Company", application.DifferentiationEntity);

            var entity = await (await scene.Owner.GetAsync(scene.EntityUrl("Orders"))).ReadJsonAsync<EntityResponse>();
            Assert.True(entity.HasDifferentiationProperty);
            Assert.False(Assert.Single(entity.Properties ?? new List<PropertyResponse>(), x => x.Name == "Company_ID").AllowEdit);
        }

        [Theory]
        [InlineData("Nothing")]
        [InlineData("orders")]
        [InlineData("ORDERS")]
        public async Task Unknown_Or_Differently_Cased_Entity_Should_Return_404_And_Change_Nothing(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var url = scene.EntityUrl(name);

            await AssertNotFoundAsync(await scene.Owner.GetAsync(url), "Entity");
            await AssertNotFoundAsync(await scene.Owner.PutAsync(url, new { Description = "x", RequireChangeTracking = false }.ToJsonContent()), "Entity");
            await AssertNotFoundAsync(await scene.Owner.PostAsync($"{url}/rename", new { NewName = "Renamed" }.ToJsonContent()), "Entity");
            await AssertNotFoundAsync(await scene.Owner.DeleteAsync(url), "Entity");

            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(new[] { "Users", "Files", "Orders", "Customers", "Invoices", "archive" }, (await LoadEntitiesAsync(scene)).Select(x => x.Name));
            Assert.Equal("Customer orders", (await LoadEntityAsync(scene, "Orders")).Description);
        }

        // ---------- Access ----------

        [Fact]
        public async Task Stranger_Admin_And_Unknown_Token_Should_Return_404_For_Every_Endpoint_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            var admin = await _portal.CreateAdminClientAsync();
            ScriptApiServer();

            foreach (var send in AllRequests())
            {
                await AssertNotFoundAsync(await send(scene.Stranger, scene.AppUrl), "Application");
                await AssertNotFoundAsync(await send(admin, scene.AppUrl), "Application");
                await AssertNotFoundAsync(await send(scene.Owner, $"/api/v1/applications/{Guid.NewGuid()}"), "Application");
            }

            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Anonymous_Should_Return_401_For_Every_Endpoint()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var anonymous = _portal.CreateAnonymousClient();

            foreach (var send in AllRequests())
            {
                await AssertUnauthorizedAsync(await send(anonymous, scene.AppUrl));
            }

            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Write_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            foreach (var send in WriteRequests())
            {
                var response = await send(scene.Owner, scene.AppUrl);

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Forbidden, error.Code);
                Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
            }

            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Collaborator_Should_Be_Able_To_Do_Everything_The_Owner_Can()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var client = scene.Collaborator;

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(scene.EntitiesUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(scene.EntityUrl("Orders"))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsync(scene.EntitiesUrl, CreateBody("Shipments").ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(scene.EntityUrl("Shipments"), new { Description = "By a collaborator", RequireChangeTracking = true }.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{scene.EntityUrl("Shipments")}/rename", new { NewName = "Deliveries" }.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(scene.EntityUrl("Deliveries"))).StatusCode);

            // Every call to the API server was made as the collaborator.
            var bearer = $"Bearer {await StoredTokenAsync(scene.CollaboratorEmail)}";
            Assert.Equal(8, _portal.ApiServer.Requests.Count);
            Assert.All(_portal.ApiServer.Requests, x => Assert.Equal(bearer, x.Headers["Authorization"]));

            await AssertSeedUnchangedAsync(scene);

            // And audited under the collaborator's name.
            var audit = await AuditRowsAsync(scene.CollaboratorEmail);
            Assert.Equal(
                new[] { "Created", "Modified", "Modified", "Deleted" },
                audit.Where(x => x.EntityType == "Entity").Select(x => x.Action));
        }

        // ---------- Create ----------

        [Fact]
        public async Task Create_Should_Return_201_With_The_Entity_And_Its_System_Properties()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Shipments");
            body["Description"] = "Parcels on the road";
            body["RequireChangeTracking"] = true;

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal($"{scene.EntitiesUrl}/Shipments", response.Headers.Location?.ToString());
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var created = await response.ReadJsonAsync<EntityResponse>();
            Assert.Equal("Shipments", created.Name);
            Assert.Equal("Parcels on the road", created.Description);
            Assert.True(created.RequireChangeTracking);
            Assert.False(created.HasDifferentiationProperty);
            Assert.False(created.IsSystem);
            Assert.False(created.IsReadOnly);
            Assert.True(created.AllowAddProperties);
            Assert.Equal(new[] { "ID", "Created", "Owner" }, (created.Properties ?? new List<PropertyResponse>()).Select(x => x.Name));

            var constraint = Assert.Single(created.Constraints);
            Assert.True(constraint.IsSystem);
            Assert.Equal("Owner", constraint.Property);
            Assert.Equal("Users", constraint.ForeignEntity);

            // The same answer from GET, and the entity is in the list.
            var read = await (await scene.Owner.GetAsync(scene.EntityUrl("Shipments"))).ReadJsonAsync<EntityResponse>();
            Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(read));

            var list = await (await scene.Owner.GetAsync(scene.EntitiesUrl)).ReadJsonAsync<ListResponse<EntityResponse>>();
            Assert.Contains(list.Data, x => x.Name == "Shipments");
        }

        [Fact]
        public async Task Create_Should_Send_Three_Requests_To_The_Api_Server_And_Store_The_Entity()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Shipments");
            body["Description"] = "Parcels on the road";

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, body.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var stored = await LoadEntityAsync(scene, "Shipments");
            var requests = _portal.ApiServer.Requests;
            Assert.Equal(3, requests.Count);

            // 1. What a new entity starts with.
            Assert.Equal(HttpMethod.Get, requests[0].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/GetSystemPropertiesAndConstraints?entityHasDifferentiationProperty=False", requests[0].Url);
            await AssertPortalHeadersAsync(requests[0], scene, scene.OwnerEmail);

            // 2. The table, before anything is saved in the Portal: the IDs are still 0.
            Assert.Equal(HttpMethod.Post, requests[1].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/GenerateEntity", requests[1].Url);
            await AssertPortalHeadersAsync(requests[1], scene, scene.OwnerEmail);

            var sent = JsonSerializer.Deserialize<DBWS_Entity>(requests[1].Body) ?? throw new InvalidOperationException("No GenerateEntity body.");
            Assert.Equal(0, sent.ID);
            Assert.Equal(scene.AppId, sent.AppID);
            Assert.Equal("Shipments", sent.Name);
            Assert.Equal("Parcels on the road", sent.Description);
            Assert.False(sent.IsSystem);
            Assert.False(sent.IsReadOnly);
            Assert.False(sent.RequireChangeTracking);
            Assert.False(sent.HasDifferentiationProperty);
            Assert.Equal(new[] { "ID", "Owner", "Created" }, sent.Properties.Select(x => x.Name));
            Assert.All(sent.Properties, x => Assert.Equal(0, x.ID));
            Assert.Equal(JsonSerializer.Serialize(SystemPropertiesAndConstraints().Constraints), sent.EntConstraints);

            // 3. The cache reset, after the row was saved.
            Assert.Equal(HttpMethod.Get, requests[2].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", requests[2].Url);
            await AssertPortalHeadersAsync(requests[2], scene, scene.OwnerEmail);

            Assert.True(stored.ID > 0);
            Assert.Equal(scene.AppId, stored.AppID);
            Assert.Equal("Parcels on the road", stored.Description);
            Assert.False(stored.IsSystem);
            Assert.False(stored.IsReadOnly);
            Assert.Equal(sent.EntConstraints, stored.EntConstraints);
            Assert.Null(stored.EntDefaultOrder);
            Assert.Equal(new[] { "ID", "Owner", "Created" }, stored.Properties.OrderBy(x => x.ID).Select(x => x.Name));
            Assert.All(stored.Properties, x => Assert.Equal(stored.ID, x.EntityID));
            Assert.All(stored.Properties, x => Assert.True(x.IsSystem));
        }

        [Fact]
        public async Task Create_Should_Write_Audit_Rows_For_The_Entity_And_Its_Properties()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody("Shipments").ToJsonContent());

            var audit = await AuditRowsAsync(scene.OwnerEmail);

            Assert.Equal(4, audit.Count);

            var entity = Assert.Single(audit, x => x.EntityType == "Entity");
            Assert.Equal("Shipments", entity.EntityIdentifier);
            Assert.Equal("Created", entity.Action);
            Assert.Equal(scene.AppId, entity.AppID);
            Assert.Contains("Shipments", entity.Changes);

            Assert.Equal(
                new[] { "Created", "ID", "Owner" },
                audit.Where(x => x.EntityType == "Property" && x.Action == "Created").Select(x => x.EntityIdentifier).OrderBy(x => x));

            // The rows of the system properties of a new entity carry no application.
            Assert.All(audit.Where(x => x.EntityType == "Property"), x => Assert.Null(x.AppID));
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("abcdefghijklmnopqrstuvwxyzabcde")]
        [InlineData("with space")]
        [InlineData("Digits1")]
        [InlineData("dash-ed")]
        [InlineData("Ünicode")]
        [InlineData("")]
        [InlineData(null)]
        public async Task Create_Invalid_Name_Should_Return_400_On_Name_And_Create_Nothing(string? name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody(name).ToJsonContent());

            await AssertValidationAsync(response, "Name", null);
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Create_Name_With_Other_Characters_Should_Say_Which_Are_Allowed()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody("Digits1").ToJsonContent());

            await AssertValidationAsync(response, "Name", "Allowed characters are a-z, A-Z and _");
        }

        [Theory]
        [InlineData("abcd")]
        [InlineData("abcdefghijklmnopqrstuvwxyzabcd")]
        [InlineData("_with_under_scores_")]
        public async Task Create_Should_Accept_The_Shortest_And_The_Longest_Name_And_Underscores(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody(name).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(name, (await LoadEntityAsync(scene, name)).Name);
        }

        [Theory]
        [InlineData("Orders")]
        [InlineData("ORDERS")]
        [InlineData("users")]
        public async Task Create_Existing_Name_In_Any_Case_Should_Return_409_And_Create_Nothing(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody(name).ToJsonContent());

            await AssertConflictAsync(response, $"Entity '{name}' already exists");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Create_Same_Name_In_Another_Application_Should_Work()
        {
            var first = await CreateSceneAsync();
            var second = await CreateSceneAsync();
            ScriptApiServer();

            Assert.Equal(HttpStatusCode.Created, (await first.Owner.PostAsync(first.EntitiesUrl, CreateBody("Shipments").ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await second.Owner.PostAsync(second.EntitiesUrl, CreateBody("Shipments").ToJsonContent())).StatusCode);

            // Each in its own application.
            await AssertNotFoundAsync(await first.Owner.GetAsync(second.EntityUrl("Shipments")), "Application");
        }

        [Fact]
        public async Task Create_With_Differentiation_Property_In_An_Application_Without_One_Should_Return_400()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Shipments");
            body["HasDifferentiationProperty"] = true;

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, body.ToJsonContent());

            await AssertValidationAsync(response, "HasDifferentiationProperty", "The application has no differentiation entity");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Create_With_Differentiation_Property_Should_Ask_The_Api_Server_For_It_And_Store_The_Flag()
        {
            var scene = await CreateSceneAsync(differentiationEntity: "Company");
            ScriptApiServer();

            var body = CreateBody("Shipments");
            body["HasDifferentiationProperty"] = true;

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.True((await response.ReadJsonAsync<EntityResponse>()).HasDifferentiationProperty);
            Assert.True((await LoadEntityAsync(scene, "Shipments")).HasDifferentiationProperty);

            Assert.Equal(
                $"{scene.ServerUrl}/api/Application/GetSystemPropertiesAndConstraints?entityHasDifferentiationProperty=True",
                _portal.ApiServer.Requests[0].Url);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task Create_Blank_Description_Should_Be_Stored_As_None(string? description)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Shipments");
            body["Description"] = description;

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null((await LoadEntityAsync(scene, "Shipments")).Description);
        }

        [Fact]
        public async Task Create_Should_Ignore_Values_A_Client_May_Not_Set()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Shipments");
            body["IsSystem"] = true;
            body["IsReadOnly"] = true;
            body["ID"] = 1;
            body["AppID"] = 1;
            body["EntConstraints"] = "[]";
            body["EntDefaultOrder"] = "[{\"Property\":\"ID\",\"Direction\":\"ASC\"}]";
            body["Properties"] = Array.Empty<object>();

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var stored = await LoadEntityAsync(scene, "Shipments");
            Assert.False(stored.IsSystem);
            Assert.False(stored.IsReadOnly);
            Assert.NotEqual(1L, stored.ID);
            Assert.NotEqual("[]", stored.EntConstraints);
            Assert.Null(stored.EntDefaultOrder);
            Assert.Equal(3, stored.Properties.Count);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "{\"Message\":\"Cannot say.\"}", "Cannot say.")]
        [InlineData(HttpStatusCode.OK, "this is not json", "Invalid response from Api server")]
        [InlineData(HttpStatusCode.OK, "null", "Invalid response from Api server")]
        [InlineData(HttpStatusCode.OK, "{\"Properties\":null,\"Constraints\":[]}", "Invalid response from Api server")]
        [InlineData(HttpStatusCode.OK, "{\"Properties\":[null],\"Constraints\":[]}", "Invalid response from Api server")]
        [InlineData(HttpStatusCode.OK, "{\"Properties\":[]}", "Invalid response from Api server")]
        public async Task Create_When_The_System_Properties_Cannot_Be_Read_Should_Return_502_And_Not_Generate(HttpStatusCode upstream, string answer, string message)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GetSystemPropertiesAndConstraintsPath, upstream, answer);

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody("Shipments").ToJsonContent());

            await AssertUpstreamAsync(response, message);
            Assert.Single(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Create_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message_And_Leave_The_Portal_Unchanged()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GenerateEntityPath, HttpStatusCode.BadRequest, ApiError("Entity Shipments already exists", "Name"));

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody("Shipments").ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("Entity Shipments already exists", error.Message);
            Assert.Equal("Name", Assert.Single(error.Errors ?? new List<ErrorDetail>()).Property);

            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Create_When_The_Api_Server_Fails_Should_Return_502_And_Leave_The_Portal_Unchanged(bool unreachable)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            Fail(FakeApiServer.GenerateEntityPath, unreachable);

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody("Shipments").ToJsonContent());

            await AssertUpstreamAsync(response, unreachable ? ApiServerClient.UnusableAnswerMessage : "It broke.");
            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        [Fact]
        public async Task Create_When_The_Portal_Cannot_Save_After_Generate_Should_Return_500_That_Says_So_And_Not_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // A property without a name: a real API server never answers one, this one does, and the
            // Portal database cannot store it.
            _portal.ApiServer.Respond(
                FakeApiServer.GetSystemPropertiesAndConstraintsPath,
                HttpStatusCode.OK,
                "{\"Properties\":[{\"Name\":null,\"TypeID\":1}],\"Constraints\":[]}");

            var response = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody("Shipments").ToJsonContent());

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Error, error.Code);
            Assert.Equal("The change was made on the API server but could not be saved in the Portal.", error.Message);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateEntityPath));
            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        // ---------- Update ----------

        [Fact]
        public async Task Update_Should_Save_Description_And_Change_Tracking_And_Only_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.EntityUrl("Invoices"),
                new { Description = "What customers owe", RequireChangeTracking = true }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var updated = await response.ReadJsonAsync<EntityResponse>();
            Assert.Equal("Invoices", updated.Name);
            Assert.Equal("What customers owe", updated.Description);
            Assert.True(updated.RequireChangeTracking);
            Assert.NotNull(updated.Properties);

            var stored = await LoadEntityAsync(scene, "Invoices");
            Assert.Equal("What customers owe", stored.Description);
            Assert.True(stored.RequireChangeTracking);

            // The cache reset is the only way the API server learns about it.
            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", request.Url);
            await AssertPortalHeadersAsync(request, scene, scene.OwnerEmail);

            var audit = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Entity", audit.EntityType);
            Assert.Equal("Invoices", audit.EntityIdentifier);
            Assert.Equal("Modified", audit.Action);
            Assert.Equal(scene.AppId, audit.AppID);
            Assert.Contains("What customers owe", audit.Changes);
            Assert.Contains("RequireChangeTracking", audit.Changes);
        }

        [Fact]
        public async Task Update_Should_Change_Nothing_Else_Of_The_Entity()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var before = await LoadEntityAsync(scene, "Orders");

            var response = await scene.Owner.PutAsync(
                scene.EntityUrl("Orders"),
                new { Description = "", RequireChangeTracking = false, Name = "Hacked", IsSystem = true, IsReadOnly = true }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var after = await LoadEntityAsync(scene, "Orders");
            Assert.Null(after.Description);
            Assert.False(after.RequireChangeTracking);
            Assert.Equal(before.Name, after.Name);
            Assert.False(after.IsSystem);
            Assert.False(after.IsReadOnly);
            Assert.Equal(before.EntConstraints, after.EntConstraints);
            Assert.Equal(before.Properties.Count, after.Properties.Count);
        }

        [Fact]
        public async Task Update_System_Entity_Should_Work()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.EntityUrl("Users"),
                new { Description = "People", RequireChangeTracking = true }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var stored = await LoadEntityAsync(scene, "Users");
            Assert.Equal("People", stored.Description);
            Assert.True(stored.RequireChangeTracking);
            Assert.True(stored.IsSystem);
        }

        [Theory]
        [InlineData("Files")]
        [InlineData("archive")]
        public async Task Update_Change_Tracking_For_An_Entity_That_Cannot_Be_Updated_Should_Return_400(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var refused = await scene.Owner.PutAsync(scene.EntityUrl(name), new { Description = "x", RequireChangeTracking = true }.ToJsonContent());

            await AssertValidationAsync(refused, "RequireChangeTracking", null);
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);

            // The description alone can be changed.
            var accepted = await scene.Owner.PutAsync(scene.EntityUrl(name), new { Description = "x", RequireChangeTracking = false }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal("x", (await LoadEntityAsync(scene, name)).Description);
        }

        [Theory]
        [InlineData("{\"Description\":\"x\"}")]
        [InlineData("{\"Description\":\"x\",\"RequireChangeTracking\":null}")]
        public async Task Update_Without_Change_Tracking_Should_Return_400_And_Change_Nothing(string body)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // Orders has change tracking on: a value left out must not turn it off.
            var response = await scene.Owner.PutAsync(scene.EntityUrl("Orders"), new StringContent(body, Encoding.UTF8, "application/json"));

            await AssertValidationAsync(response, "RequireChangeTracking", "Required");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Update_Without_A_Change_Should_Still_Reset_The_Cache_And_Write_No_Audit_Row()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.EntityUrl("Orders"),
                new { Description = "Customer orders", RequireChangeTracking = true }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
        }

        // ---------- Rename ----------

        [Fact]
        public async Task Rename_Should_Call_The_Api_Server_With_The_Entity_ID_Then_Save_Then_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var before = await LoadEntityAsync(scene, "Invoices");

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl("Invoices")}/rename", new { NewName = "Bills" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var renamed = await response.ReadJsonAsync<EntityResponse>();
            Assert.Equal("Bills", renamed.Name);
            Assert.NotNull(renamed.Properties);

            var requests = _portal.ApiServer.Requests;
            Assert.Equal(2, requests.Count);

            Assert.Equal(HttpMethod.Get, requests[0].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/RenameEntity?ID={before.ID}&NewName=Bills", requests[0].Url);
            await AssertPortalHeadersAsync(requests[0], scene, scene.OwnerEmail);

            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", requests[1].Url);

            // The same row under its new name; the old URL is gone.
            Assert.Equal(before.ID, (await LoadEntityAsync(scene, "Bills")).ID);
            await AssertNotFoundAsync(await scene.Owner.GetAsync(scene.EntityUrl("Invoices")), "Entity");
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync(scene.EntityUrl("Bills"))).StatusCode);

            var audit = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Entity", audit.EntityType);
            Assert.Equal("Modified", audit.Action);
            Assert.Equal("Bills", audit.EntityIdentifier);
            Assert.Equal(scene.AppId, audit.AppID);
            Assert.Contains("Invoices", audit.Changes);
            Assert.Contains("Bills", audit.Changes);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("abcdefghijklmnopqrstuvwxyzabcde")]
        [InlineData("with space")]
        [InlineData("Digits1")]
        [InlineData("")]
        [InlineData(null)]
        public async Task Rename_Invalid_Name_Should_Return_400_On_NewName_And_Change_Nothing(string? newName)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl("Invoices")}/rename", new { NewName = newName }.ToJsonContent());

            await AssertValidationAsync(response, "NewName", null);
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Theory]
        [InlineData("Users")]
        [InlineData("Files")]
        public async Task Rename_System_Entity_Should_Return_409(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl(name)}/rename", new { NewName = "Renamed" }.ToJsonContent());

            await AssertConflictAsync(response, "Cannot rename system Entities");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Rename_Entity_Referenced_By_A_Foreign_Key_Should_Return_409_Naming_The_Entity_That_Refers_To_It()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl("Customers")}/rename", new { NewName = "Clients" }.ToJsonContent());

            await AssertConflictAsync(response, "Cannot rename entity as it is referenced by 'Orders'");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Rename_Entity_Referenced_By_Its_Own_Foreign_Key_Should_Return_409()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetConstraintsAsync(scene, "Invoices", "[{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Parent_ID,Invoices,ON_DELETE_NO_ACTION\"}]");

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl("Invoices")}/rename", new { NewName = "Bills" }.ToJsonContent());

            await AssertConflictAsync(response, "Cannot rename entity as it is referenced by 'Invoices'");
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Theory]
        [InlineData("Orders")]
        [InlineData("ORDERS")]
        [InlineData("users")]
        public async Task Rename_To_The_Name_Of_Another_Entity_In_Any_Case_Should_Return_409(string newName)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl("Invoices")}/rename", new { NewName = newName }.ToJsonContent());

            await AssertConflictAsync(response, $"Entity '{newName}' already exists");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Theory]
        [InlineData("Invoices")]
        [InlineData("INVOICES")]
        public async Task Rename_To_Its_Own_Name_Should_Be_Left_To_The_Api_Server(string newName)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl("Invoices")}/rename", new { NewName = newName }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.RenameEntityPath));
            Assert.Equal(newName, (await LoadEntityAsync(scene, newName)).Name);
        }

        [Fact]
        public async Task Rename_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message_And_Keep_The_Name()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.RenameEntityPath, HttpStatusCode.BadRequest, ApiError("Entity named 'Bills' already exists"));

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl("Invoices")}/rename", new { NewName = "Bills" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("Entity named 'Bills' already exists", error.Message);

            await AssertNothingSavedAndNoCacheResetAsync(scene);

            // The old name still answers.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync(scene.EntityUrl("Invoices"))).StatusCode);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Rename_When_The_Api_Server_Fails_Should_Return_502_And_Keep_The_Name(bool unreachable)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            Fail(FakeApiServer.RenameEntityPath, unreachable);

            var response = await scene.Owner.PostAsync($"{scene.EntityUrl("Invoices")}/rename", new { NewName = "Bills" }.ToJsonContent());

            await AssertUpstreamAsync(response, unreachable ? ApiServerClient.UnusableAnswerMessage : "It broke.");
            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_Should_Call_The_Api_Server_Then_Remove_The_Entity_With_Its_Properties_Then_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var before = await LoadEntityAsync(scene, "Orders");

            var response = await scene.Owner.DeleteAsync(scene.EntityUrl("Orders"));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var requests = _portal.ApiServer.Requests;
            Assert.Equal(2, requests.Count);

            Assert.Equal(HttpMethod.Get, requests[0].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/DegenerateEntity?Entity=Orders", requests[0].Url);
            await AssertPortalHeadersAsync(requests[0], scene, scene.OwnerEmail);

            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", requests[1].Url);

            Assert.DoesNotContain(await LoadEntitiesAsync(scene), x => x.Name == "Orders");
            Assert.False(await _portal.WithDbContextAsync(db => db.EntityProperties.AnyAsync(x => x.EntityID == before.ID)));
            await AssertNotFoundAsync(await scene.Owner.GetAsync(scene.EntityUrl("Orders")), "Entity");

            // The other entities are still there.
            Assert.Equal(new[] { "Users", "Files", "Customers", "Invoices", "archive" }, (await LoadEntitiesAsync(scene)).Select(x => x.Name));

            var audit = await AuditRowsAsync(scene.OwnerEmail);

            Assert.Equal(1 + before.Properties.Count, audit.Count);

            var entity = Assert.Single(audit, x => x.EntityType == "Entity");
            Assert.Equal("Orders", entity.EntityIdentifier);
            Assert.Equal("Deleted", entity.Action);
            Assert.Equal(scene.AppId, entity.AppID);

            Assert.Equal(
                before.Properties.Select(x => x.Name).OrderBy(x => x),
                audit.Where(x => x.EntityType == "Property" && x.Action == "Deleted").Select(x => x.EntityIdentifier).OrderBy(x => x));
        }

        [Theory]
        [InlineData("Users")]
        [InlineData("Files")]
        public async Task Delete_System_Entity_Should_Return_409(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.DeleteAsync(scene.EntityUrl(name));

            await AssertConflictAsync(response, "Cannot delete system Entities");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Delete_Entity_Referenced_By_A_Foreign_Key_Should_Return_409_And_Say_Delete()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.DeleteAsync(scene.EntityUrl("Customers"));

            await AssertConflictAsync(response, "Cannot delete entity as it is referenced by 'Orders'");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);

            // Once the entity that points to it is gone, it can be deleted.
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.EntityUrl("Orders"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.EntityUrl("Customers"))).StatusCode);
        }

        [Theory]
        [InlineData("Prop,Customers,ON_DELETE_SOMETHING")]
        [InlineData("Prop,Customers,5")]
        [InlineData("Prop,Customers,ON_DELETE_CASCADE,Extra")]
        public async Task Rename_And_Delete_Entity_Referenced_By_A_Foreign_Key_That_Cannot_Be_Read_Should_Return_409(string properties)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // Invoices is the only entity that points to Customers, with a value the responses leave out.
            await SetConstraintsAsync(scene, "Orders", "[]");
            await SetConstraintsAsync(scene, "Invoices", $"[{{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"{properties}\"}}]");

            var deleted = await scene.Owner.DeleteAsync(scene.EntityUrl("Customers"));
            await AssertConflictAsync(deleted, "Cannot delete entity as it is referenced by 'Invoices'");

            var renamed = await scene.Owner.PostAsync($"{scene.EntityUrl("Customers")}/rename", new { NewName = "Clients" }.ToJsonContent());
            await AssertConflictAsync(renamed, "Cannot rename entity as it is referenced by 'Invoices'");

            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(new[] { "Users", "Files", "Orders", "Customers", "Invoices", "archive" }, (await LoadEntitiesAsync(scene)).Select(x => x.Name));
            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
        }

        [Fact]
        public async Task Delete_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message_And_Keep_The_Entity()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.DegenerateEntityPath, HttpStatusCode.BadRequest, ApiError("Cannot delete entity 'Invoices'"));

            var response = await scene.Owner.DeleteAsync(scene.EntityUrl("Invoices"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Cannot delete entity 'Invoices'", (await response.ReadJsonAsync<ErrorResponse>()).Message);

            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Delete_When_The_Api_Server_Fails_Should_Return_502_And_Keep_The_Entity(bool unreachable)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            Fail(FakeApiServer.DegenerateEntityPath, unreachable);

            var response = await scene.Owner.DeleteAsync(scene.EntityUrl("Invoices"));

            await AssertUpstreamAsync(response, unreachable ? ApiServerClient.UnusableAnswerMessage : "It broke.");
            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        // ---------- Cache reset ----------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Write_When_The_Cache_Reset_Fails_Should_Still_Succeed_With_A_Warning_Header(bool unreachable)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            if (unreachable)
            {
                _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);
            }
            else
            {
                _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.BadRequest, ApiError("No."));
            }

            var created = await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody("Shipments").ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            AssertWarning(created);
            Assert.Equal("Shipments", (await created.ReadJsonAsync<EntityResponse>()).Name);

            var updated = await scene.Owner.PutAsync(scene.EntityUrl("Shipments"), new { Description = "Saved anyway", RequireChangeTracking = false }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            AssertWarning(updated);

            var renamed = await scene.Owner.PostAsync($"{scene.EntityUrl("Shipments")}/rename", new { NewName = "Deliveries" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            AssertWarning(renamed);

            // Each change was saved.
            Assert.Equal("Saved anyway", (await LoadEntityAsync(scene, "Deliveries")).Description);

            var deleted = await scene.Owner.DeleteAsync(scene.EntityUrl("Deliveries"));
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            AssertWarning(deleted);

            await AssertSeedUnchangedAsync(scene);

            // One reset per write, tried once.
            Assert.Equal(4, _portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath).Count);
        }

        [Fact]
        public async Task Each_Successful_Write_Should_Reset_The_Cache_Once_As_Its_Last_Call()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            await scene.Owner.PostAsync(scene.EntitiesUrl, CreateBody("Shipments").ToJsonContent());
            AssertEndsWithOneCacheReset(3);

            await scene.Owner.PutAsync(scene.EntityUrl("Shipments"), new { Description = "x", RequireChangeTracking = false }.ToJsonContent());
            AssertEndsWithOneCacheReset(1);

            await scene.Owner.PostAsync($"{scene.EntityUrl("Shipments")}/rename", new { NewName = "Deliveries" }.ToJsonContent());
            AssertEndsWithOneCacheReset(2);

            await scene.Owner.DeleteAsync(scene.EntityUrl("Deliveries"));
            AssertEndsWithOneCacheReset(2);
        }

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Warning_Header_Of_Every_Entity_Write()
        {
            var client = await _portal.CreateUserClientAsync();
            var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")) ?? throw new InvalidOperationException("No document.");

            const string entities = "/api/v1/applications/{appToken}/entities";

            Assert.NotNull(document["paths"]?[entities]?["post"]?["responses"]?["201"]?["headers"]?["Warning"]);
            Assert.NotNull(document["paths"]?[$"{entities}/{{entity}}"]?["put"]?["responses"]?["200"]?["headers"]?["Warning"]);
            Assert.NotNull(document["paths"]?[$"{entities}/{{entity}}"]?["delete"]?["responses"]?["204"]?["headers"]?["Warning"]);
            Assert.NotNull(document["paths"]?[$"{entities}/{{entity}}/rename"]?["post"]?["responses"]?["200"]?["headers"]?["Warning"]);
        }

        // ---------- Helpers ----------

        private record Scene(
            string OwnerEmail,
            HttpClient Owner,
            string CollaboratorEmail,
            HttpClient Collaborator,
            HttpClient Stranger,
            string ServerUrl,
            string Token,
            long AppId)
        {
            public string AppUrl => $"/api/v1/applications/{Token}";

            public string EntitiesUrl => $"{AppUrl}/entities";

            public string EntityUrl(string name) => $"{EntitiesUrl}/{name}";
        }

        /// <summary>
        /// Three new users and one application of the owner, shared with the collaborator, with two
        /// system entities and four custom ones, added out of the order of their names. 'Orders'
        /// has a foreign key to 'Customers'; nothing points to 'Invoices'; 'archive' is read-only.
        /// </summary>
        private async Task<Scene> CreateSceneAsync(string? differentiationEntity = null)
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await _portal.CreateUserAsync();

            var server = await _portal.CreateServerAsync();
            var seeded = await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"entities-{Guid.NewGuid():N}", collaboratorEmail);
            var appId = seeded.Application.ID;

            await _portal.WithDbContextAsync(async db =>
            {
                if (differentiationEntity is not null)
                {
                    (await db.Applications.SingleAsync(x => x.ID == appId)).DifferentiationEntity = differentiationEntity;
                }

                db.Entities.AddRange(SeedEntities(appId));

                return await db.SaveChangesAsync();
            });

            return new Scene(
                ownerEmail,
                await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword),
                collaboratorEmail,
                await _portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword),
                await _portal.CreateSignedInClientAsync(strangerEmail, strangerPassword),
                server.ServerUrl,
                seeded.Application.Token,
                appId);
        }

        private static List<DBWS_Entity> SeedEntities(long appId)
        {
            var users = Entity(appId, "Users", true,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                Property("Email", PropertyType.String, isSystem: true),
                Property("LastLogin", PropertyType.Date, isSystem: true));

            var files = Entity(appId, "Files", true,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true));

            var orders = Entity(appId, "Orders", false,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                Property("Owner", PropertyType.Number, isSystem: true),
                Property("Created", PropertyType.Date, isSystem: true),
                Property("Customer_ID", PropertyType.Number),
                Property("Paid", PropertyType.Boolean),
                Property("Amount", PropertyType.Number),
                Property("Secret", PropertyType.String));

            orders.Description = "Customer orders";
            orders.RequireChangeTracking = true;
            orders.EntConstraints = "[{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,ON_DELETE_CASCADE\"}]";

            orders.Properties[3].DecimalPlaces = 0;

            var amount = orders.Properties[5];
            amount.Description = "The total";
            amount.Required = true;
            amount.DecimalPlaces = 2;
            amount.Minimum = 0;
            amount.Maximum = 1000;

            var secret = orders.Properties[6];
            secret.Encrypted = true;
            secret.ValidationRegex = "^[A-Z]+$";
            secret.Maximum = 50;

            var customers = Entity(appId, "Customers", false,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                Property("Owner", PropertyType.Number, isSystem: true),
                Property("Name", PropertyType.String));

            customers.EntConstraints =
                "[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Name,Owner\"}," +
                "{\"IsSystem\":true,\"TypeID\":2,\"Properties\":\"Owner,Users,ON_DELETE_SET_NULL\"}]";

            var invoices = Entity(appId, "Invoices", false,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true));

            var archive = Entity(appId, "archive", false,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true));

            archive.IsReadOnly = true;

            return new List<DBWS_Entity> { users, files, orders, customers, invoices, archive };
        }

        private static DBWS_Entity Entity(long appId, string name, bool isSystem, params DBWS_EntityProperty[] properties)
        {
            return new DBWS_Entity
            {
                AppID = appId,
                Name = name,
                IsSystem = isSystem,
                DateModified = DateTime.UtcNow,
                Properties = properties.ToList()
            };
        }

        private static DBWS_EntityProperty Property(string name, PropertyType type, bool isSystem = false, bool isPrimaryKey = false, long entityId = 0)
        {
            return new DBWS_EntityProperty
            {
                EntityID = entityId,
                Name = name,
                TypeID = (int)type,
                IsSystem = isSystem,
                IsPrimaryKey = isPrimaryKey,
                Required = isPrimaryKey,
                DateModified = DateTime.UtcNow
            };
        }

        /// <summary>
        /// What the API server answers for a new entity: numbered -1, as it returns them.
        /// </summary>
        private static EntityPropertiesConstrainsDto SystemPropertiesAndConstraints()
        {
            var id = Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true, entityId: -1);
            var owner = Property("Owner", PropertyType.Number, isSystem: true, entityId: -1);
            var created = Property("Created", PropertyType.Date, isSystem: true, entityId: -1);

            foreach (var property in new[] { id, owner, created })
            {
                property.ID = -1;
                property.DateModified = default;
            }

            return new EntityPropertiesConstrainsDto
            {
                Properties = new List<DBWS_EntityProperty> { id, owner, created },
                Constraints = new List<EntityConstraint>
                {
                    new EntityConstraint { IsSystem = true, TypeID = (int)ConstraintType.ForeignKey, Properties = "Owner,Users,ON_DELETE_SET_NULL" }
                }
            };
        }

        /// <summary>
        /// The API server answers every call of this slice the way a healthy one does.
        /// </summary>
        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.GetSystemPropertiesAndConstraintsPath, HttpStatusCode.OK, JsonSerializer.Serialize(SystemPropertiesAndConstraints()));
            _portal.ApiServer.Respond(FakeApiServer.GenerateEntityPath, HttpStatusCode.OK, string.Empty);
            _portal.ApiServer.Respond(FakeApiServer.RenameEntityPath, HttpStatusCode.OK, string.Empty);
            _portal.ApiServer.Respond(FakeApiServer.DegenerateEntityPath, HttpStatusCode.OK, string.Empty);
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        private void Fail(string path, bool unreachable)
        {
            if (unreachable)
            {
                _portal.ApiServer.Unreachable(path);
            }
            else
            {
                _portal.ApiServer.Respond(path, HttpStatusCode.InternalServerError, ApiError("It broke."));
            }
        }

        private static Dictionary<string, object?> CreateBody(string? name)
        {
            return new Dictionary<string, object?>
            {
                ["Name"] = name,
                ["Description"] = null,
                ["RequireChangeTracking"] = false,
                ["HasDifferentiationProperty"] = false
            };
        }

        private static string ApiError(string message, string? property = null)
        {
            return JsonSerializer.Serialize(new { Code = "ERROR", Message = message, Property = property, Entity = (string?)null });
        }

        /// <summary>
        /// One request to each of the seven endpoints of an application, valid for the owner.
        /// </summary>
        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> AllRequests()
        {
            var requests = new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, appUrl) => client.GetAsync(appUrl),
                (client, appUrl) => client.GetAsync($"{appUrl}/entities"),
                (client, appUrl) => client.GetAsync($"{appUrl}/entities/Orders")
            };

            requests.AddRange(WriteRequests());

            return requests;
        }

        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> WriteRequests()
        {
            return new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, appUrl) => client.PostAsync($"{appUrl}/entities", CreateBody("Shipments").ToJsonContent()),
                (client, appUrl) => client.PutAsync($"{appUrl}/entities/Invoices", new { Description = "Changed", RequireChangeTracking = true }.ToJsonContent()),
                (client, appUrl) => client.PostAsync($"{appUrl}/entities/Invoices/rename", new { NewName = "Bills" }.ToJsonContent()),
                (client, appUrl) => client.DeleteAsync($"{appUrl}/entities/Invoices")
            };
        }

        /// <summary>
        /// The entities of the application with their properties, in the order they were added.
        /// </summary>
        private async Task<List<DBWS_Entity>> LoadEntitiesAsync(Scene scene)
        {
            var entities = await _portal.WithDbContextAsync(db => db.Entities
                .AsNoTracking()
                .Include(x => x.Properties)
                .Where(x => x.AppID == scene.AppId)
                .ToListAsync());

            return entities.OrderBy(x => x.ID).ToList();
        }

        private async Task<DBWS_Entity> LoadEntityAsync(Scene scene, string name)
        {
            return Assert.Single(await LoadEntitiesAsync(scene), x => x.Name == name);
        }

        private Task<int> SetConstraintsAsync(Scene scene, string entityName, string stored)
        {
            return _portal.WithDbContextAsync(async db =>
            {
                (await db.Entities.SingleAsync(x => x.AppID == scene.AppId && x.Name == entityName)).EntConstraints = stored;

                return await db.SaveChangesAsync();
            });
        }

        /// <summary>
        /// The audit rows a user caused, oldest first. Seeding writes none: it has no signed-in request.
        /// </summary>
        private async Task<List<PortalAuditLog>> AuditRowsAsync(string userEmail)
        {
            return await _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .Where(x => x.UserEmail == userEmail)
                .OrderBy(x => x.ID)
                .ToListAsync());
        }

        /// <summary>
        /// The entities are as the scene was seeded and nobody caused an audit row.
        /// </summary>
        private async Task AssertNothingChangedAsync(Scene scene)
        {
            await AssertSeedUnchangedAsync(scene);

            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Empty(await AuditRowsAsync(scene.CollaboratorEmail));
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.AppID == scene.AppId)));
        }

        /// <summary>
        /// The seeded entities and their properties are as the scene was seeded.
        /// </summary>
        private async Task AssertSeedUnchangedAsync(Scene scene)
        {
            var expected = SeedEntities(scene.AppId);
            var stored = await LoadEntitiesAsync(scene);

            Assert.Equal(
                expected.Select(x => $"{x.Name} | {x.Description} | {x.RequireChangeTracking} | {x.EntConstraints} | {string.Join(",", x.Properties.Select(p => p.Name))}"),
                stored.Select(x => $"{x.Name} | {x.Description} | {x.RequireChangeTracking} | {x.EntConstraints} | {string.Join(",", x.Properties.OrderBy(p => p.ID).Select(p => p.Name))}"));
        }

        private async Task AssertNothingSavedAndNoCacheResetAsync(Scene scene)
        {
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            await AssertNothingChangedAsync(scene);
        }

        private void AssertEndsWithOneCacheReset(int expectedRequests)
        {
            var requests = _portal.ApiServer.Requests;

            Assert.Equal(expectedRequests, requests.Count);
            Assert.Equal(FakeApiServer.ClearCachePath, requests[^1].Path);
            Assert.Single(requests, x => x.Path == FakeApiServer.ClearCachePath);

            _portal.ApiServer.Reset();
            ScriptApiServer();
        }

        private static void AssertWarning(HttpResponseMessage response)
        {
            Assert.Equal(ApiServerCacheReset.WarningText, Assert.Single(response.Headers.GetValues(ApiServerCacheReset.WarningHeaderName)));
        }

        /// <summary>
        /// The headers every Portal call carries: the application token, the caller's own API
        /// token, the client name and Accept. Nothing else: no installation key.
        /// </summary>
        private async Task AssertPortalHeadersAsync(ApiServerRequest request, Scene scene, string callerEmail)
        {
            Assert.Equal(scene.Token, request.Headers["x-application-token"]);
            Assert.Equal("portal", request.Headers["x-client-id"]);
            Assert.Equal($"Bearer {await StoredTokenAsync(callerEmail)}", request.Headers["Authorization"]);
            Assert.Equal(new[] { "Accept", "Authorization", "x-application-token", "x-client-id" }, request.Headers.Keys.OrderBy(x => x));
        }

        private async Task<string> StoredTokenAsync(string email)
        {
            var token = await _portal.WithDbContextAsync(db => db.Users
                .AsNoTracking()
                .Where(x => x.Email == email)
                .Select(x => x.AdminAuthToken)
                .SingleAsync());

            return token ?? throw new InvalidOperationException($"{email} has no stored token.");
        }

        private static async Task AssertNotFoundAsync(HttpResponseMessage response, string entity)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Equal(entity, error.Entity);
        }

        private static async Task AssertConflictAsync(HttpResponseMessage response, string message)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Conflict, error.Code);
            Assert.Equal(message, error.Message);
            Assert.Equal("Entity", error.Entity);
        }

        private static async Task AssertValidationAsync(HttpResponseMessage response, string property, string? message)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var errors = error.Errors ?? throw new InvalidOperationException("No Errors.");
            Assert.NotEmpty(errors);
            Assert.All(errors, x => Assert.Equal(property, x.Property));
            Assert.All(errors, x => Assert.False(string.IsNullOrWhiteSpace(x.Message)));

            if (message is not null)
            {
                Assert.Equal(message, Assert.Single(errors).Message);
            }
        }

        private static async Task AssertUpstreamAsync(HttpResponseMessage response, string message)
        {
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.UpstreamError, error.Code);
            Assert.Equal(message, error.Message);
        }

        private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }
    }
}
