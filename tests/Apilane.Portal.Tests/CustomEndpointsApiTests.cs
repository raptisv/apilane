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
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class CustomEndpointsApiTests
    {
        private const string NoParametersQuery = "select 1";
        private const string OneParameterQuery = "select * from Orders where ID = {OrderId} and Owner = {Owner}";

        // Repeated, out of order, a lower-case 'owner' (a normal parameter), one with an underscore
        // and one with a digit (not a parameter: it stays in the SQL as typed).
        private const string SeveralParametersQuery = "select {Beta}, {Alpha}, {Beta}, {owner}, {With_Underscore}, {Product1} from Items where Owner = {Owner}";

        private readonly PortalFactory _portal;

        public CustomEndpointsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Should_Return_The_Endpoints_Of_The_Application_In_The_Order_They_Were_Created()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.GetAsync(scene.EndpointsUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var list = await response.ReadJsonAsync<ListResponse<CustomEndpointResponse>>();

            // Not by name: 'Zeta' was created first. The endpoint of the owner's other application is not listed.
            Assert.Equal(new[] { "Zeta", "Alpha", "Multi" }, list.Data.Select(x => x.Name));
            Assert.Equal(new[] { scene.ZetaId, scene.AlphaId, scene.MultiId }, list.Data.Select(x => x.ID));
            Assert.Equal(3, list.Total);

            var zeta = list.Data[0];
            Assert.Equal("The first one", zeta.Description);
            Assert.Equal(NoParametersQuery, zeta.Query);
            Assert.Null(list.Data[1].Description);

            // Reading does not involve the API server.
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task List_Of_An_Application_Without_Endpoints_Should_Be_Empty()
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var application = await _portal.CreateApplicationAsync(server.ID, ownerEmail, "no-endpoints");
            var owner = await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword);

            var list = await (await owner.GetAsync($"/api/v1/applications/{application.Application.Token}/custom-endpoints"))
                .ReadJsonAsync<ListResponse<CustomEndpointResponse>>();

            Assert.Empty(list.Data);
            Assert.Equal(0, list.Total);
        }

        [Fact]
        public async Task List_Should_Give_Parameters_Url_And_CallUrl_Of_The_Model_Methods()
        {
            var scene = await CreateSceneAsync();

            var list = (await (await scene.Owner.GetAsync(scene.EndpointsUrl)).ReadJsonAsync<ListResponse<CustomEndpointResponse>>()).Data;

            Assert.Equal(3, list.Count);
            Assert.All(list, x => AssertComputedLikeTheModel(x, scene));

            // And what those methods give, written out.
            var zeta = Assert.Single(list, x => x.Name == "Zeta");
            Assert.Empty(zeta.Parameters);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Zeta", zeta.Url);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Zeta?appToken={scene.Token}", zeta.CallUrl);

            var alpha = Assert.Single(list, x => x.Name == "Alpha");
            Assert.Equal(new[] { "OrderId" }, alpha.Parameters);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Alpha?OrderId={{OrderId}}", alpha.Url);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Alpha?appToken={scene.Token}&OrderId={{OrderId}}", alpha.CallUrl);

            var multi = Assert.Single(list, x => x.Name == "Multi");
            Assert.Equal(new[] { "Beta", "Alpha", "owner", "With_Underscore" }, multi.Parameters);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Multi?Beta={{Beta}}&Alpha={{Alpha}}&owner={{owner}}&With_Underscore={{With_Underscore}}", multi.Url);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Multi?appToken={scene.Token}&Beta={{Beta}}&Alpha={{Alpha}}&owner={{owner}}&With_Underscore={{With_Underscore}}", multi.CallUrl);
        }

        [Fact]
        public async Task Urls_Should_Not_Double_The_Slash_Of_A_Server_Url_That_Ends_In_One()
        {
            var scene = await CreateSceneAsync();

            await _portal.WithDbContextAsync(async db =>
            {
                var server = await db.Servers.SingleAsync(x => x.ServerUrl == scene.ServerUrl);
                server.ServerUrl += "/";
                return await db.SaveChangesAsync();
            });

            var zeta = await (await scene.Owner.GetAsync(scene.EndpointUrl(scene.ZetaId))).ReadJsonAsync<CustomEndpointResponse>();

            Assert.Equal($"{scene.ServerUrl}/api/Custom/Zeta", zeta.Url);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Zeta?appToken={scene.Token}", zeta.CallUrl);
        }

        // ---------- Get ----------

        [Fact]
        public async Task Get_Should_Return_The_Endpoint()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.GetAsync(scene.EndpointUrl(scene.AlphaId));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var alpha = await response.ReadJsonAsync<CustomEndpointResponse>();
            var stored = await LoadEndpointAsync(scene.AlphaId);

            Assert.Equal(scene.AlphaId, alpha.ID);
            Assert.Equal("Alpha", alpha.Name);
            Assert.Null(alpha.Description);
            Assert.Equal(OneParameterQuery, alpha.Query);
            Assert.Equal(stored.DateModified, alpha.DateModified);
            AssertComputedLikeTheModel(alpha, scene);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task An_Id_Of_Another_Application_Or_Unknown_Should_Return_404_For_Get_Put_And_Delete()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            foreach (var id in new[] { scene.OtherApplicationEndpointId, 987654321L })
            {
                await AssertNotFoundAsync(await scene.Owner.GetAsync(scene.EndpointUrl(id)), "CustomEndpoint");
                await AssertNotFoundAsync(await scene.Owner.PutAsync(scene.EndpointUrl(id), Body("Renamed", "select 2").ToJsonContent()), "CustomEndpoint");
                await AssertNotFoundAsync(await scene.Owner.DeleteAsync(scene.EndpointUrl(id)), "CustomEndpoint");
            }

            Assert.Equal("Other", (await LoadEndpointAsync(scene.OtherApplicationEndpointId)).Name);
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        public async Task An_Id_That_Is_Not_A_Number_Should_Return_The_Api_404(string method)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // 'Alpha' is the name of an endpoint of the scene: a by-name route would find it.
            var request = new HttpRequestMessage(new HttpMethod(method), $"{scene.EndpointsUrl}/Alpha");

            if (method == "PUT")
            {
                request.Content = Body("Renamed", "select 2").ToJsonContent();
            }

            var response = await scene.Owner.SendAsync(request);

            // No route matches, so the API fallback answers: NOT_FOUND without an Entity.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Null(error.Entity);

            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        // ---------- The appToken parameter ----------

        [Fact]
        public async Task A_Query_With_The_AppToken_Parameter_Should_Be_Created_Listed_Read_And_Previewed()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            const string query = "select {appToken}";
            var url = $"{scene.ServerUrl}/api/Custom/Tokens?appToken={{appToken}}";
            var callUrl = $"{scene.ServerUrl}/api/Custom/Tokens?appToken={scene.Token}&appToken={{appToken}}";

            var response = await scene.Owner.PostAsync(scene.EndpointsUrl, Body("Tokens", query).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<CustomEndpointResponse>();
            Assert.Equal(new[] { "appToken" }, created.Parameters);
            Assert.Equal(url, created.Url);
            Assert.Equal(callUrl, created.CallUrl);

            var listResponse = await scene.Owner.GetAsync(scene.EndpointsUrl);
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var list = await listResponse.ReadJsonAsync<ListResponse<CustomEndpointResponse>>();
            Assert.Equal(new[] { "Zeta", "Alpha", "Multi", "Tokens" }, list.Data.Select(x => x.Name));
            Assert.Equal(callUrl, list.Data[3].CallUrl);

            var readResponse = await scene.Owner.GetAsync(scene.EndpointUrl(created.ID));
            Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
            Assert.Equal(Comparable(created), Comparable(await readResponse.ReadJsonAsync<CustomEndpointResponse>()));

            var updateResponse = await scene.Owner.PutAsync(scene.EndpointUrl(created.ID), Body("Tokens", query, "Changed").ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            Assert.Equal(callUrl, (await updateResponse.ReadJsonAsync<CustomEndpointResponse>()).CallUrl);

            var previewResponse = await scene.Owner.PostAsync($"{scene.EndpointsUrl}/preview", new { Name = "Tokens", Query = query }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);

            var preview = await previewResponse.ReadJsonAsync<CustomEndpointPreviewResponse>();
            Assert.Equal(new[] { "appToken" }, preview.Parameters);
            Assert.Equal(url, preview.Url);
            Assert.Equal(callUrl, preview.CallUrl);
        }

        // ---------- Create ----------

        [Fact]
        public async Task Create_Should_Trim_The_Name_Store_The_Endpoint_And_Return_201()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var before = DateTime.UtcNow;

            var response = await scene.Owner.PostAsync(scene.EndpointsUrl, Body("  Shipments ", "select * from Shipments where ID = {ShipmentId}\n", "Parcels").ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<CustomEndpointResponse>();
            var stored = await LoadEndpointAsync(created.ID);

            Assert.Equal("Shipments", created.Name);
            Assert.Equal("Parcels", created.Description);
            // The query is stored as sent.
            Assert.Equal("select * from Shipments where ID = {ShipmentId}\n", created.Query);
            Assert.Equal(new[] { "ShipmentId" }, created.Parameters);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Shipments?ShipmentId={{ShipmentId}}", created.Url);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Shipments?appToken={scene.Token}&ShipmentId={{ShipmentId}}", created.CallUrl);
            Assert.InRange(created.DateModified.ToUniversalTime(), before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));

            Assert.Equal(scene.AppId, stored.AppID);
            Assert.Equal("Shipments", stored.Name);
            Assert.Equal("Parcels", stored.Description);
            Assert.Equal(created.Query, stored.Query);
            Assert.Equal(created.DateModified, stored.DateModified);

            Assert.Equal($"/api/v1/applications/{scene.Token}/custom-endpoints/{created.ID}", response.Headers.Location?.OriginalString);

            // GET answers the same.
            var read = await (await scene.Owner.GetAsync(scene.EndpointUrl(created.ID))).ReadJsonAsync<CustomEndpointResponse>();
            Assert.Equal(Comparable(created), Comparable(read));
        }

        [Fact]
        public async Task Create_With_An_Empty_Description_Should_Store_None()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var created = await (await scene.Owner.PostAsync(scene.EndpointsUrl, Body("Shipments", "select 1", "   ").ToJsonContent()))
                .ReadJsonAsync<CustomEndpointResponse>();

            Assert.Null(created.Description);
            Assert.Null((await LoadEndpointAsync(created.ID)).Description);
        }

        [Fact]
        public async Task Create_With_A_Name_Of_80_Letters_Should_Succeed()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var name = new string('a', 80);

            var response = await scene.Owner.PostAsync(scene.EndpointsUrl, Body(name, "select 1").ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(name, (await response.ReadJsonAsync<CustomEndpointResponse>()).Name);
        }

        [Theory]
        [InlineData("Alpha")]
        [InlineData("alpha")]
        [InlineData(" ALPHA ")]
        public async Task Create_With_The_Name_Of_Another_Endpoint_In_Any_Letter_Case_Should_Return_409(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.EndpointsUrl, Body(name, "select 2").ToJsonContent());

            await AssertConflictAsync(response, $"Custom endpoint '{name.Trim()}' already exists");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Create_With_The_Name_Of_An_Endpoint_Of_Another_Application_Should_Succeed()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.EndpointsUrl, Body("Other", "select 2").ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Theory]
        [InlineData("Ship ments", CustomEndpointNameRules.PatternMessage)]
        [InlineData("Ship_ments", CustomEndpointNameRules.PatternMessage)]
        [InlineData("Shipments1", CustomEndpointNameRules.PatternMessage)]
        [InlineData("Ünicode", CustomEndpointNameRules.PatternMessage)]
        [InlineData("", "Required")]
        [InlineData("   ", "Required")]
        [InlineData(null, "Required")]
        public async Task Create_With_A_Name_That_Is_Not_Letters_Only_Should_Return_400(string? name, string message)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.EndpointsUrl, Body(name, "select 1").ToJsonContent());

            await AssertValidationAsync(response, "Name", message);
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Fact]
        public async Task Create_With_A_Name_Longer_Than_80_Should_Return_400_With_The_Rule()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.EndpointsUrl, Body(new string('a', 81), "select 1").ToJsonContent());

            await AssertValidationAsync(response, "Name", CustomEndpointNameRules.PatternMessage);
            await AssertNothingChangedAsync(scene);
        }

        [Theory]
        [InlineData("")]
        [InlineData("  \n ")]
        [InlineData(null)]
        public async Task Create_Without_A_Query_Should_Return_400(string? query)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.EndpointsUrl, Body("Shipments", query).ToJsonContent());

            await AssertValidationAsync(response, "Query", "Required");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        // ---------- Update ----------

        [Fact]
        public async Task Update_Should_Change_Name_Description_And_Query_And_Keep_The_Date()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var before = await LoadEndpointAsync(scene.AlphaId);
            await SetSecurityAsync(scene, AlphaRule);

            var response = await scene.Owner.PutAsync(scene.EndpointUrl(scene.AlphaId), Body(" Orders ", "select {A}, {B}", "All orders").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var updated = await response.ReadJsonAsync<CustomEndpointResponse>();
            var stored = await LoadEndpointAsync(scene.AlphaId);

            Assert.Equal(scene.AlphaId, updated.ID);
            Assert.Equal("Orders", updated.Name);
            Assert.Equal("All orders", updated.Description);
            Assert.Equal("select {A}, {B}", updated.Query);
            Assert.Equal(new[] { "A", "B" }, updated.Parameters);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/Orders?A={{A}}&B={{B}}", updated.Url);
            AssertComputedLikeTheModel(updated, scene);

            Assert.Equal("Orders", stored.Name);
            Assert.Equal("All orders", stored.Description);
            Assert.Equal("select {A}, {B}", stored.Query);
            Assert.Equal(scene.AppId, stored.AppID);
            Assert.Equal(before.DateModified, stored.DateModified);
            Assert.Equal(before.DateModified, updated.DateModified);

            var read = await (await scene.Owner.GetAsync(scene.EndpointUrl(scene.AlphaId))).ReadJsonAsync<CustomEndpointResponse>();
            Assert.Equal(Comparable(updated), Comparable(read));

            // The rules of the old name are kept.
            Assert.Equal(AlphaRule, await LoadSecurityAsync(scene));
        }

        [Fact]
        public async Task Update_With_An_Empty_Description_Should_Remove_It()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var updated = await (await scene.Owner.PutAsync(scene.EndpointUrl(scene.ZetaId), Body("Zeta", NoParametersQuery, "").ToJsonContent()))
                .ReadJsonAsync<CustomEndpointResponse>();

            Assert.Null(updated.Description);
            Assert.Null((await LoadEndpointAsync(scene.ZetaId)).Description);
        }

        [Theory]
        [InlineData("Alpha")]
        [InlineData("ALPHA")]
        public async Task Update_Keeping_Its_Own_Name_In_Any_Letter_Case_Should_Succeed(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.EndpointUrl(scene.AlphaId), Body(name, "select 3").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(name, (await LoadEndpointAsync(scene.AlphaId)).Name);
        }

        [Theory]
        [InlineData("Zeta")]
        [InlineData("zeta")]
        [InlineData(" MULTI ")]
        public async Task Update_To_The_Name_Of_Another_Endpoint_In_Any_Letter_Case_Should_Return_409(string name)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.EndpointUrl(scene.AlphaId), Body(name, "select 2").ToJsonContent());

            await AssertConflictAsync(response, $"Custom endpoint '{name.Trim()}' already exists");
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        [Theory]
        [InlineData("Or ders", "select 1", "Name")]
        [InlineData("Orders2", "select 1", "Name")]
        [InlineData(null, "select 1", "Name")]
        [InlineData("Orders", "", "Query")]
        [InlineData("Orders", null, "Query")]
        public async Task Update_With_Invalid_Values_Should_Return_400(string? name, string? query, string property)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.EndpointUrl(scene.AlphaId), Body(name, query).ToJsonContent());

            await AssertValidationAsync(response, property);
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_Should_Remove_The_Endpoint_And_Return_204()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetSecurityAsync(scene, AlphaRule);

            var response = await scene.Owner.DeleteAsync(scene.EndpointUrl(scene.AlphaId));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.CustomEndpoints.AnyAsync(x => x.ID == scene.AlphaId)));

            // The rules written for its name are kept.
            Assert.Equal(AlphaRule, await LoadSecurityAsync(scene));

            await AssertNotFoundAsync(await scene.Owner.GetAsync(scene.EndpointUrl(scene.AlphaId)), "CustomEndpoint");

            var list = await (await scene.Owner.GetAsync(scene.EndpointsUrl)).ReadJsonAsync<ListResponse<CustomEndpointResponse>>();
            Assert.Equal(new[] { "Zeta", "Multi" }, list.Data.Select(x => x.Name));

            // A second delete finds nothing.
            await AssertNotFoundAsync(await scene.Owner.DeleteAsync(scene.EndpointUrl(scene.AlphaId)), "CustomEndpoint");
        }

        // ---------- Preview ----------

        [Theory]
        [InlineData(NoParametersQuery)]
        [InlineData(OneParameterQuery)]
        [InlineData(SeveralParametersQuery)]
        public async Task Preview_Should_Give_What_The_Model_Methods_Give_And_What_A_Save_Stores(string query)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync($"{scene.EndpointsUrl}/preview", new { Name = " Shipments ", Query = query }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var preview = await response.ReadJsonAsync<CustomEndpointPreviewResponse>();
            var model = new DBWS_CustomEndpoint { Name = "Shipments", Query = query };

            Assert.Equal(model.GetParameters(), preview.Parameters);
            Assert.Equal(model.GetUrl(scene.ServerUrl, scene.Token, false), preview.Url);
            Assert.Equal(model.GetUrl(scene.ServerUrl, scene.Token, true), preview.CallUrl);

            // Nothing was saved and the API server was not called.
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);

            // A save of the same name and query stores exactly that.
            var created = await (await scene.Owner.PostAsync(scene.EndpointsUrl, Body(" Shipments ", query).ToJsonContent()))
                .ReadJsonAsync<CustomEndpointResponse>();

            Assert.Equal(preview.Parameters, created.Parameters);
            Assert.Equal(preview.Url, created.Url);
            Assert.Equal(preview.CallUrl, created.CallUrl);
        }

        [Fact]
        public async Task Preview_Should_Check_Nothing()
        {
            var scene = await CreateSceneAsync();

            var empty = await (await scene.Owner.PostAsync($"{scene.EndpointsUrl}/preview", new { Name = (string?)null, Query = (string?)null }.ToJsonContent()))
                .ReadJsonAsync<CustomEndpointPreviewResponse>();

            Assert.Empty(empty.Parameters);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/", empty.Url);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/?appToken={scene.Token}", empty.CallUrl);

            // A name that is not valid is previewed too.
            var invalid = await scene.Owner.PostAsync($"{scene.EndpointsUrl}/preview", new { Name = "alpha 1", Query = "select {X}" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/alpha 1?X={{X}}", (await invalid.ReadJsonAsync<CustomEndpointPreviewResponse>()).Url);

            // And a name that is taken ('Alpha' exists).
            var taken = await scene.Owner.PostAsync($"{scene.EndpointsUrl}/preview", new { Name = "alpha", Query = "select {X}" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
            Assert.Equal($"{scene.ServerUrl}/api/Custom/alpha?X={{X}}", (await taken.ReadJsonAsync<CustomEndpointPreviewResponse>()).Url);

            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
        }

        // ---------- Audit ----------

        [Fact]
        public async Task Writes_Should_Write_One_Audit_Row_Per_Change()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var created = await (await scene.Owner.PostAsync(scene.EndpointsUrl, Body("Shipments", "select 1", "Parcels").ToJsonContent()))
                .ReadJsonAsync<CustomEndpointResponse>();

            // Only Name and Query change: Description is sent as it is stored.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PutAsync(scene.EndpointUrl(created.ID), Body("Deliveries", "select 2", "Parcels").ToJsonContent())).StatusCode);

            // Nothing changes: the write succeeds and writes no row.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PutAsync(scene.EndpointUrl(created.ID), Body("Deliveries", "select 2", "Parcels").ToJsonContent())).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.EndpointUrl(created.ID))).StatusCode);

            var rows = await AuditRowsAsync(scene.OwnerEmail);

            Assert.Equal(
                new[] { "Created | Shipments", "Modified | Deliveries", "Deleted | Deliveries" },
                rows.Select(x => $"{x.Action} | {x.EntityIdentifier}"));
            Assert.All(rows, x => Assert.Equal("Custom Endpoint", x.EntityType));
            Assert.All(rows, x => Assert.Equal(scene.AppId, x.AppID));

            Assert.Equal(new[] { "AppID", "DateModified", "Description", "Name", "Query" }, ChangedProperties(rows[0]).OrderBy(x => x));

            var modified = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(rows[1].Changes ?? "[]") ?? new List<Dictionary<string, string?>>();
            Assert.Equal(
                new[] { "Name: Shipments -> Deliveries", "Query: select 1 -> select 2" },
                modified.Select(x => $"{x["Property"]}: {x["OldValue"]} -> {x["NewValue"]}").OrderBy(x => x));

            Assert.Contains("Name", ChangedProperties(rows[2]));
        }

        // ---------- Cache reset ----------

        [Fact]
        public async Task Each_Successful_Write_Should_Reset_The_Cache_Once_As_Its_Only_Call()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var created = await (await scene.Owner.PostAsync(scene.EndpointsUrl, Body("Shipments", "select 1").ToJsonContent()))
                .ReadJsonAsync<CustomEndpointResponse>();
            await AssertOneCacheResetAsync(scene, scene.OwnerEmail);

            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PutAsync(scene.EndpointUrl(created.ID), Body("Deliveries", "select 2").ToJsonContent())).StatusCode);
            await AssertOneCacheResetAsync(scene, scene.OwnerEmail);

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.EndpointUrl(created.ID))).StatusCode);
            await AssertOneCacheResetAsync(scene, scene.OwnerEmail);

            // Reads and previews do not reset it.
            await scene.Owner.GetAsync(scene.EndpointsUrl);
            await scene.Owner.GetAsync(scene.EndpointUrl(scene.AlphaId));
            await scene.Owner.PostAsync($"{scene.EndpointsUrl}/preview", new { Name = "X", Query = "select 1" }.ToJsonContent());
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Write_When_The_Cache_Reset_Fails_Should_Still_Succeed_With_A_Warning_Header(bool unreachable)
        {
            var scene = await CreateSceneAsync();

            if (unreachable)
            {
                _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);
            }
            else
            {
                _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { Code = "ERROR", Message = "No." }));
            }

            var created = await scene.Owner.PostAsync(scene.EndpointsUrl, Body("Shipments", "select 1").ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            AssertWarning(created);
            var id = (await created.ReadJsonAsync<CustomEndpointResponse>()).ID;

            var updated = await scene.Owner.PutAsync(scene.EndpointUrl(id), Body("Deliveries", "select 2").ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            AssertWarning(updated);
            Assert.Equal("Deliveries", (await LoadEndpointAsync(id)).Name);

            var deleted = await scene.Owner.DeleteAsync(scene.EndpointUrl(id));
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            AssertWarning(deleted);
            Assert.False(await _portal.WithDbContextAsync(db => db.CustomEndpoints.AnyAsync(x => x.ID == id)));

            // One reset per write, tried once.
            Assert.Equal(3, _portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath).Count);
        }

        // ---------- Access ----------

        [Fact]
        public async Task Collaborator_Should_Be_Able_To_Do_Everything_The_Owner_Can()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var client = scene.Collaborator;

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(scene.EndpointsUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(scene.EndpointUrl(scene.AlphaId))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{scene.EndpointsUrl}/preview", new { Name = "X", Query = "select 1" }.ToJsonContent())).StatusCode);

            var created = await client.PostAsync(scene.EndpointsUrl, Body("Shipments", "select 1").ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.ReadJsonAsync<CustomEndpointResponse>()).ID;

            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(scene.EndpointUrl(id), Body("Deliveries", "select 2").ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(scene.EndpointUrl(id))).StatusCode);

            // Every cache reset was made as the collaborator.
            var bearer = $"Bearer {await StoredTokenAsync(scene.CollaboratorEmail)}";
            Assert.Equal(3, _portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath).Count);
            Assert.All(_portal.ApiServer.Requests, x => Assert.Equal(bearer, x.Headers["Authorization"]));

            // And audited under the collaborator's name.
            Assert.Equal(
                new[] { "Created", "Modified", "Deleted" },
                (await AuditRowsAsync(scene.CollaboratorEmail)).Select(x => x.Action));
        }

        [Fact]
        public async Task Stranger_Admin_And_Unknown_Token_Should_Get_404_For_Every_Endpoint()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            var admin = await _portal.CreateAdminClientAsync();

            foreach (var send in AllRequests(scene))
            {
                await AssertNotFoundAsync(await send(scene.Stranger, scene.Token), "Application");
                await AssertNotFoundAsync(await send(admin, scene.Token), "Application");
                await AssertNotFoundAsync(await send(scene.Owner, Guid.NewGuid().ToString()), "Application");
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

            foreach (var send in AllRequests(scene))
            {
                var response = await send(anonymous, scene.Token);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
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

            foreach (var send in WriteRequests(scene))
            {
                var response = await send(scene.Owner, scene.Token);

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Forbidden, error.Code);
                Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
            }

            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNothingChangedAsync(scene);
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
            long AppId,
            long ZetaId,
            long AlphaId,
            long MultiId,
            long OtherApplicationEndpointId)
        {
            public string EndpointsUrl => $"/api/v1/applications/{Token}/custom-endpoints";

            public string EndpointUrl(long id) => $"{EndpointsUrl}/{id}";
        }

        /// <summary>
        /// Three new users and an application of the owner, shared with the collaborator, with three
        /// custom endpoints created out of the order of their names: 'Zeta' (no parameters),
        /// 'Alpha' (one) and 'Multi' (several). A second application of the owner has 'Other'.
        /// </summary>
        private async Task<Scene> CreateSceneAsync()
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await _portal.CreateUserAsync();

            var server = await _portal.CreateServerAsync();
            var seeded = await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"endpoints-{Guid.NewGuid():N}", collaboratorEmail);
            var other = await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"other-{Guid.NewGuid():N}");
            var appId = seeded.Application.ID;

            var zeta = new DBWS_CustomEndpoint { AppID = appId, Name = "Zeta", Description = "The first one", Query = NoParametersQuery, DateModified = DateTime.UtcNow.AddDays(-3) };
            var alpha = new DBWS_CustomEndpoint { AppID = appId, Name = "Alpha", Query = OneParameterQuery, DateModified = DateTime.UtcNow.AddDays(-2) };
            var multi = new DBWS_CustomEndpoint { AppID = appId, Name = "Multi", Query = SeveralParametersQuery, DateModified = DateTime.UtcNow.AddDays(-1) };
            var otherEndpoint = new DBWS_CustomEndpoint { AppID = other.Application.ID, Name = "Other", Query = "select 9", DateModified = DateTime.UtcNow };

            // One at a time, so the IDs follow this order.
            foreach (var endpoint in new[] { zeta, alpha, multi, otherEndpoint })
            {
                await _portal.WithDbContextAsync(db =>
                {
                    db.CustomEndpoints.Add(endpoint);
                    return db.SaveChangesAsync();
                });
            }

            return new Scene(
                ownerEmail,
                await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword),
                collaboratorEmail,
                await _portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword),
                await _portal.CreateSignedInClientAsync(strangerEmail, strangerPassword),
                server.ServerUrl,
                seeded.Application.Token,
                appId,
                zeta.ID,
                alpha.ID,
                multi.ID,
                otherEndpoint.ID);
        }

        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        private static Dictionary<string, object?> Body(string? name, string? query, string? description = null)
        {
            return new Dictionary<string, object?>
            {
                ["Name"] = name,
                ["Description"] = description,
                ["Query"] = query
            };
        }

        /// <summary>
        /// One request to each endpoint of this controller, valid for the owner of the scene.
        /// </summary>
        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> AllRequests(Scene scene)
        {
            var requests = new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, token) => client.GetAsync($"/api/v1/applications/{token}/custom-endpoints"),
                (client, token) => client.GetAsync($"/api/v1/applications/{token}/custom-endpoints/{scene.AlphaId}")
            };

            requests.AddRange(WriteRequests(scene));

            return requests;
        }

        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> WriteRequests(Scene scene)
        {
            return new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, token) => client.PostAsync($"/api/v1/applications/{token}/custom-endpoints", Body("Shipments", "select 1").ToJsonContent()),
                (client, token) => client.PutAsync($"/api/v1/applications/{token}/custom-endpoints/{scene.AlphaId}", Body("Renamed", "select 2").ToJsonContent()),
                (client, token) => client.DeleteAsync($"/api/v1/applications/{token}/custom-endpoints/{scene.AlphaId}"),
                (client, token) => client.PostAsync($"/api/v1/applications/{token}/custom-endpoints/preview", new { Name = "X", Query = "select {A}" }.ToJsonContent())
            };
        }

        private static void AssertComputedLikeTheModel(CustomEndpointResponse response, Scene scene)
        {
            var model = new DBWS_CustomEndpoint { Name = response.Name, Query = response.Query };

            Assert.Equal(model.GetParameters(), response.Parameters);
            Assert.Equal(model.GetUrl(scene.ServerUrl, scene.Token, false), response.Url);
            Assert.Equal(model.GetUrl(scene.ServerUrl, scene.Token, true), response.CallUrl);
        }

        private static string Comparable(CustomEndpointResponse response)
        {
            return $"{response.ID} | {response.Name} | {response.Description} | {response.Query} | {string.Join(",", response.Parameters)} | {response.Url} | {response.CallUrl} | {response.DateModified.Ticks}";
        }

        // A stored rule for the 'Alpha' endpoint; nothing on these paths parses it.
        private const string AlphaRule = "[{\"Name\":\"Alpha\",\"TypeID\":1,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null}]";

        private async Task SetSecurityAsync(Scene scene, string security)
        {
            await _portal.WithDbContextAsync(async db =>
            {
                var application = await db.Applications.SingleAsync(x => x.ID == scene.AppId);
                application.Security = security;
                return await db.SaveChangesAsync();
            });
        }

        private async Task<string?> LoadSecurityAsync(Scene scene)
        {
            return await _portal.WithDbContextAsync(db => db.Applications
                .AsNoTracking()
                .Where(x => x.ID == scene.AppId)
                .Select(x => x.Security)
                .SingleAsync());
        }

        private async Task<DBWS_CustomEndpoint> LoadEndpointAsync(long id)
        {
            return await _portal.WithDbContextAsync(db => db.CustomEndpoints.AsNoTracking().SingleAsync(x => x.ID == id));
        }

        /// <summary>
        /// The three endpoints are as the scene seeded them and nobody caused an audit row.
        /// </summary>
        private async Task AssertNothingChangedAsync(Scene scene)
        {
            var stored = await _portal.WithDbContextAsync(db => db.CustomEndpoints
                .AsNoTracking()
                .Where(x => x.AppID == scene.AppId)
                .OrderBy(x => x.ID)
                .ToListAsync());

            Assert.Equal(
                new[]
                {
                    $"{scene.ZetaId} | Zeta | The first one | {NoParametersQuery}",
                    $"{scene.AlphaId} | Alpha |  | {OneParameterQuery}",
                    $"{scene.MultiId} | Multi |  | {SeveralParametersQuery}"
                },
                stored.Select(x => $"{x.ID} | {x.Name} | {x.Description} | {x.Query}"));

            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Empty(await AuditRowsAsync(scene.CollaboratorEmail));
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.AppID == scene.AppId)));
        }

        private async Task AssertOneCacheResetAsync(Scene scene, string callerEmail)
        {
            var request = Assert.Single(_portal.ApiServer.Requests);

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", request.Url);
            Assert.Equal(scene.Token, request.Headers["x-application-token"]);
            Assert.Equal($"Bearer {await StoredTokenAsync(callerEmail)}", request.Headers["Authorization"]);

            _portal.ApiServer.Reset();
            ScriptApiServer();
        }

        private static void AssertWarning(HttpResponseMessage response)
        {
            Assert.Equal(ApiServerCacheReset.WarningText, Assert.Single(response.Headers.GetValues(ApiServerCacheReset.WarningHeaderName)));
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

        private static List<string> ChangedProperties(PortalAuditLog row)
        {
            var changes = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(row.Changes ?? "[]") ?? new List<Dictionary<string, string?>>();

            return changes.Select(x => x["Property"] ?? string.Empty).ToList();
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
            Assert.Equal("CustomEndpoint", error.Entity);
        }

        private static async Task AssertValidationAsync(HttpResponseMessage response, string property, string? message = null)
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
    }
}
