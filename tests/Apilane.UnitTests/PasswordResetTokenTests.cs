using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Data.Helper.Models;
using FakeItEasy;

namespace Apilane.UnitTests
{
    [TestClass]
    public class PasswordResetTokenTests
    {
        [TestMethod]
        public async Task GetUserIdFromPasswordResetTokenAsync_Should_Only_Accept_Tokens_Of_The_Last_24_Hours()
        {
            // Arrange
            var factory = A.Fake<IApplicationDataStoreFactory>();
            FilterData? usedFilter = null;

            A.CallTo(factory)
                .Where(call => call.Method.Name == nameof(IApplicationDataStoreFactory.GetPagedDataAsync))
                .WithReturnType<Task<List<Dictionary<string, object?>>>>()
                .Invokes(call => usedFilter = call.Arguments.OfType<FilterData>().SingleOrDefault())
                .Returns(new List<Dictionary<string, object?>>());

            var service = new ApplicationHelperService(factory);
            var before = Apilane.Common.Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow.AddHours(-24));

            // Act
            var userId = await service.GetUserIdFromPasswordResetTokenAsync(Guid.NewGuid().ToString());

            // Assert
            var after = Apilane.Common.Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow.AddHours(-24));

            Assert.IsNull(userId);
            Assert.IsNotNull(usedFilter);
            Assert.AreEqual(FilterData.FilterLogic.AND, usedFilter.Logic);
            Assert.IsNotNull(usedFilter.Filters);

            var token = usedFilter.Filters.Single(x => x.Property == nameof(H_Auth_Password_Reset_Tokens.Token));
            Assert.AreEqual(FilterData.FilterOperators.equal, token.Operator);

            var created = usedFilter.Filters.Single(x => x.Property == nameof(H_Auth_Password_Reset_Tokens.Created));
            Assert.AreEqual(FilterData.FilterOperators.greaterorequal, created.Operator);

            var oldestValid = Convert.ToInt64(created.Value);
            Assert.IsTrue(oldestValid >= before && oldestValid <= after, $"The cut-off {oldestValid} is not 24 hours ago ({before} to {after}).");
        }

        [TestMethod]
        public async Task GetUserIdFromPasswordResetTokenAsync_Not_A_Token_Should_Return_Null_Without_A_Query()
        {
            // Arrange
            var factory = A.Fake<IApplicationDataStoreFactory>();
            var service = new ApplicationHelperService(factory);

            // Act
            var userId = await service.GetUserIdFromPasswordResetTokenAsync("not-a-token");

            // Assert
            Assert.IsNull(userId);
            A.CallTo(factory).MustNotHaveHappened();
        }
    }
}
