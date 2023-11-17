using Apilane.Api.Services.Metrics;

namespace Apilane.Api.Abstractions.Metrics
{
    public interface IMetricsService
    {
        MetricsTimer RecordDataDuration(
            string action,
            string entity,
            string apptoken);
    }
}
