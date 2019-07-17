using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace SimpleMetrics
{
    public sealed class Summary
    {
        private const string QuantileLabel = "quantile";

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

        private readonly IReadOnlyList<QuantileEpsilonPair> _objectives;
        private readonly TimeSpan _maxAge;
        private readonly int _ageBuckets;
        private readonly int _bufCap;

        public Summary(string name)
        {
            if (!Metrics.ValidNameRegex.Match(name).Success)
            {
                throw new ArgumentOutOfRangeException("Invalid counter name: " + name);
            }

            this.Name = name;
            this.defaultInstance = new SummaryChild(this);

            _objectives = DefObjectivesArray;
            _maxAge = DefMaxAge;
            _ageBuckets = DefAgeBuckets;
            _bufCap = DefBufCap;

            if (_objectives.Count == 0)
            {
                _objectives = DefObjectivesArray;
            }

            if (_maxAge < TimeSpan.Zero)
                throw new ArgumentException($"Illegal max age {_maxAge}");

            if (_ageBuckets == 0)
                _ageBuckets = DefAgeBuckets;

            if (_bufCap == 0)
                _bufCap = DefBufCap;
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
            child.Observe(val);
        }

        public class SummaryChild
        {
            // Objectives defines the quantile rank estimates with their respective
            // absolute error. If Objectives[q] = e, then the value reported
            // for q will be the φ-quantile value for some φ between q-e and q+e.
            // The default value is DefObjectives.
            private IReadOnlyList<QuantileEpsilonPair> _objectives = new List<QuantileEpsilonPair>();
            private readonly string _parentName;
            private double[] _sortedObjectives;
            private double _sum;
            private uint _count;
            private SampleBuffer _hotBuf;
            private SampleBuffer _coldBuf;
            private QuantileStream[] _streams;
            private TimeSpan _streamDuration;
            private QuantileStream _headStream;
            private int _headStreamIdx;
            private DateTime _headStreamExpTime;
            private DateTime _hotBufExpTime;

            // Protects hotBuf and hotBufExpTime.
            private readonly object _bufLock = new object();

            // Protects every other moving part.
            // Lock bufMtx before mtx if both are needed.
            private readonly object _lock = new object();

            // MaxAge defines the duration for which an observation stays relevant
            // for the summary. Must be positive. The default value is DefMaxAge.
            private TimeSpan _maxAge;

            // AgeBuckets is the number of buckets used to exclude observations that
            // are older than MaxAge from the summary. A higher number has a
            // resource penalty, so only increase it if the higher resolution is
            // really required. For very high observation rates, you might want to
            // reduce the number of age buckets. With only one age bucket, you will
            // effectively see a complete reset of the summary each time MaxAge has
            // passed. The default value is DefAgeBuckets.
            private int _ageBuckets;

            // BufCap defines the default sample stream buffer size.  The default
            // value of DefBufCap should suffice for most uses. If there is a need
            // to increase the value, a multiple of 500 is recommended (because that
            // is the internal buffer size of the underlying package
            // "github.com/bmizerany/perks/quantile").      
            private int _bufCap;

            public SummaryChild(Summary parent)
            {
                _parentName = parent.Name;
                _objectives = parent._objectives;
                _maxAge = parent._maxAge;
                _ageBuckets = parent._ageBuckets;
                _bufCap = parent._bufCap;

                _sortedObjectives = new double[_objectives.Count];
                _hotBuf = new SampleBuffer(_bufCap);
                _coldBuf = new SampleBuffer(_bufCap);
                _streamDuration = new TimeSpan(_maxAge.Ticks / _ageBuckets);
                _headStreamExpTime = DateTime.UtcNow.Add(_streamDuration);
                _hotBufExpTime = _headStreamExpTime;

                _streams = new QuantileStream[_ageBuckets];
                for (var i = 0; i < _ageBuckets; i++)
                {
                    _streams[i] = QuantileStream.NewTargeted(_objectives);
                }

                _headStream = _streams[0];

                for (var i = 0; i < _objectives.Count; i++)
                {
                    _sortedObjectives[i] = _objectives[i].Quantile;
                }

                Array.Sort(_sortedObjectives);
            }

            public void Observe(double val)
            {
                Observe(val, DateTime.UtcNow);
            }

            internal void Observe(double val, DateTime now)
            {
                if (double.IsNaN(val))
                    return;

                lock (_bufLock)
                {
                    if (now > _hotBufExpTime)
                    {
                        Flush(now);
                    }

                    _hotBuf.Append(val);

                    if (_hotBuf.IsFull)
                    {
                        Flush(now);
                    }
                }

            }

            public void WriteTo(TextWriter tw, string instance)
            {
                var now = DateTime.UtcNow;

                double count;
                double sum;
                var values = new List<(double quantile, double value)>(_objectives.Count);

                lock (_bufLock)
                {
                    lock (_lock)
                    {
                        // Swap bufs even if hotBuf is empty to set new hotBufExpTime.
                        SwapBufs(now);
                        FlushColdBuf();

                        count = _count;
                        sum = _sum;

                        for (var i = 0; i < _sortedObjectives.Length; i++)
                        {
                            var quantile = _sortedObjectives[i];
                            var value = _headStream.Count == 0 ? double.NaN : _headStream.Query(quantile);

                            values.Add((quantile, value));
                        }
                    }
                }


                // write the sum
                tw.Write(_parentName);
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
                tw.Write(_parentName);
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
                    tw.Write(_parentName);
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
                lock (_lock)
                {
                    SwapBufs(now);

                    // Go version flushes on a separate goroutine, but doing this on another
                    // thread actually makes the benchmark tests slower in .net
                    FlushColdBuf();
                }
            }

            // SwapBufs needs mtx AND bufMtx locked, coldBuf must be empty.
            private void SwapBufs(DateTime now)
            {
                if (!_coldBuf.IsEmpty)
                {
                    throw new InvalidOperationException("coldBuf is not empty");
                }

                var temp = _hotBuf;
                _hotBuf = _coldBuf;
                _coldBuf = temp;

                // hotBuf is now empty and gets new expiration set.
                while (now > _hotBufExpTime)
                {
                    _hotBufExpTime = _hotBufExpTime.Add(_streamDuration);
                }
            }

            // FlushColdBuf needs mtx locked. 
            private void FlushColdBuf()
            {
                for (var bufIdx = 0; bufIdx < _coldBuf.Position; bufIdx++)
                {
                    var value = _coldBuf[bufIdx];

                    for (var streamIdx = 0; streamIdx < _streams.Length; streamIdx++)
                    {
                        _streams[streamIdx].Insert(value);
                    }

                    _count++;
                    _sum += value;
                }

                _coldBuf.Reset();
                MaybeRotateStreams();
            }

            // MaybeRotateStreams needs mtx AND bufMtx locked.
            private void MaybeRotateStreams()
            {
                while (!_hotBufExpTime.Equals(_headStreamExpTime))
                {
                    _headStream.Reset();
                    _headStreamIdx++;

                    if (_headStreamIdx >= _streams.Length)
                        _headStreamIdx = 0;

                    _headStream = _streams[_headStreamIdx];
                    _headStreamExpTime = _headStreamExpTime.Add(_streamDuration);
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
