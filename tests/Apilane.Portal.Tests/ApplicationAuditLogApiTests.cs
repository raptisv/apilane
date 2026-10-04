using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// GET /applications/{appToken}/audit-log. Every test works on a new application, so the
    /// application's log holds only the entries the test added (seeding writes none).
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class ApplicationAuditLogApiTests
    {
        private readonly PortalFactory _portal;

        public ApplicationAuditLogApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Fact]
        public async Task List_Should_Return_Only_The_Entries_Of_This_Application()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var server = await _portal.CreateServerAsync();
            var other = await _portal.CreateApplicationAsync(server.ID, scene.OwnerEmail, $"other-{Guid.NewGuid():N}");
            var now = DateTime.UtcNow;

            var mine = await AddAsync("mine-1", now, scene.AppId, entityType: "Entity", action: "Modified",
                changes: "[{\"Property\":\"MailPassword\",\"OldValue\":\"***\",\"NewValue\":\"***\"}]");
            await AddAsync("mine-2", now.AddMilliseconds(1), scene.AppId);
            await AddAsync("other-app", now.AddMilliseconds(2), other.Application.ID);
            await AddAsync("instance", now.AddMilliseconds(3), appId: null);

            var response = await scene.Owner.GetAsync(Url(scene.Token));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var log = await response.ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            Assert.Equal(2, log.Total);
            Assert.Equal(new[] { "mine-2", "mine-1" }, log.Data.Select(x => x.EntityIdentifier));

            // The same contract as the instance audit log.
            var entry = log.Data[1];
            Assert.Equal(mine.ID, entry.ID);
            Assert.Equal(now, entry.Timestamp);
            Assert.Equal(DateTimeKind.Utc, entry.Timestamp.Kind);
            Assert.Equal("seed-user-id", entry.UserId);
            Assert.Equal("seed@portal.test", entry.UserEmail);
            Assert.Equal("Entity", entry.EntityType);
            Assert.Equal("Modified", entry.Action);

            var change = Assert.Single(entry.Changes);
            Assert.Equal("MailPassword", change.Property);
            Assert.Equal("***", change.OldValue);
            Assert.Equal("***", change.NewValue);

            // The other application sees its own entry only.
            var otherLog = await (await scene.Owner.GetAsync(Url(other.Application.Token))).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            Assert.Equal(new[] { "other-app" }, otherLog.Data.Select(x => x.EntityIdentifier));
            Assert.Equal(1, otherLog.Total);
        }

        [Fact]
        public async Task List_Should_Show_The_Entries_The_Razor_Page_Shows()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var server = await _portal.CreateServerAsync();
            var other = await _portal.CreateApplicationAsync(server.ID, scene.OwnerEmail, $"other-{Guid.NewGuid():N}");
            var mine = $"mine-{Guid.NewGuid():N}";
            var notMine = $"not-mine-{Guid.NewGuid():N}";
            var instance = $"instance-{Guid.NewGuid():N}";

            await AddAsync(mine, DateTime.UtcNow, scene.AppId);
            await AddAsync(notMine, DateTime.UtcNow, other.Application.ID);
            await AddAsync(instance, DateTime.UtcNow, appId: null);

            var razor = await (await scene.Owner.GetAsync($"/App/{scene.Token}/Application/AuditLog")).Content.ReadAsStringAsync();
            var api = await (await scene.Owner.GetAsync(Url(scene.Token))).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            Assert.Contains(mine, razor);
            Assert.DoesNotContain(notMine, razor);
            Assert.DoesNotContain(instance, razor);

            Assert.Equal(new[] { mine }, api.Data.Select(x => x.EntityIdentifier));
        }

        [Fact]
        public async Task List_Should_Show_A_Change_Made_Through_The_Api()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");

            var update = await scene.Owner.PutAsync($"{scene.AppUrl}/email-settings", new { MailServer = "smtp.audit.test", MailPassword = "secret-value" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var response = await scene.Collaborator.GetAsync(Url(scene.Token));
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("secret-value", text);

            var log = await response.ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            var entry = Assert.Single(log.Data);
            Assert.Equal("Application", entry.EntityType);
            Assert.Equal("Modified", entry.Action);
            Assert.Equal(scene.OwnerEmail, entry.UserEmail);
            Assert.Equal(
                new[] { "MailPassword:  -> ***", "MailServer:  -> smtp.audit.test" },
                entry.Changes.Select(x => $"{x.Property}: {x.OldValue} -> {x.NewValue}").OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public async Task List_Should_Be_Newest_First_And_Paged()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var now = DateTime.UtcNow;

            // Added out of order, so the order of the IDs is not the order of the timestamps; two share a timestamp.
            await AddAsync("middle", now.AddMilliseconds(2), scene.AppId);
            await AddAsync("newest", now.AddMilliseconds(3), scene.AppId);
            await AddAsync("oldest-a", now.AddMilliseconds(1), scene.AppId);
            await AddAsync("oldest-b", now.AddMilliseconds(1), scene.AppId);

            var firstPage = await (await scene.Owner.GetAsync($"{Url(scene.Token)}?Page=1&PageSize=2")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            var secondPage = await (await scene.Owner.GetAsync($"{Url(scene.Token)}?Page=2&PageSize=2")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            var pastTheEnd = await (await scene.Owner.GetAsync($"{Url(scene.Token)}?Page=2147483647&PageSize=200")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            Assert.Equal(new[] { "newest", "middle" }, firstPage.Data.Select(x => x.EntityIdentifier));
            Assert.Equal(new[] { "oldest-b", "oldest-a" }, secondPage.Data.Select(x => x.EntityIdentifier));
            Assert.Empty(pastTheEnd.Data);
            Assert.All(new[] { firstPage, secondPage, pastTheEnd }, x => Assert.Equal(4, x.Total));
        }

        [Fact]
        public async Task List_Without_Paging_Values_Should_Return_The_First_50()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var now = DateTime.UtcNow;

            await AddAsync("oldest", now, scene.AppId);

            for (var i = 1; i <= PageQuery.DefaultPageSize; i++)
            {
                await AddAsync($"entry-{i}", now.AddMilliseconds(i), scene.AppId);
            }

            var firstPage = await (await scene.Owner.GetAsync(Url(scene.Token))).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            var secondPage = await (await scene.Owner.GetAsync($"{Url(scene.Token)}?Page=2")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            Assert.Equal(50, firstPage.Data.Count);
            Assert.Equal(51, firstPage.Total);
            Assert.DoesNotContain(firstPage.Data, x => x.EntityIdentifier == "oldest");
            Assert.Equal(new[] { "oldest" }, secondPage.Data.Select(x => x.EntityIdentifier));
        }

        [Theory]
        [InlineData("Page=0", "Page", "Must be 1 or more")]
        [InlineData("Page=-1", "Page", "Must be 1 or more")]
        [InlineData("PageSize=0", "PageSize", "Must be between 1 and 200")]
        [InlineData("PageSize=201", "PageSize", "Must be between 1 and 200")]
        public async Task List_Paging_Value_Out_Of_Range_Should_Return_400_With_The_Property(string query, string property, string message)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.GetAsync($"{Url(scene.Token)}?{query}");

            await EntityScene.AssertValidationAsync(response, $"{property}: {message}");
        }

        [Theory]
        [InlineData("Page=abc", "Page")]
        [InlineData("PageSize=1.5", "PageSize")]
        public async Task List_Paging_Value_That_Is_Not_A_Whole_Number_Should_Return_400_With_The_Property(string query, string property)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.GetAsync($"{Url(scene.Token)}?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(property, Assert.Single(error.Errors ?? new List<ErrorDetail>()).Property);
        }

        [Fact]
        public async Task Collaborator_Should_See_The_Log()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await AddAsync("shared", DateTime.UtcNow, scene.AppId);

            var response = await scene.Collaborator.GetAsync(Url(scene.Token));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "shared" }, (await response.ReadJsonAsync<ListResponse<AuditLogEntryResponse>>()).Data.Select(x => x.EntityIdentifier));
        }

        [Fact]
        public async Task Stranger_Administrator_And_Unknown_Token_Should_Get_404()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await AddAsync("hidden", DateTime.UtcNow, scene.AppId);
            var admin = await _portal.CreateAdminClientAsync();

            await EntityScene.AssertNotFoundAsync(await scene.Stranger.GetAsync(Url(scene.Token)), "Application");
            await EntityScene.AssertNotFoundAsync(await admin.GetAsync(Url(scene.Token)), "Application");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(Url(Guid.NewGuid().ToString())), "Application");
        }

        [Fact]
        public async Task Anonymous_Should_Get_401()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await _portal.CreateAnonymousClient().GetAsync(Url(scene.Token));

            await EntityScene.AssertErrorAsync(response, HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
        }

        // ---------- Helpers ----------

        private static string Url(string token)
        {
            return $"/api/v1/applications/{token}/audit-log";
        }

        private async Task<PortalAuditLog> AddAsync(
            string identifier,
            DateTime timestamp,
            long? appId,
            string entityType = "Property",
            string action = "Created",
            string? changes = null)
        {
            var log = new PortalAuditLog
            {
                Timestamp = timestamp,
                UserId = "seed-user-id",
                UserEmail = "seed@portal.test",
                AppID = appId,
                EntityType = entityType,
                EntityIdentifier = identifier,
                Action = action,
                Changes = changes
            };

            await _portal.WithDbContextAsync(db =>
            {
                db.AuditLogs.Add(log);
                return db.SaveChangesAsync();
            });

            return log;
        }
    }
}
