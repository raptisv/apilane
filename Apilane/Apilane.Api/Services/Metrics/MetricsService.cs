using Apilane.Api.Abstractions.Metrics;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Apilane.Api.Services.Metrics
{
    public class MetricsService : IMetricsService
    {
        public const string MeterName = "Apilane.Web.Api";

        private readonly Histogram<double> _dataDurationHistogram;

        public MetricsService(
            IMeterFactory meterFactory)
        {
            var meter = meterFactory.Create(MeterName);
            _dataDurationHistogram = meter.CreateHistogram<double>("apilane_api_data_duration");
        }

        public MetricsTimer RecordDataDuration(
            string action,
            string entity,
            string apptoken)
        {
            return new MetricsTimer(_dataDurationHistogram,
                new KeyValuePair<string, object?>("action", action.ToLower()),
                new KeyValuePair<string, object?>("entity", entity.ToLower()),
                new KeyValuePair<string, object?>("apptoken", apptoken.ToLower()));
        }
    }
}
