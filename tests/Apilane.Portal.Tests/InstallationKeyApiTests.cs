using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// The installation key is the secret the Portal shares with every API server. The key stored
    /// in the Portal database, the one an administrator changes under Instance > Settings, is the
    /// single source of truth in both directions: it is what the Portal expects from an API server
    /// (/api/internal) and what it sends to one when it creates an application (Generate: create,
    /// import and clone). The InstallationKey setting the Portal is started with only seeds the
    /// stored key on the first start.
    /// The instance has one settings row, shared by every test class: a test here that changes the
    /// key does it through PUT /api/v1/admin/settings, and the key is put back afterwards.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class InstallationKeyApiTests : IAsyncLifetime
    {
        private const string SettingsUrl = "/api/v1/admin/settings";
        private const string ApplicationsUrl = "/api/v1/applications";
        private const string ImportUrl = "/api/v1/applications/import";
        private const string InstallationKeyHeader = "x-installation-key";

        private readonly PortalFactory _portal;

        // Every key that is in play in the running test: none may show up in an address, a body
        // or a response, and only Generate carries one, in its header.
        private readonly List<string> _keys = new List<string>();

        private string? _originalKey;

        public InstallationKeyApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        public async Task InitializeAsync()
        {
            _originalKey = await _portal.StoredInstallationKeyAsync();

            _keys.Add(_originalKey);
            _keys.Add(ConfiguredKey);
        }

        public async Task DisposeAsync()
        {
            if (_originalKey is null)
            {
                return;
            }

            await _portal.WithDbContextAsync(async db =>
            {
                var stored = await db.GlobalSettings.SingleAsync();

                stored.InstallationKey = _originalKey;

                return await db.SaveChangesAsync();
            });
        }

        // The key the Portal was started with, which is what the first start stored.
        private string ConfiguredKey => _portal.Services.GetRequiredService<PortalConfiguration>().InstallationKey;

        // ---------- Create ----------

        [Fact]
        public async Task Create_Without_A_Change_Should_Send_The_Stored_Key_In_Generate()
        {
            // Nobody changed it: the stored key is the one that was seeded from the setting.
            Assert.Equal(ConfiguredKey, await _portal.StoredInstallationKeyAsync());

            await CreateApplicationAsync();

            Assert.Equal(await _portal.StoredInstallationKeyAsync(), TheGenerateRequest().Headers[InstallationKeyHeader]);
            AssertKeysAreOnlyInTheGenerateHeader();
        }

        [Fact]
        public async Task Create_After_The_Key_Was_Changed_Should_Send_The_New_Stored_Key_In_Generate()
        {
            var newKey = await ChangeKeyAsync();

            await CreateApplicationAsync();

            var sent = TheGenerateRequest().Headers[InstallationKeyHeader];
            Assert.Equal(newKey, sent);
            Assert.NotEqual(ConfiguredKey, sent);
            AssertKeysAreOnlyInTheGenerateHeader();
        }

        [Fact]
        public async Task Create_Before_And_After_Each_Change_Should_Send_The_Key_Stored_At_That_Moment()
        {
            await CreateApplicationAsync();
            Assert.Equal(_originalKey, TheGenerateRequest().Headers[InstallationKeyHeader]);

            var first = await ChangeKeyAsync();
            await CreateApplicationAsync();
            Assert.Equal(first, TheGenerateRequest().Headers[InstallationKeyHeader]);

            var second = await ChangeKeyAsync();
            await CreateApplicationAsync();
            Assert.Equal(second, TheGenerateRequest().Headers[InstallationKeyHeader]);

            AssertKeysAreOnlyInTheGenerateHeader();
        }

        // ---------- Import ----------

        [Fact]
        public async Task Import_Without_A_Change_Should_Send_The_Stored_Key_In_Generate()
        {
            Assert.Equal(ConfiguredKey, await _portal.StoredInstallationKeyAsync());

            await ImportApplicationAsync();

            Assert.Equal(await _portal.StoredInstallationKeyAsync(), TheGenerateRequest().Headers[InstallationKeyHeader]);
            AssertKeysAreOnlyInTheGenerateHeader();
        }

        [Fact]
        public async Task Import_After_The_Key_Was_Changed_Should_Send_The_New_Stored_Key_In_Generate()
        {
            var newKey = await ChangeKeyAsync();

            await ImportApplicationAsync();

            var sent = TheGenerateRequest().Headers[InstallationKeyHeader];
            Assert.Equal(newKey, sent);
            Assert.NotEqual(ConfiguredKey, sent);
            AssertKeysAreOnlyInTheGenerateHeader();
        }

        // ---------- Clone ----------

        [Fact]
        public async Task Clone_Without_A_Change_Should_Send_The_Stored_Key_In_Generate()
        {
            Assert.Equal(ConfiguredKey, await _portal.StoredInstallationKeyAsync());

            await CloneApplicationAsync();

            Assert.Equal(await _portal.StoredInstallationKeyAsync(), TheGenerateRequest().Headers[InstallationKeyHeader]);
            AssertKeysAreOnlyInTheGenerateHeader();
        }

        [Fact]
        public async Task Clone_After_The_Key_Was_Changed_Should_Send_The_New_Stored_Key_In_Generate()
        {
            var newKey = await ChangeKeyAsync();

            await CloneApplicationAsync();

            var sent = TheGenerateRequest().Headers[InstallationKeyHeader];
            Assert.Equal(newKey, sent);
            Assert.NotEqual(ConfiguredKey, sent);
            AssertKeysAreOnlyInTheGenerateHeader();
        }

        [Fact]
        public async Task Clone_Before_And_After_Each_Change_Should_Send_The_Key_Stored_At_That_Moment()
        {
            // The clone service lives as long as the Portal: it must not hold on to the key it read first.
            await CloneApplicationAsync();
            Assert.Equal(_originalKey, TheGenerateRequest().Headers[InstallationKeyHeader]);

            var first = await ChangeKeyAsync();
            await CloneApplicationAsync();
            Assert.Equal(first, TheGenerateRequest().Headers[InstallationKeyHeader]);

            var second = await ChangeKeyAsync();
            await CloneApplicationAsync();
            Assert.Equal(second, TheGenerateRequest().Headers[InstallationKeyHeader]);

            AssertKeysAreOnlyInTheGenerateHeader();
        }

        // ---------- The other direction ----------

        [Fact]
        public async Task Internal_Api_After_The_Key_Was_Changed_Should_Accept_The_New_Key_And_Refuse_The_Old_One()
        {
            var server = await _portal.CreateServerAsync();
            var application = await _portal.CreateApplicationAsync(server.ID);
            var url = $"/api/internal/applications/{application.Token}";

            Assert.Equal(HttpStatusCode.OK, (await GetAsApiServerAsync(url, _originalKey)).StatusCode);

            var newKey = await ChangeKeyAsync();

            // Neither the key it had nor the one the Portal was started with works any more.
            Assert.Equal(HttpStatusCode.OK, (await GetAsApiServerAsync(url, newKey)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsApiServerAsync(url, _originalKey)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsApiServerAsync(url, ConfiguredKey)).StatusCode);
        }

        // ---------- Helpers ----------

        /// <summary>
        /// Changes the key the way an administrator does, under Instance > Settings, and returns
        /// the new one. Everything else in the request is what is stored, so only the key changes.
        /// </summary>
        private async Task<string> ChangeKeyAsync()
        {
            var newKey = $"rotated-{Guid.NewGuid():N}";
            var stored = await _portal.WithDbContextAsync(db => db.GlobalSettings.AsNoTracking().SingleAsync());
            var admin = await _portal.CreateAdminClientAsync();

            var response = await admin.PutAsync(SettingsUrl, new InstanceSettingsRequest
            {
                InstanceTitle = stored.InstanceTitle,
                AllowRegisterToPortal = stored.AllowRegisterToPortal,
                InstallationKey = newKey,
                MailServer = stored.MailServer,
                MailServerPort = stored.MailServerPort,
                MailUserName = stored.MailUserName,
                MailFromAddress = stored.MailFromAddress,
                MailFromDisplayName = stored.MailFromDisplayName
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            _keys.Add(newKey);
            await AssertNoKeyInTheAnswerAsync(response);
            Assert.Equal(newKey, await _portal.StoredInstallationKeyAsync());

            return newKey;
        }

        /// <summary>
        /// POST /applications as a new user, to a server that answers like a healthy one.
        /// </summary>
        private async Task CreateApplicationAsync()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var response = await client.PostAsync(ApplicationsUrl, new Dictionary<string, object?>
            {
                ["Name"] = $"app-{Guid.NewGuid():N}",
                ["ServerID"] = server.ID,
                ["DatabaseType"] = "SQLLite",
                ["ConnectionString"] = null
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await AssertNoKeyInTheAnswerAsync(response);
        }

        /// <summary>
        /// POST /applications/import as a new user, with the file of an exported application.
        /// </summary>
        private async Task ImportApplicationAsync()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Export())));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var form = new MultipartFormDataContent
            {
                { file, "File", "application.json" },
                { new StringContent(server.ID.ToString()), "ServerID" },
                { new StringContent("SQLLite"), "DatabaseType" }
            };

            var response = await client.PostAsync(ImportUrl, form);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await AssertNoKeyInTheAnswerAsync(response);
        }

        /// <summary>
        /// POST /applications/{appToken}/clones as the owner of an application, and waits for the
        /// background routine to end.
        /// </summary>
        private async Task CloneApplicationAsync()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var clonesUrl = $"{ApplicationsUrl}/{scene.Token}/clones";

            var response = await scene.Owner.PostAsync(clonesUrl, new Dictionary<string, object?>
            {
                ["ServerID"] = target.ID,
                ["DatabaseType"] = "SQLLite",
                ["ConnectionString"] = null,
                ["CloneData"] = false,
                ["Entities"] = null
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await AssertNoKeyInTheAnswerAsync(response);

            var started = await response.ReadJsonAsync<CloneStartedResponse>();
            await WaitForEndOfCloneAsync(started.OperationId);

            var operation = await scene.Owner.GetAsync($"{clonesUrl}/{started.OperationId}");

            Assert.Equal(HttpStatusCode.OK, operation.StatusCode);
            await AssertNoKeyInTheAnswerAsync(operation);
        }

        private async Task WaitForEndOfCloneAsync(string operationId)
        {
            var progress = _portal.Services.GetRequiredService<ICloneService>().GetProgress(operationId)
                ?? throw new InvalidOperationException($"No clone operation {operationId}.");

            for (var attempt = 0; attempt < 400 && !IsFinal(progress); attempt++)
            {
                await Task.Delay(25);
            }

            Assert.True(IsFinal(progress), $"The clone did not end; it is {progress.Status}.");
            Assert.True(progress.Status == CloneStatus.Completed, $"The clone ended as {progress.Status}: {progress.ErrorMessage}");
        }

        private static bool IsFinal(CloneProgressInfo progress)
        {
            return progress.Status == CloneStatus.Completed || progress.Status == CloneStatus.Failed;
        }

        /// <summary>
        /// A call as an API server makes it: no cookie, the installation key in a header.
        /// </summary>
        private async Task<HttpResponseMessage> GetAsApiServerAsync(string url, string? installationKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (installationKey is not null)
            {
                request.Headers.Add(Globals.InstallationKeyHeaderName, installationKey);
            }

            return await _portal.CreateCookielessClient().SendAsync(request);
        }

        /// <summary>
        /// Forgets what the fake API server received so far, then makes it answer every call of a
        /// create, an import and a clone the way a healthy one does. What it records afterwards is
        /// the call the test is about to make.
        /// </summary>
        private void ScriptApiServer()
        {
            _portal.ResetApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GetSystemEntitiesPath, HttpStatusCode.OK, JsonSerializer.Serialize(new List<DBWS_Entity> { SystemEntity("Users"), SystemEntity("Files") }));
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.OK, "true");
            _portal.ApiServer.Respond(FakeApiServer.GenerateEntityPath, HttpStatusCode.OK, "true");
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        // Numbered -1, as the API server returns them.
        private static DBWS_Entity SystemEntity(string name)
        {
            return new DBWS_Entity
            {
                ID = -1,
                AppID = -1,
                Name = name,
                IsSystem = true,
                Properties = new List<DBWS_EntityProperty>
                {
                    new DBWS_EntityProperty
                    {
                        ID = -1,
                        EntityID = -1,
                        Name = "ID",
                        IsSystem = true,
                        IsPrimaryKey = true,
                        Required = true,
                        TypeID = (int)PropertyType.Number
                    }
                }
            };
        }

        /// <summary>
        /// An application as an export holds it, with a new token and name.
        /// </summary>
        private static DBWS_Application Export()
        {
            var entity = SystemEntity("Users");
            entity.ID = 10;
            entity.Properties[0].ID = 11;

            return new DBWS_Application
            {
                ID = 0,
                Token = Guid.NewGuid().ToString(),
                Name = $"app-{Guid.NewGuid():N}",
                UserID = string.Empty,
                ServerID = 0,
                Server = new DBWS_Server(),
                EncryptionKey = "KEY12345".Encrypt(Globals.EncryptionKey),
                Online = false,
                AuthTokenExpireMinutes = 45,
                MaxAllowedFileSizeInKB = 2048,
                DatabaseType = 0,
                DifferentiationEntity = string.Empty,
                Security = "[]",
                Collaborates = new List<DBWS_Collaborate>(),
                Entities = new List<DBWS_Entity> { entity },
                CustomEndpoints = new List<DBWS_CustomEndpoint>(),
                Reports = new List<DBWS_ReportPanel>()
            };
        }

        /// <summary>
        /// The one call of an application to an API server that creates the application on it.
        /// </summary>
        private ApiServerRequest TheGenerateRequest()
        {
            return Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GeneratePath));
        }

        /// <summary>
        /// Of everything the Portal sent to the API server only Generate carries a key, in its
        /// header: no key is in an address, in a body or in another header.
        /// </summary>
        private void AssertKeysAreOnlyInTheGenerateHeader()
        {
            var requests = _portal.ApiServer.Requests;

            Assert.NotEmpty(requests);

            foreach (var request in requests)
            {
                var isGenerate = request.Path.Equals(FakeApiServer.GeneratePath, StringComparison.OrdinalIgnoreCase);
                var otherHeaders = request.Headers.Where(x => !x.Key.Equals(InstallationKeyHeader, StringComparison.OrdinalIgnoreCase)).ToList();

                Assert.Equal(isGenerate, request.Headers.ContainsKey(InstallationKeyHeader));

                foreach (var key in _keys)
                {
                    Assert.DoesNotContain(key, request.Url);
                    Assert.DoesNotContain(key, request.Body);
                    Assert.All(otherHeaders, x => Assert.DoesNotContain(key, x.Value));
                }
            }
        }

        /// <summary>
        /// The Portal never answers with a key: not in the body and not in a header (Location).
        /// </summary>
        private async Task AssertNoKeyInTheAnswerAsync(HttpResponseMessage response)
        {
            var headers = string.Join("\n", response.Headers.SelectMany(x => x.Value));
            var body = await response.Content.ReadAsStringAsync();

            foreach (var key in _keys)
            {
                Assert.DoesNotContain(key, headers);
                Assert.DoesNotContain(key, body);
            }
        }
    }
}
