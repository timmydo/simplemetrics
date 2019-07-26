using System;
using System.Collections.Generic;
using System.Text;

namespace SimpleMetrics
{
    public static class HistogramBuckets
    {
        public static double[] SampleExponentialBuckets = ExponentialBuckets(0.001, 2, 16);

        // From https://github.com/prometheus/client_golang/blob/master/prometheus/histogram.go
        /// <summary>  
        ///  Creates '<paramref name="count"/>' buckets, where the lowest bucket has an
        ///  upper bound of '<paramref name="start"/>' and each following bucket's upper bound is '<paramref name="factor"/>'
        ///  times the previous bucket's upper bound.
        /// 
        ///  The function throws if '<paramref name="count"/>' is 0 or negative, if '<paramref name="start"/>' is 0 or negative,
        ///  or if '<paramref name="factor"/>' is less than or equal 1.
        /// </summary>
        /// <param name="start">The upper bound of the lowest bucket. Must be positive.</param>
        /// <param name="factor">The factor to increase the upper bound of subsequent buckets. Must be greater than 1.</param>
        /// <param name="count">The number of buckets to create. Must be positive.</param>
        public static double[] ExponentialBuckets(double start, double factor, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentException($"{nameof(ExponentialBuckets)} needs a positive {nameof(count)}");
            }

            if (start <= 0)
            {
                throw new ArgumentException($"{nameof(ExponentialBuckets)} needs a positive {nameof(start)}");
            }

            if (factor <= 1)
            {
                throw new ArgumentException($"{nameof(ExponentialBuckets)} needs a {nameof(factor)} greater than 1");
            }

            var buckets = new double[count];

            for (var i = 0; i < buckets.Length; i++)
            {
                buckets[i] = start;
                start *= factor;
            }

            return buckets;
        }

        // From https://github.com/prometheus/client_golang/blob/master/prometheus/histogram.go
        /// <summary>  
        ///  Creates '<paramref name="count"/>' buckets, where the lowest bucket has an
        ///  upper bound of '<paramref name="start"/>' and each following bucket's upper bound is the upper bound of the
        ///  previous bucket, incremented by '<paramref name="width"/>'
        /// 
        ///  The function throws if '<paramref name="count"/>' is 0 or negative.
        /// </summary>
        /// <param name="start">The upper bound of the lowest bucket.</param>
        /// <param name="width">The width of each bucket (distance between lower and upper bound).</param>
        /// <param name="count">The number of buckets to create. Must be positive.</param>
        public static double[] LinearBuckets(double start, double width, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentException($"{nameof(LinearBuckets)} needs a positive {nameof(count)}");
            }

            var buckets = new double[count];

            for (var i = 0; i < buckets.Length; i++)
            {
                buckets[i] = start;
                start += width;
            }

            return buckets;
        }
    }
}
