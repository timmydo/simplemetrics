using System;
using System.Collections.Concurrent;
using System.IO;

namespace SimpleMetrics
{
    public sealed class Histogram
    {
        private readonly ConcurrentDictionary<string, HistogramChild> instances = new ConcurrentDictionary<string, HistogramChild>();
        private readonly string typeName;
        private readonly double[] bucketIntervals;
        private readonly HistogramChild defaultInstance;

        public string Name { get; private set; }

        public Histogram(string name, double[] bucketIntervals)
        {
            if (!Metrics.ValidNameRegex.Match(name).Success)
            {
                throw new ArgumentOutOfRangeException("Invalid counter name: " + name);
            }

            if (bucketIntervals.Length == 0)
            {
                throw new ArgumentException("Histogram must have at least one bucket");
            }

            if (bucketIntervals.Length > 1)
            {
                for (var i = 1; i < bucketIntervals.Length; i++)
                {
                    if (bucketIntervals[i] <= bucketIntervals[i-1])
                    {
                        throw new ArgumentOutOfRangeException("Histogram buckets must increaese");
                    }
                }
            }

            this.Name = name;
            this.typeName = "histogram";
            this.bucketIntervals = bucketIntervals;
            this.defaultInstance = new HistogramChild(bucketIntervals);
        }

        public void Observe(double amount)
        {
            this.defaultInstance.Observe(amount);
        }

        public void Observe(string instance, double amount)
        {
            if (instance == null)
            {
                this.Observe(amount);
            }
            else
            {
                var val = this.instances.GetOrAdd(instance, (iname) => new HistogramChild(this.bucketIntervals));
                val.Observe(amount);
            }
        }

        public void WriteTo(TextWriter tw)
        {
            tw.Write("# TYPE ");
            tw.Write(this.Name);
            tw.Write(' ');
            tw.Write(this.typeName);
            tw.Write('\n');

            this.defaultInstance.WriteTo(tw, this.Name, null);

            foreach (var item in this.instances)
            {
                item.Value.WriteTo(tw, this.Name, item.Key);
            }
        }

        private class HistogramChild
        {
            private readonly double[] bucketIntervals;
            private readonly ThreadSafeDouble count;
            private readonly ThreadSafeDouble sum;
            private readonly ThreadSafeDoubleStruct[] values;

            public HistogramChild(double[] bucketIntervals)
            {
                this.bucketIntervals = bucketIntervals;
                this.count = new ThreadSafeDouble(0);
                this.sum = new ThreadSafeDouble(0);
                this.values = new ThreadSafeDoubleStruct[bucketIntervals.Length + 1];
            }

            public void Observe(double amount)
            {
                if (double.IsNaN(amount))
                {
                    return;
                }

                this.sum.Add(amount);
                this.count.Add(1.0);
                // amount is always less than infinity
                this.values[this.bucketIntervals.Length].Add(1);

                for (var i = this.bucketIntervals.Length - 1; i >= 0; i--)
                {
                    if (amount > this.bucketIntervals[i])
                    {
                        break;
                    }

                    this.values[i].Add(1.0);
                }
            }

            internal void WriteTo(TextWriter tw, string metric, string instance)
            {
                if (instance == null)
                {
                    tw.Write(metric);
                    tw.Write("_sum ");
                    tw.Write(this.sum.Value);
                    tw.Write('\n');

                    tw.Write(metric);
                    tw.Write("_count ");
                    tw.Write(this.count.Value);
                    tw.Write('\n');

                    for (var i = 0; i < this.bucketIntervals.Length; i++ )
                    {
                        tw.Write(metric);
                        tw.Write("_bucket{le=\"");
                        tw.Write(this.bucketIntervals[i]);
                        tw.Write("\"} ");
                        tw.Write(this.values[i].Value);
                        tw.Write('\n');
                    }

                    tw.Write(metric);
                    tw.Write("_bucket{le=\"+Inf\"} ");
                    tw.Write(this.values[this.bucketIntervals.Length].Value);
                    tw.Write('\n');
                }
                else
                {
                    tw.Write(metric);
                    tw.Write("_sum{i=\"");
                    tw.Write(instance);
                    tw.Write("\"} ");
                    tw.Write(this.sum.Value);
                    tw.Write('\n');

                    tw.Write(metric);
                    tw.Write("_count{i=\"");
                    tw.Write(instance);
                    tw.Write("\"} ");
                    tw.Write(this.count.Value);
                    tw.Write('\n');

                    for (var i = 0; i < this.bucketIntervals.Length; i++)
                    {
                        tw.Write(metric);
                        tw.Write("_bucket{i=\"");
                        tw.Write(instance);
                        tw.Write("\",le=\"");
                        tw.Write(this.bucketIntervals[i]);
                        tw.Write("\"} ");
                        tw.Write(this.values[i].Value);
                        tw.Write('\n');
                    }

                    tw.Write(metric);
                    tw.Write("_bucket{i=\"");
                    tw.Write(instance);
                    tw.Write("\",le=\"+Inf\"} ");
                    tw.Write(this.values[this.bucketIntervals.Length].Value);
                    tw.Write('\n');
                }
            }
        }
    }
}
