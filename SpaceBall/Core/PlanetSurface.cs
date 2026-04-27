using System;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Единый источник геометрии поверхности планеты.
    /// Содержит радиус, heightmap и формулы выборки для CPU-привязки пешек.
    /// </summary>
    public sealed class PlanetSurface
    {
        private float[,]? _heightmapCurrent;
        private float[,]? _heightmapNext;
        private int _width;
        private int _height;

        public float Radius { get; private set; } = WorldConstants.EarthRadius;
        public float DisplacementScale { get; private set; } = 1f;
        public float BlendFactor { get; private set; }
        public bool HasHeightmap => _heightmapCurrent != null;

        public void SetPlanet(float radius, float displacementScale)
        {
            Radius = radius;
            DisplacementScale = displacementScale;
        }

        public void SetHeightmaps(float[,] currentHeightmap, float[,]? nextHeightmap, float blendFactor)
        {
            _heightmapCurrent = currentHeightmap;
            _heightmapNext = nextHeightmap;
            _width = currentHeightmap.GetLength(0);
            _height = currentHeightmap.GetLength(1);
            BlendFactor = Math.Clamp(blendFactor, 0f, 1f);
        }

        public float GetHeight(Vector3 direction)
        {
            if (_heightmapCurrent == null)
                return 0f;

            Vector3 normal = GetSurfaceNormal(direction);
            float h1 = SampleHeightNormalized(_heightmapCurrent, normal);
            float h2 = _heightmapNext != null ? SampleHeightNormalized(_heightmapNext, normal) : h1;
            float h = h1 + (h2 - h1) * BlendFactor;
            return h * DisplacementScale;
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

        public float GetSurfaceRadius(Vector3 direction)
        {
            return Radius + GetHeight(direction);
        }

        private float SampleHeightNormalized(float[,] map, Vector3 normal)
        {
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

            float x = uvX * _width - 0.5f;
            float y = uvY * _height - 0.5f;

            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
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
            float clamped = Math.Clamp(rawHeight, -1f, 1f);
            byte encoded = (byte)((clamped * 0.5f + 0.5f) * 255f);
            float texelR = encoded / 255f;
            return texelR * 2f - 1f;
        }
    }
}
