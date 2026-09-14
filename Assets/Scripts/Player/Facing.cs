using UnityEngine;

namespace JurassicPark.Player
{
    /// <summary>Four sprite directions. Sprites are drawn for these only; the camera never rotates.</summary>
    public enum Facing
    {
        Down = 0,
        Left = 1,
        Right = 2,
        Up = 3,
    }

    public static class FacingUtil
    {
        /// <summary>
        /// Picks the sprite direction for a world-space XZ movement vector. Diagonals resolve to
        /// the dominant axis; on an exact tie the horizontal wins so strafing reads as a turn.
        /// A zero vector keeps <paramref name="current"/>.
        /// </summary>
        public static Facing FromDirection(Vector2 xz, Facing current)
        {
            if (xz.sqrMagnitude < 0.0001f)
            {
                return current;
            }

            if (Mathf.Abs(xz.x) >= Mathf.Abs(xz.y))
            {
                return xz.x < 0f ? Facing.Left : Facing.Right;
            }

            return xz.y < 0f ? Facing.Down : Facing.Up;
        }

        public static Vector2 ToVector(Facing facing)
        {
            switch (facing)
            {
                case Facing.Left: return Vector2.left;
                case Facing.Right: return Vector2.right;
                case Facing.Up: return Vector2.up;
                default: return Vector2.down;
            }
        }
    }
}
