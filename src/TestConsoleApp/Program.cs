using SimpleMetrics;
using System;

namespace TestConsoleApp
{
    class Program
    {
        static void Main()
        {
            var counter = Metrics.CreateCounter("abc");
            counter.Increment();
            counter.Increment(2);
            counter.Increment("def");
            counter.Increment("ghi", 5000);

            var summary = Metrics.CreateSummary("my_summary");
            for (int i = 0; i < 100; i++)
            {
                summary.Observe(i);
                summary.Observe("inst", i * 2);
            }

            var instance = DotNetStats.Register(Metrics.DefaultRegistry);
            instance.UpdateMetrics();

            Metrics.DefaultRegistry.WriteTo(Console.Out);
        }
    }
}
