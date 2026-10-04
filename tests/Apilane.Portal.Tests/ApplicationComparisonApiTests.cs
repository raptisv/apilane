using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class ApplicationComparisonApiTests
    {
        private readonly PortalFactory _portal;

        public ApplicationComparisonApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Fact]
        public async Task Comparison_Should_Equal_What_The_Razor_Dialog_Loads()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);
            var target = await scene.AddApplicationAsync("right", FillRight);

            await AssertEqualsRazorAsync(scene, source, target);

            // The other way round, where Added and Removed change places.
            await AssertEqualsRazorAsync(scene, target, source);

            // Reading does not involve the API server and writes no audit row.
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Empty(await scene.AuditAsync(source));
            Assert.Empty(await scene.AuditAsync(target));
        }

        [Fact]
        public async Task Comparison_Should_List_Added_Removed_And_Changed_Entities()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);
            var target = await scene.AddApplicationAsync("right", FillRight);

            var response = await scene.Owner.GetAsync(CompareUrl(source, target));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.ReadJsonAsync<ApplicationComparisonResponse>();

            Assert.Equal(source.Name, body.ApplicationSource);
            Assert.Equal(target.Name, body.ApplicationTarget);

            // Added is what only the target has; names are matched exactly, so 'invoices' is not 'Invoices'.
            Assert.Equal(new[] { "Shipments", "invoices" }, body.Entities.Added.Select(x => x.Name));
            Assert.Equal(new[] { "Orders", "Invoices" }, body.Entities.Removed.Select(x => x.Name));

            // An added entity comes with its custom properties and constraints only.
            var shipments = body.Entities.Added[0];
            Assert.Equal("Where things go", shipments.Description);
            Assert.True(shipments.RequireChangeTracking);
            Assert.Equal(new[] { "Customer_ID Number 2", "Address String 1" }, shipments.Properties.Select(x => $"{x.Name} {x.TypeLabel} {x.TypeID}"));
            Assert.True(shipments.Properties[1].Required);
            Assert.Equal(250, shipments.Properties[1].Maximum);
            Assert.Equal(
                new[] { "2 Customer_ID,Customers,ON_DELETE_CASCADE", "1 Address" },
                shipments.Constraints.Select(x => $"{x.TypeID} {x.Properties}"));

            // Changed: the system entity Users (a custom property more) and Customers.
            Assert.Equal(new[] { "Users", "Customers" }, body.Entities.Changed.Select(x => x.Name));

            var users = body.Entities.Changed[0];
            Assert.Equal("Nickname", Assert.Single(users.PropertiesAdded).Name);
            Assert.Empty(users.MetadataChanges);
            Assert.Empty(users.PropertiesChanged);
            Assert.Empty(users.PropertiesRemoved);
            Assert.Empty(users.ConstraintsAdded);
            Assert.Empty(users.ConstraintsRemoved);

            var customers = body.Entities.Changed[1];

            Assert.Equal(
                new[] { "Description: Clients -> Clients of the shop", "RequireChangeTracking: False -> True", "HasDifferentiationProperty: False -> True" },
                customers.MetadataChanges.Select(Text));

            // Added and removed go by the exact name...
            Assert.Equal(new[] { "notes", "Email" }, customers.PropertiesAdded.Select(x => x.Name));
            Assert.Equal(new[] { "Phone String", "Notes String" }, customers.PropertiesRemoved.Select(x => $"{x.Name} {x.TypeLabel}"));

            // ...while the values are compared whatever the letter case of the name: 'Notes' and 'notes'.
            Assert.Equal(new[] { "Name", "Level", "Notes" }, customers.PropertiesChanged.Select(x => x.Name));
            Assert.Equal(new[] { "Maximum: 100 -> 200" }, customers.PropertiesChanged[0].Changes.Select(Text));
            Assert.Equal(
                new[] { "Type: Number -> String", "Required: True -> False", "Minimum: 1 -> ", "Maximum: 5 -> ", "DecimalPlaces: 0 -> " },
                customers.PropertiesChanged[1].Changes.Select(Text));
            Assert.Equal(
                new[] { "Encrypted: False -> True", "ValidationRegex:  -> ^[a-z]+$", "Description: Old text -> New text" },
                customers.PropertiesChanged[2].Changes.Select(Text));
            Assert.Null(customers.PropertiesChanged[1].Changes[2].After);

            // 'Name' and 'NAME' are the same unique constraint.
            Assert.Equal(new[] { "1 Email" }, customers.ConstraintsAdded.Select(x => $"{x.TypeID} {x.Properties}"));
            Assert.Equal(new[] { "1 Phone" }, customers.ConstraintsRemoved.Select(x => $"{x.TypeID} {x.Properties}"));
        }

        [Fact]
        public async Task Comparison_Should_List_Added_Removed_And_Changed_Custom_Endpoints_And_Security_Rules()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);
            var target = await scene.AddApplicationAsync("right", FillRight);

            var body = await (await scene.Owner.GetAsync(CompareUrl(source, target))).ReadJsonAsync<ApplicationComparisonResponse>();

            Assert.Equal(new[] { "OnlyRight", "casename" }, body.CustomEndpoints.Added.Select(x => x.Name));
            Assert.Equal(new[] { "OnlyLeft", "CaseName" }, body.CustomEndpoints.Removed.Select(x => x.Name));
            Assert.Equal("SELECT 2", body.CustomEndpoints.Added[0].Query);
            Assert.Equal("Right only", body.CustomEndpoints.Added[0].Description);

            // 'Same' has the same query and description in both.
            Assert.Equal(
                new[] { "Totals | Sums -> Sums | SELECT 1 -> SELECT 1 + 1", "Described | Old -> New | SELECT 3 -> SELECT 3" },
                body.CustomEndpoints.Changed.Select(x => $"{x.Name} | {x.DescriptionBefore} -> {x.DescriptionAfter} | {x.QueryBefore} -> {x.QueryAfter}"));

            // Not the Schema rules, not the rules of 'Gone' and 'customers', which neither application has.
            Assert.Equal(
                new[]
                {
                    "Entity Shipments - ANONYMOUS get | ANONYMOUS | Entity | 20 request per hour | get | All | Address",
                    "Entity Shipments - AUTHENTICATED post | AUTHENTICATED | Entity |  | post | Owned | "
                },
                body.Security.Added.Select(Text));

            Assert.Equal(new[] { "Entity Orders - admin get | admin | Entity |  | get | All | " }, body.Security.Removed.Select(Text));

            // No rate limit is null, never a text.
            Assert.Null(body.Security.Added[1].RateLimit);
            Assert.Null(body.Security.Removed[0].RateLimit);

            // The rule of the editors is the same in both.
            Assert.Equal(
                new[]
                {
                    "Entity Customers - ANONYMOUS get",
                    "Entity Customers - AUTHENTICATED put",
                    "CustomEndpoint Totals - ANONYMOUS get"
                },
                body.Security.Changed.Select(x => x.Name));

            // Property lists are sorted before they are compared and shown.
            Assert.Equal("Entity Customers - ANONYMOUS get | ANONYMOUS | Entity |  | get | All | Name,Phone", Text(body.Security.Changed[0].SecurityBefore));
            Assert.Equal("Entity Customers - ANONYMOUS get | ANONYMOUS | Entity |  | get | All | Email,Name,Phone", Text(body.Security.Changed[0].SecurityAfter));

            Assert.Equal("Entity Customers - AUTHENTICATED put | AUTHENTICATED | Entity | 5 request per second | put | Owned | Name", Text(body.Security.Changed[1].SecurityBefore));
            Assert.Equal("Entity Customers - AUTHENTICATED put | AUTHENTICATED | Entity | 10 request per minute | put | All | Name", Text(body.Security.Changed[1].SecurityAfter));

            Assert.Null(body.Security.Changed[2].SecurityBefore.RateLimit);
            Assert.Equal("3 request per hour", body.Security.Changed[2].SecurityAfter.RateLimit);
            Assert.Equal("CustomEndpoint", body.Security.Changed[2].SecurityAfter.Type);
        }

        [Fact]
        public async Task Comparison_Of_Applications_With_The_Same_Schema_Should_Return_Empty_Lists_As_The_Razor_Dialog_Does()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);
            var target = await scene.AddApplicationAsync("twin", FillLeft);

            var body = await (await scene.Owner.GetAsync(CompareUrl(source, target))).ReadJsonAsync<ApplicationComparisonResponse>();

            Assert.Equal(source.Name, body.ApplicationSource);
            Assert.Equal(target.Name, body.ApplicationTarget);

            Assert.Empty(body.Entities.Added);
            Assert.Empty(body.Entities.Removed);
            Assert.Empty(body.Entities.Changed);
            Assert.Empty(body.CustomEndpoints.Added);
            Assert.Empty(body.CustomEndpoints.Removed);
            Assert.Empty(body.CustomEndpoints.Changed);
            Assert.Empty(body.Security.Added);
            Assert.Empty(body.Security.Removed);
            Assert.Empty(body.Security.Changed);

            await AssertEqualsRazorAsync(scene, source, target);
        }

        [Fact]
        public async Task Comparison_With_An_Application_Without_Security_Rules_Should_Work_As_The_Razor_Dialog_Does()
        {
            var scene = await SchemaScene.CreateAsync(_portal);

            // What a new application stores: no security rules at all.
            var source = await scene.AddApplicationAsync("left", x =>
            {
                FillLeft(x);
                x.Security = null;
            });
            var target = await scene.AddApplicationAsync("right", FillRight);

            var body = await (await scene.Owner.GetAsync(CompareUrl(source, target))).ReadJsonAsync<ApplicationComparisonResponse>();

            // Every rule of the target whose entity or custom endpoint it has: not 'customers', not the Schema rule.
            Assert.Equal(6, body.Security.Added.Count);
            Assert.Empty(body.Security.Removed);
            Assert.Empty(body.Security.Changed);

            await AssertEqualsRazorAsync(scene, source, target);
            await AssertEqualsRazorAsync(scene, target, source);
        }

        [Fact]
        public async Task Comparison_Should_Work_For_A_Collaborator_Of_Both_Applications()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);
            var target = await scene.AddApplicationAsync("right", FillRight);

            var body = await (await scene.Collaborator.GetAsync(CompareUrl(source, target))).ReadJsonAsync<ApplicationComparisonResponse>();

            Assert.Equal(2, body.Entities.Added.Count);
        }

        [Fact]
        public async Task Comparison_With_Itself_Should_Return_400_On_Target()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);

            await SchemaScene.AssertValidationAsync(await scene.Owner.GetAsync(CompareUrl(source, source)), "Target: Cannot compare to self");
        }

        [Fact]
        public async Task Comparison_Without_A_Target_Should_Return_400_On_Target()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);

            await SchemaScene.AssertValidationAsync(await scene.Owner.GetAsync($"{SchemaScene.AppUrl(source)}/comparison"), "Target: Required");
            await SchemaScene.AssertValidationAsync(await scene.Owner.GetAsync($"{SchemaScene.AppUrl(source)}/comparison?Target="), "Target: Required");
        }

        [Fact]
        public async Task Comparison_With_An_Application_The_Caller_Cannot_See_Should_Return_404()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);
            var foreign = await scene.AddApplicationAsync("foreign", scene.StrangerEmail, Array.Empty<string>(), FillRight);
            var admin = await _portal.CreateAdminClientAsync();

            // The target is not the caller's...
            await SchemaScene.AssertNotFoundAsync(await scene.Owner.GetAsync(CompareUrl(source, foreign)));
            await SchemaScene.AssertNotFoundAsync(await scene.Collaborator.GetAsync(CompareUrl(source, foreign)));
            await SchemaScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"{SchemaScene.AppUrl(source)}/comparison?Target={Guid.NewGuid()}"));

            // ...a token in another letter case is no application at all...
            await SchemaScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"{SchemaScene.AppUrl(source)}/comparison?Target={source.Token.ToUpperInvariant()}"));

            // ...the application of the route is not the caller's: the stranger, who owns the target, and an administrator.
            await SchemaScene.AssertNotFoundAsync(await scene.Stranger.GetAsync(CompareUrl(source, foreign)));
            await SchemaScene.AssertNotFoundAsync(await admin.GetAsync(CompareUrl(source, foreign)));
            await SchemaScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"/api/v1/applications/{Guid.NewGuid()}/comparison?Target={source.Token}"));

            // The same answer whether the target exists or not, so a token cannot be probed.
            var unseen = await (await scene.Owner.GetAsync(CompareUrl(source, foreign))).ReadJsonAsync<ErrorResponse>();
            var unknown = await (await scene.Owner.GetAsync($"{SchemaScene.AppUrl(source)}/comparison?Target={Guid.NewGuid()}")).ReadJsonAsync<ErrorResponse>();
            Assert.Equal($"{unseen.Code} | {unseen.Message} | {unseen.Entity}", $"{unknown.Code} | {unknown.Message} | {unknown.Entity}");
        }

        [Fact]
        public async Task Comparison_Anonymous_Should_Return_401()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);
            var target = await scene.AddApplicationAsync("right", FillRight);

            var response = await _portal.CreateAnonymousClient().GetAsync(CompareUrl(source, target));

            await SchemaScene.AssertErrorAsync(response, HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
        }

        [Fact]
        public async Task Comparison_With_Stored_Security_Rules_That_Cannot_Be_Read_Should_Return_409()
        {
            var scene = await SchemaScene.CreateAsync(_portal);
            var source = await scene.AddApplicationAsync("left", FillLeft);
            var broken = await scene.AddApplicationAsync("broken", x =>
            {
                FillRight(x);
                x.Security = "this is not json";
            });

            var message = $"The stored security rules of application '{broken.Name}' cannot be read.";

            var asTarget = await SchemaScene.AssertErrorAsync(await scene.Owner.GetAsync(CompareUrl(source, broken)), HttpStatusCode.Conflict, PortalErrorCode.Conflict);
            var asSource = await SchemaScene.AssertErrorAsync(await scene.Owner.GetAsync(CompareUrl(broken, source)), HttpStatusCode.Conflict, PortalErrorCode.Conflict);

            Assert.Equal(message, asTarget.Message);
            Assert.Equal(message, asSource.Message);
        }

        // ---------- Helpers ----------

        private static string CompareUrl(DBWS_Application source, DBWS_Application target)
        {
            return $"{SchemaScene.AppUrl(source)}/comparison?Target={target.Token}";
        }

        private static string Text(ComparisonFieldChange change)
        {
            return $"{change.Field}: {change.Before} -> {change.After}";
        }

        private static string Text(ComparisonSecurityRule rule)
        {
            return $"{rule.Name} | {rule.Role} | {rule.Type} | {rule.RateLimit} | {rule.Action} | {rule.Record} | {rule.Properties}";
        }

        /// <summary>
        /// The answer of the API is the answer of ApplicationsController.CompareApplications for
        /// the same two applications.
        /// </summary>
        private static async Task AssertEqualsRazorAsync(SchemaScene scene, DBWS_Application source, DBWS_Application target)
        {
            var response = await scene.Owner.GetAsync(CompareUrl(source, target));
            var razorResponse = await scene.Owner.GetAsync($"/Applications/CompareApplications?appTokenSource={source.Token}&appTokenTarget={target.Token}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, razorResponse.StatusCode);

            var api = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var razor = JsonNode.Parse(await razorResponse.Content.ReadAsStringAsync());

            // The one difference: in its Added and Removed lists the Razor answer says 'none' for
            // a rule without a rate limit (and null in Changed); the API says null everywhere.
            foreach (var list in new[] { "Added", "Removed" })
            {
                foreach (var rule in razor?["Security"]?[list]?.AsArray() ?? new JsonArray())
                {
                    if (rule is JsonObject item && item["RateLimit"]?.GetValue<string>() == "none")
                    {
                        item["RateLimit"] = null;
                    }
                }
            }

            Assert.True(JsonNode.DeepEquals(razor, api), $"Razor:{Environment.NewLine}{razor}{Environment.NewLine}API:{Environment.NewLine}{api}");
        }

        private static DBWS_EntityProperty Stored(string name, PropertyType type, Action<DBWS_EntityProperty>? set = null)
        {
            var property = EntityScene.Property(name, type);
            set?.Invoke(property);

            return property;
        }

        /// <summary>
        /// The source of the comparisons.
        /// </summary>
        private static void FillLeft(DBWS_Application application)
        {
            application.Entities.Add(SchemaScene.Users());

            var customers = SchemaScene.Custom(
                "Customers",
                new[]
                {
                    Stored("Name", PropertyType.String, x => x.Maximum = 100),
                    Stored("Phone", PropertyType.String),
                    Stored("Level", PropertyType.Number, x =>
                    {
                        x.Required = true;
                        x.DecimalPlaces = 0;
                        x.Minimum = 1;
                        x.Maximum = 5;
                    }),
                    Stored("Notes", PropertyType.String, x => x.Description = "Old text")
                },
                SchemaScene.Unique("Name"),
                SchemaScene.Unique("Phone"));

            customers.Description = "Clients";
            application.Entities.Add(customers);

            application.Entities.Add(SchemaScene.Custom("Orders", new[] { Stored("Code", PropertyType.String) }, SchemaScene.Unique("Code")));
            application.Entities.Add(SchemaScene.Custom("Invoices", Array.Empty<DBWS_EntityProperty>()));

            application.CustomEndpoints.Add(SchemaScene.Endpoint("Totals", "SELECT 1", "Sums"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("Same", "SELECT 0"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("Described", "SELECT 3", "Old"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("OnlyLeft", "SELECT 4"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("CaseName", "SELECT 5"));

            application.Security = SchemaScene.Security(
                SchemaScene.Rule(SecurityTypes.Entity, "Customers", "ANONYMOUS", "get", properties: "Phone,Name"),
                SchemaScene.Rule(SecurityTypes.Entity, "Customers", "AUTHENTICATED", "put", EndpointRecordAuthorization.Owned, "Name", DBWS_Security.RateLimitItem.New(5, EndpointRateLimit.Per_Second)),
                SchemaScene.Rule(SecurityTypes.Entity, "Customers", "editors", "delete"),
                SchemaScene.Rule(SecurityTypes.CustomEndpoint, "Totals", "ANONYMOUS", "get"),
                SchemaScene.Rule(SecurityTypes.Entity, "Orders", "admin", "get"),
                SchemaScene.Rule(SecurityTypes.Entity, "Gone", "ANONYMOUS", "get"),
                SchemaScene.Rule(SecurityTypes.Schema, "Schema", "AUTHENTICATED", "get"));
        }

        /// <summary>
        /// What <see cref="FillLeft"/> has, with something added, removed and changed of every kind.
        /// </summary>
        private static void FillRight(DBWS_Application application)
        {
            var users = SchemaScene.Users();
            users.Properties.Add(Stored("Nickname", PropertyType.String));
            application.Entities.Add(users);

            var customers = SchemaScene.Custom(
                "Customers",
                new[]
                {
                    Stored("Name", PropertyType.String, x => x.Maximum = 200),
                    Stored("Level", PropertyType.String),
                    Stored("notes", PropertyType.String, x =>
                    {
                        x.Description = "New text";
                        x.Encrypted = true;
                        x.ValidationRegex = "^[a-z]+$";
                    }),
                    Stored("Email", PropertyType.String)
                },
                SchemaScene.Unique("NAME"),
                SchemaScene.Unique("Email"));

            customers.Description = "Clients of the shop";
            customers.RequireChangeTracking = true;
            customers.HasDifferentiationProperty = true;
            application.Entities.Add(customers);

            var shipments = SchemaScene.Custom(
                "Shipments",
                new[]
                {
                    Stored("Customer_ID", PropertyType.Number, x => x.DecimalPlaces = 0),
                    Stored("Address", PropertyType.String, x => { x.Required = true; x.Maximum = 250; })
                },
                SchemaScene.ForeignKey("Customer_ID,Customers,ON_DELETE_CASCADE"),
                SchemaScene.Unique("Address"));

            shipments.Description = "Where things go";
            shipments.RequireChangeTracking = true;
            application.Entities.Add(shipments);

            application.Entities.Add(SchemaScene.Custom("invoices", Array.Empty<DBWS_EntityProperty>()));

            application.CustomEndpoints.Add(SchemaScene.Endpoint("Totals", "SELECT 1 + 1", "Sums"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("Same", "SELECT 0"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("Described", "SELECT 3", "New"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("OnlyRight", "SELECT 2", "Right only"));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("casename", "SELECT 5"));

            application.Security = SchemaScene.Security(
                SchemaScene.Rule(SecurityTypes.Entity, "Customers", "ANONYMOUS", "get", properties: "Phone,Name,Email"),
                SchemaScene.Rule(SecurityTypes.Entity, "Customers", "AUTHENTICATED", "put", EndpointRecordAuthorization.All, "Name", DBWS_Security.RateLimitItem.New(10, EndpointRateLimit.Per_Minute)),
                SchemaScene.Rule(SecurityTypes.Entity, "Customers", "editors", "delete"),
                SchemaScene.Rule(SecurityTypes.CustomEndpoint, "Totals", "ANONYMOUS", "get", rateLimit: DBWS_Security.RateLimitItem.New(3, EndpointRateLimit.Per_Hour)),
                SchemaScene.Rule(SecurityTypes.Entity, "Shipments", "ANONYMOUS", "get", properties: "Address", rateLimit: DBWS_Security.RateLimitItem.New(20, EndpointRateLimit.Per_Hour)),
                SchemaScene.Rule(SecurityTypes.Entity, "Shipments", "AUTHENTICATED", "post", EndpointRecordAuthorization.Owned),
                SchemaScene.Rule(SecurityTypes.Entity, "customers", "ANONYMOUS", "post"),
                SchemaScene.Rule(SecurityTypes.Schema, "Schema", "ANONYMOUS", "get"));
        }
    }
}
