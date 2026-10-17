using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Data.Repository.Factory;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The values a request writes are read once, and stored as read or refused: a Number was checked with the
    /// server's culture and stored with another parser ('1,000' passed a maximum of 100 and was stored as 1), and a
    /// Boolean that was neither true nor false was stored as false. Users can no longer register the same e-mail or
    /// user name in another letter case, which login (case-insensitive) cannot tell apart. A sort by a property that
    /// does not exist, and a sum or average of anything but a Number, are refused instead of answering something
    /// else than asked. Record IDs above int.MaxValue can be deleted.
    /// </summary>
    public class ValueValidationTests : AppicationTestsBase
    {
        private const string ItemEntity = "ValueItem";
        private const string Password = "password";

        public ValueValidationTests() : base(SuiteContext.Shared)
        {
        }

        private class ValueItem : DataItem
        {
            public string? Title { get; set; }
            public decimal? Price { get; set; }
            public decimal? Quantity { get; set; }
            public bool? InStock { get; set; }
        }

        // ─── Number ───────────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Number_Should_Be_Stored_As_Read_Or_Refused(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await PrepareItemsAsync(dbType, connectionString, useDiffEntity);

            using (GrantItemAccess())
            {
                var mismatches = new List<string>();

                // '.' is the decimal separator whatever the culture of the server. A lone comma that cannot group
                // thousands still reads as the decimal separator.
                var accepted = new (object Value, decimal Expected)[]
                {
                    (12.5m, 12.5m),
                    ("12.5", 12.5m),
                    ("-5.25", -5.25m),
                    ("1e3", 1000m),
                    ("12,5", 12.5m),
                    ("0,1234", 0.12m), // two decimal places: the rest is cut, as before
                };

                foreach (var (value, expected) in accepted)
                {
                    var answer = await PostAsync(new Dictionary<string, object?> { ["Price"] = value });

                    if (answer.Id is null)
                    {
                        mismatches.Add($"{Describe(value)} was refused | {answer.Error}");
                        continue;
                    }

                    var stored = (await GetItemAsync(answer.Id.Value)).Price;
                    if (stored != expected)
                    {
                        mismatches.Add($"{Describe(value)} was stored as {stored}, expected {expected}");
                    }
                }

                // Values that used to be stored as another number ('1,000' as 1, '1,234,567.89' as 0) or that are no number
                foreach (var value in new[] { "1,000", "1,234,567.89", "1.234,5", "12,5,0", "5-", "abc", "" })
                {
                    var answer = await PostAsync(new Dictionary<string, object?> { ["Price"] = value });

                    if (answer.Id is not null)
                    {
                        mismatches.Add($"{Describe(value)} was stored as {(await GetItemAsync(answer.Id.Value)).Price}, expected 'Invalid number'");
                    }
                    else if (answer.Error != $"{ValidationError.VALIDATION} Price Invalid number")
                    {
                        mismatches.Add($"{Describe(value)} answered {answer.Error}, expected 'Invalid number'");
                    }
                }

                Assert.True(mismatches.Count == 0, string.Join(" | ", mismatches));

                // The maximum is checked on the value that is stored: '1,000' passed a maximum of 100, as 1
                var overMaximum = await PostAsync(new Dictionary<string, object?> { ["Quantity"] = "150" });
                Assert.Equal($"{ValidationError.VALIDATION} Quantity Maximum allowed value 100", overMaximum.Error);

                var grouped = await PostAsync(new Dictionary<string, object?> { ["Quantity"] = "1,000" });
                Assert.Equal($"{ValidationError.VALIDATION} Quantity Invalid number", grouped.Error);

                var withinMaximum = await PostAsync(new Dictionary<string, object?> { ["Quantity"] = "100" });
                var withinMaximumId = withinMaximum.Id ?? throw new Exception($"The maximum itself was refused | {withinMaximum.Error}");
                Assert.Equal(100m, (await GetItemAsync(withinMaximumId)).Quantity);
            }
        }

        // ─── Boolean ──────────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Boolean_Should_Be_True_Or_False_Or_Refused(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await PrepareItemsAsync(dbType, connectionString, useDiffEntity);

            using (GrantItemAccess())
            {
                var mismatches = new List<string>();

                var accepted = new (object Value, bool Expected)[]
                {
                    (true, true),
                    (false, false),
                    ("true", true),
                    ("TRUE", true),
                    ("False", false),
                    ("1", true),
                    ("0", false),
                    (1, true),
                    (0, false),
                };

                foreach (var (value, expected) in accepted)
                {
                    var answer = await PostAsync(new Dictionary<string, object?> { ["InStock"] = value });

                    if (answer.Id is null)
                    {
                        mismatches.Add($"{Describe(value)} was refused | {answer.Error}");
                        continue;
                    }

                    var stored = (await GetItemAsync(answer.Id.Value)).InStock;
                    if (stored != expected)
                    {
                        mismatches.Add($"{Describe(value)} was stored as {stored}, expected {expected}");
                    }
                }

                // Every one of these used to be stored as false
                foreach (var value in new object[] { "yes", "abc", 2, "on" })
                {
                    var answer = await PostAsync(new Dictionary<string, object?> { ["InStock"] = value });

                    if (answer.Id is not null)
                    {
                        mismatches.Add($"{Describe(value)} was stored as {(await GetItemAsync(answer.Id.Value)).InStock}, expected 'Invalid boolean'");
                    }
                    else if (answer.Error != $"{ValidationError.VALIDATION} InStock Invalid boolean")
                    {
                        mismatches.Add($"{Describe(value)} answered {answer.Error}, expected 'Invalid boolean'");
                    }
                }

                Assert.True(mismatches.Count == 0, string.Join(" | ", mismatches));
            }
        }

        // ─── Email and Username ───────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Email_And_Username_Should_Be_Unique_In_Any_Letter_Case(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var aliceId = await RegisterAsync("alice@example.test", "alice");

            // The same address or name in another case is the same account to login
            Assert.Equal($"{ValidationError.UNIQUE_CONSTRAINT_VIOLATION} Email", await RegisterRefusedAsync("ALICE@example.test", "alice2"));
            Assert.Equal($"{ValidationError.UNIQUE_CONSTRAINT_VIOLATION} Username", await RegisterRefusedAsync("alice2@example.test", "ALICE"));

            var bobId = await RegisterAsync("bob@example.test", "bobby");

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Users",
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.put,
                properties: new() { "Email", "Username" }))
            {
                // Another user's address or name, in another case, is refused on Data/Put too
                Assert.Equal($"{ValidationError.UNIQUE_CONSTRAINT_VIOLATION} Email", await PutUserAsync(new Dictionary<string, object?> { ["ID"] = bobId, ["Email"] = "Alice@Example.test" }));
                Assert.Equal($"{ValidationError.UNIQUE_CONSTRAINT_VIOLATION} Username", await PutUserAsync(new Dictionary<string, object?> { ["ID"] = bobId, ["Username"] = "Alice" }));

                // A user may change the case of their own
                Assert.Equal("1", await PutUserAsync(new Dictionary<string, object?> { ["ID"] = aliceId, ["Email"] = "Alice@Example.test" }));
            }

            // Login finds the one account, whatever the case
            var login = await ApilaneService.AccountLoginAsync<ApiUser>(AccountLoginRequest.New(new LoginItem() { Email = "ALICE@EXAMPLE.TEST", Password = Password }));
            login.Match(
                success => Assert.Equal(aliceId, success.User.ID),
                error => throw new Exception($"Login failed | {error.Code} | {error.Message}"));
        }

        // ─── Sort and Stats ───────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Unknown_Sort_Property_And_Sum_Of_Not_A_Number_Should_Be_Refused(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await PrepareItemsAsync(dbType, connectionString, useDiffEntity);

            using (GrantItemAccess())
            {
                Assert.NotNull((await PostAsync(new Dictionary<string, object?> { ["Title"] = "a", ["Price"] = 2, ["InStock"] = true })).Id);

                // A sort by a property that does not exist answers like one by a property the caller may not use
                var unknownSort = await ApilaneService.GetDataAsync<ValueItem>(DataGetListRequest.New(ItemEntity)
                    .WithSort(new SortItem() { Property = "Nope", Direction = "asc" }));
                unknownSort.Match(
                    response => throw new Exception("A sort by an unknown property answered records"),
                    error => Assert.Equal(ValidationError.INVALID_SORT_PARAMETER, error.Code));

                // A known property in another case still sorts
                var knownSort = await ApilaneService.GetDataAsync<ValueItem>(DataGetListRequest.New(ItemEntity)
                    .WithSort(new SortItem() { Property = "price", Direction = "asc" }));
                knownSort.Match(
                    response => { Assert.Single(response.Data); },
                    error => throw new Exception($"The sort by a known property failed | {error.Code} | {error.Message}"));

                // The default order saved with the entity can name a property deleted since: it is left out, not refused
                TestApplication.Entities.Single(x => x.Name.Equals(ItemEntity)).EntDefaultOrder = "[{\"Property\":\"Gone\",\"Direction\":\"asc\"}]";
                MockApplicationService(TestApplication);

                var defaultOrder = await ApilaneService.GetDataAsync<ValueItem>(DataGetListRequest.New(ItemEntity));
                defaultOrder.Match(
                    response => { Assert.Single(response.Data); },
                    error => throw new Exception($"The default order failed | {error.Code} | {error.Message}"));

                // A sum or an average of text or a boolean is refused (text answered 0)
                foreach (var (property, aggregate) in new[]
                {
                    (nameof(ValueItem.Title), StatsAggregateRequest.DataAggregates.Sum),
                    (nameof(ValueItem.InStock), StatsAggregateRequest.DataAggregates.Avg)
                })
                {
                    var stats = await ApilaneService.GetStatsAggregateAsync(StatsAggregateRequest.New(ItemEntity).WithProperty(property, aggregate));
                    stats.Match(
                        response => throw new Exception($"{property}.{aggregate} answered {response}"),
                        error =>
                        {
                            Assert.Equal(ValidationError.VALIDATION, error.Code);
                            Assert.Equal(property, error.Property);
                        });
                }

                // Of a Number, and a count of anything, as before
                var sum = await ApilaneService.GetStatsAggregateAsync(StatsAggregateRequest.New(ItemEntity)
                    .WithProperty(nameof(ValueItem.Price), StatsAggregateRequest.DataAggregates.Sum)
                    .WithProperty(nameof(ValueItem.Title), StatsAggregateRequest.DataAggregates.Count));
                sum.Match(
                    response =>
                    {
                        using var document = JsonDocument.Parse(response);
                        Assert.Equal(2m, document.RootElement[0].GetProperty("Price_sum").GetDecimal());
                        Assert.Equal(1, document.RootElement[0].GetProperty("Title_count").GetInt32());
                    },
                    error => throw new Exception($"The sum of a Number failed | {error.Code} | {error.Message}"));

                // An unknown function used to count, and an unknown group-by property answered the general error
                Assert.Equal(nameof(ValidationError.ERROR), await GetErrorCodeAsync($"/api/Stats/Aggregate?appToken={TestApplication.Token}&entity={ItemEntity}&properties=Price.sun"));
                Assert.Equal(nameof(ValidationError.INVALID_GROUPBY_PARAMETER), await GetErrorCodeAsync($"/api/Stats/Aggregate?appToken={TestApplication.Token}&entity={ItemEntity}&properties=Price.sum&groupBy=Titel"));
            }
        }

        // ─── IDs above int.MaxValue ───────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Delete_Should_Take_Ids_Above_Int_MaxValue(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            const long bigId = 3_000_000_001;

            await PrepareItemsAsync(dbType, connectionString, useDiffEntity);

            // A record with an ID past int.MaxValue, as a database that has handed out that many IDs has
            await using (var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath)))
            {
                await store.CreateDataAsync(
                    ItemEntity,
                    new Dictionary<string, object?> { [Globals.PrimaryKeyColumn] = bigId, [Globals.CreatedColumn] = 1L, ["Title"] = "big" },
                    allowInsertIdentity: true);
            }

            using (GrantItemAccess())
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.delete))
            {
                var smallId = (await PostAsync(new Dictionary<string, object?> { ["Title"] = "small" })).Id
                    ?? throw new Exception("The small record was not created");

                // Both are deleted: the big ID used to be dropped, so that only the small one was
                var deleted = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New(ItemEntity, new List<long> { smallId, bigId }));
                deleted.Match(
                    // The small record is created after the big one, so its ID follows it
                    ids => Assert.Equal(new[] { smallId, bigId }.OrderBy(x => x).ToArray(), ids.OrderBy(x => x).ToArray()),
                    error => throw new Exception($"Delete failed | {error.Code} | {error.Message}"));

                var left = await ApilaneService.GetDataAsync<ValueItem>(DataGetListRequest.New(ItemEntity));
                left.Match(
                    response => Assert.Empty(response.Data),
                    error => throw new Exception($"Get failed | {error.Code} | {error.Message}"));
            }

            // Files/Delete reads the IDs the same way: an unknown big ID is nothing to delete, not 'no records found'
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.delete))
            {
                var deletedFiles = await ApilaneService.DeleteFileAsync(FileDeleteRequest.New(new List<long> { bigId }));
                deletedFiles.Match(
                    ids => Assert.Empty(ids),
                    error => throw new Exception($"Files delete failed | {error.Code} | {error.Message}"));
            }
        }

        // ─── Application and calls ────────────────────────────────────────────────

        private async Task PrepareItemsAsync(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);
            await AddEntityAsync(ItemEntity);

            await AddStringPropertyAsync(ItemEntity, nameof(ValueItem.Title), required: false);
            await AddNumberPropertyAsync(ItemEntity, nameof(ValueItem.Price), required: false, decimalPlaces: 2);
            await AddNumberPropertyAsync(ItemEntity, nameof(ValueItem.Quantity), required: false, minimum: 0, maximum: 100, decimalPlaces: 0);
            await AddBooleanPropertyAsync(ItemEntity, nameof(ValueItem.InStock), required: false);
        }

        /// <summary>Anonymous callers may read and create the properties of the item entity.</summary>
        private IDisposable GrantItemAccess()
        {
            var properties = new List<string>() { nameof(ValueItem.Title), nameof(ValueItem.Price), nameof(ValueItem.Quantity), nameof(ValueItem.InStock) };

            return new CompositeDisposable(
                new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                    inRole: Globals.ANONYMOUS, actionType: SecurityActionType.get, properties: properties),
                new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                    inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post, properties: properties));
        }

        /// <summary>The ID of the new record, or the error as 'CODE Property Message'.</summary>
        private async Task<(long? Id, string? Error)> PostAsync(object body)
        {
            var post = await ApilaneService.PostDataAsync(DataPostRequest.New(ItemEntity), body);

            return post.Match<(long? Id, string? Error)>(
                ids => (ids.Single(), null),
                error => (null, $"{error.Code} {error.Property} {error.Message}"));
        }

        private async Task<ValueItem> GetItemAsync(long id)
        {
            var get = await ApilaneService.GetDataByIdAsync<ValueItem>(DataGetByIdRequest.New(ItemEntity, id));

            return get.Match(
                item => item,
                error => throw new Exception($"Get failed | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task<long> RegisterAsync(string email, string username)
        {
            var register = await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem()
            {
                Email = email,
                Username = username,
                Password = Password
            }));

            return register.Match(
                userId => userId,
                error => throw new Exception($"Register failed | {email} | {error.Code} | {error.Message} | {error.Property}"));
        }

        /// <summary>The error of a registration that must fail, as 'CODE Property'.</summary>
        private async Task<string> RegisterRefusedAsync(string email, string username)
        {
            var register = await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem()
            {
                Email = email,
                Username = username,
                Password = Password
            }));

            return register.Match(
                userId => $"registered as user {userId}",
                error => $"{error.Code} {error.Property}");
        }

        /// <summary>The number of records changed, or the error as 'CODE Property'.</summary>
        private async Task<string> PutUserAsync(object body)
        {
            var put = await ApilaneService.PutDataAsync(DataPutRequest.New("Users"), body);

            return put.Match(
                affected => $"{affected}",
                error => $"{error.Code} {error.Property}");
        }

        private async Task<string?> GetErrorCodeAsync(string path)
        {
            using var response = await HttpClient.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("Code", out var code)
                ? code.GetString()
                : $"no error: {body}";
        }

        private static string Describe(object value)
        {
            return value is string text ? $"\"{text}\"" : JsonSerializer.Serialize(value);
        }

        private sealed class CompositeDisposable : IDisposable
        {
            private readonly IDisposable[] _disposables;

            public CompositeDisposable(params IDisposable[] disposables)
            {
                _disposables = disposables;
            }

            public void Dispose()
            {
                foreach (var disposable in _disposables.Reverse())
                {
                    disposable.Dispose();
                }
            }
        }
    }
}
