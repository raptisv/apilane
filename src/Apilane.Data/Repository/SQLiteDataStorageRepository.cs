using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Data.Extensions;
using Apilane.Data.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Transactions;

namespace Apilane.Data.Repository
{
    public class SQLiteDataStorageRepository : IDataStorageRepository
    {
        public string _connectionString;
        private SQLiteConnection _databaseConnection = null!;

        public SQLiteDataStorageRepository(
            string connectionString)
        {
            _connectionString = connectionString;
        }

        private void TryOpenConnection()
        {
            _databaseConnection ??= new SQLiteConnection(_connectionString);

            if (_databaseConnection.State != ConnectionState.Open)
            {
                _databaseConnection.Open();

                try
                {
                    ConfigureOpenConnection(_databaseConnection);
                }
                catch
                {
                    // A connection that is open but not confined must never be used: a caller that swallows
                    // this error would otherwise carry on with it.
                    _databaseConnection.Close();
                    throw;
                }
            }

            // Every command goes through here, so every command runs in the current transaction scope, also
            // when the connection was opened before the scope began.
            TransactionEnlistment.JoinAmbientTransaction(_databaseConnection);
        }

        private void ConfigureOpenConnection(SQLiteConnection connection)
        {
            // Extensions are enabled only long enough to load FTS5. While they are enabled, SQL can call
            // load_extension() and load any native library into this process.
            connection.EnableExtensions(true);
            connection.LoadExtension(GetPathToSqliteInterop(), "sqlite3_fts5_init");
            connection.EnableExtensions(false);

            // Application owners write custom endpoint SQL that runs on this connection, so confine it to
            // its own database file: no attached databases (which also disables VACUUM INTO), and an
            // authorizer for the statements a limit cannot express.
            connection.SetLimitOption(SQLiteLimitOpsEnum.SQLITE_LIMIT_ATTACHED, 0);

            // Defensive mode: SQL cannot write raw pages (sqlite_dbpage), FTS shadow tables or the schema
            // table, so it cannot craft a deliberately corrupt database for the engine to parse.
            connection.SetConfigurationOption(SQLiteConfigDbOpsEnum.SQLITE_DBCONFIG_DEFENSIVE, true);

            connection.Authorize -= AuthorizeStatement;
            connection.Authorize += AuthorizeStatement;
        }

        /// <summary>
        /// PRAGMA statements that stay available: foreign_keys (this repository sets it on every command),
        /// data_version and page_size (the full-text and R-Tree modules read them internally) and read-only
        /// schema introspection. Every other PRAGMA is
        /// denied, because several of them reach beyond the database (temp_store_directory), change the
        /// whole process (hard_heap_limit) or let the schema be rewritten (writable_schema).
        /// </summary>
        private static readonly HashSet<string> _allowedPragmas = new(StringComparer.OrdinalIgnoreCase)
        {
            "foreign_keys",
            "data_version",
            "page_size",
            "table_info",
            "table_xinfo",
            "table_list",
            "index_list",
            "index_info",
            "index_xinfo",
            "foreign_key_list",
            "foreign_key_check"
        };

        /// <summary>
        /// Virtual table modules that may be created: full-text search and R-Tree, which only use the
        /// database itself. Modules that read or write other files (csv, zipfile, ...) are not part of the
        /// bundled SQLite build today; the allow list keeps it that way if the build ever changes.
        /// </summary>
        private static readonly HashSet<string> _allowedVirtualTableModules = new(StringComparer.OrdinalIgnoreCase)
        {
            "fts5",
            "fts5vocab",
            "fts4",
            "fts3",
            "rtree"
        };

        /// <summary>
        /// Functions that load code or touch the file system. Only load_extension exists in the bundled
        /// build (and is disabled above); the others are denied by name for the same reason as the modules.
        /// </summary>
        private static readonly HashSet<string> _deniedFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "load_extension",
            "readfile",
            "writefile",
            "edit",
            "fts3_tokenizer"
        };

        /// <summary>
        /// SQLite calls this while compiling every statement. Denying an action makes the statement fail
        /// with "not authorized" before it runs.
        /// </summary>
        private static void AuthorizeStatement(object sender, AuthorizerEventArgs e)
        {
            switch (e.ActionCode)
            {
                case SQLiteAuthorizerActionCode.Attach:
                case SQLiteAuthorizerActionCode.Detach:
                    e.ReturnCode = SQLiteAuthorizerReturnCode.Deny;
                    break;
                case SQLiteAuthorizerActionCode.Pragma:
                    if (!_allowedPragmas.Contains(e.Argument1 ?? string.Empty))
                    {
                        e.ReturnCode = SQLiteAuthorizerReturnCode.Deny;
                    }
                    break;
                case SQLiteAuthorizerActionCode.Function:
                    if (_deniedFunctions.Contains(e.Argument2 ?? string.Empty))
                    {
                        e.ReturnCode = SQLiteAuthorizerReturnCode.Deny;
                    }
                    break;
                case SQLiteAuthorizerActionCode.CreateVtable:
                    if (!_allowedVirtualTableModules.Contains(e.Argument2 ?? string.Empty))
                    {
                        e.ReturnCode = SQLiteAuthorizerReturnCode.Deny;
                    }
                    break;
            }
        }

        private static string GetPathToSqliteInterop()
        {
            const string fileName = "SQLite.Interop.dll";

            // Can be overriden by this environment variable
            var customPathToSqliteInterop = Environment.GetEnvironmentVariable("CUSTOM_PATH_TO_SQLITE_INTEROP");
            if (!string.IsNullOrWhiteSpace(customPathToSqliteInterop))
            {
                return Path.Combine(customPathToSqliteInterop, fileName);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // A publish for one runtime (the Docker images) puts the library next to the application;
                // a portable build keeps it in the package's runtimes folder.
                var nextToApplication = Path.Combine(AppContext.BaseDirectory, fileName);
                if (File.Exists(nextToApplication))
                {
                    return nextToApplication;
                }

                var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                return Path.Combine(AppContext.BaseDirectory, "runtimes", $"linux-{architecture}", "native", fileName);
            }

            return fileName;
        }

        public ValueTask DisposeAsync()
        {
            if (_databaseConnection is not null &&
                _databaseConnection.State != ConnectionState.Closed)
            {
                _databaseConnection.Close();
            }

            GC.SuppressFinalize(this);

            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_databaseConnection is not null &&
                _databaseConnection.State != ConnectionState.Closed)
            {
                _databaseConnection.Close();
            }

            GC.SuppressFinalize(this);
        }

        private async Task<object?> ExecScalarAsync(string cmd)
        {
            TryOpenConnection();

            using (SQLiteCommand command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(command.CommandTimeout);
                command.CommandType = CommandType.Text;
                command.CommandText = $"PRAGMA foreign_keys=on;{cmd}";
                return await command.ExecuteScalarAsync();
            }
        }

        public async Task<int> ExecNQAsync(string cmd)
        {
            TryOpenConnection();

            if (!string.IsNullOrWhiteSpace(cmd))
            {
                using (SQLiteCommand command = new SQLiteCommand(_databaseConnection))
                {
                    command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(command.CommandTimeout);
                    command.CommandType = CommandType.Text;
                    command.CommandText = $"PRAGMA foreign_keys=on;{cmd}";
                    return await command.ExecuteNonQueryAsync();
                }
            }

            return 0;
        }

        private SQLiteDataAdapter GetLiteAdapter()
        {
            return new SQLiteDataAdapter(string.Empty, _databaseConnection);
        }

        public Task<DataTable> ExecTableAsync(string cmd)
        {
            TryOpenConnection();

            var adapter = GetLiteAdapter();
            var datatable = new DataTable();
            using (SQLiteCommand command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(command.CommandTimeout);
                command.CommandType = CommandType.Text;
                command.CommandText = $"PRAGMA foreign_keys=on;{cmd}";
                adapter.SelectCommand = command;
                adapter.Fill(datatable);
            }

            return Task.FromResult(datatable);
        }

        public Task<DataTableCollection> ExecTablesAsync(string cmd)
        {
            TryOpenConnection();

            SQLiteDataAdapter adapter = GetLiteAdapter();
            DataSet dataSet = new DataSet();
            DataTable result = new DataTable();
            using (SQLiteCommand command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(command.CommandTimeout);
                command.CommandText = $"PRAGMA foreign_keys=on;{cmd}";
                adapter.SelectCommand = command;
                adapter.Fill(dataSet);
            }

            return Task.FromResult(dataSet.Tables);
        }

        private Task<DataTable> ExecPagingAsync(
            string tables,
            string primaryKey,
            string fields = "*",
            int pageSize = -1,
            int pageIndex = -1,
            string? filter = null,
            string? sort = null,
            string? group = null)
        {
            TryOpenConnection();

            var cmdData = string.Format("SELECT {0} FROM {1} {2} {3} {4} {5}",
                fields,
                tables,
                (string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {filter} COLLATE NOCASE"),
                string.IsNullOrWhiteSpace(group) ? string.Empty : $"GROUP BY {group}",
                SqlUtilis.GetPagingOrderBy(sort, $"[{primaryKey}]"),
                (pageSize <= 0 || pageIndex <= 0) ? string.Empty : $"LIMIT {pageSize} OFFSET {SqlUtilis.GetPagingOffset(pageIndex, pageSize)} ");

            using (var command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(command.CommandTimeout);
                var adapter = GetLiteAdapter();
                command.CommandText = cmdData;
                adapter.SelectCommand = command;
                var dataSet = new DataSet();
                adapter.Fill(dataSet);

                return Task.FromResult(dataSet.Tables[0]);
            }
        }

        private Task<long> ExecCountAsync(string table, string primaryKey, string? filter = null, string? group = null)
        {
            TryOpenConnection();

            var cmdTotal = string.Format("SELECT COUNT({0}) FROM {1} {2} {3}",
                primaryKey,
                table,
                string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {filter} COLLATE NOCASE",
                string.IsNullOrWhiteSpace(group) ? string.Empty : $"GROUP BY {group}");

            using (var command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(command.CommandTimeout);
                command.CommandType = CommandType.Text;
                command.CommandText = cmdTotal;
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return Task.FromResult(Utils.GetLong(reader[0]));
                    }
                }
            }

            return Task.FromResult((long)0);
        }

        public static void GenerateDatabase(string path)
        {
            SQLiteConnection.CreateFile(path);

            /* Consider using this right after creating the database
                PRAGMA main.page_size = 4096;
                PRAGMA main.cache_size=10000;
                PRAGMA main.locking_mode=EXCLUSIVE;
                PRAGMA main.synchronous=NORMAL;
                PRAGMA main.journal_mode=WAL;
                PRAGMA main.cache_size=5000;
            */
        }

        public Task CreateTableWithPrimaryKeyAsync(string tableName)
        {
            return ExecNQAsync($@"CREATE TABLE [{tableName}] 
                                ( 
                                    [{Globals.PrimaryKeyColumn}] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT
                                );");
        }

        public async Task RenameTableAsync(string oldTableName, string newTableName, List<EntityConstraint> constraints)
        {
            var cmd = $@"ALTER TABLE [{oldTableName}] RENAME TO [{newTableName}];";

            // The names of the unique indexes carry the entity name, and a later save of the constraints
            // drops and creates them by that name: rename them with the table. SQLite has no RENAME INDEX.
            // An index is renamed only when it exists and its new name is free (names are database-wide).
            // Foreign keys need nothing: SQLite never looks a foreign key up by its name.
            var indexes = (await ExecTableAsync("SELECT name, tbl_name FROM sqlite_master WHERE type = 'index';")).Rows.Cast<DataRow>()
                .Select(row => (Name: Convert.ToString(row["name"]) ?? string.Empty, Table: Convert.ToString(row["tbl_name"]) ?? string.Empty))
                .ToList();

            foreach (var constraint in constraints.Where(x => x.TypeID == (int)ConstraintType.Unique))
            {
                var properties = constraint.GetUniqueProperties();
                if (properties.Count == 0)
                {
                    continue;
                }

                var oldName = GetUniqueConstraintName(oldTableName, properties);
                var newName = GetUniqueConstraintName(newTableName, properties);

                if (indexes.Any(x => x.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase) && x.Table.Equals(oldTableName, StringComparison.OrdinalIgnoreCase)) &&
                    !indexes.Any(x => x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                {
                    cmd += $"DROP INDEX {oldName};CREATE UNIQUE INDEX {newName} ON [{newTableName}]({string.Join(",", properties.Select(x => $"[{x}]"))});";

                    indexes.RemoveAll(x => x.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
                    indexes.Add((newName, newTableName));
                }
            }

            await ExecNQAsync(cmd);
        }

        public Task DropTableAsync(string tableName)
        {
            return ExecNQAsync($"DROP TABLE [{tableName}];");
        }

        public async Task<bool> ExistsTableAsync(string tableName)
        {
            return (await GetColumnNamesAsync(tableName)).Count > 0;
        }

        public async Task<bool> ExistsColumnAsync(string tableName, string columnName)
        {
            return (await GetColumnNamesAsync(tableName)).Contains(columnName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The columns of a table, read from the schema; none when the table does not exist. An error (a
        /// locked database, a connection that cannot be set up) is thrown, never answered as "missing".
        /// </summary>
        private async Task<List<string>> GetColumnNamesAsync(string tableName)
        {
            var columns = await ExecTableAsync($"PRAGMA table_info('{tableName.Replace("'", "''")}');");

            return columns.Rows.Cast<DataRow>()
                .Select(row => Convert.ToString(row["name"]) ?? string.Empty)
                .ToList();
        }

        public Task CreateColumnsAsync(
            string tableName,
            List<(string Name, PropertyType Type, bool NotNull, int? NumDecimalPlaces, long? StrMaxLength)> columns)
        {
            return ExecNQAsync(string.Concat(columns.Select(x =>
                $"ALTER TABLE [{tableName}] ADD {GetCreate(x.Name, x.Type, x.NotNull, x.NumDecimalPlaces)};")));
        }

        private static string GetCreate(
            string columnName,
            PropertyType type,
            bool notNull,
            int? numDecimalPlaces)
        {
            switch (type)
            {
                case PropertyType.Number:
                    if (!numDecimalPlaces.HasValue || numDecimalPlaces.Value == 0)
                    {
                        return $"[{columnName}] INTEGER {(notNull ? "DEFAULT 0 NOT NULL" : "NULL")}";
                    }
                    else
                    {
                        return $"[{columnName}] NUMERIC(18,{numDecimalPlaces.Value}) {(notNull ? "DEFAULT 0 NOT NULL" : "NULL")}";
                    }
                case PropertyType.Boolean:
                    return $"[{columnName}] BOOLEAN {(notNull ? "DEFAULT 0 NOT NULL" : "NULL")}";
                case PropertyType.Date:
                    return $"[{columnName}] BIGINT {(notNull ? "DEFAULT 0 NOT NULL" : "NULL")}";
                case PropertyType.String:
                    return $"[{columnName}] TEXT {(notNull ? "DEFAULT '' NOT NULL" : "NULL")}";
                default:
                    throw new NotImplementedException();
            }
        }

        public Task DropColumnAsync(string tableName, string columnName)
        {
            return ExecNQAsync($@"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}];");
        }

        public Task RenameColumnAsync(string tableName, string oldColumnName, string newColumnName)
        {
            return ExecNQAsync($"ALTER TABLE [{tableName}] RENAME COLUMN [{oldColumnName}] TO [{newColumnName}];");
        }

        public async Task SetConstraintsAsync(
            string tableName,
            List<(string Name, bool IsPrimaryKey, PropertyType Type, bool NotNull, int? NumDecimalPlaces)> tableColumns,
            List<EntityConstraint> incomingConstraints,
            List<EntityConstraint> currentConstraints)
        {
            // SQLite cannot add or drop a foreign key: when they change, the table is rebuilt with the new ones
            var rebuildCmd = GetForeignKeyConstraintsCommand(
                tableName,
                tableColumns,
                incomingConstraints.Where(x => x.TypeID == (int)ConstraintType.ForeignKey),
                currentConstraints.Where(x => x.TypeID == (int)ConstraintType.ForeignKey));

            var uniqueCmd = GetUniqueConstraintsCommand(
                tableName,
                incomingConstraints.Where(x => x.TypeID == (int)ConstraintType.Unique),
                currentConstraints.Where(x => x.TypeID == (int)ConstraintType.Unique));

            if (!string.IsNullOrWhiteSpace(rebuildCmd))
            {
                await RebuildTableAsync(tableName, rebuildCmd, uniqueCmd);
            }
            else if (!string.IsNullOrWhiteSpace(uniqueCmd))
            {
                await ExecNQAsync(uniqueCmd);
            }
        }

        /// <summary>
        /// Rebuilds a table to change its foreign keys, the way SQLite documents it
        /// (https://www.sqlite.org/lang_altertable.html#otheralter): foreign keys off, then in one transaction
        /// copy the rows to a new table, drop the old one, give the new one its name, re-create the indexes
        /// (the unique constraints) and triggers the drop removed, carry the AUTOINCREMENT high-water mark
        /// over, apply <paramref name="uniqueCmd"/> and check the foreign keys of the table; foreign keys back on.
        /// </summary>
        /// <remarks>
        /// With foreign keys on, DROP TABLE first deletes every row of the table. That runs the ON DELETE
        /// action of every table that references it (and of the copy, for a self-reference): their rows are
        /// deleted or set to NULL, or the drop fails. Foreign keys cannot be switched off inside a
        /// transaction, so the rebuild runs outside the ambient transaction scope, in a transaction of its own.
        /// <para>
        /// A connection that already takes part in a transaction (the table was created in it a moment ago:
        /// a new entity, a new or rebuilt application) rebuilds within it with foreign keys on. That is
        /// safe only while the table has no rows, so it is refused otherwise.
        /// </para>
        /// </remarks>
        private async Task RebuildTableAsync(string tableName, string rebuildCmd, string uniqueCmd)
        {
            using (var suppressScope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            {
                TryOpenConnection();

                var tableLiteral = tableName.Replace("'", "''");
                var ownTransaction = _databaseConnection.AutoCommit;

                if (!ownTransaction &&
                    (await ExecUnprefixedTableAsync($"SELECT 1 FROM [{tableName}] LIMIT 1;")).Rows.Count > 0)
                {
                    throw new InvalidOperationException($"The foreign keys of '{tableName}' cannot be changed in a transaction while it has records");
                }

                if (ownTransaction)
                {
                    await ExecUnprefixedNQAsync("PRAGMA foreign_keys=OFF;");
                }

                try
                {
                    if (ownTransaction)
                    {
                        await ExecUnprefixedNQAsync("BEGIN IMMEDIATE;");
                    }

                    // DROP TABLE removes the indexes and triggers of the table: keep their definitions
                    var schema = await ExecUnprefixedTableAsync($@"SELECT sql FROM sqlite_master
                                                                   WHERE tbl_name = '{tableLiteral}' COLLATE NOCASE
                                                                   AND type IN ('index', 'trigger') AND sql IS NOT NULL;");

                    var recreateCmd = string.Concat(schema.Rows.Cast<DataRow>().Select(row => $"{Convert.ToString(row["sql"])};"));

                    // The copy's AUTOINCREMENT starts after its largest ID: without the old high-water mark the
                    // IDs of deleted records, whose history is kept, would be handed out again.
                    var sequence = await ExecUnprefixedTableAsync($"SELECT seq FROM sqlite_sequence WHERE name = '{tableLiteral}' COLLATE NOCASE;");
                    var highWaterMark = sequence.Rows.Count > 0 ? Utils.GetNullLong(sequence.Rows[0]["seq"]) : null;

                    var sequenceCmd = highWaterMark is null
                        ? string.Empty
                        : $@"UPDATE sqlite_sequence SET seq = {highWaterMark.Value} WHERE name = '{tableLiteral}' AND seq < {highWaterMark.Value};
                             INSERT INTO sqlite_sequence (name, seq) SELECT '{tableLiteral}', {highWaterMark.Value}
                             WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = '{tableLiteral}');";

                    await ExecUnprefixedNQAsync(rebuildCmd + recreateCmd + sequenceCmd + uniqueCmd);

                    // With foreign keys off nothing checked the rows against a foreign key just added
                    var violations = await ExecUnprefixedTableAsync($"PRAGMA foreign_key_check('{tableLiteral}');");
                    if (violations.Rows.Count > 0)
                    {
                        // What adding the foreign key answers with foreign keys on, and on the other databases
                        throw new SQLiteException(SQLiteErrorCode.Constraint, "FOREIGN KEY constraint failed");
                    }

                    if (ownTransaction)
                    {
                        await ExecUnprefixedNQAsync("COMMIT;");
                    }
                }
                catch
                {
                    if (ownTransaction && !_databaseConnection.AutoCommit)
                    {
                        await ExecUnprefixedNQAsync("ROLLBACK;");
                    }

                    throw;
                }
                finally
                {
                    if (ownTransaction)
                    {
                        await ExecUnprefixedNQAsync("PRAGMA foreign_keys=ON;");
                    }
                }

                suppressScope.Complete();
            }
        }

        /// <summary>
        /// Runs a command as it is, on the open connection: without the <c>PRAGMA foreign_keys=on</c> every
        /// other command of this repository starts with, and without joining the ambient transaction.
        /// </summary>
        private async Task ExecUnprefixedNQAsync(string cmd)
        {
            using (var command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandType = CommandType.Text;
                command.CommandText = cmd;
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// The rows of a query run as it is: see <see cref="ExecUnprefixedNQAsync"/>.
        /// </summary>
        private Task<DataTable> ExecUnprefixedTableAsync(string cmd)
        {
            var adapter = GetLiteAdapter();
            var datatable = new DataTable();
            using (var command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandType = CommandType.Text;
                command.CommandText = cmd;
                adapter.SelectCommand = command;
                adapter.Fill(datatable);
            }

            return Task.FromResult(datatable);
        }

        private static string GetForeignKeyConstraintsCommand(
            string tableName,
            List<(string Name, bool IsPrimaryKey, PropertyType Type, bool NotNull, int? NumDecimalPlaces)> tableColumns,
            IEnumerable<EntityConstraint> incomingForeignKeyConstraints,
            IEnumerable<EntityConstraint> currentForeignKeyConstraints)
        {
            var finalCmd = string.Empty;
            var foreignKeysChanged = false;

            incomingForeignKeyConstraints = incomingForeignKeyConstraints.DistinctBy(x => string.Join("_", x.GetForeignKeyPropertiesAsList())).ToList();

            foreach (var currentConstraint in currentForeignKeyConstraints)
            {
                if (!incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", currentConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // Nothing to do here, just notify that we need to recreate the table.
                    foreignKeysChanged = true;
                    break;
                }
            }

            foreach (var incomingConstraint in incomingForeignKeyConstraints)
            {
                if (!currentForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", incomingConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // Nothing to do here, just notify that we need to recreate the table.          
                    foreignKeysChanged = true;
                    break;
                }
            }

            if (foreignKeysChanged)
            {
                var sqliteForeighKeysStringCreate = new List<string>();

                foreach (var currentConstraint in currentForeignKeyConstraints)
                {
                    if (incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", currentConstraint.GetForeignKeyPropertiesAsList()))))
                    {
                        // The constraint still exists, keep it
                        var constrainName = $"FOREIGN_KEY_{tableName}_{string.Join("_", currentConstraint.GetForeignKeyPropertiesAsList())}";
                        var properties = currentConstraint.GetForeignKeyProperties();
                        var constraintText = $"CONSTRAINT {constrainName} FOREIGN KEY ([{properties.Property}]) REFERENCES [{properties.FKEntity}]([ID]) {GetFKLogic(properties.FKLogic)}";
                        sqliteForeighKeysStringCreate.Add(constraintText);
                    }
                }

                foreach (var incomingConstraint in incomingForeignKeyConstraints)
                {
                    if (!currentForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", incomingConstraint.GetForeignKeyPropertiesAsList()))))
                    {
                        // The constraint does not exist, create it
                        var constrainName = $"FOREIGN_KEY_{tableName}_{string.Join("_", incomingConstraint.GetForeignKeyPropertiesAsList())}";
                        var properties = incomingConstraint.GetForeignKeyProperties();
                        var constraintText = $"CONSTRAINT {constrainName} FOREIGN KEY ([{properties.Property}]) REFERENCES [{properties.FKEntity}]([ID]) {GetFKLogic(properties.FKLogic)}";
                        sqliteForeighKeysStringCreate.Add(constraintText);
                    }
                }

                var propertiesString = tableColumns.Where(x => !x.IsPrimaryKey).Select(x => x.Name).ToList();
                var propertiesStringCreate = tableColumns.Where(x => !x.IsPrimaryKey).Select(x => GetCreate(x.Name, x.Type, x.NotNull, x.NumDecimalPlaces)).ToList();
                var fksCmd = sqliteForeighKeysStringCreate.Any() ? $",{string.Join(",", sqliteForeighKeysStringCreate)}" : string.Empty;
                var columnsCmd = $"[ID],{string.Join(",", propertiesString.Select(x => $"[{x}]"))}";

                // Run by RebuildTableAsync, which also re-creates what the DROP removes
                finalCmd += $@"DROP TABLE IF EXISTS [{tableName}__backup__];
                                CREATE TABLE [{tableName}__backup__]([ID] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,{string.Join(",", propertiesStringCreate)}{fksCmd});
                                INSERT INTO [{tableName}__backup__] ({columnsCmd}) SELECT {columnsCmd} FROM [{tableName}];
                                DROP TABLE [{tableName}];
                                ALTER TABLE [{tableName}__backup__] RENAME TO [{tableName}];";
            }

            return finalCmd;
        }

        private static string GetUniqueConstraintsCommand(
           string entityName,
           IEnumerable<EntityConstraint> incomingUniqueConstraints,
           IEnumerable<EntityConstraint> currentUniqueConstraints)
        {
            var finalCmd = string.Empty;

            incomingUniqueConstraints = incomingUniqueConstraints.DistinctBy(x => string.Join("_", x.GetUniqueProperties())).ToList();

            foreach (var currentConstraint in currentUniqueConstraints)
            {
                if (!incomingUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", currentConstraint.GetUniqueProperties()))))
                {
                    // The constraint is deleted, remove it from database
                    var constrainName = GetUniqueConstraintName(entityName, currentConstraint.GetUniqueProperties());

                    finalCmd += $"DROP INDEX IF EXISTS {constrainName};";
                }
            }

            foreach (var incomingConstraint in incomingUniqueConstraints)
            {
                if (!currentUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", incomingConstraint.GetUniqueProperties()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = GetUniqueConstraintName(entityName, incomingConstraint.GetUniqueProperties());
                    var properties = string.Join(",", incomingConstraint.GetUniqueProperties().Select(x => $"[{x}]"));

                    finalCmd += $"CREATE UNIQUE INDEX IF NOT EXISTS {constrainName} ON [{entityName}]({properties});";
                }
            }

            return finalCmd;
        }

        private static string GetUniqueConstraintName(string entityName, List<string> properties)
        {
            return $"UNIQUE_{entityName}_{string.Join("_", properties)}";
        }

        private static string GetFKLogic(ForeignKeyLogic logic) => logic switch
        {
            ForeignKeyLogic.ON_DELETE_CASCADE => "ON DELETE CASCADE ON UPDATE CASCADE",
            ForeignKeyLogic.ON_DELETE_SET_NULL => "ON DELETE SET NULL ON UPDATE SET NULL",
            _ => "ON DELETE NO ACTION ON UPDATE NO ACTION"
        };

        public async Task<Dictionary<string, object?>?> GetDataByIdAsync(
            string entityName,
            long id,
            List<string>? entityProperties)
        {
            var result = await ExecPagingAsync($"[{entityName}]", Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"[{x}]")),
                filter: $" [{entityName}].[{Globals.PrimaryKeyColumn}] = {id}",
                sort: null,
                pageIndex: 1,
                pageSize: 1);

            if (result.Rows.Count == 1)
            {
                return result.Rows[0].ToDictionary();
            }

            return null;
        }

        public async Task<List<Dictionary<string, object?>>> GetPagedDataAsync(
            string entityName,
            List<string>? entityProperties,
            FilterData? filter,
            List<SortData>? sort,
            int pageIndex,
            int pageSize)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);
            string? strSort = sort?.ToSqlExpression(DatabaseType.SQLLite);

            var resultData = await ExecPagingAsync($"[{entityName}]", Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"[{x}]")),
                pageIndex: pageIndex, pageSize: pageSize, filter: strFilter, sort: strSort);

            return resultData.ToDictionary();
        }

        public async Task<long> GetDataCountAsync(string entityName, FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);

            return await ExecCountAsync(
                $"[{entityName}]",
                Globals.PrimaryKeyColumn,
                filter: strFilter);
        }

        public async Task<long?> CreateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            bool allowInsertIdentity)
        {
            var insertCmd = $@" INSERT INTO [{entityName}] 
                                ({string.Join(",", propertiesValues.Select(x => $"[{x.Key}]"))}) 
                                VALUES 
                                ({string.Join(",", propertiesValues.Select(x => $"{GetSqlValue(x.Value)}"))});

                                SELECT last_insert_rowid();";

            var result = await ExecScalarAsync(insertCmd);

            return Utils.GetNullLong(result);
        }

        public Task SyncIdSequenceAsync(string entityName)
        {
            // Nothing to do: AUTOINCREMENT moves past an ID that was inserted with its value.
            return Task.CompletedTask;
        }

        public async Task<long> DeleteDataAsync(string entityName, FilterData? filter)
        {
            var strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);

            var cmd = $@"DELETE FROM [{entityName}]
                        {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")};";

            var result = await ExecNQAsync(cmd);

            return Utils.GetLong(result, 0);
        }

        public async Task<long> UpdateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);

            var insertCmd = $@" UPDATE [{entityName}] 
                                SET {string.Join(",", propertiesValues.Select(x => $"[{x.Key}] = {GetSqlValue(x.Value)}"))}
                                {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")};";

            var result = await ExecNQAsync(insertCmd);

            return Utils.GetLong(result, 0);
        }

        public async Task<List<List<Dictionary<string, object?>>>> ExecuteCustomAsync(string command)
        {
            var result = await ExecTablesAsync(command);
            return result.ToDictionary();
        }

        public async Task<List<Dictionary<string, object?>>> DistinctDataAsync(
            string entityName,
            string propertyName,
            FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);

            // At most one page of values, like every other read, in a fixed order
            var result = await ExecTableAsync($@"SELECT DISTINCT [{propertyName}]
                                                 FROM [{entityName}] {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}
                                                 ORDER BY [{propertyName}]
                                                 LIMIT {Globals.MaxPageSize}");

            return result.ToDictionary();
        }

        public async Task<List<Dictionary<string, object?>>> AggregateDataAsync(
            string entityName,
            AggregateData aggregates,
            FilterData? filter,
            GroupData? group,
            bool sortAsc,
            int pageIndex,
            int pageSize)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);
            string sqlAggProps = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}([{x.Name}]) as '{x.Alias}' "));
            string sqlAggPropsOrder = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}([{x.Name}]) {(sortAsc ? "ASC" : "DESC")} "));

            var groupByProps = (group?.Properties?.Any() ?? false) ? $", + {string.Join(",", group.Properties.Select(x => $"{GetGroupExpression(x)} AS {SqlUtilis.QuoteIdentifier(x.Alias, DatabaseType.SQLLite)}"))}" : string.Empty;
            var groupByGroup = (group?.Properties?.Any() ?? false) ? $"GROUP BY {string.Join(",", group.Properties.Select(x => GetGroupExpression(x)))}" : string.Empty;

            // Groups with equal aggregates are ordered by the group values, so that the pages do not overlap
            var groupByOrder = (group?.Properties?.Any() ?? false) ? $", {string.Join(",", group.Properties.Select(x => GetGroupExpression(x)))}" : string.Empty;

            var dtResult = await ExecTableAsync($@"SELECT {sqlAggProps}
                                        {groupByProps}
                                        FROM [{entityName}]
                                        {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter} COLLATE NOCASE")}
                                        {groupByGroup}
                                        ORDER BY {sqlAggPropsOrder}{groupByOrder}
                                        LIMIT {pageSize} OFFSET {SqlUtilis.GetPagingOffset(pageIndex, pageSize)}");

            return dtResult.ToDictionary();
        }

        private static string GetGroupExpression(GroupData.GroupProperty property)
        {
            // In UTC ('unixepoch' without 'localtime') and as integers, like the other databases: strftime
            // answers text with leading zeros ('03').
            return property.Type switch
            {
                GroupData.GroupByType.Date_Year => $"CAST(strftime('%Y', datetime([{property.Name}]/1000, 'unixepoch')) AS INTEGER)",
                GroupData.GroupByType.Date_Month => $"CAST(strftime('%m', datetime([{property.Name}]/1000, 'unixepoch')) AS INTEGER)",
                GroupData.GroupByType.Date_Day => $"CAST(strftime('%d', datetime([{property.Name}]/1000, 'unixepoch')) AS INTEGER)",
                GroupData.GroupByType.Date_Hour => $"CAST(strftime('%H', datetime([{property.Name}]/1000, 'unixepoch')) AS INTEGER)",
                GroupData.GroupByType.Date_Minute => $"CAST(strftime('%M', datetime([{property.Name}]/1000, 'unixepoch')) AS INTEGER)",
                GroupData.GroupByType.Date_Second => $"CAST(strftime('%S', datetime([{property.Name}]/1000, 'unixepoch')) AS INTEGER)",
                _ => $"[{property.Name}]",
            };
        }

        private static string GetSqlValue(object? val)
        {
            if (val is null)
            {
                return "NULL";
            }

            if (val is long longVal)
            {
                return longVal.ToString(CultureInfo.InvariantCulture);
            }

            if (val is bool boolVal)
            {
                return boolVal ? "1" : "0";
            }

            if (val is JsonElement jsonElement &&
                (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False))
            {
                return jsonElement.ValueKind == JsonValueKind.True ? "1" : "0";
            }

            if (val is decimal decimalVal)
            {
                return decimalVal.ToString(CultureInfo.InvariantCulture);
            }

            // All the rest
            return $"'{Utils.GetString(val).Replace("'", "''").Trim()}'";
        }
    }
}
