using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
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
    public class SchemaImportApiTests
    {
        private const string Stopped = " The import stopped at this step; the steps before it stay applied.";
        private const string ApiPrefix = "/api/Application";

        private readonly PortalFactory _portal;

        public SchemaImportApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Diff ----------

        [Fact]
        public async Task Diff_And_Import_Into_An_Application_Without_Security_Rules_Should_Work()
        {
            var scene = await SchemaScene.CreateAsync(_portal);

            // What a new application stores: no security rules at all.
            var target = await scene.AddApplicationAsync("target", x =>
            {
                FillTarget(x);
                x.Security = null;
            });
            var source = await scene.AddApplicationAsync("source", FillSource);
            scene.ScriptApiServer();

            var payload = await (await scene.Owner.GetAsync(DiffUrl(target, source))).Content.ReadAsStringAsync();
            var diff = JsonSerializer.Deserialize<SchemaImportRequest>(payload) ?? throw new InvalidOperationException("No diff.");

            // Every rule of the source whose entity or custom endpoint it still has; never the Schema rule.
            Assert.Equal(
                new[]
                {
                    "0 Customers ANONYMOUS get 0 Name,Phone",
                    "0 Orders AUTHENTICATED get 1 Code,Amount",
                    "0 orders admin post 0 ",
                    "1 TopOrders ANONYMOUS get 0 "
                },
                (diff.Security ?? new List<SchemaImportSecurityRule>()).Select(x => $"{x.TypeID} {x.Name} {x.RoleID} {x.Action} {x.Record} {x.Properties}"));

            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(payload));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "Entity 'Customers' already exists — skipped creation." }, (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);

            // The first write of the column: the rules of the source, in its order.
            Assert.Contains(
                "security [{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name,Phone\",\"RateLimit\":null},",
                await scene.StoredSchemaAsync(target));

            Assert.Equal(
                "{\"Entities\":[],\"Security\":[],\"CustomEndpoints\":[]}",
                await (await scene.Owner.GetAsync(DiffUrl(target, source))).Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Diff_Should_List_What_The_Source_Has_And_This_Application_Lacks()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            var source = await scene.AddApplicationAsync("source", FillSource);

            var diff = await (await scene.Owner.GetAsync(DiffUrl(target, source))).ReadJsonAsync<SchemaImportRequest>();

            // In the order the source created them. Users is a system entity: never listed.
            var entities = diff.Entities ?? throw new InvalidOperationException("No Entities.");
            Assert.Equal(new[] { "Customers False", "OrderLines True", "Orders True" }, entities.Select(x => $"{x.Name} {x.IsNew}"));

            // An entity both have: only what is missing. 'name' and 'Name' are the same unique constraint.
            var customers = entities[0];
            Assert.Equal(new[] { "Phone", "Level" }, (customers.Properties ?? new List<SchemaImportProperty>()).Select(x => x.Name));
            Assert.Equal(new[] { "False 1 Phone" }, (customers.Constraints ?? new List<SchemaImportConstraint>()).Select(x => $"{x.IsSystem} {x.TypeID} {x.Properties}"));

            var level = (customers.Properties ?? new List<SchemaImportProperty>())[1];
            Assert.Equal((int)PropertyType.Number, level.TypeID);
            Assert.True(level.Required);
            Assert.Equal(0, level.DecimalPlaces);
            Assert.Equal(1, level.Minimum);
            Assert.Equal(5, level.Maximum);

            var phone = (customers.Properties ?? new List<SchemaImportProperty>())[0];
            Assert.Equal((int)PropertyType.String, phone.TypeID);
            Assert.Equal("^[0-9]+$", phone.ValidationRegex);
            Assert.Equal("Phone number", phone.Description);
            Assert.Equal(30, phone.Maximum);

            // A new entity: its custom properties and constraints, never the system ones.
            var orders = entities[2];
            Assert.Equal("Customer orders", orders.Description);
            Assert.True(orders.RequireChangeTracking);
            Assert.False(orders.HasDifferentiationProperty);
            Assert.Equal(new[] { "Customer_ID", "Agent_ID", "Amount", "Code", "Secret" }, (orders.Properties ?? new List<SchemaImportProperty>()).Select(x => x.Name));
            Assert.Equal(
                new[] { "2 Agent_ID,Users,ON_DELETE_SET_NULL", "1 Code" },
                (orders.Constraints ?? new List<SchemaImportConstraint>()).Select(x => $"{x.TypeID} {x.Properties}"));
            Assert.True((orders.Properties ?? new List<SchemaImportProperty>())[4].Encrypted);

            // Not the rule this application has with other values, not the Schema rule, not the rule of an entity that is gone.
            var security = diff.Security ?? throw new InvalidOperationException("No Security.");
            Assert.Equal(
                new[] { "0 Orders AUTHENTICATED get 1 Code,Amount", "0 orders admin post 0 ", "1 TopOrders ANONYMOUS get 0 " },
                security.Select(x => $"{x.TypeID} {x.Name} {x.RoleID} {x.Action} {x.Record} {x.Properties}"));
            Assert.Equal(10, security[0].RateLimit?.MaxRequests);
            Assert.Equal((int)EndpointRateLimit.Per_Minute, security[0].RateLimit?.TimeWindowType);
            Assert.Null(security[1].RateLimit);

            // 'getcustomers' is the 'GetCustomers' this application has.
            var endpoint = Assert.Single(diff.CustomEndpoints ?? new List<SchemaImportCustomEndpoint>());
            Assert.Equal("TopOrders", endpoint.Name);
            Assert.Equal("The biggest orders", endpoint.Description);
            Assert.Equal("SELECT TOP {Top} * FROM [Orders]", endpoint.Query);

            // Reading does not involve the API server.
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Diff_Should_Offer_The_Rules_As_The_Import_Accepts_Them_And_Be_Accepted_Posted_Back()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);

            // Stale or hidden things the source keeps in its rules: a custom property of Users (the
            // diff lists custom entities only, so the target would lack it), a property in another
            // letter case, an action Users does not offer, two rules for one cell, a record scope
            // that is no number the import knows.
            var source = await scene.AddApplicationAsync("source", x =>
            {
                FillTarget(x);

                x.Entities.Single(e => e.Name == "Users").Properties.Add(Stored("Nickname", PropertyType.String, p => p.Maximum = 50));
                x.Security = SchemaScene.Security(
                    SchemaScene.Rule(SecurityTypes.Entity, "Users", "AUTHENTICATED", "get", properties: "Email,Nickname"),
                    SchemaScene.Rule(SecurityTypes.Entity, "Users", "ANONYMOUS", "get", properties: "Nickname"),
                    SchemaScene.Rule(SecurityTypes.Entity, "Users", "admin", "post"),
                    SchemaScene.Rule(SecurityTypes.Entity, "Customers", "AUTHENTICATED", "get", properties: "name,Name"),
                    SchemaScene.Rule(SecurityTypes.Entity, "Customers", "editors", "get", properties: "Name"),
                    SchemaScene.Rule(SecurityTypes.Entity, "Customers", "editors", "get"),
                    SchemaScene.Rule(SecurityTypes.Entity, "Customers", "viewers", "get", (EndpointRecordAuthorization)5));
            });
            scene.ScriptApiServer();

            var payload = await (await scene.Owner.GetAsync(DiffUrl(target, source))).Content.ReadAsStringAsync();
            var diff = JsonSerializer.Deserialize<SchemaImportRequest>(payload) ?? throw new InvalidOperationException("No diff.");

            // What the target would have after the import: no Nickname, no post on Users, the first of two
            // rules for a cell, the property and the record scope as the Security tab shows them.
            Assert.Empty(diff.Entities ?? new List<SchemaImportEntity>());
            Assert.Equal(
                new[]
                {
                    "0 Users AUTHENTICATED get 0 Email",
                    "0 Users ANONYMOUS get 0 ",
                    "0 Customers AUTHENTICATED get 0 Name",
                    "0 Customers editors get 0 Name",
                    "0 Customers viewers get 0 "
                },
                (diff.Security ?? new List<SchemaImportSecurityRule>()).Select(x => $"{x.TypeID} {x.Name} {x.RoleID} {x.Action} {x.Record} {x.Properties}"));

            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(payload));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);

            var schema = await scene.StoredSchemaAsync(target);
            Assert.Contains("{\"Name\":\"Users\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Email\",\"RateLimit\":null}", schema);
            Assert.DoesNotContain("Nickname", schema);

            // Nothing is left to offer.
            Assert.Equal(
                "{\"Entities\":[],\"Security\":[],\"CustomEndpoints\":[]}",
                await (await scene.Owner.GetAsync(DiffUrl(target, source))).Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Diff_Should_List_Entities_And_Custom_Endpoints_In_The_Order_The_Source_Created_Them()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);

            // Created in the reverse of their name order, so the two orders cannot be mistaken.
            var source = await scene.AddApplicationAsync("source", application =>
            {
                application.Entities.Add(SchemaScene.Users());
                application.Entities.Add(SchemaScene.Custom("Zones", Array.Empty<DBWS_EntityProperty>()));
                application.Entities.Add(SchemaScene.Custom("Areas", Array.Empty<DBWS_EntityProperty>()));
                application.CustomEndpoints.Add(SchemaScene.Endpoint("Zulu", "SELECT 1"));
                application.CustomEndpoints.Add(SchemaScene.Endpoint("Alpha", "SELECT 2"));
            });

            var diff = await (await scene.Owner.GetAsync(DiffUrl(target, source))).ReadJsonAsync<SchemaImportRequest>();

            Assert.Equal(new[] { "Zones", "Areas" }, (diff.Entities ?? new List<SchemaImportEntity>()).Select(x => x.Name));
            Assert.Equal(new[] { "Zulu", "Alpha" }, (diff.CustomEndpoints ?? new List<SchemaImportCustomEndpoint>()).Select(x => x.Name));
        }

        [Fact]
        public async Task Diff_Of_Applications_With_The_Same_Schema_Should_Return_Three_Empty_Lists()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillSource);
            var source = await scene.AddApplicationAsync("source", FillSource);

            var response = await scene.Owner.GetAsync(DiffUrl(target, source));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"Entities\":[],\"Security\":[],\"CustomEndpoints\":[]}", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Diff_Should_Work_For_A_Collaborator_Of_Both_Applications()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            var source = await scene.AddApplicationAsync("source", FillSource);

            var diff = await (await scene.Collaborator.GetAsync(DiffUrl(target, source))).ReadJsonAsync<SchemaImportRequest>();

            Assert.Equal(3, diff.Entities?.Count);
        }

        [Fact]
        public async Task Diff_Against_Itself_Should_Return_400_On_Source()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);

            await SchemaScene.AssertValidationAsync(
                await scene.Owner.GetAsync(DiffUrl(target, target)),
                "Source: Cannot diff an application against itself");

            // The token of this application in another letter case is still this application.
            await SchemaScene.AssertValidationAsync(
                await scene.Owner.GetAsync($"{SchemaScene.AppUrl(target)}/schema-import/diff?Source={target.Token.ToUpperInvariant()}"),
                "Source: Cannot diff an application against itself");
        }

        [Fact]
        public async Task Diff_Without_A_Source_Should_Return_400_On_Source()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);

            await SchemaScene.AssertValidationAsync(await scene.Owner.GetAsync($"{SchemaScene.AppUrl(target)}/schema-import/diff"), "Source: Required");
            await SchemaScene.AssertValidationAsync(await scene.Owner.GetAsync($"{SchemaScene.AppUrl(target)}/schema-import/diff?Source="), "Source: Required");
        }

        [Fact]
        public async Task Diff_With_A_Source_The_Caller_Cannot_See_Should_Return_404()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            var foreign = await scene.AddApplicationAsync("foreign", scene.StrangerEmail, Array.Empty<string>(), FillSource);
            var source = await scene.AddApplicationAsync("source", FillSource);

            // The owner of the target does not see the stranger's application, and the other way round.
            await SchemaScene.AssertNotFoundAsync(await scene.Owner.GetAsync(DiffUrl(target, foreign)));
            await SchemaScene.AssertNotFoundAsync(await scene.Stranger.GetAsync(DiffUrl(foreign, target)));

            // A source the caller can see, written in another letter case, is no application at all.
            await SchemaScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"{SchemaScene.AppUrl(target)}/schema-import/diff?Source={source.Token.ToUpperInvariant()}"));

            // The same answer for a token that does not exist, so a token cannot be probed.
            var unseen = await (await scene.Owner.GetAsync(DiffUrl(target, foreign))).ReadJsonAsync<ErrorResponse>();
            var unknown = await (await scene.Owner.GetAsync($"{SchemaScene.AppUrl(target)}/schema-import/diff?Source={Guid.NewGuid()}")).ReadJsonAsync<ErrorResponse>();

            Assert.Equal(PortalErrorCode.NotFound, unknown.Code);
            Assert.Equal($"{unseen.Code} | {unseen.Message} | {unseen.Entity}", $"{unknown.Code} | {unknown.Message} | {unknown.Entity}");
        }

        // ---------- Import: what it does ----------

        [Fact]
        public async Task Import_Of_The_Diff_Should_Apply_Every_Kind_Of_Item_In_Order_And_Leave_Nothing_Missing()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            var source = await scene.AddApplicationAsync("source", FillSource);
            scene.ScriptApiServer();

            // The answer of the diff, posted back as it came.
            var payload = await (await scene.Owner.GetAsync(DiffUrl(target, source))).Content.ReadAsStringAsync();

            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(payload));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var body = await response.ReadJsonAsync<SchemaImportResponse>();
            Assert.Equal(new[] { "Entity 'Customers' already exists — skipped creation." }, body.Warnings);

            // Customers has no foreign key, Orders points to Users, OrderLines to Orders: in that
            // order, although the payload lists OrderLines before Orders. One cache reset, last.
            var requests = _portal.ApiServer.Requests;

            Assert.Equal(CallsOfTheDiff(scene.Server.ServerUrl), requests.Select(x => $"{x.Method} {x.Url}"));

            // Every call as the caller, for this application.
            var callerToken = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().Where(x => x.Email == scene.OwnerEmail).Select(x => x.AdminAuthToken).SingleAsync());

            Assert.All(requests, x =>
            {
                Assert.Equal(target.Token, x.Headers["x-application-token"]);
                Assert.Equal("portal", x.Headers["x-client-id"]);
                Assert.Equal($"Bearer {callerToken}", x.Headers["Authorization"]);
            });

            // A property goes as it is in the payload, for the entity the Portal has.
            var stored = await LoadEntitiesAsync(target);
            var phone = JsonSerializer.Deserialize<DBWS_EntityProperty>(requests[0].Body) ?? throw new InvalidOperationException("No body.");
            Assert.Equal("Phone", phone.Name);
            Assert.Equal((int)PropertyType.String, phone.TypeID);
            Assert.Equal(30, phone.Maximum);
            Assert.Equal("^[0-9]+$", phone.ValidationRegex);
            Assert.Equal("Phone number", phone.Description);
            Assert.False(phone.IsSystem);
            Assert.False(phone.IsPrimaryKey);
            Assert.Equal(0, phone.ID);
            Assert.Equal(stored.Single(x => x.Name == "Customers").ID, phone.EntityID);

            // Constraints go as the whole list: what the entity had, then what is new.
            Assert.Equal($"[{SchemaScene.OwnerConstraint},{SchemaScene.Unique("Name")},{SchemaScene.Unique("Phone")}]", requests[2].Body);
            Assert.Equal(
                $"[{SchemaScene.OwnerConstraint},{SchemaScene.ForeignKey("Agent_ID,Users,ON_DELETE_SET_NULL")},{SchemaScene.Unique("Code")}]",
                requests[10].Body);
            Assert.Equal($"[{SchemaScene.OwnerConstraint},{SchemaScene.ForeignKey("Order_ID,Orders")}]", requests[14].Body);

            // A new entity goes with the system properties and constraints the API server gave for it.
            var orders = JsonSerializer.Deserialize<DBWS_Entity>(requests[4].Body) ?? throw new InvalidOperationException("No body.");
            Assert.Equal("Orders", orders.Name);
            Assert.Equal("Customer orders", orders.Description);
            Assert.True(orders.RequireChangeTracking);
            Assert.False(orders.IsSystem);
            Assert.False(orders.IsReadOnly);
            Assert.Equal(target.ID, orders.AppID);
            Assert.Equal(new[] { "ID 0", "Owner 0", "Created 0" }, orders.Properties.Select(x => $"{x.Name} {x.ID}"));
            Assert.Equal($"[{SchemaScene.OwnerConstraint}]", orders.EntConstraints);

            // Stored: this application now has what the source has, besides what it had.
            var schema = await scene.StoredSchemaAsync(target);
            Assert.Contains("entity Orders | Customer orders | tracking True | differentiation False | system False | read-only False | " +
                $"[{SchemaScene.OwnerConstraint},{SchemaScene.ForeignKey("Agent_ID,Users,ON_DELETE_SET_NULL")},{SchemaScene.Unique("Code")}]", schema);
            Assert.Contains("  property Level | type 2 | required True | min 1 | max 5 | decimals 0 | encrypted False | regex  |  | system False | key False", schema);
            Assert.Contains("  property Secret | type 1 | required False | min  | max  | decimals  | encrypted True | regex  |  | system False | key False", schema);
            Assert.Contains("endpoint GetCustomers |  | SELECT * FROM [Customers]", schema);
            Assert.Contains("endpoint TopOrders | The biggest orders | SELECT TOP {Top} * FROM [Orders]", schema);

            // The rules it had stay first and as they were; the new ones follow. The source has the rule
            // of Orders for 'orders': it is stored under the name the entity has.
            Assert.Contains(
                "security [{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name\",\"RateLimit\":null}," +
                "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\",\"Record\":1,\"Properties\":\"Code,Amount\",\"RateLimit\":{\"MaxRequests\":10,\"TimeWindowType\":2,\"TimeWindow\":\"00:01:00\"}}," +
                "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"admin\",\"Action\":\"post\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}," +
                "{\"Name\":\"TopOrders\",\"TypeID\":1,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}]",
                schema);

            // One audit row per thing created or changed, in the order of the steps. (The rows of
            // the system properties of a new entity carry no application, as with every new entity.)
            Assert.Equal(AuditOfTheDiff(target, scene.OwnerEmail), await scene.AuditAsync(target));

            // Nothing is missing any more, and the source was only read.
            Assert.Equal(
                "{\"Entities\":[],\"Security\":[],\"CustomEndpoints\":[]}",
                await (await scene.Owner.GetAsync(DiffUrl(target, source))).Content.ReadAsStringAsync());
            Assert.Empty(await scene.AuditAsync(source));
        }

        [Fact]
        public async Task Import_Of_Items_The_Application_Has_In_Another_Letter_Case_Should_Skip_Them_With_A_Warning()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            var source = await scene.AddApplicationAsync("source", FillSource);
            scene.ScriptApiServer();

            // What the source has more, plus a rule, a custom endpoint, a property and a constraint
            // the application already has, written in another letter case.
            var diff = JsonNode.Parse(await (await scene.Owner.GetAsync(DiffUrl(target, source))).Content.ReadAsStringAsync())?.AsObject()
                ?? throw new InvalidOperationException("No diff.");

            diff["Security"]?.AsArray().Add(JsonNode.Parse("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"anonymous\",\"Action\":\"GET\",\"Record\":0,\"Properties\":\"Name\",\"RateLimit\":null}"));
            diff["CustomEndpoints"]?.AsArray().Add(JsonNode.Parse("{\"Name\":\"getCUSTOMERS\",\"Description\":null,\"Query\":\"SELECT 1\"}"));
            diff["Entities"]?[0]?["Properties"]?.AsArray().Add(JsonNode.Parse(
                "{\"Name\":\"NAME\",\"TypeID\":1,\"Required\":false,\"Minimum\":null,\"Maximum\":100,\"DecimalPlaces\":null,\"Encrypted\":false,\"ValidationRegex\":null,\"Description\":\"Not compared\"}"));
            diff["Entities"]?[0]?["Constraints"]?.AsArray().Add(JsonNode.Parse("{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\" NAME \"}"));

            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(diff.ToJsonString()));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[]
                {
                    "Entity 'Customers' already exists — skipped creation.",
                    "Property 'Customers.NAME' already exists — skipped creation.",
                    "Constraint on entity 'Customers' (TypeID=1, Properties='Name') already exists — skipped.",
                    "Security item 'Entity Customers - anonymous GET' already exists — skipped.",
                    "Custom endpoint 'getCUSTOMERS' already exists — skipped creation."
                },
                (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);

            // The calls and the audit rows are those of the diff alone...
            Assert.Equal(CallsOfTheDiff(scene.Server.ServerUrl), _portal.ApiServer.Requests.Select(x => $"{x.Method} {x.Url}"));
            Assert.Equal(AuditOfTheDiff(target, scene.OwnerEmail), await scene.AuditAsync(target));

            // ...and what was skipped left no trace.
            var schema = await scene.StoredSchemaAsync(target);
            Assert.DoesNotContain("NAME", schema);
            Assert.DoesNotContain("anonymous", schema);
            Assert.DoesNotContain("getCUSTOMERS", schema);
        }

        [Fact]
        public async Task Import_Of_What_The_Application_Has_Should_Change_Nothing_And_Warn_For_Every_Item()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            var source = await scene.AddApplicationAsync("source", FillSource);
            scene.ScriptApiServer();

            // What the source has more than the target is, for the source itself, all there.
            var payload = await (await scene.Owner.GetAsync(DiffUrl(target, source))).Content.ReadAsStringAsync();
            var before = await scene.StoredSchemaAsync(source);

            var response = await scene.Owner.PostAsync(ImportUrl(source), Json(payload));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[]
                {
                    "Entity 'Customers' already exists — skipped creation.",
                    "Property 'Customers.Phone' already exists — skipped creation.",
                    "Property 'Customers.Level' already exists — skipped creation.",
                    "Constraint on entity 'Customers' (TypeID=1, Properties='Phone') already exists — skipped.",
                    "Entity 'Orders' already exists — skipped creation.",
                    "Property 'Orders.Customer_ID' already exists — skipped creation.",
                    "Property 'Orders.Agent_ID' already exists — skipped creation.",
                    "Property 'Orders.Amount' already exists — skipped creation.",
                    "Property 'Orders.Code' already exists — skipped creation.",
                    "Property 'Orders.Secret' already exists — skipped creation.",
                    "Constraint on entity 'Orders' (TypeID=2, Properties='Agent_ID,Users,ON_DELETE_SET_NULL') already exists — skipped.",
                    "Constraint on entity 'Orders' (TypeID=1, Properties='Code') already exists — skipped.",
                    "Entity 'OrderLines' already exists — skipped creation.",
                    "Property 'OrderLines.Order_ID' already exists — skipped creation.",
                    "Constraint on entity 'OrderLines' (TypeID=2, Properties='Order_ID,Orders') already exists — skipped.",
                    "Security item 'Entity Orders - AUTHENTICATED get' already exists — skipped.",
                    "Security item 'Entity Orders - admin post' already exists — skipped.",
                    "Security item 'CustomEndpoint TopOrders - ANONYMOUS get' already exists — skipped.",
                    "Custom endpoint 'TopOrders' already exists — skipped creation."
                },
                (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);

            // Nothing to create: the cache reset is the only call.
            Assert.Equal(FakeApiServer.ClearCachePath, Assert.Single(_portal.ApiServer.Requests).Path);
            Assert.Equal(before, await scene.StoredSchemaAsync(source));
            Assert.Empty(await scene.AuditAsync(source));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"Entities\":null,\"Security\":null,\"CustomEndpoints\":null}")]
        [InlineData("{\"Entities\":[],\"Security\":[],\"CustomEndpoints\":[]}")]
        public async Task Import_Of_Nothing_Should_Return_No_Warnings_And_Reset_The_Cache(string payload)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(payload));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"Warnings\":[]}", await response.Content.ReadAsStringAsync());
            Assert.Equal(FakeApiServer.ClearCachePath, Assert.Single(_portal.ApiServer.Requests).Path);
            Assert.Empty(await scene.AuditAsync(target));
        }

        [Fact]
        public async Task Import_By_A_Collaborator_Should_Work_And_Be_Audited_Under_Its_Name()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Collaborator.PostAsync(ImportUrl(target), new
            {
                CustomEndpoints = new[]
                {
                    new { Name = "  Totals  ", Query = "SELECT 1" },
                    new { Name = "  getcustomers  ", Query = "SELECT 1" }
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // The name is matched without the spaces around it too: 'GetCustomers' is there.
            Assert.Equal(
                new[] { "Custom endpoint 'getcustomers' already exists — skipped creation." },
                (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);

            // The name is stored without the spaces around it, as POST custom-endpoints stores it.
            Assert.Contains("endpoint Totals |  | SELECT 1", await scene.StoredSchemaAsync(target));
            Assert.Equal(new[] { $"Custom Endpoint | Totals | Created | {scene.CollaboratorEmail}" }, await scene.AuditAsync(target));
        }

        [Fact]
        public async Task Import_Should_Keep_The_Order_Of_The_Payload_For_Entities_Not_Linked_To_Users()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // The order is computed from Users down, so a chain of foreign keys that does not
            // reach Users is processed as it is listed.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new object[]
                {
                    Entity("Lines", properties: new[] { ForeignKeyProperty("Bill_ID") }, constraints: new[] { new { TypeID = 2, Properties = "Bill_ID,Bills" } }),
                    Entity("Bills", properties: new[] { ForeignKeyProperty("Customer_ID") }, constraints: new[] { new { TypeID = 2, Properties = "Customer_ID,Customers" } })
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[] { "Lines", "Bills" },
                _portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath).Select(x => x.Url.Split('=')[1]));
        }

        [Fact]
        public async Task Import_Should_Process_An_Entity_After_Every_Entity_Its_Foreign_Keys_Lead_To()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // Tasks points to Users directly and through Projects: the longer way counts, so it
            // comes after Projects although its foreign key to Users is listed first.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new object[]
                {
                    Entity("Comments", properties: new[] { ForeignKeyProperty("Task_ID") }, constraints: new[] { new { TypeID = 2, Properties = "Task_ID,Tasks" } }),
                    Entity("Tasks", properties: new[] { ForeignKeyProperty("Assignee_ID"), ForeignKeyProperty("Project_ID") }, constraints: new[]
                    {
                        new { TypeID = 2, Properties = "Assignee_ID,Users" },
                        new { TypeID = 2, Properties = "Project_ID,Projects" }
                    }),
                    Entity("Projects", properties: new[] { ForeignKeyProperty("Lead_ID") }, constraints: new[] { new { TypeID = 2, Properties = "Lead_ID,Users" } })
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[] { "Projects", "Tasks", "Comments" },
                _portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath).Select(x => x.Url.Split('=')[1]));
        }

        [Fact]
        public async Task Import_Should_Order_Entities_From_The_Differentiation_Entity_Too()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", x =>
            {
                FillTarget(x);
                x.DifferentiationEntity = "Companies";
                x.Entities.Add(SchemaScene.Custom("Companies", Array.Empty<DBWS_EntityProperty>()));
            });
            scene.ScriptApiServer();

            // Neither leads to Users: without the differentiation entity they would stay as listed.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new object[]
                {
                    Entity("Desks", properties: new[] { ForeignKeyProperty("Office_ID") }, constraints: new[] { new { TypeID = 2, Properties = "Office_ID,Offices" } }),
                    Entity("Offices", properties: new[] { ForeignKeyProperty("Company_ID") }, constraints: new[] { new { TypeID = 2, Properties = "Company_ID,Companies" } })
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[] { "Offices", "Desks" },
                _portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath).Select(x => x.Url.Split('=')[1]));
        }

        [Fact]
        public async Task Import_Entity_With_A_Foreign_Key_To_Itself_Should_Work()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // A foreign-key cycle that is reached from Users: computing the order must end.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[]
                {
                    Entity("Comments", properties: new[] { ForeignKeyProperty("Author_ID"), ForeignKeyProperty("Parent_ID") }, constraints: new[]
                    {
                        new { TypeID = 2, Properties = "Author_ID,Users" },
                        new { TypeID = 2, Properties = "Parent_ID,Comments" }
                    })
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                $"[{SchemaScene.OwnerConstraint},{SchemaScene.ForeignKey("Author_ID,Users")},{SchemaScene.ForeignKey("Parent_ID,Comments")}]",
                Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath)).Body);
        }

        [Fact]
        public async Task Import_Entities_That_Point_To_Each_Other_Should_Work()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new object[]
                {
                    Entity("Alpha", properties: new[] { ForeignKeyProperty("User_ID"), ForeignKeyProperty("Bravo_ID") }, constraints: new[]
                    {
                        new { TypeID = 2, Properties = "User_ID,Users" },
                        new { TypeID = 2, Properties = "Bravo_ID,Bravo" }
                    }),
                    Entity("Bravo", properties: new[] { ForeignKeyProperty("Alpha_ID") }, constraints: new[] { new { TypeID = 2, Properties = "Alpha_ID,Alpha" } })
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[] { "Bravo", "Alpha" },
                _portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath).Select(x => x.Url.Split('=')[1]));
        }

        [Fact]
        public async Task Import_New_Entity_With_Differentiation_Property_Should_Ask_The_Api_Server_For_It_And_Store_The_Flag()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // Unlike POST entities: accepted although the application has no differentiation entity.
            var entity = Entity("Suppliers");
            entity["HasDifferentiationProperty"] = true;

            var response = await scene.Owner.PostAsync(ImportUrl(target), new { Entities = new[] { entity } }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.EndsWith("?entityHasDifferentiationProperty=True", Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GetSystemPropertiesAndConstraintsPath)).Url);

            var sent = JsonSerializer.Deserialize<DBWS_Entity>(Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateEntityPath)).Body) ?? throw new InvalidOperationException("No body.");
            Assert.True(sent.HasDifferentiationProperty);

            Assert.Contains("entity Suppliers |  | tracking False | differentiation True | ", await scene.StoredSchemaAsync(target));
        }

        [Fact]
        public async Task Import_Of_A_Payload_That_Names_Items_Twice_Should_Create_Each_Once_And_Warn_For_The_Second()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var rule = new { Name = "Suppliers", TypeID = 0, RoleID = "ANONYMOUS", Action = "get", Record = 0 };

            // The second mention finds what the first one created.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[]
                {
                    Entity(
                        "Suppliers",
                        properties: new[] { Property("Phone", PropertyType.String), Property("phone", PropertyType.String) },
                        constraints: new[] { new { TypeID = 1, Properties = "Phone" }, new { TypeID = 1, Properties = "Phone" } }),
                    Entity("suppliers")
                },
                Security = new[] { rule, rule },
                CustomEndpoints = new[] { new { Name = "Totals", Query = "SELECT 1" }, new { Name = "totals", Query = "SELECT 1" } }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[]
                {
                    "Property 'Suppliers.phone' already exists — skipped creation.",
                    "Entity 'suppliers' already exists — skipped creation.",
                    "Security item 'Entity Suppliers - ANONYMOUS get' already exists — skipped.",
                    "Custom endpoint 'totals' already exists — skipped creation."
                },
                (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);

            Assert.Equal(
                new[]
                {
                    FakeApiServer.GetSystemPropertiesAndConstraintsPath,
                    FakeApiServer.GenerateEntityPath,
                    FakeApiServer.GeneratePropertyPath,
                    FakeApiServer.GenerateConstraintsPath,
                    FakeApiServer.ClearCachePath
                },
                _portal.ApiServer.Requests.Select(x => x.Path));

            // The same constraint twice is sent once.
            Assert.Equal(
                $"[{SchemaScene.OwnerConstraint},{SchemaScene.Unique("Phone")}]",
                Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath)).Body);

            var schema = await scene.StoredSchemaAsync(target);
            // One entity, one property, one custom endpoint and one rule.
            Assert.Single(schema.Split(Environment.NewLine), x => x.StartsWith("entity Suppliers |", StringComparison.OrdinalIgnoreCase));
            Assert.Single(schema.Split(Environment.NewLine), x => x.StartsWith("  property Phone |", StringComparison.OrdinalIgnoreCase));
            Assert.Single(schema.Split(Environment.NewLine), x => x.StartsWith("endpoint Totals |", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, schema.Split("\"Name\":\"Suppliers\"").Length);
        }

        [Fact]
        public async Task Import_And_Comparison_Should_Take_The_First_Of_Two_Stored_Rules_With_The_Same_Key()
        {
            var scene = await SchemaScene.CreateAsync(_portal);

            // The same rule stored twice.
            var rule = SchemaScene.Rule(SecurityTypes.Entity, "Customers", "ANONYMOUS", "get", properties: "Name");
            var target = await scene.AddApplicationAsync("target", x =>
            {
                FillTarget(x);
                x.Security = SchemaScene.Security(rule, rule);
            });
            var single = await scene.AddApplicationAsync("single", FillTarget);
            scene.ScriptApiServer();
            var before = await scene.StoredSchemaAsync(target);

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Security = new[] { new { Name = "Customers", TypeID = 0, RoleID = "ANONYMOUS", Action = "get", Record = 0, Properties = "Name" } }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[] { "Security item 'Entity Customers - ANONYMOUS get' already exists — skipped." },
                (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);
            Assert.Equal(before, await scene.StoredSchemaAsync(target));

            var comparison = await scene.Owner.GetAsync($"{SchemaScene.AppUrl(single)}/comparison?Target={target.Token}");

            Assert.Equal(HttpStatusCode.OK, comparison.StatusCode);
            Assert.Empty((await comparison.ReadJsonAsync<ApplicationComparisonResponse>()).Security.Changed);
        }

        [Fact]
        public async Task Import_Existing_Items_With_Names_A_New_Item_Could_Not_Have_Should_Be_Skipped_With_A_Warning()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", x =>
            {
                FillTarget(x);
                x.CustomEndpoints.Add(SchemaScene.Endpoint("Top_1", "SELECT 1"));
            });
            scene.ScriptApiServer();

            // The name rules are for new items only: 'ID' (the stored
            // primary key, 2 characters) and 'Top_1' would both be refused as new names.
            var id = Property("ID", PropertyType.Number);
            id["Required"] = true;
            id["DecimalPlaces"] = 0;

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Customers", properties: new[] { id }) },
                CustomEndpoints = new[] { new { Name = "Top_1", Query = "SELECT 2" } }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[]
                {
                    "Entity 'Customers' already exists — skipped creation.",
                    "Property 'Customers.ID' already exists — skipped creation.",
                    "Custom endpoint 'Top_1' already exists — skipped creation."
                },
                (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);
            Assert.Equal(FakeApiServer.ClearCachePath, Assert.Single(_portal.ApiServer.Requests).Path);
            Assert.Empty(await scene.AuditAsync(target));
        }

        [Fact]
        public async Task Import_New_Entity_That_Lists_A_System_Property_Should_Match_It_With_What_The_Api_Server_Gives()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // 'ID' comes with every new entity, so it is not a new name to check.
            var id = Property("ID", PropertyType.Number);
            id["Required"] = true;
            id["DecimalPlaces"] = 0;

            var response = await scene.Owner.PostAsync(ImportUrl(target), new { Entities = new[] { Entity("Suppliers", properties: new[] { id }) } }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "Property 'Suppliers.ID' already exists — skipped creation." }, (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.GeneratePropertyPath));
        }

        [Fact]
        public async Task Import_Should_Ignore_A_Constraint_Without_Properties_And_A_Stored_Foreign_Key_That_Cannot_Be_Read()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var stored = $"{SchemaScene.OwnerConstraint},{SchemaScene.Unique("Name")},{SchemaScene.ForeignKey("Broken")}";
            var target = await scene.AddApplicationAsync("target", x =>
            {
                FillTarget(x);
                x.Entities.Single(e => e.Name == "Customers").EntConstraints = $"[{stored}]";
            });
            scene.ScriptApiServer();

            // Nothing to add: the API server is not asked to set the constraints again.
            var blank = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[]
                {
                    Entity("Customers", constraints: new[] { new { TypeID = 1, Properties = (string?)null }, new { TypeID = 1, Properties = (string?)"  " } })
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath));
            Assert.Empty(await scene.AuditAsync(target));

            // The stored foreign key 'Broken' names no property, so it is no conflict for a new one.
            var added = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Customers", properties: new[] { ForeignKeyProperty("Agent_ID") }, constraints: new[] { new { TypeID = 2, Properties = "Agent_ID,Users" } }) }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            Assert.Equal(
                $"[{stored},{SchemaScene.ForeignKey("Agent_ID,Users")}]",
                Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath)).Body);
        }

        [Theory]
        [InlineData(1, "Code;marker", "Property 'Code;marker' does not exist or is not a valid constraint identifier")]
        [InlineData(1, "Code--marker", "Property 'Code--marker' does not exist or is not a valid constraint identifier")]
        [InlineData(1, "Code]marker", "Property 'Code]marker' does not exist or is not a valid constraint identifier")]
        [InlineData(1, "Code,,Owner", "Property '' does not exist or is not a valid constraint identifier")]
        [InlineData(1, "Missing", "Property 'Missing' does not exist or is not a valid constraint identifier")]
        [InlineData(1, "Code,code", "A property can be listed only once")]
        [InlineData(1, "Secret", "Property 'Secret' is encrypted and cannot be unique")]
        [InlineData(2, "Agent_ID,Users;marker", ForeignKeyFormat)]
        [InlineData(2, "Agent_ID,Users,7", ForeignKeyFormat)]
        [InlineData(2, "Missing,Users", "Property 'Missing' does not exist or is not a valid constraint identifier")]
        [InlineData(2, "Agent_ID,Missing", "Entity 'Missing' does not exist or is not a valid constraint identifier")]
        [InlineData(2, "Code,Users", "A foreign key must use a custom Number property with 0 decimal places")]
        [InlineData(2, "Amount,Users", "A foreign key must use a custom Number property with 0 decimal places")]
        [InlineData(2, "Owner,Users", "A foreign key must use a custom Number property with 0 decimal places")]
        public async Task Import_Invalid_Constraint_On_A_New_Entity_Should_Reject_The_Whole_Payload_Before_Writing(int typeId, string properties, string message)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();
            var before = await scene.StoredSchemaAsync(target);
            var secret = Property("Secret", PropertyType.String);
            secret["Encrypted"] = true;
            var amount = Property("Amount", PropertyType.Number);
            amount["DecimalPlaces"] = 2;

            // A valid entity comes first, and the invalid constraint's columns are introduced by
            // this same request. Neither the API server nor tracked metadata may see any changes.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[]
                {
                    Entity("Suppliers"),
                    Entity("Orders", properties: new[] { Property("Code", PropertyType.String), secret, amount, ForeignKeyProperty("Agent_ID") },
                        constraints: new[] { new { TypeID = typeId, Properties = properties } })
                },
                CustomEndpoints = new[] { new { Name = "Totals", Query = "SELECT 1" } }
            }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, $"Entities[1].Constraints[0].Properties: {message}");
            Assert.Equal(before, await scene.StoredSchemaAsync(target));
            await AssertNothingAppliedAsync(scene, target);
        }

        [Theory]
        [InlineData(1, "Secret", "Property 'Secret' is encrypted and cannot be unique")]
        [InlineData(2, "Code,Users", "A foreign key must use a custom Number property with 0 decimal places")]
        [InlineData(2, "Amount,Users", "A foreign key must use a custom Number property with 0 decimal places")]
        [InlineData(2, "Customer_ID,Files", "A foreign key cannot point to Files")]
        public async Task Import_Constraint_Should_Check_Stored_Property_Types_And_Disallowed_Foreign_Entities(int typeId, string properties, string message)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", application =>
            {
                FillSource(application);
                application.Entities.Add(SchemaScene.Custom("Files", Array.Empty<DBWS_EntityProperty>()));
            });
            scene.ScriptApiServer();
            var before = await scene.StoredSchemaAsync(target);

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[]
                {
                    Entity("Suppliers"),
                    Entity("Orders", requireChangeTracking: true, constraints: new[] { new { TypeID = typeId, Properties = properties } })
                }
            }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, $"Entities[1].Constraints[0].Properties: {message}");
            Assert.Equal(before, await scene.StoredSchemaAsync(target));
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Empty(await scene.AuditAsync(target));
        }

        [Fact]
        public async Task Import_Constraint_Should_Use_Canonical_Names_From_Existing_And_Later_Imported_Schema()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[]
                {
                    Entity("Orders", properties: new[] { ForeignKeyProperty("Agent_ID"), ForeignKeyProperty("Invoice_ID") }, constraints: new[]
                    {
                        new { TypeID = 1, Properties = " agent_id , owner " },
                        new { TypeID = 2, Properties = " agent_id , users , 1 " },
                        new { TypeID = 2, Properties = " invoice_id , invoices " }
                    }),
                    Entity("Invoices")
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var constraints = JsonSerializer.Deserialize<List<EntityConstraint>>(
                Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath)).Body)
                ?? throw new InvalidOperationException("No constraints.");
            Assert.Equal(new[] { "Agent_ID,Owner", "Agent_ID,Users,ON_DELETE_SET_NULL", "Invoice_ID,Invoices" },
                constraints.Where(x => !x.IsSystem).Select(x => x.Properties));
        }

        [Theory]
        [InlineData("Agent_ID,Users,1", " agent_id , USERS , ON_DELETE_SET_NULL ")]
        [InlineData("Agent_ID,Users,ON_DELETE_SET_NULL", " agent_id , USERS , 1 ")]
        [InlineData("Agent_ID,Users", " agent_id , USERS , 0 ")]
        [InlineData("Agent_ID,Users,0", " agent_id , USERS ")]
        public async Task Import_Equivalent_Constraint_After_Canonicalization_Should_Skip_Without_Changing_Stored_Metadata(string stored, string imported)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", application =>
            {
                FillSource(application);
                application.Entities.Single(x => x.Name == "Orders").EntConstraints =
                    $"[{SchemaScene.OwnerConstraint},{SchemaScene.ForeignKey(stored)},{SchemaScene.Unique(" Code , Owner ")}]";
            });
            scene.ScriptApiServer();
            var before = await scene.StoredSchemaAsync(target);

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Orders", requireChangeTracking: true, constraints: new[]
                {
                    new { TypeID = 1, Properties = " owner , CODE " },
                    new { TypeID = 2, Properties = imported }
                }) }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(3, (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings.Count);
            Assert.Equal(FakeApiServer.ClearCachePath, Assert.Single(_portal.ApiServer.Requests).Path);
            Assert.Equal(before, await scene.StoredSchemaAsync(target));
            Assert.Empty(await scene.AuditAsync(target));
        }

        [Fact]
        public async Task Import_Constraint_For_A_System_Entity_Should_Return_403_Unless_The_Caller_Is_An_Administrator()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // Found whatever the letter case of its name. The entity listed before it is not created.
            var payload = new
            {
                Entities = new[]
                {
                    Entity("Suppliers"),
                    Entity("users", constraints: new[] { new { TypeID = 1, Properties = "Email" } })
                }
            };

            foreach (var client in new[] { scene.Owner, scene.Collaborator })
            {
                var refused = await SchemaScene.AssertErrorAsync(await client.PostAsync(ImportUrl(target), payload.ToJsonContent()), HttpStatusCode.Forbidden, PortalErrorCode.Forbidden);

                Assert.Equal("Only an administrator can change the constraints of a system entity.", refused.Message);
            }

            await AssertNothingAppliedAsync(scene, target);

            // An administrator who owns the application may.
            var (adminEmail, adminPassword) = await _portal.CreateAdminUserAsync();
            var admin = await _portal.CreateSignedInClientAsync(adminEmail, adminPassword);
            var adminTarget = await scene.AddApplicationAsync("admin-target", adminEmail, Array.Empty<string>(), FillTarget);

            var response = await admin.PostAsync(ImportUrl(adminTarget), payload.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal($"[{SchemaScene.Unique("Email")}]", Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GenerateConstraintsPath)).Body);
            Assert.Contains($"entity Users |  | tracking False | differentiation False | system True | read-only False | [{SchemaScene.Unique("Email")}]", await scene.StoredSchemaAsync(adminTarget));
        }

        [Fact]
        public async Task Import_When_The_Cache_Reset_Fails_Should_Succeed_With_The_Warning_Header()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();
            _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);

            var response = await scene.Owner.PostAsync(ImportUrl(target), new { Entities = new[] { Entity("Suppliers") } }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ApiServerCacheReset.WarningText, Assert.Single(response.Headers.GetValues(ApiServerCacheReset.WarningHeaderName)));
            Assert.Contains("entity Suppliers", await scene.StoredSchemaAsync(target));
        }

        // ---------- Import: what the application has, with other values ----------

        [Theory]
        [InlineData("RequireChangeTracking", "Entity 'customers': 'RequireChangeTracking' mismatch (existing: False, import: True).")]
        [InlineData("HasDifferentiationProperty", "Entity 'customers': 'HasDifferentiationProperty' mismatch (existing: False, import: True).")]
        public async Task Import_Existing_Entity_With_Another_Flag_Should_Return_400_Naming_It(string field, string message)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // Found whatever the letter case of its name.
            var entity = Entity("customers");
            entity[field] = true;

            var response = await scene.Owner.PostAsync(ImportUrl(target), new { Entities = new[] { entity } }.ToJsonContent());

            await AssertStoppedAsync(response, $"Entities[0].{field}", message);
            await AssertNothingAppliedAsync(scene, target);
        }

        [Theory]
        [InlineData("TypeID", 2, "Property 'Customers.name': 'TypeID' mismatch (existing: 1, import: 2).")]
        [InlineData("Required", true, "Property 'Customers.name': 'Required' mismatch (existing: False, import: True).")]
        [InlineData("Encrypted", true, "Property 'Customers.name': 'Encrypted' mismatch (existing: False, import: True).")]
        [InlineData("DecimalPlaces", 2, "Property 'Customers.name': 'DecimalPlaces' mismatch (existing: , import: 2).")]
        [InlineData("Maximum", 50, "Property 'Customers.name': 'Maximum' mismatch (existing: 100, import: 50).")]
        [InlineData("Minimum", 1, "Property 'Customers.name': 'Minimum' mismatch (existing: , import: 1).")]
        [InlineData("ValidationRegex", "^a$", "Property 'Customers.name': 'ValidationRegex' mismatch (existing: '', import: '^a$').")]
        public async Task Import_Existing_Property_With_Another_Value_Should_Return_400_Naming_It(string field, object value, string message)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // The stored property, found whatever the letter case of its name, with one value changed.
            var property = Property("name", PropertyType.String);
            property["Maximum"] = 100;
            property[field] = value;

            var response = await scene.Owner.PostAsync(
                ImportUrl(target),
                new { Entities = new[] { Entity("Customers", properties: new[] { Property("Phone", PropertyType.String), property }) } }.ToJsonContent());

            // The second property of the first entity. The property before it was created and stays.
            await AssertStoppedAsync(response, $"Entities[0].Properties[1].{field}", message);

            Assert.Contains("  property Phone |", await scene.StoredSchemaAsync(target));
            Assert.Equal(new[] { $"Property | Phone | Created | {scene.OwnerEmail}" }, await scene.AuditAsync(target));
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Fact]
        public async Task Import_Existing_Property_With_Another_Description_Should_Be_Skipped_With_A_Warning()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var property = Property("Name", PropertyType.String);
            property["Maximum"] = 100;
            property["Description"] = "Another text";

            var response = await scene.Owner.PostAsync(
                ImportUrl(target),
                new { Entities = new[] { Entity("Customers", properties: new[] { property }) } }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[] { "Entity 'Customers' already exists — skipped creation.", "Property 'Customers.Name' already exists — skipped creation." },
                (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);
            Assert.Empty(await scene.AuditAsync(target));
        }

        [Fact]
        public async Task Import_Foreign_Key_Of_A_Property_That_Has_Another_One_Should_Return_400_Naming_It()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillSource);
            scene.ScriptApiServer();
            var before = await scene.StoredSchemaAsync(target);

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[]
                {
                    Entity("Orders", requireChangeTracking: true, constraints: new[]
                    {
                        new { TypeID = 1, Properties = "CODE" },
                        new { TypeID = 2, Properties = "agent_id,Customers" }
                    })
                }
            }.ToJsonContent());

            await AssertStoppedAsync(
                response,
                "Entities[0].Constraints[1].Properties",
                "Entity 'Orders': FK constraint on local property 'Agent_ID' already exists with different configuration (existing: 'Agent_ID,Users,ON_DELETE_SET_NULL', import: 'Agent_ID,Customers').");

            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(before, await scene.StoredSchemaAsync(target));
            Assert.Empty(await scene.AuditAsync(target));
        }

        [Fact]
        public async Task Import_Existing_Security_Rule_With_Other_Values_Should_Return_400_Naming_It()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Suppliers") },
                Security = new[]
                {
                    new { Name = "Suppliers", TypeID = 0, RoleID = "ANONYMOUS", Action = "get", Record = 0, Properties = (string?)null },
                    new { Name = "customers", TypeID = 0, RoleID = "anonymous", Action = "GET", Record = 0, Properties = (string?)"Name,Owner" }
                },
                CustomEndpoints = new[] { new { Name = "Never", Query = "SELECT 1" } }
            }.ToJsonContent());

            await AssertStoppedAsync(
                response,
                "Security[1]",
                "Security item 'Entity Customers - anonymous GET' already exists with different configuration " +
                "(existing: '0_Customers_ANONYMOUS_get_0_Name_', import: '0_Customers_anonymous_GET_0_Name,Owner_').");

            // The entity before it stays; no rule was stored, the custom endpoint after it was not reached, no cache reset.
            var schema = await scene.StoredSchemaAsync(target);
            Assert.Contains("entity Suppliers", schema);
            Assert.DoesNotContain("\"Name\":\"Suppliers\"", schema);
            Assert.DoesNotContain("endpoint Never", schema);
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        // ---------- Import: a step fails on the API server ----------

        [Fact]
        public async Task Import_That_Fails_Half_Way_Should_Name_The_Step_Keep_What_Was_Applied_And_Not_Reset_The_Cache()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePropertyPath, HttpStatusCode.InternalServerError, EntityScene.ApiError("It broke."));

            var payload = new
            {
                Entities = new[] { Entity("Suppliers", properties: new[] { Property("Phone", PropertyType.String) }) },
                CustomEndpoints = new[] { new { Name = "Totals", Query = "SELECT 1" } }
            };

            var response = await scene.Owner.PostAsync(ImportUrl(target), payload.ToJsonContent());

            var error = await SchemaScene.AssertErrorAsync(response, HttpStatusCode.BadGateway, PortalErrorCode.UpstreamError);
            Assert.Equal($"Creating property 'Suppliers.Phone' on the API server: It broke.{Stopped}", error.Message);

            // The entity was created before the failing step and stays; the property and the endpoint are not there.
            var schema = await scene.StoredSchemaAsync(target);
            Assert.Contains("entity Suppliers", schema);
            Assert.DoesNotContain("property Phone", schema);
            Assert.DoesNotContain("endpoint Totals", schema);

            Assert.Equal(
                new[] { $"Entity | Suppliers | Created | {scene.OwnerEmail}" },
                await scene.AuditAsync(target));

            Assert.Equal(
                new[] { FakeApiServer.GetSystemPropertiesAndConstraintsPath, FakeApiServer.GenerateEntityPath, FakeApiServer.GeneratePropertyPath },
                _portal.ApiServer.Requests.Select(x => x.Path));

            // The same body again finishes the job: what was applied is skipped.
            _portal.ApiServer.Reset();
            scene.ScriptApiServer();

            var again = await scene.Owner.PostAsync(ImportUrl(target), payload.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal(new[] { "Entity 'Suppliers' already exists — skipped creation." }, (await again.ReadJsonAsync<SchemaImportResponse>()).Warnings);
            Assert.Equal(
                new[] { FakeApiServer.GeneratePropertyPath, FakeApiServer.ClearCachePath },
                _portal.ApiServer.Requests.Select(x => x.Path));

            schema = await scene.StoredSchemaAsync(target);
            Assert.Contains("  property Phone |", schema);
            Assert.Contains("endpoint Totals", schema);
        }

        [Fact]
        public async Task Import_Refused_By_The_Api_Server_Should_Return_400_With_Its_Message_For_The_Item()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GenerateEntityPath, HttpStatusCode.BadRequest, EntityScene.ApiError("Entity Suppliers already exists"));

            var response = await scene.Owner.PostAsync(
                ImportUrl(target),
                new { Entities = new[] { Entity("Customers"), Entity("Suppliers") } }.ToJsonContent());

            await AssertStoppedAsync(response, "Entities[1]", "Creating entity 'Suppliers' on the API server: Entity Suppliers already exists");
            await AssertNothingAppliedAsync(scene, target, apiServerCalled: true);
        }

        [Theory]
        [InlineData(FakeApiServer.GeneratePropertyPath, "Entities[0].Properties[0]", "Creating property 'Customers.Phone' on the API server")]
        [InlineData(FakeApiServer.GenerateConstraintsPath, "Entities[0].Constraints", "Setting the constraints of entity 'Customers' on the API server")]
        public async Task Import_Step_Refused_By_The_Api_Server_Should_Return_400_On_Its_Item_And_Store_Nothing(string path, string property, string step)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();
            _portal.ApiServer.Respond(path, HttpStatusCode.BadRequest, EntityScene.ApiError("Refused."));
            var before = await scene.StoredSchemaAsync(target);

            var entity = path == FakeApiServer.GeneratePropertyPath
                ? Entity("Customers", properties: new[] { Property("Phone", PropertyType.String) })
                : Entity("Customers", constraints: new[] { new { TypeID = 1, Properties = "Owner" } });

            var response = await scene.Owner.PostAsync(ImportUrl(target), new { Entities = new[] { entity } }.ToJsonContent());

            // The constraints are sent as one list, so the item is the list, without an index.
            await AssertStoppedAsync(response, property, $"{step}: Refused.");

            Assert.Equal(before, await scene.StoredSchemaAsync(target));
            Assert.Empty(await scene.AuditAsync(target));
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Theory]
        [InlineData(FakeApiServer.GetSystemPropertiesAndConstraintsPath, "Reading the system properties of entity 'Suppliers' from the API server")]
        [InlineData(FakeApiServer.GenerateEntityPath, "Creating entity 'Suppliers' on the API server")]
        public async Task Import_When_The_Api_Server_Cannot_Be_Reached_Should_Return_502_Naming_The_Step(string path, string step)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();
            _portal.ApiServer.Unreachable(path);

            var response = await scene.Owner.PostAsync(ImportUrl(target), new { Entities = new[] { Entity("Suppliers") } }.ToJsonContent());

            var error = await SchemaScene.AssertErrorAsync(response, HttpStatusCode.BadGateway, PortalErrorCode.UpstreamError);
            Assert.Equal($"{step}: {ApiServerClient.UnusableAnswerMessage}{Stopped}", error.Message);

            await AssertNothingAppliedAsync(scene, target, apiServerCalled: true);
        }

        [Fact]
        public async Task Import_When_The_Constraints_Fail_Should_Keep_The_Stored_Constraints()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GenerateConstraintsPath, HttpStatusCode.InternalServerError, EntityScene.ApiError("No such column."));
            var before = await scene.StoredSchemaAsync(target);

            var response = await scene.Owner.PostAsync(
                ImportUrl(target),
                new { Entities = new[] { Entity("Customers", constraints: new[] { new { TypeID = 1, Properties = "Owner" } }) } }.ToJsonContent());

            var error = await SchemaScene.AssertErrorAsync(response, HttpStatusCode.BadGateway, PortalErrorCode.UpstreamError);
            Assert.Equal($"Setting the constraints of entity 'Customers' on the API server: No such column.{Stopped}", error.Message);

            Assert.Equal(before, await scene.StoredSchemaAsync(target));
            Assert.Empty(await scene.AuditAsync(target));
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        // ---------- Import: a payload that is refused ----------

        [Fact]
        public async Task Import_With_Missing_Values_Should_Return_400_For_Each_And_Apply_Nothing()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(
                "{\"Entities\":[{\"Properties\":[{\"TypeID\":1}],\"Constraints\":[{\"TypeID\":0,\"Properties\":\"Code\"},{\"TypeID\":3,\"Properties\":\"Code\"}]}]," +
                "\"Security\":[{\"TypeID\":0,\"Name\":\" \",\"RateLimit\":{\"MaxRequests\":0,\"TimeWindowType\":9}}]," +
                "\"CustomEndpoints\":[{\"Description\":\"No name and no query\"}]}"));

            var error = await SchemaScene.AssertErrorAsync(response, HttpStatusCode.BadRequest, PortalErrorCode.Validation);

            Assert.Equal(
                new[]
                {
                    "CustomEndpoints[0].Name: Required",
                    "CustomEndpoints[0].Query: Required",
                    "Entities[0].Constraints[0].TypeID: Must be 1 (Unique) or 2 (ForeignKey)",
                    "Entities[0].Constraints[1].TypeID: Must be 1 (Unique) or 2 (ForeignKey)",
                    "Entities[0].Name: Required",
                    "Entities[0].Properties[0].Name: Required",
                    "Security[0].Action: Required",
                    "Security[0].Name: Required",
                    "Security[0].RateLimit.MaxRequests: Must be 1 or more",
                    "Security[0].RateLimit.TimeWindowType: Must be 0 (None), 1 (Per_Second), 2 (Per_Minute) or 3 (Per_Hour)",
                    "Security[0].RoleID: Required"
                },
                (error.Errors ?? new List<ErrorDetail>()).Select(x => $"{x.Property}: {x.Message}").OrderBy(x => x, StringComparer.Ordinal));

            await AssertNothingAppliedAsync(scene, target);
        }

        [Fact]
        public async Task Import_With_Null_Items_A_System_Constraint_Or_An_Unreadable_Foreign_Key_Should_Return_400_For_Each_And_Apply_Nothing()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // The first entity is fine and listed first: it is still not created. The on-delete
            // action of a foreign key may be written as its number, 0 to 2: not 7, and not a
            // number too large to read.
            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(
                "{\"Entities\":[{\"Name\":\"Suppliers\"},null,{\"Name\":\"Bills\",\"Properties\":[null," +
                "{\"Name\":\"Customer_ID\",\"TypeID\":2,\"DecimalPlaces\":0},{\"Name\":\"Agent_ID\",\"TypeID\":2,\"DecimalPlaces\":0}],\"Constraints\":[null," +
                "{\"TypeID\":2,\"Properties\":\"Customer_ID\"},{\"TypeID\":2,\"Properties\":null},{\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,ON_DELETE_EXPLODE\"}," +
                "{\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,ON_DELETE_CASCADE\"},{\"TypeID\":1,\"Properties\":null}," +
                "{\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,7\"},{\"TypeID\":2,\"Properties\":\"Customer_ID,Customers,99999999999\"}," +
                "{\"TypeID\":2,\"Properties\":\"Agent_ID,Users,1\"},{\"IsSystem\":true,\"TypeID\":1,\"Properties\":\"Code\"}]}]," +
                "\"Security\":[null],\"CustomEndpoints\":[null]}"));

            var error = await SchemaScene.AssertValidationAsync(
                response,
                "Entities[1]: Required",
                "Entities[2].Properties[0]: Required",
                "Entities[2].Constraints[0]: Required",
                $"Entities[2].Constraints[1].Properties: {ForeignKeyFormat}",
                $"Entities[2].Constraints[2].Properties: {ForeignKeyFormat}",
                $"Entities[2].Constraints[3].Properties: {ForeignKeyFormat}",
                $"Entities[2].Constraints[6].Properties: {ForeignKeyFormat}",
                $"Entities[2].Constraints[7].Properties: {ForeignKeyFormat}",
                "Entities[2].Constraints[9].IsSystem: Must be false: system constraints come with the entity",
                "Security[0]: Required",
                "CustomEndpoints[0]: Required");

            Assert.Equal(PortalApiErrors.ValidationMessage, error.Message);
            await AssertNothingAppliedAsync(scene, target);
        }

        [Theory]
        [InlineData("Sup pliers", "Allowed characters are a-z, A-Z and _")]
        [InlineData("Sup\n", "Allowed characters are a-z, A-Z and _")]
        [InlineData("Abc", "Must be 4 to 30 characters")]
        [InlineData("Abcdefghijklmnopqrstuvwxyzabcde", "Must be 4 to 30 characters")]
        public async Task Import_New_Entity_With_A_Name_Post_Entities_Refuses_Should_Return_400_On_Its_Name(string name, string message)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), new { Entities = new[] { Entity(name) } }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, $"Entities[0].Name: {message}");
            await AssertNothingAppliedAsync(scene, target);
        }

        [Theory]
        [InlineData("Customers", "Pho ne", 1, "Name", "Allowed characters are a-z, A-Z and _")]
        [InlineData("Customers", "Tel", 1, "Name", "Must be 4 to 120 characters")]
        [InlineData("Customers", "Phone_Data", 1, "Name", "The name cannot end with '_Data'")]
        [InlineData("Customers", "Phone", 0, "TypeID", "Must be 1 (String), 2 (Number), 3 (Boolean) or 4 (Date)")]
        [InlineData("Customers", "Phone", 5, "TypeID", "Must be 1 (String), 2 (Number), 3 (Boolean) or 4 (Date)")]
        [InlineData("Suppliers", "Tel", 1, "Name", "Must be 4 to 120 characters")]
        [InlineData("Suppliers", "Phone", 5, "TypeID", "Must be 1 (String), 2 (Number), 3 (Boolean) or 4 (Date)")]
        public async Task Import_New_Property_With_A_Name_Or_Type_Post_Properties_Refuses_Should_Return_400_On_It(string entity, string name, int typeId, string field, string message)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var property = Property(name, PropertyType.String);
            property["TypeID"] = typeId;

            // Of an entity the application has, and of one that is new too.
            var response = await scene.Owner.PostAsync(
                ImportUrl(target),
                new { Entities = new[] { Entity(entity, properties: new[] { property }) } }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, $"Entities[0].Properties[0].{field}: {message}");
            await AssertNothingAppliedAsync(scene, target);
        }

        [Theory]
        [InlineData("Top Orders")]
        [InlineData("Top_Orders")]
        [InlineData("Top1")]
        public async Task Import_New_Custom_Endpoint_With_A_Name_Post_Custom_Endpoints_Refuses_Should_Return_400_On_Its_Name(string name)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(
                ImportUrl(target),
                new { CustomEndpoints = new[] { new { Name = name, Query = "SELECT 1" } } }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, "CustomEndpoints[0].Name: Letters a-z and A-Z only, at most 80 characters");
            await AssertNothingAppliedAsync(scene, target);
        }

        [Fact]
        public async Task Import_With_A_Name_That_Is_Refused_In_Its_Last_Item_Should_Return_400_For_Each_And_Apply_Nothing()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // Everything before the custom endpoint is fine: it is still not applied.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Suppliers", properties: new[] { Property("Phone", PropertyType.String) }), Entity("Abc") },
                Security = new[] { new { Name = "Suppliers", TypeID = 0, RoleID = "ANONYMOUS", Action = "get" } },
                CustomEndpoints = new[] { new { Name = "Totals", Query = "SELECT 1" }, new { Name = "Get_Orders", Query = "SELECT 1" } }
            }.ToJsonContent());

            var error = await SchemaScene.AssertValidationAsync(
                response,
                "Entities[1].Name: Must be 4 to 30 characters",
                "CustomEndpoints[1].Name: Letters a-z and A-Z only, at most 80 characters");

            Assert.Equal(PortalApiErrors.ValidationMessage, error.Message);
            await AssertNothingAppliedAsync(scene, target);
        }

        // ---------- Import: security rules ----------

        // What PUT security/rules refuses (ApplicationSecurityApiTests), the import refuses too: each
        // rule in the payload of a new entity, a new custom endpoint and the rule, and nothing of it
        // is applied. Its place is the field of the import (TypeID, Record), its message the editor's.
        [Theory]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":3,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0}", "Security[0].TypeID: Must be 0 (Entity), 1 (CustomEndpoint) or 2 (Schema)")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":-1,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0}", "Security[0].TypeID: Must be 0 (Entity), 1 (CustomEndpoint) or 2 (Schema)")]
        [InlineData("{\"Name\":\"Nothing\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0}", "Security[0].Name: The application has no entity 'Nothing'")]
        [InlineData("{\"Name\":\"GetCustomers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0}", "Security[0].Name: The application has no entity 'GetCustomers'")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":1,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0}", "Security[0].Name: The application has no custom endpoint 'Customers'")]
        [InlineData("{\"Name\":\"Everything\",\"TypeID\":2,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0}", "Security[0].Name: Must be Schema")]
        [InlineData("{\"Name\":\"Schema\",\"TypeID\":2,\"RoleID\":\"X\",\"Action\":\"post\",\"Record\":0}", "Security[0].Action: Schema rules allow only get")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"patch\",\"Record\":0}", "Security[0].Action: Must be get, post, put or delete")]
        [InlineData("{\"Name\":\"Users\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"post\",\"Record\":0}", "Security[0].Action: Users does not allow post")]
        [InlineData("{\"Name\":\"GetCustomers\",\"TypeID\":1,\"RoleID\":\"X\",\"Action\":\"put\",\"Record\":0}", "Security[0].Action: GetCustomers does not allow put")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":5}", "Security[0].Record: Must be 0 (All) or 1 (Owned)")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":-1}", "Security[0].Record: Must be 0 (All) or 1 (Owned)")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name,Nope\"}", "Security[0].Properties: 'Nope' is not a property a get rule of Customers can list")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"ID\"}", "Security[0].Properties: 'ID' is not a property a get rule of Customers can list")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"name\"}", "Security[0].Properties: 'name' is not a property a get rule of Customers can list")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name, Created\"}", "Security[0].Properties: ' Created' is not a property a get rule of Customers can list")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"put\",\"Record\":0,\"Properties\":\"Owner\"}", "Security[0].Properties: 'Owner' is not a property a put rule of Customers can list")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"delete\",\"Record\":0,\"Properties\":\"Name\"}", "Security[0].Properties: 'Name' is not a property a delete rule of Customers can list")]
        [InlineData("{\"Name\":\"Users\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Nope\"}", "Security[0].Properties: 'Nope' is not a property a get rule of Users can list")]
        [InlineData("{\"Name\":\"GetCustomers\",\"TypeID\":1,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name\"}", "Security[0].Properties: Only entity rules have properties")]
        [InlineData("{\"Name\":\"Schema\",\"TypeID\":2,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name\"}", "Security[0].Properties: Only entity rules have properties")]
        // A new entity of the payload: its properties are the system ones and the ones the payload lists.
        [InlineData("{\"Name\":\"Suppliers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Phone,Nope\"}", "Security[0].Properties: 'Nope' is not a property a get rule of Suppliers can list")]
        [InlineData("{\"Name\":\"Suppliers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name\"}", "Security[0].Properties: 'Name' is not a property a get rule of Suppliers can list")]
        [InlineData("{\"Name\":\"Suppliers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"ID\"}", "Security[0].Properties: 'ID' is not a property a get rule of Suppliers can list")]
        [InlineData("{\"Name\":\"Suppliers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"put\",\"Record\":0,\"Properties\":\"Created\"}", "Security[0].Properties: 'Created' is not a property a put rule of Suppliers can list")]
        // One error for a rule, the first one in the order PUT security/rules checks them: type, name, role, action, record, properties.
        [InlineData("{\"Name\":\"Nothing\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"patch\",\"Record\":5,\"Properties\":\"Nope\"}", "Security[0].Name: The application has no entity 'Nothing'")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"patch\",\"Record\":5,\"Properties\":\"Nope\"}", "Security[0].Action: Must be get, post, put or delete")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":5,\"Properties\":\"Nope\"}", "Security[0].Record: Must be 0 (All) or 1 (Owned)")]
        public async Task Import_Security_Rule_The_Rules_Editor_Refuses_Should_Return_400_On_It_And_Apply_Nothing(string rule, string expected)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // The entity and the custom endpoint before and after the rule are fine: still not applied.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Suppliers", properties: new[] { Property("Phone", PropertyType.String) }) },
                Security = new[] { JsonNode.Parse(rule) },
                CustomEndpoints = new[] { new { Name = "Totals", Query = "SELECT 1" } }
            }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, expected);
            await AssertNothingAppliedAsync(scene, target);
        }

        [Fact]
        public async Task Import_Several_Bad_Security_Rules_Should_Return_400_For_Each_With_The_Other_Errors_In_Order()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // The first problem of each bad rule; the good rule between them is not mentioned.
            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(
                "{\"Entities\":[{\"Name\":\"Suppliers\"},{\"Name\":\"Abc\"}]," +
                "\"Security\":[{\"Name\":\"Nothing\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"patch\",\"Record\":5,\"Properties\":\"Nope\"}," +
                "{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name\"}," +
                "{\"Name\":\"Schema\",\"TypeID\":2,\"RoleID\":\"X\",\"Action\":\"post\",\"Record\":0}]," +
                "\"CustomEndpoints\":[{\"Name\":\"Get_Orders\",\"Query\":\"SELECT 1\"}]}"));

            var error = await SchemaScene.AssertValidationAsync(
                response,
                "Entities[1].Name: Must be 4 to 30 characters",
                "Security[0].Name: The application has no entity 'Nothing'",
                "Security[2].Action: Schema rules allow only get",
                "CustomEndpoints[0].Name: Letters a-z and A-Z only, at most 80 characters");

            Assert.Equal(PortalApiErrors.ValidationMessage, error.Message);
            await AssertNothingAppliedAsync(scene, target);
        }

        // The contract ([Required]) answers this before the checks of the rules are reached.
        [Theory]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\" \",\"Action\":\"get\",\"Record\":0}")]
        [InlineData("{\"Name\":\"Customers\",\"TypeID\":0,\"Action\":\"get\",\"Record\":0}")]
        public async Task Import_Security_Rule_Without_A_Role_Should_Return_400_On_It_And_Apply_Nothing(string rule)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Suppliers") },
                Security = new[] { JsonNode.Parse(rule) }
            }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, "Security[0].RoleID: Required");
            await AssertNothingAppliedAsync(scene, target);
        }

        [Fact]
        public async Task Import_Security_Rules_For_What_The_Payload_Creates_And_What_The_Application_Has_Should_Be_Accepted()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // Suppliers and Totals are new, Customers gets a Phone: the rules can name all of them.
            var totals = SecurityRule("Totals", 1, "ANONYMOUS", "get");
            totals["RateLimit"] = new { MaxRequests = 10, TimeWindowType = 2 };

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[]
                {
                    Entity("Suppliers", properties: new[] { Property("Phone", PropertyType.String) }),
                    Entity("Customers", properties: new[] { Property("Phone", PropertyType.String) })
                },
                Security = new[]
                {
                    SecurityRule("Suppliers", 0, "ANONYMOUS", "get", properties: "Phone,Owner,Created"),
                    SecurityRule("Suppliers", 0, "AUTHENTICATED", "put", properties: "Phone"),
                    SecurityRule("Suppliers", 0, "AUTHENTICATED", "delete"),
                    SecurityRule("Customers", 0, "AUTHENTICATED", "get", properties: "Name,Phone"),
                    SecurityRule("Users", 0, "AUTHENTICATED", "get", properties: "Email"),
                    SecurityRule("Schema", 2, "AUTHENTICATED", "get"),
                    totals
                },
                CustomEndpoints = new[] { new { Name = "Totals", Query = "SELECT 1" } }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "Entity 'Customers' already exists — skipped creation." }, (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);

            var schema = await scene.StoredSchemaAsync(target);

            foreach (var stored in new[]
            {
                "{\"Name\":\"Suppliers\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Phone,Owner,Created\",\"RateLimit\":null}",
                "{\"Name\":\"Suppliers\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"put\",\"Record\":0,\"Properties\":\"Phone\",\"RateLimit\":null}",
                "{\"Name\":\"Suppliers\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"delete\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}",
                "{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name,Phone\",\"RateLimit\":null}",
                "{\"Name\":\"Users\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Email\",\"RateLimit\":null}",
                "{\"Name\":\"Schema\",\"TypeID\":2,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}",
                "{\"Name\":\"Totals\",\"TypeID\":1,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":{\"MaxRequests\":10,\"TimeWindowType\":2,\"TimeWindow\":\"00:01:00\"}}"
            })
            {
                Assert.Contains(stored, schema);
            }
        }

        [Fact]
        public async Task Import_Of_The_Same_Payload_With_Security_Rules_Again_Should_Be_Accepted_And_Skip_Them_With_A_Warning()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var payload = new
            {
                Entities = new[] { Entity("Suppliers", properties: new[] { Property("Phone", PropertyType.String) }) },
                Security = new[]
                {
                    SecurityRule("Suppliers", 0, "ANONYMOUS", "get", properties: "Phone,Owner,Created"),
                    SecurityRule("Suppliers", 0, "AUTHENTICATED", "put", properties: "Phone")
                }
            };

            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PostAsync(ImportUrl(target), payload.ToJsonContent())).StatusCode);

            // The second time the entity and its property are the application's own, not the payload's.
            var response = await scene.Owner.PostAsync(ImportUrl(target), payload.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[]
                {
                    "Entity 'Suppliers' already exists — skipped creation.",
                    "Property 'Suppliers.Phone' already exists — skipped creation.",
                    "Security item 'Entity Suppliers - ANONYMOUS get' already exists — skipped.",
                    "Security item 'Entity Suppliers - AUTHENTICATED put' already exists — skipped."
                },
                (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);
        }

        [Fact]
        public async Task Import_Security_Rules_Naming_Their_Item_In_Another_Letter_Case_Should_Store_Them_Under_The_Name_The_Item_Has()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // The API server matches the name of a rule whatever its letter case, and so does the
            // import everywhere else (the diff offers such a rule too). The Security tab does not: a
            // rule stored as sent would be live but missing from it, and the next save of the tab
            // would delete it. So the rules are stored under the names the items have.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Suppliers") },
                Security = new[]
                {
                    SecurityRule("customers", 0, "X", "get", properties: "Name"),
                    SecurityRule("suppliers", 0, "X", "get"),
                    SecurityRule("getcustomers", 1, "X", "get"),
                    SecurityRule("SCHEMA", 2, "X", "get")
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var schema = await scene.StoredSchemaAsync(target);

            foreach (var stored in new[]
            {
                "{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name\",\"RateLimit\":null}",
                "{\"Name\":\"Suppliers\",\"TypeID\":0,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}",
                "{\"Name\":\"GetCustomers\",\"TypeID\":1,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}",
                "{\"Name\":\"Schema\",\"TypeID\":2,\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}"
            })
            {
                Assert.Contains(stored, schema);
            }

            // The Security tab lists all four.
            _portal.ApiServer.Respond(FakeApiServer.StatsDistinctPath, HttpStatusCode.OK, "[]");

            var security = await (await scene.Owner.GetAsync($"{SchemaScene.AppUrl(target)}/security")).ReadJsonAsync<SecurityResponse>();

            Assert.Equal(
                new[] { "Entity Customers ANONYMOUS", "Entity Customers X", "Entity Suppliers X", "CustomEndpoint GetCustomers X", "Schema Schema X" }
                    .OrderBy(x => x),
                security.Rules.Select(x => $"{x.Type} {x.Name} {x.RoleID}").OrderBy(x => x));
        }

        [Fact]
        public async Task Import_Security_Rule_Naming_Its_Item_In_Another_Letter_Case_Than_An_Existing_Rule_Should_Skip_It_With_A_Warning()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", x =>
            {
                FillTarget(x);

                // What an older import stored as sent.
                x.Security = SchemaScene.Security(SchemaScene.Rule(SecurityTypes.Entity, "customers", "editors", "get"));
            });
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Security = new[] { SecurityRule("Customers", 0, "editors", "get") }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "Security item 'Entity Customers - editors get' already exists — skipped." }, (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);
        }

        [Fact]
        public async Task Import_Security_Rules_With_A_Role_No_User_Holds_Should_Be_Accepted_As_Sent()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // As in PUT security/rules, which only flags such a role as Orphaned: any non-blank text
            // is a role. 'anonymous' is not ANONYMOUS to the API server, but a user may hold it.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Security = new[]
                {
                    SecurityRule("Customers", 0, "editors", "delete"),
                    SecurityRule("Customers", 0, "anonymous", "post", properties: "Name"),
                    SecurityRule("Customers", 0, "Authenticated", "put", properties: "Name")
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var schema = await scene.StoredSchemaAsync(target);

            foreach (var role in new[] { "editors", "anonymous", "Authenticated" })
            {
                Assert.Contains($"\"RoleID\":\"{role}\",", schema);
            }
        }

        [Fact]
        public async Task Import_Security_Rules_With_Owned_Where_It_Has_No_Effect_Should_Be_Accepted_As_PUT_Rules_Does()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // Owned has no effect for ANONYMOUS, for an entity without an owner (Users) and for an
            // endpoint (see Security in the developer guide). The rules editor keeps such a rule and
            // warns in the grid, so it is no reason to refuse one.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Security = new[]
                {
                    SecurityRule("Customers", 0, "ANONYMOUS", "delete", record: 1),
                    SecurityRule("Users", 0, "AUTHENTICATED", "get", record: 1),
                    SecurityRule("GetCustomers", 1, "ANONYMOUS", "get", record: 1)
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var schema = await scene.StoredSchemaAsync(target);

            foreach (var stored in new[]
            {
                "{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"delete\",\"Record\":1,\"Properties\":null,\"RateLimit\":null}",
                "{\"Name\":\"Users\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\",\"Record\":1,\"Properties\":null,\"RateLimit\":null}",
                "{\"Name\":\"GetCustomers\",\"TypeID\":1,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":1,\"Properties\":null,\"RateLimit\":null}"
            })
            {
                Assert.Contains(stored, schema);
            }
        }

        [Fact]
        public async Task Import_Security_Rule_For_A_Property_The_Payload_Lists_In_Another_Letter_Case_Should_Return_400_On_It()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // 'NAME' is the property Customers has, so it is skipped, not added: a rule lists the
            // property the application has, spelled as it is, as the Security tab compares them.
            var name = Property("NAME", PropertyType.String);
            name["Maximum"] = 100;

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("customers", properties: new[] { name }) },
                Security = new[] { SecurityRule("Customers", 0, "X", "get", properties: "NAME") }
            }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, "Security[0].Properties: 'NAME' is not a property a get rule of Customers can list");
            await AssertNothingAppliedAsync(scene, target);
        }

        [Fact]
        public async Task Import_Two_Security_Rules_For_The_Same_Cell_With_Other_Values_Should_Return_400_On_The_Second_And_Apply_Nothing()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // Left to its step, the second rule would stop the import after Suppliers was created.
            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Suppliers") },
                Security = new[]
                {
                    SecurityRule("Suppliers", 0, "ANONYMOUS", "get"),
                    SecurityRule("suppliers", 0, "anonymous", "GET", record: 1)
                }
            }.ToJsonContent());

            await SchemaScene.AssertValidationAsync(response, "Security[1]: Same type, name, role and action as Security[0], with other values");
            await AssertNothingAppliedAsync(scene, target);
        }

        [Fact]
        public async Task Import_Two_Identical_Security_Rules_Should_Store_One_And_Skip_The_Other_With_A_Warning()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Security = new[]
                {
                    SecurityRule("Customers", 0, "editors", "get", properties: "Name"),
                    SecurityRule("Customers", 0, "editors", "get", properties: "Name")
                }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "Security item 'Entity Customers - editors get' already exists — skipped." }, (await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);
            Assert.Equal(1, (await scene.StoredSchemaAsync(target)).Split("\"RoleID\":\"editors\"").Length - 1);
        }

        [Theory]
        [InlineData(true, "get", null)]
        [InlineData(true, "put", "Security[0].Properties: 'Companies_ID' is not a property a put rule of Suppliers can list")]
        [InlineData(false, "get", "Security[0].Properties: 'Companies_ID' is not a property a get rule of Suppliers can list")]
        public async Task Import_Security_Rule_For_The_Differentiation_Property_Of_A_New_Entity_Should_Follow_Its_Flag(bool hasDifferentiationProperty, string action, string? expected)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", x =>
            {
                FillTarget(x);
                x.DifferentiationEntity = "Companies";
            });
            scene.ScriptApiServer();

            // The API server gives the new entity the property Companies_ID when it has the flag;
            // it can be read but not written.
            var entity = Entity("Suppliers");
            entity["HasDifferentiationProperty"] = hasDifferentiationProperty;

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { entity },
                Security = new[] { SecurityRule("Suppliers", 0, "X", action, properties: "Companies_ID") }
            }.ToJsonContent());

            if (expected is null)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("\"Properties\":\"Companies_ID\"", await scene.StoredSchemaAsync(target));
            }
            else
            {
                await SchemaScene.AssertValidationAsync(response, expected);
                await AssertNothingAppliedAsync(scene, target);
            }
        }

        [Fact]
        public async Task Import_Of_The_Example_Payload_Of_The_Import_Screen_Should_Work()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // examplePayload of src/Apilane.Portal.Ui/src/lib/schemaImport.ts, which that screen offers
            // in its Help: it must stay valid whatever is checked.
            var response = await scene.Owner.PostAsync(ImportUrl(target), Json(
                "{\"Entities\":[" +
                "{\"Name\":\"Product\",\"Description\":\"Product catalog\",\"RequireChangeTracking\":false,\"HasDifferentiationProperty\":false,\"Properties\":[" +
                "{\"Name\":\"Title\",\"TypeID\":1,\"Required\":true,\"Minimum\":null,\"Maximum\":200,\"DecimalPlaces\":null,\"Encrypted\":false,\"ValidationRegex\":null,\"Description\":null}," +
                "{\"Name\":\"Price\",\"TypeID\":2,\"Required\":true,\"Minimum\":0,\"Maximum\":null,\"DecimalPlaces\":2,\"Encrypted\":false,\"ValidationRegex\":null,\"Description\":null}],\"Constraints\":[]}," +
                "{\"Name\":\"OrderItem\",\"Description\":\"Line item in an order\",\"RequireChangeTracking\":false,\"HasDifferentiationProperty\":false,\"Properties\":[" +
                "{\"Name\":\"Product_ID\",\"TypeID\":2,\"Required\":true,\"Minimum\":null,\"Maximum\":null,\"DecimalPlaces\":0,\"Encrypted\":false,\"ValidationRegex\":null,\"Description\":null}]," +
                "\"Constraints\":[{\"TypeID\":2,\"Properties\":\"Product_ID,Product\"}]}]," +
                "\"Security\":[{\"Name\":\"Product\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}]," +
                "\"CustomEndpoints\":[{\"Name\":\"GetAllProduct\",\"Description\":\"Retrieves all products.\",\"Query\":\"SELECT * FROM [Product];\"}]}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.ReadJsonAsync<SchemaImportResponse>()).Warnings);
            Assert.Contains("{\"Name\":\"Product\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}", await scene.StoredSchemaAsync(target));
        }

        [Fact]
        public async Task Import_Should_Store_Security_Rules_And_Property_Values_As_Sent()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();

            // What PUT security/rules and POST properties would clean up, the import passes on: a
            // rule with its action in another letter case (the API server matches it whatever its
            // case); a Boolean with a maximum and decimal places.
            var property = Property("Active", PropertyType.Boolean);
            property["Maximum"] = 7;
            property["DecimalPlaces"] = 3;

            var response = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                Entities = new[] { Entity("Customers", properties: new[] { property }) },
                Security = new[] { new { Name = "Customers", TypeID = 0, RoleID = "editors", Action = "DELETE", Record = 1 } }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var schema = await scene.StoredSchemaAsync(target);
            Assert.Contains("  property Active | type 3 | required False | min  | max 7 | decimals 3 |", schema);
            Assert.Contains("{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"editors\",\"Action\":\"DELETE\",\"Record\":1,\"Properties\":null,\"RateLimit\":null}]", schema);

            var sent = JsonSerializer.Deserialize<DBWS_EntityProperty>(Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GeneratePropertyPath)).Body);
            Assert.Equal(7, sent?.Maximum);
            Assert.Equal(3, sent?.DecimalPlaces);
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData("{\"Name\":\"not a list\"}")]
        public async Task Stored_Security_Rules_That_Cannot_Be_Read_Should_Return_409_And_Stay(string stored)
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", x =>
            {
                FillTarget(x);
                x.Security = stored;
            });
            var source = await scene.AddApplicationAsync("source", FillSource);
            scene.ScriptApiServer();

            var message = $"The stored security rules of application '{target.Name}' cannot be read.";

            var diff = await SchemaScene.AssertErrorAsync(await scene.Owner.GetAsync(DiffUrl(target, source)), HttpStatusCode.Conflict, PortalErrorCode.Conflict);
            Assert.Equal(message, diff.Message);

            // The other way round too: the unreadable rules are those of the source.
            await SchemaScene.AssertErrorAsync(await scene.Owner.GetAsync(DiffUrl(source, target)), HttpStatusCode.Conflict, PortalErrorCode.Conflict);

            // Refused before the entity listed with the rule is created.
            var import = await SchemaScene.AssertErrorAsync(
                await scene.Owner.PostAsync(ImportUrl(target), new
                {
                    Entities = new[] { Entity("Suppliers") },
                    Security = new[] { new { Name = "Customers", TypeID = 0, RoleID = "ANONYMOUS", Action = "get" } }
                }.ToJsonContent()),
                HttpStatusCode.Conflict,
                PortalErrorCode.Conflict);

            Assert.Equal(message, import.Message);

            var schema = await scene.StoredSchemaAsync(target);
            Assert.Contains($"security {stored}", schema);
            Assert.DoesNotContain("entity Suppliers", schema);
            Assert.Empty(await scene.AuditAsync(target));
            Assert.Empty(_portal.ApiServer.Requests);

            // A body without security rules does not touch the stored ones, so it goes through.
            var withoutRules = await scene.Owner.PostAsync(ImportUrl(target), new
            {
                CustomEndpoints = new[] { new { Name = "Totals", Query = "SELECT 1" } }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, withoutRules.StatusCode);
            Assert.Contains($"security {stored}", await scene.StoredSchemaAsync(target));
        }

        // ---------- Access ----------

        [Fact]
        public async Task Stranger_Admin_And_Unknown_Token_Should_Return_404_And_Change_Nothing()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            var source = await scene.AddApplicationAsync("source", FillSource);
            var admin = await _portal.CreateAdminClientAsync();
            scene.ScriptApiServer();

            foreach (var send in AllRequests(source))
            {
                await SchemaScene.AssertNotFoundAsync(await send(scene.Stranger, SchemaScene.AppUrl(target)));
                await SchemaScene.AssertNotFoundAsync(await send(admin, SchemaScene.AppUrl(target)));
                await SchemaScene.AssertNotFoundAsync(await send(scene.Owner, $"/api/v1/applications/{Guid.NewGuid()}"));
            }

            await AssertNothingAppliedAsync(scene, target);
        }

        [Fact]
        public async Task Anonymous_Should_Return_401_And_Change_Nothing()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            var source = await scene.AddApplicationAsync("source", FillSource);
            var anonymous = _portal.CreateAnonymousClient();
            scene.ScriptApiServer();

            foreach (var send in AllRequests(source))
            {
                await SchemaScene.AssertErrorAsync(await send(anonymous, SchemaScene.AppUrl(target)), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            }

            await AssertNothingAppliedAsync(scene, target);
        }

        [Fact]
        public async Task Import_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var target = await scene.AddApplicationAsync("target", FillTarget);
            scene.ScriptApiServer();
            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await scene.Owner.PostAsync(ImportUrl(target), new { Entities = new[] { Entity("Suppliers") } }.ToJsonContent());

            var error = await SchemaScene.AssertErrorAsync(response, HttpStatusCode.Forbidden, PortalErrorCode.Forbidden);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);

            await AssertNothingAppliedAsync(scene, target);

            // Reading the diff needs no header.
            var source = await scene.AddApplicationAsync("source", FillSource);
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync(DiffUrl(target, source))).StatusCode);
        }

        // ---------- Contract ----------

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Warning_Header_Of_The_Import()
        {
            var client = await _portal.CreateAdminClientAsync();

            var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
            var import = document?["paths"]?["/api/v1/applications/{appToken}/schema-import"]?["post"];

            Assert.NotNull(import?["responses"]?["200"]?["headers"]?["Warning"]);
            Assert.Contains("NOT ATOMIC", import?["summary"]?.GetValue<string>());
        }

        // ---------- Helpers ----------

        private const string ForeignKeyFormat = "A foreign key is 'Property,Entity' or 'Property,Entity,ON_DELETE_NO_ACTION' (or ON_DELETE_SET_NULL, ON_DELETE_CASCADE)";

        private static string DiffUrl(DBWS_Application application, DBWS_Application source)
        {
            return $"{SchemaScene.AppUrl(application)}/schema-import/diff?Source={source.Token}";
        }

        private static string ImportUrl(DBWS_Application application)
        {
            return $"{SchemaScene.AppUrl(application)}/schema-import";
        }

        private static StringContent Json(string body)
        {
            return new StringContent(body, Encoding.UTF8, "application/json");
        }

        /// <summary>
        /// The calls of an import of the diff between <see cref="FillTarget"/> and <see cref="FillSource"/>.
        /// Customers has no foreign key, Orders points to Users, OrderLines to Orders: in that
        /// order, although the payload lists OrderLines before Orders. One cache reset, last.
        /// </summary>
        private static string[] CallsOfTheDiff(string server)
        {
            return new[]
            {
                $"POST {server}{ApiPrefix}/GenerateProperty?Entity=Customers",
                $"POST {server}{ApiPrefix}/GenerateProperty?Entity=Customers",
                $"POST {server}{ApiPrefix}/GenerateConstraints?Entity=Customers",
                $"GET {server}{ApiPrefix}/GetSystemPropertiesAndConstraints?entityHasDifferentiationProperty=False",
                $"POST {server}{ApiPrefix}/GenerateEntity",
                $"POST {server}{ApiPrefix}/GenerateProperty?Entity=Orders",
                $"POST {server}{ApiPrefix}/GenerateProperty?Entity=Orders",
                $"POST {server}{ApiPrefix}/GenerateProperty?Entity=Orders",
                $"POST {server}{ApiPrefix}/GenerateProperty?Entity=Orders",
                $"POST {server}{ApiPrefix}/GenerateProperty?Entity=Orders",
                $"POST {server}{ApiPrefix}/GenerateConstraints?Entity=Orders",
                $"GET {server}{ApiPrefix}/GetSystemPropertiesAndConstraints?entityHasDifferentiationProperty=False",
                $"POST {server}{ApiPrefix}/GenerateEntity",
                $"POST {server}{ApiPrefix}/GenerateProperty?Entity=OrderLines",
                $"POST {server}{ApiPrefix}/GenerateConstraints?Entity=OrderLines",
                $"GET {server}{ApiPrefix}/ClearCache"
            };
        }

        /// <summary>
        /// The audit rows of that import: one per thing created or changed, in the order of the
        /// steps. (The rows of the system properties of a new entity carry no application, as
        /// with every new entity.)
        /// </summary>
        private static IEnumerable<string> AuditOfTheDiff(DBWS_Application target, string ownerEmail)
        {
            return new[]
            {
                "Property | Phone | Created",
                "Property | Level | Created",
                "Entity | Customers | Modified",
                "Entity | Orders | Created",
                "Property | Customer_ID | Created",
                "Property | Agent_ID | Created",
                "Property | Amount | Created",
                "Property | Code | Created",
                "Property | Secret | Created",
                "Entity | Orders | Modified",
                "Entity | OrderLines | Created",
                "Property | Order_ID | Created",
                "Entity | OrderLines | Modified",
                $"Application | {target.Name} | Modified",
                "Custom Endpoint | TopOrders | Created"
            }.Select(x => $"{x} | {ownerEmail}");
        }

        /// <summary>
        /// The application the tests import into: Users, Customers with a Name that is unique, one
        /// custom endpoint and one security rule.
        /// </summary>
        private static void FillTarget(DBWS_Application application)
        {
            application.Entities.Add(SchemaScene.Users());
            application.Entities.Add(SchemaScene.Custom("Customers", new[] { Stored("Name", PropertyType.String, x => x.Maximum = 100) }, SchemaScene.Unique("Name")));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("GetCustomers", "SELECT * FROM [Customers]"));
            application.Security = SchemaScene.Security(SchemaScene.Rule(SecurityTypes.Entity, "Customers", "ANONYMOUS", "get", properties: "Name"));
        }

        /// <summary>
        /// What <see cref="FillTarget"/> has, written in another letter case here and there, and
        /// more: two properties and a constraint on Customers, the entities OrderLines and Orders
        /// (created in that order; OrderLines points to Orders, Orders to Users), a custom
        /// endpoint and security rules of every kind.
        /// </summary>
        private static void FillSource(DBWS_Application application)
        {
            application.Entities.Add(SchemaScene.Users());

            application.Entities.Add(SchemaScene.Custom(
                "Customers",
                new[]
                {
                    Stored("Name", PropertyType.String, x => x.Maximum = 100),
                    Stored("Phone", PropertyType.String, x =>
                    {
                        x.Maximum = 30;
                        x.ValidationRegex = "^[0-9]+$";
                        x.Description = "Phone number";
                    }),
                    Stored("Level", PropertyType.Number, x =>
                    {
                        x.Required = true;
                        x.DecimalPlaces = 0;
                        x.Minimum = 1;
                        x.Maximum = 5;
                    })
                },
                SchemaScene.Unique("name"),
                SchemaScene.Unique("Phone")));

            application.Entities.Add(SchemaScene.Custom(
                "OrderLines",
                new[] { Stored("Order_ID", PropertyType.Number, x => { x.Required = true; x.DecimalPlaces = 0; }) },
                SchemaScene.ForeignKey("Order_ID,Orders")));

            var orders = SchemaScene.Custom(
                "Orders",
                new[]
                {
                    Stored("Customer_ID", PropertyType.Number, x => { x.Required = true; x.DecimalPlaces = 0; }),
                    Stored("Agent_ID", PropertyType.Number, x => x.DecimalPlaces = 0),
                    Stored("Amount", PropertyType.Number, x => { x.DecimalPlaces = 2; x.Minimum = 0; }),
                    Stored("Code", PropertyType.String, x => x.Maximum = 20),
                    Stored("Secret", PropertyType.String, x => x.Encrypted = true)
                },
                SchemaScene.ForeignKey("Agent_ID,Users,ON_DELETE_SET_NULL"),
                SchemaScene.Unique("Code"));

            orders.Description = "Customer orders";
            orders.RequireChangeTracking = true;
            application.Entities.Add(orders);

            application.CustomEndpoints.Add(SchemaScene.Endpoint("getcustomers", "SELECT [Name] FROM [Customers]"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("TopOrders", "SELECT TOP {Top} * FROM [Orders]", "The biggest orders"));

            application.Security = SchemaScene.Security(
                SchemaScene.Rule(SecurityTypes.Entity, "Customers", "ANONYMOUS", "get", properties: "Name,Phone"),
                SchemaScene.Rule(SecurityTypes.Entity, "Orders", "AUTHENTICATED", "get", EndpointRecordAuthorization.Owned, "Code,Amount", DBWS_Security.RateLimitItem.New(10, EndpointRateLimit.Per_Minute)),
                SchemaScene.Rule(SecurityTypes.Entity, "orders", "admin", "post"),
                SchemaScene.Rule(SecurityTypes.CustomEndpoint, "TopOrders", "ANONYMOUS", "get"),
                SchemaScene.Rule(SecurityTypes.Schema, "Schema", "AUTHENTICATED", "get"),
                SchemaScene.Rule(SecurityTypes.Entity, "Gone", "ANONYMOUS", "get"));
        }

        private static DBWS_EntityProperty Stored(string name, PropertyType type, Action<DBWS_EntityProperty> set)
        {
            var property = EntityScene.Property(name, type);
            set(property);

            return property;
        }

        /// <summary>
        /// An entity of a payload, with the values a new one gets when nothing else is said.
        /// </summary>
        private static Dictionary<string, object?> Entity(string name, bool requireChangeTracking = false, object? properties = null, object? constraints = null)
        {
            return new Dictionary<string, object?>
            {
                ["Name"] = name,
                ["Description"] = null,
                ["RequireChangeTracking"] = requireChangeTracking,
                ["HasDifferentiationProperty"] = false,
                ["Properties"] = properties,
                ["Constraints"] = constraints
            };
        }

        /// <summary>
        /// A security rule of a payload, with the values a rule without a rate limit has.
        /// </summary>
        private static Dictionary<string, object?> SecurityRule(string name, int typeId, string roleId, string action, int record = 0, string? properties = null)
        {
            return new Dictionary<string, object?>
            {
                ["Name"] = name,
                ["TypeID"] = typeId,
                ["RoleID"] = roleId,
                ["Action"] = action,
                ["Record"] = record,
                ["Properties"] = properties,
                ["RateLimit"] = null
            };
        }

        private static Dictionary<string, object?> Property(string name, PropertyType type)
        {
            return new Dictionary<string, object?>
            {
                ["Name"] = name,
                ["TypeID"] = (int)type,
                ["Required"] = false,
                ["Minimum"] = null,
                ["Maximum"] = null,
                ["DecimalPlaces"] = null,
                ["Encrypted"] = false,
                ["ValidationRegex"] = null,
                ["Description"] = null
            };
        }

        private static Dictionary<string, object?> ForeignKeyProperty(string name)
        {
            var property = Property(name, PropertyType.Number);
            property["DecimalPlaces"] = 0;
            return property;
        }

        /// <summary>
        /// One request to each endpoint of the slice, valid for the owner.
        /// </summary>
        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> AllRequests(DBWS_Application source)
        {
            return new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, appUrl) => client.GetAsync($"{appUrl}/schema-import/diff?Source={source.Token}"),
                (client, appUrl) => client.PostAsync($"{appUrl}/schema-import", new { Entities = new[] { Entity("Suppliers") } }.ToJsonContent())
            };
        }

        private async Task<List<DBWS_Entity>> LoadEntitiesAsync(DBWS_Application application)
        {
            var appId = application.ID;

            return await _portal.WithDbContextAsync(db => db.Entities.AsNoTracking().Where(x => x.AppID == appId).ToListAsync());
        }

        /// <summary>
        /// A 400 VALIDATION of a step: the item of the payload in Errors, and a message that says
        /// what is wrong and that the steps before it stay.
        /// </summary>
        private static async Task AssertStoppedAsync(HttpResponseMessage response, string property, string message)
        {
            var error = await SchemaScene.AssertValidationAsync(response, $"{property}: {message}");

            Assert.Equal(property, error.Property);
            Assert.Equal(message + Stopped, error.Message);
        }

        /// <summary>
        /// The application is as <see cref="FillTarget"/> made it, nobody caused an audit row for
        /// it and the API server was not asked to reset its cache or, unless
        /// <paramref name="apiServerCalled"/>, for anything at all.
        /// </summary>
        private async Task AssertNothingAppliedAsync(SchemaScene scene, DBWS_Application target, bool apiServerCalled = false)
        {
            var schema = await scene.StoredSchemaAsync(target);

            Assert.Equal(new[] { "Users", "Customers" }, (await LoadEntitiesAsync(target)).OrderBy(x => x.ID).Select(x => x.Name));
            Assert.DoesNotContain("property Phone", schema);
            Assert.Contains("security [{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Name\",\"RateLimit\":null}]", schema);
            Assert.EndsWith("endpoint GetCustomers |  | SELECT * FROM [Customers]", schema);

            Assert.Empty(await scene.AuditAsync(target));
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));

            if (!apiServerCalled)
            {
                Assert.Empty(_portal.ApiServer.Requests);
            }
        }
    }
}
