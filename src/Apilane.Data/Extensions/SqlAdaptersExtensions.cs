using Apilane.Common.Enums;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using System;
using System.Data.Common;

namespace Apilane.Data.Extensions
{
    public static class SqlAdaptersExtensions
    {
        public static DbProviderFactory GetDbProviderFactory(this DatabaseType type)
        {
            return type switch
            {
                DatabaseType.SQLServer => SqlClientFactory.Instance,
                DatabaseType.MySQL => MySqlConnectorFactory.Instance,
                DatabaseType.PostgreSQL => NpgsqlFactory.Instance,
                _ => throw new NotImplementedException(),
            };
        }
    }
}
