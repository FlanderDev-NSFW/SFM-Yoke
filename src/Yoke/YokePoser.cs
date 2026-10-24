using System;
using UnityEngine;
using Yoke.Logic;
using NVector3 = System.Numerics.Vector3;

namespace Yoke
{
    /// <summary>
    /// Holds her arms in the yoke: every <c>LateUpdate</c> (after the animator, before skinning) each upper arm is turned to
    /// its elbow target and each forearm to its hand target (<see cref="YokePose.Arm"/>); the rest of the body keeps its
    /// animation. The yoke's frame comes from the upper body, so it rides along while she walks or crouches.
    /// </summary>
    internal static class YokePoser
    {
        public struct Body
        {
            public float NeckRadius, UpperArm, Forearm;
            public float NeckFitRadius;    // the neck the ring is fitted to (NeckRadius only sizes the whole yoke)
        }

        /// <summary>Bone lengths (pose independent); the neck's thickness is estimated from the spine (like WallRestraint).</summary>
        public static bool Measure(Animator anim, out Body b, out string problem)
        {
            b = default;
            problem = null;
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            Transform hips = B(HumanBodyBones.Hips), neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
            Transform uaL = B(HumanBodyBones.LeftUpperArm), uaR = B(HumanBodyBones.RightUpperArm);
            Transform laL = B(HumanBodyBones.LeftLowerArm), laR = B(HumanBodyBones.RightLowerArm);
            Transform hdL = B(HumanBodyBones.LeftHand), hdR = B(HumanBodyBones.RightHand);
            if (hips == null || neck == null || head == null || uaL == null || uaR == null || laL == null || laR == null || hdL == null || hdR == null
                || Chest(anim) == null)
            {
                problem = "her skeleton is missing a bone";
                return false;
            }
            float D(Transform a, Transform c) => Vector3.Distance(a.position, c.position);
            b.UpperArm = (D(uaL, laL) + D(uaR, laR)) / 2f;
            b.Forearm = (D(laL, hdL) + D(laR, hdR)) / 2f;
            // Estimate until measured from the mesh (12 % of the spine, as WallRestraint's collar - L3 there).
            b.NeckRadius = 0.12f * D(hips, neck);
            // The neck itself is much thinner: 7 % of the spine (2.9 cm on the first save) - a ring fitted to the 12 % estimate
            // stood 2 cm off her neck; screenshots put the neck at about 2.7 cm [observed 2026-10-24].
            b.NeckFitRadius = 0.07f * D(hips, neck);
            if (b.UpperArm < 0.05f || b.Forearm < 0.05f || b.NeckRadius < 0.01f)
            {
                problem = "her proportions could not be measured";
                return false;
            }
            return true;
        }

        private static Transform Chest(Animator anim)
        {
            foreach (var bone in new[] { HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine })
            {
                var t = anim.GetBoneTransform(bone);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>The yoke on her as the bones are now: neck ring around the neck (<paramref name="ringAt"/> of the way up to the
        /// head), the bar along the shoulder line, up along chest -> neck.</summary>
        public static YokeFrame Frame(Animator anim, float ringAt, float scale)
        {
            Transform neck = anim.GetBoneTransform(HumanBodyBones.Neck), head = anim.GetBoneTransform(HumanBodyBones.Head);
            Vector3 right = anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position - anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            Vector3 up = neck.position - Chest(anim).position;
            Vector3 origin = Vector3.LerpUnclamped(neck.position, head.position, ringAt);
            return new YokeFrame(N(origin), N(right), N(up), scale);
        }

        // Bone lengths as drawn (WallRestraint's recipe, il2cpp-interop rules "Bones and rendering"): between LateUpdate and the
        // render the game changes the bones' local positions, building on what it finds. Record drawn - animator per bone in
        // onPreCull, aim with animator + offset in LateUpdate, then put the animator's values back.
        private static readonly HumanBodyBones[] Posed = {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
        private static readonly Vector3[] Animated = new Vector3[Posed.Length], Offset = new Vector3[Posed.Length];
        private static bool _haveAnimated, _haveOffset;

        public static void UseDrawnLengths(Animator anim, bool enabled)
        {
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t == null) continue;
                Animated[i] = t.localPosition;
                if (enabled && _haveOffset) t.localPosition = Animated[i] + Offset[i];
            }
            _haveAnimated = true;
        }

        public static void RestoreAnimatedLengths(Animator anim)
        {
            if (!_haveAnimated) return;
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t != null) t.localPosition = Animated[i];
            }
        }

        public static void RecordDrawn(Animator anim)
        {
            if (!_haveAnimated) return;
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t != null) Offset[i] = t.localPosition - Animated[i];
            }
            _haveOffset = true;
        }

        public static void ForgetDrawn() => _haveAnimated = _haveOffset = false;

        /// <summary>Both arms' targets for <paramref name="model"/> at <paramref name="frame"/> and <paramref name="width"/>, the cuffs
        /// sized by <paramref name="cuffFit"/> and swivelling up to <paramref name="maxCuffDeg"/> to follow the forearms.</summary>
        public static (ArmPose L, ArmPose R) Solve(Animator anim, YokeModel model, YokeFrame frame, float width, float cuffFit, float maxCuffDeg,
                                                   Body b, float cuffOnForearm)
        {
            // The cuff anywhere on the forearm from a little past its middle up to where the ring's top edge meets the wrist.
            const float alongMin = 0.55f;
            float cuffHalfHeight = (model.Cuffs[1].Top - model.Cuffs[1].Bottom) / 2f * frame.Scale * cuffFit;
            float alongMax = Math.Max(alongMin + 0.05f, 1f - cuffHalfHeight / b.Forearm);
            cuffOnForearm = Math.Min(cuffOnForearm, alongMax);
            ArmPose Side(HumanBodyBones upper, int side) => YokePose.ArmInSwivelCuff(frame, tilt => frame.Point(model.CuffCenter(side, width, tilt, cuffFit)),
                maxCuffDeg, N(anim.GetBoneTransform(upper).position), b.UpperArm, b.Forearm, alongMin, alongMax, cuffOnForearm);
            // Model side 0 is x < 0, her left.
            return (Side(HumanBodyBones.LeftUpperArm, 0), Side(HumanBodyBones.RightUpperArm, 1));
        }

        /// <summary>The neck ring's turn (yoke axes) for her neck as it is drawn now (neck joint -> head joint).</summary>
        public static System.Numerics.Quaternion DrawnNeckTilt(Animator anim, YokeFrame frame, float maxDeg)
        {
            Transform neck = anim.GetBoneTransform(HumanBodyBones.Neck), head = anim.GetBoneTransform(HumanBodyBones.Head);
            return YokePose.TiltTowards(frame.ModelDirection(N(head.position - neck.position)), maxDeg);
        }

        /// <summary>The cuff's turn (yoke axes) for the forearm as it is drawn now, so the visible cuff follows the visible arm.</summary>
        public static System.Numerics.Quaternion DrawnCuffTilt(Animator anim, YokeFrame frame, int side, float maxCuffDeg)
        {
            Transform lower = anim.GetBoneTransform(side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = anim.GetBoneTransform(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            return YokePose.TiltTowards(frame.ModelDirection(N(hand.position - lower.position)), maxCuffDeg);
        }

        public static void Apply(Animator anim, ArmPose l, ArmPose r)
        {
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            Aim(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), U(l.Elbow));
            Aim(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), U(r.Elbow));
            Aim(B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), U(l.Hand));
            Aim(B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), U(r.Hand));
        }

        /// <summary>Turn <paramref name="bone"/> so that <paramref name="child"/> lies in the direction of <paramref name="target"/>.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 target)
        {
            Vector3 cur = child.position - bone.position, want = target - bone.position;
            if (cur.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        internal static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);
        internal static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
