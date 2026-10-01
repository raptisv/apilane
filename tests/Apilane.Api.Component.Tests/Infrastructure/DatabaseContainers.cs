using Apilane.Common.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace CasinoService.ComponentTests.Infrastructure
{
    /// <summary>
    /// SQL Server, MySQL and PostgreSQL for the component tests, each in a throwaway Docker container
    /// (Testcontainers), so nothing but Docker has to be installed or running. The containers start
    /// together the first time a test asks for a server database, listen on random host ports, and are
    /// removed when the test run ends. SQLite needs no server.
    /// </summary>
    public sealed class DatabaseContainers : IAsyncDisposable
    {
        private const string Password = "Apilane_Tests_1!";

        private readonly object _startLock = new();
        private Dictionary<DatabaseType, Task<string>>? _connectionStrings;

        private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword(Password)
            .Build();

        // The application database must already exist: the API only connects to it, it never creates it.
        // The tests create and drop tables constantly, and durable disk writes make that very slow in a
        // container. Test data does not have to survive a crash, so keep it in memory and skip the
        // binary log and the flush on every commit.
        private readonly MySqlContainer _mySql = new MySqlBuilder("mysql:8.0")
            .WithUsername("root")
            .WithPassword(Password)
            .WithDatabase("testapp")
            .WithTmpfsMount("/var/lib/mysql")
            .WithCommand("--skip-log-bin", "--innodb-flush-log-at-trx-commit=0", "--innodb-doublewrite=0")
            .Build();

        private readonly PostgreSqlContainer _postgreSql = new PostgreSqlBuilder("postgres:17-alpine")
            .WithUsername("postgres")
            .WithPassword(Password)
            .WithDatabase("TestApp")
            .Build();

        /// <summary>
        /// Returns the connection string of the test database for the given type, starting the containers
        /// on first use. Returns null for SQLite, which uses a local file.
        /// </summary>
        public async Task<string?> GetConnectionStringAsync(DatabaseType databaseType)
        {
            if (databaseType == DatabaseType.SQLLite)
            {
                return null;
            }

            Task<string> connectionString;

            lock (_startLock)
            {
                // A full run needs all three, so start them side by side rather than one after the other.
                _connectionStrings ??= new Dictionary<DatabaseType, Task<string>>()
                {
                    [DatabaseType.SQLServer] = StartSqlServerAsync(),
                    [DatabaseType.MySQL] = StartMySqlAsync(),
                    [DatabaseType.PostgreSQL] = StartPostgreSqlAsync()
                };

                connectionString = _connectionStrings[databaseType];
            }

            return await connectionString;
        }

        private async Task<string> StartSqlServerAsync()
        {
            await _sqlServer.StartAsync();

            // Delayed durability: commits do not wait for the log to reach the disk. Fine for test data.
            var result = await _sqlServer.ExecScriptAsync("CREATE DATABASE [TestApp]; ALTER DATABASE [TestApp] SET DELAYED_DURABILITY = FORCED;");

            if (result.ExitCode != 0)
            {
                throw new Exception($"Could not create the SQL Server test database | {result.Stderr}");
            }

            return $"Server={_sqlServer.Hostname},{_sqlServer.GetMappedPublicPort(MsSqlBuilder.MsSqlPort)};Database=TestApp;User Id=sa;Password={Password};TrustServerCertificate=true;";
        }

        private async Task<string> StartMySqlAsync()
        {
            await _mySql.StartAsync();

            return $"Server={_mySql.Hostname};Port={_mySql.GetMappedPublicPort(MySqlBuilder.MySqlPort)};Uid=root;Pwd={Password};Database=testapp;UseXaTransactions=false;";
        }

        private async Task<string> StartPostgreSqlAsync()
        {
            await _postgreSql.StartAsync();

            return $"Server={_postgreSql.Hostname};Port={_postgreSql.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort)};Database=TestApp;User Id=postgres;Password={Password};";
        }

        public async ValueTask DisposeAsync()
        {
            await _sqlServer.DisposeAsync();
            await _mySql.DisposeAsync();
            await _postgreSql.DisposeAsync();
        }
    }
}
