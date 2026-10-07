using Apilane.Common;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ApplicationRateLimiterTests
    {
        [TestMethod]
        public async Task TryAcquireAsync_UniqueKeysAtCapacity_Should_RejectWithoutEvictingLiveCounters()
        {
            var clock = new ManualTimeProvider();
            var limiter = new ApplicationRateLimiter(clock, maximumWindows: 2, maximumTimestamps: 10);

            Assert.IsTrue(await AcquireAsync(limiter, "first"));
            Assert.IsTrue(await AcquireAsync(limiter, "second"));

            for (var i = 0; i < 100; i++)
            {
                Assert.IsFalse(await AcquireAsync(limiter, $"unknown-{i}"));
            }

            Assert.IsFalse(await AcquireAsync(limiter, "first"));
            Assert.IsFalse(await AcquireAsync(limiter, "second"));

            clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromTicks(1));
            Assert.IsTrue(await AcquireAsync(limiter, "new-first"));
            Assert.IsTrue(await AcquireAsync(limiter, "new-second"));
            Assert.IsFalse(await AcquireAsync(limiter, "new-third"));
        }

        [TestMethod]
        public async Task TryAcquireAsync_TimestampsAtCapacity_Should_RejectUntilExpiredOrReset()
        {
            var limiter = new ApplicationRateLimiter(new ManualTimeProvider(), maximumWindows: 10, maximumTimestamps: 3);

            Assert.IsTrue(await AcquireAsync(limiter, "first", maxRequests: 10));
            Assert.IsTrue(await AcquireAsync(limiter, "first", maxRequests: 10));
            Assert.IsTrue(await AcquireAsync(limiter, "second"));
            Assert.IsFalse(await AcquireAsync(limiter, "first", maxRequests: 10));
            Assert.IsFalse(await AcquireAsync(limiter, "third"));

            limiter.Reset("first", "records", "get");

            Assert.IsTrue(await AcquireAsync(limiter, "third"));
            Assert.IsTrue(await AcquireAsync(limiter, "fourth"));
            Assert.IsFalse(await AcquireAsync(limiter, "fifth"));
            Assert.IsFalse(await AcquireAsync(limiter, "second"));
        }

        [TestMethod]
        public async Task TryAcquireAsync_SlidingWindow_Should_KeepUnexpiredRequests()
        {
            var clock = new ManualTimeProvider();
            var limiter = new ApplicationRateLimiter(clock, maximumWindows: 2, maximumTimestamps: 10);

            Assert.IsTrue(await AcquireAsync(limiter, "user", maxRequests: 2));
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.IsTrue(await AcquireAsync(limiter, "user", maxRequests: 2));
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.IsFalse(await AcquireAsync(limiter, "user", maxRequests: 2));
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.IsTrue(await AcquireAsync(limiter, "user", maxRequests: 2));
            Assert.IsFalse(await AcquireAsync(limiter, "user", maxRequests: 2));
        }

        [TestMethod]
        public async Task TryAcquireAsync_LongerWindowAfterRefusal_Should_NotExpireTheLiveCounterEarly()
        {
            var clock = new ManualTimeProvider();
            var limiter = new ApplicationRateLimiter(clock, maximumWindows: 1, maximumTimestamps: 10);
            Assert.IsTrue(await limiter.TryAcquireAsync(1, TimeSpan.FromSeconds(1), "user", "records", "get", default));
            Assert.IsFalse(await AcquireAsync(limiter, "user"));

            clock.Advance(TimeSpan.FromSeconds(2));

            Assert.IsFalse(await AcquireAsync(limiter, "user"));
            Assert.IsFalse(await AcquireAsync(limiter, "other"));
        }

        [TestMethod]
        public async Task TryAcquireAsync_ExpiredKeysWithConcurrentRequests_Should_NotExceedNewAllowance()
        {
            var clock = new ManualTimeProvider();
            var limiter = new ApplicationRateLimiter(clock, maximumWindows: 1, maximumTimestamps: 10);
            Assert.IsTrue(await AcquireAsync(limiter, "expired"));
            clock.Advance(TimeSpan.FromMinutes(2));

            var results = await Task.WhenAll(Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() => AcquireAsync(limiter, "new", maxRequests: 3))));

            Assert.AreEqual(3, results.Count(x => x));
            Assert.IsFalse(await AcquireAsync(limiter, "other"));
        }

        [TestMethod]
        public async Task TryAcquireAsync_OldAndNewApplicationHandlesAfterReset_Should_ShareOneAllowance()
        {
            var token = Guid.NewGuid().ToString();
            var first = ApplicationRateLimiter.GetOrCreate(token);
            var second = ApplicationRateLimiter.GetOrCreate(token);
            Assert.IsTrue(await AcquireAsync(first, "user"));
            Assert.IsFalse(await AcquireAsync(second, "user"));

            first.Reset("user", "records", "GET");
            var results = await Task.WhenAll(Enumerable.Range(0, 100)
                .Select(i => Task.Run(() => AcquireAsync(i % 2 == 0 ? first : second, "user", maxRequests: 3))));

            Assert.AreEqual(3, results.Count(x => x));
            Assert.IsFalse(await AcquireAsync(ApplicationRateLimiter.GetOrCreate(token), "user", maxRequests: 3));
            first.Reset("user", "records", "get");
        }

        [TestMethod]
        public async Task TryAcquireAsync_CaseAndKeySeparators_Should_KeepExpectedCounterBoundaries()
        {
            var limiter = new ApplicationRateLimiter(new ManualTimeProvider(), maximumWindows: 5, maximumTimestamps: 10);
            Assert.IsTrue(await limiter.TryAcquireAsync(1, TimeSpan.FromMinutes(1), "user", "Records", "GET", default));
            Assert.IsFalse(await limiter.TryAcquireAsync(1, TimeSpan.FromMinutes(1), "user", "records", "get", default));

            Assert.IsTrue(await limiter.TryAcquireAsync(1, TimeSpan.FromMinutes(1), "user:part", "endpoint", "get", default));
            Assert.IsTrue(await limiter.TryAcquireAsync(1, TimeSpan.FromMinutes(1), "user", "part:endpoint", "get", default));
        }

        [TestMethod]
        public async Task TryAcquireAsync_CancelledRequest_Should_NotConsumeCapacity()
        {
            var limiter = new ApplicationRateLimiter(new ManualTimeProvider(), maximumWindows: 1, maximumTimestamps: 1);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                limiter.TryAcquireAsync(1, TimeSpan.FromMinutes(1), "cancelled", "records", "get", cancellation.Token));

            Assert.IsTrue(await AcquireAsync(limiter, "allowed"));
        }

        private static Task<bool> AcquireAsync(ApplicationRateLimiter limiter, string user, int maxRequests = 1)
        {
            return limiter.TryAcquireAsync(maxRequests, TimeSpan.FromMinutes(1), user, "records", "get", default);
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow()
            {
                return _now;
            }

            public void Advance(TimeSpan duration)
            {
                _now += duration;
            }
        }
    }
}
