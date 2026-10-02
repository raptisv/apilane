using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Models.Files;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// A rate limit belongs to an access rule (entity or endpoint + action + role). Every request that
    /// is let in by a rule counts against that rule's limit, whichever endpoint it arrives through:
    /// reads by id, history, statistics, files, the schema, and each operation of a transaction.
    /// </summary>
    public class RateLimitCoverageTests : AppicationTestsBase
    {
        public RateLimitCoverageTests() : base(SuiteContext.Shared)
        {
        }

        private const string EntityName = "Notes";
        private const string FilesEntityName = "Files";
        private const string EndpointName = "ping";
        private const string Password = "password";

        private class Note : DataItem
        {
            public string? Text { get; set; }
        }

        private class NoteHistory
        {
            public string? Text { get; set; }
        }

        private static readonly List<string> _noteProperties = new() { nameof(Note.Text) };
        private static readonly List<string> _fileProperties = new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) };

        // An access rule for anonymous callers (or the given role), with a limit of one request per minute when 'limited'
        private WithSecurityAccess Allow(
            string name,
            SecurityActionType action,
            bool limited,
            List<string>? properties = null,
            SecurityTypes type = SecurityTypes.Entity,
            string role = Globals.ANONYMOUS)
        {
            return new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, name,
                inRole: role,
                type: type,
                actionType: action,
                properties: properties,
                rateLimit: limited ? DBWS_Security.RateLimitItem.New(1, EndpointRateLimit.Per_Minute) : null);
        }

        private void ResetWindow(string name, SecurityActionType action, SecurityTypes type = SecurityTypes.Entity)
        {
            ApplicationRateLimiter.GetOrCreate(TestApplication.Token).Reset(null, EntityAccess.GetRateLimitName(name, type), action.ToString());
        }

        // With a limit of one per minute and a fresh window: the first request passes (so the request
        // itself is valid), the second is refused by the limit.
        private async Task AssertAllowedThenLimitedAsync<T>(
            string what,
            string name,
            SecurityActionType action,
            Func<Task<Either<T, ApilaneError>>> call,
            SecurityTypes type = SecurityTypes.Entity)
        {
            ResetWindow(name, action, type);

            (await call()).Match(
                r => { },
                e => throw new Exception($"{what}: the first request was rejected | {e.Code} | {e.Message}"));

            await AssertLimitedAsync(what, call);
        }

        private static async Task AssertLimitedAsync<T>(string what, Func<Task<Either<T, ApilaneError>>> call)
        {
            (await call()).Match(
                r => throw new Exception($"{what}: the request was not rate limited"),
                e => Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, e.Code));
        }

        private async Task AddNotesAsync(bool requireChangeTracking = false)
        {
            await AddEntityAsync(EntityName, requireChangeTracking: requireChangeTracking);
            await AddStringPropertyAsync(EntityName, nameof(Note.Text), required: false);
        }

        private async Task<long> PostNoteAsync()
        {
            using (Allow(EntityName, SecurityActionType.post, limited: false, _noteProperties))
            {
                return (await ApilaneService.PostDataAsync(DataPostRequest.New(EntityName), new { Text = "a note" }))
                    .Match(r => r.Single(), e => throw new Exception($"Post failed | {e.Code} | {e.Message}"));
            }
        }

        private async Task<int> CountNotesAsync()
        {
            using (Allow(EntityName, SecurityActionType.get, limited: false, _noteProperties))
            {
                return (await ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName)))
                    .Match(r => r.Data.Count, e => throw new Exception($"Get failed | {e.Code} | {e.Message}"));
            }
        }

        [Fact]
        public async Task Reads_By_Id_History_And_Statistics_Count_Against_The_Get_Limit()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            await AddNotesAsync(requireChangeTracking: true);

            var id = await PostNoteAsync();

            using (Allow(EntityName, SecurityActionType.get, limited: true, _noteProperties))
            {
                await AssertAllowedThenLimitedAsync("Data/Get", EntityName, SecurityActionType.get,
                    () => ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName)));

                await AssertAllowedThenLimitedAsync("Data/GetByID", EntityName, SecurityActionType.get,
                    () => ApilaneService.GetDataByIdAsync<Note>(DataGetByIdRequest.New(EntityName, id)));

                await AssertAllowedThenLimitedAsync("Data/GetHistoryByID", EntityName, SecurityActionType.get,
                    () => ApilaneService.GetHistoryByIdAsync<NoteHistory>(DataGetHistoryByIdRequest.New(EntityName, id)));

                await AssertAllowedThenLimitedAsync("Stats/Aggregate", EntityName, SecurityActionType.get,
                    () => ApilaneService.GetStatsAggregateAsync(StatsAggregateRequest.New(EntityName).WithProperty("ID", StatsAggregateRequest.DataAggregates.Count)));

                await AssertAllowedThenLimitedAsync("Stats/Distinct", EntityName, SecurityActionType.get,
                    () => ApilaneService.GetStatsDistinctAsync(StatsDistinctRequest.New(EntityName, nameof(Note.Text))));

                // They are all reads of the same entity: one allowance, not one each
                await AssertLimitedAsync("Data/Get after Stats/Distinct",
                    () => ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName)));
            }
        }

        [Fact]
        public async Task Files_Requests_Count_Against_The_Limits_Of_The_Files_Rules()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            await AddNotesAsync();

            long fileId;
            using (Allow(FilesEntityName, SecurityActionType.post, limited: false, _fileProperties))
            {
                fileId = (await ApilaneService.PostFileAsync(FilePostRequest.New(), new byte[1] { 0 }))
                    .Match(r => r ?? 0, e => throw new Exception($"Upload failed | {e.Code} | {e.Message}"));
            }

            using (Allow(FilesEntityName, SecurityActionType.get, limited: true, _fileProperties))
            {
                await AssertAllowedThenLimitedAsync("Files/Get", FilesEntityName, SecurityActionType.get,
                    () => ApilaneService.GetFilesAsync<FileItem>(FileGetListRequest.New()));

                await AssertAllowedThenLimitedAsync("Files/GetByID", FilesEntityName, SecurityActionType.get,
                    () => ApilaneService.GetFileByIdAsync<FileItem>(FileGetByIdRequest.New(fileId)));

                ResetWindow(FilesEntityName, SecurityActionType.get);

                using (var first = await HttpClient.GetAsync($"/api/Files/Download?fileID={fileId}"))
                {
                    Assert.True(first.IsSuccessStatusCode, $"Files/Download: the first request was rejected | {await first.Content.ReadAsStringAsync()}");
                }

                using (var second = await HttpClient.GetAsync($"/api/Files/Download?fileID={fileId}"))
                {
                    Assert.False(second.IsSuccessStatusCode, "Files/Download: the request was not rate limited");
                    Assert.Contains(ValidationError.RATE_LIMIT_EXCEEDED.ToString(), await second.Content.ReadAsStringAsync());
                }
            }

            using (Allow(FilesEntityName, SecurityActionType.post, limited: true, _fileProperties))
            {
                await AssertAllowedThenLimitedAsync("Files/Post", FilesEntityName, SecurityActionType.post,
                    () => ApilaneService.PostFileAsync(FilePostRequest.New(), new byte[1] { 0 }));
            }

            using (Allow(FilesEntityName, SecurityActionType.delete, limited: true))
            {
                await AssertAllowedThenLimitedAsync("Files/Delete", FilesEntityName, SecurityActionType.delete,
                    () => ApilaneService.DeleteFileAsync(FileDeleteRequest.New(new List<long>() { fileId })));
            }

            // A files request is a files request, whatever it says in an 'entity' parameter: it does not
            // use up the allowance of another entity
            using (Allow(FilesEntityName, SecurityActionType.get, limited: false, _fileProperties))
            using (Allow(EntityName, SecurityActionType.get, limited: true, _noteProperties))
            {
                ResetWindow(EntityName, SecurityActionType.get);

                using (var files = await HttpClient.GetAsync($"/api/Files/Get?entity={EntityName}"))
                {
                    Assert.True(files.IsSuccessStatusCode, $"Files/Get was rejected | {await files.Content.ReadAsStringAsync()}");
                }

                (await ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName))).Match(
                    r => { },
                    e => throw new Exception($"A files request used up the allowance of '{EntityName}' | {e.Code} | {e.Message}"));
            }
        }

        [Fact]
        public async Task Transaction_Operations_Count_Against_The_Limit_Of_Their_Entity()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            await AddNotesAsync();
            AddCustomEndpoint(EndpointName, "SELECT 1");

            Apilane.Net.Models.Data.InTransactionData PostNotes(int count)
            {
                return new Apilane.Net.Models.Data.InTransactionData()
                {
                    Post = Enumerable.Range(0, count)
                        .Select(i => new Apilane.Net.Models.Data.InTransactionData.InTransactionSet() { Entity = EntityName, Data = new { Text = $"note {i}" } })
                        .ToList()
                };
            }

            using (Allow(EntityName, SecurityActionType.post, limited: true, _noteProperties))
            {
                // Data/Transaction
                await AssertAllowedThenLimitedAsync("Data/Transaction", EntityName, SecurityActionType.post,
                    () => ApilaneService.TransactionDataAsync(DataTransactionRequest.New(), PostNotes(1)));

                // ...shares the allowance of a plain post
                await AssertLimitedAsync("Data/Post after Data/Transaction",
                    () => ApilaneService.PostDataAsync(DataPostRequest.New(EntityName), new { Text = "a note" }));

                // Each operation is a request: with one allowed per minute, a transaction with two posts
                // is refused at the second one and writes nothing
                var notesBefore = await CountNotesAsync();
                ResetWindow(EntityName, SecurityActionType.post);

                await AssertLimitedAsync("Data/Transaction with two posts",
                    () => ApilaneService.TransactionDataAsync(DataTransactionRequest.New(), PostNotes(2)));

                Assert.Equal(notesBefore, await CountNotesAsync());

                // Data/TransactionOperations
                await AssertAllowedThenLimitedAsync("Data/TransactionOperations", EntityName, SecurityActionType.post,
                    () => ApilaneService.TransactionOperationsAsync(
                        DataTransactionOperationsRequest.New(),
                        new TransactionBuilder().Post(EntityName, new { Text = "a note" }).Build()));

                ResetWindow(EntityName, SecurityActionType.post);

                await AssertLimitedAsync("Data/TransactionOperations with two posts",
                    () => ApilaneService.TransactionOperationsAsync(
                        DataTransactionOperationsRequest.New(),
                        new TransactionBuilder()
                            .Post(EntityName, new { Text = "a note" })
                            .Post(EntityName, new { Text = "another note" })
                            .Build()));

                Assert.Equal(notesBefore + 1, await CountNotesAsync());
            }

            // A custom endpoint called from a transaction
            using (Allow(EntityName, SecurityActionType.post, limited: false, _noteProperties))
            using (Allow(EndpointName, SecurityActionType.get, limited: true, type: SecurityTypes.CustomEndpoint))
            {
                await AssertAllowedThenLimitedAsync("Custom endpoint in Data/TransactionOperations", EndpointName, SecurityActionType.get,
                    () => ApilaneService.TransactionOperationsAsync(
                        DataTransactionOperationsRequest.New(),
                        new TransactionBuilder().Custom(EndpointName, new { }).Build()),
                    SecurityTypes.CustomEndpoint);

                await AssertLimitedAsync("Custom endpoint after the transaction",
                    () => ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(EndpointName)));

                // When one operation is refused, the operations before it are not written
                var notesBefore = await CountNotesAsync();

                await AssertLimitedAsync("Transaction with a post and a refused endpoint call",
                    () => ApilaneService.TransactionOperationsAsync(
                        DataTransactionOperationsRequest.New(),
                        new TransactionBuilder()
                            .Post(EntityName, new { Text = "not written" })
                            .Custom(EndpointName, new { })
                            .Build()));

                Assert.Equal(notesBefore, await CountNotesAsync());
            }
        }

        [Fact]
        public async Task Entity_And_Custom_Endpoint_With_The_Same_Name_Are_Counted_Separately()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            await AddNotesAsync();

            // Entities and custom endpoints are named independently
            AddCustomEndpoint(EntityName, "SELECT 1");

            using (Allow(EntityName, SecurityActionType.get, limited: true, _noteProperties))
            using (Allow(EntityName, SecurityActionType.get, limited: true, type: SecurityTypes.CustomEndpoint))
            {
                ResetWindow(EntityName, SecurityActionType.get);
                ResetWindow(EntityName, SecurityActionType.get, SecurityTypes.CustomEndpoint);

                // The entity's one request of this minute...
                (await ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName))).Match(
                    r => { },
                    e => throw new Exception($"The entity read was rejected | {e.Code} | {e.Message}"));

                // ...does not use up the endpoint's
                (await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(EntityName))).Match(
                    r => { },
                    e => throw new Exception($"The entity read used up the endpoint's allowance | {e.Code} | {e.Message}"));

                await AssertLimitedAsync("Second entity read",
                    () => ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName)));

                await AssertLimitedAsync("Second endpoint call",
                    () => ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(EntityName)));
            }
        }

        [Fact]
        public async Task Schema_Requests_Count_Against_The_Limit_Of_The_Schema_Rule()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            using (Allow(Globals.SCHEMA, SecurityActionType.get, limited: true, type: SecurityTypes.Schema))
            {
                await AssertAllowedThenLimitedAsync("Data/Schema", Globals.SCHEMA, SecurityActionType.get,
                    () => ApilaneService.GetApplicationSchemaAsync(DataGetSchemaRequest.New()),
                    SecurityTypes.Schema);
            }
        }

        [Fact]
        public async Task Limit_Comes_From_The_Rules_That_Apply_To_The_Caller()
        {
            const string role = "manager";

            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            await AddNotesAsync();

            var userId = (await ApilaneService.AccountRegisterAsync(
                AccountRegisterRequest.New(new RegisterItem { Email = "manager@test.com", Username = "manager@test.com", Password = Password })))
                .Match(r => r, e => throw new Exception($"Register failed | {e.Code} | {e.Message}"));

            await SetUserRolesAsync(userId, new() { role }, DatabaseType.SQLLite);

            var token = (await ApilaneService.AccountLoginAsync<ApiUser>(
                AccountLoginRequest.New(new LoginItem { Email = "manager@test.com", Password = Password })))
                .Match(r => r.AuthToken, e => throw new Exception($"Login failed | {e.Code} | {e.Message}"));

            // Anonymous callers: one request per minute. Managers: no limit.
            using (Allow(EntityName, SecurityActionType.get, limited: true, _noteProperties))
            using (Allow(EntityName, SecurityActionType.get, limited: false, _noteProperties, role: role))
            {
                // The managers' rule does not apply to an anonymous caller, so it does not lift their limit
                await AssertAllowedThenLimitedAsync("Anonymous Data/Get", EntityName, SecurityActionType.get,
                    () => ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName)));

                // A manager qualifies for both rules and gets the more permissive one
                for (var i = 0; i < 3; i++)
                {
                    (await ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName).WithAuthToken(token))).Match(
                        r => { },
                        e => throw new Exception($"The manager was limited | {e.Code} | {e.Message}"));
                }
            }
        }
    }
}
