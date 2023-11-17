using Apilane.Common.Enums;
using Apilane.Common.Utilities;
using Microsoft.Data.SqlClient;
using MySqlConnector;
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
                DatabaseType.SQLLite => GetDbProviderFactory("Microsoft.Data.Sqlite.SqliteFactory", "Microsoft.Data.Sqlite"),
                DatabaseType.SQLServer => SqlClientFactory.Instance,
                DatabaseType.MySQL => MySqlConnectorFactory.Instance,
                _ => throw new NotImplementedException(),
            };
        }

        private static DbProviderFactory GetDbProviderFactory(string dbProviderFactoryTypename, string assemblyName)
        {
            var instance = ReflectionUtils.GetStaticProperty(dbProviderFactoryTypename, "Instance");
            if (instance is null)
            {
                var a = ReflectionUtils.LoadAssembly(assemblyName);
                if (a != null)
                {
                    instance = ReflectionUtils.GetStaticProperty(dbProviderFactoryTypename, "Instance");
                }
            }

            return instance as DbProviderFactory
                 ?? throw new Exception($"Could not load '{dbProviderFactoryTypename}' from '{assemblyName}'");
        }
    }
}
