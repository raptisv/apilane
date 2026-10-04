using Apilane.Common.Enums;
using Apilane.Common.Models;
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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class PropertiesApiTests
    {
        private readonly PortalFactory _portal;

        public PropertiesApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- List and get ----------

        [Fact]
        public async Task List_Should_Return_The_Key_Then_The_Custom_Properties_Then_The_System_Ones()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.GetAsync(scene.PropertiesUrl("Orders"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var list = await response.ReadJsonAsync<ListResponse<PropertyResponse>>();

            // The primary key, then the custom properties by name, then the system ones by name.
            Assert.Equal(new[] { "ID", "Amount", "Customer_ID", "Paid", "Secret", "Created", "Owner" }, list.Data.Select(x => x.Name));
            Assert.Equal(7, list.Total);

            // Reading does not involve the API server.
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task List_Should_Be_The_Properties_Of_The_Entity_Endpoint()
        {
            var scene = await CreateSceneAsync();

            var list = await (await scene.Owner.GetAsync(scene.PropertiesUrl("Orders"))).ReadJsonAsync<ListResponse<PropertyResponse>>();
            var entity = await (await scene.Owner.GetAsync($"{scene.AppUrl}/entities/Orders")).ReadJsonAsync<EntityResponse>();

            Assert.Equal(JsonSerializer.Serialize(entity.Properties), JsonSerializer.Serialize(list.Data));
        }

        [Fact]
        public async Task Position_Should_Be_The_Creation_Order_In_Every_Response()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var list = await (await scene.Owner.GetAsync(scene.PropertiesUrl("Orders"))).ReadJsonAsync<ListResponse<PropertyResponse>>();
            var positions = list.Data.ToDictionary(x => x.Name, x => x.Position);

            // The order they were created in: the system properties first, then the custom ones
            // as they were added, whatever their names.
            Assert.Equal(0, positions["ID"]);
            Assert.Equal(1, positions["Owner"]);
            Assert.Equal(2, positions["Created"]);
            Assert.True(positions["Amount"] > positions["Paid"]);

            // A property added later comes last even though its name sorts first.
            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Orders"), CreateBody("Aardvark", "Boolean").ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<PropertyResponse>();
            Assert.Equal(7, created.Position);

            var got = await (await scene.Owner.GetAsync(scene.PropertyUrl("Orders", "Aardvark"))).ReadJsonAsync<PropertyResponse>();
            Assert.Equal(7, got.Position);
        }

        [Fact]
        public async Task Get_Should_Return_One_Property_With_The_Flags_Of_The_Model()
        {
            var scene = await CreateSceneAsync();

            var amount = await (await scene.Owner.GetAsync(scene.PropertyUrl("Orders", "Amount"))).ReadJsonAsync<PropertyResponse>();
            Assert.Equal("Amount", amount.Name);
            Assert.Equal("Number", amount.Type);
            Assert.Equal("The total", amount.Description);
            Assert.True(amount.Required);
            Assert.Equal(2, amount.DecimalPlaces);
            Assert.Equal(0, amount.Minimum);
            Assert.Equal(1000, amount.Maximum);
            Assert.False(amount.IsSystem);
            Assert.True(amount.AllowMin);
            Assert.True(amount.AllowMaxEdit);
            Assert.False(amount.AllowValidationRegex);

            var secret = await (await scene.Owner.GetAsync(scene.PropertyUrl("Orders", "Secret"))).ReadJsonAsync<PropertyResponse>();
            Assert.Equal("String", secret.Type);
            Assert.True(secret.Encrypted);
            Assert.Equal("^[A-Z]+$", secret.ValidationRegex);
            Assert.True(secret.AllowMin);
            Assert.False(secret.AllowMaxEdit);
            Assert.True(secret.AllowValidationRegex);

            var id = await (await scene.Owner.GetAsync(scene.PropertyUrl("Orders", "ID"))).ReadJsonAsync<PropertyResponse>();
            Assert.True(id.IsPrimaryKey);
            Assert.True(id.IsSystem);

            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Theory]
        [InlineData("orders", "Amount", "Entity")]
        [InlineData("ORDERS", "Amount", "Entity")]
        [InlineData("Nothing", "Amount", "Entity")]
        [InlineData("Orders", "amount", "Property")]
        [InlineData("Orders", "AMOUNT", "Property")]
        [InlineData("Orders", "Nothing", "Property")]
        // A property of another entity of the same application.
        [InlineData("Invoices", "Amount", "Property")]
        public async Task Unknown_Or_Differently_Cased_Names_Should_Return_404_And_Change_Nothing(string entity, string property, string notFound)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var url = scene.PropertyUrl(entity, property);

            await AssertNotFoundAsync(await scene.Owner.GetAsync(url), notFound);
            await AssertNotFoundAsync(await scene.Owner.PutAsync(url, new { Description = "x" }.ToJsonContent()), notFound);
            await AssertNotFoundAsync(await scene.Owner.PostAsync($"{url}/rename", new { NewName = "Renamed" }.ToJsonContent()), notFound);
            await AssertNotFoundAsync(await scene.Owner.DeleteAsync(url), notFound);

            if (notFound == "Entity")
            {
                await AssertNotFoundAsync(await scene.Owner.GetAsync(scene.PropertiesUrl(entity)), notFound);
                await AssertNotFoundAsync(await scene.Owner.PostAsync(scene.PropertiesUrl(entity), CreateBody("Total", "Boolean").ToJsonContent()), notFound);
            }

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
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

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Anonymous_Should_Return_401_For_Every_Endpoint()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var anonymous = _portal.CreateAnonymousClient();

            foreach (var send in AllRequests())
            {
                var response = await send(anonymous, scene.AppUrl);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            }

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
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

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Collaborator_Should_Be_Able_To_Do_Everything_The_Owner_Can()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var client = scene.Collaborator;

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(scene.PropertiesUrl("Invoices"))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", "Number", decimalPlaces: 2).ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(scene.PropertyUrl("Invoices", "Total"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(scene.PropertyUrl("Invoices", "Total"), new { Description = "By a collaborator" }.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{scene.PropertyUrl("Invoices", "Total")}/rename", new { NewName = "Gross" }.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(scene.PropertyUrl("Invoices", "Gross"))).StatusCode);

            // Every call to the API server was made as the collaborator.
            var bearer = $"Bearer {await StoredTokenAsync(scene.CollaboratorEmail)}";
            Assert.Equal(7, _portal.ApiServer.Requests.Count);
            Assert.All(_portal.ApiServer.Requests, x => Assert.Equal(bearer, x.Headers["Authorization"]));

            await AssertSeedUnchangedAsync(scene);

            // And audited under the collaborator's name.
            var audit = await AuditRowsAsync(scene.CollaboratorEmail);
            Assert.All(audit, x => Assert.Equal("Property", x.EntityType));
            Assert.Equal(new[] { "Created", "Modified", "Modified", "Deleted" }, audit.Select(x => x.Action));
        }

        // ---------- Create ----------

        // Every request carries every type-dependent value; the type decides which ones are kept.
        [Theory]
        [InlineData("String", 1, 2L, 50L, "^[a-z]+$", null, true)]
        [InlineData("Number", 2, 2L, 50L, null, 3, false)]
        [InlineData("Boolean", 3, null, null, null, null, false)]
        [InlineData("Date", 4, null, null, null, null, false)]
        public async Task Create_Should_Send_The_Property_To_The_Api_Server_Then_Store_It_Then_Reset_The_Cache(
            string type, int typeId, long? minimum, long? maximum, string? regex, int? decimalPlaces, bool encrypted)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var invoices = await LoadEntityAsync(scene, "Invoices");

            var body = CreateBody("Total", type, decimalPlaces: 3);
            body["Description"] = "What it adds up to";
            body["Required"] = true;
            body["Encrypted"] = true;
            body["ValidationRegex"] = "^[a-z]+$";
            body["Minimum"] = 2;
            body["Maximum"] = 50;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal($"{scene.PropertyUrl("Invoices", "Total")}", response.Headers.Location?.OriginalString);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var created = await response.ReadJsonAsync<PropertyResponse>();
            Assert.Equal("Total", created.Name);
            Assert.Equal(type, created.Type);
            Assert.Equal("What it adds up to", created.Description);
            Assert.True(created.Required);
            Assert.False(created.IsSystem);
            Assert.False(created.IsPrimaryKey);
            Assert.Equal(minimum, created.Minimum);
            Assert.Equal(maximum, created.Maximum);
            Assert.Equal(regex, created.ValidationRegex);
            Assert.Equal(decimalPlaces, created.DecimalPlaces);
            Assert.Equal(encrypted, created.Encrypted);

            // One request with the property as its body, then the cache reset.
            var requests = _portal.ApiServer.Requests;
            Assert.Equal(2, requests.Count);

            Assert.Equal(HttpMethod.Post, requests[0].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/GenerateProperty?Entity=Invoices", requests[0].Url);
            await AssertPortalHeadersAsync(requests[0], scene, scene.OwnerEmail);

            var sentJson = JsonNode.Parse(requests[0].Body)?.AsObject() ?? throw new InvalidOperationException("No body.");
            Assert.Equal(
                new[] { "DateModified", "DecimalPlaces", "Description", "Encrypted", "EntityID", "ID", "IsPrimaryKey", "IsSystem", "Maximum", "Minimum", "Name", "Required", "TypeID", "ValidationRegex" },
                sentJson.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));

            var sent = JsonSerializer.Deserialize<DBWS_EntityProperty>(requests[0].Body) ?? throw new InvalidOperationException("No body.");
            Assert.Equal(0, sent.ID);
            Assert.Equal(invoices.ID, sent.EntityID);
            Assert.Equal("Total", sent.Name);
            Assert.Equal("What it adds up to", sent.Description);
            Assert.Equal(typeId, sent.TypeID);
            Assert.True(sent.Required);
            Assert.False(sent.IsSystem);
            Assert.False(sent.IsPrimaryKey);
            Assert.Equal(minimum, sent.Minimum);
            Assert.Equal(maximum, sent.Maximum);
            Assert.Equal(regex, sent.ValidationRegex);
            Assert.Equal(decimalPlaces, sent.DecimalPlaces);
            Assert.Equal(encrypted, sent.Encrypted);

            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", requests[1].Url);

            // Stored as sent.
            var stored = Assert.Single((await LoadEntityAsync(scene, "Invoices")).Properties, x => x.Name == "Total");
            Assert.NotEqual(0, stored.ID);
            Assert.Equal(Comparable(sent), Comparable(stored));

            var audit = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Property", audit.EntityType);
            Assert.Equal("Created", audit.Action);
            Assert.Equal("Total", audit.EntityIdentifier);
            Assert.Equal(scene.AppId, audit.AppID);
        }

        [Fact]
        public async Task Create_Should_Ignore_Values_A_Client_May_Not_Set()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Total", "Boolean");
            body["ID"] = 987654;
            body["EntityID"] = 987654;
            body["IsPrimaryKey"] = true;
            body["IsSystem"] = true;
            body["TypeID"] = 1;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<PropertyResponse>();
            Assert.False(created.IsPrimaryKey);
            Assert.False(created.IsSystem);
            Assert.Equal("Boolean", created.Type);

            var invoices = await LoadEntityAsync(scene, "Invoices");
            var stored = Assert.Single(invoices.Properties, x => x.Name == "Total");
            Assert.NotEqual(987654, stored.ID);
            Assert.Equal(invoices.ID, stored.EntityID);
            Assert.False(stored.IsPrimaryKey);
            Assert.False(stored.IsSystem);

            var sent = JsonSerializer.Deserialize<DBWS_EntityProperty>(_portal.ApiServer.Requests[0].Body) ?? throw new InvalidOperationException("No body.");
            Assert.Equal(0, sent.ID);
            Assert.False(sent.IsPrimaryKey);
            Assert.False(sent.IsSystem);
        }

        [Theory]
        [InlineData("abc")]
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

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody(name, "Boolean").ToJsonContent());

            await AssertValidationAsync(response, "Name", null);
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_Name_Longer_Than_120_Characters_Should_Return_400_On_Name()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody(new string('a', 121), "Boolean").ToJsonContent());

            await AssertValidationAsync(response, "Name", null);
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData("Total2")]
        // The characters are checked before the suffix.
        [InlineData("Tot1_Data")]
        public async Task Create_Name_With_Other_Characters_Should_Say_Which_Are_Allowed(string name)
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody(name, "Boolean").ToJsonContent());

            await AssertValidationAsync(response, "Name", "Allowed characters are a-z, A-Z and _");
        }

        [Theory]
        [InlineData("Total_Data")]
        [InlineData("_Data")]
        public async Task Create_Name_Ending_In_Data_Should_Return_400_On_Name_And_Create_Nothing(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody(name, "Boolean").ToJsonContent());

            await AssertValidationAsync(response, "Name", "The name cannot end with '_Data'");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData("Abcd")]
        [InlineData("With_Underscore_")]
        [InlineData("____")]
        // Only the exact suffix is refused.
        [InlineData("Total_data")]
        [InlineData("Total_Data_")]
        public async Task Create_Should_Accept_Names_With_Underscores_And_Near_Misses_Of_The_Reserved_Suffix(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody(name, "Boolean").ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(name, (await response.ReadJsonAsync<PropertyResponse>()).Name);
        }

        [Fact]
        public async Task Create_Should_Accept_The_Longest_Name()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var name = new string('a', 120);

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody(name, "Boolean").ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Contains((await LoadEntityAsync(scene, "Invoices")).Properties, x => x.Name == name);
        }

        [Theory]
        [InlineData("Amount")]
        [InlineData("amount")]
        [InlineData("AMOUNT")]
        // System properties count too.
        [InlineData("owner")]
        public async Task Create_Existing_Name_In_Any_Case_Should_Return_409_And_Create_Nothing(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Orders"), CreateBody(name, "Boolean").ToJsonContent());

            await AssertConflictAsync(response, $"Property name '{name}' already exists", "Property");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_Name_Of_A_Property_Of_Another_Entity_Should_Work()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Amount", "Boolean").ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Theory]
        [InlineData("Text")]
        [InlineData("string")]
        [InlineData("1")]
        [InlineData("100")]
        [InlineData("")]
        [InlineData(null)]
        public async Task Create_Unknown_Type_Should_Return_400_On_Type_And_Create_Nothing(string? type)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", type).ToJsonContent());

            await AssertValidationAsync(response, "Type", string.IsNullOrEmpty(type) ? "Required" : "Must be one of: String, Number, Boolean, Date");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_Number_Without_Decimal_Places_Should_Return_400_On_DecimalPlaces()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", "Number").ToJsonContent());

            await AssertValidationAsync(response, "DecimalPlaces", "Required");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        public async Task Create_Number_With_Decimal_Places_Outside_0_To_8_Should_Return_400_On_DecimalPlaces(int decimalPlaces)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", "Number", decimalPlaces).ToJsonContent());

            await AssertValidationAsync(response, "DecimalPlaces", "Must be between 0 and 8");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(8)]
        public async Task Create_Number_Should_Accept_0_And_8_Decimal_Places(int decimalPlaces)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", "Number", decimalPlaces).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(decimalPlaces, (await response.ReadJsonAsync<PropertyResponse>()).DecimalPlaces);
        }

        [Theory]
        [InlineData("[a-")]
        [InlineData("(")]
        [InlineData("*")]
        public async Task Create_String_With_A_Regex_That_Does_Not_Compile_Should_Return_400_On_ValidationRegex(string regex)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Code", "String");
            body["ValidationRegex"] = regex;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            await AssertValidationAsync(response, "ValidationRegex", "Invalid regex");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_Number_With_A_Regex_That_Does_Not_Compile_Should_Work_Because_It_Is_Dropped()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Total", "Number", decimalPlaces: 0);
            body["ValidationRegex"] = "[a-";

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null((await response.ReadJsonAsync<PropertyResponse>()).ValidationRegex);
        }

        [Theory]
        [InlineData("String", null)]
        [InlineData("Number", 2)]
        public async Task Create_Minimum_Greater_Than_Maximum_Should_Return_400_On_Minimum(string type, int? decimalPlaces)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Total", type, decimalPlaces);
            body["Minimum"] = 11;
            body["Maximum"] = 10;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            await AssertValidationAsync(response, "Minimum", "Min (11) cannot be greater than Max (10)");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData("String", null, "Minimum", 9007199254740992)]
        [InlineData("String", null, "Maximum", 9007199254740992)]
        [InlineData("Number", 0, "Minimum", -9007199254740992)]
        [InlineData("Number", 0, "Maximum", 9007199254740992)]
        [InlineData("Number", 0, "Maximum", long.MaxValue)]
        public async Task Create_Limit_A_Browser_Cannot_Hold_Exactly_Should_Return_400_On_That_Limit(string type, int? decimalPlaces, string limit, long value)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Total", type, decimalPlaces);
            body[limit] = value;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            await AssertValidationAsync(response, limit, "Must be between -9007199254740991 and 9007199254740991");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_Number_With_The_Largest_Limits_A_Browser_Holds_Exactly_Should_Work()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Total", "Number", 0);
            body["Minimum"] = -9007199254740991;
            body["Maximum"] = 9007199254740991;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var stored = await LoadPropertyAsync(scene, "Invoices", "Total");
            Assert.Equal(-9007199254740991, stored.Minimum);
            Assert.Equal(9007199254740991, stored.Maximum);
        }

        [Fact]
        public async Task Create_Boolean_Should_Ignore_A_Limit_Beyond_The_Range()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // Values that do not apply to the type are ignored, not refused.
            var body = CreateBody("Flagged", "Boolean");
            body["Maximum"] = long.MaxValue;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null((await LoadPropertyAsync(scene, "Invoices", "Flagged")).Maximum);
        }

        [Fact]
        public async Task Create_Minimum_Equal_To_Maximum_And_One_Of_The_Two_Alone_Should_Work()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var equal = CreateBody("Equal", "String");
            equal["Minimum"] = 5;
            equal["Maximum"] = 5;
            Assert.Equal(HttpStatusCode.Created, (await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), equal.ToJsonContent())).StatusCode);

            var onlyMinimum = CreateBody("OnlyMin", "String");
            onlyMinimum["Minimum"] = 500;
            Assert.Equal(HttpStatusCode.Created, (await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), onlyMinimum.ToJsonContent())).StatusCode);

            var onlyMaximum = CreateBody("OnlyMax", "String");
            onlyMaximum["Maximum"] = 5;
            Assert.Equal(HttpStatusCode.Created, (await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), onlyMaximum.ToJsonContent())).StatusCode);
        }

        [Fact]
        public async Task Create_With_Several_Problems_Should_List_Each_In_Errors()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Total_Data", "Number");
            body["Minimum"] = 11;
            body["Maximum"] = 10;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(new[] { "Name", "DecimalPlaces", "Minimum" }, (error.Errors ?? new List<ErrorDetail>()).Select(x => x.Property));

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_Unknown_Type_And_Forbidden_Name_Should_List_Both_In_Errors()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // Minimum and Maximum are not judged: whether they apply depends on the type.
            var body = CreateBody("Total_Data", "Text");
            body["Minimum"] = 11;
            body["Maximum"] = 10;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(new[] { "Name", "Type" }, (error.Errors ?? new List<ErrorDetail>()).Select(x => x.Property));

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_Name_Of_The_Wrong_Shape_Should_Answer_Before_The_Rules_Of_The_Type()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("ab1", "String");
            body["Minimum"] = 11;
            body["Maximum"] = 10;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            await AssertValidationAsync(response, "Name", null);
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task Create_String_With_Maximum_Below_1_Should_Return_400_On_Maximum(int maximum)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Code", "String");
            body["Maximum"] = maximum;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            await AssertValidationAsync(response, "Maximum", "Must be at least 1");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_String_With_Negative_Minimum_Should_Return_400_On_Minimum()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Code", "String");
            body["Minimum"] = -1;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            await AssertValidationAsync(response, "Minimum", "Cannot be negative");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_Number_With_Negative_Limits_Should_Work()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Balance", "Number", decimalPlaces: 0);
            body["Minimum"] = -10;
            body["Maximum"] = -1;

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task Create_Blank_Description_And_Regex_Should_Be_Stored_As_None(string? value)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = CreateBody("Code", "String");
            body["Description"] = value;
            body["ValidationRegex"] = value;

            Assert.Equal(HttpStatusCode.Created, (await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), body.ToJsonContent())).StatusCode);

            var stored = Assert.Single((await LoadEntityAsync(scene, "Invoices")).Properties, x => x.Name == "Code");
            Assert.Null(stored.Description);
            Assert.Null(stored.ValidationRegex);
        }

        [Theory]
        [InlineData("Files")]
        [InlineData("archive")]
        public async Task Create_On_An_Entity_That_Takes_No_Properties_Should_Return_409(string entity)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl(entity), CreateBody("Total", "Boolean").ToJsonContent());

            await AssertConflictAsync(response, "Properties cannot be added to this entity", "Entity");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Create_On_A_System_Entity_That_Takes_Properties_Should_Work()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Users"), CreateBody("Nickname", "String").ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.False((await response.ReadJsonAsync<PropertyResponse>()).IsSystem);

            // And it can be changed, renamed and deleted like any other custom property.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PutAsync(scene.PropertyUrl("Users", "Nickname"), new { Description = "Changed" }.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PostAsync($"{scene.PropertyUrl("Users", "Nickname")}/rename", new { NewName = "Alias" }.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.PropertyUrl("Users", "Alias"))).StatusCode);
        }

        [Fact]
        public async Task Create_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message_And_Leave_The_Portal_Unchanged()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePropertyPath, HttpStatusCode.BadRequest, ApiError("Property Total already exists", "Total"));

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", "Boolean").ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("Property Total already exists", error.Message);
            Assert.Equal("Total", Assert.Single(error.Errors ?? new List<ErrorDetail>()).Property);

            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Create_When_The_Api_Server_Fails_Should_Return_502_And_Leave_The_Portal_Unchanged(bool unreachable)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            Fail(FakeApiServer.GeneratePropertyPath, unreachable);

            var response = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", "Boolean").ToJsonContent());

            await AssertUpstreamAsync(response, unreachable ? ApiServerClient.UnusableAnswerMessage : "It broke.");
            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        // ---------- Update ----------

        [Fact]
        public async Task Update_Number_Should_Save_Description_Minimum_And_Maximum_And_Only_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var before = await LoadPropertyAsync(scene, "Orders", "Amount");

            var response = await scene.Owner.PutAsync(
                scene.PropertyUrl("Orders", "Amount"),
                new { Description = "Net amount", Minimum = -5, Maximum = 5000 }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var updated = await response.ReadJsonAsync<PropertyResponse>();
            Assert.Equal("Net amount", updated.Description);
            Assert.Equal(-5, updated.Minimum);
            Assert.Equal(5000, updated.Maximum);

            // The API server is not asked to change anything: only the cache reset.
            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", request.Url);
            await AssertPortalHeadersAsync(request, scene, scene.OwnerEmail);

            var stored = await LoadPropertyAsync(scene, "Orders", "Amount");
            Assert.Equal("Net amount", stored.Description);
            Assert.Equal(-5, stored.Minimum);
            Assert.Equal(5000, stored.Maximum);

            // Nothing else of it changed.
            Assert.Equal(before.ID, stored.ID);
            Assert.Equal(before.TypeID, stored.TypeID);
            Assert.Equal(before.Required, stored.Required);
            Assert.Equal(before.DecimalPlaces, stored.DecimalPlaces);
            Assert.Null(stored.ValidationRegex);

            var audit = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Property", audit.EntityType);
            Assert.Equal("Modified", audit.Action);
            Assert.Equal("Amount", audit.EntityIdentifier);
            Assert.Equal(scene.AppId, audit.AppID);
            Assert.Contains("Net amount", audit.Changes);
        }

        [Fact]
        public async Task Update_Should_Use_The_Stored_Type_And_Ignore_A_Type_And_Other_Fixed_Values_In_The_Body()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // The body claims the Number is a String, and sends what only a String has.
            var response = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Amount"), new Dictionary<string, object?>
            {
                ["Description"] = "Still a number",
                ["Type"] = "String",
                ["TypeID"] = 1,
                ["ValidationRegex"] = "^[a-z]+$",
                ["Minimum"] = 1,
                ["Maximum"] = 2,
                ["Name"] = "Hijacked",
                ["Required"] = false,
                ["Encrypted"] = true,
                ["DecimalPlaces"] = 7,
                ["IsSystem"] = true,
                ["IsPrimaryKey"] = true
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var updated = await response.ReadJsonAsync<PropertyResponse>();
            Assert.Equal("Number", updated.Type);
            Assert.Null(updated.ValidationRegex);

            var stored = await LoadPropertyAsync(scene, "Orders", "Amount");
            Assert.Equal((int)PropertyType.Number, stored.TypeID);
            Assert.Equal("Still a number", stored.Description);
            Assert.Null(stored.ValidationRegex);
            Assert.Equal(1, stored.Minimum);
            Assert.Equal(2, stored.Maximum);
            Assert.Equal("Amount", stored.Name);
            Assert.True(stored.Required);
            Assert.False(stored.Encrypted);
            Assert.Equal(2, stored.DecimalPlaces);
            Assert.False(stored.IsSystem);
            Assert.False(stored.IsPrimaryKey);
        }

        [Fact]
        public async Task Update_String_Should_Save_Regex_And_Minimum_And_Leave_Its_Maximum_As_It_Is()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.PropertyUrl("Orders", "Secret"),
                new { Description = "A code", ValidationRegex = "^[0-9]+$", Minimum = 3, Maximum = 999 }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // AllowMaxEdit is false for a String: its maximum is the size of the column.
            var updated = await response.ReadJsonAsync<PropertyResponse>();
            Assert.False(updated.AllowMaxEdit);
            Assert.Equal(50, updated.Maximum);

            var stored = await LoadPropertyAsync(scene, "Orders", "Secret");
            Assert.Equal("A code", stored.Description);
            Assert.Equal("^[0-9]+$", stored.ValidationRegex);
            Assert.Equal(3, stored.Minimum);
            Assert.Equal(50, stored.Maximum);
            Assert.True(stored.Encrypted);
        }

        [Fact]
        public async Task Update_String_Without_A_Maximum_In_The_Body_Should_Keep_Its_Maximum()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Secret"), new { Description = "A code" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(50, (await LoadPropertyAsync(scene, "Orders", "Secret")).Maximum);
        }

        [Fact]
        public async Task Update_String_Minimum_Greater_Than_Its_Stored_Maximum_Should_Return_400_On_Minimum()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // The maximum in the body does not count: the stored 50 does.
            var response = await scene.Owner.PutAsync(
                scene.PropertyUrl("Orders", "Secret"),
                new { ValidationRegex = "^[A-Z]+$", Minimum = 51, Maximum = 999 }.ToJsonContent());

            await AssertValidationAsync(response, "Minimum", "Min (51) cannot be greater than Max (50)");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Update_Number_Minimum_Greater_Than_Maximum_Should_Return_400_On_Minimum()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Amount"), new { Minimum = 11, Maximum = 10 }.ToJsonContent());

            await AssertValidationAsync(response, "Minimum", "Min (11) cannot be greater than Max (10)");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Update_String_With_A_Regex_That_Does_Not_Compile_Should_Return_400_On_ValidationRegex()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Secret"), new { ValidationRegex = "[a-" }.ToJsonContent());

            await AssertValidationAsync(response, "ValidationRegex", "Invalid regex");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Update_String_With_Negative_Minimum_Should_Return_400_On_Minimum()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Secret"), new { ValidationRegex = "^[A-Z]+$", Minimum = -1 }.ToJsonContent());

            await AssertValidationAsync(response, "Minimum", "Cannot be negative");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData("Minimum", -9007199254740992)]
        [InlineData("Maximum", 9007199254740992)]
        public async Task Update_Number_Limit_A_Browser_Cannot_Hold_Exactly_Should_Return_400_On_That_Limit(string limit, long value)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = new JsonObject { ["Description"] = "Changed", [limit] = value };

            var response = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Amount"), body.ToJsonContent());

            await AssertValidationAsync(response, limit, "Must be between -9007199254740991 and 9007199254740991");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Update_String_With_A_Stored_Maximum_Beyond_The_Range_Should_Still_Save_Its_Other_Values()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // A schema import, a clone or an older version can store any long. The maximum of a String
            // cannot be edited, so it must not stop the description from being saved.
            await _portal.WithDbContextAsync(async db =>
            {
                var invoices = await db.Entities.SingleAsync(x => x.AppID == scene.AppId && x.Name == "Invoices");
                var imported = Property("Imported", PropertyType.String, entityId: invoices.ID);
                imported.Maximum = long.MaxValue;
                db.EntityProperties.Add(imported);

                return await db.SaveChangesAsync();
            });

            var response = await scene.Owner.PutAsync(
                scene.PropertyUrl("Invoices", "Imported"),
                new { Description = "Changed", Minimum = 2, Maximum = 5 }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var stored = await LoadPropertyAsync(scene, "Invoices", "Imported");
            Assert.Equal("Changed", stored.Description);
            Assert.Equal(2, stored.Minimum);
            Assert.Equal(long.MaxValue, stored.Maximum);
        }

        [Fact]
        public async Task Update_With_Several_Problems_Should_List_Each_In_Errors()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Secret"), new { ValidationRegex = "[a-", Minimum = 51 }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(new[] { "ValidationRegex", "Minimum" }, (error.Errors ?? new List<ErrorDetail>()).Select(x => x.Property));

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Update_Number_Stored_Without_Decimal_Places_Should_Work()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // An imported property can have none: decimal places are asked for only when a property is created.
            await _portal.WithDbContextAsync(async db =>
            {
                var invoices = await db.Entities.SingleAsync(x => x.AppID == scene.AppId && x.Name == "Invoices");
                db.EntityProperties.Add(Property("Legacy", PropertyType.Number, entityId: invoices.ID));

                return await db.SaveChangesAsync();
            });

            var response = await scene.Owner.PutAsync(scene.PropertyUrl("Invoices", "Legacy"), new { Description = "Old", Minimum = 5, Maximum = 5 }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var stored = await LoadPropertyAsync(scene, "Invoices", "Legacy");
            Assert.Equal(5, stored.Minimum);
            Assert.Equal(5, stored.Maximum);
            Assert.Null(stored.DecimalPlaces);
        }

        [Theory]
        // The constraints stop a rename and a delete, not a change of the rules.
        [InlineData("Orders", "Customer_ID")]
        [InlineData("Customers", "Name")]
        public async Task Update_Property_That_Is_Part_Of_A_Constraint_Should_Work(string entity, string property)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.PropertyUrl(entity, property), new { Description = "Changed" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Changed", (await LoadPropertyAsync(scene, entity, property)).Description);
        }

        [Fact]
        public async Task Update_Boolean_Should_Save_The_Description_Only()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // Not even a regex that does not compile matters: a Boolean has none.
            var response = await scene.Owner.PutAsync(
                scene.PropertyUrl("Orders", "Paid"),
                new { Description = "Whether it was paid", ValidationRegex = "[a-", Minimum = 9, Maximum = 1 }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var stored = await LoadPropertyAsync(scene, "Orders", "Paid");
            Assert.Equal("Whether it was paid", stored.Description);
            Assert.Null(stored.ValidationRegex);
            Assert.Null(stored.Minimum);
            Assert.Null(stored.Maximum);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"Description\":null,\"ValidationRegex\":null,\"Minimum\":null,\"Maximum\":null}")]
        [InlineData("{\"Description\":\"\",\"ValidationRegex\":\"  \"}")]
        public async Task Update_Values_Left_Out_Or_Empty_Should_Be_Removed(string body)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var amount = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Amount"), new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, amount.StatusCode);

            var storedAmount = await LoadPropertyAsync(scene, "Orders", "Amount");
            Assert.Null(storedAmount.Description);
            Assert.Null(storedAmount.Minimum);
            Assert.Null(storedAmount.Maximum);

            var secret = await scene.Owner.PutAsync(scene.PropertyUrl("Orders", "Secret"), new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, secret.StatusCode);

            var storedSecret = await LoadPropertyAsync(scene, "Orders", "Secret");
            Assert.Null(storedSecret.ValidationRegex);
            Assert.Equal(50, storedSecret.Maximum);
        }

        // ---------- Rename ----------

        [Fact]
        public async Task Rename_Should_Call_The_Api_Server_With_The_Property_ID_Then_Save_Then_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var before = await LoadPropertyAsync(scene, "Orders", "Amount");

            var response = await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", "Amount")}/rename", new { NewName = "Total" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var renamed = await response.ReadJsonAsync<PropertyResponse>();
            Assert.Equal("Total", renamed.Name);
            Assert.Equal("The total", renamed.Description);

            var requests = _portal.ApiServer.Requests;
            Assert.Equal(2, requests.Count);

            Assert.Equal(HttpMethod.Get, requests[0].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/RenameEntityProperty?ID={before.ID}&NewName=Total", requests[0].Url);
            await AssertPortalHeadersAsync(requests[0], scene, scene.OwnerEmail);

            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", requests[1].Url);

            // The same row under its new name; the old URL is gone.
            Assert.Equal(before.ID, (await LoadPropertyAsync(scene, "Orders", "Total")).ID);
            await AssertNotFoundAsync(await scene.Owner.GetAsync(scene.PropertyUrl("Orders", "Amount")), "Property");
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync(scene.PropertyUrl("Orders", "Total"))).StatusCode);

            var audit = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Property", audit.EntityType);
            Assert.Equal("Modified", audit.Action);
            Assert.Equal("Total", audit.EntityIdentifier);
            Assert.Equal(scene.AppId, audit.AppID);
            Assert.Contains("Amount", audit.Changes);
            Assert.Contains("Total", audit.Changes);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("with space")]
        [InlineData("Digits1")]
        [InlineData("Total_Data")]
        [InlineData("")]
        [InlineData(null)]
        public async Task Rename_Invalid_Name_Should_Return_400_On_NewName_And_Change_Nothing(string? newName)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", "Amount")}/rename", new { NewName = newName }.ToJsonContent());

            await AssertValidationAsync(response, "NewName", newName == "Total_Data" ? "The name cannot end with '_Data'" : null);
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Fact]
        public async Task Rename_System_Property_Should_Return_409()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            foreach (var name in new[] { "ID", "Owner", "Created" })
            {
                var response = await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", name)}/rename", new { NewName = "Renamed" }.ToJsonContent());

                await AssertConflictAsync(response, "Cannot rename system Properties", "Property");
            }

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        // The property of a foreign key, and one of the properties of a unique constraint.
        [InlineData("Orders", "Customer_ID")]
        [InlineData("Customers", "Name")]
        public async Task Rename_Property_That_Is_Part_Of_A_Constraint_Should_Return_409(string entity, string property)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.PropertyUrl(entity, property)}/rename", new { NewName = "Renamed" }.ToJsonContent());

            await AssertConflictAsync(response, "Cannot rename property as it is part of a constraint of the entity", "Property");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData("Paid")]
        [InlineData("paid")]
        [InlineData("OWNER")]
        public async Task Rename_To_The_Name_Of_Another_Property_In_Any_Case_Should_Return_409(string newName)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", "Amount")}/rename", new { NewName = newName }.ToJsonContent());

            await AssertConflictAsync(response, $"Property name '{newName}' already exists", "Property");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData("Amount")]
        [InlineData("amount")]
        public async Task Rename_To_Its_Own_Name_Should_Be_Left_To_The_Api_Server(string newName)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", "Amount")}/rename", new { NewName = newName }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.RenameEntityPropertyPath));
            Assert.Equal(newName, (await response.ReadJsonAsync<PropertyResponse>()).Name);
        }

        [Fact]
        public async Task Rename_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message_And_Keep_The_Name()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.RenameEntityPropertyPath, HttpStatusCode.BadRequest, ApiError("Property named 'Total' already exists"));

            var response = await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", "Amount")}/rename", new { NewName = "Total" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("Property named 'Total' already exists", error.Message);

            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Rename_When_The_Api_Server_Fails_Should_Return_502_And_Keep_The_Name(bool unreachable)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            Fail(FakeApiServer.RenameEntityPropertyPath, unreachable);

            var response = await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", "Amount")}/rename", new { NewName = "Total" }.ToJsonContent());

            await AssertUpstreamAsync(response, unreachable ? ApiServerClient.UnusableAnswerMessage : "It broke.");
            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_Should_Call_The_Api_Server_With_The_Property_ID_Then_Remove_It_Then_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var before = await LoadPropertyAsync(scene, "Orders", "Amount");

            var response = await scene.Owner.DeleteAsync(scene.PropertyUrl("Orders", "Amount"));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var requests = _portal.ApiServer.Requests;
            Assert.Equal(2, requests.Count);

            Assert.Equal(HttpMethod.Get, requests[0].Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/DegenerateProperty?ID={before.ID}", requests[0].Url);
            await AssertPortalHeadersAsync(requests[0], scene, scene.OwnerEmail);

            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", requests[1].Url);

            // Gone, and the other properties of the entity are still there.
            Assert.Equal(
                new[] { "ID", "Owner", "Created", "Customer_ID", "Paid", "Secret" },
                (await LoadEntityAsync(scene, "Orders")).Properties.OrderBy(x => x.ID).Select(x => x.Name));
            await AssertNotFoundAsync(await scene.Owner.GetAsync(scene.PropertyUrl("Orders", "Amount")), "Property");

            var audit = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Property", audit.EntityType);
            Assert.Equal("Deleted", audit.Action);
            Assert.Equal("Amount", audit.EntityIdentifier);
            Assert.Equal(scene.AppId, audit.AppID);
        }

        [Fact]
        public async Task Delete_System_Property_Should_Return_409_Saying_It_Is_Part_Of_A_Module()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            foreach (var name in new[] { "ID", "Owner", "Created" })
            {
                await AssertConflictAsync(
                    await scene.Owner.DeleteAsync(scene.PropertyUrl("Orders", name)),
                    "This property is part of a module and cannot be deleted.",
                    "Property");
            }

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        [InlineData("Orders", "Customer_ID")]
        [InlineData("Customers", "Name")]
        public async Task Delete_Property_That_Is_Part_Of_A_Constraint_Should_Return_409(string entity, string property)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.DeleteAsync(scene.PropertyUrl(entity, property));

            await AssertConflictAsync(response, "Cannot delete property as it is part of a constraint of the entity", "Property");
            await AssertNothingSavedAndNoApiServerCallAsync(scene);
        }

        [Theory]
        // Spaces after the commas, another letter case, and a foreign key the entity mapper cannot show.
        [InlineData("[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Owner, Paid\"}]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"paid\"}]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Paid,Customers,ON_DELETE_SOMETHING\"}]")]
        public async Task Rename_And_Delete_Should_Read_The_Stored_Constraints_Tolerantly(string stored)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetConstraintsAsync(scene, "Orders", stored);

            await AssertConflictAsync(
                await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", "Paid")}/rename", new { NewName = "Settled" }.ToJsonContent()),
                "Cannot rename property as it is part of a constraint of the entity",
                "Property");

            await AssertConflictAsync(
                await scene.Owner.DeleteAsync(scene.PropertyUrl("Orders", "Paid")),
                "Cannot delete property as it is part of a constraint of the entity",
                "Property");

            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Contains((await LoadEntityAsync(scene, "Orders")).Properties, x => x.Name == "Paid");
        }

        [Theory]
        // The entity a foreign key points to is not a property of this entity; neither is what cannot be read.
        [InlineData("[{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"Customer_ID,Paid,ON_DELETE_CASCADE\"}]")]
        [InlineData("this is not json")]
        [InlineData("null")]
        [InlineData("[null]")]
        [InlineData("[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":null}]")]
        // Names that only begin like the property, or that the property begins with, are other names.
        [InlineData("[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Paid_Twice,Pai\"}]")]
        public async Task Rename_And_Delete_Property_That_No_Constraint_Names_Should_Work(string stored)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetConstraintsAsync(scene, "Orders", stored);

            var renamed = await scene.Owner.PostAsync($"{scene.PropertyUrl("Orders", "Paid")}/rename", new { NewName = "Settled" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.PropertyUrl("Orders", "Settled"))).StatusCode);
        }

        [Fact]
        public async Task Delete_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message_And_Keep_The_Property()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.DegeneratePropertyPath, HttpStatusCode.BadRequest, ApiError("Cannot delete property 'Amount'"));

            var response = await scene.Owner.DeleteAsync(scene.PropertyUrl("Orders", "Amount"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("Cannot delete property 'Amount'", error.Message);

            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Delete_When_The_Api_Server_Fails_Should_Return_502_And_Keep_The_Property(bool unreachable)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            Fail(FakeApiServer.DegeneratePropertyPath, unreachable);

            var response = await scene.Owner.DeleteAsync(scene.PropertyUrl("Orders", "Amount"));

            await AssertUpstreamAsync(response, unreachable ? ApiServerClient.UnusableAnswerMessage : "It broke.");
            await AssertNothingSavedAndNoCacheResetAsync(scene);
        }

        [Fact]
        public async Task Update_System_Property_Should_Return_409()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            foreach (var name in new[] { "ID", "Owner", "Created" })
            {
                await AssertConflictAsync(
                    await scene.Owner.PutAsync(scene.PropertyUrl("Orders", name), new { Description = "Changed" }.ToJsonContent()),
                    "Cannot edit system Properties",
                    "Property");
            }

            await AssertNothingSavedAndNoApiServerCallAsync(scene);
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

            var created = await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", "Number", decimalPlaces: 2).ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            AssertWarning(created);
            Assert.Equal("Total", (await created.ReadJsonAsync<PropertyResponse>()).Name);

            var updated = await scene.Owner.PutAsync(scene.PropertyUrl("Invoices", "Total"), new { Description = "Saved anyway" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            AssertWarning(updated);

            var renamed = await scene.Owner.PostAsync($"{scene.PropertyUrl("Invoices", "Total")}/rename", new { NewName = "Gross" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            AssertWarning(renamed);

            // Each change was saved.
            Assert.Equal("Saved anyway", (await LoadPropertyAsync(scene, "Invoices", "Gross")).Description);

            var deleted = await scene.Owner.DeleteAsync(scene.PropertyUrl("Invoices", "Gross"));
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

            await scene.Owner.PostAsync(scene.PropertiesUrl("Invoices"), CreateBody("Total", "Number", decimalPlaces: 2).ToJsonContent());
            AssertEndsWithOneCacheReset(2);

            await scene.Owner.PutAsync(scene.PropertyUrl("Invoices", "Total"), new { Description = "x" }.ToJsonContent());
            AssertEndsWithOneCacheReset(1);

            await scene.Owner.PostAsync($"{scene.PropertyUrl("Invoices", "Total")}/rename", new { NewName = "Gross" }.ToJsonContent());
            AssertEndsWithOneCacheReset(2);

            await scene.Owner.DeleteAsync(scene.PropertyUrl("Invoices", "Gross"));
            AssertEndsWithOneCacheReset(2);
        }

        [Fact]
        public async Task Update_Without_A_Change_Should_Still_Reset_The_Cache_And_Write_No_Audit_Row()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.PropertyUrl("Orders", "Amount"),
                new { Description = "The total", Minimum = 0, Maximum = 1000 }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
        }

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Warning_Header_Of_Every_Property_Write()
        {
            var client = await _portal.CreateUserClientAsync();
            var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")) ?? throw new InvalidOperationException("No document.");

            const string properties = "/api/v1/applications/{appToken}/entities/{entity}/properties";

            Assert.NotNull(document["paths"]?[properties]?["post"]?["responses"]?["201"]?["headers"]?["Warning"]);
            Assert.NotNull(document["paths"]?[$"{properties}/{{property}}"]?["put"]?["responses"]?["200"]?["headers"]?["Warning"]);
            Assert.NotNull(document["paths"]?[$"{properties}/{{property}}"]?["delete"]?["responses"]?["204"]?["headers"]?["Warning"]);
            Assert.NotNull(document["paths"]?[$"{properties}/{{property}}/rename"]?["post"]?["responses"]?["200"]?["headers"]?["Warning"]);
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

            public string PropertiesUrl(string entity) => $"{AppUrl}/entities/{entity}/properties";

            public string PropertyUrl(string entity, string property) => $"{PropertiesUrl(entity)}/{property}";
        }

        /// <summary>
        /// Three new users and one application of the owner, shared with the collaborator.
        /// 'Orders' has a property of every type, and a foreign key on 'Customer_ID'; 'Customers'
        /// has a unique constraint on 'Name'; 'Invoices' has only its ID; 'Files' and the
        /// read-only 'archive' take no new properties.
        /// </summary>
        private async Task<Scene> CreateSceneAsync()
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await _portal.CreateUserAsync();

            var server = await _portal.CreateServerAsync();
            var seeded = await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"properties-{Guid.NewGuid():N}", collaboratorEmail);
            var appId = seeded.Application.ID;

            await _portal.WithDbContextAsync(db =>
            {
                db.Entities.AddRange(SeedEntities(appId));

                return db.SaveChangesAsync();
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
                Property("Email", PropertyType.String, isSystem: true));

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
                "[{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Owner,Name\"}," +
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
        /// The API server answers every call of this slice the way a healthy one does.
        /// </summary>
        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.GeneratePropertyPath, HttpStatusCode.OK, string.Empty);
            _portal.ApiServer.Respond(FakeApiServer.RenameEntityPropertyPath, HttpStatusCode.OK, string.Empty);
            _portal.ApiServer.Respond(FakeApiServer.DegeneratePropertyPath, HttpStatusCode.OK, string.Empty);
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

        private static Dictionary<string, object?> CreateBody(string? name, string? type, int? decimalPlaces = null)
        {
            return new Dictionary<string, object?>
            {
                ["Name"] = name,
                ["Type"] = type,
                ["DecimalPlaces"] = decimalPlaces
            };
        }

        private static string ApiError(string message, string? property = null)
        {
            return JsonSerializer.Serialize(new { Code = "ERROR", Message = message, Property = property, Entity = (string?)null });
        }

        /// <summary>
        /// One request to each of the six endpoints, valid for the owner.
        /// </summary>
        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> AllRequests()
        {
            var requests = new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, appUrl) => client.GetAsync($"{appUrl}/entities/Orders/properties"),
                (client, appUrl) => client.GetAsync($"{appUrl}/entities/Orders/properties/Amount")
            };

            requests.AddRange(WriteRequests());

            return requests;
        }

        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> WriteRequests()
        {
            return new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, appUrl) => client.PostAsync($"{appUrl}/entities/Orders/properties", CreateBody("Total", "Boolean").ToJsonContent()),
                (client, appUrl) => client.PutAsync($"{appUrl}/entities/Orders/properties/Amount", new { Description = "Changed" }.ToJsonContent()),
                (client, appUrl) => client.PostAsync($"{appUrl}/entities/Orders/properties/Amount/rename", new { NewName = "Total" }.ToJsonContent()),
                (client, appUrl) => client.DeleteAsync($"{appUrl}/entities/Orders/properties/Amount")
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

        private async Task<DBWS_EntityProperty> LoadPropertyAsync(Scene scene, string entity, string property)
        {
            return Assert.Single((await LoadEntityAsync(scene, entity)).Properties, x => x.Name == property);
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
        /// A property as JSON without what differs between a request body and its stored row
        /// (ID, DateModified) and, when a name is given, between two properties (the name).
        /// </summary>
        private static string Comparable(DBWS_EntityProperty property, string? name = null)
        {
            var root = JsonSerializer.SerializeToNode(property)?.AsObject() ?? throw new InvalidOperationException("No property.");

            root.Remove("ID");
            root.Remove("DateModified");

            if (name is not null)
            {
                Assert.Equal(name, root["Name"]?.GetValue<string>());
                root.Remove("Name");
            }

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
        /// The seeded entities and every value of their properties are as the scene was seeded.
        /// </summary>
        private async Task AssertSeedUnchangedAsync(Scene scene)
        {
            Assert.Equal(Snapshot(SeedEntities(scene.AppId), byId: false), Snapshot(await LoadEntitiesAsync(scene), byId: true));
        }

        private static List<string> Snapshot(List<DBWS_Entity> entities, bool byId)
        {
            return entities
                .SelectMany(entity => (byId ? entity.Properties.OrderBy(x => x.ID).ToList() : entity.Properties).Select(x =>
                    $"{entity.Name} | {entity.EntConstraints} | {x.Name} | {x.TypeID} | {x.Description} | {x.IsSystem} | {x.IsPrimaryKey} | {x.Required} | {x.Encrypted} | {x.ValidationRegex} | {x.DecimalPlaces} | {x.Minimum} | {x.Maximum}"))
                .ToList();
        }

        /// <summary>
        /// Nothing was saved, nobody caused an audit row and the API server was not called at all.
        /// </summary>
        private async Task AssertNothingSavedAndNoApiServerCallAsync(Scene scene)
        {
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        private async Task AssertNothingSavedAndNoCacheResetAsync(Scene scene)
        {
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            await AssertNothingChangedAsync(scene);
        }

        private async Task AssertNothingChangedAsync(Scene scene)
        {
            await AssertSeedUnchangedAsync(scene);

            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Empty(await AuditRowsAsync(scene.CollaboratorEmail));
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.AppID == scene.AppId)));
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

        private static async Task AssertConflictAsync(HttpResponseMessage response, string message, string entity)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Conflict, error.Code);
            Assert.Equal(message, error.Message);
            Assert.Equal(entity, error.Entity);
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
    }
}
