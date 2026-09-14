using System;
using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>
    /// One animation on a 4-row sprite sheet. Rows are directions (Down, Left, Right, Up, top to
    /// bottom), columns are frames, every cell the same size.
    /// </summary>
    [Serializable]
    public sealed class SpriteSheetClip
    {
        public string name = "Idle";
        public Texture2D sheet;
        [Min(1)] public int columns = 4;
        [Min(1)] public int rows = 4;
        [Min(0.1f)] public float framesPerSecond = 6f;
        public bool loop = true;
    }

    /// <summary>All clips for one character. Sheets come from the Blender render pipeline.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Sprite Sheet Set", fileName = "SpriteSheetSet")]
    public sealed class SpriteSheetSet : ScriptableObject
    {
        public SpriteSheetClip[] clips = Array.Empty<SpriteSheetClip>();

        public SpriteSheetClip Find(string clipName)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                if (string.Equals(clips[i].name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    return clips[i];
                }
            }

            return null;
        }
    }

    public static class SpriteSheetMath
    {
        /// <summary>
        /// Texture scale and offset (xy = scale, zw = offset) that shows cell (column, row) of a
        /// columns x rows sheet. Row 0 is the top row of the image; Unity UV v grows upward.
        /// </summary>
        public static Vector4 CellScaleOffset(int column, int row, int columns, int rows)
        {
            float sx = 1f / columns;
            float sy = 1f / rows;
            float ox = column * sx;
            float oy = 1f - (row + 1) * sy;
            return new Vector4(sx, sy, ox, oy);
        }

        /// <summary>Frame index for a time in seconds.</summary>
        public static int FrameAt(float time, float framesPerSecond, int columns, bool loop)
        {
            int frame = Mathf.FloorToInt(time * framesPerSecond);
            return loop ? ((frame % columns) + columns) % columns : Mathf.Clamp(frame, 0, columns - 1);
        }
    }
}
