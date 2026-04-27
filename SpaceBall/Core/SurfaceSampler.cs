using System;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Единый CPU sampler поверхности планеты.
    /// Формулы UV/height/displacement синхронизированы с planet_vertex.glsl.
    /// </summary>
    public class SurfaceSampler
    {
        public readonly struct SurfaceSample
        {
            public Vector3 Position { get; }
            public Vector3 Normal { get; }
            public float Radius { get; }
            public float Height { get; }
            public bool IsWater { get; }
            public float Slope { get; }

            public SurfaceSample(Vector3 position, Vector3 normal, float radius, float height, bool isWater, float slope)
            {
                Position = position;
                Normal = normal;
                Radius = radius;
                Height = height;
                IsWater = isWater;
                Slope = slope;
            }
        }

        private float[,]? _heightmapCurrent;
        private float[,]? _heightmapNext;
        private int _size;

        public float PlanetRadius { get; private set; } = WorldConstants.EarthRadius;
        public float DisplacementScale { get; private set; } = 1f;
        public float BlendFactor { get; private set; }
        public bool HasData => _heightmapCurrent != null;

        public void SetPlanet(float radius, float displacementScale)
        {
            PlanetRadius = radius;
            DisplacementScale = displacementScale;
        }

        public void SetHeightmaps(float[,] current, float[,]? next, float blendFactor)
        {
            _heightmapCurrent = current;
            _heightmapNext = next;
            _size = current.GetLength(0);
            BlendFactor = Math.Clamp(blendFactor, 0f, 1f);
        }

        public SurfaceSample SampleSurface(Vector3 direction)
        {
            Vector3 normal = direction.LengthSquared > 0.000001f ? Vector3.Normalize(direction) : Vector3.UnitY;
            float height = SampleHeightWorld(normal);
            float slope = EstimateSlope(normal, height);
            float radius = PlanetRadius + height;
            return new SurfaceSample(
                position: normal * radius,
                normal: normal,
                radius: radius,
                height: height,
                isWater: height < 0f,
                slope: slope);
        }

        public float SampleHeightWorld(Vector3 direction)
        {
            if (_heightmapCurrent == null)
                return 0f;

            Vector3 normal = direction.LengthSquared > 0.000001f ? Vector3.Normalize(direction) : Vector3.UnitY;
            float h1 = SampleHeightNormalized(_heightmapCurrent, normal);
            float h2 = _heightmapNext != null ? SampleHeightNormalized(_heightmapNext, normal) : h1;
            float h = h1 + (h2 - h1) * BlendFactor;
            return h * DisplacementScale;
        }

        private float SampleHeightNormalized(float[,] map, Vector3 normal)
        {
            // Shader эквивалент:
            // uv.x = aUV.x;
            // uv.y = 1.0 - aUV.y;
            // aUV.y = 0.5 - asin(y)/PI
            // => uv.y = 0.5 + asin(y)/PI
            const float twoPi = MathF.PI * 2f;
            float lon = MathF.Atan2(normal.Z, normal.X);
            if (lon < 0f) lon += twoPi;
            float uvX = lon / twoPi;
            float uvY = 0.5f + MathF.Asin(Math.Clamp(normal.Y, -1f, 1f)) / MathF.PI;

            int x = (int)(uvX * (_size - 1));
            int y = (int)(uvY * (_size - 1));
            x = Math.Clamp(x, 0, _size - 1);
            y = Math.Clamp(y, 0, _size - 1);

            return map[x, y];
        }

        private float EstimateSlope(Vector3 normal, float baseHeightWorld)
        {
            if (_heightmapCurrent == null) return 0f;

            BuildTangentBasis(normal, out var tangentA, out var tangentB);
            const float eps = 0.015f;
            float hA = SampleHeightWorld(Vector3.Normalize(normal + tangentA * eps));
            float hB = SampleHeightWorld(Vector3.Normalize(normal + tangentB * eps));
            float gradA = MathF.Abs(hA - baseHeightWorld) / eps;
            float gradB = MathF.Abs(hB - baseHeightWorld) / eps;
            return MathF.Min(1f, MathF.Sqrt(gradA * gradA + gradB * gradB));
        }

        private static void BuildTangentBasis(Vector3 normal, out Vector3 tangentA, out Vector3 tangentB)
        {
            Vector3 helper = MathF.Abs(normal.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
            tangentA = Vector3.Normalize(Vector3.Cross(normal, helper));
            tangentB = Vector3.Normalize(Vector3.Cross(normal, tangentA));
        }
    }
}
