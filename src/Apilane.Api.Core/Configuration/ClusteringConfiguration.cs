using System;

namespace Apilane.Api.Core.Configuration
{
    public class ClusteringConfiguration
    {
        public string ClusterId { get; set; } = "apilane_api_cluster";
        public string ServiceId { get; set; } = "apilane_api_service";
        public ClusteringType Type { get; set; } = ClusteringType.Localhost;
        public int SiloPort { get; set; } = 11111;
        public int GatewayPort { get; set; } = 30000;
        public int DashboardPort { get; set; } = 8080;
        public RedisClusteringConfiguration? Redis { get; set; }
        public AdoNetClusteringConfiguration? AdoNet { get; set; }

        public class RedisClusteringConfiguration
        {
            public string ConnectionString { get; set; } = null!;
        }

        public class AdoNetClusteringConfiguration
        {
            public string ConnectionString { get; set; } = null!;
            public string Invariant { get; set; } = "System.Data.SqlClient";
        }
    }

    public enum ClusteringType
    {
        Localhost,
        Redis,
        AdoNet
    }
}
