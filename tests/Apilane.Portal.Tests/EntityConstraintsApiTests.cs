using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Services;
using Apilane.Portal.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class EntityConstraintsApiTests
    {
        private const string CodeConstraint = "{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Code\"}";
        private const string OnDeleteNames = "ON_DELETE_NO_ACTION, ON_DELETE_SET_NULL, ON_DELETE_CASCADE";

        private readonly PortalFactory _portal;

        public EntityConstraintsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Get ----------

        [Fact]
        public async Task Get_Should_Return_The_Constraints_And_What_Can_Be_Chosen()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.GetAsync(scene.ConstraintsUrl("Orders"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.ReadJsonAsync<EntityConstraintsResponse>();

            Assert.Equal(2, body.Constraints.Count);

            Assert.Equal("ForeignKey", body.Constraints[0].Type);
            Assert.True(body.Constraints[0].IsSystem);
            Assert.Equal("Owner", body.Constraints[0].Property);
            Assert.Equal("Users", body.Constraints[0].ForeignEntity);
            Assert.Equal("ON_DELETE_SET_NULL", body.Constraints[0].OnDelete);

            Assert.Equal("Unique", body.Constraints[1].Type);
            Assert.False(body.Constraints[1].IsSystem);
            Assert.Equal(new[] { "Code" }, body.Constraints[1].Properties);

            // Every property that is not encrypted, system ones included.
            Assert.Equal(new[] { "ID", "Owner", "Created", "Customer_ID", "Agent_ID", "Amount", "Code", "Paid" }, body.Candidates.UniqueProperties);

            // Custom whole numbers only: not Owner (system), not Amount (2 decimal places).
            Assert.Equal(new[] { "Customer_ID", "Agent_ID" }, body.Candidates.ForeignKeyProperties);

            // Every entity except Files, the entity itself included.
            Assert.Equal(new[] { "Users", "Customers", "Orders", "Invoices" }, body.Candidates.ForeignEntities);
            Assert.Equal(new[] { "ON_DELETE_NO_ACTION", "ON_DELETE_SET_NULL", "ON_DELETE_CASCADE" }, body.Candidates.OnDeleteActions);

            // Reading does not involve the API server.
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Get_Entity_Without_Constraints_Should_Return_An_Empty_List_With_Candidates()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var body = await (await scene.Owner.GetAsync(scene.ConstraintsUrl("Invoices"))).ReadJsonAsync<EntityConstraintsResponse>();

            Assert.Empty(body.Constraints);
            Assert.Equal(new[] { "ID" }, body.Candidates.UniqueProperties);
            Assert.Empty(body.Candidates.ForeignKeyProperties);
            Assert.Equal(4, body.Candidates.ForeignEntities.Count);
        }

        [Fact]
        public async Task Get_System_Entity_Should_Work_For_A_User_Who_Is_Not_An_Administrator()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var body = await (await scene.Owner.GetAsync(scene.ConstraintsUrl("Users"))).ReadJsonAsync<EntityConstraintsResponse>();

            var constraint = Assert.Single(body.Constraints);
            Assert.True(constraint.IsSystem);
            Assert.Equal(new[] { "Email" }, constraint.Properties);
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData("null")]
        [InlineData("[null]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":7,\"Properties\":\"Code\"}]")]
        public async Task Stored_Constraints_That_Cannot_Be_Read_Should_Read_As_None_And_Be_Replaced_By_A_Put(string stored)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();
            await scene.SetStoredAsync("Orders", x => x.EntConstraints = stored);

            var read = await (await scene.Owner.GetAsync(scene.ConstraintsUrl("Orders"))).ReadJsonAsync<EntityConstraintsResponse>();
            Assert.Empty(read.Constraints);

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), Unique("Code").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal($"[{CodeConstraint}]", (await scene.LoadEntityAsync("Orders")).EntConstraints);
        }

        // ---------- Put ----------

        [Fact]
        public async Task Put_Unique_Should_Call_The_Api_Server_Then_Save_Then_Reset_The_Cache()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.ConstraintsUrl("Orders"),
                Body(new { Type = "Unique", Properties = new[] { "Paid", "Code" } }).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            // The stored text: the system constraint as it was, then the new one with its properties in the order sent.
            const string expected = "[" + EntityScene.OwnerConstraint + ",{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Paid,Code\"}]";
            Assert.Equal(expected, (await scene.LoadEntityAsync("Orders")).EntConstraints);

            // One request with the whole list as its body, then the cache reset.
            var requests = _portal.ApiServer.Requests;
            Assert.Equal(2, requests.Count);
            Assert.Equal(HttpMethod.Post, requests[0].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/GenerateConstraints?Entity=Orders", requests[0].Url);
            Assert.Equal(expected, requests[0].Body);
            await scene.AssertPortalHeadersAsync(requests[0], scene.OwnerEmail);
            Assert.Equal(FakeApiServer.ClearCachePath, requests[1].Path);

            // The answer is what GET answers.
            var body = await response.ReadJsonAsync<EntityConstraintsResponse>();
            Assert.Equal(2, body.Constraints.Count);
            Assert.True(body.Constraints[0].IsSystem);
            Assert.Equal(new[] { "Paid", "Code" }, body.Constraints[1].Properties);

            var read = await (await scene.Owner.GetAsync(scene.ConstraintsUrl("Orders"))).ReadJsonAsync<EntityConstraintsResponse>();
            Assert.Equal(JsonSerializer.Serialize(body), JsonSerializer.Serialize(read));

            // One audit row, for the entity.
            var audit = Assert.Single(await scene.AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Entity | Orders | Modified", $"{audit.EntityType} | {audit.EntityIdentifier} | {audit.Action}");
            Assert.Equal(scene.AppId, audit.AppID);
        }

        [Theory]
        [InlineData("ON_DELETE_NO_ACTION")]
        [InlineData("ON_DELETE_SET_NULL")]
        [InlineData("ON_DELETE_CASCADE")]
        public async Task Put_Foreign_Key_Should_Store_Property_Entity_And_On_Delete(string onDelete)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.ConstraintsUrl("Orders"),
                Body(new { Type = "ForeignKey", Property = "Customer_ID", ForeignEntity = "Customers", OnDelete = onDelete }).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var expected = $"[{EntityScene.OwnerConstraint},{{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,{onDelete}\"}}]";
            Assert.Equal(expected, (await scene.LoadEntityAsync("Orders")).EntConstraints);
            Assert.Equal(expected, Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath)).Body);

            var constraint = (await response.ReadJsonAsync<EntityConstraintsResponse>()).Constraints[1];
            Assert.Equal("ForeignKey", constraint.Type);
            Assert.False(constraint.IsSystem);
            Assert.Empty(constraint.Properties);
            Assert.Equal("Customer_ID", constraint.Property);
            Assert.Equal("Customers", constraint.ForeignEntity);
            Assert.Equal(onDelete, constraint.OnDelete);
        }

        [Fact]
        public async Task Put_Should_Take_Several_Constraints_At_Once_And_A_Foreign_Key_To_The_Entity_Itself()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.ConstraintsUrl("Orders"),
                Body(
                    new { Type = "Unique", Properties = new[] { "Code" } },
                    new { Type = "Unique", Properties = new[] { "Owner", "Amount" } },
                    new { Type = "ForeignKey", Property = "Customer_ID", ForeignEntity = "Customers", OnDelete = "ON_DELETE_CASCADE" },
                    new { Type = "ForeignKey", Property = "Agent_ID", ForeignEntity = "Orders", OnDelete = "ON_DELETE_NO_ACTION" }).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                "[" + EntityScene.OwnerConstraint + "," + CodeConstraint + "," +
                "{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Owner,Amount\"}," +
                "{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,ON_DELETE_CASCADE\"}," +
                "{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Agent_ID,Orders,ON_DELETE_NO_ACTION\"}]",
                (await scene.LoadEntityAsync("Orders")).EntConstraints);
        }

        [Fact]
        public async Task Put_Empty_List_Should_Remove_The_Custom_Constraints_And_Keep_The_System_Ones()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), Body().ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal($"[{EntityScene.OwnerConstraint}]", (await scene.LoadEntityAsync("Orders")).EntConstraints);
            Assert.Equal($"[{EntityScene.OwnerConstraint}]", Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath)).Body);
            Assert.True(Assert.Single((await response.ReadJsonAsync<EntityConstraintsResponse>()).Constraints).IsSystem);

            // An entity that has none at all stores an empty list.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PutAsync(scene.ConstraintsUrl("Invoices"), Body().ToJsonContent())).StatusCode);
            Assert.Equal("[]", (await scene.LoadEntityAsync("Invoices")).EntConstraints);
        }

        [Fact]
        public async Task Put_Should_Keep_System_Constraints_Exactly_As_They_Are_Stored()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            // The older format of a foreign key, without the on-delete choice, and a system unique constraint.
            const string system = "{\"IsSystem\":true,\"TypeID\":2,\"Properties\":\"Owner,Users\"},{\"IsSystem\":true,\"TypeID\":1,\"Properties\":\"Created,Owner\"}";
            await scene.SetStoredAsync("Orders", x => x.EntConstraints = $"[{CodeConstraint},{system}]");

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), Unique("Paid").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // System ones first, then what was sent; the custom one that was not sent is gone.
            Assert.Equal(
                $"[{system},{{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Paid\"}}]",
                (await scene.LoadEntityAsync("Orders")).EntConstraints);
        }

        [Fact]
        public async Task Put_What_Is_Already_Stored_Should_Still_Call_The_Api_Server_And_Write_No_Audit_Row()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), Unique("Code").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, _portal.ApiServer.Requests.Count);
            await scene.AssertNothingChangedAsync();
        }

        // ---------- Sending back what GET answers ----------

        [Fact]
        public async Task Get_Then_Put_Should_Rewrite_A_Foreign_Key_Stored_Without_On_Delete_As_No_Action()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            // The older format, from before the on-delete choice existed.
            await scene.SetStoredAsync("Orders", x => x.EntConstraints =
                $"[{EntityScene.OwnerConstraint},{{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers\"}},{CodeConstraint}]");

            var body = await (await scene.Owner.GetAsync(scene.ConstraintsUrl("Orders"))).ReadJsonAsync<EntityConstraintsResponse>();

            var legacy = body.Constraints[1];
            Assert.Equal("Customer_ID", legacy.Property);
            Assert.Equal("Customers", legacy.ForeignEntity);
            Assert.Equal("ON_DELETE_NO_ACTION", legacy.OnDelete);

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), new { Constraints = body.Constraints.Where(x => !x.IsSystem) }.ToJsonContent());

            // The API server compares foreign keys by property and entity, so the table does not change.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                $"[{EntityScene.OwnerConstraint},{{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,ON_DELETE_NO_ACTION\"}},{CodeConstraint}]",
                (await scene.LoadEntityAsync("Orders")).EntConstraints);
        }

        [Theory]
        // Older versions stored a second foreign key on the same property and entity with another action.
        [InlineData("Customer_ID,Customers,ON_DELETE_CASCADE", 2, "Customer_ID,Customers,ON_DELETE_SET_NULL", 2)]
        // Only from an import or hand-edited data.
        [InlineData("Code,Paid", 1, "Paid,Code", 1)]
        public async Task Get_Then_Put_Of_Stored_Duplicates_Should_Return_400_And_Change_Nothing(string first, int firstType, string second, int secondType)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var stored =
                $"[{EntityScene.OwnerConstraint}," +
                $"{{\"IsSystem\":false,\"TypeID\":{firstType},\"Properties\":\"{first}\"}}," +
                $"{{\"IsSystem\":false,\"TypeID\":{secondType},\"Properties\":\"{second}\"}}]";
            await scene.SetStoredAsync("Orders", x => x.EntConstraints = stored);

            var body = await (await scene.Owner.GetAsync(scene.ConstraintsUrl("Orders"))).ReadJsonAsync<EntityConstraintsResponse>();
            Assert.Equal(3, body.Constraints.Count);

            // Refused on purpose: the user keeps one of the two first.
            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), new { Constraints = body.Constraints.Where(x => !x.IsSystem) }.ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "Constraints[1]: The entity already has this constraint");
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(stored, (await scene.LoadEntityAsync("Orders")).EntConstraints);
            Assert.Empty(await scene.AuditRowsAsync(scene.OwnerEmail));
        }

        [Theory]
        [InlineData("Customer_ID,Customers,ON_DELETE_NO_ACTION")]
        // The older format reads as no action.
        [InlineData("Customer_ID,Customers")]
        public async Task Put_That_Changes_The_On_Delete_Of_A_Stored_Foreign_Key_Should_Return_400_And_Change_Nothing(string properties)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var stored = $"[{EntityScene.OwnerConstraint},{{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"{properties}\"}}]";
            await scene.SetStoredAsync("Orders", x => x.EntConstraints = stored);

            // The API server would see the same foreign key and leave the table as it is.
            var response = await scene.Owner.PutAsync(
                scene.ConstraintsUrl("Orders"),
                Body(new { Type = "ForeignKey", Property = "Customer_ID", ForeignEntity = "Customers", OnDelete = "ON_DELETE_CASCADE" }).ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "Constraints[0].OnDelete: To change what happens on delete, remove the foreign key and save, then add it again");
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(stored, (await scene.LoadEntityAsync("Orders")).EntConstraints);
            Assert.Empty(await scene.AuditRowsAsync(scene.OwnerEmail));
        }

        [Fact]
        public async Task Put_Should_Keep_Either_Of_Two_Stored_Rows_Of_One_Foreign_Key()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            await scene.SetStoredAsync("Orders", x => x.EntConstraints =
                $"[{EntityScene.OwnerConstraint}," +
                "{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,ON_DELETE_CASCADE\"}," +
                "{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,ON_DELETE_SET_NULL\"}]");

            var response = await scene.Owner.PutAsync(
                scene.ConstraintsUrl("Orders"),
                Body(new { Type = "ForeignKey", Property = "Customer_ID", ForeignEntity = "Customers", OnDelete = "ON_DELETE_SET_NULL" }).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                $"[{EntityScene.OwnerConstraint},{{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,ON_DELETE_SET_NULL\"}}]",
                (await scene.LoadEntityAsync("Orders")).EntConstraints);
        }

        // ---------- Rules ----------

        [Theory]
        [InlineData("{\"Type\":\"Unique\",\"IsSystem\":true,\"Properties\":[\"Paid\"]}", "IsSystem", "System constraints cannot be changed: leave them out")]
        [InlineData("{\"Type\":\"ForeignKey\",\"IsSystem\":true,\"Property\":\"Owner\",\"ForeignEntity\":\"Users\",\"OnDelete\":\"ON_DELETE_SET_NULL\"}", "IsSystem", "System constraints cannot be changed: leave them out")]
        [InlineData("{\"Properties\":[\"Paid\"]}", "Type", "Required")]
        [InlineData("{\"Type\":null,\"Properties\":[\"Paid\"]}", "Type", "Required")]
        [InlineData("{\"Type\":\"Index\",\"Properties\":[\"Paid\"]}", "Type", "Must be one of: Unique, ForeignKey")]
        [InlineData("{\"Type\":\"unique\",\"Properties\":[\"Paid\"]}", "Type", "Must be one of: Unique, ForeignKey")]
        [InlineData("{\"Type\":\"1\",\"Properties\":[\"Paid\"]}", "Type", "Must be one of: Unique, ForeignKey")]
        [InlineData("{\"Type\":\"Unique\"}", "Properties", "Select at least one property")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":null}", "Properties", "Select at least one property")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[]}", "Properties", "Select at least one property")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[\"Paid\",\"Nope\"]}", "Properties", "Property 'Nope' does not exist")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[\"paid\"]}", "Properties", "Property 'paid' does not exist")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[\"Paid,Code\"]}", "Properties", "Property 'Paid,Code' does not exist")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[\"\"]}", "Properties", "Property '' does not exist")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[\"Secret\"]}", "Properties", "Property 'Secret' is encrypted and cannot be unique")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[\"Paid\",\"Amount\",\"Paid\"]}", "Properties", "A property can be listed only once")]
        // A unique constraint does not become a foreign key because it carries the values of one.
        [InlineData("{\"Type\":\"Unique\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Properties", "Select at least one property")]
        [InlineData("{\"Type\":\"ForeignKey\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Required")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\" \",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Required")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Nope\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Property 'Nope' does not exist")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"customer_id\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Property 'customer_id' does not exist")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Amount\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Must be a custom Number property with 0 decimal places")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Owner\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Must be a custom Number property with 0 decimal places")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Must be a custom Number property with 0 decimal places")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Code\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Must be a custom Number property with 0 decimal places")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "ForeignEntity", "Required")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Files\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "ForeignEntity", "A foreign key cannot point to Files")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Nope\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "ForeignEntity", "Entity 'Nope' does not exist")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "ForeignEntity", "Entity 'customers' does not exist")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\"}", "OnDelete", "Required")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"CASCADE\"}", "OnDelete", "Must be one of: " + OnDeleteNames)]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"on_delete_cascade\"}", "OnDelete", "Must be one of: " + OnDeleteNames)]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"2\"}", "OnDelete", "Must be one of: " + OnDeleteNames)]
        // A foreign key does not become valid because it carries the properties of a unique constraint.
        [InlineData("{\"Type\":\"ForeignKey\",\"Properties\":[\"Customer_ID\",\"Customers\",\"ON_DELETE_CASCADE\"],\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "Property", "Required")]
        public async Task Put_Constraint_That_Breaks_A_Rule_Should_Return_400_And_Change_Nothing(string constraint, string property, string message)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), EntityScene.Json($"{{\"Constraints\":[{constraint}]}}"));

            await EntityScene.AssertValidationAsync(response, $"Constraints[0].{property}: {message}");
            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"Constraints\":null}")]
        public async Task Put_Without_The_List_Should_Return_400_And_Change_Nothing(string body)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), EntityScene.Json(body));

            await EntityScene.AssertValidationAsync(response, "Constraints: Required");
            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Put_List_With_A_Null_Should_Return_400_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), EntityScene.Json("{\"Constraints\":[null]}"));

            await EntityScene.AssertValidationAsync(response, "Constraints[0]: Required");
            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Theory]
        // The same properties, in the same or another order.
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[\"Paid\",\"Amount\"]}", "{\"Type\":\"Unique\",\"Properties\":[\"Paid\",\"Amount\"]}")]
        [InlineData("{\"Type\":\"Unique\",\"Properties\":[\"Paid\",\"Amount\"]}", "{\"Type\":\"Unique\",\"Properties\":[\"Amount\",\"Paid\"]}")]
        // The same property to the same entity, whatever happens on delete.
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}")]
        [InlineData("{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_CASCADE\"}", "{\"Type\":\"ForeignKey\",\"Property\":\"Customer_ID\",\"ForeignEntity\":\"Customers\",\"OnDelete\":\"ON_DELETE_SET_NULL\"}")]
        public async Task Put_The_Same_Constraint_Twice_Should_Return_400_And_Change_Nothing(string first, string second)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), EntityScene.Json($"{{\"Constraints\":[{first},{second}]}}"));

            await EntityScene.AssertValidationAsync(response, "Constraints[1]: The entity already has this constraint");
            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Put_Constraint_That_Repeats_A_System_Constraint_Should_Return_400()
        {
            var scene = await EntityScene.CreateAsync(_portal, ownerIsAdmin: true);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Users"), Unique("Email").ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "Constraints[0]: The entity already has this constraint");
            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Put_Should_Report_Every_Constraint_That_Breaks_A_Rule()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.ConstraintsUrl("Orders"),
                Body(
                    new { Type = "Unique", Properties = new[] { "Paid" } },
                    new { Type = "Unique", Properties = new[] { "Nope" } },
                    new { Type = "ForeignKey", Property = "Amount", ForeignEntity = "Files", OnDelete = "NEVER" }).ToJsonContent());

            await EntityScene.AssertValidationAsync(
                response,
                "Constraints[1].Properties: Property 'Nope' does not exist",
                "Constraints[2].Property: Must be a custom Number property with 0 decimal places",
                "Constraints[2].ForeignEntity: A foreign key cannot point to Files",
                "Constraints[2].OnDelete: Must be one of: " + OnDeleteNames);

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        // ---------- System entities ----------

        [Fact]
        public async Task Put_System_Entity_Should_Return_403_For_A_User_Who_Is_Not_An_Administrator()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            foreach (var client in new[] { scene.Owner, scene.Collaborator })
            {
                // Before the body is looked at: a valid and an invalid list get the same answer.
                foreach (var body in new[] { Unique("Nickname"), Unique("Nope") })
                {
                    var response = await client.PutAsync(scene.ConstraintsUrl("Users"), body.ToJsonContent());

                    await EntityScene.AssertErrorAsync(response, HttpStatusCode.Forbidden, PortalErrorCode.Forbidden);
                }
            }

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Put_System_Entity_Should_Work_For_An_Administrator_Who_Owns_The_Application()
        {
            var scene = await EntityScene.CreateAsync(_portal, ownerIsAdmin: true);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Users"), Unique("Nickname").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            const string expected = "[{\"IsSystem\":true,\"TypeID\":1,\"Properties\":\"Email\"},{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Nickname\"}]";
            Assert.Equal(expected, (await scene.LoadEntityAsync("Users")).EntConstraints);

            var request = Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath));
            Assert.Equal($"{scene.ServerUrl}/api/Application/GenerateConstraints?Entity=Users", request.Url);
            Assert.Equal(expected, request.Body);
        }

        // ---------- Access ----------

        [Theory]
        [InlineData("Nothing")]
        [InlineData("orders")]
        [InlineData("ORDERS")]
        public async Task Unknown_Or_Differently_Cased_Entity_Should_Return_404_And_Change_Nothing(string name)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(scene.ConstraintsUrl(name)), "Entity");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.PutAsync(scene.ConstraintsUrl(name), Unique("Paid").ToJsonContent()), "Entity");

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Stranger_Admin_And_Unknown_Token_Should_Return_404_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var admin = await _portal.CreateAdminClientAsync();
            ScriptApiServer();

            // The Admin role opens no application, not even for a system entity.
            foreach (var entity in new[] { "Orders", "Users" })
            {
                foreach (var send in AllRequests(entity))
                {
                    await EntityScene.AssertNotFoundAsync(await send(scene.Stranger, scene.AppUrl), "Application");
                    await EntityScene.AssertNotFoundAsync(await send(admin, scene.AppUrl), "Application");
                    await EntityScene.AssertNotFoundAsync(await send(scene.Owner, $"/api/v1/applications/{Guid.NewGuid()}"), "Application");
                }
            }

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Anonymous_Should_Return_401()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();
            var anonymous = _portal.CreateAnonymousClient();

            foreach (var send in AllRequests("Orders"))
            {
                await EntityScene.AssertErrorAsync(await send(anonymous, scene.AppUrl), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            }

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Put_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();
            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), Unique("Paid").ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Collaborator_Should_Be_Able_To_Read_And_Replace_The_Constraints()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            Assert.Equal(HttpStatusCode.OK, (await scene.Collaborator.GetAsync(scene.ConstraintsUrl("Orders"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.Collaborator.PutAsync(scene.ConstraintsUrl("Orders"), Unique("Paid").ToJsonContent())).StatusCode);

            // Called as the collaborator and audited under the collaborator's name.
            Assert.Equal(2, _portal.ApiServer.Requests.Count);

            foreach (var request in _portal.ApiServer.Requests)
            {
                await scene.AssertPortalHeadersAsync(request, scene.CollaboratorEmail);
            }

            Assert.Single(await scene.AuditRowsAsync(scene.CollaboratorEmail));
            Assert.Empty(await scene.AuditRowsAsync(scene.OwnerEmail));
        }

        // ---------- API server ----------

        [Fact]
        public async Task Put_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GenerateConstraintsPath, HttpStatusCode.BadRequest, EntityScene.ApiError("UNIQUE constraint failed: Orders.Paid"));

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), Unique("Paid").ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("UNIQUE constraint failed: Orders.Paid", error.Message);

            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            await scene.AssertNothingChangedAsync();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Put_When_The_Api_Server_Fails_Should_Return_502_And_Change_Nothing(bool unreachable)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            if (unreachable)
            {
                _portal.ApiServer.Unreachable(FakeApiServer.GenerateConstraintsPath);
            }
            else
            {
                _portal.ApiServer.Respond(FakeApiServer.GenerateConstraintsPath, HttpStatusCode.InternalServerError, EntityScene.ApiError("It broke."));
            }

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), Unique("Paid").ToJsonContent());

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.UpstreamError, error.Code);
            Assert.Equal(unreachable ? ApiServerClient.UnusableAnswerMessage : "It broke.", error.Message);

            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            await scene.AssertNothingChangedAsync();
        }

        // ---------- Cache reset ----------

        [Fact]
        public async Task Each_Successful_Put_Should_Reset_The_Cache_Once_As_Its_Last_Call()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            foreach (var body in new[] { Unique("Paid"), Body(), Unique("Code") })
            {
                Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), body.ToJsonContent())).StatusCode);

                var requests = _portal.ApiServer.Requests;
                Assert.Equal(new[] { FakeApiServer.GenerateConstraintsPath, FakeApiServer.ClearCachePath }, requests.Select(x => x.Path));

                _portal.ApiServer.Reset();
                ScriptApiServer();
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Put_When_The_Cache_Reset_Fails_Should_Still_Succeed_With_A_Warning_Header(bool unreachable)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            if (unreachable)
            {
                _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);
            }
            else
            {
                _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.BadRequest, EntityScene.ApiError("No."));
            }

            var response = await scene.Owner.PutAsync(scene.ConstraintsUrl("Orders"), Unique("Paid").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            EntityScene.AssertWarning(response);
            Assert.Equal(2, (await response.ReadJsonAsync<EntityConstraintsResponse>()).Constraints.Count);
            Assert.Contains("\"Paid\"", (await scene.LoadEntityAsync("Orders")).EntConstraints);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Warning_Header_Of_The_Put()
        {
            var client = await _portal.CreateUserClientAsync();
            var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")) ?? throw new InvalidOperationException("No document.");

            var put = document["paths"]?["/api/v1/applications/{appToken}/entities/{entity}/constraints"]?["put"];

            Assert.NotNull(put?["responses"]?["200"]?["headers"]?["Warning"]);
        }

        // ---------- Helpers ----------

        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.GenerateConstraintsPath, HttpStatusCode.OK, string.Empty);
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        private static Dictionary<string, object?> Body(params object[] constraints)
        {
            return new Dictionary<string, object?> { ["Constraints"] = constraints };
        }

        private static Dictionary<string, object?> Unique(params string[] properties)
        {
            return Body(new { Type = "Unique", Properties = properties });
        }

        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> AllRequests(string entity)
        {
            return new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, appUrl) => client.GetAsync($"{appUrl}/entities/{entity}/constraints"),
                (client, appUrl) => client.PutAsync($"{appUrl}/entities/{entity}/constraints", Unique("Nickname").ToJsonContent())
            };
        }
    }
}
