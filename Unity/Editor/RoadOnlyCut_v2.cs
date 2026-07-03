// =============================================================================
//  RoadOnlyCut_v2.cs   (class RoadOnlyCutV2)
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  Fix over RoadOnlyCut: MapRoads in this scene has 4275 children, but ~3004 of them
//  are "(Footpath)" (pavements / pedestrian paths / park paths), not drivable road.
//  v1 collected ALL of them, so it cut footpaths across pavements/plazas/parks - i.e.
//  it cut "non-road flat ground". v2 EXCLUDES footpaths (by name token) and cuts only
//  the drivable carriageways: (Main Road) / (Minor Road) / (Dual Carriageway) /
//  (Turning Circle). Everything else identical: cut the carriageway corridor only,
//  invertSelection=false, leave buildings untouched, keep the existing Clipping overlay
//  and DualClip material.
//
//  Pair with the DualClip height gate ("横着切"): with the gate ACTIVE, only the LOW
//  part of the carriageway is cut, so overhangs/facades leaning over the road are kept.
//
//  OPEN VIA: Tools > CityGen3D x Cesium > Road Only Cut v2
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

namespace SustainableDriving.SimulationTools.EditorTools
{
    public sealed class RoadOnlyCutV2 : EditorWindow
    {
        [SerializeField] private Cesium3DTileset tileset;
        [SerializeField] private CesiumGeoreference georeference;
        [SerializeField] private Material dualClipMaterial;
        [SerializeField] private Transform mapRoads;
        [SerializeField] private string mapRoadsName = "MapRoads";
        [SerializeField] private bool includeInactive = true;
        [SerializeField] private string landscapePrefix = "Landscape (";

        // exclude non-drivable road children by name token (footpaths are the big one here)
        [SerializeField] private string excludeTokens = "Footpath";

        [SerializeField] private float roadMargin = 0.3f;
        [SerializeField] private float vertexWeld = 0.05f;
        [SerializeField] private float simplifyTol = 0.15f;
        [SerializeField] private float minArea = 1.0f;
        [SerializeField] private int maxPolygons = 6000;
        [SerializeField] private bool limitToStudyArea = true;

        private Vector2 areaMin = new(-256, -256), areaMax = new(1280, 1280);
        private const string ClippingKey = "Clipping";
        private const string RoadCutoutKey = "RoadCutout";
        private const string RootName = "Road Only Cut v2 Polygons";
        private string status = "Detect -> Build. Cuts ONLY drivable carriageways (excludes Footpaths). Buildings untouched.";

        [MenuItem("Tools/CityGen3D x Cesium/Road Only Cut v2")]
        public static void Open() => GetWindow<RoadOnlyCutV2>("Road Only Cut v2");

        private void OnEnable() => Detect();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Road Only Cut v2 (carriageways only, no footpaths)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Cuts Cesium only over the DRIVABLE roads (Main/Minor/Dual/Turning), excluding " +
                "MapRoads footpaths (which run across pavements/plazas/parks). Pair with the DualClip " +
                "height gate to cut only the LOW part of the carriageway (keeps overhangs). Keeps the " +
                "existing Clipping overlay; uses the existing DualClip material. NO shader wiring.",
                MessageType.None);

            tileset = (Cesium3DTileset)EditorGUILayout.ObjectField("Cesium3DTileset", tileset, typeof(Cesium3DTileset), true);
            georeference = (CesiumGeoreference)EditorGUILayout.ObjectField("CesiumGeoreference", georeference, typeof(CesiumGeoreference), true);
            dualClipMaterial = (Material)EditorGUILayout.ObjectField("DualClip material", dualClipMaterial, typeof(Material), false);
            mapRoads = (Transform)EditorGUILayout.ObjectField("MapRoads", mapRoads, typeof(Transform), true);

            EditorGUILayout.Space(4);
            excludeTokens = EditorGUILayout.TextField("Exclude name tokens", excludeTokens);
            roadMargin = Mathf.Max(0f, EditorGUILayout.FloatField("Road expand out (m)", roadMargin));
            simplifyTol = Mathf.Max(0.01f, EditorGUILayout.FloatField("Simplify tolerance (m)", simplifyTol));
            vertexWeld = Mathf.Max(0.01f, EditorGUILayout.FloatField("Boundary weld tol (m)", vertexWeld));
            minArea = EditorGUILayout.FloatField("Min road area (m^2)", minArea);
            maxPolygons = Mathf.Max(1, EditorGUILayout.IntField("Max polygons (keep largest)", maxPolygons));
            limitToStudyArea = EditorGUILayout.Toggle("Limit to study area (Landscape)", limitToStudyArea);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Detect")) Detect();
            if (GUILayout.Button("Build road cut", GUILayout.Height(28))) Build();
            if (GUILayout.Button("Clear")) Clear();

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        private string[] ExArr() =>
            (excludeTokens ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();

        private static bool Excluded(string name, string[] toks)
        {
            if (toks.Length == 0) return false;
            string h = name.ToLowerInvariant();
            return toks.Any(h.Contains);
        }

        private List<MeshFilter> CollectRoadFilters()
        {
            if (mapRoads == null) return new List<MeshFilter>();
            var toks = ExArr();
            return mapRoads.GetComponentsInChildren<MeshFilter>(includeInactive)
                .Where(f => f.sharedMesh != null && !Excluded(f.gameObject.name, toks)).ToList();
        }

        private void Detect()
        {
            if (tileset == null) tileset = FindFirstObjectByType<Cesium3DTileset>();
            if (georeference == null && tileset != null) georeference = tileset.GetComponentInParent<CesiumGeoreference>();
            if (georeference == null) georeference = FindFirstObjectByType<CesiumGeoreference>();
            if (mapRoads == null)
                mapRoads = FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == mapRoadsName);
            if (dualClipMaterial == null)
            {
                string g = AssetDatabase.FindAssets("CesiumDefaultTilesetDualClippingMaterial t:Material").FirstOrDefault();
                if (g != null) dualClipMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            }
            if (limitToStudyArea) ComputeStudyArea();
            int total = mapRoads == null ? 0 : mapRoads.GetComponentsInChildren<MeshFilter>(includeInactive).Count(f => f.sharedMesh != null);
            int kept = CollectRoadFilters().Count;
            bool hasClip = tileset != null && tileset.GetComponents<CesiumPolygonRasterOverlay>().Any(o => o.materialKey == ClippingKey);
            status = $"tileset {(tileset ? "OK" : "MISSING")}; georeference {(georeference ? "OK" : "MISSING")}; material {(dualClipMaterial ? "OK" : "MISSING")}; " +
                     $"MapRoads {(mapRoads ? "OK" : "MISSING")}: {kept} drivable kept / {total} total ({total - kept} excluded by '{excludeTokens}'); " +
                     $"Clipping {(hasClip ? "OK" : "MISSING")}.";
        }

        private void ComputeStudyArea()
        {
            var terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith(landscapePrefix, StringComparison.Ordinal)).ToArray();
            if (terrains.Length == 0) return;
            Vector2 mn = new(float.MaxValue, float.MaxValue), mx = new(float.MinValue, float.MinValue);
            foreach (var t in terrains)
            {
                Vector3 p = t.transform.position; Vector3 s = t.terrainData.size;
                mn.x = Mathf.Min(mn.x, p.x); mn.y = Mathf.Min(mn.y, p.z);
                mx.x = Mathf.Max(mx.x, p.x + s.x); mx.y = Mathf.Max(mx.y, p.z + s.z);
            }
            areaMin = mn; areaMax = mx;
        }

        private void Build()
        {
            Detect();
            if (tileset == null || georeference == null) { status = "Need tileset + georeference."; return; }
            if (mapRoads == null) { status = "No MapRoads."; return; }
            var filters = CollectRoadFilters();
            if (filters.Count == 0) { status = "No drivable road meshes after exclude filter."; return; }

            var loops = new List<List<Vector3>>();
            try
            {
                for (int i = 0; i < filters.Count; i++)
                {
                    if ((i & 63) == 0 && EditorUtility.DisplayCancelableProgressBar("Road Only Cut v2", $"Road {i}/{filters.Count}", (float)i / filters.Count))
                    { status = "Cancelled."; return; }
                    var main = LargestLoop(filters[i].sharedMesh, filters[i].transform);
                    if (main == null || main.Count < 3) continue;
                    Vector3 c = Centroid(main);
                    if (limitToStudyArea && (c.x < areaMin.x || c.x > areaMax.x || c.z < areaMin.y || c.z > areaMax.y)) continue;
                    var loop = Simplify(roadMargin > 0 ? OffsetLoopOutward(main, roadMargin) : main, simplifyTol);
                    if (loop.Count >= 3 && PolygonArea(loop) >= minArea) loops.Add(loop);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            loops = loops.OrderByDescending(PolygonArea).Take(maxPolygons).ToList();
            if (loops.Count == 0) { status = "No usable road loops."; return; }

            Clear();
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create road cut v2 root");
            root.transform.SetParent(georeference.transform, false);

            var polys = new List<CesiumCartographicPolygon>(loops.Count);
            for (int i = 0; i < loops.Count; i++)
            {
                var p = CreateCartographicPolygon(root.transform, $"Road {i}", loops[i]);
                if (p != null) polys.Add(p);
            }

            foreach (var ov in tileset.GetComponents<CesiumPolygonRasterOverlay>())
                if (ov.materialKey == RoadCutoutKey) Undo.DestroyObjectImmediate(ov);
            var overlay = Undo.AddComponent<CesiumPolygonRasterOverlay>(tileset.gameObject);
            overlay.materialKey = RoadCutoutKey;
            overlay.polygons = polys;
            overlay.invertSelection = false;
            overlay.excludeSelectedTiles = false;
            EditorUtility.SetDirty(overlay);

            if (dualClipMaterial != null && tileset.opaqueMaterial != dualClipMaterial)
            { Undo.RecordObject(tileset, "Assign DualClip material"); tileset.opaqueMaterial = dualClipMaterial; EditorUtility.SetDirty(tileset); }
            EditorUtility.SetDirty(tileset.gameObject);
            if (root.scene.IsValid()) EditorSceneManager.MarkSceneDirty(root.scene);

            bool hasClip = tileset.GetComponents<CesiumPolygonRasterOverlay>().Any(o => o.materialKey == ClippingKey);
            status = $"Built {polys.Count} drivable-road polygons (excluded '{excludeTokens}'). " +
                     (hasClip ? "Clipping present." : "WARNING: no Clipping overlay.") +
                     " With the DualClip height gate active, only the LOW part of the carriageway is cut.";
            Debug.Log("[RoadOnlyCutV2] " + status);
        }

        private CesiumCartographicPolygon CreateCartographicPolygon(Transform parent, string name, List<Vector3> worldPts)
        {
            if (worldPts == null || worldPts.Count < 3) return null;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create road polygon");
            go.transform.SetParent(parent, false);
            var polygon = Undo.AddComponent<CesiumCartographicPolygon>(go);
            var container = go.GetComponent<SplineContainer>() ?? Undo.AddComponent<SplineContainer>(go);
            var spline = new Spline();
            foreach (var wp in worldPts)
            {
                Vector3 lp = go.transform.InverseTransformPoint(new Vector3(wp.x, 0f, wp.z));
                spline.Add(new BezierKnot(new float3(lp.x, lp.y, lp.z)), TangentMode.Linear);
            }
            spline.Closed = true; spline.SetTangentMode(TangentMode.Linear);
            container.Spline = spline;
            var anchor = go.GetComponent<CesiumGlobeAnchor>(); if (anchor != null) anchor.enabled = false;
            EditorUtility.SetDirty(go);
            return polygon;
        }

        private List<Vector3> LargestLoop(Mesh mesh, Transform tr)
        {
            var loops = BuildOutlineLoops(mesh, tr);
            return loops.Count == 0 ? null : loops.Aggregate((a, b) => PolygonArea(a) >= PolygonArea(b) ? a : b);
        }
        private List<List<Vector3>> BuildOutlineLoops(Mesh mesh, Transform tr)
        {
            var result = new List<List<Vector3>>();
            int[] t = mesh.triangles; Vector3[] mv = mesh.vertices;
            if (t == null || t.Length < 3) return result;
            var key2id = new Dictionary<long, int>(); var idPos = new List<Vector3>();
            int Q(Vector3 wp)
            {
                long kx = Mathf.RoundToInt(wp.x / vertexWeld), kz = Mathf.RoundToInt(wp.z / vertexWeld);
                long key = (kx << 32) ^ (kz & 0xffffffffL);
                if (!key2id.TryGetValue(key, out int id)) { id = idPos.Count; key2id[key] = id; idPos.Add(wp); }
                return id;
            }
            var ec = new Dictionary<long, int>(); var ed = new Dictionary<long, (int a, int b)>();
            long EK(int a, int b) { int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b); return ((long)lo << 32) | (uint)hi; }
            void AddEdge(int a, int b) { long k = EK(a, b); ec[k] = ec.TryGetValue(k, out int c) ? c + 1 : 1; if (!ed.ContainsKey(k)) ed[k] = (a, b); }
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = Q(tr.TransformPoint(mv[t[i]])), b = Q(tr.TransformPoint(mv[t[i + 1]])), c = Q(tr.TransformPoint(mv[t[i + 2]]));
                if (a == b || b == c || c == a) continue;
                AddEdge(a, b); AddEdge(b, c); AddEdge(c, a);
            }
            var adj = new Dictionary<int, List<int>>();
            foreach (var kv in ec)
            {
                if (kv.Value != 1) continue;
                var (a, b) = ed[kv.Key];
                if (!adj.TryGetValue(a, out var la)) adj[a] = la = new List<int>();
                if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new List<int>();
                la.Add(b); lb.Add(a);
            }
            var visited = new HashSet<int>();
            foreach (var start in adj.Keys)
            {
                if (visited.Contains(start)) continue;
                var loop = new List<int>(); int cur = start, prev = -1;
                while (cur != -1 && !visited.Contains(cur))
                {
                    visited.Add(cur); loop.Add(cur); int next = -1;
                    foreach (var nb in adj[cur]) if (nb != prev && !visited.Contains(nb)) { next = nb; break; }
                    prev = cur; cur = next;
                }
                if (loop.Count >= 3) result.Add(loop.Select(id => idPos[id]).ToList());
            }
            return result;
        }
        private static List<Vector3> OffsetLoopOutward(List<Vector3> loop, float d)
        {
            int n = loop.Count; if (n < 3 || d <= 0f) return loop;
            if (SignedArea(loop) < 0f) loop.Reverse();
            var outp = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = loop[(i - 1 + n) % n], cur = loop[i], next = loop[(i + 1) % n];
                Vector2 e1 = Norm(new Vector2(cur.x - prev.x, cur.z - prev.z)), e2 = Norm(new Vector2(next.x - cur.x, next.z - cur.z));
                Vector2 n1 = new(e1.y, -e1.x), n2 = new(e2.y, -e2.x);
                Vector2 bis = n1 + n2; float len = bis.magnitude; Vector2 dir; float miter;
                if (len < 1e-4f) { dir = n2; miter = 1f; } else { dir = bis / len; miter = Mathf.Min(3f, 1f / Mathf.Max(0.25f, Vector2.Dot(dir, n2))); }
                outp.Add(new Vector3(cur.x + dir.x * d * miter, cur.y, cur.z + dir.y * d * miter));
            }
            return outp;
        }
        private static List<Vector3> Simplify(List<Vector3> loop, float tol)
        {
            if (loop.Count <= 4) return loop;
            var keep = new bool[loop.Count]; keep[0] = keep[loop.Count - 1] = true;
            DP(loop, 0, loop.Count - 1, tol, keep);
            var outp = new List<Vector3>(); for (int i = 0; i < loop.Count; i++) if (keep[i]) outp.Add(loop[i]); return outp;
        }
        private static void DP(List<Vector3> p, int a, int b, float tol, bool[] keep)
        {
            if (b <= a + 1) return; float max = -1f; int idx = -1;
            for (int i = a + 1; i < b; i++) { float dd = DistPointSeg(p[i], p[a], p[b]); if (dd > max) { max = dd; idx = i; } }
            if (max > tol && idx > 0) { keep[idx] = true; DP(p, a, idx, tol, keep); DP(p, idx, b, tol, keep); }
        }
        private static float DistPointSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 P = new(p.x, p.z), A = new(a.x, a.z), B = new(b.x, b.z), AB = B - A;
            float l2 = AB.sqrMagnitude; if (l2 < 1e-9f) return (P - A).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(P - A, AB) / l2); return (P - (A + t * AB)).magnitude;
        }
        private static Vector2 Norm(Vector2 v) { float m = v.magnitude; return m < 1e-6f ? Vector2.zero : v / m; }
        private static Vector3 Centroid(List<Vector3> l) { var s = Vector3.zero; foreach (var p in l) s += p; return s / l.Count; }
        private static float SignedArea(List<Vector3> l) { float s = 0; int n = l.Count; for (int i = 0; i < n; i++) { var p = l[i]; var q = l[(i + 1) % n]; s += p.x * q.z - q.x * p.z; } return 0.5f * s; }
        private static float PolygonArea(List<Vector3> l) => Mathf.Abs(SignedArea(l));

        private void Clear()
        {
            var root = GameObject.Find(RootName);
            if (root != null) Undo.DestroyObjectImmediate(root);
            if (tileset != null)
                foreach (var ov in tileset.GetComponents<CesiumPolygonRasterOverlay>())
                    if (ov.materialKey == RoadCutoutKey) Undo.DestroyObjectImmediate(ov);
            status = "Cleared road RoadCutout overlay + polygons (Clipping kept).";
        }
    }
}
