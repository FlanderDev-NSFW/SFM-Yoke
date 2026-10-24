"""Export the yoke of Yoke_V1.1_Z88OaNZ.blend (collection "Yoke", object "Yoke") into the mod's runtime model (assets/yoke.ykm).

Usage (Blender 5.x, headless):
    blender -b --disable-autoexec <Yoke_V1.1_Z88OaNZ.blend> --python mods/Yoke/tools/export_model.py -- <out.ykm>

The yoke is one mesh (from a Skyrim mod, all weighted to one bone, rest pose) made of 124 loose pieces: a neck ring in the
middle, a wrist cuff at each end (rings with an upright axis, a lock and two eyelets) and a straight rod from the neck ring to
each cuff. The pieces are sorted by where they lie along the bar (Blender x):
  * |x| inside the neck ring's reach        -> neck ring (ring, lock, eyelets, the knobs the rods go into)
  * every vertex beyond the cuffs' brackets  -> cuff of that side
  * the rest (long thin pieces)              -> rod of that side (stretched at runtime for + / -)
Axes are kept (units as in the file, x along the bar, y front / back, z up = the rings' axis), moved so the neck ring's centre
is the origin. The runtime scales the model to her neck and maps x -> her right, z -> up along her neck (Logic/YokeModel.cs).

Format (little endian): "YOKE" u32 version=1
  neck  f32x3 center, f32 innerRadius, f32 outerRadius, f32 bottom, f32 top
  2 x cuff (side 0 = x < 0, 1 = x > 0): f32x3 center, f32 innerRadius, f32 outerRadius, f32 bottom, f32 top
  2 x rod (side as above): f32 inner (|x| of the end in the neck ring's knob), f32 outer (|x| of the end in the cuff's bracket),
                           f32 radius
  u32 partCount, per part: u8 kind (0 neck, 1 cuff, 2 rod), u8 side (0, 1, 2 = centre), f32x3 color,
      u32 vertexCount, f32x3 positions[], f32x3 normals[], u32 indexCount, u32 indices[]
"""
import math
import struct
import sys

import bpy
from mathutils import Vector

out_path = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "yoke.ykm"
OBJECT = "Yoke"
METAL = (0.14, 0.14, 0.14)
NECK_REACH = 0.62   # |x| of the neck ring's knobs (the rods start at 0.418)
CUFF_START = 2.84   # |x| where the cuffs' brackets begin (the rods end at 2.991, inside the brackets)

dg = bpy.context.evaluated_depsgraph_get()
o = bpy.data.objects[OBJECT]
e = o.evaluated_get(dg)
m = e.to_mesh()
m.calc_loop_triangles()
mw = e.matrix_world
nmat = mw.to_3x3().inverted().transposed()
verts = [mw @ v.co for v in m.vertices]

# --- loose pieces (connected by edges) ----------------------------------------------------------------
parent = list(range(len(verts)))


def find(i):
    while parent[i] != i:
        parent[i] = parent[parent[i]]
        i = parent[i]
    return i


for ed in m.edges:
    a, b = find(ed.vertices[0]), find(ed.vertices[1])
    if a != b:
        parent[a] = b
pieces = {}
for i in range(len(verts)):
    pieces.setdefault(find(i), []).append(i)


def classify(ids):
    xs = [verts[i].x for i in ids]
    lo, hi = min(xs), max(xs)
    if hi <= NECK_REACH and lo >= -NECK_REACH:
        return 0, 2
    if lo >= CUFF_START:
        return 1, 1
    if hi <= -CUFF_START:
        return 1, 0
    if lo > 0 and hi > CUFF_START - 0.1:
        return 2, 1
    if hi < 0 and lo < -(CUFF_START - 0.1):
        return 2, 0
    raise SystemExit("unclassified piece x %.3f..%.3f (%d vertices)" % (lo, hi, len(ids)))


kind_of = {}
for ids in pieces.values():
    k = classify(ids)
    for i in ids:
        kind_of[i] = k


def bbox(points):
    return (Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points))),
            Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points))))


def radii(points, cx, cy):
    rs = [math.hypot(p.x - cx, p.y - cy) for p in points]
    return min(rs), max(rs)


# --- anchors -------------------------------------------------------------------------------------------
# Neck ring: the largest neck piece is the ring itself; its bounding box centre is the ring's centre.
neck_ids = max((ids for ids in pieces.values() if kind_of[ids[0]] == (0, 2)), key=len)
lo, hi = bbox([verts[i] for i in neck_ids])
center = (lo + hi) / 2
origin = Vector((0.0, center.y, center.z))  # x stays on the symmetry plane
neck_pts = [verts[i] - origin for i in neck_ids]
neck_inner, neck_outer = radii(neck_pts, 0.0, 0.0)
neck = (Vector((0.0, 0.0, 0.0)), neck_inner, neck_outer, lo.z - origin.z, hi.z - origin.z)

cuffs = []
for side in (0, 1):
    ids = max((ids for ids in pieces.values() if kind_of[ids[0]] == (1, side)), key=len)
    pts = [verts[i] - origin for i in ids]
    lo, hi = bbox(pts)
    # The ring is round: its outer radius is half its depth (y); the centre lies that far in from the far rim (the
    # bracket on the rod side would shift a bounding box centre).
    r_out = (hi.y - lo.y) / 2
    cx = (hi.x - r_out) if side == 1 else (lo.x + r_out)
    cy = (lo.y + hi.y) / 2
    ring = [p for p in pts if math.hypot(p.x - cx, p.y - cy) < r_out + 1e-4]
    inner, outer = radii(ring, cx, cy)
    cuffs.append((Vector((cx, cy, (lo.z + hi.z) / 2)), inner, outer, lo.z, hi.z))

rods = []
for side in (0, 1):
    pts = [verts[i] - origin for i, k in kind_of.items() if k == (2, side)]
    lo, hi = bbox(pts)
    radius = max(hi.y - lo.y, hi.z - lo.z) / 2
    rods.append((min(abs(lo.x), abs(hi.x)), max(abs(lo.x), abs(hi.x)), radius))

print("YKM-CHECK origin (file) %s" % (tuple(round(v, 4) for v in origin),))
print("YKM-CHECK neck inner r %.4f outer r %.4f z %.4f..%.4f" % neck[1:])
for s, (c, ri, ro, b, t) in enumerate(cuffs):
    print("YKM-CHECK cuff %d center %s inner r %.4f outer r %.4f z %.4f..%.4f" % (s, tuple(round(v, 4) for v in c), ri, ro, b, t))
for s, (a, b, r) in enumerate(rods):
    print("YKM-CHECK rod %d |x| %.4f..%.4f radius %.4f" % (s, a, b, r))

# --- parts ---------------------------------------------------------------------------------------------
parts = {}  # (kind, side) -> [positions, normals, indices, index_of]
normals = m.corner_normals
for tri in m.loop_triangles:
    key = kind_of[tri.vertices[0]]
    if any(kind_of[v] != key for v in tri.vertices):
        raise SystemExit("a triangle spans two pieces")
    if key not in parts:
        parts[key] = ([], [], [], {})
    pos, nrm, idx, index_of = parts[key]
    for li, v in zip(tri.loops, tri.vertices):
        n = (nmat @ normals[li].vector).normalized()
        k = (v, round(n.x, 4), round(n.y, 4), round(n.z, 4))
        if k not in index_of:
            index_of[k] = len(pos)
            pos.append(verts[v] - origin)
            nrm.append(n)
        idx.append(index_of[k])
e.to_mesh_clear()


# --- write ---------------------------------------------------------------------------------------------
def v3(f, v):
    f.write(struct.pack("<3f", v[0], v[1], v[2]))


with open(out_path, "wb") as f:
    f.write(b"YOKE")
    f.write(struct.pack("<I", 1))
    v3(f, neck[0])
    f.write(struct.pack("<4f", *neck[1:]))
    for c, ri, ro, b, t in cuffs:
        v3(f, c)
        f.write(struct.pack("<4f", ri, ro, b, t))
    for a, b, r in rods:
        f.write(struct.pack("<3f", a, b, r))
    f.write(struct.pack("<I", len(parts)))
    total = 0
    for (kind, side), (pos, nrm, idx, _) in sorted(parts.items()):
        f.write(struct.pack("<2B", kind, side))
        v3(f, METAL)
        f.write(struct.pack("<I", len(pos)))
        for p in pos:
            v3(f, p)
        for n in nrm:
            v3(f, n)
        f.write(struct.pack("<I", len(idx)))
        f.write(struct.pack("<%dI" % len(idx), *idx))
        total += len(idx) // 3
        print("YKM-CHECK part kind %d side %d: %d vertices, %d triangles" % (kind, side, len(pos), len(idx) // 3))
print("YKM-CHECK wrote %s: %d parts, %d triangles" % (out_path, len(parts), total))
