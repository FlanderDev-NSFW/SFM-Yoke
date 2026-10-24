using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using Yoke.Logic;
using NVector3 = System.Numerics.Vector3;

namespace Yoke
{
    /// <summary>
    /// The visible yoke: five groups (neck ring, cuff and rod per side), each holding its model part with vertices relative
    /// to the group's anchor (<see cref="YokeModel.Anchor"/>), so the cuffs can move along the bar and the rods stretch for the
    /// width.
    /// </summary>
    internal sealed class YokeRig
    {
        private readonly YokeModel _model;
        private GameObject _root;
        private readonly Dictionary<(PartKind, int), Transform> _groups = new Dictionary<(PartKind, int), Transform>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(); // meshes and materials

        public YokeRig(YokeModel model) { _model = model; }

        public bool Built => _root != null;

        public void Build(int layer)
        {
            Destroy();
            _root = new GameObject("Yoke");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.layer = layer;
            Material source = FindHandcuffMaterial();
            foreach (var part in _model.Parts)
            {
                var g = new GameObject(part.Kind + (part.Side == 2 ? "" : part.Side == 0 ? "L" : "R"));
                g.layer = layer;
                g.transform.SetParent(_root.transform, false);
                _groups[(part.Kind, part.Side)] = g.transform;
                NVector3 anchor = _model.Anchor(part.Kind, part.Side);
                var mesh = new Mesh { name = "Yoke." + g.name };
                if (part.Positions.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var v = new Vector3[part.Positions.Length];
                var n = new Vector3[part.Normals.Length];
                for (int i = 0; i < v.Length; i++) v[i] = YokePoser.U(part.Positions[i] - anchor);
                for (int i = 0; i < n.Length; i++) n[i] = YokePoser.U(part.Normals[i]);
                mesh.vertices = v;
                mesh.normals = n;
                mesh.triangles = part.Indices;
                mesh.RecalculateBounds();
                mesh.hideFlags = HideFlags.DontUnloadUnusedAsset; // survives map changes (UnloadUnusedAssets)
                _owned.Add(mesh);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = g.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = MakeMaterial(source, part);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            _root.SetActive(false);
        }

        /// <summary>Put the yoke on <paramref name="frame"/> at <paramref name="width"/> (see <see cref="YokeModel.AtWidth"/>), the
        /// cuffs scaled by <paramref name="cuffFit"/> and turned by <paramref name="tiltL"/> / <paramref name="tiltR"/> (yoke axes)
        /// about their joints (<see cref="YokeModel.CuffJoint"/>).</summary>
        public void Place(YokeFrame frame, float width, float neckFit, float cuffFit, System.Numerics.Quaternion neckTilt,
                          System.Numerics.Quaternion tiltL, System.Numerics.Quaternion tiltR)
        {
            if (_root == null) return;
            var rot = U(frame.Rotation());
            float s = frame.Scale;
            // The neck ring is sized to her neck and turns with it, both about its centre.
            Set(PartKind.Neck, 2, frame.Point(_model.Neck.Center), rot * U(neckTilt), Vector3.one * (s * neckFit));
            // The cuff group's mesh hangs from the cuff's centre: put that centre where the turn about the joint takes it.
            Set(PartKind.Cuff, 0, frame.Point(_model.CuffCenter(0, width, tiltL, cuffFit)), rot * U(tiltL), Vector3.one * (s * cuffFit));
            Set(PartKind.Cuff, 1, frame.Point(_model.CuffCenter(1, width, tiltR, cuffFit)), rot * U(tiltR), Vector3.one * (s * cuffFit));
            // Rods: from the (turned) neck ring's knob to the cuff's joint - aimed along that line and stretched to its length.
            PlaceRod(frame, rot, 0, width, neckTilt, neckFit);
            PlaceRod(frame, rot, 1, width, neckTilt, neckFit);
        }

        private void PlaceRod(YokeFrame frame, Quaternion rot, int side, float width, System.Numerics.Quaternion neckTilt, float neckFit)
        {
            var (knob, joint) = _model.RodEnds(side, width, neckTilt, neckFit);
            var rod = _model.Rods[side];
            NVector3 along = new NVector3(side == 1 ? 1f : -1f, 0f, 0f); // the rod's mesh runs from its anchor outwards
            NVector3 span = joint - knob;
            var aim = YokePose.Turn(along, span);
            Set(PartKind.Rod, side, frame.Point(knob), rot * U(aim), new Vector3(frame.Scale * span.Length() / (rod.Outer - rod.Inner), frame.Scale, frame.Scale));
        }

        private static Quaternion U(System.Numerics.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);

        private void Set(PartKind kind, int side, NVector3 at, Quaternion rot, Vector3 scale)
        {
            if (!_groups.TryGetValue((kind, side), out Transform t)) return;
            t.SetPositionAndRotation(YokePoser.U(at), rot);
            t.localScale = scale;
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>The game's handcuff material (UnityChan toon shader), so the yoke is lit and outlined like the game's own items.</summary>
        private static Material FindHandcuffMaterial()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>());
                foreach (var o in all)
                {
                    var mat = o.TryCast<Material>();
                    if (mat != null && mat.name != null && mat.name.StartsWith("tejyou", StringComparison.Ordinal)) return mat;
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("handcuff material lookup failed: " + e.Message); }
            Plugin.Logger.LogWarning("handcuff material not found; using a toon/standard shader");
            return null;
        }

        private Material MakeMaterial(Material source, ModelPart part)
        {
            Material mat;
            if (source != null) mat = new Material(source);
            else
            {
                Shader shader = Shader.Find("UnityChanToonShader/Toon_DoubleShadeWithFeather");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
            }
            mat.name = "Yoke.Metal";
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Texture white = Texture2D.whiteTexture;
            foreach (string tex in new[] { "_MainTex", "_BaseMap", "_1st_ShadeMap", "_2nd_ShadeMap" })
                if (mat.HasProperty(tex)) mat.SetTexture(tex, white);
            // The model's metal is black chrome; toon shading has no reflections, so lift it to a dark gunmetal (as WallRestraint).
            float shade = Plugin.MetalShade.Value;
            Color baseColor = new Color(shade, shade, shade, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_1st_ShadeColor")) mat.SetColor("_1st_ShadeColor", baseColor * 0.62f);
            if (mat.HasProperty("_2nd_ShadeColor")) mat.SetColor("_2nd_ShadeColor", baseColor * 0.38f);
            _owned.Add(mat);
            return mat;
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _groups.Clear();
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
