﻿using System.Text.RegularExpressions;

namespace SimpleMetrics
{
    static public class Metrics
    {
        internal static readonly Regex ValidNameRegex = new Regex(@"[a-zA-Z_:][a-zA-Z0-9_:]*", RegexOptions.Compiled);

        public const string CounterTypeName = "counter";

        public const string GaugeTypeName = "gauge";

        public const string SummaryTypeName = "summary";

        public static readonly Registry DefaultRegistry = new Registry();

        public static Counter CreateCounter(string name)
        {
            return DefaultRegistry.GetOrCreateCounter(name, CounterTypeName);
        }

        public static Counter CreateGauge(string name)
        {
            return DefaultRegistry.GetOrCreateCounter(name, GaugeTypeName);
        }

        public static Summary CreateSummary(string name)
        {
            return DefaultRegistry.GetOrCreateSummary(name);
        }
    }
}