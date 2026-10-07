using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Common
{
    public class ApplicationRateLimiter
    {
        private const int MaximumRetainedWindows = 100000;
        private const int MaximumRetainedTimestamps = 1000000;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
        private static readonly WindowStore _sharedStore = new(TimeProvider.System, MaximumRetainedWindows, MaximumRetainedTimestamps);
        private static readonly Timer _cleanupTimer = new(_ => _sharedStore.RemoveExpired(), null, CleanupInterval, CleanupInterval);

        private readonly WindowStore _store;
        private readonly string _appToken;

        public ApplicationRateLimiter() : this(_sharedStore, Guid.NewGuid().ToString())
        {
        }

        /// <summary>
        /// Creates an isolated limiter with a supplied clock and memory limits. Application requests use
        /// <see cref="GetOrCreate"/> so that every handle for an application shares its counters.
        /// </summary>
        public ApplicationRateLimiter(TimeProvider timeProvider, int maximumWindows, int maximumTimestamps)
            : this(new WindowStore(timeProvider, maximumWindows, maximumTimestamps), string.Empty)
        {
        }

        private ApplicationRateLimiter(WindowStore store, string appToken)
        {
            _store = store;
            _appToken = appToken;
        }

        public static ApplicationRateLimiter GetOrCreate(string appToken)
        {
            // Handles retain no counters. Removing an expired window cannot leave an older handle using
            // detached state, and application tokens themselves need no permanent instance dictionary.
            return new ApplicationRateLimiter(_sharedStore, appToken);
        }

        public Task<bool> TryAcquireAsync(
            int maxRequests,
            TimeSpan timeWindow,
            string? userIdentifier,
            string entityOrEndpoint,
            string action,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (maxRequests <= 0 || timeWindow <= TimeSpan.Zero)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(_store.TryAcquire(BuildKey(userIdentifier, entityOrEndpoint, action), maxRequests, timeWindow, ct));
        }

        // Keep key components separate: an email address can contain ':', which must never let it name
        // a different endpoint's counter. Entity/endpoint and action names remain case-insensitive.
        private WindowKey BuildKey(string? userIdentifier, string entityOrEndpoint, string action)
        {
            return new WindowKey(_appToken, userIdentifier ?? string.Empty, entityOrEndpoint.ToLowerInvariant(), action.ToLowerInvariant());
        }

        public void Reset(string? userIdentifier, string entityOrEndpoint, string action)
        {
            _store.Reset(BuildKey(userIdentifier, entityOrEndpoint, action));
        }

        private readonly record struct WindowKey(string AppToken, string UserIdentifier, string Endpoint, string Action);

        private sealed class Window
        {
            public Queue<DateTimeOffset> Timestamps { get; } = new();
            public DateTimeOffset ExpiresAt { get; set; }
            public DateTimeOffset LastAcceptedAt { get; set; }
        }

        private sealed class WindowStore
        {
            private readonly object _lock = new();
            private readonly Dictionary<WindowKey, Window> _windows = new();
            private readonly TimeProvider _timeProvider;
            private readonly int _maximumWindows;
            private readonly int _maximumTimestamps;
            private int _timestampCount;
            private DateTimeOffset _nextCleanup;
            private DateTimeOffset _nextExpiry = DateTimeOffset.MaxValue;

            public WindowStore(TimeProvider timeProvider, int maximumWindows, int maximumTimestamps)
            {
                ArgumentNullException.ThrowIfNull(timeProvider);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWindows);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTimestamps);
                _timeProvider = timeProvider;
                _maximumWindows = maximumWindows;
                _maximumTimestamps = maximumTimestamps;
            }

            public bool TryAcquire(WindowKey key, int maxRequests, TimeSpan timeWindow, CancellationToken ct)
            {
                // Acquisition, reset and eviction use the same lock. No semaphore or window reference
                // escapes it, so a request can never increment a window that has already been removed.
                lock (_lock)
                {
                    ct.ThrowIfCancellationRequested();
                    var now = _timeProvider.GetUtcNow();
                    if (now >= _nextCleanup || (now > _nextExpiry && (_windows.Count >= _maximumWindows || _timestampCount >= _maximumTimestamps)))
                    {
                        RemoveExpired(now);
                    }

                    if (_windows.TryGetValue(key, out var window))
                    {
                        while (window.Timestamps.TryPeek(out var timestamp) && now - timestamp > timeWindow)
                        {
                            window.Timestamps.Dequeue();
                            _timestampCount--;
                        }

                        // Queue capacity also consumes memory after timestamps expire. Keep that backing
                        // storage proportional to the live count so changing limits cannot retain large arrays.
                        if (window.Timestamps.Count < window.Timestamps.EnsureCapacity(0) / 2)
                        {
                            window.Timestamps.TrimExcess();
                        }

                        if (window.Timestamps.Count >= maxRequests)
                        {
                            // Different applicable rules can select a longer window for the same user.
                            // A refusal under that rule must not let the earlier, shorter expiry reset it.
                            var expiresAt = window.LastAcceptedAt + timeWindow;
                            if (expiresAt > window.ExpiresAt)
                            {
                                window.ExpiresAt = expiresAt;
                            }
                            return false;
                        }
                    }

                    // At capacity, reject rather than evict live counters: evicting one would let an
                    // attacker reset their allowance by sending enough requests with other keys.
                    if (_timestampCount >= _maximumTimestamps || (window is null && _windows.Count >= _maximumWindows))
                    {
                        return false;
                    }

                    if (window is null)
                    {
                        window = new Window();
                        _windows.Add(key, window);
                    }

                    window.Timestamps.Enqueue(now);
                    window.LastAcceptedAt = now;
                    window.ExpiresAt = now + timeWindow;
                    if (window.ExpiresAt < _nextExpiry)
                    {
                        _nextExpiry = window.ExpiresAt;
                    }
                    _timestampCount++;
                    return true;
                }
            }

            public void Reset(WindowKey key)
            {
                lock (_lock)
                {
                    if (_windows.Remove(key, out var window))
                    {
                        _timestampCount -= window.Timestamps.Count;
                    }
                }
            }

            public void RemoveExpired()
            {
                lock (_lock)
                {
                    RemoveExpired(_timeProvider.GetUtcNow());
                }
            }

            private void RemoveExpired(DateTimeOffset now)
            {
                foreach (var key in _windows.Where(x => x.Value.ExpiresAt < now).Select(x => x.Key).ToArray())
                {
                    _timestampCount -= _windows[key].Timestamps.Count;
                    _windows.Remove(key);
                }

                _nextExpiry = _windows.Count == 0 ? DateTimeOffset.MaxValue : _windows.Values.Min(x => x.ExpiresAt);
                _nextCleanup = now + CleanupInterval;
            }
        }
    }
}
