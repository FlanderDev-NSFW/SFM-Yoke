using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Yoke.Logic
{
    public enum PartKind : byte { Neck = 0, Cuff = 1, Rod = 2 }

    /// <summary>One piece of the model in yoke coordinates (see <see cref="YokeModel"/>).</summary>
    public sealed class ModelPart
    {
        public PartKind Kind;
        public int Side;               // 0 = model x < 0 = her left, 1 = model x > 0 = her right, 2 = centre
        public Vector3 Color;
        public Vector3[] Positions;
        public Vector3[] Normals;
        public int[] Indices;
    }

    public struct RingAnchor
    {
        public Vector3 Center;         // middle of the ring (height: middle of the ring)
        public float InnerRadius, OuterRadius, Bottom, Top;
    }

    public struct RodAnchor
    {
        public float Inner, Outer;     // |x| of the end in the neck ring's knob / in the cuff's bracket
        public float Radius;
        public float Y, Z;             // the rod's axis
    }

    /// <summary>
    /// The yoke exported by tools/export_model.py (format described there), in yoke coordinates: origin = the neck ring's
    /// centre, x along the bar (her right), z up along her neck (the rings' axis), y front / back - in the file's units
    /// (about 7 per metre); <see cref="YokeFrame"/> scales and places them.
    /// </summary>
    public sealed class YokeModel
    {
        public const uint Magic = 0x454B4F59; // "YOKE"
        public RingAnchor Neck;
        public RingAnchor[] Cuffs = new RingAnchor[2];
        public RodAnchor[] Rods = new RodAnchor[2];
        public List<ModelPart> Parts = new List<ModelPart>();

        public static YokeModel Read(Stream stream)
        {
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a yoke model (magic)");
            uint version = r.ReadUInt32();
            if (version != 1) throw new InvalidDataException("unsupported model version " + version);
            var m = new YokeModel { Neck = Ring(r) };
            for (int s = 0; s < 2; s++) m.Cuffs[s] = Ring(r);
            for (int s = 0; s < 2; s++) m.Rods[s] = new RodAnchor { Inner = r.ReadSingle(), Outer = r.ReadSingle(), Radius = r.ReadSingle() };
            uint count = r.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                var p = new ModelPart { Kind = (PartKind)r.ReadByte(), Side = r.ReadByte(), Color = V(r) };
                if ((byte)p.Kind > 2 || p.Side > 2) throw new InvalidDataException("bad part header");
                int n = checked((int)r.ReadUInt32());
                p.Positions = new Vector3[n];
                p.Normals = new Vector3[n];
                for (int k = 0; k < n; k++) p.Positions[k] = V(r);
                for (int k = 0; k < n; k++) p.Normals[k] = V(r);
                int ni = checked((int)r.ReadUInt32());
                if (ni % 3 != 0) throw new InvalidDataException("index count is not a multiple of 3");
                p.Indices = new int[ni];
                for (int k = 0; k < ni; k++)
                {
                    uint idx = r.ReadUInt32();
                    if (idx >= n) throw new InvalidDataException("index out of range");
                    p.Indices[k] = (int)idx;
                }
                m.Parts.Add(p);
            }
            // The rods' axes (y, z) from their vertices: the middle of their extent across the bar.
            for (int s = 0; s < 2; s++)
            {
                float y0 = float.MaxValue, y1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (var p in m.Parts)
                    if (p.Kind == PartKind.Rod && p.Side == s)
                        foreach (var v in p.Positions) { y0 = Math.Min(y0, v.Y); y1 = Math.Max(y1, v.Y); z0 = Math.Min(z0, v.Z); z1 = Math.Max(z1, v.Z); }
                if (y0 > y1) throw new InvalidDataException("rod " + s + " missing");
                m.Rods[s].Y = (y0 + y1) / 2f;
                m.Rods[s].Z = (z0 + z1) / 2f;
            }
            return m;
        }

        private static RingAnchor Ring(BinaryReader r) => new RingAnchor
        {
            Center = V(r), InnerRadius = r.ReadSingle(), OuterRadius = r.ReadSingle(), Bottom = r.ReadSingle(), Top = r.ReadSingle(),
        };

        private static Vector3 V(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>Where a group hangs (model units): neck ring centre, cuff centre, the rod's end in the neck ring's knob.</summary>
        public Vector3 Anchor(PartKind kind, int side) => kind switch
        {
            PartKind.Neck => Neck.Center,
            PartKind.Cuff => Cuffs[side].Center,
            _ => new Vector3(side == 1 ? Rods[side].Inner : -Rods[side].Inner, Rods[side].Y, Rods[side].Z),
        };

        /// <summary>
        /// The yoke at a width (1 = as modelled): the cuffs move along the bar to <paramref name="width"/> times their modelled
        /// distance, the rods stretch between the neck ring's knobs and the cuffs' brackets. Returns where each cuff's centre is
        /// and the rods' stretch along the bar.
        /// </summary>
        public (Vector3 CuffL, Vector3 CuffR, float RodStretch) AtWidth(float width)
        {
            Vector3 Moved(int side)
            {
                var c = Cuffs[side].Center;
                return new Vector3(c.X * width, c.Y, c.Z);
            }
            var rod = Rods[1];
            float extra = (width - 1f) * Math.Abs(Cuffs[1].Center.X);
            return (Moved(0), Moved(1), (rod.Outer - rod.Inner + extra) / (rod.Outer - rod.Inner));
        }

        /// <summary>Narrowest width (factor) that leaves the rods <paramref name="minRod"/> of their length showing and keeps a gap
        /// between the cuffs and the neck ring.</summary>
        public float MinWidth(float minRod = 0.15f)
        {
            float cx = Math.Abs(Cuffs[1].Center.X);
            float rodLength = Rods[1].Outer - Rods[1].Inner;
            float byRod = 1f - rodLength * (1f - minRod) / cx;
            float byGap = (Neck.OuterRadius + Cuffs[1].OuterRadius + 0.1f) / cx;
            return Math.Max(byRod, byGap);
        }
    }

    /// <summary>
    /// Yoke coordinates -> world (any y-up frame, e.g. Unity's numbers): x along <see cref="Right"/> (her right), z along
    /// <see cref="Up"/> (up her neck), y = up x right, all scaled by <see cref="Scale"/> about <see cref="Origin"/> (the neck
    /// ring's centre). The same construction as WallRestraint's wall frame: determinant +1, triangle winding is kept.
    /// </summary>
    public readonly struct YokeFrame
    {
        public readonly Vector3 Origin, Right, Side, Up;
        public readonly float Scale;

        public YokeFrame(Vector3 origin, Vector3 right, Vector3 up, float scale)
        {
            Right = Vector3.Normalize(right);
            Up = Vector3.Normalize(up - Right * Vector3.Dot(up, Right)); // square to the shoulder line
            Side = Vector3.Cross(Up, Right);
            Origin = origin;
            Scale = scale;
        }

        public Vector3 Point(Vector3 model) => Origin + Direction(model) * Scale;

        public Vector3 Direction(Vector3 model) => Right * model.X + Side * model.Y + Up * model.Z;

        /// <summary>World -> yoke coordinates (inverse of <see cref="Point"/>).</summary>
        public Vector3 ToModel(Vector3 world)
        {
            Vector3 d = (world - Origin) / Scale;
            return new Vector3(Vector3.Dot(d, Right), Vector3.Dot(d, Side), Vector3.Dot(d, Up));
        }

        /// <summary>The rotation that turns model axes into world axes (for a transform that carries the meshes).</summary>
        public Quaternion Rotation()
        {
            // System.Numerics uses row vectors: row i is the image of model axis i.
            var m = new Matrix4x4(Right.X, Right.Y, Right.Z, 0, Side.X, Side.Y, Side.Z, 0, Up.X, Up.Y, Up.Z, 0, 0, 0, 0, 1);
            return Quaternion.CreateFromRotationMatrix(m);
        }
    }
}
