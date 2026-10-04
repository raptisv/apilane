using Apilane.Data.Repository;
using System.Data.SQLite;

namespace Apilane.UnitTests
{
    /// <summary>
    /// "Does this table or column exist" is answered from the schema. An error must surface as an error:
    /// answered as "missing", the caller would go on to create what is already there.
    /// </summary>
    [TestClass]
    public class SQLiteSchemaChecksTests
    {
        private string _directory = null!;
        private string _path = null!;
        private string _connectionString = null!;

        [TestInitialize]
        public async Task InitializeAsync()
        {
            _directory = Path.Combine(Path.GetTempPath(), "apilane-sqlite-schema", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            _path = Path.Combine(_directory, "app.db");
            SQLiteDataStorageRepository.GenerateDatabase(_path);
            _connectionString = $"Data Source={_path};Cache Size=2000;Version=3;FailIfMissing=True;foreign keys=true;";

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
        [DataRow("Items", true)]
        [DataRow("items", true)]
        [DataRow("Missing", false)]
        [DataRow("It'ems", false)]
        public async Task ExistsTableAsync_Should_Answer_From_The_Schema(string tableName, bool expected)
        {
            using var repository = new SQLiteDataStorageRepository(_connectionString);

            Assert.AreEqual(expected, await repository.ExistsTableAsync(tableName));
        }

        [TestMethod]
        [DataRow("Items", "Name", true)]
        [DataRow("Items", "name", true)]
        [DataRow("Items", "Missing", false)]
        [DataRow("Missing", "Name", false)]
        public async Task ExistsColumnAsync_Should_Answer_From_The_Schema(string tableName, string columnName, bool expected)
        {
            using var repository = new SQLiteDataStorageRepository(_connectionString);

            Assert.AreEqual(expected, await repository.ExistsColumnAsync(tableName, columnName));
        }

        [TestMethod]
        public async Task ExistsTableAsync_Empty_Table_Should_Return_True()
        {
            using var repository = new SQLiteDataStorageRepository(_connectionString);

            Assert.IsTrue(await repository.ExistsTableAsync("Items"));
            Assert.AreEqual(0L, Convert.ToInt64((await repository.ExecTableAsync("SELECT COUNT(*) FROM Items;")).Rows[0][0]));
        }

        [TestMethod]
        public async Task ExistsTableAsync_Locked_Database_Should_Throw_Not_Return_False()
        {
            // Another connection holds the database exclusively
            using var locker = new SQLiteConnection($"Data Source={_path};Version=3;");
            locker.Open();
            using (var begin = new SQLiteCommand("BEGIN EXCLUSIVE;", locker))
            {
                begin.ExecuteNonQuery();
            }

            using var repository = new SQLiteDataStorageRepository(_connectionString);

            await Assert.ThrowsAsync<SQLiteException>(() => repository.ExistsTableAsync("Items"));
        }
    }
}
