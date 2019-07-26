using System.Collections.Concurrent;
using System.IO;

namespace SimpleMetrics
{
    public sealed class Registry
    {
        private readonly ConcurrentDictionary<string, Counter> counters = new ConcurrentDictionary<string, Counter>();

        private readonly ConcurrentDictionary<string, Summary> summaries = new ConcurrentDictionary<string, Summary>();

        private readonly ConcurrentDictionary<string, Histogram> histograms = new ConcurrentDictionary<string, Histogram>();

        public Counter GetOrCreateCounter(string name)
        {
            return this.counters.GetOrAdd(name, (n) => new Counter(n, Metrics.CounterTypeName));
        }

        public Counter GetOrCreateGauge(string name)
        {
            return this.counters.GetOrAdd(name, (n) => new Counter(n, Metrics.GaugeTypeName));
        }

        public Summary GetOrCreateSummary(string name)
        {
            return this.summaries.GetOrAdd(name, (n) => new Summary(n));
        }

        public Histogram GetOrCreateHistogram(string name, double[] buckets)
        {
            return this.histograms.GetOrAdd(name, (n) => new Histogram(n, buckets));
        }

        public void WriteTo(TextWriter tw)
        {
            foreach (var item in this.counters)
            {
                item.Value.WriteTo(tw);
            }

            foreach (var item in this.summaries)
            {
                item.Value.WriteTo(tw);
            }

            foreach (var item in this.histograms)
            {
                item.Value.WriteTo(tw);
            }
        }

        public string GetMetricsAsString()
        {
            var sw = new StringWriter();
            this.WriteTo(sw);
            return sw.ToString();
        }
    }
}
