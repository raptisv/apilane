using Apilane.Data.Repository;
using System.Data.SQLite;
using System.Transactions;

namespace Apilane.UnitTests
{
    /// <summary>
    /// Work done inside a transaction scope must be undone when the scope is not completed, whether the
    /// repository was first used inside the scope or before it (an update with change tracking reads the
    /// record first and opens the scope afterwards).
    /// </summary>
    [TestClass]
    public class SQLiteTransactionScopeTests
    {
        private string _directory = null!;
        private string _connectionString = null!;

        [TestInitialize]
        public async Task InitializeAsync()
        {
            _directory = Path.Combine(Path.GetTempPath(), "apilane-sqlite-scope", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            var path = Path.Combine(_directory, "app.db");
            SQLiteDataStorageRepository.GenerateDatabase(path);

            // Same connection string shape as ApplicationExtensions.GetConnectionstring
            _connectionString = $"Data Source={path};Cache Size=2000;Version=3;FailIfMissing=True;foreign keys=true;";

            using var setup = new SQLiteDataStorageRepository(_connectionString);
            await setup.ExecNQAsync("CREATE TABLE Items (ID INTEGER PRIMARY KEY, Name TEXT);");
        }

        [TestCleanup]
        public void Cleanup()
        {
            SQLiteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a file handle may outlive the test on some platforms
            }
        }

        [TestMethod]
        public async Task Scope_Opened_Before_First_Use_Not_Completed_Should_Roll_Back()
        {
            using (var repository = new SQLiteDataStorageRepository(_connectionString))
            {
                using (NewScope())
                {
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('a');");
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('b');");
                }
            }

            Assert.AreEqual(0, await CountAsync());
        }

        [TestMethod]
        public async Task Scope_Opened_After_A_Read_Not_Completed_Should_Roll_Back()
        {
            using (var repository = new SQLiteDataStorageRepository(_connectionString))
            {
                // The connection is open before the scope starts
                await repository.ExecTableAsync("SELECT COUNT(*) FROM Items;");

                using (NewScope())
                {
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('a');");
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('b');");
                }
            }

            Assert.AreEqual(0, await CountAsync());
        }

        [TestMethod]
        public async Task Scope_Opened_After_A_Read_Completed_Should_Commit()
        {
            using (var repository = new SQLiteDataStorageRepository(_connectionString))
            {
                await repository.ExecTableAsync("SELECT COUNT(*) FROM Items;");

                using (var scope = NewScope())
                {
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('a');");
                    scope.Complete();
                }
            }

            Assert.AreEqual(1, await CountAsync());
        }

        [TestMethod]
        public async Task Nested_Scope_Completed_Inside_An_Abandoned_One_Should_Roll_Back()
        {
            using (var repository = new SQLiteDataStorageRepository(_connectionString))
            {
                await repository.ExecTableAsync("SELECT COUNT(*) FROM Items;");

                using (NewScope())
                {
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('outer');");

                    using (var inner = NewScope())
                    {
                        await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('inner');");
                        inner.Complete();
                    }
                }
            }

            Assert.AreEqual(0, await CountAsync());
        }

        [TestMethod]
        public async Task Repository_After_An_Abandoned_Scope_Should_Work_And_Join_The_Next_Scope()
        {
            using (var repository = new SQLiteDataStorageRepository(_connectionString))
            {
                using (NewScope())
                {
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('undone');");
                }

                // Outside any scope: saved at once
                await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('kept');");

                using (NewScope())
                {
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('undone again');");
                }

                using (var scope = NewScope())
                {
                    await repository.ExecNQAsync("INSERT INTO Items (Name) VALUES ('kept too');");
                    scope.Complete();
                }
            }

            Assert.AreEqual(2, await CountAsync());
        }

        private static TransactionScope NewScope()
        {
            // As TransactionScopeService opens it
            return new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromSeconds(30) },
                TransactionScopeAsyncFlowOption.Enabled);
        }

        private async Task<long> CountAsync()
        {
            using var repository = new SQLiteDataStorageRepository(_connectionString);
            var table = await repository.ExecTableAsync("SELECT COUNT(*) FROM Items;");
            return Convert.ToInt64(table.Rows[0][0]);
        }
    }
}
