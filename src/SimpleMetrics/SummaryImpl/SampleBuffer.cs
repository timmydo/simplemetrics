using System;

namespace SimpleMetrics
{
    internal class SampleBuffer
    {
        private readonly double[] _buffer;

        public SampleBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Must be > 0");
            }

            this._buffer = new double[capacity];
            this.Position = 0;
        }

        public void Append(double value)
        {
            if (this.Position >= this.Capacity)
            {
                throw new InvalidOperationException("Buffer is full");
            }

            this._buffer[this.Position++] = value;
        }

        public double this[int index]
        {
            get
            {
                if (index > this.Position)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), "Index is greater than position");
                }

                return this._buffer[index];
            }
        }

        public void Reset()
        {
            this.Position = 0;
        }

        public int Position { get; private set; }

        public int Capacity => this._buffer.Length;
        public bool IsFull => this.Position == this.Capacity;
        public bool IsEmpty => this.Position == 0;
    }
}
