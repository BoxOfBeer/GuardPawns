using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Обертка совместимости над PlanetSurface.
    /// Вся логика поверхности теперь находится в PlanetSurface.
    /// </summary>
    public class SurfaceSampler
    {
        private readonly PlanetSurface _surface = new PlanetSurface();

        public float PlanetRadius => _surface.Radius;
        public float DisplacementScale => _surface.DisplacementScale;
        public float BlendFactor => _surface.BlendFactor;
        public bool HasData => _surface.HasHeightmap;

        public void SetPlanet(float radius, float displacementScale) => _surface.SetPlanet(radius, displacementScale);

        public void SetHeightmaps(float[,] current, float[,]? next, float blendFactor) => _surface.SetHeightmaps(current, next, blendFactor);

        public float SampleHeightWorld(Vector3 direction) => _surface.GetHeight(direction);

        public SurfaceSample SampleSurface(Vector3 direction)
        {
            Vector3 normal = _surface.GetSurfaceNormal(direction);
            return new SurfaceSample(
                _surface.GetSurfacePoint(normal),
                normal,
                _surface.GetSurfaceRadius(normal),
                _surface.GetHeight(normal),
                _surface.GetHeight(normal) < 0f,
                0f);
        }

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
    }
}
