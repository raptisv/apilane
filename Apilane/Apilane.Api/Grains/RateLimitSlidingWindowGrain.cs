using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api.Grains
{
    public interface IRateLimitSlidingWindowGrain : IGrainWithGuidCompoundKey
    {
        Task<(bool IsRequestAllowed, TimeSpan TimeToWait)> IsRequestAllowedAsync();

        Task ResetLimitsAsync();
    }

    public class RateLimitSlidingWindowGrain : Grain, IRateLimitSlidingWindowGrain
    {
        private readonly List<DateTime> _requestTimestamps = new();

        private int _maxRequests;
        private TimeSpan _timeWindow;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // Parse the grain key to extract MaxRequests and TimeWindow
            var applicationToken = this.GetPrimaryKey(out string keyExt);
            var parts = keyExt.Split('_');

            // Part 0 = max requests
            // Part 1 = timespan
            // Part 2 = identifier (e.g. ID:role:post:entity for users in role post entity)

            // IMPORTANT! An identifier might contain a _ so we check for length < 3 and NOT exactly 3.
            // We only care about the 2 first parts, the third is not used. it is just identifier.

            if (parts.Length < 3 ||
                !int.TryParse(parts[0], out _maxRequests) ||
                !TimeSpan.TryParse(parts[1], out _timeWindow))
            {
                throw new ArgumentException($"Given key '{keyExt}' | Grain key must be in the format '{{MaxRequests}}_{{TimeWindow}}_{{Identifier}}', e.g., '4_100_00:01:00_UserID:role:post:entity'");
            }

            return base.OnActivateAsync(cancellationToken);
        }

        public Task<(bool IsRequestAllowed, TimeSpan TimeToWait)> IsRequestAllowedAsync()
        {
            var now = DateTime.UtcNow;

            // Remove timestamps outside of the time window
            _requestTimestamps.RemoveAll(t => now - t > _timeWindow);

            if (_requestTimestamps.Count < _maxRequests)
            {
                _requestTimestamps.Add(now);
                return Task.FromResult((true, TimeSpan.Zero)); // Can make a request immediately
            }

            var oldestTimestamp = _requestTimestamps.First();
            var waitTime = (oldestTimestamp + _timeWindow) - now;
            return Task.FromResult((false, waitTime > TimeSpan.Zero ? waitTime : TimeSpan.Zero));
        }

        public Task ResetLimitsAsync()
        {
            _requestTimestamps.Clear();
            return Task.CompletedTask;
        }
    }
}
