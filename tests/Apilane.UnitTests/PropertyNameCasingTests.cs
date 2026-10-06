using Apilane.Api.Core;
using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Services;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using FakeItEasy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Text.Json.Nodes;

namespace Apilane.UnitTests
{
    /// <summary>
    /// Property names are matched without regard to case everywhere else in the API (filter, sort, properties,
    /// Stats), and Data/Put and Account/Update already pick the properties to write that way. The values were
    /// read from the body with an exact comparison, so a body like {"ID":5,"price":9} for the property Price
    /// answered success and wrote NULL to the column. These tests look at what reaches the data layer.
    /// </summary>
    [TestClass]
    public class PropertyNameCasingTests
    {
        private const string AppToken = "11111111-1111-1111-1111-111111111111";
        private const string EntityName = "Items";

        private readonly IApplicationDataStoreFactory _dataStore = A.Fake<IApplicationDataStoreFactory>();
        private readonly IApplicationHelperService _accountHelperService = A.Fake<IApplicationHelperService>();
        private readonly ApplicationDataService _service;
        private Dictionary<string, object?>? _written;

        // MSTest creates a new instance for every test
        public PropertyNameCasingTests()
        {
            A.CallTo(() => _dataStore.UpdateDataAsync(A<string>._, A<Dictionary<string, object?>>._, A<FilterData?>._))
                .Invokes(call => _written = call.GetArgument<Dictionary<string, object?>>(1))
                .Returns(Task.FromResult(1L));
            A.CallTo(() => _dataStore.CreateDataAsync(A<string>._, A<Dictionary<string, object?>>._, A<bool>._))
                .Invokes(call => _written = call.GetArgument<Dictionary<string, object?>>(1))
                .Returns(Task.FromResult<long?>(1L));

            _service = new ApplicationDataService(
                A.Fake<ILogger<ApplicationDataService>>(),
                _dataStore,
                A.Fake<IApplicationService>(),
                A.Fake<IApplicationHelperService>(),
                A.Fake<IEntityHistoryAPI>(),
                A.Fake<ITransactionScopeService>());
        }

        private static DBWS_Entity MakeEntity(string name)
        {
            var properties = new List<DBWS_EntityProperty>()
            {
                new() { ID = 1, EntityID = 1, Name = "ID", TypeID = (int)PropertyType.Number, IsPrimaryKey = true, IsSystem = true, Required = true },
                new() { ID = 2, EntityID = 1, Name = "Title", TypeID = (int)PropertyType.String, Required = false },
                new() { ID = 3, EntityID = 1, Name = "Price", TypeID = (int)PropertyType.Number, Required = false }
            };

            return new DBWS_Entity
            {
                ID = 1,
                AppID = 1,
                Name = name,
                Properties = properties,
                DateModified = DateTime.UtcNow
            };
        }

        private Task<long> PutAsync(string body, SecurityActionType action = SecurityActionType.put)
        {
            var entity = MakeEntity(EntityName);

            return _service.PutAsync(
                AppToken,
                entity,
                userHasFullAccess: true,
                DatabaseType.SQLLite,
                differentiationEntity: null,
                applicationEncryptionKey: string.Empty,
                item: body,
                (null, EntityAccess.GetFull(entity.Name, entity.Properties, action)));
        }

        private Task<List<long>> PostAsync(string body)
        {
            var entity = MakeEntity(EntityName);

            return _service.PostAsync(
                AppToken,
                entity,
                DatabaseType.SQLLite,
                differentiationEntity: null,
                applicationEncryptionKey: string.Empty,
                item: body,
                (null, EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.post)));
        }

        // ─── Data/Put ───────────────────────────────────────────────────────────

        [TestMethod]
        public async Task PutAsync_PropertyNameInExactCase_WritesTheValue()
        {
            await PutAsync("{\"ID\":5,\"Price\":9}");

            Assert.IsNotNull(_written);
            Assert.AreEqual(9m, _written["Price"]);
            Assert.IsFalse(_written.ContainsKey("Title"));
        }

        [TestMethod]
        [DataRow("{\"ID\":5,\"price\":9}")]
        [DataRow("{\"ID\":5,\"PRICE\":9}")]
        [DataRow("{\"ID\":5,\"pRiCe\":9}")]
        public async Task PutAsync_PropertyNameInOtherCase_WritesTheValueAndNeverNull(string body)
        {
            await PutAsync(body);

            Assert.IsNotNull(_written);
            Assert.AreEqual(1, _written.Count, "Only the property of the body is written");
            Assert.AreEqual(9m, _written["Price"], "The value of the body is written, not NULL");
        }

        [TestMethod]
        public async Task PutAsync_IdInOtherCase_UpdatesTheRecord()
        {
            await PutAsync("{\"id\":5,\"Price\":9}");

            Assert.IsNotNull(_written);
            Assert.AreEqual(9m, _written["Price"]);
        }

        [TestMethod]
        [DataRow("{\"ID\":5,\"Price\":1,\"price\":2}")]
        [DataRow("{\"ID\":5,\"price\":2,\"Price\":1}")]
        [DataRow("{\"ID\":5,\"Title\":\"a\",\"PRICE\":1,\"price\":2}")]
        public async Task PutAsync_TwoSpellingsOfOneProperty_IsRejectedAndNamesTheProperty(string body)
        {
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => PutAsync(body));

            Assert.AreEqual(AppErrors.VALIDATION, ex.Error);
            Assert.AreEqual("Price", ex.Property);
            Assert.AreEqual(EntityName, ex.Entity);
            Assert.IsNull(_written, "Nothing is written when the body is ambiguous");
        }

        [TestMethod]
        [DataRow("{\"ID\":5,\"id\":6,\"Price\":9}")]
        [DataRow("{\"id\":6,\"ID\":5,\"Price\":9}")]
        public async Task PutAsync_TwoSpellingsOfId_IsRejectedAndNamesTheId(string body)
        {
            // Which record to change must never be a guess
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => PutAsync(body));

            Assert.AreEqual(AppErrors.VALIDATION, ex.Error);
            Assert.AreEqual("ID", ex.Property);
            Assert.IsNull(_written, "Nothing is written when the record is ambiguous");
        }

        [TestMethod]
        public async Task PutAsync_NameWithSpaces_IsNotAPropertyAndNeverClearsTheColumn()
        {
            // " price " used to select Price and then find no value for it: the column was set to NULL
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => PutAsync("{\"ID\":5,\" price \":9}"));

            Assert.AreEqual(AppErrors.NO_PROPERTIES_PROVIDED, ex.Error);
            Assert.IsNull(_written);
        }

        // ─── Data/Post ──────────────────────────────────────────────────────────

        [TestMethod]
        public async Task PostAsync_PropertyNamesInOtherCase_StoreTheValues()
        {
            await PostAsync("{\"title\":\"t\",\"PRICE\":9}");

            Assert.IsNotNull(_written);
            Assert.AreEqual("t", _written["Title"]);
            Assert.AreEqual(9m, _written["Price"]);
        }

        [TestMethod]
        public async Task PostAsync_TwoSpellingsOfOneProperty_IsRejectedAndNamesTheProperty()
        {
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => PostAsync("{\"Title\":\"a\",\"title\":\"b\"}"));

            Assert.AreEqual(AppErrors.VALIDATION, ex.Error);
            Assert.AreEqual("Title", ex.Property);
            Assert.IsNull(_written, "Nothing is stored when the body is ambiguous");
        }

        // ─── Account/Update ─────────────────────────────────────────────────────

        private static DBWS_Entity MakeUsersEntity(bool requireChangeTracking)
        {
            return new DBWS_Entity
            {
                ID = 1,
                AppID = 1,
                Name = nameof(Users),
                RequireChangeTracking = requireChangeTracking,
                Properties = new List<DBWS_EntityProperty>()
                {
                    new() { ID = 1, EntityID = 1, Name = "ID", TypeID = (int)PropertyType.Number, IsPrimaryKey = true, IsSystem = true, Required = true },
                    new() { ID = 2, EntityID = 1, Name = "Email", TypeID = (int)PropertyType.String, IsSystem = true, Required = true },
                    new() { ID = 3, EntityID = 1, Name = "Nickname", TypeID = (int)PropertyType.String, Required = false },
                    new() { ID = 4, EntityID = 1, Name = "Age", TypeID = (int)PropertyType.Number, Required = false }
                },
                DateModified = DateTime.UtcNow
            };
        }

        private AccountAPI CreateAccountAPI()
        {
            // Account/Update reads the user back after the write
            A.CallTo(() => _dataStore.GetPagedDataAsync(A<string>._, A<List<string>?>._, A<FilterData?>._, A<List<SortData>?>._, A<int>._, A<int>._))
                .Returns(Task.FromResult(new List<Dictionary<string, object?>>() { new() { ["ID"] = 1L, ["Password"] = "secret" } }));

            // The record the history snapshot is taken from
            A.CallTo(() => _dataStore.GetDataByIdAsync(nameof(Users), A<long>._, A<List<string>?>._))
                .Returns(Task.FromResult<Dictionary<string, object?>?>(new Dictionary<string, object?>() { ["ID"] = 1L }));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>()
                {
                    ["Url"] = "http://localhost:5001",
                    ["PortalUrl"] = "http://localhost:5000",
                    ["FilesPath"] = Path.Combine(Path.GetTempPath(), "apilane-property-casing-tests"),
                    ["InstallationKey"] = "unit-tests-installation-key-not-a-secret"
                })
                .Build();

            return new AccountAPI(
                A.Fake<ILogger<AccountAPI>>(),
                _service,
                _dataStore,
                A.Fake<IApplicationEmailService>(),
                new ApiConfiguration(configuration),
                _accountHelperService,
                A.Fake<IClusterClient>());
        }

        private static JsonObject ParseBody(string body)
        {
            return JsonNode.Parse(body) as JsonObject
                ?? throw new InvalidOperationException("The test body is not a JSON object");
        }

        private Task<Dictionary<string, object?>> AccountUpdateAsync(string body, bool requireChangeTracking = false)
        {
            var currentUser = new Users()
            {
                ID = 1,
                Email = "test@test.com",
                Username = "test",
                EmailConfirmed = false,
                Password = "secret",
                Roles = null,
                Created = 1,
                LastLogin = 1,
                DifferentiationPropertyValue = null
            };

            return CreateAccountAPI().UpdateAsync(
                string.Empty,
                MakeUsersEntity(requireChangeTracking),
                currentUser,
                DatabaseType.SQLLite,
                appEncryptionKey: string.Empty,
                differentiationEntity: null,
                ParseBody(body));
        }

        [TestMethod]
        public async Task AccountUpdateAsync_PropertyNameInExactCase_WritesTheValue()
        {
            await AccountUpdateAsync("{\"Nickname\":\"nick\"}");

            Assert.IsNotNull(_written);
            Assert.AreEqual("nick", _written["Nickname"]);
        }

        [TestMethod]
        [DataRow("{\"nickname\":\"nick\"}")]
        [DataRow("{\"NICKNAME\":\"nick\"}")]
        public async Task AccountUpdateAsync_PropertyNameInOtherCase_WritesTheValueAndNeverNull(string body)
        {
            await AccountUpdateAsync(body);

            Assert.IsNotNull(_written);
            Assert.AreEqual(1, _written.Count, "Only the property of the body is written");
            Assert.AreEqual("nick", _written["Nickname"], "The value of the body is written, not NULL");
        }

        [TestMethod]
        public async Task AccountUpdateAsync_TwoSpellingsOfOneProperty_IsRejectedAndNamesTheProperty()
        {
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => AccountUpdateAsync("{\"Nickname\":\"a\",\"nickname\":\"b\"}"));

            Assert.AreEqual(AppErrors.VALIDATION, ex.Error);
            Assert.AreEqual("Nickname", ex.Property);
            Assert.IsNull(_written, "Nothing is written when the body is ambiguous");
        }

        [TestMethod]
        public async Task AccountUpdateAsync_ValidBodyWithChangeTracking_WritesOneHistoryRow()
        {
            // The control of the next test: the history snapshot is taken for a body that is accepted
            await AccountUpdateAsync("{\"Nickname\":\"nick\"}", requireChangeTracking: true);

            A.CallTo(() => _accountHelperService.CreateHistoryAsync(nameof(Users), 1, 1, A<Dictionary<string, object?>>._))
                .MustHaveHappenedOnceExactly();
            Assert.IsNotNull(_written);
        }

        [TestMethod]
        [DataRow("{\"Age\":\"abc\"}")]
        [DataRow("{\"Nickname\":\"a\",\"nickname\":\"b\"}")]
        public async Task AccountUpdateAsync_RefusedBodyWithChangeTracking_LeavesNoHistoryRow(string body)
        {
            // A history row says "the record was changed here": a body that is refused changes nothing
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => AccountUpdateAsync(body, requireChangeTracking: true));

            Assert.AreEqual(AppErrors.VALIDATION, ex.Error);
            A.CallTo(() => _accountHelperService.CreateHistoryAsync(A<string>._, A<long>._, A<long?>._, A<Dictionary<string, object?>>._))
                .MustNotHaveHappened();
            Assert.IsNull(_written);
        }
    }
}
