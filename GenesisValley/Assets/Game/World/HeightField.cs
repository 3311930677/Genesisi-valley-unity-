using System;

namespace GenesisValley.World
{
    /// <summary>Optional per-point mask (world x, world z) -> [0..1] multiplier, used for building protection zones.</summary>
    public delegate float BrushMask(float worldX, float worldZ);

    /// <summary>Remembers the first "before" value of every sample touched by one transaction so it can be undone.</summary>
    public sealed class HeightEditRecorder
    {
        public readonly System.Collections.Generic.Dictionary<int, float> Before = new System.Collections.Generic.Dictionary<int, float>();
        public IntRect Bounds = IntRect.Empty;

        public void Record(int index, float before, int x, int z)
        {
            if (Before.ContainsKey(index)) return;
            Before[index] = before;
            Bounds = Bounds.Union(new IntRect(x, z, x, z));
        }
    }

    /// <summary>
    /// Authoritative CPU height data (GDD §11.1): 513×513 samples over 500 m, normalised to 0..1 of a 100 m range.
    /// Layout is row-major [z * Resolution + x], matching Unity TerrainData.GetHeights[y=z, x].
    /// </summary>
    public sealed class HeightField
    {
        public readonly int Resolution;
        public readonly float WorldSize;
        public readonly float VerticalRange;
        public readonly float[] Heights;

        public HeightField(int resolution, float worldSize, float verticalRange)
        {
            if (resolution < 2) throw new ArgumentOutOfRangeException(nameof(resolution));
            Resolution = resolution;
            WorldSize = worldSize;
            VerticalRange = verticalRange;
            Heights = new float[resolution * resolution];
        }

        public float Spacing => WorldSize / (Resolution - 1);

        public float GetNormalized(int x, int z) => Heights[z * Resolution + x];
        public void SetNormalized(int x, int z, float v) => Heights[z * Resolution + x] = Clamp01(v);
        public float GetMeters(int x, int z) => Heights[z * Resolution + x] * VerticalRange;

        /// <summary>Bilinear height in metres at a world position (clamped to the map).</summary>
        public float SampleHeight(float worldX, float worldZ)
        {
            float fx = Clamp(worldX / Spacing, 0, Resolution - 1.0001f);
            float fz = Clamp(worldZ / Spacing, 0, Resolution - 1.0001f);
            int x0 = (int)fx, z0 = (int)fz;
            float tx = fx - x0, tz = fz - z0;
            float h00 = GetNormalized(x0, z0), h10 = GetNormalized(x0 + 1, z0);
            float h01 = GetNormalized(x0, z0 + 1), h11 = GetNormalized(x0 + 1, z0 + 1);
            float a = h00 + (h10 - h00) * tx;
            float b = h01 + (h11 - h01) * tx;
            return (a + (b - a) * tz) * VerticalRange;
        }

        /// <summary>Approximate slope in degrees using central differences.</summary>
        public float SampleSlopeDegrees(float worldX, float worldZ)
        {
            float d = Spacing;
            float dx = (SampleHeight(worldX + d, worldZ) - SampleHeight(worldX - d, worldZ)) / (2 * d);
            float dz = (SampleHeight(worldX, worldZ + d) - SampleHeight(worldX, worldZ - d)) / (2 * d);
            return (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180.0 / Math.PI);
        }

        /// <summary>
        /// Soft gaussian brush. <paramref name="deltaMeters"/> is the peak change at the centre (negative lowers).
        /// Returns the dirty sample rectangle (empty if nothing changed).
        /// </summary>
        public IntRect ApplyBrush(float centerX, float centerZ, float radius, float deltaMeters, BrushMask mask = null, HeightEditRecorder recorder = null)
        {
            if (radius <= 0 || deltaMeters == 0) return IntRect.Empty;
            float s = Spacing;
            var rect = new IntRect(
                (int)Math.Floor((centerX - radius) / s), (int)Math.Floor((centerZ - radius) / s),
                (int)Math.Ceiling((centerX + radius) / s), (int)Math.Ceiling((centerZ + radius) / s)).Clamp(Resolution, Resolution);
            if (rect.IsEmpty) return IntRect.Empty;

            float deltaNorm = deltaMeters / VerticalRange;
            float r2 = radius * radius;
            var dirty = IntRect.Empty;
            for (int z = rect.ZMin; z <= rect.ZMax; z++)
            {
                float wz = z * s;
                for (int x = rect.XMin; x <= rect.XMax; x++)
                {
                    float wx = x * s;
                    float d2 = (wx - centerX) * (wx - centerX) + (wz - centerZ) * (wz - centerZ);
                    if (d2 >= r2) continue;
                    float w = (float)Math.Exp(-4.5 * d2 / r2); // ≈0.011 at the rim: soft edge
                    if (mask != null) w *= mask(wx, wz);
                    if (w <= 1e-5f) continue;
                    int i = z * Resolution + x;
                    float before = Heights[i];
                    float after = Clamp01(before + deltaNorm * w);
                    if (after != before)
                    {
                        recorder?.Record(i, before, x, z);
                        Heights[i] = after;
                        dirty = dirty.Union(new IntRect(x, z, x, z));
                    }
                }
            }
            return dirty;
        }

        /// <summary>
        /// Flatten brush (GDD §4): moves samples toward a target height that was locked when the stroke began,
        /// at most <paramref name="maxStepMeters"/> per command, with a soft rim.
        /// </summary>
        public IntRect ApplyFlatten(float centerX, float centerZ, float radius, float targetMeters, float maxStepMeters, BrushMask mask = null, HeightEditRecorder recorder = null)
        {
            if (radius <= 0 || maxStepMeters <= 0) return IntRect.Empty;
            float s = Spacing;
            var rect = SampleRect(centerX, centerZ, radius);
            if (rect.IsEmpty) return IntRect.Empty;
            float target = Clamp01(targetMeters / VerticalRange);
            float step = maxStepMeters / VerticalRange;
            float r2 = radius * radius;
            var dirty = IntRect.Empty;
            for (int z = rect.ZMin; z <= rect.ZMax; z++)
            for (int x = rect.XMin; x <= rect.XMax; x++)
            {
                float wx = x * s, wz = z * s;
                float d2 = (wx - centerX) * (wx - centerX) + (wz - centerZ) * (wz - centerZ);
                if (d2 >= r2) continue;
                float t = (float)Math.Sqrt(d2) / radius;
                float w = t < 0.6f ? 1f : 1f - SmoothStep01((t - 0.6f) / 0.4f);
                if (mask != null) w *= mask(wx, wz);
                if (w <= 1e-4f) continue;
                int i = z * Resolution + x;
                float before = Heights[i];
                float diff = target - before;
                float move = diff * w;
                float lim = step * w;
                if (move > lim) move = lim; else if (move < -lim) move = -lim;
                float after = Clamp01(before + move);
                if (Math.Abs(after - before) < 1e-7f) continue;
                recorder?.Record(i, before, x, z);
                Heights[i] = after;
                dirty = dirty.Union(new IntRect(x, z, x, z));
            }
            return dirty;
        }

        /// <summary>Sets one sample (metres) while recording undo data. Used by river carving and building pads.</summary>
        public bool SetMetersRecorded(int x, int z, float meters, HeightEditRecorder recorder)
        {
            int i = z * Resolution + x;
            float before = Heights[i];
            float after = Clamp01(meters / VerticalRange);
            if (Math.Abs(after - before) < 1e-7f) return false;
            recorder?.Record(i, before, x, z);
            Heights[i] = after;
            return true;
        }

        public IntRect SampleRect(float centerX, float centerZ, float radius)
        {
            float s = Spacing;
            return new IntRect(
                (int)Math.Floor((centerX - radius) / s), (int)Math.Floor((centerZ - radius) / s),
                (int)Math.Ceiling((centerX + radius) / s), (int)Math.Ceiling((centerZ + radius) / s)).Clamp(Resolution, Resolution);
        }

        /// <summary>Min/max height (metres) of samples inside a world-space axis-aligned box.</summary>
        public void HeightRange(float minX, float minZ, float maxX, float maxZ, out float lo, out float hi)
        {
            float s = Spacing;
            var r = new IntRect((int)Math.Floor(minX / s), (int)Math.Floor(minZ / s), (int)Math.Ceiling(maxX / s), (int)Math.Ceiling(maxZ / s)).Clamp(Resolution, Resolution);
            lo = float.MaxValue; hi = float.MinValue;
            for (int z = r.ZMin; z <= r.ZMax; z++)
            for (int x = r.XMin; x <= r.XMax; x++)
            {
                float h = GetMeters(x, z);
                if (h < lo) lo = h;
                if (h > hi) hi = h;
            }
        }

        /// <summary>Stable hash of the height data (for save/load verification).</summary>
        public ulong ComputeHash()
        {
            var bytes = new byte[Heights.Length * 4];
            Buffer.BlockCopy(Heights, 0, bytes, 0, bytes.Length);
            return GenesisValley.Core.Hashing.Fnv1a64(bytes);
        }

        public static float SmoothStep01(float t)
        {
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            return t * t * (3 - 2 * t);
        }

        public void CopyFrom(float[] source)
        {
            if (source.Length != Heights.Length) throw new ArgumentException("Height array size mismatch");
            Array.Copy(source, Heights, Heights.Length);
        }

        private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
