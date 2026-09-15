using JurassicPark.Core;
using UnityEngine;

namespace JurassicPark.Building
{
    /// <summary>Pure grid math: snapping, footprints, rotation, affordability. Testable without a scene.</summary>
    public static class BuildGrid
    {
        /// <summary>Snaps a world XZ point to the center of its cell.</summary>
        public static Vector3 Snap(Vector3 world, float cellSize)
        {
            float x = (Mathf.Floor(world.x / cellSize) + 0.5f) * cellSize;
            float z = (Mathf.Floor(world.z / cellSize) + 0.5f) * cellSize;
            return new Vector3(x, world.y, z);
        }

        /// <summary>Footprint size in world units after rotating by 90-degree steps.</summary>
        public static Vector2 RotatedFootprint(Vector2Int footprint, int rotationSteps, float cellSize)
        {
            bool swap = ((rotationSteps % 4) + 4) % 4 % 2 == 1;
            return swap
                ? new Vector2(footprint.y * cellSize, footprint.x * cellSize)
                : new Vector2(footprint.x * cellSize, footprint.y * cellSize);
        }

        /// <summary>Center of a footprint anchored at a snapped cell so even-sized footprints still align to the grid.</summary>
        public static Vector3 FootprintCenter(Vector3 snappedCell, Vector2Int footprint, int rotationSteps, float cellSize)
        {
            Vector2 size = RotatedFootprint(footprint, rotationSteps, cellSize);
            float ox = ((Mathf.RoundToInt(size.x / cellSize) + 1) % 2) * cellSize * 0.5f;
            float oz = ((Mathf.RoundToInt(size.y / cellSize) + 1) % 2) * cellSize * 0.5f;
            return snappedCell + new Vector3(ox, 0f, oz);
        }

        /// <summary>Nearest grid-aligned footprint center to the pointer, including even-sized footprints.</summary>
        public static Vector3 SnapFootprint(Vector3 world, Vector2Int footprint, int rotationSteps, float cellSize)
        {
            Vector3 offset = FootprintCenter(Vector3.zero, footprint, rotationSteps, cellSize);
            return Snap(world - offset, cellSize) + offset;
        }

        /// <summary>Axis-aligned XZ overlap of two rotated footprints (rotations are 90-degree steps, so AABB is exact).</summary>
        public static bool Overlaps(Vector3 centerA, Vector2 sizeA, Vector3 centerB, Vector2 sizeB)
        {
            const float eps = 0.01f;
            return Mathf.Abs(centerA.x - centerB.x) < (sizeA.x + sizeB.x) * 0.5f - eps
                && Mathf.Abs(centerA.z - centerB.z) < (sizeA.y + sizeB.y) * 0.5f - eps;
        }

        public static bool CanAfford(ResourceInventory inv, StructureDef def, float fraction = 1f)
        {
            foreach (ResourceCost c in def.cost)
            {
                int amount = fraction < 1f ? Mathf.CeilToInt(c.amount * fraction) : c.amount;
                if (!inv.Has(c.kind, amount)) return false;
            }

            return true;
        }

        public static bool Pay(ResourceInventory inv, StructureDef def, float fraction = 1f)
        {
            if (!CanAfford(inv, def, fraction)) return false;
            foreach (ResourceCost c in def.cost)
                inv.TryTake(c.kind, fraction < 1f ? Mathf.CeilToInt(c.amount * fraction) : c.amount);
            return true;
        }
    }
}
