using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Минимальный агент пешек для стабильной привязки к поверхности планеты.
    /// Без ДНК-эволюции, еды, размножения и авто-мутаций.
    /// </summary>
    public class PawnAgent
    {
        private readonly Random _rnd = new Random();
        private readonly List<Pawn> _pawns = new List<Pawn>();
        private readonly PlanetSurface _surface = new PlanetSurface();

        public float PlanetRadius { get; private set; }
        public float PlanetDensity { get; private set; }
        public float PlanetTemperature { get; private set; }
        public float PlanetAtmosphere { get; private set; }
        public float PlanetGeologicActivity { get; private set; }

        public IReadOnlyList<Pawn> Pawns => _pawns;
        public int AliveCount => _pawns.Count;
        public int TotalCount => _pawns.Count;

        public PawnAgent()
        {
            SetPlanetParameters(WorldConstants.EarthRadius, 1f, 0.5f, 1f, 1f);
        }

        public void SetPlanetParameters(float radius, float density, float temperature, float atmosphere, float geologicActivity)
        {
            PlanetRadius = radius;
            PlanetDensity = density;
            PlanetTemperature = temperature;
            PlanetAtmosphere = atmosphere;
            PlanetGeologicActivity = geologicActivity;
            _surface.SetPlanet(radius, _surface.DisplacementScale);
        }

        public void SetHeightmap(float[,] heightmap, float displacementScale)
        {
            SetSurfaceState(heightmap, null, 0f, displacementScale);
        }

        public void SetSurfaceState(float[,] currentHeightmap, float[,]? nextHeightmap, float blendFactor, float displacementScale)
        {
            _surface.SetPlanet(PlanetRadius, displacementScale);
            _surface.SetHeightmaps(currentHeightmap, nextHeightmap, blendFactor);
        }

        public void SetSpeciesDna(DnaSequence? dna)
        {
            // Отключено в milestone намеренно.
        }

        public void InitializePopulation(int count)
        {
            _pawns.Clear();
            for (int i = 0; i < count; i++)
            {
                _pawns.Add(new Pawn(RandomDirection(), random: _rnd));
            }
        }

        public void Update(float deltaTime)
        {
            float speed = 0.22f;
            foreach (var pawn in _pawns)
                pawn.Update(deltaTime, speed);
        }

        public float GetTerrainHeightAt(Vector3 unitDirection) => _surface.GetHeight(unitDirection);

        public float GetSurfaceRadius(Vector3 direction) => _surface.GetSurfaceRadius(direction);

        public Vector3 GetSurfacePoint(Vector3 direction) => _surface.GetSurfacePoint(direction);

        public Vector3 GetWorldPosition(Pawn pawn)
        {
            Vector3 normal = _surface.GetSurfaceNormal(pawn.Direction);
            float offset = GetPawnSurfaceOffset(pawn);
            return _surface.GetSurfacePoint(normal) + normal * offset;
        }

        public int ValidateSurfaceAnchoring(float tolerance = 0.02f)
        {
            int invalid = 0;
            foreach (var pawn in _pawns)
            {
                Vector3 normal = _surface.GetSurfaceNormal(pawn.Direction);
                float expected = _surface.GetSurfaceRadius(normal) + GetPawnSurfaceOffset(pawn);
                float got = GetWorldPosition(pawn).Length;
                if (MathF.Abs(expected - got) > tolerance)
                    invalid++;
            }

            return invalid;
        }

        public (float maxAbsDiff, float avgAbsDiff, int samples) MeasureRenderedRadiusDifference()
        {
            float max = 0f;
            float sum = 0f;
            int samples = 0;
            foreach (var pawn in _pawns)
            {
                Vector3 normal = _surface.GetSurfaceNormal(pawn.Direction);
                float expected = _surface.GetSurfaceRadius(normal);
                float byPoint = _surface.GetSurfacePoint(normal).Length;
                float diff = MathF.Abs(expected - byPoint);
                sum += diff;
                if (diff > max) max = diff;
                samples++;
            }

            return (max, samples > 0 ? sum / samples : 0f, samples);
        }

        private float GetPawnSurfaceOffset(Pawn pawn)
        {
            return pawn.VisualOffset;
        }

        private Vector3 RandomDirection()
        {
            float theta = (float)(_rnd.NextDouble() * Math.PI * 2.0);
            float phi = (float)Math.Acos(2.0 * _rnd.NextDouble() - 1.0);
            return new Vector3(
                MathF.Sin(phi) * MathF.Cos(theta),
                MathF.Sin(phi) * MathF.Sin(theta),
                MathF.Cos(phi));
        }
    }
}
