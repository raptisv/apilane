using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// The audit log is shared by every test class, so a test adds its own entries with a unique
    /// identifier and looks only at those. The test classes run one after the other, which makes
    /// the entries a test has just added the newest ones.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class AdminAuditLogApiTests
    {
        private const string Url = "/api/v1/admin/audit-log";

        private readonly PortalFactory _portal;

        public AdminAuditLogApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Should_Return_The_Entry_With_Its_Changes_As_Stored()
        {
            var identifier = NewIdentifier();
            var timestamp = DateTime.UtcNow;

            // Values exactly as ApplicationDbContext stores them: text, a masked secret, JSON text and null.
            var seeded = await AddAsync(identifier, timestamp, action: "Modified", changes:
                "[{\"Property\":\"Name\",\"OldValue\":\"old name\",\"NewValue\":\"new name\"}," +
                "{\"Property\":\"MailPassword\",\"OldValue\":null,\"NewValue\":\"***\"}," +
                "{\"Property\":\"Security\",\"OldValue\":\"[]\",\"NewValue\":\"[{\\\"Name\\\":\\\"Users\\\",\\\"TypeID\\\":0}]\"}]");

            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var log = await response.ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            var entry = Assert.Single(log.Data, x => x.EntityIdentifier == identifier);

            Assert.True(log.Total >= log.Data.Count);
            Assert.Equal(seeded.ID, entry.ID);
            Assert.Equal(DateTimeKind.Utc, entry.Timestamp.Kind);
            Assert.Equal(timestamp, entry.Timestamp);
            Assert.Equal("seed-user-id", entry.UserId);
            Assert.Equal("seed@portal.test", entry.UserEmail);
            Assert.Equal("Server", entry.EntityType);
            Assert.Equal("Modified", entry.Action);

            Assert.Equal(3, entry.Changes.Count);

            Assert.Equal("Name", entry.Changes[0].Property);
            Assert.Equal("old name", entry.Changes[0].OldValue);
            Assert.Equal("new name", entry.Changes[0].NewValue);

            Assert.Equal("MailPassword", entry.Changes[1].Property);
            Assert.Null(entry.Changes[1].OldValue);
            Assert.Equal("***", entry.Changes[1].NewValue);

            // JSON values stay text, so the client can compare the old value with the new one itself.
            Assert.Equal("Security", entry.Changes[2].Property);
            Assert.Equal("[]", entry.Changes[2].OldValue);
            Assert.Equal("[{\"Name\":\"Users\",\"TypeID\":0}]", entry.Changes[2].NewValue);
        }

        [Fact]
        public async Task List_Should_Send_The_Timestamp_As_Utc()
        {
            await AddAsync(NewIdentifier(), DateTime.UtcNow);
            var client = await _portal.CreateAdminClientAsync();

            var body = await (await client.GetAsync(Url)).Content.ReadAsStringAsync();

            // Without the Z a browser would read the value as local time.
            Assert.Matches("\"Timestamp\":\"[0-9T:.-]+Z\"", body);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"Property\":\"Name\"}")]
        [InlineData("[{\"Property\":\"Name\",\"OldValue\":1}]")]
        [InlineData("null")]
        [InlineData("[null]")]
        [InlineData("[{\"Property\":null,\"OldValue\":\"a\",\"NewValue\":\"b\"}]")]
        public async Task List_Changes_That_Are_Missing_Or_Not_Readable_Should_Be_An_Empty_Array(string? changes)
        {
            var identifier = NewIdentifier();
            await AddAsync(identifier, DateTime.UtcNow, changes: changes);

            var client = await _portal.CreateAdminClientAsync();

            var log = await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            var entry = Assert.Single(log.Data, x => x.EntityIdentifier == identifier);
            Assert.Empty(entry.Changes);
        }

        [Fact]
        public async Task List_Should_Be_Newest_First_And_Paged()
        {
            var now = DateTime.UtcNow;
            var oldest = NewIdentifier();
            var middle = NewIdentifier();
            var newest = NewIdentifier();

            // Added out of order, so the order of the IDs is not the order of the timestamps.
            await AddAsync(middle, now.AddMilliseconds(2));
            await AddAsync(newest, now.AddMilliseconds(3));
            await AddAsync(oldest, now.AddMilliseconds(1));

            var client = await _portal.CreateAdminClientAsync();

            var firstPage = await (await client.GetAsync($"{Url}?Page=1&PageSize=2")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            var secondPage = await (await client.GetAsync($"{Url}?Page=2&PageSize=2")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            Assert.Equal(new[] { newest, middle }, firstPage.Data.Select(x => x.EntityIdentifier));
            Assert.Equal(oldest, secondPage.Data[0].EntityIdentifier);
            Assert.True(secondPage.Data.Count <= 2);

            // Total counts the whole log, not the page.
            Assert.True(firstPage.Total >= 3);
            Assert.Equal(firstPage.Total, secondPage.Total);
        }

        [Fact]
        public async Task List_Entries_With_The_Same_Timestamp_Should_Be_In_The_Order_Added_Newest_First()
        {
            var now = DateTime.UtcNow.AddMilliseconds(1);
            var first = NewIdentifier();
            var second = NewIdentifier();

            await AddAsync(first, now);
            await AddAsync(second, now);

            var client = await _portal.CreateAdminClientAsync();

            var log = await (await client.GetAsync($"{Url}?PageSize=2")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            Assert.Equal(new[] { second, first }, log.Data.Select(x => x.EntityIdentifier));
        }

        [Fact]
        public async Task List_Without_Paging_Values_Should_Return_The_First_50()
        {
            var identifier = NewIdentifier();
            var now = DateTime.UtcNow;

            // 51 entries; the oldest of them is the one that does not fit on the first page.
            await AddAsync(identifier, now);

            for (var i = 1; i <= PageQuery.DefaultPageSize; i++)
            {
                await AddAsync(NewIdentifier(), now.AddMilliseconds(i));
            }

            var client = await _portal.CreateAdminClientAsync();

            var firstPage = await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            var secondPage = await (await client.GetAsync($"{Url}?Page=2")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            Assert.Equal(50, firstPage.Data.Count);
            Assert.DoesNotContain(firstPage.Data, x => x.EntityIdentifier == identifier);
            Assert.Equal(identifier, secondPage.Data[0].EntityIdentifier);
        }

        [Fact]
        public async Task List_Should_Leave_Out_The_Entries_Of_Applications()
        {
            var instanceEntry = NewIdentifier();
            var applicationEntry = NewIdentifier();
            var now = DateTime.UtcNow;

            var client = await _portal.CreateAdminClientAsync();

            // Total moves only by the entries added here: the test classes run one after the other
            // and a read writes no audit entry.
            var before = await ReadTotalAsync(client);

            await AddAsync(instanceEntry, now);
            var afterInstance = await ReadTotalAsync(client);
            Assert.Equal(before + 1, afterInstance);

            await AddAsync(applicationEntry, now.AddMilliseconds(1), appId: 123456);

            var log = await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            Assert.Equal(afterInstance, log.Total);
            Assert.Contains(log.Data, x => x.EntityIdentifier == instanceEntry);
            Assert.DoesNotContain(log.Data, x => x.EntityIdentifier == applicationEntry);
        }

        [Theory]
        [InlineData("Page=1000000&PageSize=200")]
        // (Page - 1) * PageSize does not fit in an int.
        [InlineData("Page=2147483647&PageSize=200")]
        public async Task List_Page_Past_The_End_Should_Return_No_Entries_And_The_Total(string query)
        {
            await AddAsync(NewIdentifier(), DateTime.UtcNow);
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync($"{Url}?{query}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var log = await response.ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();
            Assert.Empty(log.Data);
            Assert.True(log.Total >= 1);
        }

        [Fact]
        public async Task List_Largest_Page_Size_Should_Be_Accepted()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync($"{Url}?Page=1&PageSize=200");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("Page=0", "Page", "Must be 1 or more")]
        [InlineData("Page=-1", "Page", "Must be 1 or more")]
        [InlineData("PageSize=0", "PageSize", "Must be between 1 and 200")]
        [InlineData("PageSize=-5", "PageSize", "Must be between 1 and 200")]
        [InlineData("PageSize=201", "PageSize", "Must be between 1 and 200")]
        public async Task List_Paging_Value_Out_Of_Range_Should_Return_400_With_The_Property(string query, string property, string message)
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync($"{Url}?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal(property, detail.Property);
            Assert.Equal(message, detail.Message);
        }

        [Theory]
        [InlineData("Page=abc", "Page")]
        [InlineData("PageSize=1.5", "PageSize")]
        [InlineData("Page=99999999999", "Page")]
        public async Task List_Paging_Value_That_Is_Not_A_Whole_Number_Should_Return_400_With_The_Property(string query, string property)
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync($"{Url}?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(property, Assert.Single(error.Errors ?? new List<ErrorDetail>()).Property);
        }

        [Fact]
        public async Task List_Should_Show_A_Change_Made_Through_The_Api()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);
            var name = $"server-{Guid.NewGuid():N}";

            await client.PostAsync("/api/v1/admin/servers", new ServerRequest { Name = name, ServerUrl = "https://one.example.test" }.ToJsonContent());

            var log = await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            var entry = Assert.Single(log.Data, x => x.EntityIdentifier == name);
            Assert.Equal("Server", entry.EntityType);
            Assert.Equal("Created", entry.Action);
            Assert.Equal(email, entry.UserEmail);

            var change = Assert.Single(entry.Changes, x => x.Property == "ServerUrl");
            Assert.Null(change.OldValue);
            Assert.Equal("https://one.example.test", change.NewValue);
        }

        [Fact]
        public async Task List_Anonymous_Should_Return_401()
        {
            var response = await _portal.CreateAnonymousClient().GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task List_User_Without_Admin_Role_Should_Return_403()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        // ---------- Helpers ----------

        private static string NewIdentifier()
        {
            return $"audit-{Guid.NewGuid():N}";
        }

        private static async Task<long> ReadTotalAsync(HttpClient client)
        {
            return (await (await client.GetAsync($"{Url}?PageSize=1")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>()).Total;
        }

        private async Task<PortalAuditLog> AddAsync(
            string identifier,
            DateTime timestamp,
            string action = "Created",
            string? changes = null,
            long? appId = null)
        {
            var log = new PortalAuditLog
            {
                Timestamp = timestamp,
                UserId = "seed-user-id",
                UserEmail = "seed@portal.test",
                AppID = appId,
                EntityType = "Server",
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
