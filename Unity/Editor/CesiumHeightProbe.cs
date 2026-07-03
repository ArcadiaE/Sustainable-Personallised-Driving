// =============================================================================
//  CesiumHeightProbe.cs   (class CesiumHeightProbe)
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  FEASIBILITY PROBE for "Direction A": derive building outlines from the Cesium
//  photogrammetry's own surface heights (so they match the real outline exactly,
//  shape-adaptive, and generalise to any location - no region constants).
//
//  This script ONLY samples and reports. It does NOT cut, build geometry, or change
//  Cesium / the scene in any way. It samples a coarse grid of surface heights with
//  Cesium's `SampleHeightMostDetailed` (which loads the most-detailed tiles on demand,
//  independent of what is currently rendered) and prints the height distribution, so
//  we can confirm that "tall = building / low = road+ground" actually separates here
//  before committing to the full approach.
//
//  Coordinate chain: Unity XZ -> ECEF (georeference) -> lon/lat (WGS84 ellipsoid)
//  -> SampleHeightMostDetailed -> sampled surface height (m above ellipsoid).
//  Async result is polled on EditorApplication.update so the tileset can load.
//
//  OPEN VIA: Tools > CityGen3D x Cesium > Cesium Height Probe
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SustainableDriving.SimulationTools.EditorTools
{
    public sealed class CesiumHeightProbe : EditorWindow
    {
        [SerializeField] private Cesium3DTileset tileset;
        [SerializeField] private CesiumGeoreference georeference;
        [SerializeField] private string landscapePrefix = "Landscape (";
        [SerializeField] private int gridN = 64;            // gridN x gridN sample points over the study area (keep modest for a probe)
        [SerializeField] private float sampleUnityY = 0f;   // Unity Y used to build the sample positions (ignored by sampling; only XZ matters)

        private Vector2 areaMin = new(-256, -256), areaMax = new(1280, 1280);
        private Task<CesiumSampleHeightResult> _task;
        private int _requested;
        private string status = "Detect -> Run probe. Samples a grid of Cesium surface heights and prints the distribution. No cutting, no geometry.";

        [MenuItem("Tools/CityGen3D x Cesium/Cesium Height Probe")]
        public static void Open() => GetWindow<CesiumHeightProbe>("Cesium Height Probe");

        private void OnEnable() => Detect();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Cesium Height Probe (Direction A feasibility)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Samples a coarse grid of Cesium surface heights (SampleHeightMostDetailed) and " +
                "prints the height distribution to the Console, so we can see whether buildings " +
                "(tall) separate cleanly from road/ground (low). Read-only: it does NOT cut or " +
                "create anything. Sampling loads detailed tiles on demand and may take a moment.",
                MessageType.None);

            tileset = (Cesium3DTileset)EditorGUILayout.ObjectField("Cesium3DTileset", tileset, typeof(Cesium3DTileset), true);
            georeference = (CesiumGeoreference)EditorGUILayout.ObjectField("CesiumGeoreference", georeference, typeof(CesiumGeoreference), true);
            gridN = Mathf.Clamp(EditorGUILayout.IntField("Grid N (NxN points)", gridN), 4, 256);
            EditorGUILayout.LabelField($"-> {gridN * gridN} sample points over [{areaMin.x:F0},{areaMin.y:F0}]..[{areaMax.x:F0},{areaMax.y:F0}]");

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Detect")) Detect();
            using (new EditorGUI.DisabledScope(_task != null))
                if (GUILayout.Button("Run probe sample", GUILayout.Height(28))) RunProbe();

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        private void Detect()
        {
            if (tileset == null) tileset = FindFirstObjectByType<Cesium3DTileset>();
            if (georeference == null && tileset != null) georeference = tileset.GetComponentInParent<CesiumGeoreference>();
            if (georeference == null) georeference = FindFirstObjectByType<CesiumGeoreference>();
            ComputeStudyArea();
            status = $"tileset {(tileset ? "OK" : "MISSING")}; georeference {(georeference ? "OK" : "MISSING")}; " +
                     $"area [{areaMin.x:F0},{areaMin.y:F0}]..[{areaMax.x:F0},{areaMax.y:F0}].";
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

        private void RunProbe()
        {
            Detect();
            if (tileset == null || georeference == null) { status = "Need tileset + georeference."; return; }

            // Build lon/lat sample positions from a Unity XZ grid.
            var lonLatH = new double3[gridN * gridN];
            int k = 0;
            for (int j = 0; j < gridN; j++)
                for (int i = 0; i < gridN; i++)
                {
                    float x = Mathf.Lerp(areaMin.x, areaMax.x, (i + 0.5f) / gridN);
                    float z = Mathf.Lerp(areaMin.y, areaMax.y, (j + 0.5f) / gridN);
                    double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(new double3(x, sampleUnityY, z));
                    double3 llh = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
                    lonLatH[k++] = new double3(llh.x, llh.y, 0.0);
                }

            _requested = lonLatH.Length;
            status = $"Sampling {_requested} points... (loading detailed tiles, please wait)";
            Debug.Log($"[CesiumHeightProbe] requesting {_requested} samples over study area...");
            try
            {
                _task = tileset.SampleHeightMostDetailed(lonLatH);
                EditorApplication.update += Poll;
            }
            catch (Exception e)
            {
                status = "SampleHeightMostDetailed threw: " + e.Message;
                _task = null;
            }
            Repaint();
        }

        private void Poll()
        {
            if (_task == null) { EditorApplication.update -= Poll; return; }
            if (!_task.IsCompleted) return;
            EditorApplication.update -= Poll;

            if (_task.IsFaulted)
            {
                status = "Sampling failed: " + (_task.Exception?.GetBaseException().Message ?? "unknown");
                Debug.LogError("[CesiumHeightProbe] " + status);
                _task = null; Repaint(); return;
            }

            var res = _task.Result;
            _task = null;
            Analyze(res);
            Repaint();
        }

        private void Analyze(CesiumSampleHeightResult res)
        {
            var pos = res.longitudeLatitudeHeightPositions;
            var ok = res.sampleSuccess;
            var heights = new List<double>();
            int success = 0;
            for (int i = 0; i < pos.Length; i++)
            {
                if (ok != null && i < ok.Length && ok[i]) { success++; heights.Add(pos[i].z); }
            }
            if (heights.Count == 0)
            {
                status = $"0 / {_requested} samples succeeded. Tileset may not be loaded/visible, or positions are off the tileset.";
                Debug.LogWarning("[CesiumHeightProbe] " + status);
                return;
            }

            heights.Sort();
            double min = heights[0], max = heights[heights.Count - 1];
            double Pct(double p) => heights[Mathf.Clamp((int)(p * (heights.Count - 1)), 0, heights.Count - 1)];
            double p05 = Pct(0.05), p50 = Pct(0.50), p95 = Pct(0.95);
            double span = max - min;

            // 12-bin histogram of (height - min)
            int bins = 12;
            var hist = new int[bins];
            foreach (var h in heights)
            {
                int b = span < 1e-6 ? 0 : Mathf.Clamp((int)((h - min) / span * bins), 0, bins - 1);
                hist[b]++;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[CesiumHeightProbe] {success}/{_requested} samples ok. height span = {span:F1} m");
            sb.AppendLine($"  min {min:F1}  p05 {p05:F1}  median {p50:F1}  p95 {p95:F1}  max {max:F1}");
            sb.AppendLine($"  ground baseline ~ p05 = {p05:F1};  >baseline+3m (candidate building) = {heights.Count(h => h > p05 + 3.0)} pts ({100.0 * heights.Count(h => h > p05 + 3.0) / heights.Count:F0}%)");
            for (int b = 0; b < bins; b++)
            {
                double lo = min + span * b / bins, hi = min + span * (b + 1) / bins;
                int n = hist[b];
                string bar = new string('#', Mathf.Clamp(n * 60 / Mathf.Max(1, heights.Count), 0, 60));
                sb.AppendLine($"  [{lo,7:F1}..{hi,7:F1}] {n,5}  {bar}");
            }
            Debug.Log(sb.ToString());

            // crude verdict: separable if there is a clear low cluster (ground) plus a meaningful tail above it
            double aboveFrac = (double)heights.Count(h => h > p05 + 3.0) / heights.Count;
            bool separable = span > 6.0 && aboveFrac > 0.05 && aboveFrac < 0.95;
            status = $"{success}/{_requested} ok. span {span:F1}m, median {p50:F1}, {aboveFrac * 100:F0}% above ground+3m. " +
                     (separable ? "Looks SEPARABLE (ground vs building). Direction A viable." : "Separation UNCLEAR - see Console histogram (maybe area not loaded, or too coarse).");
        }
    }
}
