using System;
using System.Globalization;
using System.Threading;

namespace SimpleMetrics
{
    // Keep this boxed so we can store it as a value in a concurrrent dictionary
    internal class ThreadSafeDouble
    {
        private long _value;

        public ThreadSafeDouble(double value)
        {
            this._value = BitConverter.DoubleToInt64Bits(value);
        }

        public double Value
        {
            get
            {
                return BitConverter.Int64BitsToDouble(Interlocked.Read(ref this._value));
            }
            set
            {
                Interlocked.Exchange(ref this._value, BitConverter.DoubleToInt64Bits(value));
            }
        }

        public void Add(double increment)
        {
            while (true)
            {
                var initialValue = this._value;
                var computedValue = BitConverter.Int64BitsToDouble(initialValue) + increment;

                //Compare exchange will only set the computed value if it is equal to the expected value
                //It will always return the the value of _value prior to the exchange (whether it happens or not)
                //So, only exit the loop if the value was what we expected it to be (initialValue) at the time of exchange otherwise another thread updated and we need to try again.
                if (initialValue == Interlocked.CompareExchange(ref this._value, BitConverter.DoubleToInt64Bits(computedValue), initialValue))
                {
                    return;
                }
            }
        }

        public override string ToString()
        {
            return this.Value.ToString(CultureInfo.InvariantCulture);
        }

        public override bool Equals(object obj)
        {
            return obj is ThreadSafeDouble ? this.Value.Equals(((ThreadSafeDouble)obj).Value) : this.Value.Equals(obj);
        }

        public override int GetHashCode()
        {
            return this.Value.GetHashCode();
        }
    }
}
