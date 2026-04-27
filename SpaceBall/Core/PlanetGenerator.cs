using System;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Planet heightmap: 3D noise on sphere, no seam. Simulates gradual geology (erosion, uplift)
    /// without sharp "relief" - no clear mountains/cliffs; smooth, blended elevation variation.
    /// Noise scale ~ 1/NoiseFrequency. Size = heightmap resolution (e.g. 512).
    /// </summary>
    public static class PlanetGenerator
    {
        public static float[,] GenerateHeightmap(Genome g, int size)
        {
            int octaves = Math.Max(1, g.NoiseOctaves);
            float baseFreq = Math.Max(0.0001f, g.NoiseFrequency);
            // Minimum amplitude so planet is never perfectly flat (e.g. config GeologicActivity=0 at start)
            float geo = Math.Max(g.GeologicActivity, 0.25f);
            var outMap = new float[size, size];

            // For normalization - sum of amplitudes
            float maxAmp = 0f;
            float amp = 1f;
            for (int o = 0; o < octaves; o++) 
            { 
                maxAmp += amp; 
                amp *= 0.5f; 
            }

            // First pass: generate 3D noise on sphere points
            var rawMap = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                // v from 0..1 -> latitude from -pi/2 .. +pi/2
                float v = y / (float)(size - 1);
                float lat = (v - 0.5f) * MathF.PI; // -pi/2 .. +pi/2
                float sy = MathF.Sin(lat), cy = MathF.Cos(lat);

                for (int x = 0; x < size; x++)
                {
                    // u from 0..1 -> longitude from 0 .. 2pi
                    float u = x / (float)(size - 1);
                    float lon = u * MathF.PI * 2f; // 0 .. 2pi
                    float sx = MathF.Cos(lon) * cy;
                    float sz = MathF.Sin(lon) * cy;

                    // Position on unit sphere (3D point)
                    float px = sx;
                    float py = sy;
                    float pz = sz;

                    // Sample 3D noise at the spherical point
                    float freq = baseFreq;
                    float amplitude = 1f;
                    float sum = 0f;
                    for (int o = 0; o < octaves; o++)
                    {
                        float n = ValueNoise3D(px * freq, py * freq, pz * freq, g.Seed + o * 997);
                        sum += n * amplitude;
                        freq *= 2f;
                        amplitude *= 0.5f;
                    }

                    sum /= maxAmp;
                    sum *= geo;

                    // Soft detail: gentle variation, no sharp ridges (geological smoothing)
                    float detailFreq = baseFreq * 2.5f;
                    float detailSum = 0f;
                    float dAmp = 0.2f;
                    float dFreq = detailFreq;
                    for (int d = 0; d < 3; d++)
                    {
                        detailSum += dAmp * ValueNoise3D(px * dFreq, py * dFreq, pz * dFreq, g.Seed + 1000 + d * 997);
                        dAmp *= 0.5f;
                        dFreq *= 2f;
                    }
                    rawMap[x, y] = sum + detailSum * geo;
                }
            }

            // Find min/max for normalization
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float h = rawMap[x, y];
                    if (h < minHeight) minHeight = h;
                    if (h > maxHeight) maxHeight = h;
                }
            }

            // Normalize to [-1, 1] range
            float range = maxHeight - minHeight;
            if (range < 0.0001f) range = 1f;
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float normalized = (rawMap[x, y] - minHeight) / range;
                    float height = (normalized - 0.5f) * 2f; // -1 to 1
                    // No sharp relief: very soft curve so elevation is gradual (real geology, erosion)
                    float sign = MathF.Sign(height);
                    height = sign * MathF.Pow(MathF.Abs(height), 0.96f);
                    outMap[x, y] = Math.Clamp(height, -1f, 1f);
                }
            }

            return outMap;
        }

        // 3D value noise on sphere
        private static float ValueNoise3D(float x, float y, float z, int seed)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            int zi = FastFloor(z);

            float xf = x - xi;
            float yf = y - yi;
            float zf = z - zi;

            float u = Fade(xf);
            float v = Fade(yf);
            float w = Fade(zf);

            float n000 = Hash3D(xi + 0, yi + 0, zi + 0, seed);
            float n100 = Hash3D(xi + 1, yi + 0, zi + 0, seed);
            float n010 = Hash3D(xi + 0, yi + 1, zi + 0, seed);
            float n110 = Hash3D(xi + 1, yi + 1, zi + 0, seed);
            float n001 = Hash3D(xi + 0, yi + 0, zi + 1, seed);
            float n101 = Hash3D(xi + 1, yi + 0, zi + 1, seed);
            float n011 = Hash3D(xi + 0, yi + 1, zi + 1, seed);
            float n111 = Hash3D(xi + 1, yi + 1, zi + 1, seed);

            float nx00 = Lerp(n000, n100, u);
            float nx10 = Lerp(n010, n110, u);
            float nx01 = Lerp(n001, n101, u);
            float nx11 = Lerp(n011, n111, u);

            float nxy0 = Lerp(nx00, nx10, v);
            float nxy1 = Lerp(nx01, nx11, v);

            return Lerp(nxy0, nxy1, w);
        }

        private static int FastFloor(float v) => (v >= 0f) ? (int)v : (int)v - 1;
        private static float Fade(float t) => t * t * (3f - 2f * t);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Hash3D(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)x) * 16777619u;
                h = (h ^ (uint)y) * 16777619u;
                h = (h ^ (uint)z) * 16777619u;
                h = (h ^ (uint)seed) * 16777619u;
                // map to [-1..1]
                return (float)((h & 0xFFFFFFu) / (float)0xFFFFFFu * 2.0 - 1.0);
            }
        }
    }
}
