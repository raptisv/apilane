using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// Property names are matched without regard to case everywhere in the API (filter, sort, properties, Stats).
    /// Data/Put and Account/Update picked the properties to write that way, but read their values from the body
    /// with an exact comparison: a body like {"ID":5,"price":9} for the property Price answered success and set
    /// Price to NULL. Data/Post, Account/Register and the transactions read the body the same way. A property
    /// written in another case is written with its value, and two spellings of one property in a body are
    /// refused (naming the property) rather than one of the values being dropped.
    /// </summary>
    public class PropertyNameCasingTests : AppicationTestsBase
    {
        private const string ItemEntity = "CasingItem";

        public PropertyNameCasingTests() : base(SuiteContext.Shared)
        {
        }

        private class CasingItem : DataItem
        {
            public string? Title { get; set; }
            public decimal? Price { get; set; }
        }

        private class UserItem : IApiUser
        {
            public long ID { get; set; }
            public long Created { get; set; }
            public string Email { get; set; } = null!;
            public string Username { get; set; } = null!;
            public string Password { get; set; } = null!;
            public bool EmailConfirmed { get; set; }
            public long? LastLogin { get; set; }
            public string Roles { get; set; } = null!;
            public string? Nickname { get; set; }
            public int? Age { get; set; }

            public bool IsInRole(string role)
            {
                return !string.IsNullOrWhiteSpace(Roles) && Roles.Split(',').Contains(role);
            }
        }

        // The register item of the SDK has no custom properties: the names are the ones of the body
        private class ProfileRegisterItem : IRegisterItem
        {
            public string Email { get; set; } = null!;
            public string Username { get; set; } = null!;
            public string Password { get; set; } = null!;
            public string Nickname { get; set; } = null!;
            public int Age { get; set; }
        }

        private class LowerCaseRegisterItem : IRegisterItem
        {
            public string Email { get; set; } = null!;
            public string Username { get; set; } = null!;
            public string Password { get; set; } = null!;
            public string nickname { get; set; } = null!;
            public int AGE { get; set; }
        }

        // ─── Data/Put ─────────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Put_PropertyNameInExactCase_Should_Update_The_Property(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                var id = await PostItemAsync(new { Title = "t", Price = 1 });

                await PutAsync(new Dictionary<string, object?> { ["ID"] = id, ["Price"] = 9 });

                var item = await GetItemAsync(id);
                Assert.Equal(9m, item.Price);
                Assert.Equal("t", item.Title);
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Put_PropertyNameInOtherCase_Should_Update_The_Property_And_Never_Clear_It(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                // The property named in another case gets the value of the body, the other one is left alone.
                // Every case starts from a record of its own, so that one failure does not hide the next.
                var mismatches = new List<string>();
                var cases = new (string Name, object Value, string Expected)[]
                {
                    ("price", 9, "Price=9 Title=t"),
                    ("PRICE", 10, "Price=10 Title=t"),
                    ("pRiCe", 11, "Price=11 Title=t"),
                    ("title", "t2", "Price=1 Title=t2"),
                    ("TITLE", "t3", "Price=1 Title=t3"),
                };

                foreach (var (name, value, expected) in cases)
                {
                    var id = await PostItemAsync(new { Title = "t", Price = 1 });

                    var put = await ApilaneService.PutDataAsync(
                        DataPutRequest.New(ItemEntity),
                        new Dictionary<string, object?> { ["ID"] = id, [name] = value });

                    var affected = put.Match(r => $"{r}", e => $"error {e.Code} {e.Message} {e.Property}");
                    var item = await GetItemAsync(id);
                    var actual = $"Price={item.Price} Title={item.Title}";

                    if (affected != "1" || actual != expected)
                    {
                        mismatches.Add($"{{\"ID\":{id},\"{name}\":{value}}} answered {affected}, record is [{actual}], expected [{expected}]");
                    }
                }

                Assert.True(mismatches.Count == 0, string.Join(" | ", mismatches));
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Put_IdInOtherCase_Should_Update_The_Record(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                var id = await PostItemAsync(new { Title = "t", Price = 1 });

                await PutAsync(new Dictionary<string, object?> { ["id"] = id, ["Price"] = 9 });

                var item = await GetItemAsync(id);
                Assert.Equal(9m, item.Price);
                Assert.Equal("t", item.Title);
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Put_TwoSpellingsOfOneProperty_Should_Be_Rejected_And_Change_Nothing(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                var id = await PostItemAsync(new { Title = "t", Price = 1 });

                var put = await ApilaneService.PutDataAsync(
                    DataPutRequest.New(ItemEntity),
                    new Dictionary<string, object?> { ["ID"] = id, ["Price"] = 8, ["price"] = 9, ["Title"] = "changed" });

                put.Match(
                    affected => throw new Exception($"A body with two spellings of Price should be refused, {affected} record(s) were updated"),
                    error =>
                    {
                        Assert.Equal(ValidationError.VALIDATION, error.Code);
                        Assert.Equal("Price", error.Property);
                        return 0;
                    });

                // Nothing was written, not even the property that is not ambiguous
                var item = await GetItemAsync(id);
                Assert.Equal(1m, item.Price);
                Assert.Equal("t", item.Title);
            }
        }

        // ─── Data/Post ────────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Post_PropertyNameInOtherCase_Should_Store_The_Value(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                var id = await PostItemAsync(new Dictionary<string, object?> { ["title"] = "lower", ["PRICE"] = 9 });

                var item = await GetItemAsync(id);
                Assert.Equal("lower", item.Title);
                Assert.Equal(9m, item.Price);
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Post_TwoSpellingsOfOneProperty_Should_Be_Rejected_And_Create_Nothing(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                var post = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(ItemEntity),
                    new Dictionary<string, object?> { ["Title"] = "a", ["title"] = "b", ["Price"] = 1 });

                post.Match(
                    ids => throw new Exception($"A body with two spellings of Title should be refused, record(s) {string.Join(",", ids)} were created"),
                    error =>
                    {
                        Assert.Equal(ValidationError.VALIDATION, error.Code);
                        Assert.Equal("Title", error.Property);
                        return 0;
                    });

                var all = await ApilaneService.GetDataTotalAsync<CasingItem>(DataGetListRequest.New(ItemEntity));
                Assert.Equal(0, all.Match(r => r.Total, e => throw new Exception($"Get failed | {e.Code} | {e.Message}")));
            }
        }

        // ─── Transactions ─────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Transaction_PropertyNamesInOtherCase_Should_Store_The_Values(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                var existingId = await PostItemAsync(new { Title = "t", Price = 1 });

                var result = await ApilaneService.TransactionDataAsync(DataTransactionRequest.New(), new InTransactionData()
                {
                    Post = new List<InTransactionData.InTransactionSet>()
                    {
                        new() { Entity = ItemEntity, Data = new Dictionary<string, object?> { ["title"] = "posted", ["PRICE"] = 3 } }
                    },
                    Put = new List<InTransactionData.InTransactionSet>()
                    {
                        new() { Entity = ItemEntity, Data = new Dictionary<string, object?> { ["ID"] = existingId, ["price"] = 9 } }
                    }
                });

                var response = result.Match(r => r, e => throw new Exception($"Transaction failed | {e.Code} | {e.Message} | {e.Property}"));

                var posted = await GetItemAsync(response.Post.Single());
                Assert.Equal("posted", posted.Title);
                Assert.Equal(3m, posted.Price);

                var updated = await GetItemAsync(existingId);
                Assert.Equal(9m, updated.Price);
                Assert.Equal("t", updated.Title);
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Transaction_TwoSpellingsOfOneProperty_Should_Be_Rejected_And_Roll_Back_The_Whole_Transaction(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                var existingId = await PostItemAsync(new { Title = "t", Price = 1 });

                // The put is refused after the post of the same transaction was done
                var result = await ApilaneService.TransactionDataAsync(DataTransactionRequest.New(), new InTransactionData()
                {
                    Post = new List<InTransactionData.InTransactionSet>()
                    {
                        new() { Entity = ItemEntity, Data = new Dictionary<string, object?> { ["Title"] = "posted", ["Price"] = 3 } }
                    },
                    Put = new List<InTransactionData.InTransactionSet>()
                    {
                        new() { Entity = ItemEntity, Data = new Dictionary<string, object?> { ["ID"] = existingId, ["Price"] = 8, ["price"] = 9 } }
                    }
                });

                result.Match(
                    response => throw new Exception($"A body with two spellings of Price should refuse the transaction, it created {string.Join(",", response.Post)}"),
                    error =>
                    {
                        Assert.Equal(ValidationError.VALIDATION, error.Code);
                        Assert.Equal("Price", error.Property);
                        return 0;
                    });

                var all = await ApilaneService.GetDataTotalAsync<CasingItem>(DataGetListRequest.New(ItemEntity));
                Assert.Equal(1, all.Match(r => r.Total, e => throw new Exception($"Get failed | {e.Code} | {e.Message}")));

                var unchanged = await GetItemAsync(existingId);
                Assert.Equal(1m, unchanged.Price);
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task TransactionOperations_PropertyNamesInOtherCase_Should_Store_The_Values(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantItemAccess())
            {
                var transaction = new TransactionBuilder()
                    .Post(ItemEntity, new Dictionary<string, object?> { ["title"] = "posted", ["PRICE"] = 3 }, out var postedRef)
                    .Put(ItemEntity, new Dictionary<string, object?> { ["ID"] = postedRef.Id(), ["price"] = 8 })
                    .Build();

                var result = await ApilaneService.TransactionOperationsAsync(DataTransactionOperationsRequest.New(), transaction);
                var response = result.Match(r => r, e => throw new Exception($"TransactionOperations failed | {e.Code} | {e.Message} | {e.Property}"));

                var posted = await GetItemAsync(response.Results[0].Created!.Single());
                Assert.Equal("posted", posted.Title);
                Assert.Equal(8m, posted.Price);
            }
        }

        // ─── Account/Update and Account/Register ──────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task AccountUpdate_PropertyNameInOtherCase_Should_Update_The_Property_And_Never_Clear_It(DatabaseType dbType)
        {
            await PrepareUsersAsync(dbType);

            var authToken = await RegisterAndLoginAsync(new ProfileRegisterItem()
            {
                Email = "test@test.com",
                Username = "test",
                Password = "password",
                Nickname = "first",
                Age = 30
            });

            // Every case starts from the same profile, so that one failure does not hide the next
            var mismatches = new List<string>();
            var cases = new (string Name, object Value, string Expected)[]
            {
                ("nickname", "second", "Nickname=second Age=30"),
                ("NICKNAME", "third", "Nickname=third Age=30"),
                ("age", 31, "Nickname=first Age=31"),
                ("AGE", 32, "Nickname=first Age=32"),
            };

            foreach (var (name, value, expected) in cases)
            {
                var reset = await ApilaneService.AccountUpdateAsync<UserItem>(
                    AccountUpdateRequest.New().WithAuthToken(authToken),
                    new Dictionary<string, object?> { ["Nickname"] = "first", ["Age"] = 30 });
                reset.Match(user => Assert.Equal("first", user.Nickname), error => throw new Exception($"Update failed | {error.Code} | {error.Message}"));

                var update = await ApilaneService.AccountUpdateAsync<UserItem>(
                    AccountUpdateRequest.New().WithAuthToken(authToken),
                    new Dictionary<string, object?> { [name] = value });

                var actual = update.Match(
                    user => $"Nickname={user.Nickname} Age={user.Age}",
                    error => $"error {error.Code} {error.Message} {error.Property}");

                if (actual != expected)
                {
                    mismatches.Add($"{{\"{name}\":{value}}} left the user as [{actual}], expected [{expected}]");
                }
            }

            Assert.True(mismatches.Count == 0, string.Join(" | ", mismatches));
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task AccountUpdate_TwoSpellingsOfOneProperty_Should_Be_Rejected_And_Change_Nothing(DatabaseType dbType)
        {
            await PrepareUsersAsync(dbType);

            var authToken = await RegisterAndLoginAsync(new ProfileRegisterItem()
            {
                Email = "test@test.com",
                Username = "test",
                Password = "password",
                Nickname = "first",
                Age = 30
            });

            var update = await ApilaneService.AccountUpdateAsync<UserItem>(
                AccountUpdateRequest.New().WithAuthToken(authToken),
                new Dictionary<string, object?> { ["Nickname"] = "a", ["nickname"] = "b", ["Age"] = 40 });

            update.Match(
                user => throw new Exception($"A body with two spellings of Nickname should be refused, the user became [Nickname={user.Nickname} Age={user.Age}]"),
                error =>
                {
                    Assert.Equal(ValidationError.VALIDATION, error.Code);
                    Assert.Equal("Nickname", error.Property);
                    return 0;
                });

            // Nothing was written, not even the property that is not ambiguous
            var unchanged = await ApilaneService.AccountUpdateAsync<UserItem>(
                AccountUpdateRequest.New().WithAuthToken(authToken),
                new Dictionary<string, object?> { ["Nickname"] = "first" });

            unchanged.Match(
                user => Assert.Equal(30, user.Age),
                error => throw new Exception($"Update failed | {error.Code} | {error.Message}"));
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task AccountRegister_PropertyNameInOtherCase_Should_Store_The_Value(DatabaseType dbType)
        {
            await PrepareUsersAsync(dbType);

            // Registered with nickname and AGE: the properties are Nickname and Age
            var authToken = await RegisterAndLoginAsync(new LowerCaseRegisterItem()
            {
                Email = "test@test.com",
                Username = "test",
                Password = "password",
                nickname = "registered",
                AGE = 41
            });

            var user = await ApilaneService.GetAccountUserDataAsync<UserItem>(AccountUserDataRequest.New().WithAuthToken(authToken));

            user.Match(
                response =>
                {
                    Assert.Equal("registered", response.User.Nickname);
                    Assert.Equal(41, response.User.Age);
                },
                error => throw new Exception($"Account data failed | {error.Code} | {error.Message}"));
        }

        // ─── Application and calls ────────────────────────────────────────────────

        private async Task PrepareItemsAsync(DatabaseType dbType)
        {
            await InitializeApplicationAsync(dbType, null, false);
            await AddEntityAsync(ItemEntity);

            // Not required: a value that goes missing is written as NULL without an error
            await AddStringPropertyAsync(ItemEntity, nameof(CasingItem.Title), required: false);
            await AddNumberPropertyAsync(ItemEntity, nameof(CasingItem.Price), required: false, decimalPlaces: 0);
        }

        private async Task PrepareUsersAsync(DatabaseType dbType)
        {
            await InitializeApplicationAsync(dbType, null, false);

            await AddStringPropertyAsync("Users", nameof(UserItem.Nickname), required: false);
            await AddNumberPropertyAsync("Users", nameof(UserItem.Age), required: false, decimalPlaces: 0);
        }

        private async Task<string> RegisterAndLoginAsync(IRegisterItem registerItem)
        {
            var register = await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(registerItem));
            register.Match(
                userId => Assert.True(userId > 0),
                error => throw new Exception($"Register failed | {error.Code} | {error.Message} | {error.Property}"));

            var login = await ApilaneService.AccountLoginAsync<UserItem>(AccountLoginRequest.New(new LoginItem()
            {
                Email = registerItem.Email,
                Password = registerItem.Password
            }));

            return login.Match(
                success => success.AuthToken,
                error => throw new Exception($"Login failed | {error.Code} | {error.Message} | {error.Property}"));
        }

        /// <summary>Anonymous callers may read, create and update the two properties of the item entity.</summary>
        private IDisposable GrantItemAccess()
        {
            var properties = new List<string>() { nameof(CasingItem.Title), nameof(CasingItem.Price) };

            return new CompositeDisposable(
                new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                    inRole: Globals.ANONYMOUS, actionType: SecurityActionType.get, properties: properties),
                new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                    inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post, properties: properties),
                new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                    inRole: Globals.ANONYMOUS, actionType: SecurityActionType.put, properties: properties));
        }

        private async Task<long> PostItemAsync(object body)
        {
            var post = await ApilaneService.PostDataAsync(DataPostRequest.New(ItemEntity), body);

            return post.Match(
                ids => ids.Single(),
                error => throw new Exception($"Post failed | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task PutAsync(object body)
        {
            var put = await ApilaneService.PutDataAsync(DataPutRequest.New(ItemEntity), body);

            put.Match(
                affected => Assert.Equal(1, affected),
                error => throw new Exception($"Put failed | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task<CasingItem> GetItemAsync(long id)
        {
            var get = await ApilaneService.GetDataByIdAsync<CasingItem>(DataGetByIdRequest.New(ItemEntity, id));

            return get.Match(
                item => item,
                error => throw new Exception($"Get failed | {error.Code} | {error.Message} | {error.Property}"));
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
