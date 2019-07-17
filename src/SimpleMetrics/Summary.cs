using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace SimpleMetrics
{
    public sealed class Summary
    {
        private readonly ConcurrentDictionary<string, SummaryChild> instances = new ConcurrentDictionary<string, SummaryChild>();
        private readonly SummaryChild defaultInstance;

        public string Name { get; private set; }

        /// <summary>
        /// Client library guidelines say that the summary should default to not measuring quantiles.
        /// https://prometheus.io/docs/instrumenting/writing_clientlibs/#summary
        /// </summary>
        internal static readonly QuantileEpsilonPair[] DefObjectivesArray = new QuantileEpsilonPair[]
        {
            new QuantileEpsilonPair(0.5, 0.05),
            new QuantileEpsilonPair(0.9, 0.05),
            new QuantileEpsilonPair(0.95, 0.01),
            new QuantileEpsilonPair(0.99, 0.01),
        };

        // Default Summary quantile values.
        public static readonly IList<QuantileEpsilonPair> DefObjectives = new List<QuantileEpsilonPair>(DefObjectivesArray);

        // Default duration for which observations stay relevant
        public static readonly TimeSpan DefMaxAge = TimeSpan.FromMinutes(10);

        // Default number of buckets used to calculate the age of observations
        public static readonly int DefAgeBuckets = 5;

        // Standard buffer size for collecting Summary observations
        public static readonly int DefBufCap = 500;

        private readonly IReadOnlyList<QuantileEpsilonPair> objectives;
        private readonly TimeSpan maxAge;
        private readonly int ageBuckets;
        private readonly int bufCap;

        public Summary(string name)
        {
            if (!Metrics.ValidNameRegex.Match(name).Success)
            {
                throw new ArgumentOutOfRangeException("Invalid counter name: " + name);
            }

            this.Name = name;
            this.objectives = DefObjectivesArray;
            this.maxAge = DefMaxAge;
            this.ageBuckets = DefAgeBuckets;
            this.bufCap = DefBufCap;

            if (this.objectives.Count == 0)
            {
                this.objectives = DefObjectivesArray;
            }

            if (this.maxAge < TimeSpan.Zero)
            {
                throw new ArgumentException($"Illegal max age {this.maxAge}");
            }

            if (this.ageBuckets == 0)
            {
                this.ageBuckets = DefAgeBuckets;
            }

            if (this.bufCap == 0)
            {
                this.bufCap = DefBufCap;
            }

            // initialize this after the others as the child might try to reference the parent
            this.defaultInstance = new SummaryChild(this);
        }

        public void Observe(double val)
        {
            this.defaultInstance.Observe(val);
        }

        internal void Observe(double val, DateTime dateTime)
        {
            this.defaultInstance.Observe(val, dateTime);
        }

        public void Observe(string instance, double val)
        {
            var child = this.instances.GetOrAdd(instance, (key) => new SummaryChild(this));
            child.Observe(val);
        }

        internal void Observe(string instance, double val, DateTime timestamp)
        {
            var child = this.instances.GetOrAdd(instance, (key) => new SummaryChild(this));
            child.Observe(val, timestamp);
        }

        public class SummaryChild
        {
            // Objectives defines the quantile rank estimates with their respective
            // absolute error. If Objectives[q] = e, then the value reported
            // for q will be the φ-quantile value for some φ between q-e and q+e.
            // The default value is DefObjectives.
            private readonly IReadOnlyList<QuantileEpsilonPair> objectives = new List<QuantileEpsilonPair>();
            private readonly string parentName;
            private readonly double[] sortedObjectives;
            private double sum;
            private uint count;
            private SampleBuffer hotBuf;
            private SampleBuffer coldBuf;
            private readonly QuantileStream[] streams;
            private readonly TimeSpan streamDuration;
            private QuantileStream headStream;
            private int headStreamIdx;
            private DateTime headStreamExpTime;
            private DateTime hotBufExpTime;

            // Protects hotBuf and hotBufExpTime.
            private readonly object _bufLock = new object();

            // Protects every other moving part.
            // Lock bufMtx before mtx if both are needed.
            private readonly object _lock = new object();

            // MaxAge defines the duration for which an observation stays relevant
            // for the summary. Must be positive. The default value is DefMaxAge.
            private readonly TimeSpan maxAge;

            // AgeBuckets is the number of buckets used to exclude observations that
            // are older than MaxAge from the summary. A higher number has a
            // resource penalty, so only increase it if the higher resolution is
            // really required. For very high observation rates, you might want to
            // reduce the number of age buckets. With only one age bucket, you will
            // effectively see a complete reset of the summary each time MaxAge has
            // passed. The default value is DefAgeBuckets.
            private readonly int ageBuckets;

            // BufCap defines the default sample stream buffer size.  The default
            // value of DefBufCap should suffice for most uses. If there is a need
            // to increase the value, a multiple of 500 is recommended (because that
            // is the internal buffer size of the underlying package
            // "github.com/bmizerany/perks/quantile").      
            private readonly int bufCap;

            public SummaryChild(Summary parent)
            {
                this.parentName = parent.Name;
                this.objectives = parent.objectives;
                this.maxAge = parent.maxAge;
                this.ageBuckets = parent.ageBuckets;
                this.bufCap = parent.bufCap;

                this.sortedObjectives = new double[this.objectives.Count];
                this.hotBuf = new SampleBuffer(this.bufCap);
                this.coldBuf = new SampleBuffer(this.bufCap);
                this.streamDuration = new TimeSpan(this.maxAge.Ticks / this.ageBuckets);
                this.headStreamExpTime = DateTime.UtcNow.Add(this.streamDuration);
                this.hotBufExpTime = this.headStreamExpTime;

                this.streams = new QuantileStream[this.ageBuckets];
                for (var i = 0; i < this.ageBuckets; i++)
                {
                    this.streams[i] = QuantileStream.NewTargeted(this.objectives);
                }

                this.headStream = this.streams[0];

                for (var i = 0; i < this.objectives.Count; i++)
                {
                    this.sortedObjectives[i] = this.objectives[i].Quantile;
                }

                Array.Sort(this.sortedObjectives);
            }

            public void Observe(double val)
            {
                this.Observe(val, DateTime.UtcNow);
            }

            internal void Observe(double val, DateTime now)
            {
                if (double.IsNaN(val))
                {
                    return;
                }

                lock (this._bufLock)
                {
                    if (now > this.hotBufExpTime)
                    {
                        this.Flush(now);
                    }

                    this.hotBuf.Append(val);

                    if (this.hotBuf.IsFull)
                    {
                        this.Flush(now);
                    }
                }

            }

            public void WriteTo(TextWriter tw, string instance)
            {
                var now = DateTime.UtcNow;

                double count;
                double sum;
                var values = new List<(double quantile, double value)>(this.objectives.Count);

                lock (this._bufLock)
                {
                    lock (this._lock)
                    {
                        // Swap bufs even if hotBuf is empty to set new hotBufExpTime.
                        this.SwapBufs(now);
                        this.FlushColdBuf();

                        count = this.count;
                        sum = this.sum;

                        for (var i = 0; i < this.sortedObjectives.Length; i++)
                        {
                            var quantile = this.sortedObjectives[i];
                            var value = this.headStream.Count == 0 ? double.NaN : this.headStream.Query(quantile);

                            values.Add((quantile, value));
                        }
                    }
                }


                // write the sum
                tw.Write(this.parentName);
                if (instance == null)
                {
                    tw.Write("_sum ");
                }
                else
                {
                    tw.Write("_sum{i=\"");
                    tw.Write(instance);
                    tw.Write("\"} ");
                }

                tw.Write(sum);
                tw.WriteLine();


                // write the count
                tw.Write(this.parentName);
                if (instance == null)
                {
                    tw.Write("_count ");
                }
                else
                {
                    tw.Write("_count{i=\"");
                    tw.Write(instance);
                    tw.Write("\"} ");
                }

                tw.Write(count);
                tw.WriteLine();

                // write the quantiles for this instance
                for (var i = 0; i < values.Count; i++)
                {
                    tw.Write(this.parentName);
                    if (instance == null)
                    {
                        tw.Write("{");
                    }
                    else
                    {
                        tw.Write("{i=\"");
                        tw.Write(instance);
                        tw.Write(",");
                    }

                    tw.Write("quantile=\"");
                    tw.Write(values[i].quantile);
                    tw.Write("\"} ");
                    tw.Write(values[i].value);
                    tw.WriteLine();
                }
            }

            // Flush needs bufMtx locked.
            private void Flush(DateTime now)
            {
                lock (this._lock)
                {
                    this.SwapBufs(now);

                    // Go version flushes on a separate goroutine, but doing this on another
                    // thread actually makes the benchmark tests slower in .net
                    this.FlushColdBuf();
                }
            }

            // SwapBufs needs mtx AND bufMtx locked, coldBuf must be empty.
            private void SwapBufs(DateTime now)
            {
                if (!this.coldBuf.IsEmpty)
                {
                    throw new InvalidOperationException("coldBuf is not empty");
                }

                var temp = this.hotBuf;
                this.hotBuf = this.coldBuf;
                this.coldBuf = temp;

                // hotBuf is now empty and gets new expiration set.
                while (now > this.hotBufExpTime)
                {
                    this.hotBufExpTime = this.hotBufExpTime.Add(this.streamDuration);
                }
            }

            // FlushColdBuf needs mtx locked. 
            private void FlushColdBuf()
            {
                for (var bufIdx = 0; bufIdx < this.coldBuf.Position; bufIdx++)
                {
                    var value = this.coldBuf[bufIdx];

                    for (var streamIdx = 0; streamIdx < this.streams.Length; streamIdx++)
                    {
                        this.streams[streamIdx].Insert(value);
                    }

                    this.count++;
                    this.sum += value;
                }

                this.coldBuf.Reset();
                this.MaybeRotateStreams();
            }

            // MaybeRotateStreams needs mtx AND bufMtx locked.
            private void MaybeRotateStreams()
            {
                while (!this.hotBufExpTime.Equals(this.headStreamExpTime))
                {
                    this.headStream.Reset();
                    this.headStreamIdx++;

                    if (this.headStreamIdx >= this.streams.Length)
                    {
                        this.headStreamIdx = 0;
                    }

                    this.headStream = this.streams[this.headStreamIdx];
                    this.headStreamExpTime = this.headStreamExpTime.Add(this.streamDuration);
                }
            }
        }


        public void WriteTo(TextWriter tw)
        {
            tw.Write("# TYPE ");
            tw.Write(this.Name);
            tw.Write(" ");
            tw.Write(Metrics.SummaryTypeName);
            tw.WriteLine();

            this.defaultInstance.WriteTo(tw, null);
            foreach (var instance in this.instances)
            {
                instance.Value.WriteTo(tw, instance.Key);
            }
        }
    }
}
