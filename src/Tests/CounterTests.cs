using SimpleMetrics;
using System;
using Xunit;

namespace Tests
{
    public class CounterTests
    {
        [Fact]
        public void TestInitialValue()
        {
            var counter = Metrics.CreateCounter("abc");

            Assert.Equal(0, counter.Value);
            counter.Increment();
            counter.Increment(2);
            counter.Increment("def");
            counter.Increment("ghi", 5000);

        }

        [Fact]
        public void TestIncrement()
        {
            var counter = Metrics.CreateCounter("abc2");

            counter.Increment();

            Assert.Equal(1, counter.Value);
        }

        [Fact]
        public void TestIncrementAmount()
        {
            var counter = Metrics.CreateCounter("abc3");

            counter.Increment(2);

            Assert.Equal(2, counter.Value);
        }

        [Fact]
        public void TestIncrementInstance()
        {
            var counter = Metrics.CreateCounter("abc4");

            counter.Increment("a");

            Assert.Equal(1, counter.GetInstanceValue("a"));
        }

        [Fact]
        public void TestIncrementInstanceAmount()
        {
            var counter = Metrics.CreateCounter("abc5");

            counter.Increment("a", 2);

            Assert.Equal(2, counter.GetInstanceValue("a"));
        }

        [Fact]
        public void TestSet()
        {
            var counter = Metrics.CreateCounter("abc6");

            counter.Set(222);

            Assert.Equal(222, counter.Value);
        }

        [Fact]
        public void TestSetInstance()
        {
            var counter = Metrics.CreateCounter("abc7");

            counter.Set("a", 333);

            Assert.Equal(333, counter.GetInstanceValue("a"));
        }
    }
}
