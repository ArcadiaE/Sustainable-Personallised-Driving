// -----------------------------------------------------------------------------
//  CesiumRigidAlign.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  Rigid vertical alignment between the Cesium tileset and the CityGen3D world.
//  Measures the median (Cesium - terrain) height gap on the road corridors and
//  compensates it through CesiumGeoreference.height. The terrain heightmap is
//  never touched - that is the whole point of this tool replacing the conform
//  pipeline. Local residuals (a metre here and there) are accepted and hidden
//  by the road-corridor clipping, not chased.
//
//  Workflow: MEASURE -> APPLY -> wait for tiles to settle -> MEASURE again
//  (expect |median| well under 0.5 m) -> done. REVERT restores the original height.
// -----------------------------------------------------------------------------
//  OPEN VIA: Tools > CityGen3D x Cesium > Rigid Align
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CesiumForUnity;
using UnityEditor;
using UnityEngine;

namespace SustainableDriving.SimulationTools.EditorTools
{
    public sealed class CesiumRigidAlign : EditorWindow
    {
        [SerializeField] private Cesium3DTileset tileset;
        [SerializeField] private CesiumGeoreference georeference;
        [SerializeField] private Transform mapRoads;
        [SerializeField] private string landscapeNamePrefix = "Landscape (";

        [SerializeField] private int sampleTarget = 4000;     // road points to test
        [SerializeField] private float rayStartWorldY = 2000f;
        [SerializeField] private float rayLength = 6000f;
        [SerializeField] private float groundNormalYMin = 0.30f;

        private readonly List<Terrain> terrains = new();
        private double originalHeight = double.NaN;          // for REVERT
        private float lastMedian = float.NaN;
        private string status = "1) Detect  2) MEASURE  3) APPLY  4) MEASURE again to verify.";

        [MenuItem("Tools/CityGen3D x Cesium/Rigid Align")]
        public static void Open() => GetWindow<CesiumRigidAlign>("Rigid Align");

        private void OnEnable() => Detect();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Cesium x CityGen3D - Rigid Vertical Align", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Moves the WHOLE Cesium world up/down via Georeference height. " +
                "Never writes to any terrain. Make sure Cesium physics meshes are on " +
                "and tiles over the area are fully loaded before measuring.",
                MessageType.None);

            tileset = (Cesium3DTileset)EditorGUILayout.ObjectField("Cesium3DTileset", tileset, typeof(Cesium3DTileset), true);
            georeference = (CesiumGeoreference)EditorGUILayout.ObjectField("CesiumGeoreference", georeference, typeof(CesiumGeoreference), true);
            mapRoads = (Transform)EditorGUILayout.ObjectField("MapRoads", mapRoads, typeof(Transform), true);
            landscapeNamePrefix = EditorGUILayout.TextField("Landscape prefix", landscapeNamePrefix);
            sampleTarget = Mathf.Clamp(EditorGUILayout.IntField("Road samples", sampleTarget), 200, 20000);

            if (GUILayout.Button("Detect references")) Detect();
            EditorGUILayout.LabelField($"     Terrains: {terrains.Count}");

            EditorGUILayout.Space(8);
            if (GUILayout.Button("MEASURE (read-only)", GUILayout.Height(28))) Measure();

            using (new EditorGUI.DisabledScope(float.IsNaN(lastMedian) || georeference == null))
            {
                if (GUILayout.Button($"APPLY: shift Cesium by {(float.IsNaN(lastMedian) ? 0f : -lastMedian):F2} m", GUILayout.Height(28)))
                    Apply();
            }
            using (new EditorGUI.DisabledScope(double.IsNaN(originalHeight) || georeference == null))
            {
                if (GUILayout.Button("REVERT to original height")) Revert();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        private void Detect()
        {
            if (tileset == null) tileset = FindFirstObjectByType<Cesium3DTileset>();
            if (georeference == null) georeference = FindFirstObjectByType<CesiumGeoreference>();
            if (mapRoads == null)
                mapRoads = FindObjectsByType<Transform>(FindObjectsSortMode.None)
                    .FirstOrDefault(t => t.name == "MapRoads");

            terrains.Clear();
            terrains.AddRange(FindObjectsByType<Terrain>(FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith(landscapeNamePrefix, StringComparison.Ordinal)));

            status = $"tileset {(tileset ? "OK" : "MISSING")}; georeference {(georeference ? "OK" : "MISSING")}; " +
                     $"MapRoads {(mapRoads ? "OK" : "MISSING")}; terrains {terrains.Count}.";
        }

        // Median (Cesium - terrain) sampled at road-vertex XZ positions.
        // Only XZ is taken from the road meshes, so it works no matter what
        // height the road preview layer currently sits at.
        private void Measure()
        {
            if (tileset == null || mapRoads == null || terrains.Count == 0)
            {
                status = "Missing references - run Detect first.";
                return;
            }

            var deltas = new List<float>(sampleTarget);
            int cesiumMiss = 0, terrainMiss = 0, tested = 0;

            var filters = mapRoads.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null).ToArray();
            int totalVerts = filters.Sum(f => f.sharedMesh.vertexCount);
            if (totalVerts == 0) { status = "MapRoads has no mesh vertices."; return; }
            int stride = Mathf.Max(1, totalVerts / sampleTarget);

            try
            {
                int seen = 0;
                for (int fi = 0; fi < filters.Length; fi++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Rigid Align", "Sampling road corridor...",
                            (float)fi / filters.Length)) { status = "Measure cancelled."; return; }

                    Transform tr = filters[fi].transform;
                    Vector3[] verts = filters[fi].sharedMesh.vertices;
                    for (int i = 0; i < verts.Length; i++, seen++)
                    {
                        if (seen % stride != 0) continue;
                        Vector3 w = tr.TransformPoint(verts[i]);
                        tested++;

                        bool hasC = SampleDown(w, hit => IsTilesetHit(hit), out float cy);
                        bool hasT = SampleDown(w, hit => IsTerrainHit(hit), out float ty);
                        if (!hasC) { cesiumMiss++; continue; }
                        if (!hasT) { terrainMiss++; continue; }
                        deltas.Add(cy - ty);
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (deltas.Count < 50)
            {
                status = $"Too few valid samples ({deltas.Count}). Cesium misses {cesiumMiss}, terrain misses {terrainMiss}. " +
                         "Are Cesium physics meshes enabled and tiles loaded?";
                return;
            }

            lastMedian = Median(deltas);
            status = $"Median (Cesium - terrain) on roads: {lastMedian:F3} m " +
                     $"(n={deltas.Count}, tested {tested}, Cesium miss {cesiumMiss}, terrain miss {terrainMiss}).\n" +
                     "APPLY will shift Cesium by the opposite amount. Target after apply: |median| < 0.5 m.";
            Debug.Log("[RigidAlign] " + status);
        }

        private void Apply()
        {
            if (georeference == null || float.IsNaN(lastMedian)) return;
            if (double.IsNaN(originalHeight)) originalHeight = georeference.height;

            Undo.RecordObject(georeference, "Rigid align Cesium height");
            // Raising the georeference origin height lowers Cesium content in Unity
            // space by the same amount, and vice versa.
            georeference.height += lastMedian;
            EditorUtility.SetDirty(georeference);

            status = $"Shifted: georeference height {originalHeight:F3} -> {georeference.height:F3} " +
                     $"(moved Cesium by {-lastMedian:F2} m in Unity Y).\n" +
                     "Wait for tiles/physics to settle (a few seconds), then MEASURE again to verify.";
            Debug.Log("[RigidAlign] " + status);
            lastMedian = float.NaN;
        }

        private void Revert()
        {
            if (georeference == null || double.IsNaN(originalHeight)) return;
            Undo.RecordObject(georeference, "Revert rigid align");
            georeference.height = originalHeight;
            EditorUtility.SetDirty(georeference);
            status = $"Reverted georeference height to {originalHeight:F3}.";
            Debug.Log("[RigidAlign] " + status);
            originalHeight = double.NaN;
            lastMedian = float.NaN;
        }

        // ---- sampling helpers ----
        private bool SampleDown(Vector3 at, Func<RaycastHit, bool> accept, out float y)
        {
            y = float.NaN;
            var hits = Physics.RaycastAll(new Vector3(at.x, rayStartWorldY, at.z),
                Vector3.down, rayLength, ~0, QueryTriggerInteraction.Ignore);
            bool found = false; float best = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (!accept(hit)) continue;
                if (hit.point.y > best) { best = hit.point.y; found = true; }
            }
            if (found) y = best;
            return found;
        }

        private bool IsTilesetHit(RaycastHit hit)
        {
            if (hit.transform == null || tileset == null) return false;
            if (!(hit.transform == tileset.transform || hit.transform.IsChildOf(tileset.transform))) return false;
            return hit.normal.y >= groundNormalYMin; // walls out, ground/roofs in; median copes with sparse roofs over roads
        }

        private bool IsTerrainHit(RaycastHit hit)
        {
            if (!(hit.collider is TerrainCollider)) return false;
            var t = hit.collider.GetComponent<Terrain>();
            return t != null && terrains.Contains(t);
        }

        private static float Median(List<float> v)
        {
            var s = v.OrderBy(a => a).ToList();
            int m = s.Count / 2;
            return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) * 0.5f;
        }
    }
}
