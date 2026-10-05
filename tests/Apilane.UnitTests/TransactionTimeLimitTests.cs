using Apilane.Common.Services;
using Apilane.Common.Utilities;
using System.Transactions;

namespace Apilane.UnitTests
{
    /// <summary>
    /// The time limit that <see cref="TransactionScopeService"/> records for the transaction it opens, which
    /// the MySQL repository uses to stop a command before its scope times out.
    /// </summary>
    [TestClass]
    public class TransactionTimeLimitTests
    {
        [TestMethod]
        public void GetRemaining_Without_Transaction_Should_Return_Null()
        {
            Assert.IsNull(TransactionTimeLimit.GetRemaining());
        }

        [TestMethod]
        public void GetRemaining_In_A_Scope_Opened_By_The_Service_Should_Return_The_Time_Left()
        {
            using (NewServiceScope(TimeSpan.FromSeconds(10)))
            {
                var remaining = TransactionTimeLimit.GetRemaining();

                Assert.IsNotNull(remaining);
                Assert.IsTrue(remaining.Value > TimeSpan.FromSeconds(5) && remaining.Value <= TimeSpan.FromSeconds(10), $"Unexpected time left: {remaining}");
            }
        }

        [TestMethod]
        public void GetRemaining_In_The_Default_Scope_Should_Use_The_Default_Timeout_Of_The_Service()
        {
            using (new TransactionScopeService().OpenTransactionScope())
            {
                var remaining = TransactionTimeLimit.GetRemaining();

                Assert.IsNotNull(remaining);
                Assert.IsTrue(remaining.Value > TimeSpan.Zero && remaining.Value <= TimeSpan.FromSeconds(5), $"Unexpected time left: {remaining}");
            }
        }

        [TestMethod]
        public void GetRemaining_In_A_Scope_Not_Opened_By_The_Service_Should_Return_Null()
        {
            using (new TransactionScope(TransactionScopeOption.Required, TimeSpan.FromSeconds(10), TransactionScopeAsyncFlowOption.Enabled))
            {
                Assert.IsNull(TransactionTimeLimit.GetRemaining());
            }
        }

        [TestMethod]
        public void GetRemaining_In_A_Scope_That_Joined_An_Outer_Scope_Should_Keep_The_Limit_Of_The_Outer_One()
        {
            using (NewServiceScope(TimeSpan.FromSeconds(10)))
            using (NewServiceScope(TimeSpan.FromSeconds(120)))
            {
                var remaining = TransactionTimeLimit.GetRemaining();

                Assert.IsNotNull(remaining);
                Assert.IsTrue(remaining.Value <= TimeSpan.FromSeconds(10), $"Unexpected time left: {remaining}");
            }
        }

        [TestMethod]
        public void GetRemaining_In_A_New_Transaction_Inside_A_Scope_Should_Use_Its_Own_Limit()
        {
            using (NewServiceScope(TimeSpan.FromSeconds(10)))
            {
                using (new TransactionScopeService().OpenTransactionScope(TransactionScopeOption.RequiresNew, System.Transactions.IsolationLevel.ReadCommitted, TimeSpan.FromSeconds(120)))
                {
                    var inner = TransactionTimeLimit.GetRemaining();

                    Assert.IsNotNull(inner);
                    Assert.IsTrue(inner.Value > TimeSpan.FromSeconds(60), $"Unexpected time left: {inner}");
                }

                var outer = TransactionTimeLimit.GetRemaining();

                Assert.IsNotNull(outer);
                Assert.IsTrue(outer.Value <= TimeSpan.FromSeconds(10), $"Unexpected time left: {outer}");
            }
        }

        [TestMethod]
        public void GetRemaining_After_The_Scope_Ended_Should_Return_Null()
        {
            using (NewServiceScope(TimeSpan.FromSeconds(10)))
            {
            }

            Assert.IsNull(TransactionTimeLimit.GetRemaining());
        }

        private static TransactionScope NewServiceScope(TimeSpan timeout)
        {
            return new TransactionScopeService().OpenTransactionScope(
                TransactionScopeOption.Required,
                System.Transactions.IsolationLevel.ReadCommitted,
                timeout);
        }
    }
}
