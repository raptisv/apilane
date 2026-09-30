using Apilane.Api.Core;
using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using FakeItEasy;

namespace Apilane.UnitTests
{
    [TestClass]
    public class StatsAPIGroupByTests
    {
        private const string EntityName = "Orders";

        private readonly IApplicationDataService _appDataService;
        private readonly IApplicationDataStoreFactory _dataStore;
        private readonly StatsAPI _statsAPI;
        private GroupData? _capturedGroupData;

        // MSTest creates a new instance for every test
        public StatsAPIGroupByTests()
        {
            _appDataService = A.Fake<IApplicationDataService>();
            _dataStore = A.Fake<IApplicationDataStoreFactory>();
            _capturedGroupData = null;

            A.CallTo(() => _appDataService.GetSystemFilters(A<bool>._, A<string?>._, A<DBWS_Entity>._, A<(Users?, List<DBWS_Security>)>._))
                .ReturnsLazily(() => new List<FilterData>());
            A.CallTo(() => _appDataService.GetFilterData(A<DBWS_Entity>._, A<string?>._, A<List<DBWS_Security>>._))
                .Returns((FilterData?)null);
            A.CallTo(() => _appDataService.GetEntityNotAllowedProperties(A<DBWS_Entity>._, A<List<DBWS_Security>>._))
                .ReturnsLazily(() => new List<DBWS_EntityProperty>());

            A.CallTo(() => _dataStore.AggregateDataAsync(EntityName,A<AggregateData>._, A<FilterData?>._, A<GroupData?>._, A<bool>._, A<int>._, A<int>._))
                .Invokes(call => _capturedGroupData = call.GetArgument<GroupData?>(3))
                .ReturnsLazily(() => Task.FromResult(new List<Dictionary<string, object?>>()));

            _statsAPI = new StatsAPI(_appDataService, _dataStore, A.Fake<IApplicationHelperService>());
        }

        private static DBWS_EntityProperty MakeProp(string name, PropertyType type, bool isPrimaryKey = false)
        {
            return new DBWS_EntityProperty
            {
                ID = 1,
                EntityID = 1,
                Name = name,
                TypeID = (int)type,
                IsPrimaryKey = isPrimaryKey,
                IsSystem = false,
                Required = false,
                DateModified = DateTime.UtcNow
            };
        }

        private static DBWS_Entity MakeEntity()
        {
            return new DBWS_Entity
            {
                ID = 1,
                AppID = 1,
                Name = EntityName,
                IsReadOnly = false,
                IsSystem = false,
                HasDifferentiationProperty = false,
                RequireChangeTracking = false,
                Properties = new List<DBWS_EntityProperty>()
                {
                    MakeProp("ID", PropertyType.Number, isPrimaryKey: true),
                    MakeProp("Created", PropertyType.Date),
                    MakeProp("Name", PropertyType.String),
                    MakeProp("Created_year", PropertyType.String)
                },
                EntConstraints = null,
                EntDefaultOrder = null,
                DateModified = DateTime.UtcNow
            };
        }

        private static List<DBWS_Security> AnonymousGetSecurity()
        {
            return new List<DBWS_Security>()
            {
                new()
                {
                    Name = EntityName,
                    RoleID = Globals.ANONYMOUS,
                    TypeID = (int)SecurityTypes.Entity,
                    Action = SecurityActionType.get.ToString(),
                    Record = (int)EndpointRecordAuthorization.All,
                    Properties = "ID,Created,Name,Created_year"
                }
            };
        }

        private Task<List<Dictionary<string, object?>>> AggregateAsAnonymousAsync(string groupBy)
        {
            return _statsAPI.AggregateAsync(
                MakeEntity(),
                userHasFullAccess: false,
                appUser: null,
                applicationSecurityList: AnonymousGetSecurity(),
                differentiationEntity: null,
                properties: "ID.Count",
                pageIndex: 1,
                pageSize: 20,
                filter: null,
                groupBy: groupBy);
        }

        // ─── Rejected group-by suffixes ─────────────────────────────────────────

        [TestMethod]
        [DataRow("Created.x from Users--", "Created.x from Users--")]
        [DataRow("Created.foo", "Created.foo")]
        [DataRow("Created.year--", "Created.year--")]
        [DataRow("Created.year)", "Created.year)")]
        [DataRow("Created.\"", "Created.\"")]
        [DataRow("Created.]", "Created.]")]
        [DataRow("Created.`", "Created.`")]
        [DataRow("Name,Created.bad", "Created.bad")]
        public async Task AggregateAsync_UnknownGroupBySuffix_ThrowsInvalidGroupByParameter(string groupBy, string expectedProperty)
        {
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => AggregateAsAnonymousAsync(groupBy));

            Assert.AreEqual(AppErrors.INVALID_GROUPBY_PARAMETER, ex.Error);
            Assert.AreEqual(expectedProperty, ex.Property);
            Assert.AreEqual(EntityName, ex.Entity);
            A.CallTo(() => _dataStore.AggregateDataAsync(EntityName,A<AggregateData>._, A<FilterData?>._, A<GroupData?>._, A<bool>._, A<int>._, A<int>._))
                .MustNotHaveHappened();
        }

        // ─── Accepted group-by values ───────────────────────────────────────────

        [TestMethod]
        public async Task AggregateAsync_ValidGroupBySuffixes_PassesCanonicalAliases()
        {
            await AggregateAsAnonymousAsync("Created.year,Created.MONTH,Created. day ,name");

            Assert.IsNotNull(_capturedGroupData);
            CollectionAssert.AreEqual(
                new[] { "Created_year", "Created_month", "Created_day", "Name" },
                _capturedGroupData.Properties.Select(x => x.Alias).ToArray());
            CollectionAssert.AreEqual(
                new[] { "Created", "Created", "Created", "Name" },
                _capturedGroupData.Properties.Select(x => x.Name).ToArray());
            CollectionAssert.AreEqual(
                new[] { GroupData.GroupByType.Date_Year, GroupData.GroupByType.Date_Month, GroupData.GroupByType.Date_Day, GroupData.GroupByType.None },
                _capturedGroupData.Properties.Select(x => x.Type).ToArray());
        }

        [TestMethod]
        [DataRow("Created")]
        [DataRow("Created.")]
        [DataRow("Created. ")]
        public async Task AggregateAsync_GroupByWithoutSuffix_UsesPropertyNameAsAlias(string groupBy)
        {
            await AggregateAsAnonymousAsync(groupBy);

            Assert.IsNotNull(_capturedGroupData);
            Assert.AreEqual(1, _capturedGroupData.Properties.Count);
            Assert.AreEqual("Created", _capturedGroupData.Properties[0].Alias);
            Assert.AreEqual(GroupData.GroupByType.None, _capturedGroupData.Properties[0].Type);
        }

        [TestMethod]
        public async Task AggregateAsync_GroupByCaseVariantDuplicates_KeptOnce()
        {
            await AggregateAsAnonymousAsync("Created.year,Created.YEAR,created.Year");

            Assert.IsNotNull(_capturedGroupData);
            Assert.AreEqual(1, _capturedGroupData.Properties.Count);
            Assert.AreEqual("Created_year", _capturedGroupData.Properties[0].Alias);
        }

        [TestMethod]
        [DataRow("Created.year,Created_year")]
        [DataRow("Created_year,Created.year")]
        public async Task AggregateAsync_DifferentGroupingsSameAlias_ThrowsInvalidGroupByParameter(string groupBy)
        {
            // A date part of 'Created' and a property named 'Created_year' would both produce the column 'Created_year'
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => AggregateAsAnonymousAsync(groupBy));

            Assert.AreEqual(AppErrors.INVALID_GROUPBY_PARAMETER, ex.Error);
            Assert.AreEqual("Created_year", ex.Property);
            A.CallTo(() => _dataStore.AggregateDataAsync(EntityName, A<AggregateData>._, A<FilterData?>._, A<GroupData?>._, A<bool>._, A<int>._, A<int>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AggregateAsync_AdminWithUnknownGroupBySuffix_ThrowsInvalidGroupByParameter()
        {
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => _statsAPI.AggregateAsync(
                MakeEntity(),
                userHasFullAccess: true,
                appUser: null,
                applicationSecurityList: new List<DBWS_Security>(),
                differentiationEntity: null,
                properties: "ID.Count",
                pageIndex: 1,
                pageSize: 20,
                filter: null,
                groupBy: "Created.x from Users--"));

            Assert.AreEqual(AppErrors.INVALID_GROUPBY_PARAMETER, ex.Error);
        }
    }
}
