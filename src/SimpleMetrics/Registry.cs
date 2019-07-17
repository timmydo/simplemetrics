using System.Collections.Concurrent;
using System.IO;

namespace SimpleMetrics
{
    public sealed class Registry
    {
        private readonly ConcurrentDictionary<string, Counter> counters = new ConcurrentDictionary<string, Counter>();

        private readonly ConcurrentDictionary<string, Summary> summaries = new ConcurrentDictionary<string, Summary>();

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
        }

        public string GetMetricsAsString()
        {
            var sw = new StringWriter();
            this.WriteTo(sw);
            return sw.ToString();
        }
    }
}
