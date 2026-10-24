using System;
using System.IO;
using System.Numerics;
using Yoke.Logic;

namespace Yoke.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static bool Near(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;
        private static bool Near(Vector3 a, Vector3 b, float eps = 1e-4f) => Vector3.Distance(a, b) <= eps;

        private static int Main()
        {
            string modelPath = Path.Combine(AppContext.BaseDirectory, "yoke.ykm");
            if (File.Exists(modelPath)) { ModelTests(modelPath); FitTests(modelPath); SwivelTests(modelPath); }
            else Console.WriteLine("  SKIP  model tests (" + modelPath + " missing: export it with mods/Yoke/tools/export_model.py)");
            FormatErrorTests();
            FrameTests();
            ArmTests();

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static YokeModel Load(string path)
        {
            using var s = File.OpenRead(path);
            return YokeModel.Read(s);
        }

        private static void ModelTests(string path)
        {
            var m = Load(path);
            Check(m.Parts.Count == 5, "model: 5 parts (neck ring, 2 cuffs, 2 rods)");
            Check(m.Parts.TrueForAll(p => p.Positions.Length == p.Normals.Length && p.Indices.Length > 0), "model: every part has geometry");
            Check(Near(m.Neck.Center, Vector3.Zero), "model: origin is the neck ring's centre");
            Check(Near(m.Cuffs[0].Center.X, -m.Cuffs[1].Center.X, 1e-3f) && Near(m.Cuffs[0].Center.Y, m.Cuffs[1].Center.Y, 1e-3f), "model: cuffs mirror");
            Check(m.Cuffs[0].Center.X < 0 && m.Cuffs[1].Center.X > 0, "model: side 0 is x < 0");
            Check(m.Neck.InnerRadius > m.Cuffs[0].InnerRadius && m.Cuffs[0].InnerRadius > 0, "model: neck ring wider than the cuffs");
            Check(m.Neck.InnerRadius < m.Neck.OuterRadius && m.Cuffs[1].InnerRadius < m.Cuffs[1].OuterRadius, "model: rings inner < outer");
            Check(m.Rods[1].Inner > m.Neck.InnerRadius && m.Rods[1].Inner < m.Neck.OuterRadius + 0.2f, "model: rods start at the neck ring");
            Check(m.Rods[1].Outer > Math.Abs(m.Cuffs[1].Center.X) - m.Cuffs[1].OuterRadius - 0.05f && m.Rods[1].Outer < Math.Abs(m.Cuffs[1].Center.X),
                  "model: rods end in the cuffs' brackets");
            Check(Math.Abs(m.Rods[1].Y) < 0.05f && Math.Abs(m.Rods[1].Z) < 0.05f, "model: rods run through the rings' middle plane");
            bool sides = m.Parts.TrueForAll(p => p.Side == 2 || Array.TrueForAll(p.Positions, v => p.Side == 1 ? v.X > 0 : v.X < 0));
            Check(sides, "model: every side part lies on its side");
            Check(m.Anchor(PartKind.Rod, 0).X < 0 && Near(m.Anchor(PartKind.Rod, 1).X, m.Rods[1].Inner), "model: rod anchors at the neck ring's knobs");

            var w1 = m.AtWidth(1f);
            float rodLen = m.Rods[1].Outer - m.Rods[1].Inner;
            Check(Near(w1.CuffR, m.Cuffs[1].Center) && w1.RodStretch > 0.95f && w1.RodStretch < 1f, "width 1: cuffs as modelled, rods end a little short");
            var j1 = m.CuffJoint(1, 1f);
            Check(j1.X > m.Cuffs[1].Center.X - m.Cuffs[1].OuterRadius && j1.X < m.Cuffs[1].Center.X - m.Cuffs[1].InnerRadius,
                  "joint: inside the cuff's wall (" + j1.X.ToString("0.000") + ")");
            Check(Near(m.CuffJoint(0, 1f), new Vector3(-j1.X, j1.Y, j1.Z), 1e-3f), "joint: mirrors");
            Check(Near(m.Rods[1].Inner + rodLen * w1.RodStretch, j1.X), "width 1: the rod ends at the joint");
            var w2 = m.AtWidth(0.8f);
            Check(Near(w2.CuffR.X, 0.8f * m.Cuffs[1].Center.X) && Near(w2.CuffL.X, 0.8f * m.Cuffs[0].Center.X), "width 0.8: cuffs moved in");
            Check(Near(m.Rods[1].Inner + rodLen * w2.RodStretch, m.CuffJoint(1, 0.8f).X), "width 0.8: the rod still ends at the joint");

            // The neck ring turned: the rods run from its turned knobs to the cuffs' joints.
            var flatRod = m.RodEnds(1, 0.8f, Quaternion.Identity);
            Check(Near(flatRod.Knob, m.Anchor(PartKind.Rod, 1)) && Near(flatRod.Joint, m.CuffJoint(1, 0.8f)), "rod ends: neck ring not turned = knob and joint");
            var nod = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
            var nodRod = m.RodEnds(1, 0.8f, nod);
            Check(Near(nodRod.Knob, m.Anchor(PartKind.Rod, 1), 2e-2f) && Near(nodRod.Joint, flatRod.Joint), "rod ends: a nod turns the knob about the bar, the joint stays");
            var lean = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
            var leanRod = m.RodEnds(1, 0.8f, lean);
            Check(leanRod.Knob.Z < -0.1f && Near(leanRod.Knob.Length(), m.Anchor(PartKind.Rod, 1).Length(), 1e-4f), "rod ends: a sideways lean lowers the knob, same distance from the centre");
            var tight = m.RodEnds(1, 0.8f, Quaternion.Identity, 0.8f);
            Check(Near(tight.Knob.X, 0.8f * m.Anchor(PartKind.Rod, 1).X) && Near(tight.Joint, flatRod.Joint), "rod ends: a tighter neck ring pulls the knob in, the joint stays");
            var aim = YokePose.Turn(Vector3.UnitX, leanRod.Joint - leanRod.Knob);
            Check(Near(Vector3.Transform(Vector3.UnitX, aim), Vector3.Normalize(leanRod.Joint - leanRod.Knob)), "rod: aimed from the knob at the joint");
            Check(Near(Vector3.Transform(-Vector3.UnitX, YokePose.Turn(-Vector3.UnitX, new Vector3(-1, 0, 0.2f))), Vector3.Normalize(new Vector3(-1, 0, 0.2f))), "rod: left side aimed");
            Check(YokePose.Turn(Vector3.UnitX, Vector3.UnitX) == Quaternion.Identity, "turn: none needed");

            // Swivel: turned and sized about the joint.
            Check(Near(m.CuffCenter(1, 0.8f, Quaternion.Identity, 1f), w2.CuffR), "cuff centre: not turned, full size = moved centre");
            var j2 = m.CuffJoint(1, 0.8f);
            var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f);
            Check(Near(Vector3.Distance(m.CuffCenter(1, 0.8f, tilt, 1f), j2), Vector3.Distance(w2.CuffR, j2)), "cuff centre: turning keeps its distance to the joint");
            Check(Near(Vector3.Distance(m.CuffCenter(1, 0.8f, tilt, 0.5f), j2), 0.5f * Vector3.Distance(w2.CuffR, j2)), "cuff centre: sizing scales about the joint");
            float min = m.MinWidth();
            Check(min > 0.2f && min < 0.6f, "min width " + min);
            var wm = m.AtWidth(min);
            Check(wm.CuffR.X - m.Cuffs[1].OuterRadius > m.Neck.OuterRadius && wm.RodStretch > 0.1f, "min width: cuffs clear of the neck ring, rods shown");
        }

        // Proportions measured in game [observed 2026-10-24, WallRestraint WallController.Bones]: the first save's character
        // (about 1.5 m) and the much smaller character of the "Amateur" save.
        private struct Body { public float NeckRadius, ShoulderHalf, ShoulderDrop, UpperArm, Forearm; }
        private static readonly Body Adult = new Body { NeckRadius = 0.05f, ShoulderHalf = 0.104f, ShoulderDrop = 0.068f, UpperArm = 0.263f, Forearm = 0.252f };
        private static readonly Body Small = new Body { NeckRadius = 0.035f, ShoulderHalf = 0.05f, ShoulderDrop = 0.04f, UpperArm = 0.179f, Forearm = 0.172f };

        private static void FitTests(string path)
        {
            var m = Load(path);
            foreach (var (name, b) in new[] { ("adult", Adult), ("small", Small) })
            {
                float s = YokePose.Scale(m, b.NeckRadius, 0.005f, 1f);
                Check(Near(m.Neck.InnerRadius * s, b.NeckRadius + 0.005f), name + ": neck ring fits the neck");
                // The yoke as modelled, on a frame at her neck (world = yoke axes, metres).
                var f = new YokeFrame(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, s);
                float lastTilt = float.MaxValue;
                foreach (float width in new[] { 1f, 0.8f })
                {
                    var at = m.AtWidth(width);
                    Vector3 shoulder = new Vector3(b.ShoulderHalf, 0, -b.ShoulderDrop);
                    Vector3 cuff = f.Point(at.CuffR);
                    var arm = YokePose.Arm(shoulder, cuff, f.Up, b.UpperArm, b.Forearm, 0.55f, 0.95f, 0.85f);
                    string w = name + " width " + width + ": ";
                    Check(arm.Miss < 1e-3f, w + "the arm reaches the cuff (cuff " + cuff.X.ToString("0.000") + " m out, tilt " + arm.TiltDeg.ToString("0") + " deg)");
                    Check(arm.Hand.Z > cuff.Z && arm.Elbow.Z < cuff.Z, w + "hand above the cuff, elbow below");
                    Check(arm.Elbow.X > shoulder.X, w + "elbow out to the side");
                    // As modelled the cuffs are wider than her bent arms: the forearms lean out (adult 36 deg, small 51 deg);
                    // a narrower yoke stands them up.
                    Check(arm.TiltDeg < lastTilt && arm.TiltDeg < 60f, w + "forearm leans less than at the wider width");
                    lastTilt = arm.TiltDeg;
                    if (width < 1f && name == "adult") Check(arm.TiltDeg < 25f, w + "nearly upright (" + arm.TiltDeg.ToString("0") + " deg)");
                    Check(arm.Along > 0.75f, w + "cuff stays near the wrist (" + arm.Along.ToString("0.00") + " of the forearm)");
                }
            }
        }

        private static void FormatErrorTests()
        {
            bool Throws(byte[] data)
            {
                try { YokeModel.Read(new MemoryStream(data)); return false; }
                catch (InvalidDataException) { return true; }
                catch (EndOfStreamException) { return true; }
            }
            Check(Throws(new byte[] { 1, 2, 3, 4, 1, 0, 0, 0 }), "format: wrong magic refused");
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(YokeModel.Magic);
            w.Write(2u);
            Check(Throws(ms.ToArray()), "format: unknown version refused");
            Check(Throws(new byte[] { 0x59, 0x4F, 0x4B, 0x45, 1, 0, 0, 0, 0 }), "format: truncated file refused");
        }

        private static void FrameTests()
        {
            // Unity's numbers (y up): her right = +x, up = +y. Model y = up x right.
            var f = new YokeFrame(new Vector3(1, 1.4f, 2), new Vector3(1, 0, 0), new Vector3(0.1f, 1, 0), 0.5f);
            Check(Near(Vector3.Dot(f.Up, f.Right), 0f) && Near(f.Up.Length(), 1f), "frame: up squared to the shoulder line");
            Check(Near(f.Point(Vector3.Zero), new Vector3(1, 1.4f, 2)), "frame: origin");
            Check(Near(f.Point(new Vector3(2, 0, 0)), new Vector3(2, 1.4f, 2)), "frame: model x -> right, scaled");
            Check(Near(f.ToModel(f.Point(new Vector3(0.3f, -0.2f, 0.7f))), new Vector3(0.3f, -0.2f, 0.7f)), "frame: ToModel inverts Point");
            Check(Near(Vector3.Dot(Vector3.Cross(f.Right, f.Side), f.Up), 1f), "frame: right-handed basis (winding kept)");
            var q = f.Rotation();
            Check(Near(Vector3.Transform(Vector3.UnitX, q), f.Right) && Near(Vector3.Transform(Vector3.UnitZ, q), f.Up), "frame: rotation maps the axes");
        }

        private static void SwivelTests(string path)
        {
            var m = Load(path);
            var b = Adult;
            float s = YokePose.Scale(m, b.NeckRadius, 0.005f, 1f);
            var f = new YokeFrame(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, s);
            Vector3 shoulder = new Vector3(b.ShoulderHalf, 0, -b.ShoulderDrop);
            foreach (float width in new[] { 1f, 1.15f })
            {
                string w = "swivel width " + width + ": ";
                const float fit = 0.6f;
                var arm = YokePose.ArmInSwivelCuff(f, t => f.Point(m.CuffCenter(1, width, t, fit)), 70f, shoulder, b.UpperArm, b.Forearm, 0.55f, 0.95f, 0.85f);
                Vector3 cuff = f.Point(m.CuffCenter(1, width, arm.CuffTilt, fit));
                Vector3 fore = Vector3.Normalize(arm.Hand - arm.Elbow);
                Check(arm.Miss < 1e-3f, w + "reached (lean " + arm.LeanDeg.ToString("0") + " deg)");
                Check(Near(arm.Elbow + fore * (arm.Along * b.Forearm), cuff, 1e-3f), w + "forearm through the turned cuff's centre");
                Check(arm.TiltDeg < 0.5f, w + "cuff follows the forearm (" + arm.TiltDeg.ToString("0.00") + " deg off)");
                Vector3 axis = Vector3.Transform(Vector3.UnitZ, arm.CuffTilt);
                Check(axis.X > 0.2f, w + "cuff leans out with the forearm");
            }
            // Fixed cuffs: as before (upright, the forearm leans through it).
            var fixedArm = YokePose.ArmInSwivelCuff(f, t => f.Point(m.CuffCenter(1, 1f, t, 1f)), 0f, shoulder, b.UpperArm, b.Forearm, 0.55f, 0.95f, 0.85f);
            Check(fixedArm.CuffTilt == Quaternion.Identity && Near(fixedArm.TiltDeg, fixedArm.LeanDeg, 1e-3f), "swivel 0: cuff stays upright");
            // At the limit the forearm leans past the cuff.
            var limited = YokePose.ArmInSwivelCuff(f, t => f.Point(m.CuffCenter(1, 1f, t, 1f)), 10f, shoulder, b.UpperArm, b.Forearm, 0.55f, 0.95f, 0.85f);
            Check(limited.TiltDeg > 5f, "swivel limited to 10 deg: forearm " + limited.TiltDeg.ToString("0") + " deg off the cuff");
        }

        private static void ArmTests()
        {
            var up = YokePose.TiltTowards(Vector3.UnitZ, 70f);
            Check(up == Quaternion.Identity, "cuff tilt: upright forearm -> not turned");
            var leanTilt = YokePose.TiltTowards(new Vector3(1, 0, 1), 70f);
            Check(Near(Vector3.Transform(Vector3.UnitZ, leanTilt), Vector3.Normalize(new Vector3(1, 0, 1))), "cuff tilt: axis onto the forearm");
            var flat = YokePose.TiltTowards(new Vector3(1, 0, 0.1f), 30f);
            Check(Near(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, flat), Vector3.UnitZ), (float)Math.Cos(Math.PI / 6), 1e-4f), "cuff tilt: limited");

            // Directly under the cuff: the forearm stands upright.
            var a = YokePose.Arm(Vector3.Zero, new Vector3(0.2f, 0, 0.0f), Vector3.UnitZ, 0.3f, 0.25f, 0.5f, 0.95f, 0.85f);
            Check(a.Miss < 1e-3f && a.TiltDeg < 5f, "arm: cuff in reach of an upright forearm -> (nearly) upright (" + a.TiltDeg + ")");
            Check(Near(Vector3.Distance(a.Elbow, a.Hand), 0.25f, 1e-4f), "arm: forearm keeps its length");
            Vector3 cuff = new Vector3(0.2f, 0, 0);
            Vector3 fore = Vector3.Normalize(a.Hand - a.Elbow);
            Check(Near(a.Elbow + fore * (a.Along * 0.25f), cuff, 1e-4f), "arm: the forearm passes through the cuff");
            // Too far: reports the miss.
            var far = YokePose.Arm(Vector3.Zero, new Vector3(1f, 0, 0), Vector3.UnitZ, 0.3f, 0.25f, 0.5f, 0.95f, 0.85f);
            Check(far.Miss > 0.4f, "arm: cuff out of reach -> miss " + far.Miss);
            // Far but reachable: leans out, still exact.
            var lean = YokePose.Arm(Vector3.Zero, new Vector3(0.4f, 0, 0.05f), Vector3.UnitZ, 0.3f, 0.25f, 0.5f, 0.95f, 0.85f);
            Check(lean.Miss < 1e-3f && lean.TiltDeg > 5f && Vector3.Normalize(lean.Hand - lean.Elbow).X > 0, "arm: wide cuff -> forearm leans out (" + lean.TiltDeg + ")");
        }
    }
}
