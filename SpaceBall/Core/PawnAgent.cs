using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Активная система существ: только пешки на поверхности.
    /// </summary>
    public sealed class PawnAgent
    {
        private readonly Random _random = new();
        private readonly List<Pawn> _pawns = new();
        private readonly PlanetSurface _surface;

        public IReadOnlyList<Pawn> Pawns => _pawns;

        public PawnAgent(PlanetSurface surface)
        {
            _surface = surface;
        }

        public void InitializePopulation(int count)
        {
            _pawns.Clear();
            int safeCount = Math.Clamp(count, 1, 2000);
            for (int i = 0; i < safeCount; i++)
            {
                _pawns.Add(new Pawn(RandomDirection(), 0.08f + (float)_random.NextDouble() * 0.05f));
            }
        }

        public void Update(float deltaTime)
        {
            float moveSpeed = 0.18f;
            foreach (var pawn in _pawns)
            {
                Vector3 noiseDir = RandomDirection();
                pawn.MoveAlongTangent(noiseDir * moveSpeed * deltaTime);
            }
        }

        public Vector3 GetWorldPosition(Pawn pawn)
        {
            Vector3 normal = _surface.GetSurfaceNormal(pawn.Direction);
            Vector3 surfacePoint = _surface.GetSurfacePoint(normal);
            float offset = MathF.Max(WorldConstants.MinPawnSurfaceOffset, pawn.Size * 0.6f);
            return surfacePoint + normal * offset;
        }

        private Vector3 RandomDirection()
        {
            float z = (float)(_random.NextDouble() * 2.0 - 1.0);
            float angle = (float)(_random.NextDouble() * Math.PI * 2.0);
            float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            return new Vector3(r * MathF.Cos(angle), z, r * MathF.Sin(angle));
        }
    }
}
