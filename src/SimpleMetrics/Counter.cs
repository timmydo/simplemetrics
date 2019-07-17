using System;
using System.Collections.Concurrent;
using System.IO;

namespace SimpleMetrics
{
    public sealed class Counter
    {
        private readonly ConcurrentDictionary<string, ThreadSafeDouble> instances = new ConcurrentDictionary<string, ThreadSafeDouble>();
        private readonly string typeName;
        private readonly ThreadSafeDouble value = new ThreadSafeDouble(0.0);

        public string Name { get; private set; }

        public double Value
        {
            get
            {
                return this.value.Value;
            }
        }

        public Counter(string name, string typeName)
        {
            if (!Metrics.ValidNameRegex.Match(name).Success)
            {
                throw new ArgumentOutOfRangeException("Invalid counter name: " + name);
            }

            this.Name = name;
            this.typeName = typeName;
        }

        public double GetInstanceValue(string instance)
        {
            return this.instances.TryGetValue(instance, out var val) ? val.Value : -1;
        }

        public void Increment()
        {
            this.Increment(1.0);
        }

        public void Increment(double amount)
        {
            this.value.Add(amount);
        }

        public void Increment(string instance)
        {
            this.Increment(instance, 1.0);
        }

        public void Increment(string instance, double amount)
        {
            var val = this.instances.GetOrAdd(instance, (iname) => new ThreadSafeDouble(0.0));
            val.Add(amount);
        }

        public void Set(string instance, double amount)
        {
            var c = this.instances.GetOrAdd(instance, (iname) => new ThreadSafeDouble(0.0));
            c.Value = amount;
        }

        public void Set(double amount)
        {
            this.value.Value = amount;
        }

        public void WriteTo(TextWriter tw)
        {
            tw.Write("# TYPE ");
            tw.Write(this.Name);
            tw.Write(" ");
            tw.Write(this.typeName);
            tw.WriteLine();

            tw.Write(this.Name);
            tw.Write(" ");
            tw.Write(this.Value);
            tw.WriteLine();

            foreach (var item in this.instances)
            {
                tw.Write(this.Name);
                tw.Write("{i=\"");
                tw.Write(item.Key);
                tw.Write("\"} ");
                tw.Write(item.Value);
                tw.WriteLine();
            }
        }
    }
}
