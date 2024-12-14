using Apilane.Api.Core.Services.Metrics;

namespace Apilane.Api.Core.Abstractions.Metrics
{
    public interface IMetricsService
    {
        MetricsTimer RecordDataDuration(
            string action,
            string entity,
            string apptoken);
    }
}
