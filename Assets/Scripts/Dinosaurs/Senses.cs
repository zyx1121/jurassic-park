using UnityEngine;

namespace JurassicPark.Dinosaurs
{
    /// <summary>Pure perception math shared by all dinosaur brains.</summary>
    public static class Senses
    {
        /// <summary>
        /// Target is seen when inside the (possibly firelight-boosted) sight range and within the
        /// cone around the forward direction. A zero forward sees all around.
        /// </summary>
        public static bool CanSee(Vector3 eye, Vector3 forward, Vector3 target, float sightRange, float halfAngleDeg, float rangeMultiplier = 1f)
        {
            Vector3 to = target - eye;
            to.y = 0f;
            float d = to.magnitude;
            if (d > sightRange * rangeMultiplier) return false;
            if (d < 0.001f || halfAngleDeg >= 180f) return true;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(forward, to) <= halfAngleDeg;
        }

        /// <summary>
        /// Flank slot for pack member i of n around a target approached from approachDir: the
        /// leader (i = 0) takes the direct line, followers alternate left and right at flankAngle.
        /// </summary>
        public static Vector3 FlankSlot(int index, int count, Vector3 target, Vector3 approachDir, float radius, float flankAngleDeg)
        {
            approachDir.y = 0f;
            if (approachDir.sqrMagnitude < 0.0001f) approachDir = Vector3.forward;
            approachDir.Normalize();
            if (index <= 0 || count <= 1)
            {
                return target - approachDir * radius;
            }

            int side = index % 2 == 1 ? 1 : -1;
            int ring = (index + 1) / 2;
            float angle = side * flankAngleDeg * ring / Mathf.Max(1, (count) / 2);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * -approachDir;
            return target + dir * radius;
        }
    }
}
