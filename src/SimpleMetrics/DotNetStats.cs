using System;
using System.Diagnostics;

namespace SimpleMetrics
{
    /// <summary>
    /// Collects basic .NET metrics about the current process. This is not meant to be an especially serious collector,
    /// more of a producer of sample data so users of the library see something when they install it.
    /// </summary>
    public sealed class DotNetStats
    {
        /// <summary>
        /// Registers the .NET metrics in the specified registry.
        /// </summary>
        public static DotNetStats Register(Registry registry)
        {
            var instance = new DotNetStats();
            instance.RegisterMetrics(registry);
            return instance;
        }

        public void UpdateMetrics()
        {
            try
            {
                this.process.Refresh();

                for (var gen = 0; gen <= GC.MaxGeneration; gen++)
                {
                    this.collectionCounts.Set(gen.ToString(), GC.CollectionCount(gen));
                }

                this.totalMemory.Set(GC.GetTotalMemory(false));
                this.virtualMemorySize.Set(this.process.VirtualMemorySize64);
                this.workingSet.Set(this.process.WorkingSet64);
                this.privateMemorySize.Set(this.process.PrivateMemorySize64);
                this.cpuTotal.Increment(Math.Max(0, this.process.TotalProcessorTime.TotalSeconds - this.cpuTotal.Value));
                this.openHandles.Set(this.process.HandleCount);
                this.numThreads.Set(this.process.Threads.Count);
            }
            catch (Exception)
            {
            }
        }

        private readonly Process process;
        private Counter totalMemory;
        private Counter virtualMemorySize;
        private Counter workingSet;
        private Counter privateMemorySize;
        private Counter cpuTotal;
        private Counter openHandles;
        private Counter collectionCounts;
        private Counter startTime;
        private Counter numThreads;

        private DotNetStats()
        {
            this.process = Process.GetCurrentProcess();
        }

        private void RegisterMetrics(Registry registry)
        {

            this.collectionCounts = registry.GetOrCreateCounter("dotnet_collection_count_total");

            // Metrics that make sense to compare between all operating systems
            // Note that old versions of pushgateway errored out if different metrics had same name but different help string.
            // This is fixed in newer versions but keep the help text synchronized with the Go implementation just in case.
            // See https://github.com/prometheus/pushgateway/issues/194
            // and https://github.com/prometheus-net/prometheus-net/issues/89
            this.startTime = registry.GetOrCreateGauge("process_start_time_seconds");
            this.cpuTotal = registry.GetOrCreateCounter("process_cpu_seconds_total");

            this.virtualMemorySize = registry.GetOrCreateGauge("process_virtual_memory_bytes");
            this.workingSet = registry.GetOrCreateGauge("process_working_set_bytes");
            this.privateMemorySize = registry.GetOrCreateGauge("process_private_memory_bytes");
            this.openHandles = registry.GetOrCreateGauge("process_open_handles");
            this.numThreads = registry.GetOrCreateGauge("process_num_threads");

            // .net specific metrics
            this.totalMemory = registry.GetOrCreateGauge("dotnet_total_memory_bytes");

            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            this.startTime.Set((this.process.StartTime.ToUniversalTime() - epoch).TotalSeconds);
        }
    }
}