namespace SimpleMetrics
{
    public struct QuantileEpsilonPair
    {
        public QuantileEpsilonPair(double quantile, double epsilon)
        {
            this.Quantile = quantile;
            this.Epsilon = epsilon;
        }

        public double Quantile { get; }
        public double Epsilon { get; }
    }
}
