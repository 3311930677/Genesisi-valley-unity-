using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>Deterministic hash/value noise for presentation-only variation (never used by the simulation).</summary>
    public static class Noise
    {
        public static uint Hash(int x, int z, uint seed = 0)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)z * 668265263u + seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int x, int z, uint seed = 0) => (Hash(x, z, seed) & 0xFFFFFF) / 16777215f;

        public static float Value(float x, float z, uint seed = 0)
        {
            int xi = Mathf.FloorToInt(x), zi = Mathf.FloorToInt(z);
            float fx = x - xi, fz = z - zi;
            fx = fx * fx * (3 - 2 * fx); fz = fz * fz * (3 - 2 * fz);
            float a = Hash01(xi, zi, seed), b = Hash01(xi + 1, zi, seed), c = Hash01(xi, zi + 1, seed), d = Hash01(xi + 1, zi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        /// <summary>Fractal value noise in [0,1].</summary>
        public static float Fbm(float x, float z, int octaves, uint seed = 0)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x, z, seed + (uint)i * 17u) * amp;
                norm += amp; amp *= 0.5f; x *= 2.03f; z *= 2.03f;
            }
            return sum / norm;
        }

        public static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }
    }
}
