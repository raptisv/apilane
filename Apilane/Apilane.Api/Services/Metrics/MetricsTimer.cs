using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Apilane.Api.Services.Metrics
{
    public class MetricsTimer : IDisposable
    {
        private readonly Stopwatch _stopWatch;
        private readonly Histogram<double> _histogram;
        private readonly KeyValuePair<string, object?>[] _tags;

        public MetricsTimer(
            Histogram<double> histogram,
            params KeyValuePair<string, object?>[] tags)
        {
            _histogram = histogram;
            _tags = tags;
            _stopWatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _histogram.Record(_stopWatch.Elapsed.TotalSeconds, _tags);
            _stopWatch.Stop();
        }
    }
}
