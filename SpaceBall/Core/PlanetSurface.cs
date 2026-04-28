using System;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Единая координатная модель планеты: центр в (0,0,0), направление всегда нормализовано.
    /// </summary>
    public sealed class PlanetSurface
    {
        private float[,] _heightmap = new float[1, 1];

        public float Radius { get; private set; } = WorldConstants.EarthRadius;
        public float DisplacementScale { get; private set; } = 0.3f;
        public float[,] Heightmap => _heightmap;

        public float MinHeight01 { get; private set; }
        public float MaxHeight01 { get; private set; }
        public float MinSurfaceRadius { get; private set; }
        public float MaxSurfaceRadius { get; private set; }

        public void SetPlanet(float radius, float displacementScale)
        {
            Radius = Math.Max(0.01f, radius);
            DisplacementScale = Math.Max(0f, displacementScale);
            RecalculateStats();
        }

        public void Generate(int seed, int size)
        {
            int safeSize = Math.Clamp(size, 32, 1024);
            var genome = new Genome
            {
                Seed = seed,
                GeologicActivity = 1f,
                NoiseOctaves = 5,
                NoiseFrequency = 0.9f,
                Temperature = 0.5f,
                Atmosphere = 0.5f,
                Density = 1f
            };

            _heightmap = PlanetGenerator.GenerateHeightmap(genome, safeSize);
            RecalculateStats();
        }

        public float GetHeight01(Vector3 direction)
        {
            if (_heightmap.Length == 0)
                return 0.5f;

            Vector3 normal = GetSurfaceNormal(direction);
            float signed = SampleHeightSigned(normal);
            return Math.Clamp(0.5f + signed * 0.5f, 0f, 1f);
        }

        public float GetHeightWorld(Vector3 direction)
        {
            return (GetHeight01(direction) * 2f - 1f) * DisplacementScale;
        }

        public float GetSurfaceRadius(Vector3 direction)
        {
            return Radius + GetHeightWorld(direction);
        }

        public Vector3 GetSurfacePoint(Vector3 direction)
        {
            Vector3 normal = GetSurfaceNormal(direction);
            return normal * GetSurfaceRadius(normal);
        }

        public Vector3 GetSurfaceNormal(Vector3 direction)
        {
            return direction.LengthSquared > 0.000001f ? Vector3.Normalize(direction) : Vector3.UnitY;
        }

        public float GetAtmosphereRadius() => Radius * 1.08f;
        public float GetCloudRadius() => Radius * 1.12f;

        public Vector3 GetPointAtRadius(Vector3 direction, float worldRadius)
        {
            return GetSurfaceNormal(direction) * worldRadius;
        }

        private float SampleHeightSigned(Vector3 normal)
        {
            int width = _heightmap.GetLength(0);
            int height = _heightmap.GetLength(1);
            if (width <= 1 || height <= 1)
                return 0f;

            float lon = MathF.Atan2(normal.Z, normal.X);
            if (lon < 0f)
                lon += MathF.PI * 2f;

            float u = lon / (MathF.PI * 2f);
            float v = 0.5f + MathF.Asin(Math.Clamp(normal.Y, -1f, 1f)) / MathF.PI;

            float x = u * (width - 1);
            float y = v * (height - 1);

            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = (x0 + 1) % width;
            int y1 = Math.Min(y0 + 1, height - 1);

            float tx = x - x0;
            float ty = y - y0;

            float h00 = _heightmap[x0, y0];
            float h10 = _heightmap[x1, y0];
            float h01 = _heightmap[x0, y1];
            float h11 = _heightmap[x1, y1];

            float hx0 = MathHelper.Lerp(h00, h10, tx);
            float hx1 = MathHelper.Lerp(h01, h11, tx);
            return MathHelper.Lerp(hx0, hx1, ty);
        }

        private void RecalculateStats()
        {
            MinHeight01 = 1f;
            MaxHeight01 = 0f;

            int width = _heightmap.GetLength(0);
            int height = _heightmap.GetLength(1);
            if (width == 0 || height == 0)
            {
                MinHeight01 = 0.5f;
                MaxHeight01 = 0.5f;
                MinSurfaceRadius = Radius;
                MaxSurfaceRadius = Radius;
                return;
            }

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float h01 = Math.Clamp(0.5f + _heightmap[x, y] * 0.5f, 0f, 1f);
                    MinHeight01 = MathF.Min(MinHeight01, h01);
                    MaxHeight01 = MathF.Max(MaxHeight01, h01);
                }
            }

            MinSurfaceRadius = Radius + (MinHeight01 * 2f - 1f) * DisplacementScale;
            MaxSurfaceRadius = Radius + (MaxHeight01 * 2f - 1f) * DisplacementScale;
        }
    }
}
