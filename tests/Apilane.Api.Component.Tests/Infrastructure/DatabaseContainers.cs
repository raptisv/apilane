using Apilane.Common.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace CasinoService.ComponentTests.Infrastructure
{
    /// <summary>
    /// SQL Server, MySQL and PostgreSQL for the component tests, each in a throwaway Docker container
    /// (Testcontainers), so nothing but Docker has to be installed or running. The containers start
    /// together the first time a test asks for a server database and listen on random host ports.
    /// Test classes run in parallel, so each one has its own database on these servers. Testcontainers'
    /// resource reaper removes the containers when the test process exits. SQLite needs no server.
    /// </summary>
    public sealed class DatabaseContainers
    {
        private const string Password = "Apilane_Tests_1!";

        private readonly IReadOnlyCollection<string> _databaseNames;
        private readonly Lazy<Task> _sqlServerReady;
        private readonly Lazy<Task> _mySqlReady;
        private readonly Lazy<Task> _postgreSqlReady;

        private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword(Password)
            .Build();

        // The tests create and drop tables constantly, and durable disk writes make that very slow in a
        // container. Test data does not have to survive a crash, so keep it in memory and skip the
        // binary log and the flush on every commit. Turning the dedicated log writer threads off makes
        // schema changes about 40% faster (measured). Every test class has its own connection pool, hence
        // the higher connection limit.
        private readonly MySqlContainer _mySql = new MySqlBuilder("mysql:8.0")
            .WithUsername("root")
            .WithPassword(Password)
            .WithTmpfsMount("/var/lib/mysql")
            .WithCommand("--skip-log-bin", "--innodb-flush-log-at-trx-commit=0", "--innodb-doublewrite=0", "--innodb-log-writer-threads=OFF", "--max-connections=500")
            .Build();

        private readonly PostgreSqlContainer _postgreSql = new PostgreSqlBuilder("postgres:17-alpine")
            .WithUsername("postgres")
            .WithPassword(Password)
            .WithCommand("-c", "max_connections=300")
            .Build();

        /// <param name="databaseNames">
        /// The databases to create on every server, one per test class. They are created up front, in one
        /// script per server: the API only connects to an application's database, it never creates it.
        /// </param>
        public DatabaseContainers(IEnumerable<string> databaseNames)
        {
            _databaseNames = databaseNames.ToList();

            _sqlServerReady = new Lazy<Task>(StartSqlServerAsync);
            _mySqlReady = new Lazy<Task>(StartMySqlAsync);
            _postgreSqlReady = new Lazy<Task>(StartPostgreSqlAsync);
        }

        /// <summary>
        /// Returns a connection string to the given database on the server of the given type, starting the
        /// containers on first use. Returns null for SQLite, which uses a local file.
        /// </summary>
        public async Task<string?> GetConnectionStringAsync(DatabaseType databaseType, string databaseName)
        {
            if (databaseType == DatabaseType.SQLLite)
            {
                return null;
            }

            if (!_databaseNames.Contains(databaseName))
            {
                throw new ArgumentException($"Database '{databaseName}' is not one of the test databases", nameof(databaseName));
            }

            // A full run needs all three servers, so start them side by side rather than one after the other
            var sqlServerReady = _sqlServerReady.Value;
            var mySqlReady = _mySqlReady.Value;
            var postgreSqlReady = _postgreSqlReady.Value;

            switch (databaseType)
            {
                case DatabaseType.SQLServer:
                    await sqlServerReady;
                    return $"Server={_sqlServer.Hostname},{_sqlServer.GetMappedPublicPort(MsSqlBuilder.MsSqlPort)};Database={databaseName};User Id=sa;Password={Password};TrustServerCertificate=true;";
                case DatabaseType.MySQL:
                    await mySqlReady;
                    return $"Server={_mySql.Hostname};Port={_mySql.GetMappedPublicPort(MySqlBuilder.MySqlPort)};Uid=root;Pwd={Password};Database={MySqlName(databaseName)};UseXaTransactions=false;";
                case DatabaseType.PostgreSQL:
                    await postgreSqlReady;
                    return $"Server={_postgreSql.Hostname};Port={_postgreSql.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort)};Database={databaseName};User Id=postgres;Password={Password};";
                default:
                    throw new NotSupportedException($"No container for database type {databaseType}");
            }
        }

        // MySQL database names are case-sensitive on Linux; keep them lower case
        private static string MySqlName(string databaseName)
        {
            return databaseName.ToLowerInvariant();
        }

        private async Task StartSqlServerAsync()
        {
            await _sqlServer.StartAsync();

            // Delayed durability: commits do not wait for the log to reach the disk. Fine for test data.
            var script = string.Join(Environment.NewLine, _databaseNames.Select(name =>
                $"CREATE DATABASE [{name}]; ALTER DATABASE [{name}] SET DELAYED_DURABILITY = FORCED;"));

            var result = await _sqlServer.ExecScriptAsync(script);

            if (result.ExitCode != 0)
            {
                throw new Exception($"Could not create the SQL Server test databases | {result.Stderr}");
            }
        }

        private async Task StartMySqlAsync()
        {
            await _mySql.StartAsync();

            var script = string.Join(Environment.NewLine, _databaseNames.Select(name => $"CREATE DATABASE `{MySqlName(name)}`;"));

            var result = await _mySql.ExecScriptAsync(script);

            if (result.ExitCode != 0)
            {
                throw new Exception($"Could not create the MySQL test databases | {result.Stderr}");
            }
        }

        private async Task StartPostgreSqlAsync()
        {
            await _postgreSql.StartAsync();

            var script = string.Join(Environment.NewLine, _databaseNames.Select(name => $"CREATE DATABASE \"{name}\";"));

            var result = await _postgreSql.ExecScriptAsync(script);

            if (result.ExitCode != 0)
            {
                throw new Exception($"Could not create the PostgreSQL test databases | {result.Stderr}");
            }
        }
    }
}
