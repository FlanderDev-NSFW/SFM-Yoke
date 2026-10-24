using System;
using System.Numerics;

namespace Yoke.Logic
{
    /// <summary>Where one arm goes (world or any frame the inputs were given in).</summary>
    public struct ArmPose
    {
        public Vector3 Elbow, Hand;    // joint targets: lower arm bone, hand bone
        public float Along;            // where the cuff sits on the forearm (0 elbow .. 1 wrist)
        public float TiltDeg;          // forearm away from the cuff's axis (degrees)
        public float Miss;             // m the upper arm is short of (or past) the elbow the cuff needs; 0 = reached
    }

    public static class YokePose
    {
        /// <summary>How much uprightness (cosine) one forearm length of cuff travel from the preferred place is worth: the cuff
        /// stays near the wrist unless sliding it buys a lot. With 0.01 a narrow yoke slid the cuffs to mid-forearm for a few
        /// degrees, the hands far above them [observed 2026-10-24].</summary>
        public const float AlongWeight = 0.3f;

        /// <summary>Forearm lean (degrees from the cuff's axis) that + may not go past: wider, the forearm lies across the ring
        /// (84 deg at the full reach, through the ring's wall) [observed 2026-10-24].</summary>
        public const float MaxTiltDeg = 45f;

        /// <summary>The model's uniform scale that puts the neck ring around a neck of <paramref name="neckRadius"/> (m) with
        /// <paramref name="clearance"/> (m) to spare, times the player's <paramref name="sizeFactor"/>.</summary>
        public static float Scale(YokeModel m, float neckRadius, float clearance, float sizeFactor) =>
            (neckRadius + clearance) / m.Neck.InnerRadius * sizeFactor;

        /// <summary>
        /// Hands up through the cuffs: the forearm passes through the cuff's centre <paramref name="cuff"/> as near to the
        /// cuff's axis <paramref name="axis"/> (up, hand side) as the arm allows, and the upper arm (shoulder joint
        /// <paramref name="shoulder"/>, length <paramref name="upperArm"/>) reaches its elbow exactly.
        ///
        /// With the cuff at <c>a</c> of the forearm (length F) from the elbow and P = cuff - shoulder, the elbow is
        /// E = cuff - d a F; |E - shoulder| = upperArm gives d . P = k = (|P|^2 + (aF)^2 - upperArm^2) / (2 aF): the
        /// directions d form a cone around P, and the one nearest the axis is taken. The cuff may sit anywhere from
        /// <paramref name="alongMin"/> to <paramref name="alongMax"/> along the forearm; the most upright solution wins
        /// (ties: nearer <paramref name="alongPreferred"/>). When no position reaches, the nearest miss is returned with
        /// <see cref="ArmPose.Miss"/> set.
        /// </summary>
        public static ArmPose Arm(Vector3 shoulder, Vector3 cuff, Vector3 axis, float upperArm, float forearm,
                                  float alongMin, float alongMax, float alongPreferred)
        {
            axis = Vector3.Normalize(axis);
            Vector3 p = cuff - shoulder;
            float pl = p.Length();
            Vector3 ph = pl > 1e-6f ? p / pl : axis;
            // the axis' part square to P (the cone opens towards it)
            Vector3 perp = axis - ph * Vector3.Dot(axis, ph);
            perp = perp.LengthSquared() > 1e-10f ? Vector3.Normalize(perp) : AnyPerpendicular(ph);

            var best = new ArmPose { Miss = float.MaxValue };
            float bestScore = float.MinValue;
            const int steps = 24;
            for (int i = 0; i <= steps; i++)
            {
                float a = alongMin + (alongMax - alongMin) * i / steps;
                float l = a * forearm;
                if (l < 1e-4f) continue;
                float k = (pl * pl + l * l - upperArm * upperArm) / (2f * l);
                // cos of the angle between d and P; outside -1..1 the arm cannot reach (d along P: too far, against: too near)
                float c = Math.Clamp(pl > 1e-6f ? k / pl : 0f, -1f, 1f);
                float s = (float)Math.Sqrt(Math.Max(0f, 1f - c * c));
                Vector3 d = Vector3.Normalize(ph * c + perp * s);
                Vector3 elbow = cuff - d * l;
                float miss = Math.Abs(Vector3.Distance(elbow, shoulder) - upperArm);
                float upright = Vector3.Dot(d, axis);
                // reached solutions first, then the most upright, then the preferred cuff position
                float score = (miss > 1e-3f ? -10f - miss * 100f : 0f) + upright - AlongWeight * Math.Abs(a - alongPreferred);
                if (score <= bestScore) continue;
                bestScore = score;
                best = new ArmPose
                {
                    Elbow = elbow,
                    Hand = elbow + d * forearm,
                    Along = a,
                    TiltDeg = (float)(Math.Acos(Math.Clamp(upright, -1f, 1f)) * 180.0 / Math.PI),
                    Miss = miss,
                };
            }
            return best;
        }

        private static Vector3 AnyPerpendicular(Vector3 v)
        {
            Vector3 t = Math.Abs(v.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            return Vector3.Normalize(Vector3.Cross(v, t));
        }
    }
}
