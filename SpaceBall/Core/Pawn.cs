using OpenTK.Mathematics;

namespace SpaceDNA.Core
{
    /// <summary>
    /// Минимальная пешка: только направление и размер.
    /// </summary>
    public sealed class Pawn
    {
        public Vector3 Direction { get; private set; }
        public float Size { get; }

        public Pawn(Vector3 direction, float size)
        {
            Direction = direction.LengthSquared > 0.000001f ? Vector3.Normalize(direction) : Vector3.UnitY;
            Size = MathHelper.Clamp(size, 0.03f, 0.25f);
        }

        public void MoveAlongTangent(Vector3 tangentDelta)
        {
            Vector3 normal = Direction;
            Vector3 tangent = tangentDelta - Vector3.Dot(tangentDelta, normal) * normal;
            if (tangent.LengthSquared < 0.000001f)
                return;

            Direction = Vector3.Normalize(normal + tangent);
        }
    }
}
