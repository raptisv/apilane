using Apilane.Api.Controllers;
using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Apilane.Api.Component.Tests.Infrastructure
{
    /// <summary>
    /// The state the API host keeps per process that decides which connection a request meets.
    /// <see cref="BaseApplicationApiController"/> runs the system tables check on the request's store
    /// the first time an application token is seen by the process, so that request has an open
    /// connection before the action (and any transaction scope the action opens) starts. Every later
    /// request for the token finds the connection closed. A restarted API, or a second instance behind
    /// a load balancer, is exactly a process that has not seen the token yet.
    /// </summary>
    /// <remarks>
    /// There is no production hook for this: the set is private and static, and the host lives in the
    /// test process, so the tests reach it by reflection. If the field is renamed the tests fail
    /// loudly here instead of passing without testing the first request.
    /// </remarks>
    internal static class ApiProcessState
    {
        private const string FieldName = "_systemTablesMigratedTokens";

        private static readonly ConcurrentDictionary<string, bool> _migratedTokens = LoadMigratedTokens();

        private static ConcurrentDictionary<string, bool> LoadMigratedTokens()
        {
            var field = typeof(BaseApplicationApiController).GetField(FieldName, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException($"{nameof(BaseApplicationApiController)}.{FieldName} no longer exists: update {nameof(ApiProcessState)}");

            return field.GetValue(null) as ConcurrentDictionary<string, bool>
                ?? throw new InvalidOperationException($"{nameof(BaseApplicationApiController)}.{FieldName} is no longer a ConcurrentDictionary<string, bool>: update {nameof(ApiProcessState)}");
        }

        /// <summary>Makes the API forget the token, so the next request for it is the first one.</summary>
        public static void SimulateApiRestart(string appToken)
        {
            _migratedTokens.TryRemove(appToken, out _);
        }

        /// <summary>Whether the API has already seen the token (the system tables check ran for it).</summary>
        public static bool HasMigratedSystemTables(string appToken)
        {
            return _migratedTokens.ContainsKey(appToken);
        }
    }
}
