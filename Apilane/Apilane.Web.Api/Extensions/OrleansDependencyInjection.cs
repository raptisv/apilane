using Apilane.Api.Configuration;
using Apilane.Api.Grains;
using Apilane.Common.Extensions;
using Apilane.Data.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Serialization;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Transactions;

namespace Apilane.Web.Api.Extensions
{
    public static class OrleansDependencyInjection
    {
        public static IServiceCollection AddOrleans(
            this WebApplicationBuilder builder,
            ApiConfiguration appConfig)
        {
            // Register storage services
            builder.Services.RegisterGrainsStorages();

            // Register Orleans
            builder.Host.UseOrleans(siloBuilder =>
            {
                siloBuilder.Services.AddSerializer(sb =>
                {
                    sb.AddJsonSerializer(
                        isSupported: type =>
                            type.Namespace!.StartsWith("Apilane")
                    );
                });

                siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("MemoryGrainStorage")
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "apilane_api_cluster";
                    options.ServiceId = "apilane_api_service";
                })
                .Configure<MessagingOptions>(options =>
                {
                    options.ResponseTimeout = TimeSpan.FromSeconds(5);
                    options.ResponseTimeoutWithDebugger = TimeSpan.FromMinutes(60);
                })
                .Configure<ClusterMembershipOptions>(options =>
                {
                    options.DefunctSiloCleanupPeriod = TimeSpan.FromHours(1);
                    options.DefunctSiloExpiration = TimeSpan.FromHours(1);
                    options.IAmAliveTablePublishTimeout = TimeSpan.FromMinutes(1);
                })
                .Configure<DeploymentLoadPublisherOptions>(options =>
                {
                    // The statistics are currently used by ActivationCountPlacementDirector that tries to achieve a balanced distribution of grain activations across silos.
                    // If you are not using ActivationCountPlacement policy for your grains classes, then decreasing the interval will have no negative effect on behavior of the cluster.
                    options.DeploymentLoadPublisherRefreshTime = TimeSpan.FromMinutes(1);
                })
                .Configure<GrainCollectionOptions>(options =>
                {
                    // Max rate limit is 60 minutes so we can't drop the grain sooner
                    options.ClassSpecificCollectionAge[typeof(RateLimitSlidingWindowGrain).FullName!] = TimeSpan.FromMinutes(65);
                })
                .Configure<EndpointOptions>(options =>
                {
                    options.SiloPort = appConfig.Orleans.Cluster.SiloPort;
                    options.GatewayPort = appConfig.Orleans.Cluster.GatewayPort;
                })
                .UseSQLiteClustering(appConfig.FilesPath)
                .UseDashboard(options =>
                {
                    options.CounterUpdateIntervalMs = 5000;
                    options.Port = appConfig.Orleans.Cluster.DashboardPort;
                });
            });

            return builder.Services;
        }

        private static ISiloBuilder UseSQLiteClustering(
            this ISiloBuilder builder,
            string filesPath)
        {
            return builder.ConfigureServices(services =>
            {
                services.AddSingleton<IMembershipTable>(sp => new SQLiteMembershipTable(
                    sp.GetRequiredService<ILoggerFactory>(),
                    sp.GetRequiredService<IOptions<ClusterOptions>>(),
                    filesPath
                ));
            });
        }

        private static void RegisterGrainsStorages(this IServiceCollection services)
        {
            //services
            //    .AddSingleton<AuthTokenGrainUserStore>()
            //    .AddKeyedSingleton<IGrainStorage>(nameof(AuthTokenGrainUserStore),
            //    (sp, _) => sp.GetRequiredService<AuthTokenGrainUserStore>());
        }

        private class SQLiteMembershipTable : IMembershipTable
        {
            private ILogger<SQLiteMembershipTable> _logger;
            private IOptions<ClusterOptions> _options;
            private string _filesPath;

            public SQLiteMembershipTable(
                ILoggerFactory loggerFactory,
                IOptions<ClusterOptions> options,
                string filesPath)
            {
                _logger = loggerFactory.CreateLogger<SQLiteMembershipTable>();
                _options = options;
                _filesPath = filesPath;
            }

            public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate)
            {
                return SqliteHelper.ExecuteAsync(
                    _filesPath,
                    $@"DELETE FROM OrleansMembershipTable WHERE Status = {(int)SiloStatus.Dead};");
            }

            public Task DeleteMembershipTableEntries(string clusterId)
            {
                return SqliteHelper.ExecuteAsync(
                    _filesPath,
                    $@"DELETE FROM OrleansMembershipTable WHERE DeploymentId = '{_options.Value.ClusterId}';
	               DELETE FROM OrleansMembershipVersionTable WHERE DeploymentId = '{_options.Value.ClusterId}';");
            }

            public Task InitializeMembershipTable(bool tryInitTableVersion)
            {
                return SqliteHelper.ExecuteAsync(
                    _filesPath,
                    $@"INSERT INTO OrleansMembershipVersionTable (DeploymentId, Timestamp)
	                SELECT '{_options.Value.ClusterId}', {Common.Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow)}
	                WHERE NOT EXISTS
	                (
		                SELECT 1
		                FROM OrleansMembershipVersionTable
		                WHERE DeploymentId = '{_options.Value.ClusterId}'
	                );");
            }

            public async Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
            {
                await SqliteHelper.ExecuteAsync(
                    _filesPath,
                    $@"INSERT INTO OrleansMembershipTable
	                (
		                DeploymentId,
		                Address,
		                Port,
		                Generation,
		                SiloName,
		                HostName,
		                Status,
		                ProxyPort,
		                StartTime,
		                IAmAliveTime
	                )
	                SELECT
		                '{_options.Value.ClusterId}',
		                '{entry.SiloAddress.Endpoint.Address.ToString()}',
		                {entry.SiloAddress.Endpoint.Port},
		                {entry.SiloAddress.Generation},
		                '{entry.SiloName}',
		                '{entry.HostName}',
		                {(int)entry.Status},
		                {entry.ProxyPort},
		                {Common.Utils.GetUnixTimestampMilliseconds(entry.StartTime)},
		                {Common.Utils.GetUnixTimestampMilliseconds(entry.IAmAliveTime)}
	                WHERE NOT EXISTS
	                (
		                SELECT 1
		                FROM OrleansMembershipTable
		                WHERE
			                DeploymentId = '{_options.Value.ClusterId}'
			                AND Address = '{entry.SiloAddress.Endpoint.Address.ToString()}'
			                AND Port = {entry.SiloAddress.Endpoint.Port}
			                AND Generation = {entry.SiloAddress.Generation}
	                );

	                UPDATE OrleansMembershipVersionTable
	                SET
		                Timestamp = {Common.Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow)},
		                Version = Version + 1
	                WHERE
		                DeploymentId = '{_options.Value.ClusterId}'
		                AND Version = {tableVersion.VersionEtag};");

                return true;
            }

            public async Task<MembershipTableData> ReadAll()
            {
                var dtResult = await SqliteHelper.ExecuteAsync(
                    _filesPath,
                    $@"SELECT
		                v.DeploymentId,
		                m.Address,
		                m.Port,
		                m.Generation,
		                m.SiloName,
		                m.HostName,
		                m.Status,
		                m.ProxyPort,
		                m.SuspectTimes,
		                m.StartTime,
		                m.IAmAliveTime,
		                v.Version
	                FROM
		                OrleansMembershipVersionTable v LEFT OUTER JOIN OrleansMembershipTable m
		                ON v.DeploymentId = m.DeploymentId
	                WHERE
		                v.DeploymentId = '{_options.Value.ClusterId}';");

                var dtoResult = dtResult.ToDictionary().Select(x => new H_Membership_Entry()
                {
                    Address = Common.Utils.GetString(x[nameof(H_Membership_Entry.Address)]),
                    Generation = Common.Utils.GetInt(x[nameof(H_Membership_Entry.Generation)]),
                    HostName = Common.Utils.GetString(x[nameof(H_Membership_Entry.HostName)]),
                    IAmAliveTime = Common.Utils.GetLong(x[nameof(H_Membership_Entry.IAmAliveTime)]),
                    Port = Common.Utils.GetInt(x[nameof(H_Membership_Entry.Port)]),
                    ProxyPort = Common.Utils.GetInt(x[nameof(H_Membership_Entry.ProxyPort)]),
                    SiloName = Common.Utils.GetString(x[nameof(H_Membership_Entry.SiloName)]),
                    StartTime = Common.Utils.GetLong(x[nameof(H_Membership_Entry.StartTime)]),
                    Status = Common.Utils.GetInt(x[nameof(H_Membership_Entry.Status)]),
                    SuspectTimes = Common.Utils.GetString(x[nameof(H_Membership_Entry.SuspectTimes)]),
                    Version = Common.Utils.GetInt(x[nameof(H_Membership_Entry.Version)])
                });

                var membershipEntries = dtoResult
                    .Where(x => x.StartTime > 0)
                    .Select(x => new Tuple<MembershipEntry, string>(x.ToMembershipEntry(), string.Empty))
                    .ToList();

                return new MembershipTableData(
                    membershipEntries,
                    new TableVersion(dtoResult.First().Version, dtoResult.First().Version.ToString()));
            }

            public async Task<MembershipTableData> ReadRow(SiloAddress key)
            {
                var dtResult = await SqliteHelper.ExecuteAsync(
                    _filesPath,
                    $@"SELECT
		            v.DeploymentId,
		            m.Address,
		            m.Port,
		            m.Generation,
		            m.SiloName,
		            m.HostName,
		            m.Status,
		            m.ProxyPort,
		            m.SuspectTimes,
		            m.StartTime,
		            m.IAmAliveTime,
		            v.Version
	            FROM
		            OrleansMembershipVersionTable v
		            -- This ensures the version table will returned even if there is no matching membership row.
		            LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId
		            AND Address = '{key.Endpoint.Address}'
		            AND Port = {key.Endpoint.Port}
		            AND Generation = {key.Generation}
	            WHERE
		            v.DeploymentId = '{key.Endpoint.Address}';");

                var dtoResult = dtResult.ToDictionary().Select(x => new H_Membership_Entry()
                {
                    Address = Common.Utils.GetString(x[nameof(H_Membership_Entry.Address)]),
                    Generation = Common.Utils.GetInt(x[nameof(H_Membership_Entry.Generation)]),
                    HostName = Common.Utils.GetString(x[nameof(H_Membership_Entry.HostName)]),
                    IAmAliveTime = Common.Utils.GetLong(x[nameof(H_Membership_Entry.IAmAliveTime)]),
                    Port = Common.Utils.GetInt(x[nameof(H_Membership_Entry.Port)]),
                    ProxyPort = Common.Utils.GetInt(x[nameof(H_Membership_Entry.ProxyPort)]),
                    SiloName = Common.Utils.GetString(x[nameof(H_Membership_Entry.SiloName)]),
                    StartTime = Common.Utils.GetLong(x[nameof(H_Membership_Entry.StartTime)]),
                    Status = Common.Utils.GetInt(x[nameof(H_Membership_Entry.Status)]),
                    SuspectTimes = Common.Utils.GetString(x[nameof(H_Membership_Entry.SuspectTimes)]),
                    Version = Common.Utils.GetInt(x[nameof(H_Membership_Entry.Version)])
                });

                var membershipEntries = dtoResult
                    .Where(x => x.StartTime != 0)
                    .Select(x => new Tuple<MembershipEntry, string>(x.ToMembershipEntry(), string.Empty))
                    .ToList();

                return new MembershipTableData(
                    membershipEntries,
                    new TableVersion(dtoResult.First().Version, dtoResult.First().Version.ToString()));
            }

            public Task UpdateIAmAlive(MembershipEntry entry)
            {
                return SqliteHelper.ExecuteAsync(
                    _filesPath,
                    $@"UPDATE OrleansMembershipTable
	                SET
		                IAmAliveTime = {Common.Utils.GetUnixTimestampMilliseconds(entry.IAmAliveTime)}
	                WHERE
		                DeploymentId = '{_options.Value.ClusterId}'
		                AND Address = '{entry.SiloAddress.Endpoint.Address.ToString()}'
		                AND Port = {entry.SiloAddress.Endpoint.Port}
		                AND Generation = {entry.SiloAddress.Generation};");
            }

            public async Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
            {
                await SqliteHelper.ExecuteAsync(
                    _filesPath,
                    $@"UPDATE OrleansMembershipVersionTable
	                SET
		                Timestamp = {Common.Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow)},
		                Version = Version + 1
	                WHERE
		                DeploymentId = '{_options.Value.ClusterId}'
		                AND Version = {tableVersion.VersionEtag};
	
	                UPDATE OrleansMembershipTable
	                SET
		                Status = {(int)entry.Status},
		                SuspectTimes = '{JsonSerializer.Serialize(entry.SuspectTimes)}',
		                IAmAliveTime = {Common.Utils.GetUnixTimestampMilliseconds(entry.IAmAliveTime)}
	                WHERE
		                DeploymentId = '{_options.Value.ClusterId}'
		                AND Address = '{entry.SiloAddress.Endpoint.Address.ToString()}'
		                AND Port = {entry.SiloAddress.Endpoint.Port}
		                AND Generation = {entry.SiloAddress.Generation};");

                return true;
            }

            private class H_Membership_Entry
            {
                public string Address { get; set; } = null!;
                public int Port { get; set; }
                public int Generation { get; set; }
                public int Version { get; set; }
                public int Status { get; set; }
                public string SiloName { get; set; } = null!;
                public string HostName { get; set; } = null!;
                public int ProxyPort { get; set; }
                public string SuspectTimes { get; set; } = null!;
                public long StartTime { get; set; }
                public long IAmAliveTime { get; set; }

                public MembershipEntry ToMembershipEntry()
                {
                    return new MembershipEntry()
                    {
                        Status = (SiloStatus)Status,
                        HostName = HostName,
                        IAmAliveTime = Common.Utils.GetDateFromUnixTimestamp(IAmAliveTime.ToString()) ?? throw new Exception($"Invalid date time {IAmAliveTime}"),
                        StartTime = Common.Utils.GetDateFromUnixTimestamp(StartTime.ToString()) ?? throw new Exception($"Invalid date time {StartTime}"),
                        ProxyPort = ProxyPort,
                        SiloAddress = SiloAddress.FromParsableString($"{Address}:{Port}@{Generation}"),
                        SiloName = SiloName,
                        SuspectTimes = string.IsNullOrWhiteSpace(SuspectTimes) ? new List<Tuple<SiloAddress, DateTime>>() : JsonSerializer.Deserialize<List<Tuple<SiloAddress, DateTime>>>(SuspectTimes),
                        // The following are used only for Azure membership
                        FaultZone = 0,
                        RoleName = string.Empty,
                        UpdateZone = 0
                    };
                }
            }

            private static class SqliteHelper
            {
                public static async Task<DataTable> ExecuteAsync(
                    string filesPath,
                    string cmd)
                {
                    if (string.IsNullOrWhiteSpace(cmd))
                    {
                        throw new Exception("Query cannot be emtpy");
                    }

                    var connectionString = await GetConnStringAsync(filesPath);

                    // Execute in suppress transaction scope to prevent lock between databases.
                    using (new TransactionScope(
                        TransactionScopeOption.Suppress,
                        new TransactionOptions() { IsolationLevel = System.Transactions.IsolationLevel.ReadUncommitted, Timeout = TimeSpan.FromSeconds(5) },
                        TransactionScopeAsyncFlowOption.Enabled))
                    {
                        await using (var ctx = new SQLiteDataStorageRepository(connectionString))
                        {
                            return await ctx.ExecTableAsync(cmd);
                        }
                    }
                }

                private static async Task<string> GetConnStringAsync(
                    string filesPath)
                {
                    var directory = new DirectoryInfo(filesPath);
                    if (!directory.Exists)
                    {
                        directory.Create();
                    }

                    var file = new FileInfo(Path.Combine(directory.FullName, $"orleans_apilane_api.db"));

                    var connSring = $"Data Source={file.FullName};Cache Size=2000;Version=3;FailIfMissing=True;";

                    // If this is the first time that is called, or something happened and the file was deleted
                    if (!file.Exists)
                    {
                        await GenerateDatabaseAsync(file, connSring);
                    }

                    return connSring;
                }

                private static async Task GenerateDatabaseAsync(
                    FileInfo file,
                    string connSring)
                {
                    SQLiteConnection.CreateFile(file.FullName);

                    await using (var ctxNoAccess = new SQLiteDataStorageRepository(connSring))
                    {
                        await ctxNoAccess.ExecNQAsync($@"CREATE TABLE IF NOT EXISTS OrleansMembershipVersionTable
                                                    (
	                                                    DeploymentId TEXT NOT NULL PRIMARY KEY,
	                                                    Timestamp BIGINT NOT NULL DEFAULT 0,
	                                                    Version INT NOT NULL DEFAULT 0
                                                    );

                                                    CREATE TABLE IF NOT EXISTS OrleansMembershipTable
                                                    (
	                                                    DeploymentId TEXT NOT NULL,
	                                                    Address TEXT NOT NULL,
	                                                    Port INT NOT NULL,
	                                                    Generation INT NOT NULL,
	                                                    SiloName TEXT NOT NULL,
	                                                    HostName TEXT NOT NULL,
	                                                    Status INT NOT NULL,
	                                                    ProxyPort INT NULL,
	                                                    SuspectTimes TEXT NULL,
	                                                    StartTime BIGINT NOT NULL,
	                                                    IAmAliveTime BIGINT NOT NULL,

	                                                    CONSTRAINT PK_MembershipTable_DeploymentId PRIMARY KEY(DeploymentId, Address, Port, Generation),
	                                                    CONSTRAINT FK_MembershipTable_MembershipVersionTable_DeploymentId FOREIGN KEY (DeploymentId) REFERENCES OrleansMembershipVersionTable (DeploymentId)
                                                    );");
                    }
                }
            }
        }
    }
}
