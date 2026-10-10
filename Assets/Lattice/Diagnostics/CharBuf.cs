using System;

namespace Lattice.Diagnostics
{
    /// <summary>
    /// Text built into a reused char buffer, without allocating: for HUD and
    /// net graph text that changes every frame (TextMeshPro's SetCharArray
    /// takes the buffer as is).
    /// </summary>
    public sealed class CharBuf
    {
        public readonly char[] Chars;
        public int Length;

        public CharBuf(int capacity)
        {
            Chars = new char[capacity];
        }

        public CharBuf Clear()
        {
            Length = 0;
            return this;
        }

        public CharBuf Append(char c)
        {
            if (Length < Chars.Length)
                Chars[Length++] = c;
            return this;
        }

        public CharBuf Append(string s)
        {
            if (s == null)
                return this;
            int n = Math.Min(s.Length, Chars.Length - Length);
            s.CopyTo(0, Chars, Length, n);
            Length += n;
            return this;
        }

        public CharBuf Append(long v)
        {
            if (v < 0)
            {
                Append('-');
                v = -v;
            }
            int start = Length;
            do
            {
                Append((char)('0' + (int)(v % 10)));
                v /= 10;
            } while (v > 0 && Length < Chars.Length);
            Array.Reverse(Chars, start, Length - start);
            return this;
        }

        public CharBuf Append(ulong v) => Append((long)Math.Min(v, long.MaxValue));

        public CharBuf Append(int v) => Append((long)v);

        /// <summary>A number with <paramref name="decimals"/> decimals (0-4), rounded.</summary>
        public CharBuf Append(float v, int decimals)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                return Append(float.IsNaN(v) ? "nan" : v > 0 ? "inf" : "-inf");
            if (v < 0)
            {
                Append('-');
                v = -v;
            }
            decimals = Math.Max(0, Math.Min(4, decimals));
            long scale = decimals switch { 0 => 1, 1 => 10, 2 => 100, 3 => 1000, _ => 10000 };
            long scaled = (long)Math.Round(v * (double)scale, MidpointRounding.AwayFromZero);
            Append(scaled / scale);
            if (decimals > 0)
            {
                Append('.');
                long frac = scaled % scale;
                for (long d = scale / 10; d > 0; d /= 10)
                {
                    Append((char)('0' + (int)(frac / d)));
                    frac %= d;
                }
            }
            return this;
        }

        /// <summary>A number right-aligned in <paramref name="width"/> characters.</summary>
        public CharBuf Pad(float v, int decimals, int width)
        {
            int start = Length;
            Append(v, decimals);
            int written = Length - start;
            if (written < width)
            {
                int shift = Math.Min(width - written, Chars.Length - Length);
                Array.Copy(Chars, start, Chars, start + shift, written);
                for (int i = 0; i < shift; i++)
                    Chars[start + i] = ' ';
                Length += shift;
            }
            return this;
        }

        public override string ToString() => new string(Chars, 0, Length);
    }
}
