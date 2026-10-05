using Apilane.Common.Services;
using Apilane.Data.Utilities;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Transactions;

namespace Apilane.UnitTests
{
    /// <summary>
    /// The shared helper that SQL Server, MySQL, PostgreSQL and SQLite repositories call before every
    /// command. The providers themselves are covered by the component tests that run against real servers.
    /// </summary>
    [TestClass]
    public class TransactionEnlistmentTests
    {
        [TestMethod]
        public void JoinAmbientTransaction_Without_Transaction_Should_Not_Enlist()
        {
            var connection = new RecordingConnection();

            TransactionEnlistment.JoinAmbientTransaction(connection);

            Assert.AreEqual(0, connection.Enlisted.Count);
        }

        [TestMethod]
        public void JoinAmbientTransaction_In_Scope_Should_Enlist_The_Current_Transaction()
        {
            var connection = new RecordingConnection();

            using (NewScope())
            {
                TransactionEnlistment.JoinAmbientTransaction(connection);

                Assert.AreEqual(1, connection.Enlisted.Count);
                Assert.AreEqual(Transaction.Current, connection.Enlisted[0]);
            }
        }

        [TestMethod]
        public void JoinAmbientTransaction_Called_Again_In_The_Same_Scope_Should_Pass_The_Same_Transaction()
        {
            var connection = new RecordingConnection();

            using (NewScope())
            {
                TransactionEnlistment.JoinAmbientTransaction(connection);
                TransactionEnlistment.JoinAmbientTransaction(connection);

                // Every command joins again; the providers treat the same transaction as a no-op
                Assert.AreEqual(2, connection.Enlisted.Count);
                Assert.AreEqual(connection.Enlisted[0], connection.Enlisted[1]);
            }
        }

        [TestMethod]
        public void JoinAmbientTransaction_After_The_Scope_Ended_Should_Not_Enlist()
        {
            var connection = new RecordingConnection();

            using (NewScope())
            {
                TransactionEnlistment.JoinAmbientTransaction(connection);
            }

            TransactionEnlistment.JoinAmbientTransaction(connection);

            Assert.AreEqual(1, connection.Enlisted.Count);
        }

        [TestMethod]
        public void JoinAmbientTransaction_In_Aborted_Transaction_Should_Throw_Without_Enlisting()
        {
            var connection = new RecordingConnection();

            using (NewScope())
            {
                var transaction = Transaction.Current ?? throw new InvalidOperationException("no transaction");
                transaction.Rollback();

                Assert.ThrowsExactly<TransactionAbortedException>(
                    () => TransactionEnlistment.JoinAmbientTransaction(connection));
            }

            // MySQL would start a transaction on the server for an ended transaction before it fails
            Assert.AreEqual(0, connection.Enlisted.Count);
        }

        [TestMethod]
        public void GetCommandTimeout_Without_Transaction_Should_Return_The_Given_Timeout()
        {
            Assert.AreEqual(0, TransactionEnlistment.GetCommandTimeout(0));
            Assert.AreEqual(30, TransactionEnlistment.GetCommandTimeout(30));
        }

        [TestMethod]
        public void GetCommandTimeout_In_A_Scope_Not_Opened_By_The_Service_Should_Return_The_Given_Timeout()
        {
            // The time limit of such a scope is not known
            using (NewScope())
            {
                Assert.AreEqual(0, TransactionEnlistment.GetCommandTimeout(0));
                Assert.AreEqual(30, TransactionEnlistment.GetCommandTimeout(30));
            }
        }

        [TestMethod]
        public void GetCommandTimeout_With_No_Limit_In_A_Scope_Should_Stay_Below_The_Scope_Timeout()
        {
            using (NewServiceScope(TimeSpan.FromSeconds(20)))
            {
                var seconds = TransactionEnlistment.GetCommandTimeout(0);

                // 20 seconds, less the margin and the time the test has taken
                Assert.IsTrue(seconds is >= 17 and <= 19, $"Unexpected timeout: {seconds}");
            }
        }

        [TestMethod]
        public void GetCommandTimeout_Shorter_Than_The_Time_Left_Should_Be_Kept()
        {
            using (NewServiceScope(TimeSpan.FromSeconds(60)))
            {
                Assert.AreEqual(30, TransactionEnlistment.GetCommandTimeout(30));
            }
        }

        [TestMethod]
        public void GetCommandTimeout_Longer_Than_The_Time_Left_Should_Be_Cut()
        {
            using (NewServiceScope(TimeSpan.FromSeconds(10)))
            {
                var seconds = TransactionEnlistment.GetCommandTimeout(30);

                Assert.IsTrue(seconds is >= 7 and <= 9, $"Unexpected timeout: {seconds}");
            }
        }

        [TestMethod]
        public void GetCommandTimeout_When_The_Scope_Is_About_To_Time_Out_Should_Throw()
        {
            // Less than a whole second would be left once the margin is taken off
            using (NewServiceScope(TimeSpan.FromSeconds(1)))
            {
                Assert.ThrowsExactly<TransactionAbortedException>(() => TransactionEnlistment.GetCommandTimeout(0));
            }
        }

        private static TransactionScope NewServiceScope(TimeSpan timeout)
        {
            return new TransactionScopeService().OpenTransactionScope(
                TransactionScopeOption.Required,
                System.Transactions.IsolationLevel.ReadCommitted,
                timeout);
        }

        private static TransactionScope NewScope()
        {
            return new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromSeconds(30) },
                TransactionScopeAsyncFlowOption.Enabled);
        }

        /// <summary>
        /// A connection that only records which transactions it was asked to join.
        /// </summary>
        private sealed class RecordingConnection : DbConnection
        {
            public List<Transaction?> Enlisted { get; } = new();

            [AllowNull]
            public override string ConnectionString { get; set; } = string.Empty;

            public override string Database
            {
                get => string.Empty;
            }

            public override string DataSource
            {
                get => string.Empty;
            }

            public override string ServerVersion
            {
                get => string.Empty;
            }

            public override ConnectionState State
            {
                get => ConnectionState.Open;
            }

            public override void ChangeDatabase(string databaseName)
            {
                throw new NotSupportedException();
            }

            public override void Close()
            {
            }

            public override void Open()
            {
            }

            public override void EnlistTransaction(Transaction? transaction)
            {
                Enlisted.Add(transaction);
            }

            protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
            {
                throw new NotSupportedException();
            }

            protected override DbCommand CreateDbCommand()
            {
                throw new NotSupportedException();
            }
        }
    }
}
