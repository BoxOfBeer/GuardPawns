using System;
using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Минимальная пешка для milestone: хранит только направление на сфере и визуальный оффсет.
    /// </summary>
    public class Pawn
    {
        private static int _nextId = 1;
        private readonly Random _rng;
        private Vector3 _moveTangent;
        private float _moveTimer;

        public int Id { get; }
        public PawnGenome Genome { get; }
        public int Generation { get; }
        public bool IsAlive { get; private set; } = true;

        /// <summary>Нормализованное направление от центра планеты.</summary>
        public Vector3 Direction { get; private set; }

        /// <summary>Для обратной совместимости UI.</summary>
        public Vector3 Position => Direction;

        /// <summary>
        /// Дополнительный визуальный отступ от поверхности вдоль нормали.
        /// </summary>
        public float VisualOffset { get; }

        public float Energy { get; private set; }
        public float MaxEnergy => WorldConstants.MaxEnergy * Genome.Size;
        public float EnergyPercent => MaxEnergy > 0f ? Energy / MaxEnergy : 1f;

        public Pawn(Vector3 direction, PawnGenome? genome = null, int generation = 1, Random? random = null)
        {
            Id = _nextId++;
            Genome = genome ?? new PawnGenome();
            Generation = generation;
            _rng = random ?? Random.Shared;

            Direction = direction.LengthSquared > 0.000001f ? Vector3.Normalize(direction) : Vector3.UnitY;
            VisualOffset = MathF.Max(WorldConstants.MinPawnSurfaceOffset, WorldConstants.PawnSurfaceOffsetFactor * 5f) + 0.03f * Genome.Size;
            Energy = MaxEnergy;
            SetRandomTangent();
        }

        public void Update(float deltaTime, float speed)
        {
            if (!IsAlive) return;
            _moveTimer -= deltaTime;
            if (_moveTimer <= 0f)
                SetRandomTangent();

            Vector3 normal = Direction;
            // Гарантируем движение по касательной.
            Vector3 tangent = Vector3.Normalize(_moveTangent - Vector3.Dot(_moveTangent, normal) * normal);
            if (tangent.LengthSquared < 0.0001f)
            {
                tangent = MathF.Abs(normal.Y) > 0.95f
                    ? Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX))
                    : Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY));
            }

            float step = speed * deltaTime;
            Direction = Vector3.Normalize(normal + tangent * step);

            // Упрощенная энергия для индикации в UI.
            Energy = MathF.Max(MaxEnergy * 0.25f, Energy - deltaTime * 0.5f);
        }

        private void SetRandomTangent()
        {
            _moveTangent = new Vector3(
                (float)(_rng.NextDouble() * 2.0 - 1.0),
                (float)(_rng.NextDouble() * 2.0 - 1.0),
                (float)(_rng.NextDouble() * 2.0 - 1.0));

            if (_moveTangent.LengthSquared < 0.0001f)
                _moveTangent = Vector3.UnitX;

            _moveTimer = 1.5f + (float)_rng.NextDouble() * 2.5f;
        }
    }
}
