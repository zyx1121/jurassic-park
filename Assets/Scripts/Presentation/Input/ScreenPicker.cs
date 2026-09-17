using System;
using System.Collections.Generic;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// Picks what the player sees, not what stands under the cursor's ground point. From a 60 degree camera the top of a 3.4 m
    /// tree is drawn two metres up-screen of its foot, and most of a depot's visible face is its roof, so a ground-distance test
    /// misses exactly the part of a thing people click on. Each entity is treated as the on-screen capsule from its foot to its top.
    /// </summary>
    public static class ScreenPicker
    {
        /// <summary>The entity whose drawn silhouette is nearest the cursor and contains it, or null. Ties go to the older entity.</summary>
        public static Entity Pick(Camera camera, World world, EntityCatalogAsset catalog, Vector2 cursor, float slackPixels, Predicate<Entity> filter)
        {
            Entity best = null;
            float bestScore = float.MaxValue;
            IReadOnlyList<Entity> entities = world.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity candidate = entities[i];
                if (!candidate.IsAlive || (filter != null && !filter(candidate))) continue;
                float height = 1f, halfWidth = 0.5f;
                if (catalog.TryGet(candidate.DefinitionId, out EntityCatalogAsset.Entry entry))
                {
                    height = entry.DrawnHeight;
                    halfWidth = entry.DrawnHalfWidth;
                }
                Vector3 foot = EntityViewRegistry.ToWorld(candidate.Position);
                Vector3 footScreen = camera.WorldToScreenPoint(foot);
                if (footScreen.z <= 0f) continue;
                Vector3 topScreen = camera.WorldToScreenPoint(foot + Vector3.up * height);
                Vector3 sideScreen = camera.WorldToScreenPoint(foot + camera.transform.right * halfWidth);
                float radius = Vector2.Distance(footScreen, sideScreen) + slackPixels;
                float distance = DistanceToSegment(cursor, footScreen, topScreen);
                if (distance > radius) continue;
                // Normalised, so a small unit in front of a big building wins when the cursor is on both.
                float score = distance / radius;
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            float t = lengthSquared < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
