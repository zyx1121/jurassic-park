using System;
using UnityEngine;

namespace JurassicPark.Core
{
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
}
