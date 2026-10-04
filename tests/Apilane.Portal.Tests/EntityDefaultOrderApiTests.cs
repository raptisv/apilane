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
    public class EntityDefaultOrderApiTests
    {
        private readonly PortalFactory _portal;

        public EntityDefaultOrderApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Get ----------

        [Fact]
        public async Task Get_Entity_Without_A_Default_Order_Should_Return_No_Items_And_Every_Property()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.GetAsync(scene.DefaultOrderUrl("Orders"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.ReadJsonAsync<DefaultOrderResponse>();
            Assert.Empty(body.Items);

            // Every property, system and encrypted ones included, in the order they were created.
            Assert.Equal(new[] { "ID", "Owner", "Created", "Customer_ID", "Agent_ID", "Amount", "Code", "Secret", "Paid" }, body.Candidates);

            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Get_Should_Return_The_Stored_Order()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await scene.SetStoredAsync("Orders", x => x.EntDefaultOrder = "[{\"Property\":\"Created\",\"Direction\":\"desc\"},{\"Property\":\"Code\",\"Direction\":\"asc\"}]");

            var body = await (await scene.Owner.GetAsync(scene.DefaultOrderUrl("Orders"))).ReadJsonAsync<DefaultOrderResponse>();

            Assert.Equal(new[] { "Created desc", "Code asc" }, body.Items.Select(x => $"{x.Property} {x.Direction}"));
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("[null]")]
        [InlineData("{\"Property\":\"Code\",\"Direction\":\"asc\"}")]
        [InlineData("[{\"Property\":\"Code\"}]")]
        public async Task Get_Stored_Order_That_Cannot_Be_Read_Should_Return_No_Items(string stored)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await scene.SetStoredAsync("Orders", x => x.EntDefaultOrder = stored);

            var response = await scene.Owner.GetAsync(scene.DefaultOrderUrl("Orders"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.ReadJsonAsync<DefaultOrderResponse>()).Items);
        }

        [Fact]
        public async Task Get_Should_Show_The_Stored_Order_As_The_Razor_Page_Does_And_So_That_It_Can_Be_Sent_Back()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            // A property that was deleted, one stored in both directions (the Razor page allows
            // it), a direction in another letter case, and directions that are neither asc nor
            // desc. Anything that is not asc reads as desc, as the Razor page labels it; a null
            // direction goes beyond the Razor page, whose view fails on it.
            await scene.SetStoredAsync("Orders", x => x.EntDefaultOrder =
                "[{\"Property\":\"Gone\",\"Direction\":\"asc\"}," +
                "{\"Property\":\"Code\",\"Direction\":\"desc\"}," +
                "{\"Property\":\"Code\",\"Direction\":\"asc\"}," +
                "{\"Property\":\"Paid\",\"Direction\":\"ASC\"}," +
                "{\"Property\":\"Created\",\"Direction\":\"up\"}," +
                "{\"Property\":\"Amount\",\"Direction\":null}]");

            var body = await (await scene.Owner.GetAsync(scene.DefaultOrderUrl("Orders"))).ReadJsonAsync<DefaultOrderResponse>();

            Assert.Equal(new[] { "Code desc", "Paid asc", "Created desc", "Amount desc" }, body.Items.Select(x => $"{x.Property} {x.Direction}"));

            var response = await scene.Owner.PutAsync(scene.DefaultOrderUrl("Orders"), new { body.Items }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                "[{\"Property\":\"Code\",\"Direction\":\"desc\"},{\"Property\":\"Paid\",\"Direction\":\"asc\"}," +
                "{\"Property\":\"Created\",\"Direction\":\"desc\"},{\"Property\":\"Amount\",\"Direction\":\"desc\"}]",
                (await scene.LoadEntityAsync("Orders")).EntDefaultOrder);
        }

        // ---------- Put ----------

        [Fact]
        public async Task Put_Should_Save_The_Order_And_Reset_The_Cache()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.DefaultOrderUrl("Orders"), Body(("Created", "desc"), ("Code", "asc")).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            Assert.Equal(
                "[{\"Property\":\"Created\",\"Direction\":\"desc\"},{\"Property\":\"Code\",\"Direction\":\"asc\"}]",
                (await scene.LoadEntityAsync("Orders")).EntDefaultOrder);

            // The API server is not asked to do anything: it learns about the change by reloading the application.
            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", request.Url);
            await scene.AssertPortalHeadersAsync(request, scene.OwnerEmail);

            // The answer is what GET answers.
            var body = await response.ReadJsonAsync<DefaultOrderResponse>();
            Assert.Equal(new[] { "Created desc", "Code asc" }, body.Items.Select(x => $"{x.Property} {x.Direction}"));
            Assert.Equal(9, body.Candidates.Count);

            var read = await (await scene.Owner.GetAsync(scene.DefaultOrderUrl("Orders"))).ReadJsonAsync<DefaultOrderResponse>();
            Assert.Equal(JsonSerializer.Serialize(body), JsonSerializer.Serialize(read));

            // One audit row, for the entity; the constraints are untouched.
            var audit = Assert.Single(await scene.AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Modified", audit.Action);
            Assert.Equal(scene.AppId, audit.AppID);
            Assert.Equal(EntityScene.SeedEntities(scene.AppId)[3].EntConstraints, (await scene.LoadEntityAsync("Orders")).EntConstraints);
        }

        [Fact]
        public async Task Put_Empty_List_Should_Clear_The_Order()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();
            await scene.SetStoredAsync("Orders", x => x.EntDefaultOrder = "[{\"Property\":\"Code\",\"Direction\":\"asc\"}]");

            var response = await scene.Owner.PutAsync(scene.DefaultOrderUrl("Orders"), Body().ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.ReadJsonAsync<DefaultOrderResponse>()).Items);

            // What the Razor page stores when nothing is ticked.
            Assert.Equal("[]", (await scene.LoadEntityAsync("Orders")).EntDefaultOrder);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Put_Should_Store_Send_And_Audit_What_The_Razor_Page_Stores_Sends_And_Audits(bool empty)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            // Two equal entities, one saved through the Razor form...
            await scene.AddTwinsAsync("RazorSide", "ApiSide", null, "[{\"Property\":\"ID\",\"Direction\":\"asc\"}]");

            var posted = empty ? "[]" : "[{\"Property\":\"Code\",\"Direction\":\"desc\"},{\"Property\":\"Owner\",\"Direction\":\"asc\"}]";

            var razorResponse = await scene.Owner.PostAsync($"/App/{scene.Token}/Ent/RazorSide/Entity/DefaultOrderSave", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["DefaultOrder"] = posted,
                ["__RequestVerificationToken"] = await _portal.GetAntiforgeryTokenAsync(scene.Owner, $"/App/{scene.Token}/Ent/RazorSide/Entity/DefaultOrder")
            }));

            Assert.Equal(HttpStatusCode.Redirect, razorResponse.StatusCode);

            // The Razor portal resets the cache in the background, after it has answered.
            await scene.WaitForRequestsAsync(FakeApiServer.ClearCachePath, 1);

            var razorRequests = _portal.ApiServer.Requests;
            var razorAudit = await scene.AuditRowsAsync(scene.OwnerEmail);
            _portal.ApiServer.Reset();
            ScriptApiServer();

            // ...and one through the API.
            var body = empty ? Body() : Body(("Code", "desc"), ("Owner", "asc"));
            var apiResponse = await scene.Owner.PutAsync(scene.DefaultOrderUrl("ApiSide"), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);

            var apiRequests = _portal.ApiServer.Requests;
            var apiAudit = (await scene.AuditRowsAsync(scene.OwnerEmail)).Skip(razorAudit.Count).ToList();

            // The same single call, the same stored text and the same audit entry.
            Assert.Single(razorRequests);
            Assert.Equal(razorRequests.Select(x => $"{x.Method} {x.Url}"), apiRequests.Select(x => $"{x.Method} {x.Url}"));
            Assert.Equal(
                razorRequests[0].Headers.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value}"),
                apiRequests[0].Headers.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value}"));

            var razorStored = (await scene.LoadEntityAsync("RazorSide")).EntDefaultOrder;
            Assert.Equal(posted, razorStored);
            Assert.Equal(razorStored, (await scene.LoadEntityAsync("ApiSide")).EntDefaultOrder);

            Assert.Single(razorAudit);
            Assert.Equal(EntityScene.AuditShape(razorAudit, "RazorSide"), EntityScene.AuditShape(apiAudit, "ApiSide"));
        }

        [Fact]
        public async Task Put_System_Entity_Should_Work_For_A_User_Who_Is_Not_An_Administrator()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.DefaultOrderUrl("Users"), Body(("Email", "asc")).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("[{\"Property\":\"Email\",\"Direction\":\"asc\"}]", (await scene.LoadEntityAsync("Users")).EntDefaultOrder);
        }

        // ---------- Rules ----------

        [Theory]
        [InlineData("{\"Direction\":\"asc\"}", "Property", "Required")]
        [InlineData("{\"Property\":null,\"Direction\":\"asc\"}", "Property", "Required")]
        [InlineData("{\"Property\":\"\",\"Direction\":\"asc\"}", "Property", "Required")]
        [InlineData("{\"Property\":\"Nope\",\"Direction\":\"asc\"}", "Property", "Property 'Nope' does not exist")]
        [InlineData("{\"Property\":\"code\",\"Direction\":\"asc\"}", "Property", "Property 'code' does not exist")]
        [InlineData("{\"Property\":\"Code\"}", "Direction", "Required")]
        [InlineData("{\"Property\":\"Code\",\"Direction\":null}", "Direction", "Required")]
        [InlineData("{\"Property\":\"Code\",\"Direction\":\"up\"}", "Direction", "Must be asc or desc")]
        [InlineData("{\"Property\":\"Code\",\"Direction\":\"ASC\"}", "Direction", "Must be asc or desc")]
        [InlineData("{\"Property\":\"Code\",\"Direction\":\"ascending\"}", "Direction", "Must be asc or desc")]
        public async Task Put_Item_That_Breaks_A_Rule_Should_Return_400_And_Change_Nothing(string item, string property, string message)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.DefaultOrderUrl("Orders"), EntityScene.Json($"{{\"Items\":[{item}]}}"));

            await EntityScene.AssertValidationAsync(response, $"Items[0].{property}: {message}");
            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Theory]
        [InlineData("asc")]
        [InlineData("desc")]
        public async Task Put_The_Same_Property_Twice_Should_Return_400_And_Change_Nothing(string secondDirection)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.DefaultOrderUrl("Orders"),
                Body(("Code", "asc"), ("Paid", "desc"), ("Code", secondDirection)).ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "Items[2].Property: A property can be listed only once");
            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Put_Should_Report_Every_Item_That_Breaks_A_Rule()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(
                scene.DefaultOrderUrl("Orders"),
                Body(("Code", "asc"), ("Nope", "down"), ("Paid", "sideways")).ToJsonContent());

            await EntityScene.AssertValidationAsync(
                response,
                "Items[1].Property: Property 'Nope' does not exist",
                "Items[1].Direction: Must be asc or desc",
                "Items[2].Direction: Must be asc or desc");

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Theory]
        [InlineData("{}", "Items: Required")]
        [InlineData("{\"Items\":null}", "Items: Required")]
        [InlineData("{\"Items\":[null]}", "Items[0]: Required")]
        public async Task Put_Without_A_Usable_List_Should_Return_400_And_Change_Nothing(string body, string expected)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.DefaultOrderUrl("Orders"), EntityScene.Json(body));

            await EntityScene.AssertValidationAsync(response, expected);
            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
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

            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(scene.DefaultOrderUrl(name)), "Entity");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.PutAsync(scene.DefaultOrderUrl(name), Body(("ID", "asc")).ToJsonContent()), "Entity");

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Stranger_Admin_And_Unknown_Token_Should_Return_404_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var admin = await _portal.CreateAdminClientAsync();
            ScriptApiServer();

            foreach (var send in AllRequests())
            {
                await EntityScene.AssertNotFoundAsync(await send(scene.Stranger, scene.AppUrl), "Application");
                await EntityScene.AssertNotFoundAsync(await send(admin, scene.AppUrl), "Application");
                await EntityScene.AssertNotFoundAsync(await send(scene.Owner, $"/api/v1/applications/{Guid.NewGuid()}"), "Application");
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

            foreach (var send in AllRequests())
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

            var response = await scene.Owner.PutAsync(scene.DefaultOrderUrl("Orders"), Body(("ID", "asc")).ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);

            Assert.Empty(_portal.ApiServer.Requests);
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Collaborator_Should_Be_Able_To_Read_And_Replace_The_Order()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            Assert.Equal(HttpStatusCode.OK, (await scene.Collaborator.GetAsync(scene.DefaultOrderUrl("Orders"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.Collaborator.PutAsync(scene.DefaultOrderUrl("Orders"), Body(("ID", "desc")).ToJsonContent())).StatusCode);

            // The cache reset was made as the collaborator, and the change is audited under the collaborator's name.
            await scene.AssertPortalHeadersAsync(Assert.Single(_portal.ApiServer.Requests), scene.CollaboratorEmail);
            Assert.Single(await scene.AuditRowsAsync(scene.CollaboratorEmail));
            Assert.Empty(await scene.AuditRowsAsync(scene.OwnerEmail));
        }

        // ---------- Cache reset ----------

        [Fact]
        public async Task Each_Successful_Put_Should_Reset_The_Cache_Once_Even_Without_A_Change()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            ScriptApiServer();

            foreach (var body in new[] { Body(("ID", "desc")), Body(("ID", "desc")), Body() })
            {
                Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PutAsync(scene.DefaultOrderUrl("Orders"), body.ToJsonContent())).StatusCode);
                Assert.Equal(FakeApiServer.ClearCachePath, Assert.Single(_portal.ApiServer.Requests).Path);

                _portal.ApiServer.Reset();
                ScriptApiServer();
            }

            // The second call changed nothing, so it left no audit row.
            Assert.Equal(2, (await scene.AuditRowsAsync(scene.OwnerEmail)).Count);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Put_When_The_Cache_Reset_Fails_Should_Still_Succeed_With_A_Warning_Header(bool unreachable)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            if (unreachable)
            {
                _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);
            }
            else
            {
                _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.BadRequest, EntityScene.ApiError("No."));
            }

            var response = await scene.Owner.PutAsync(scene.DefaultOrderUrl("Orders"), Body(("ID", "desc")).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            EntityScene.AssertWarning(response);
            Assert.Single((await response.ReadJsonAsync<DefaultOrderResponse>()).Items);
            Assert.Equal("[{\"Property\":\"ID\",\"Direction\":\"desc\"}]", (await scene.LoadEntityAsync("Orders")).EntDefaultOrder);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Warning_Header_Of_The_Put()
        {
            var client = await _portal.CreateUserClientAsync();
            var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")) ?? throw new InvalidOperationException("No document.");

            var put = document["paths"]?["/api/v1/applications/{appToken}/entities/{entity}/default-order"]?["put"];

            Assert.NotNull(put?["responses"]?["200"]?["headers"]?["Warning"]);
        }

        // ---------- Helpers ----------

        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        private static Dictionary<string, object?> Body(params (string Property, string Direction)[] items)
        {
            return new Dictionary<string, object?>
            {
                ["Items"] = items.Select(x => new { x.Property, x.Direction }).ToList()
            };
        }

        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> AllRequests()
        {
            return new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, appUrl) => client.GetAsync($"{appUrl}/entities/Orders/default-order"),
                (client, appUrl) => client.PutAsync($"{appUrl}/entities/Orders/default-order", Body(("ID", "asc")).ToJsonContent())
            };
        }
    }
}
