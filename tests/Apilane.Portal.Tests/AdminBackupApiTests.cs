using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class AdminBackupApiTests
    {
        private const string Url = "/api/v1/admin/backup";

        private readonly PortalFactory _portal;

        public AdminBackupApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Fact]
        public async Task Download_Should_Return_The_Database_As_A_File_With_The_Latest_Rows()
        {
            // Written just before the download: a plain file copy could miss it.
            var server = await _portal.CreateServerAsync("backup");
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("Apilane.db", response.Content.Headers.ContentDisposition?.FileName);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var bytes = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(1L, await CountServersAsync(bytes, server.Name));
        }

        [Fact]
        public async Task Download_Should_Add_An_Audit_Row_For_Every_Download()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);

            var audits = await FindAuditAsync(email);

            Assert.Equal(2, audits.Count);
            Assert.All(audits, audit =>
            {
                Assert.Equal("Database backup", audit.EntityType);
                Assert.Equal("Apilane.db", audit.EntityIdentifier);
                Assert.Equal("Downloaded", audit.Action);
                Assert.Null(audit.AppID);
                Assert.Null(audit.Changes);
                Assert.True(audit.Timestamp > DateTime.UtcNow.AddMinutes(-5));
            });
        }

        [Fact]
        public async Task Download_Should_Show_In_The_Instance_Audit_Log()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            await client.GetAsync(Url);

            var log = await (await client.GetAsync("/api/v1/admin/audit-log")).ReadJsonAsync<ListResponse<AuditLogEntryResponse>>();

            var entry = Assert.Single(log.Data, x => x.UserEmail == email);
            Assert.Equal("Downloaded", entry.Action);
            Assert.Equal("Database backup", entry.EntityType);
            Assert.Empty(entry.Changes);
        }

        [Fact]
        public async Task Download_Twice_At_The_Same_Time_Should_Both_Succeed()
        {
            var server = await _portal.CreateServerAsync("backup");
            var client = await _portal.CreateAdminClientAsync();

            var responses = await Task.WhenAll(client.GetAsync(Url), client.GetAsync(Url));

            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(1L, await CountServersAsync(await response.Content.ReadAsByteArrayAsync(), server.Name));
            }
        }

        [Fact]
        public async Task Download_Should_Delete_Its_Temporary_File()
        {
            var client = await _portal.CreateAdminClientAsync();

            // Files of other runs or of a Portal on this machine are not this test's business.
            var before = Directory.GetFiles(Path.GetTempPath(), BackupService.TempFilePrefix + "*");

            var response = await client.GetAsync(Url);
            await response.Content.ReadAsByteArrayAsync();

            // The server closes the file a moment after the last byte has been sent.
            var leftovers = Array.Empty<string>();

            for (var attempt = 0; attempt < 50; attempt++)
            {
                leftovers = Directory.GetFiles(Path.GetTempPath(), BackupService.TempFilePrefix + "*").Except(before).ToArray();

                if (leftovers.Length == 0)
                {
                    break;
                }

                await Task.Delay(100);
            }

            Assert.Empty(leftovers);
        }

        [Fact]
        public async Task Download_Anonymous_Should_Return_401_Json()
        {
            var response = await _portal.CreateAnonymousClient().GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task Download_User_Without_Admin_Role_Should_Return_403_Json_And_No_Audit_Row()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            Assert.Empty(await FindAuditAsync(email));
        }

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Download_As_A_Binary_File_And_Its_Errors_As_Json()
        {
            var client = await _portal.CreateAdminClientAsync();

            using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));

            var responses = document.RootElement
                .GetProperty("paths")
                .GetProperty(Url)
                .GetProperty("get")
                .GetProperty("responses");

            var file = Assert.Single(responses.GetProperty("200").GetProperty("content").EnumerateObject());
            Assert.Equal("application/octet-stream", file.Name);
            Assert.Equal("string", file.Value.GetProperty("schema").GetProperty("type").GetString());
            Assert.Equal("binary", file.Value.GetProperty("schema").GetProperty("format").GetString());

            var forbidden = Assert.Single(responses.GetProperty("403").GetProperty("content").EnumerateObject());
            Assert.Equal("application/json", forbidden.Name);
        }

        // ---------- Helpers ----------

        private Task<List<PortalAuditLog>> FindAuditAsync(string userEmail)
        {
            return _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .Where(x => x.UserEmail == userEmail)
                .ToListAsync());
        }

        /// <summary>
        /// Opens the downloaded bytes as a SQLite database and counts the servers with the name.
        /// </summary>
        private static async Task<long> CountServersAsync(byte[] database, string serverName)
        {
            var path = Path.Combine(Path.GetTempPath(), $"apilane-portal-tests-download-{Guid.NewGuid():N}.db");
            await File.WriteAllBytesAsync(path, database);

            try
            {
                var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Servers WHERE Name = $name";
                command.Parameters.AddWithValue("$name", serverName);

                return (long)(await command.ExecuteScalarAsync() ?? 0L);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
