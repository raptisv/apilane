using Apilane.Api.Core.Services;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using FakeItEasy;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ApplicationHelperServiceTests
    {
        private static IApplicationDataStoreFactory FakeStore(long? owner = null)
        {
            var store = A.Fake<IApplicationDataStoreFactory>();

            A.CallTo(() => store.GetPagedDataAsync(A<string>._, A<List<string>?>._, A<FilterData?>._, A<List<SortData>?>._, A<int>._, A<int>._))
                .Returns(owner.HasValue
                    ? new List<Dictionary<string, object?>>() { new() { { "Owner", owner.Value } } }
                    : new List<Dictionary<string, object?>>());

            return store;
        }

        private static void AssertStoreNotQueried(IApplicationDataStoreFactory store)
        {
            A.CallTo(() => store.GetPagedDataAsync(A<string>._, A<List<string>?>._, A<FilterData?>._, A<List<SortData>?>._, A<int>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => store.DeleteDataAsync(A<string>._, A<FilterData?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        [DataRow("%")]
        [DataRow("_")]
        [DataRow("%-%")]
        [DataRow("")]
        [DataRow("not-a-token")]
        public async Task GetUserIdFromPasswordResetTokenAsync_NonGuidToken_ReturnsNull_Without_Query(string token)
        {
            var store = FakeStore(owner: 7);

            var result = await new ApplicationHelperService(store).GetUserIdFromPasswordResetTokenAsync(token);

            Assert.IsNull(result);
            AssertStoreNotQueried(store);
        }

        [TestMethod]
        [DataRow("%")]
        [DataRow("_")]
        [DataRow("")]
        public async Task GetUserIdFromEmailConfitmationTokenAsync_NonGuidToken_ReturnsNull_Without_Query(string token)
        {
            var store = FakeStore(owner: 7);

            var result = await new ApplicationHelperService(store).GetUserIdFromEmailConfitmationTokenAsync(token);

            Assert.IsNull(result);
            AssertStoreNotQueried(store);
        }

        [TestMethod]
        [DataRow("%")]
        [DataRow("")]
        public async Task DeletePasswordResetTokenAsync_NonGuidToken_Does_Not_Delete(string token)
        {
            var store = FakeStore();

            await new ApplicationHelperService(store).DeletePasswordResetTokenAsync(token);

            AssertStoreNotQueried(store);
        }

        [TestMethod]
        public async Task GetUserIdFromPasswordResetTokenAsync_GuidToken_Returns_Owner()
        {
            var store = FakeStore(owner: 7);

            var result = await new ApplicationHelperService(store).GetUserIdFromPasswordResetTokenAsync(Guid.NewGuid().ToString());

            Assert.AreEqual(7L, result);
        }

        [TestMethod]
        public async Task GetUserIdFromEmailConfitmationTokenAsync_GuidToken_Returns_Owner()
        {
            var store = FakeStore(owner: 7);

            var result = await new ApplicationHelperService(store).GetUserIdFromEmailConfitmationTokenAsync(Guid.NewGuid().ToString());

            Assert.AreEqual(7L, result);
        }
    }
}
