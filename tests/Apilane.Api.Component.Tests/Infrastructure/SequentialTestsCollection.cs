using Xunit;

namespace CasinoService.ComponentTests.Infrastructure
{
    /// <summary>
    /// Test classes run in parallel by default (one collection per class). Classes that measure process
    /// memory or timings belong to this collection instead: xUnit runs it on its own, never alongside
    /// the parallel classes, so other tests cannot disturb the measurements.
    /// </summary>
    [CollectionDefinition(nameof(SequentialTestsCollection), DisableParallelization = true)]
    public class SequentialTestsCollection
    {

    }
}
