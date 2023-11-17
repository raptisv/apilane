using Xunit;

namespace CasinoService.ComponentTests.Infrastructure
{
    [CollectionDefinition(nameof(ApilaneApiComponentTestsCollection))]
    public class ApilaneApiComponentTestsCollection : ICollectionFixture<SuiteContext>
    {

    }
}