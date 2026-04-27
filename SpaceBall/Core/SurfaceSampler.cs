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
        private int _width;
        private int _height;

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
            _width = current.GetLength(0);
            _height = current.GetLength(1);
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

            return SampleTextureLinearUnpacked(map, uvX, uvY);
        }

        private float SampleTextureLinearUnpacked(float[,] map, float uvX, float uvY)
        {
            if (_width <= 0 || _height <= 0)
                return 0f;

            // Эквивалент texture() с GL_LINEAR + WrapS=Repeat + WrapT=ClampToEdge.
            float x = uvX * _width - 0.5f;
            float y = uvY * _height - 0.5f;

            int x0 = FloorToInt(x);
            int y0 = FloorToInt(y);
            int x1 = x0 + 1;
            int y1 = y0 + 1;

            float tx = x - x0;
            float ty = y - y0;

            int sx0 = WrapRepeat(x0, _width);
            int sx1 = WrapRepeat(x1, _width);
            int sy0 = ClampEdge(y0, _height);
            int sy1 = ClampEdge(y1, _height);

            float c00 = UnpackHeight(map[sx0, sy0]);
            float c10 = UnpackHeight(map[sx1, sy0]);
            float c01 = UnpackHeight(map[sx0, sy1]);
            float c11 = UnpackHeight(map[sx1, sy1]);

            float cx0 = c00 + (c10 - c00) * tx;
            float cx1 = c01 + (c11 - c01) * tx;
            return cx0 + (cx1 - cx0) * ty;
        }

        private static int FloorToInt(float value) => (int)MathF.Floor(value);

        private static int WrapRepeat(int value, int size)
        {
            int result = value % size;
            return result < 0 ? result + size : result;
        }

        private static int ClampEdge(int value, int size)
        {
            if (value < 0) return 0;
            if (value >= size) return size - 1;
            return value;
        }

        private static float UnpackHeight(float rawHeight)
        {
            // Эквивалент пути CPU->R8 texture->shader:
            // byte = (raw*0.5+0.5)*255, texture.r in [0..1], shader: r*2-1.
            float clamped = Math.Clamp(rawHeight, -1f, 1f);
            byte encoded = (byte)((clamped * 0.5f + 0.5f) * 255f);
            float texelR = encoded / 255f;
            return texelR * 2f - 1f;
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
