using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Services;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using FakeItEasy;
using Microsoft.Extensions.Logging;

namespace Apilane.UnitTests
{
    /// <summary>
    /// Covers the property-level authorization applied to the <c>filter</c> and <c>sort</c>
    /// query parameters: a caller whose security entry lists only some properties must not be
    /// able to filter or sort on the others.
    /// </summary>
    [TestClass]
    public class ApplicationDataServiceAuthorizationTests
    {
        private static ApplicationDataService CreateService()
        {
            return new ApplicationDataService(
                A.Fake<ILogger<ApplicationDataService>>(),
                A.Fake<IApplicationDataStoreFactory>(),
                A.Fake<IApplicationService>(),
                A.Fake<IApplicationHelperService>(),
                A.Fake<IEntityHistoryAPI>(),
                A.Fake<ITransactionScopeService>());
        }

        private static DBWS_Entity CreateEntity()
        {
            return new DBWS_Entity
            {
                ID = 1,
                AppID = 1,
                Name = "Items",
                Properties = new List<DBWS_EntityProperty>
                {
                    new() { ID = 1, EntityID = 1, Name = "ID", TypeID = (int)PropertyType.Number, IsPrimaryKey = true, IsSystem = true, Required = true },
                    new() { ID = 2, EntityID = 1, Name = "Public", TypeID = (int)PropertyType.String, Required = false },
                    new() { ID = 3, EntityID = 1, Name = "Secret", TypeID = (int)PropertyType.String, Required = false }
                }
            };
        }

        /// <summary>Security that grants access to the "Public" property only.</summary>
        private static List<DBWS_Security> PublicOnlySecurity()
        {
            return new List<DBWS_Security>
            {
                new()
                {
                    Name = "Items",
                    TypeID = (int)SecurityTypes.Entity,
                    RoleID = "ANONYMOUS",
                    Action = "get",
                    Record = (int)EndpointRecordAuthorization.All,
                    Properties = "Public"
                }
            };
        }

        [TestMethod]
        public void GetFilterData_ForbiddenPropertyFollowedByAllowedSibling_Throws()
        {
            var service = CreateService();
            var entity = CreateEntity();

            // The forbidden filter is deliberately NOT the last sibling.
            var filter = "{\"Logic\":\"AND\",\"Filters\":[" +
                         "{\"Property\":\"Secret\",\"Operator\":\"startswith\",\"Value\":\"a\"}," +
                         "{\"Property\":\"Public\",\"Operator\":\"equal\",\"Value\":\"x\"}]}";

            var ex = Assert.ThrowsExactly<ApilaneException>(() => service.GetFilterData(entity, filter, PublicOnlySecurity()));

            Assert.AreEqual(AppErrors.INVALID_FILTER_PARAMETER, ex.Error);
        }

        [TestMethod]
        public void GetFilterData_ForbiddenPropertyNestedDeep_Throws()
        {
            var service = CreateService();
            var entity = CreateEntity();

            var filter = "{\"Logic\":\"AND\",\"Filters\":[" +
                         "{\"Logic\":\"OR\",\"Filters\":[{\"Property\":\"secret\",\"Operator\":\"contains\",\"Value\":\"a\"},{\"Property\":\"Public\",\"Operator\":\"equal\",\"Value\":\"x\"}]}," +
                         "{\"Property\":\"Public\",\"Operator\":\"equal\",\"Value\":\"y\"}]}";

            var ex = Assert.ThrowsExactly<ApilaneException>(() => service.GetFilterData(entity, filter, PublicOnlySecurity()));

            Assert.AreEqual(AppErrors.INVALID_FILTER_PARAMETER, ex.Error);
        }

        [TestMethod]
        public void GetFilterData_AllowedPropertiesOnly_ReturnsFilter()
        {
            var service = CreateService();
            var entity = CreateEntity();

            var filter = "{\"Logic\":\"AND\",\"Filters\":[" +
                         "{\"Property\":\"Public\",\"Operator\":\"equal\",\"Value\":\"x\"}," +
                         "{\"Property\":\"ID\",\"Operator\":\"greater\",\"Value\":0}]}";

            var result = service.GetFilterData(entity, filter, PublicOnlySecurity());

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Filters!.Count);
        }

        [TestMethod]
        public void GetSortData_ForbiddenPropertyWithDifferentCasing_Throws()
        {
            var service = CreateService();
            var entity = CreateEntity();

            var ex = Assert.ThrowsExactly<ApilaneException>(() =>
                service.GetSortData(entity, "[{\"Property\":\"secret\",\"Direction\":\"ASC\"}]", PublicOnlySecurity()));

            Assert.AreEqual(AppErrors.INVALID_SORT_PARAMETER, ex.Error);
        }

        [TestMethod]
        public void GetSortData_SingleForbiddenPropertyWithDifferentCasing_Throws()
        {
            var service = CreateService();
            var entity = CreateEntity();

            var ex = Assert.ThrowsExactly<ApilaneException>(() =>
                service.GetSortData(entity, "{\"Property\":\"SECRET\",\"Direction\":\"DESC\"}", PublicOnlySecurity()));

            Assert.AreEqual(AppErrors.INVALID_SORT_PARAMETER, ex.Error);
        }

        [TestMethod]
        public void GetSortData_AllowedProperty_ReturnsCanonicalName()
        {
            var service = CreateService();
            var entity = CreateEntity();

            var result = service.GetSortData(entity, "[{\"Property\":\"public\",\"Direction\":\"asc\"}]", PublicOnlySecurity());

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Public", result[0].Property);
        }
    }
}
