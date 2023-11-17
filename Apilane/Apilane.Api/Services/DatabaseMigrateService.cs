//using Apilane.Api.Abstractions;
//using Apilane.Api.Configuration;
//using Apilane.Api.Extentions;
//using Apilane.Common;
//using Apilane.Common.Enums;
//using Apilane.Common.Models;
//using Apilane.Data.Abstractions;
//using Apilane.Data.Repository;
//using MySqlConnector;
//using System;
//using System.Collections.Generic;
//using System.Data;
//using System.Data.SQLite;
//using System.Linq;
//using System.Threading.Tasks;

// Leaving this here for future reference!

//namespace Apilane.Api.Services
//{
//    public class DatabaseMigrateService
//    {
//        private readonly ApiConfiguration _apiConfiguration;

//        public DatabaseMigrateService(
//            ApiConfiguration apiConfiguration)
//        {
//            _apiConfiguration = apiConfiguration;
//        }

//        public async Task MigrateToSQLiteAsync(DBWS_Application application)
//        {
//            var sqliteFile = application.Token.GetApplicationFileInfo(_apiConfiguration.FilesPath);

//            if (sqliteFile.Exists)
//            {
//                sqliteFile.Delete();
//            }

//            // Create sqlite file
//            SQLiteConnection.CreateFile(sqliteFile.FullName);

//            // Keep the old connection string to SQL Server
//            string currentConnectionStringSqlServer = application.GetConnectionstring(_apiConfiguration.FilesPath);

//            // Change the type to SQLite to get the correct connection string when required
//            application.DatabaseType = (int)DatabaseType.SQLLite;

//            // First validate that the SQL Server exists and is accessible
//            await using (var ctxSqLite = new SQLiteDataStorageRepository(application.GetConnectionstring(_apiConfiguration.FilesPath)))
//            {
//                // Create tables and columns
//                foreach (var ent in application.Entities)
//                {
//                    // Put the data on the table without the primary key constraint
//                    await ctxSqLite.ExecNQAsync($@"CREATE TABLE [{ent.Name}_NO_PRIMARY_KEY] 
//                                                ( 
//                                                    {string.Join(", ", ent.Properties.Select(x => x.IsPrimaryKey ? $"[{x.Name}] INTEGER NOT NULL" : GetCreate(x, DatabaseType.SQLLite)))}
//                                                );");

//                    // After the above step, drop data on the table with the PK constraint
//                    await ctxSqLite.ExecNQAsync($@"CREATE TABLE [{ent.Name}] 
//                                                ( 
//                                                    {string.Join(", ", ent.Properties.Select(x => GetCreate(x, DatabaseType.SQLLite)))}
//                                                );");
//                }

//                /*
//                     CREATE TABLE {Ent.Name}__backup__({string.Join(",", PropertiesStringCreate)});
//                     INSERT INTO {Ent.Name}__backup__ SELECT {string.Join(",", PropertiesString)} FROM {Ent.Name};
//                     DROP TABLE {Ent.Name};
//                     ALTER TABLE {Ent.Name}__backup__ RENAME TO {Ent.Name};
//                 */

//                await using (var ctxSqlServer = new SQLServerDataStorageRepository(currentConnectionStringSqlServer))
//                {
//                    foreach (var ent in application.Entities)
//                    {
//                        DataTable dt = await ctxSqlServer.ExecTableAsync($"SELECT * FROM [{ent.Name}]");

//                        // Remove the primary key
//                        dt.PrimaryKey = null;

//                        // Change row state to added in order to perform Insert later on
//                        foreach (DataRow dr in dt.Rows)
//                        {
//                            dr.SetAdded();
//                        }

//                        using (var sqliteAdapter = new SQLiteDataAdapter($"SELECT * FROM [{ent.Name}_NO_PRIMARY_KEY]", ctxSqLite.GetConnection()))
//                        {
//                            //sqliteAdapter.InsertCommand = new SqliteCommandBuilder(sqliteAdapter).GetInsertCommand(true);
//                            var cmdBuilder = new SQLiteCommandBuilder(sqliteAdapter);
//                            int jj = sqliteAdapter.Update(dt);
//                        }

//                        List<string> PropertiesString = ent.Properties.Select(x => $"[{x.Name}]").ToList();

//                        // Drop data on the table with the PK constraint
//                        await ctxSqLite.ExecNQAsync($@"INSERT INTO [{ent.Name}] SELECT {string.Join(",", PropertiesString)} FROM [{ent.Name}_NO_PRIMARY_KEY];
//                                                DROP TABLE [{ent.Name}_NO_PRIMARY_KEY];");
//                    }
//                }
//            }
//        }

//        public async Task MigrateToSQLServerAsync(DBWS_Application application, string connString)
//        {
//            try
//            {
//                // First validate that the SQL Server exists and is accessible
//                await using (var ctxSS = new SQLServerDataStorageRepository(connString))
//                {
//                    // Perform checks
//                    var countTables = Utils.GetInt(await ctxSS.ExecScalarAsync(@"SELECT Count(*)
//                                                                    FROM INFORMATION_SCHEMA.TABLES
//                                                                    WHERE TABLE_TYPE = 'BASE TABLE';"));

//                    if (countTables > 0)
//                    {
//                        throw new Exception($"Database {ctxSS.GetConnection().Database} is not empty!!!");
//                    }

//                    // Create tables and columns
//                    foreach (var ent in application.Entities)
//                    {
//                        await ctxSS.ExecNQAsync($@"CREATE TABLE [{ent.Name}] 
//                                    ( 
//                                        {string.Join(", ", ent.Properties.Select(x => GetCreate(x, DatabaseType.SQLServer)))}
//                                    );");
//                    }

//                    await using (var ctxLite = new SQLiteDataStorageRepository(application.GetConnectionstring(_apiConfiguration.FilesPath)))
//                    {
//                        foreach (var ent in application.Entities)
//                        {
//                            DataTable dt = await ctxLite.ExecTableAsync($"SELECT * FROM [{ent.Name}]");

//                            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(ctxSS.GetConnection(), SqlBulkCopyOptions.KeepIdentity, null))
//                            {
//                                foreach (DataColumn c in dt.Columns)
//                                {
//                                    bulkCopy.ColumnMappings.Add(c.ColumnName, c.ColumnName);
//                                }

//                                bulkCopy.DestinationTableName = $"dbo.[{ent.Name}]";
//                                bulkCopy.WriteToServer(dt);
//                            }
//                        }
//                    }
//                }
//            }
//            catch (Exception)
//            {
//                throw;
//            }
//        }

//        public async Task MigrateToMySQLAsync(DBWS_Application application, string connString)
//        {
//            // First validate that the SQL Server exists and is accessible
//            await using (var ctxMySQL = new MySQLDataStorageRepository($"{connString};sqlservermode=True;"))
//            {
//                // Create tables and columns
//                foreach (var ent in application.Entities)
//                {
//                    await ctxMySQL.ExecNQAsync($@"CREATE TABLE [{ent.Name}] 
//                                    ( 
//                                        {string.Join(", ", ent.Properties.Select(x => GetCreate(x, DatabaseType.MySQL)))}
//                                    );");
//                }
//            }

//            var mySqlConnection = new MySqlConnection($"{connString};AllowLoadLocalInfile=True;");
//            mySqlConnection.Open();

//            IDataStorageRepository ctxOriginal = application.DatabaseType switch
//            {
//                (int)DatabaseType.SQLServer => new SQLServerDataStorageRepository(application.GetConnectionstring(_apiConfiguration.FilesPath)),
//                (int)DatabaseType.SQLLite => new SQLiteDataStorageRepository(application.GetConnectionstring(_apiConfiguration.FilesPath)),
//                _ => throw new NotImplementedException(),
//            };
//            foreach (var ent in application.Entities)
//            {
//                var dt = await ctxOriginal.GetPagedDataAsync(
//                    ent.Name,
//                    ent.Properties.Select(x => x.Name).ToList(),
//                    null,
//                    null,
//                    1, 1_000_000);

//                MySqlBulkCopy bulkCopy = new MySqlBulkCopy(mySqlConnection);
//                for (int i = 0; i < dt.Columns.Count; i++)
//                {
//                    bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, dt.Columns[i].ColumnName));
//                }
//                bulkCopy.DestinationTableName = ent.Name;
//                bulkCopy.WriteToServer(dt);
//            }
//        }

//        private static string GetCreate(DBWS_EntityProperty property, DatabaseType databaseType)
//        {
//            switch (property.TypeID_Enum)
//            {
//                case PropertyType.Number:
//                    if (property.IsPrimaryKey)
//                    {
//                        return databaseType switch
//                        {
//                            DatabaseType.SQLServer => $"[{property.Name}] BIGINT NOT NULL PRIMARY KEY IDENTITY(1,1)",
//                            DatabaseType.MySQL => $"`{property.Name}` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY",
//                            DatabaseType.SQLLite => $"[{property.Name}] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT",
//                            _ => throw new NotImplementedException(),
//                        };
//                    }
//                    else
//                    {
//                        if (!property.DecimalPlaces.HasValue || property.DecimalPlaces.Value == 0)
//                        {
//                            return databaseType switch
//                            {
//                                DatabaseType.SQLServer => $"[{property.Name}] BIGINT {(property.Required ? "NOT NULL DEFAULT 0" : "NULL")}",
//                                DatabaseType.MySQL => $"`{property.Name}` BIGINT {(property.Required ? "NOT NULL" : "NULL")}",
//                                DatabaseType.SQLLite => $"[{property.Name}] INTEGER {(property.Required ? "DEFAULT 0 NOT NULL" : "NULL")}",
//                                _ => throw new NotImplementedException(),
//                            };
//                        }
//                        else
//                        {
//                            return databaseType switch
//                            {
//                                DatabaseType.SQLServer => $"[{property.Name}] DECIMAL(18,{property.DecimalPlaces.Value}) {(property.Required ? "NOT NULL DEFAULT 0" : "NULL")}",
//                                DatabaseType.MySQL => $"`{property.Name}` DECIMAL(18,{property.DecimalPlaces.Value}) {(property.Required ? "NOT NULL" : "NULL")}",
//                                DatabaseType.SQLLite => $"[{property.Name}] NUMERIC(18,{property.DecimalPlaces.Value}) {(property.Required ? "DEFAULT 0 NOT NULL" : "NULL")}",
//                                _ => throw new NotImplementedException(),
//                            };
//                        }
//                    }
//                case PropertyType.Boolean:
//                    return databaseType switch
//                    {
//                        DatabaseType.SQLServer => $"[{property.Name}] BIT {(property.Required ? "NOT NULL DEFAULT 0" : "NULL")}",
//                        DatabaseType.MySQL => $"`{property.Name}` BIT(1) {(property.Required ? "NOT NULL" : "NULL")}",
//                        DatabaseType.SQLLite => $"[{property.Name}] BOOLEAN {(property.Required ? "DEFAULT 0 NOT NULL" : "NULL")}",
//                        _ => throw new NotImplementedException(),
//                    };
//                case PropertyType.Date:
//                    return databaseType switch
//                    {
//                        DatabaseType.SQLServer => $"[{property.Name}] BIGINT {(property.Required ? "NOT NULL DEFAULT 0" : "NULL")}",
//                        DatabaseType.MySQL => $"`{property.Name}` BIGINT {(property.Required ? "NOT NULL" : "NULL")}",
//                        DatabaseType.SQLLite => $"[{property.Name}] BIGINT {(property.Required ? "DEFAULT 0 NOT NULL" : "NULL")}",
//                        _ => throw new NotImplementedException(),
//                    };
//                case PropertyType.String:
//                    return databaseType switch
//                    {
//                        // IMPORTANT: On SqlServer Encrypted properties will result to bigger values so it should be max.
//                        DatabaseType.SQLServer => $"[{property.Name}] NVARCHAR({(property.Maximum is not null && !property.Encrypted ? property.Maximum.Value.ToString() : "MAX")}) {(property.Required ? "NOT NULL DEFAULT ''" : "NULL")}",
//                        DatabaseType.MySQL => $"`{property.Name}` MEDIUMTEXT CHARACTER SET utf8 {(property.Required ? "NOT NULL" : "NULL")}",
//                        DatabaseType.SQLLite => $"[{property.Name}] TEXT {(property.Required ? "DEFAULT '' NOT NULL" : "NULL")}",
//                        _ => throw new NotImplementedException(),
//                    };
//                default:
//                    throw new NotImplementedException();
//            }
//        }
//    }
//}
