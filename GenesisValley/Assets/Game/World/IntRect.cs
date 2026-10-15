using System;

namespace GenesisValley.World
{
    /// <summary>Inclusive integer rectangle on a grid (x = column, z = row).</summary>
    public struct IntRect
    {
        public int XMin, ZMin, XMax, ZMax;

        public static readonly IntRect Empty = new IntRect { XMin = 0, ZMin = 0, XMax = -1, ZMax = -1 };

        public IntRect(int xMin, int zMin, int xMax, int zMax) { XMin = xMin; ZMin = zMin; XMax = xMax; ZMax = zMax; }

        public bool IsEmpty => XMax < XMin || ZMax < ZMin;
        public int Width => IsEmpty ? 0 : XMax - XMin + 1;
        public int Height => IsEmpty ? 0 : ZMax - ZMin + 1;

        public bool Contains(int x, int z) => !IsEmpty && x >= XMin && x <= XMax && z >= ZMin && z <= ZMax;

        public IntRect Union(IntRect o)
        {
            if (IsEmpty) return o;
            if (o.IsEmpty) return this;
            return new IntRect(Math.Min(XMin, o.XMin), Math.Min(ZMin, o.ZMin), Math.Max(XMax, o.XMax), Math.Max(ZMax, o.ZMax));
        }

        public IntRect Clamp(int width, int height)
        {
            if (IsEmpty) return this;
            return new IntRect(Math.Max(0, XMin), Math.Max(0, ZMin), Math.Min(width - 1, XMax), Math.Min(height - 1, ZMax));
        }

        public IntRect Expand(int n) => IsEmpty ? this : new IntRect(XMin - n, ZMin - n, XMax + n, ZMax + n);

        public override string ToString() => IsEmpty ? "(empty)" : $"({XMin},{ZMin})-({XMax},{ZMax})";
    }
}
