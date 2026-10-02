using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Common
{
    public class ApplicationRateLimiter
    {
        private static readonly ConcurrentDictionary<string, ApplicationRateLimiter> _instances = new();

        public static ApplicationRateLimiter GetOrCreate(string appToken)
            => _instances.GetOrAdd(appToken, _ => new ApplicationRateLimiter());

        private readonly ConcurrentDictionary<string, (List<DateTime> Timestamps, SemaphoreSlim Semaphore)> _windows = new();

        public async Task<bool> TryAcquireAsync(
            int maxRequests,
            TimeSpan timeWindow,
            string? userIdentifier,
            string entityOrEndpoint,
            string action,
            CancellationToken ct)
        {
            var key = BuildKey(userIdentifier, entityOrEndpoint, action);
            var entry = _windows.GetOrAdd(key, _ => (new List<DateTime>(), new SemaphoreSlim(1, 1)));

            await entry.Semaphore.WaitAsync(ct);
            try
            {
                var now = DateTime.UtcNow;
                entry.Timestamps.RemoveAll(t => now - t > timeWindow);

                if (entry.Timestamps.Count < maxRequests)
                {
                    entry.Timestamps.Add(now);
                    return true;
                }

                return false;
            }
            finally
            {
                entry.Semaphore.Release();
            }
        }

        // Entity and endpoint names are matched without regard to case, and the name arrives as the client
        // wrote it. One window per name, whatever its casing, or re-casing the name would reset the limit.
        private static string BuildKey(string? userIdentifier, string entityOrEndpoint, string action)
        {
            return $"{userIdentifier}:{entityOrEndpoint.ToLowerInvariant()}:{action.ToLowerInvariant()}";
        }

        public void Reset(string? userIdentifier, string entityOrEndpoint, string action)
        {
            var key = BuildKey(userIdentifier, entityOrEndpoint, action);
            if (_windows.TryGetValue(key, out var entry))
            {
                entry.Semaphore.Wait();
                try
                {
                    entry.Timestamps.Clear();
                }
                finally
                {
                    entry.Semaphore.Release();
                }
            }
        }
    }
}
