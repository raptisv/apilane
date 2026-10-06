using Apilane.Api.Core;
using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using FakeItEasy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Apilane.UnitTests
{
    /// <summary>
    /// The page size of the four list endpoints (Data/Get, Files/Get, Data/GetHistoryByID and Stats/Aggregate) is
    /// 1 to 1000. The repositories read a page size of 0 or less as "no LIMIT" (the API itself relies on that to
    /// load all the records of a delete), so what the endpoints hand to the data layer must never be 0 or less,
    /// or above 1000. The tests look at the page size that arrives there.
    /// </summary>
    [TestClass]
    public class PageSizeLimitTests
    {
        private const string AppToken = "11111111-1111-1111-1111-111111111111";
        private const string EntityName = "Orders";

        private static DBWS_EntityProperty MakeProp(string name, PropertyType type, bool isPrimaryKey = false)
        {
            return new DBWS_EntityProperty
            {
                ID = 1,
                EntityID = 1,
                Name = name,
                TypeID = (int)type,
                IsPrimaryKey = isPrimaryKey,
                IsSystem = isPrimaryKey,
                Required = false,
                DateModified = DateTime.UtcNow
            };
        }

        private static DBWS_Entity MakeEntity(string name)
        {
            return new DBWS_Entity
            {
                ID = 1,
                AppID = 1,
                Name = name,
                Properties = new List<DBWS_EntityProperty>()
                {
                    MakeProp("ID", PropertyType.Number, isPrimaryKey: true),
                    MakeProp("Name", PropertyType.String)
                },
                DateModified = DateTime.UtcNow
            };
        }

        // ─── Data/Get ───────────────────────────────────────────────────────────

        [TestMethod]
        [DataRow(0, 1000)]
        [DataRow(-1, 1000)]
        [DataRow(-100, 1000)]
        [DataRow(1001, 1000)]
        [DataRow(int.MaxValue, 1000)]
        [DataRow(1, 1)]
        [DataRow(20, 20)]
        [DataRow(1000, 1000)]
        public async Task DataGetAsync_PageSize_Reaches_The_Data_Layer_Within_1_To_1000(int pageSize, int expected)
        {
            var appDataService = A.Fake<IApplicationDataService>();
            var capturedPageSize = int.MinValue;

            A.CallTo(() => appDataService.GetSystemFilters(A<bool>._, A<string?>._, A<DBWS_Entity>._, A<(Users?, List<DBWS_Security>)>._))
                .ReturnsLazily(() => new List<FilterData>());
            A.CallTo(() => appDataService.GetFilterData(A<DBWS_Entity>._, A<string?>._, A<List<DBWS_Security>>._))
                .Returns((FilterData?)null);
            A.CallTo(() => appDataService.GetSortData(A<DBWS_Entity>._, A<string?>._, A<List<DBWS_Security>>._))
                .Returns((List<SortData>?)null);
            A.CallTo(() => appDataService.GetAsync(AppToken, A<string?>._, string.Empty, A<DBWS_Entity>._, A<int>._, A<int>._, A<FilterData?>._, A<List<SortData>?>._, A<string?>._, A<(Users?, List<DBWS_Security>)>._, A<bool>._))
                .Invokes(call => capturedPageSize = call.GetArgument<int>(5))
                .Returns(Task.FromResult(new DataResponse { Data = new List<Dictionary<string, object?>>() }));

            var dataAPI = new DataAPI(appDataService, A.Fake<ITransactionScopeService>(), A.Fake<ICustomAPI>());

            await dataAPI.GetAsync(
                AppToken,
                MakeEntity(EntityName),
                userHasFullAccess: true,
                appUser: null,
                applicationSecurityList: new List<DBWS_Security>(),
                databaseType: DatabaseType.SQLLite,
                differentiationEntity: null,
                applicationEncryptionKey: string.Empty,
                pageIndex: 1,
                pageSize: pageSize,
                filter: null,
                sort: null,
                properties: null,
                getTotal: false);

            Assert.AreEqual(expected, capturedPageSize);
        }

        // ─── Files/Get ──────────────────────────────────────────────────────────

        [TestMethod]
        [DataRow(0, 1000)]
        [DataRow(-1, 1000)]
        [DataRow(-100, 1000)]
        [DataRow(1001, 1000)]
        [DataRow(int.MaxValue, 1000)]
        [DataRow(1, 1)]
        [DataRow(20, 20)]
        [DataRow(1000, 1000)]
        public async Task FileGetAsync_PageSize_Reaches_The_Data_Layer_Within_1_To_1000(int pageSize, int expected)
        {
            var appDataService = A.Fake<IApplicationDataService>();
            var applicationService = A.Fake<IApplicationService>();
            var capturedPageSize = int.MinValue;

            A.CallTo(() => applicationService.GetAsync(AppToken))
                .Returns(Task.FromResult(new DBWS_Application { Entities = new List<DBWS_Entity>() { MakeEntity("Files") } }));
            A.CallTo(() => appDataService.GetSystemFilters(A<bool>._, A<string?>._, A<DBWS_Entity>._, A<(Users?, List<DBWS_Security>)>._))
                .ReturnsLazily(() => new List<FilterData>());
            A.CallTo(() => appDataService.GetFilterData(A<DBWS_Entity>._, A<string?>._, A<List<DBWS_Security>>._))
                .Returns((FilterData?)null);
            A.CallTo(() => appDataService.GetSortData(A<DBWS_Entity>._, A<string?>._, A<List<DBWS_Security>>._))
                .Returns((List<SortData>?)null);
            A.CallTo(() => appDataService.GetAsync(AppToken, A<string?>._, string.Empty, A<DBWS_Entity>._, A<int>._, A<int>._, A<FilterData?>._, A<List<SortData>?>._, A<string?>._, A<(Users?, List<DBWS_Security>)>._, A<bool>._))
                .Invokes(call => capturedPageSize = call.GetArgument<int>(5))
                .Returns(Task.FromResult(new DataResponse { Data = new List<Dictionary<string, object?>>() }));

            var fileAPI = new FileAPI(
                CreateConfiguration(),
                appDataService,
                applicationService,
                A.Fake<IApplicationDataStoreFactory>(),
                A.Fake<ICloudStorageProvider>(),
                A.Fake<ILogger<FileAPI>>());

            await fileAPI.GetAsync(
                AppToken,
                userHasFullAccess: true,
                appUser: null,
                applicationSecurityList: new List<DBWS_Security>(),
                differentiationEntity: null,
                applicationEncryptionKey: string.Empty,
                pageIndex: 1,
                pageSize: pageSize,
                properties: null,
                filter: null,
                sort: null,
                getTotal: false);

            Assert.AreEqual(expected, capturedPageSize);
        }

        // ─── Data/GetHistoryByID ────────────────────────────────────────────────

        [TestMethod]
        [DataRow(0, 1000)]
        [DataRow(-1, 1000)]
        [DataRow(-100, 1000)]
        [DataRow(1001, 1000)]
        [DataRow(int.MaxValue, 1000)]
        [DataRow(null, 1000)]
        [DataRow(1, 1)]
        [DataRow(10, 10)]
        [DataRow(1000, 1000)]
        public async Task EntityHistoryGetPagedAsync_PageSize_Reaches_The_Data_Layer_Within_1_To_1000(int? pageSize, int expected)
        {
            var helperService = A.Fake<IApplicationHelperService>();
            var capturedPageSize = int.MinValue;

            A.CallTo(() => helperService.GetHistoryForRecordPagedAsync(A<string>._, A<long>._, A<int>._, A<int>._))
                .Invokes(call => capturedPageSize = call.GetArgument<int>(3))
                .Returns(Task.FromResult((new List<Dictionary<string, object?>>(), 0L)));

            var entityHistoryAPI = new EntityHistoryAPI(helperService);

            await entityHistoryAPI.GetPagedAsync(AppToken, 1, EntityName, 1, pageSize);

            Assert.AreEqual(expected, capturedPageSize);
        }

        // ─── Stats/Aggregate ────────────────────────────────────────────────────

        [TestMethod]
        [DataRow(0, 1000)]
        [DataRow(-1, 1000)]
        [DataRow(-100, 1000)]
        [DataRow(1001, 1000)]
        [DataRow(int.MaxValue, 1000)]
        [DataRow(1, 1)]
        [DataRow(20, 20)]
        [DataRow(1000, 1000)]
        public async Task StatsAggregateAsync_PageSize_Reaches_The_Data_Layer_Within_1_To_1000(int pageSize, int expected)
        {
            var appDataService = A.Fake<IApplicationDataService>();
            var dataStore = A.Fake<IApplicationDataStoreFactory>();
            var capturedPageSize = int.MinValue;

            A.CallTo(() => appDataService.GetSystemFilters(A<bool>._, A<string?>._, A<DBWS_Entity>._, A<(Users?, List<DBWS_Security>)>._))
                .ReturnsLazily(() => new List<FilterData>());
            A.CallTo(() => appDataService.GetFilterData(A<DBWS_Entity>._, A<string?>._, A<List<DBWS_Security>>._))
                .Returns((FilterData?)null);
            A.CallTo(() => dataStore.AggregateDataAsync(EntityName, A<AggregateData>._, A<FilterData?>._, A<GroupData?>._, A<bool>._, A<int>._, A<int>._))
                .Invokes(call => capturedPageSize = call.GetArgument<int>(6))
                .Returns(Task.FromResult(new List<Dictionary<string, object?>>()));

            var statsAPI = new StatsAPI(appDataService, dataStore, A.Fake<IApplicationHelperService>());

            await statsAPI.AggregateAsync(
                AppToken,
                MakeEntity(EntityName),
                userHasFullAccess: true,
                appUser: null,
                applicationSecurityList: new List<DBWS_Security>(),
                differentiationEntity: null,
                properties: "ID.Count",
                pageIndex: 1,
                pageSize: pageSize,
                filter: null,
                groupBy: null);

            Assert.AreEqual(expected, capturedPageSize);
        }

        private static ApiConfiguration CreateConfiguration()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>()
                {
                    ["Url"] = "http://localhost:5001",
                    ["PortalUrl"] = "http://localhost:5000",
                    ["FilesPath"] = Path.Combine(Path.GetTempPath(), "apilane-page-size-tests"),
                    ["InstallationKey"] = "unit-tests-installation-key-not-a-secret"
                })
                .Build();

            return new ApiConfiguration(configuration);
        }
    }
}
