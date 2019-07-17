using System;
using System.Collections.Generic;

namespace SimpleMetrics
{
    internal class SampleStream
    {
        public double N;
        private readonly List<Sample> _samples = new List<Sample>();
        private readonly Invariant _invariant;

        public SampleStream(Invariant invariant)
        {
            this._invariant = invariant;
        }

        public void Merge(List<Sample> samples)
        {
            // TODO(beorn7): This tries to merge not only individual samples, but
            // whole summaries. The paper doesn't mention merging summaries at
            // all. Unittests show that the merging is inaccurate. Find out how to
            // do merges properly.

            double r = 0;
            var i = 0;

            for (var sampleIdx = 0; sampleIdx < samples.Count; sampleIdx++)
            {
                var sample = samples[sampleIdx];

                for (; i < this._samples.Count; i++)
                {
                    var c = this._samples[i];

                    if (c.Value > sample.Value)
                    {
                        // Insert at position i
                        this._samples.Insert(i, new Sample { Value = sample.Value, Width = sample.Width, Delta = Math.Max(sample.Delta, Math.Floor(this._invariant(this, r)) - 1) });
                        i++;
                        goto inserted;
                    }
                    r += c.Width;
                }
                this._samples.Add(new Sample { Value = sample.Value, Width = sample.Width, Delta = 0 });
                i++;

                inserted:
                this.N += sample.Width;
                r += sample.Width;
            }

            this.Compress();
        }

        private void Compress()
        {
            if (this._samples.Count < 2)
            {
                return;
            }

            var x = this._samples[this._samples.Count - 1];
            var xi = this._samples.Count - 1;
            var r = this.N - 1 - x.Width;

            for (var i = this._samples.Count - 2; i >= 0; i--)
            {
                var c = this._samples[i];

                if (c.Width + x.Width + x.Delta <= this._invariant(this, r))
                {
                    x.Width += c.Width;
                    this._samples[xi] = x;
                    this._samples.RemoveAt(i);
                    xi -= 1;
                }
                else
                {
                    x = c;
                    xi = i;
                }

                r -= c.Width;
            }
        }

        public void Reset()
        {
            this._samples.Clear();
            this.N = 0;
        }

        public int Count => (int)this.N;

        public double Query(double q)
        {
            var t = Math.Ceiling(q * this.N);
            t += Math.Ceiling(this._invariant(this, t) / 2);
            var p = this._samples[0];
            double r = 0;

            for (var i = 1; i < this._samples.Count; i++)
            {
                var c = this._samples[i];
                r += p.Width;

                if (r + c.Width + c.Delta > t)
                {
                    return p.Value;
                }

                p = c;
            }

            return p.Value;
        }

        public int SampleCount => this._samples.Count;
    }
}