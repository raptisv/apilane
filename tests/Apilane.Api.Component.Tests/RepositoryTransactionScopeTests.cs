using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Services;
using Apilane.Data.Abstractions;
using Apilane.Data.Repository;
using Apilane.Data.Utilities;
using CasinoService.ComponentTests.Infrastructure;
using MySqlConnector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// What the data layer does with a transaction scope, per database. A repository keeps one connection for
    /// its whole life; an ADO.NET connection takes part in a transaction scope on its own only when it is
    /// opened inside that scope. The API opens a repository's connection before the scope in several flows
    /// (the system tables check of the first request for an application, the read that comes before an update
    /// with change tracking), so work done in the scope must be undone, and refused once the scope is over,
    /// whenever the connection was opened. The repositories are used directly here, with the scope the API
    /// opens (<see cref="TransactionScopeService"/>), and the table is read back through a new repository
    /// after the scope is over.
    /// </summary>
    public class RepositoryTransactionScopeTests : AppicationTestsBase
    {
        public RepositoryTransactionScopeTests() : base(SuiteContext.Shared)
        {
        }

        /// <summary>The databases whose schema changes can be rolled back. MySQL commits a schema change on its own.</summary>
        protected class TransactionalDdlTestData : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                yield return new object[] { DatabaseType.SQLLite };
                yield return new object[] { DatabaseType.SQLServer };
                yield return new object[] { DatabaseType.PostgreSQL };
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static IDataStorageRepository CreateRepository(DatabaseType dbType, string connectionString)
        {
            return dbType switch
            {
                DatabaseType.SQLLite => new SQLiteDataStorageRepository(connectionString),
                DatabaseType.SQLServer => new SQLServerDataStorageRepository(connectionString),
                DatabaseType.MySQL => new MySQLDataStorageRepository(connectionString),
                DatabaseType.PostgreSQL => new PostgreSQLDataStorageRepository(connectionString),
                _ => throw new NotSupportedException($"Database type '{dbType}' is not supported")
            };
        }

        /// <summary>
        /// A scope as the API opens it. Disposing it without Complete() abandons the work done in it, which is
        /// what a failed request (or a custom endpoint Test) does.
        /// </summary>
        private static TransactionScope NewScope(TimeSpan? timeout = null)
        {
            return new TransactionScopeService().OpenTransactionScope(
                TransactionScopeOption.Required,
                IsolationLevel.ReadCommitted,
                timeout ?? TimeSpan.FromSeconds(30));
        }

        private async Task<ScopeTable> NewTableAsync(DatabaseType dbType)
        {
            // The database of this test class, in the container of that type (null for SQLite)
            var serverConnectionString = await SuiteContext.Shared.Databases.GetConnectionStringAsync(dbType, GetDatabaseName(GetType()));

            return await ScopeTable.CreateAsync(dbType, serverConnectionString);
        }

        private static FilterData ById(long id)
        {
            return new FilterData(Globals.PrimaryKeyColumn, FilterData.FilterOperators.equal, id, PropertyType.Number);
        }

        /// <summary>
        /// A throw-away table (an ID and a required Name) in the database of the test class, or in a file of its
        /// own for SQLite. Created and dropped outside any scope, with its own repository.
        /// </summary>
        private sealed class ScopeTable : IAsyncDisposable
        {
            private readonly string? _sqliteDirectory;

            private ScopeTable(DatabaseType databaseType, string connectionString, string name, string? sqliteDirectory)
            {
                DatabaseType = databaseType;
                ConnectionString = connectionString;
                Name = name;
                _sqliteDirectory = sqliteDirectory;
            }

            public DatabaseType DatabaseType { get; }
            public string ConnectionString { get; }
            public string Name { get; }

            public static async Task<ScopeTable> CreateAsync(DatabaseType dbType, string? serverConnectionString)
            {
                string connectionString;
                string? sqliteDirectory = null;

                if (dbType == DatabaseType.SQLLite)
                {
                    sqliteDirectory = Path.Combine(Path.GetTempPath(), "apilane-repository-scope", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(sqliteDirectory);

                    var path = Path.Combine(sqliteDirectory, "app.db");
                    SQLiteDataStorageRepository.GenerateDatabase(path);

                    // Same connection string shape as ApplicationExtensions.GetConnectionstring
                    connectionString = $"Data Source={path};Cache Size=2000;Version=3;FailIfMissing=True;foreign keys=true;";
                }
                else
                {
                    connectionString = serverConnectionString
                        ?? throw new InvalidOperationException($"No connection string for {dbType}");
                }

                var table = new ScopeTable(dbType, connectionString, "Items_" + Guid.NewGuid().ToString("N"), sqliteDirectory);

                using var setup = table.NewRepository();
                await setup.CreateTableWithPrimaryKeyAsync(table.Name);
                await setup.CreateColumnAsync(table.Name, "Name", PropertyType.String, notNull: true, numDecimalPlaces: null, strMaxLength: 100);

                return table;
            }

            /// <summary>A repository with a connection of its own, as the API creates one per request.</summary>
            public IDataStorageRepository NewRepository()
            {
                return CreateRepository(DatabaseType, ConnectionString);
            }

            public async Task<long> InsertAsync(IDataStorageRepository repository, string name)
            {
                var id = await repository.CreateDataAsync(Name, new Dictionary<string, object?> { ["Name"] = name }, allowInsertIdentity: false);

                return id ?? throw new InvalidOperationException("The insert returned no ID");
            }

            /// <summary>An INSERT statement for custom SQL; <paramref name="nameLiteral"/> is 'abc' or NULL.</summary>
            public string InsertSql(string nameLiteral)
            {
                return $"INSERT INTO {SqlUtilis.QuoteIdentifier(Name, DatabaseType)} ({SqlUtilis.QuoteIdentifier("Name", DatabaseType)}) VALUES ({nameLiteral});";
            }

            /// <summary>An UPDATE statement for custom SQL that sets the Name of one row.</summary>
            public string UpdateSql(long id, string name)
            {
                return $"UPDATE {SqlUtilis.QuoteIdentifier(Name, DatabaseType)} SET {SqlUtilis.QuoteIdentifier("Name", DatabaseType)} = '{name}' WHERE {SqlUtilis.QuoteIdentifier(Globals.PrimaryKeyColumn, DatabaseType)} = {id};";
            }

            public Task<long> CountAsync(IDataStorageRepository repository)
            {
                return repository.GetDataCountAsync(Name, null);
            }

            /// <summary>The rows that are really in the table, read through a new repository (a new connection).</summary>
            public async Task<long> CommittedCountAsync()
            {
                using var repository = NewRepository();

                return await CountAsync(repository);
            }

            public async Task<List<string>> CommittedNamesAsync()
            {
                using var repository = NewRepository();
                var rows = await repository.GetPagedDataAsync(Name, null, null, null, 1, 100);

                return rows
                    .Select(row => row["Name"]?.ToString() ?? string.Empty)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }

            public async ValueTask DisposeAsync()
            {
                if (_sqliteDirectory is not null)
                {
                    SQLiteConnection.ClearAllPools();

                    try
                    {
                        Directory.Delete(_sqliteDirectory, recursive: true);
                    }
                    catch (IOException)
                    {
                        // Best effort: a file handle may outlive the test on some platforms, and the file is in the temp folder
                    }

                    return;
                }

                using var repository = NewRepository();
                await repository.DropTableAsync(Name);
            }
        }

        // ─── The scope covers the repository's commands ──────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Scope_Opened_Before_First_Use_Not_Completed_Should_Roll_Back(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                // The connection is opened by the first command, inside the scope
                using (NewScope())
                {
                    await table.InsertAsync(repository, "a");
                    await table.InsertAsync(repository, "b");
                }
            }

            Assert.Equal(0, await table.CommittedCountAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Scope_Opened_After_A_Read_Not_Completed_Should_Roll_Back(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                // The connection is open before the scope starts
                await table.CountAsync(repository);

                using (NewScope())
                {
                    await table.InsertAsync(repository, "a");
                    await table.InsertAsync(repository, "b");
                }
            }

            Assert.Equal(0, await table.CommittedCountAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Scope_Opened_After_A_Read_Completed_Should_Commit(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (var scope = NewScope())
                {
                    await table.InsertAsync(repository, "a");

                    // The repository sees its own work inside the scope
                    Assert.Equal(1, await table.CountAsync(repository));

                    scope.Complete();
                }
            }

            Assert.Equal(new[] { "a" }, await table.CommittedNamesAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Nested_Scope_Completed_Inside_An_Abandoned_One_Should_Roll_Back(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (NewScope())
                {
                    await table.InsertAsync(repository, "outer");

                    // As a custom endpoint called inside a batch transaction does
                    using (var inner = NewScope())
                    {
                        await table.InsertAsync(repository, "inner");
                        inner.Complete();
                    }
                }
            }

            Assert.Equal(0, await table.CommittedCountAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Repository_After_An_Abandoned_Scope_Should_Work_And_Join_The_Next_Scope(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (NewScope())
                {
                    await table.InsertAsync(repository, "undone");
                }

                // Outside any scope: saved at once
                await table.InsertAsync(repository, "kept");

                using (NewScope())
                {
                    await table.InsertAsync(repository, "undone again");
                }

                using (var scope = NewScope())
                {
                    await table.InsertAsync(repository, "kept too");
                    scope.Complete();
                }
            }

            Assert.Equal(new[] { "kept", "kept too" }, await table.CommittedNamesAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Every_Kind_Of_Command_In_An_Abandoned_Scope_Should_Roll_Back(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            long seed1;
            long seed2;

            using (var setup = table.NewRepository())
            {
                seed1 = await table.InsertAsync(setup, "seed1");
                seed2 = await table.InsertAsync(setup, "seed2");
            }

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (NewScope())
                {
                    // A scalar command, two non-queries, a data adapter, a reader and a paged read
                    await table.InsertAsync(repository, "created");
                    await repository.UpdateDataAsync(table.Name, new Dictionary<string, object?> { ["Name"] = "updated" }, ById(seed1));
                    await repository.DeleteDataAsync(table.Name, ById(seed2));
                    await repository.ExecuteCustomAsync(table.InsertSql("'custom'"));

                    Assert.Equal(3, await table.CountAsync(repository));
                    Assert.Equal(3, (await repository.GetPagedDataAsync(table.Name, null, null, null, 1, 100)).Count);
                }
            }

            Assert.Equal(new[] { "seed1", "seed2" }, await table.CommittedNamesAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Failing_Multi_Statement_Command_In_A_Scope_Should_Roll_Back_The_Earlier_Statements(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (NewScope())
                {
                    // As a custom endpoint runs: the second statement breaks the NOT NULL rule of Name.
                    // (PostgreSQL runs the statements of one command as one implicit transaction, so for
                    // it this passes even when the scope does not cover the connection.)
                    await Assert.ThrowsAnyAsync<Exception>(
                        () => repository.ExecuteCustomAsync($"{table.InsertSql("'a'")} {table.InsertSql("NULL")}"));
                }
            }

            Assert.Equal(0, await table.CommittedCountAsync());
        }

        /// <summary>
        /// Why the PostgreSQL row of the test above passes without a scope that covers the connection: Npgsql
        /// sends the statements of one command together and PostgreSQL runs them as one implicit transaction,
        /// so a failing statement undoes the ones before it. SQL Server, MySQL and SQLite run them one by one.
        /// </summary>
        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Failing_Multi_Statement_Command_Without_A_Scope_Is_Atomic_Only_On_PostgreSQL(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                await Assert.ThrowsAnyAsync<Exception>(
                    () => repository.ExecuteCustomAsync($"{table.InsertSql("'a'")} {table.InsertSql("NULL")}"));
            }

            Assert.Equal(dbType == DatabaseType.PostgreSQL ? 0 : 1, await table.CommittedCountAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Enlisted_Transaction_Should_Not_Be_Promoted_To_A_Distributed_Transaction(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (NewScope())
                {
                    await table.InsertAsync(repository, "a");

                    // A distributed transaction needs MSDTC, which does not exist on Linux
                    var transaction = Transaction.Current ?? throw new InvalidOperationException("No ambient transaction");
                    Assert.Equal(Guid.Empty, transaction.TransactionInformation.DistributedIdentifier);
                }
            }
        }

        // ─── MySQL with XA transactions ───────────────────────────────────────────
        // The containers' connection string turns XA off (UseXaTransactions=false), which the Portal and the
        // storage providers page of the documentation tell MySQL users to do. MySqlConnector's default is on,
        // so an application that did not add it runs this way.

        private async Task<ScopeTable> NewMySqlXaTableAsync()
        {
            var connectionString = await SuiteContext.Shared.Databases.GetConnectionStringAsync(DatabaseType.MySQL, GetDatabaseName(GetType()))
                ?? throw new InvalidOperationException("No MySQL connection string");

            const string xaOff = "UseXaTransactions=false;";
            Assert.Contains(xaOff, connectionString);

            return await ScopeTable.CreateAsync(DatabaseType.MySQL, connectionString.Replace(xaOff, "UseXaTransactions=true;"));
        }

        [Fact]
        public async Task Scope_Opened_Before_First_Use_Not_Completed_Should_Roll_Back_On_MySql_With_Xa_Transactions()
        {
            await using var table = await NewMySqlXaTableAsync();

            using (var repository = table.NewRepository())
            {
                using (NewScope())
                {
                    await table.InsertAsync(repository, "a");
                    await table.InsertAsync(repository, "b");
                }
            }

            Assert.Equal(0, await table.CommittedCountAsync());
        }

        [Fact]
        public async Task Scope_Opened_After_A_Read_Not_Completed_Should_Roll_Back_On_MySql_With_Xa_Transactions()
        {
            await using var table = await NewMySqlXaTableAsync();

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (NewScope())
                {
                    await table.InsertAsync(repository, "a");
                    await table.InsertAsync(repository, "b");
                }
            }

            Assert.Equal(0, await table.CommittedCountAsync());
        }

        // ─── A scope that ended is refused ────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Aborted_Scope_Should_Refuse_Further_Commands(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (NewScope())
                {
                    await table.InsertAsync(repository, "a");

                    var transaction = Transaction.Current ?? throw new InvalidOperationException("No ambient transaction");
                    transaction.Rollback();

                    // The transaction is over: a command must not run on the connection outside of it
                    await Assert.ThrowsAsync<TransactionAbortedException>(() => table.InsertAsync(repository, "b"));
                }

                // Outside the scope the repository works again
                await table.InsertAsync(repository, "after");
            }

            Assert.Equal(new[] { "after" }, await table.CommittedNamesAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Timed_Out_Scope_Should_Refuse_Further_Commands(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                // Long enough for the first command to run well inside the scope, on a loaded machine too
                using (NewScope(TimeSpan.FromSeconds(3)))
                {
                    await table.InsertAsync(repository, "a");

                    // Wait for the scope to time out, between commands
                    var transaction = Transaction.Current ?? throw new InvalidOperationException("No ambient transaction");
                    var deadline = DateTime.UtcNow.AddSeconds(15);

                    while (transaction.TransactionInformation.Status == TransactionStatus.Active && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(100);
                    }

                    Assert.NotEqual(TransactionStatus.Active, transaction.TransactionInformation.Status);

                    await Assert.ThrowsAsync<TransactionAbortedException>(() => table.InsertAsync(repository, "b"));
                }
            }

            Assert.Equal(0, await table.CommittedCountAsync());
        }

        /// <summary>
        /// When a scope times out while a command is still running, MySqlConnector rolls the transaction back on
        /// the timer thread of System.Transactions, on the very connection the command is using, and the
        /// exception that this throws is not caught: it ends the whole API process. The MySQL repository must
        /// therefore stop a command before the scope times out, so the request fails with an error instead.
        /// Here the command waits for a row lock that another session holds, as a slow write would.
        /// </summary>
        [Fact]
        public async Task Command_Waiting_On_A_Lock_When_The_Scope_Times_Out_Should_Fail_On_MySql()
        {
            await using var table = await NewTableAsync(DatabaseType.MySQL);

            using var blocker = table.NewRepository();
            var id = await table.InsertAsync(blocker, "locked");

            // Another session holds the lock of the row until it ends
            await blocker.ExecuteCustomAsync("START TRANSACTION;");
            await blocker.ExecuteCustomAsync(table.UpdateSql(id, "held"));

            using (var repository = table.NewRepository())
            {
                await table.CountAsync(repository);

                using (NewScope(TimeSpan.FromSeconds(4)))
                {
                    await Assert.ThrowsAsync<MySqlException>(() => repository.ExecuteCustomAsync(table.UpdateSql(id, "waiting")));
                }
            }

            await blocker.ExecuteCustomAsync("ROLLBACK;");

            Assert.Equal(new[] { "locked" }, await table.CommittedNamesAsync());
        }

        // ─── Schema changes ───────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(TransactionalDdlTestData))]
        public async Task Schema_Change_In_An_Abandoned_Scope_Should_Roll_Back(DatabaseType dbType)
        {
            await using var table = await NewTableAsync(dbType);

            var otherTable = "Ddl_" + Guid.NewGuid().ToString("N");

            try
            {
                using (var repository = table.NewRepository())
                {
                    await table.CountAsync(repository);

                    using (NewScope())
                    {
                        await repository.CreateTableWithPrimaryKeyAsync(otherTable);
                    }
                }

                using var verify = table.NewRepository();
                Assert.False(await verify.ExistsTableAsync(otherTable), "The table was created for good although the scope was abandoned");
            }
            finally
            {
                using var cleanup = table.NewRepository();

                if (await cleanup.ExistsTableAsync(otherTable))
                {
                    await cleanup.DropTableAsync(otherTable);
                }
            }
        }

        /// <summary>
        /// Not a wish but a fact about MySQL, kept here so that nobody expects the schema flows of the API to be
        /// atomic on it: a schema change commits implicitly, so an abandoned scope cannot undo it.
        /// </summary>
        [Fact]
        public async Task Schema_Change_In_An_Abandoned_Scope_On_MySql_Is_Committed_Implicitly()
        {
            await using var table = await NewTableAsync(DatabaseType.MySQL);

            var otherTable = "Ddl_" + Guid.NewGuid().ToString("N");

            try
            {
                using (var repository = table.NewRepository())
                {
                    await table.CountAsync(repository);

                    using (NewScope())
                    {
                        await repository.CreateTableWithPrimaryKeyAsync(otherTable);
                    }
                }

                using var verify = table.NewRepository();
                Assert.True(await verify.ExistsTableAsync(otherTable), "MySQL is expected to commit a schema change on its own");
            }
            finally
            {
                using var cleanup = table.NewRepository();

                if (await cleanup.ExistsTableAsync(otherTable))
                {
                    await cleanup.DropTableAsync(otherTable);
                }
            }
        }
    }
}
