using Apilane.Data.Repository;
using System.Data.SQLite;

namespace Apilane.UnitTests
{
    /// <summary>
    /// Custom endpoint SQL is written by application owners and runs on the application's SQLite
    /// connection. These tests pin down that such SQL cannot leave the application's own database file
    /// or change the API process, while ordinary SQL keeps working.
    /// </summary>
    [TestClass]
    public class SQLiteCustomSqlIsolationTests
    {
        private string _directory = null!;
        private string _otherDatabasePath = null!;
        private SQLiteDataStorageRepository _repository = null!;

        [TestInitialize]
        public async Task InitializeAsync()
        {
            _directory = Path.Combine(Path.GetTempPath(), "apilane-sqlite-isolation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            // Another application's database, sitting next to ours as it does on the API server
            _otherDatabasePath = Path.Combine(_directory, "other.db");
            using (var other = CreateRepository(_otherDatabasePath))
            {
                await other.ExecNQAsync("CREATE TABLE Secrets (Value TEXT); INSERT INTO Secrets (Value) VALUES ('other tenant data');");
            }

            _repository = CreateRepository(Path.Combine(_directory, "app.db"));
            await _repository.ExecNQAsync("CREATE TABLE Items (ID INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Items (Name) VALUES ('a'), ('b');");
        }

        [TestCleanup]
        public void Cleanup()
        {
            _repository.Dispose();

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

        private static SQLiteDataStorageRepository CreateRepository(string path)
        {
            SQLiteDataStorageRepository.GenerateDatabase(path);

            // Same connection string shape as ApplicationExtensions.GetConnectionstring
            return new SQLiteDataStorageRepository($"Data Source={path};Cache Size=2000;Version=3;FailIfMissing=True;foreign keys=true;");
        }

        private static string Escaped(string path)
        {
            return path.Replace("'", "''");
        }

        [TestMethod]
        public async Task ExecuteCustomAsync_AttachAnotherDatabase_IsRejected()
        {
            var sql = $"ATTACH DATABASE '{Escaped(_otherDatabasePath)}' AS other; SELECT * FROM other.Secrets;";

            await Assert.ThrowsAsync<SQLiteException>(() => _repository.ExecuteCustomAsync(sql));
        }

        [TestMethod]
        public async Task ExecuteCustomAsync_AttachNewFile_DoesNotCreateIt()
        {
            var target = Path.Combine(_directory, "created-by-attach.db");

            await Assert.ThrowsAsync<SQLiteException>(() =>
                _repository.ExecuteCustomAsync($"ATTACH DATABASE '{Escaped(target)}' AS x; CREATE TABLE x.T (A);"));

            Assert.IsFalse(File.Exists(target));
        }

        [TestMethod]
        public async Task ExecuteCustomAsync_VacuumInto_DoesNotWriteAFile()
        {
            var target = Path.Combine(_directory, "copy.db");

            await Assert.ThrowsAsync<SQLiteException>(() =>
                _repository.ExecuteCustomAsync($"VACUUM INTO '{Escaped(target)}';"));

            Assert.IsFalse(File.Exists(target));
        }

        [TestMethod]
        [DataRow("SELECT load_extension('anything');")]
        [DataRow("SELECT LOAD_EXTENSION('anything', 'entry');")]
        [DataRow("DETACH DATABASE main;")]
        [DataRow("PRAGMA writable_schema = 1;")]
        [DataRow("PRAGMA main.writable_schema = ON;")]
        [DataRow("PRAGMA hard_heap_limit = 1024;")]
        [DataRow("PRAGMA soft_heap_limit = 1024;")]
        [DataRow("PRAGMA temp_store_directory = 'C:\\';")]
        [DataRow("PRAGMA journal_mode = DELETE;")]
        [DataRow("PRAGMA locking_mode = EXCLUSIVE;")]
        [DataRow("PRAGMA database_list;")]
        [DataRow("SELECT * FROM pragma_database_list;")]
        [DataRow("SELECT * FROM pragma_database_list();")]
        [DataRow("/* comment */ pragma\tWRITABLE_SCHEMA=1;")]
        [DataRow("UPDATE sqlite_dbpage SET data = zeroblob(4096) WHERE pgno = 1;")]
        [DataRow("UPDATE sqlite_master SET rootpage = 2 WHERE name = 'Items';")]
        [DataRow("CREATE VIRTUAL TABLE Pwn USING csv(filename='x');")]
        [DataRow("CREATE VIRTUAL TABLE temp.Pwn USING zipfile('x');")]
        [DataRow("SELECT readfile('x');")]
        [DataRow("SELECT writefile('x', 'y');")]
        [DataRow("SELECT fts3_tokenizer('simple');")]
        public async Task ExecuteCustomAsync_StatementReachingBeyondTheDatabase_IsRejected(string sql)
        {
            await Assert.ThrowsAsync<SQLiteException>(() => _repository.ExecuteCustomAsync(sql));
        }

        [TestMethod]
        public async Task ExecuteCustomAsync_RawPageWrite_LeavesTheDatabaseIntact()
        {
            await Assert.ThrowsAsync<SQLiteException>(() =>
                _repository.ExecuteCustomAsync("UPDATE sqlite_dbpage SET data = zeroblob(4096) WHERE pgno = 1;"));

            var result = await _repository.ExecuteCustomAsync("SELECT COUNT(*) AS Total FROM Items;");

            Assert.AreEqual(1, result[0].Count);
        }

        [TestMethod]
        public async Task ExecuteCustomAsync_WriteToFullTextShadowTable_IsRejected()
        {
            await _repository.ExecNQAsync("CREATE VIRTUAL TABLE Docs USING fts5(Body); INSERT INTO Docs (Body) VALUES ('hello world');");

            await Assert.ThrowsAsync<SQLiteException>(() =>
                _repository.ExecuteCustomAsync("INSERT INTO Docs_data (id, block) VALUES (999999, x'ffffffff');"));
        }

        [TestMethod]
        public async Task ExecuteCustomAsync_MultipleSelects_ReturnsEveryResultSet()
        {
            var result = await _repository.ExecuteCustomAsync("SELECT Name FROM Items ORDER BY ID; SELECT COUNT(*) AS Total FROM Items;");

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(2, result[0].Count);
            Assert.AreEqual("a", result[0][0]["Name"]);
            Assert.AreEqual(1, result[1].Count);
        }

        [TestMethod]
        [DataRow("PRAGMA table_info(Items);")]
        [DataRow("SELECT name FROM pragma_table_info('Items');")]
        [DataRow("PRAGMA foreign_key_list(Items);")]
        [DataRow("PRAGMA index_list(Items);")]
        [DataRow("CREATE TEMP TABLE Scratch (A); INSERT INTO Scratch VALUES (1); SELECT * FROM Scratch;")]
        [DataRow("SELECT name FROM sqlite_master WHERE type = 'table';")]
        [DataRow("SELECT value FROM json_each('[1,2,3]');")]
        [DataRow("CREATE VIRTUAL TABLE Geo USING rtree(id, minX, maxX); INSERT INTO Geo VALUES (1, 0, 1); SELECT * FROM Geo;")]
        [DataRow("CREATE VIRTUAL TABLE Notes USING fts4(Body); INSERT INTO Notes (Body) VALUES ('hello'); SELECT * FROM Notes WHERE Notes MATCH 'hello';")]
        [DataRow("PRAGMA foreign_keys = off; DELETE FROM Items; PRAGMA foreign_keys = on;")]
        [DataRow("WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n WHERE x < 5) SELECT SUM(x) AS Total FROM n;")]
        public async Task ExecuteCustomAsync_OrdinarySql_StillWorks(string sql)
        {
            var result = await _repository.ExecuteCustomAsync(sql);

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task ExecuteCustomAsync_FullTextSearch_StillWorks()
        {
            await _repository.ExecNQAsync("CREATE VIRTUAL TABLE Docs USING fts5(Body); INSERT INTO Docs (Body) VALUES ('hello world'), ('goodbye');");

            var result = await _repository.ExecuteCustomAsync("SELECT Body FROM Docs WHERE Docs MATCH 'hello';");

            Assert.AreEqual(1, result[0].Count);
            Assert.AreEqual("hello world", result[0][0]["Body"]);
        }

        [TestMethod]
        public async Task ExecuteCustomAsync_AfterARejectedStatement_ConnectionKeepsWorking()
        {
            await Assert.ThrowsAsync<SQLiteException>(() =>
                _repository.ExecuteCustomAsync($"ATTACH DATABASE '{Escaped(_otherDatabasePath)}' AS other;"));

            var result = await _repository.ExecuteCustomAsync("SELECT COUNT(*) AS Total FROM Items;");

            Assert.AreEqual(1, result[0].Count);
        }
    }
}
