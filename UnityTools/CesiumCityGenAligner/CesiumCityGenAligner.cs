// =============================================================================
//  CesiumCityGenAligner.cs   (v3 - hint-based ground selection)
//
//  Editor tool: aligns CityGen3D Landscape tiles vertically with the terrain
//  rendered by Cesium for Unity (Google Photorealistic 3D Tiles or any
//  other tileset).
//
//  PLACE IN:   Assets/Editor/CesiumCityGenAligner.cs
//  OPEN VIA:   Tools  >  CityGen3D x Cesium  >  Ground Aligner
//
//  WHY THIS EXISTS
//  ---------------
//  CesiumGeoreference's "Origin Height = 0" maps Unity Y=0 to the WGS84
//  ellipsoid surface - not the visible ground. The 3D Tiles render the
//  real terrain at roughly:
//
//      Y_unity  ~=  geoid_undulation  +  elevation_above_sea_level
//
//  e.g. in central London  N ~= +46 m  and elevation ~= +10..15 m, so the
//  visible Cesium ground sits at Y ~= +56..62 m in Unity world space.
//
//  Meanwhile CityGen3D Landscape tiles have no geodetic interpretation:
//  they place their road network at Transform.position.y directly. Leaving
//  them at Y=0 puts the road network ~58 m below the visible city.
//
//  WHY HINT-BASED SELECTION (v3)
//  -----------------------------
//  v1 (single ray, "first hit"): in dense cities the ray almost always
//        hits a rooftop -> reports Y ~= rooftop height (e.g. 290 m).
//
//  v2 (grid, "min Y"): tried to avoid rooftops by taking the lowest hit
//        across a grid, BUT Google Photorealistic 3D Tiles are watertight
//        meshes whose buildings extend below the visible ground as a
//        virtual "foundation" (to keep the mesh closed off underneath).
//        Result: lowest hit is a building's *underground* mesh, often at
//        Y < 0. CityGen3D ends up below the visible ground.
//
//  v3 (grid + hint): cast a grid, collect ALL hits, then pick the hit
//        whose Y is closest to a user-supplied "expected ground Y" hint.
//        Both rooftops (far above) and underground extensions (far below)
//        are automatically rejected. The user sees the full sorted hit
//        list in the window and refines the hint or switches to manual.
//
//  Author: Yike Zhang, COMP0190 P87 - Sustainable Personalized Driving
//          UCL Computer Science, supervised by Dr Mark Colley
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using CesiumForUnity;

namespace SustainableDriving.SimulationTools.EditorTools
{
    public class CesiumCityGenAligner : EditorWindow
    {
        // ---- Serialised inputs ---------------------------------------------
        [SerializeField] private CesiumGeoreference georeference;
        [SerializeField] private double sampleLongitude   = -0.0939;
        [SerializeField] private double sampleLatitude    = 51.51889;
        [SerializeField] private float  probeStartHeightAboveEllipsoid = 1500f;
        [SerializeField] private int    gridResolution    = 7;     // 7x7 = 49 samples
        [SerializeField] private float  gridRadiusMetres  = 200f;
        [SerializeField] private float  expectedGroundYHint   = 58f;
        [SerializeField] private bool   manualOverride        = false;
        [SerializeField] private float  manualGroundY         = 58f;
        [SerializeField] private float  yBiasToAvoidZFighting = 0.05f;
        [SerializeField] private string landscapeNamePrefix   = "Landscape (";

        // ---- Runtime state -------------------------------------------------
        private readonly List<Transform> landscapeTiles = new();
        private List<float> lastAllHitYs = new();
        private string status = "Idle. Run actions in order: 1 -> 2 -> 3.";
        private Vector2 scroll;

        // ===================================================================
        //  Menu entry
        // ===================================================================
        [MenuItem("Tools/CityGen3D x Cesium/Ground Aligner")]
        public static void Open()
        {
            var w = GetWindow<CesiumCityGenAligner>("Ground Aligner");
            w.minSize = new Vector2(480, 720);
        }

        private void OnEnable()
        {
            if (georeference == null)
                georeference = FindFirstObjectByType<CesiumGeoreference>();
        }

        // ===================================================================
        //  GUI
        // ===================================================================
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("CityGen3D x Cesium - Ground Aligner (v3)",
                                       EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Fires a grid of downward raycasts around the chosen lat/lon, " +
                "then picks the hit whose Y is closest to your expected " +
                "ground-Y hint. Avoids both rooftops (too high) and Google " +
                "3D-Tile underground extensions (too low).",
                MessageType.None);

            // -- 1. References --
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("1.  References", EditorStyles.boldLabel);
            georeference = (CesiumGeoreference)EditorGUILayout.ObjectField(
                "Cesium Georeference", georeference, typeof(CesiumGeoreference), true);
            landscapeNamePrefix = EditorGUILayout.TextField(
                "Landscape name prefix", landscapeNamePrefix);

            // -- 2. Probe centre --
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2.  Probe centre (WGS84)", EditorStyles.boldLabel);
            sampleLongitude = EditorGUILayout.DoubleField("Longitude (deg)", sampleLongitude);
            sampleLatitude  = EditorGUILayout.DoubleField("Latitude (deg)",  sampleLatitude);
            probeStartHeightAboveEllipsoid = EditorGUILayout.FloatField(
                "Probe start height (m above ellipsoid)", probeStartHeightAboveEllipsoid);
            if (GUILayout.Button("Use Cesium Georeference origin as probe centre",
                                  GUILayout.Height(20)))
            {
                if (georeference != null)
                {
                    sampleLongitude = georeference.longitude;
                    sampleLatitude  = georeference.latitude;
                    status = "Probe lat/lon copied from CesiumGeoreference origin.";
                }
            }

            // -- 3. Grid sampling --
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("3.  Grid sampling", EditorStyles.boldLabel);
            gridResolution   = Mathf.Clamp(
                EditorGUILayout.IntField("Grid resolution (N x N)", gridResolution), 1, 21);
            gridRadiusMetres = Mathf.Max(
                EditorGUILayout.FloatField("Grid half-width (m)", gridRadiusMetres), 0f);
            EditorGUILayout.LabelField(
                $"     will cast {gridResolution * gridResolution} rays " +
                $"over a {gridRadiusMetres * 2:F0} m x {gridRadiusMetres * 2:F0} m patch.");

            // -- 4. Ground selection --
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("4.  Ground selection", EditorStyles.boldLabel);
            expectedGroundYHint = EditorGUILayout.FloatField(
                "Expected ground Y (hint, m)", expectedGroundYHint);
            EditorGUILayout.HelpBox(
                "Tool picks the hit closest to this value. London ~= 58 m. " +
                "Use the hit list below to refine.",
                MessageType.None);

            manualOverride = EditorGUILayout.Toggle("Manual override", manualOverride);
            using (new EditorGUI.DisabledScope(!manualOverride))
            {
                manualGroundY = EditorGUILayout.FloatField(
                    "Manual ground Y (m)", manualGroundY);
            }

            // -- 5. Snap options --
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("5.  Snap options", EditorStyles.boldLabel);
            yBiasToAvoidZFighting = EditorGUILayout.FloatField(
                "Y bias above ground (m)", yBiasToAvoidZFighting);

            // -- Actions --
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            if (GUILayout.Button("1.  Detect Landscape tiles"))
                DetectLandscapes();
            EditorGUILayout.LabelField($"     Detected: {landscapeTiles.Count} tile(s).");

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(georeference == null))
            {
                if (GUILayout.Button("2.  Probe Cesium ground Y  (grid raycast)"))
                    ProbeGroundY();
            }

            float selectedY = ComputeSelectedGroundY();
            if (!float.IsNaN(selectedY))
            {
                string source = manualOverride ? "manual" :
                    $"closest to hint {expectedGroundYHint:F1}";
                EditorGUILayout.LabelField(
                    $"     -> Selected ground Y = {selectedY:F4} m   ({source})",
                    EditorStyles.boldLabel);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(
                       float.IsNaN(selectedY) || landscapeTiles.Count == 0))
            {
                if (GUILayout.Button("3.  Snap Landscape tiles to selected Y"))
                    SnapLandscapes();
            }

            // -- Hit distribution --
            if (lastAllHitYs.Count > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Hit-Y distribution (sorted)",
                                           EditorStyles.boldLabel);
                DrawHitDistribution(selectedY);
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.HelpBox(status, MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        // ===================================================================
        //  Ground Y selection (hint-based or manual)
        // ===================================================================
        private float ComputeSelectedGroundY()
        {
            if (manualOverride) return manualGroundY;
            if (lastAllHitYs == null || lastAllHitYs.Count == 0) return float.NaN;
            float hint = expectedGroundYHint;
            return lastAllHitYs.OrderBy(y => Math.Abs(y - hint)).First();
        }

        // ===================================================================
        //  Hit distribution drawer (sorted list + markers)
        // ===================================================================
        private void DrawHitDistribution(float selectedY)
        {
            var sorted = lastAllHitYs.OrderBy(y => y).ToList();
            float minY = sorted.First();
            float maxY = sorted.Last();
            float medY = sorted[sorted.Count / 2];
            EditorGUILayout.LabelField(
                $"   total {sorted.Count}   |   " +
                $"min {minY:F1}   median {medY:F1}   max {maxY:F1}");
            EditorGUILayout.Space(2);

            int show = Mathf.Min(sorted.Count, 30);
            for (int i = 0; i < show; i++)
            {
                float y = sorted[i];
                string tag = "";
                if (!float.IsNaN(selectedY) && Math.Abs(y - selectedY) < 1e-3f)
                    tag = "   <- selected";
                else if (y < 0)   tag = "   (underground - likely 3D-Tile foundation)";
                else if (y > 150) tag = "   (high - likely rooftop)";
                EditorGUILayout.LabelField($"     {y,9:F2} m{tag}");
            }
            if (sorted.Count > show)
                EditorGUILayout.LabelField($"     ... and {sorted.Count - show} more");
        }

        // ===================================================================
        //  1.  Detect Landscape tiles
        // ===================================================================
        private void DetectLandscapes()
        {
            landscapeTiles.Clear();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                CollectLandscapesRecursive(root.transform);

            status = landscapeTiles.Count > 0
                ? $"Detected {landscapeTiles.Count} Landscape tile(s)."
                : $"No Landscape tiles found. Looking for objects whose name " +
                  $"starts with '{landscapeNamePrefix}'.";
        }

        private void CollectLandscapesRecursive(Transform t)
        {
            if (t.name.StartsWith(landscapeNamePrefix))
                landscapeTiles.Add(t);
            for (int i = 0; i < t.childCount; i++)
                CollectLandscapesRecursive(t.GetChild(i));
        }

        // ===================================================================
        //  2.  Probe ground Y via N x N grid of raycasts
        // ===================================================================
        private void ProbeGroundY()
        {
            if (georeference == null)
            {
                status = "ERROR: assign a CesiumGeoreference first.";
                return;
            }

            GameObject probe = new GameObject("__CesiumGroundProbe_TEMP")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            probe.transform.SetParent(georeference.transform, worldPositionStays: false);
            var anchor = probe.AddComponent<CesiumGlobeAnchor>();

            // metres -> degrees (small-area planar approximation)
            double mPerDegLat = 111320.0;
            double mPerDegLon = 111320.0 *
                                Math.Cos(sampleLatitude * Math.PI / 180.0);
            double degRadiusLat = gridRadiusMetres / mPerDegLat;
            double degRadiusLon = gridRadiusMetres / mPerDegLon;

            lastAllHitYs.Clear();
            int totalSamples = gridResolution * gridResolution;
            float maxRayDist = probeStartHeightAboveEllipsoid * 2f + 500f;

            for (int i = 0; i < gridResolution; i++)
            {
                for (int j = 0; j < gridResolution; j++)
                {
                    double u = gridResolution == 1 ? 0.0
                             : (2.0 * i / (gridResolution - 1)) - 1.0;
                    double v = gridResolution == 1 ? 0.0
                             : (2.0 * j / (gridResolution - 1)) - 1.0;

                    double lon = sampleLongitude + u * degRadiusLon;
                    double lat = sampleLatitude  + v * degRadiusLat;

                    anchor.longitudeLatitudeHeight = new double3(
                        lon, lat, probeStartHeightAboveEllipsoid);
                    anchor.Sync();

                    Vector3 origin = probe.transform.position;
                    if (Physics.Raycast(origin, Vector3.down,
                                        out RaycastHit info, maxRayDist,
                                        ~0, QueryTriggerInteraction.Ignore))
                    {
                        lastAllHitYs.Add(info.point.y);
                    }
                }
            }

            DestroyImmediate(probe);

            if (lastAllHitYs.Count == 0)
            {
                status =
                    "Grid raycast returned no hits.\n" +
                    " - Cesium 3D Tiles for this lat/lon may not be loaded yet. " +
                    "Move the Scene-view camera near the target so the tileset " +
                    "streams in, then probe again.\n" +
                    " - Check 'Create Physics Meshes' is enabled on Cesium3DTileset.\n" +
                    " - Try a larger Probe start height (e.g. 2000 m).";
                return;
            }

            float chosen = ComputeSelectedGroundY();
            status =
                $"Grid probe OK: {lastAllHitYs.Count}/{totalSamples} rays hit.\n" +
                $"Selected ground Y = {chosen:F4} m " +
                (manualOverride
                    ? "(manual override)."
                    : $"(closest to hint {expectedGroundYHint:F1}).\n" +
                      "Inspect the sorted hit list below. If selection looks " +
                      "off, adjust the hint or enable Manual override.");
        }

        // ===================================================================
        //  3.  Snap Landscape tiles to selected Y
        // ===================================================================
        private void SnapLandscapes()
        {
            float selectedY = ComputeSelectedGroundY();
            if (float.IsNaN(selectedY) || landscapeTiles.Count == 0)
                return;

            float targetY = selectedY + yBiasToAvoidZFighting;

            Undo.RecordObjects(
                landscapeTiles.Cast<UnityEngine.Object>().ToArray(),
                "Align Landscape tiles to Cesium ground");

            foreach (var t in landscapeTiles)
            {
                var p = t.position;
                p.y   = targetY;
                t.position = p;
                EditorUtility.SetDirty(t);
            }

            status =
                $"Snapped {landscapeTiles.Count} Landscape tile(s) to Y = " +
                $"{targetY:F4} m  (selected {selectedY:F4} + bias " +
                $"{yBiasToAvoidZFighting:F4}).";
        }
    }
}