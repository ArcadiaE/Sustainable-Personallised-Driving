using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class RunFlag
{
    const string CarPackage = @"F:\10375\Realistic Mobile Car 05.unitypackage";
    static string TrafficNet(string file) => Path.Combine(Application.streamingAssetsPath, "TrafficNet", file);
    static string RoutesJson => TrafficNet("final_routes.json");
    const int TreeLayer = 19;          // CityGen "Map Tree"
    const float ScanRadius = 8f;
    const float ClearRadius = 12f;

    static double _nextPoll;

    static RunFlag()
    {
        EditorApplication.delayCall += Check;
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 2.0;
            Check();
        };
    }

    static string ResultPath => Path.GetFullPath(Application.dataPath + "/../flag_result.txt");

    static void WriteResult(string text)
    {
        Debug.Log("[RunFlag] " + text);
        File.WriteAllText(ResultPath, System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + text);
    }

    static void Check()
    {
        string path = Path.GetFullPath(Application.dataPath + "/../run_flag.txt");
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToArray();
        File.Delete(path);
        if (lines.Length == 0) return;
        string action = lines[0].Trim().ToLowerInvariant();
        string arg = lines.Length > 1 ? lines[1].Trim() : "";
        bool playSafe = action == "stopplay" || action == "bostate" || action == "unpause" || action == "mirrorinfo" || action == "mirrorcam" || action == "mirrorglass" || action == "mirrormesh" || action == "autodrive" || action == "carstate" || action == "inputdevices" || action == "axesprobe" || action == "hudshot";
        if (EditorApplication.isPlayingOrWillChangePlaymode && !playSafe)
        {
            WriteResult($"SKIPPED '{action}': editor is in Play mode.");
            return;
        }
        Debug.Log("[RunFlag] Executing action: " + action);
        try
        {
            switch (action)
            {
#if GLEY_TRAFFIC_SYSTEM
                case "analyze":
                    SustainableDriving.SimulationTools.EditorTools.GleyNetSetup.Analyze();
                    break;
                case "build":
                    SustainableDriving.SimulationTools.EditorTools.GleyNetSetup.Build();
                    break;
#endif
                case "carinfo": CarInfo(); break;
                case "swapcar": SwapCar(); break;
                case "routescan": RouteScan(); break;
                case "cleartrees": ClearTrees(long.Parse(arg)); break;
                case "pinroute": PinRoute(long.Parse(arg)); break;
                case "importcar": ImportCar(); break;
                case "fixcarmat": FixCarMats(); break;
                case "missing": MissingScan(); break;
                case "cleanmiss": CleanMissing(); break;
                case "audit": Audit(); break;
                case "cleanup": Cleanup(); break;
                case "rebuildsurvey": RebuildSurvey(); break;
                case "density": SetDensity(int.Parse(arg)); break;
                case "trafficlights": BuildTrafficLights(arg == "" ? 4 : int.Parse(arg)); break;
                case "fixlightpoles": FixLightPoles(); break;
                case "fixplayer": FixTrafficPlayer(); break;
                case "carlayer": CarLayer(); break;
                case "boxprobe": BoxProbe(arg); break;
                case "fixwallmask": FixWallMask(); break;
                case "pedestrians": BuildPedestrians(arg == "" ? 40 : int.Parse(arg)); break;
                case "probehit": ProbeHit(); break;
                case "fixcam": FixCam(arg); break;
                case "bostate": BoState(); break;
                case "unpause": Unpause(); break;
                case "mirrorinfo": MirrorInfo(); break;
                case "mirrorcam": MirrorCam(arg); break;
                case "mirrorglass": MirrorGlass(arg); break;
                case "mirrormesh": MirrorMesh(arg); break;
                case "autodrive": AutoDrive(arg); break;
                case "carstate": CarState(); break;
                case "routeshots": RouteShots(); break;
                case "whatis": WhatIs(arg); break;
                case "drawroutes": DrawRoutes(arg); break;
                case "cleanscene": CleanScene(); break;
                case "drawfinal": DrawFinal(); break;
                case "farscan": FarScan(); break;
                case "whitescan": WhiteScan(); break;
                case "mirrorscan": MirrorScan(); break;
                case "usefinal": UseFinalRoutes(); break;
                case "lighttimes": SetLightTimes(arg); break;
                case "lightphases": PairLightPhases(); break;
                case "lightheads": RebuildLightHeads(); break;
                case "routetimes": SetRouteTimes(arg); break;
                case "fixonephase": FixOnePhase(); break;
                case "armprobe": ArmProbe(); break;
                case "inputdevices": InputDevices(); break;
                case "axesprobe": AxesProbe(arg); break;
                case "fixwheelcfg": FixWheelConfig(); break;
                case "play": EditorApplication.EnterPlaymode(); WriteResult("play: entering Play mode."); break;
                case "stopplay": EditorApplication.ExitPlaymode(); WriteResult("stopplay: exiting Play mode."); break;
                case "refresh": AssetDatabase.Refresh(); WriteResult("refresh: AssetDatabase.Refresh issued."); break;
                case "hudshot": HudShot(arg); break;
                default:
                    WriteResult($"Unknown action '{action}'.");
                    break;
            }
        }
        catch (System.Exception e)
        {
            WriteResult($"ACTION '{action}' FAILED: " + e);
        }
    }

    // run_flag arg: "leaf,score,feedback,speed,accel,labels,opacity[,name]"
    static void HudShot(string arg)
    {
        if (!EditorApplication.isPlaying) { WriteResult("hudshot: needs Play mode."); return; }
        var hud = Object.FindFirstObjectByType<EcoFeedbackHUD>();
        if (hud == null) { WriteResult("hudshot: no EcoFeedbackHUD in scene."); return; }
        var parts = arg.Split(',');
        if (parts.Length < 7) { WriteResult("hudshot: need 7 comma-separated values."); return; }
        var v = new float[7];
        for (int i = 0; i < 7; i++)
            v[i] = float.Parse(parts[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        hud.ApplyDesignParams(v[0], v[1], v[2], v[3], v[4], v[5], v[6]);
        string name = parts.Length > 7 ? parts[7].Trim() : "probe";
        string path = System.IO.Path.GetFullPath(Application.dataPath + "/../RouteShots/hud_" + name + ".png");
        ShotBot.SnapHud(path);
        WriteResult($"hudshot: applied [{string.Join(", ", v)}] -> {path} (written ~0.6s later). " +
                    $"pOpacity now {hud.pOpacity:F2}, group={(hud.group != null ? "wired alpha=" + hud.group.alpha.ToString("F2") : "NULL")}");
    }

    static void DumpTransform(Transform t, string indent, System.Text.StringBuilder sb, int maxDepth)
    {
        var comps = t.GetComponents<Component>()
            .Where(c => c != null && !(c is Transform))
            .Select(c => c.GetType().Name);
        sb.AppendLine($"{indent}{t.name}  lp={t.localPosition}  ls={t.localScale}  [{string.Join(",", comps)}]" +
                      (t.gameObject.activeSelf ? "" : "  (INACTIVE)"));
        if (maxDepth <= 0) { if (t.childCount > 0) sb.AppendLine(indent + "  ..."); return; }
        foreach (Transform c in t) DumpTransform(c, indent + "  ", sb, maxDepth - 1);
    }

    static void CarInfo()
    {
        var sb = new System.Text.StringBuilder();
        var drv = Object.FindFirstObjectByType<AutoDriver>(FindObjectsInactive.Include);
        if (drv == null) sb.AppendLine("NO AutoDriver in scene.");
        else
        {
            sb.AppendLine("=== PLAYER CAR (AutoDriver object) ===");
            sb.AppendLine("root path: " + GetPath(drv.transform));
            DumpTransform(drv.transform, "", sb, 3);
            var rends = drv.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                sb.AppendLine($"visual bounds size: {b.size}  center offset: {b.center - drv.transform.position}");
            }
            foreach (var cam in drv.GetComponentsInChildren<Camera>(true))
                sb.AppendLine("camera under car: " + GetPath(cam.transform) + " lp=" + cam.transform.localPosition);
        }
        var mainCam = Camera.main;
        sb.AppendLine("Camera.main: " + (mainCam == null ? "none" : GetPath(mainCam.transform)));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/RealisticMobileCars - Pro 3D Models/Vehicles/RMCar05/Prefabs/RMCar05_Driver_EU.prefab");
        if (prefab == null) sb.AppendLine("RMCar05_Driver_EU prefab NOT found.");
        else
        {
            sb.AppendLine("\n=== RMCar05_Driver_EU prefab ===");
            DumpTransform(prefab.transform, "", sb, 4);
            var rends = prefab.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                sb.AppendLine($"prefab visual bounds size: {b.size}");
            }
        }
        WriteResult(sb.ToString());
    }

    const string NewCarPrefab =
        "Assets/RealisticMobileCars - Pro 3D Models/Vehicles/RMCar05/Prefabs/RMCar05_Driver_EU.prefab";
    const string VisualName = "RMCar05Visual";

    static void SwapCar()
    {
        var sb = new System.Text.StringBuilder();
        var drv = Object.FindFirstObjectByType<AutoDriver>(FindObjectsInactive.Include);
        if (drv == null) { WriteResult("swapcar: no AutoDriver in scene."); return; }
        var car = drv.transform;

        var old = car.Find(VisualName);
        if (old != null) { Object.DestroyImmediate(old.gameObject); sb.AppendLine("removed previous " + VisualName); }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NewCarPrefab);
        if (prefab == null) { WriteResult("swapcar: prefab missing: " + NewCarPrefab); return; }
        var shell = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(shell, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        shell.name = VisualName;
        shell.transform.SetParent(car, false);
        shell.transform.localPosition = Vector3.zero;
        shell.transform.localRotation = Quaternion.identity;

        foreach (var rb in shell.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        void Drop(string child) { var t = shell.transform.Find(child); if (t != null) Object.DestroyImmediate(t.gameObject); }
        Drop("RMCar05_Collider");
        Drop("RMCar05_WheelsHubs");
        Drop("RMCar05_Helpers");
        Drop("RMCar05_Particles");
        sb.AppendLine("shell instantiated, physics/colliders/helpers stripped");

        int hid = 0;
        foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
            if (!r.transform.IsChildOf(shell.transform) && r.enabled) { r.enabled = false; hid++; }
        sb.AppendLine($"old renderers hidden: {hid}");

        var sync = car.GetComponent<WheelPoseSync>();
        if (sync == null) sync = car.gameObject.AddComponent<WheelPoseSync>();
        Transform Col(string n) => car.Find("Wheels/Colliders/" + n);
        Transform Vis(string n) => shell.transform.Find("RMCar05_Main/" + n);
        sync.colliders = new[]
        {
            Col("FrontLeftWheel").GetComponent<WheelCollider>(),
            Col("FrontRightWheel").GetComponent<WheelCollider>(),
            Col("RearLeftWheel").GetComponent<WheelCollider>(),
            Col("RearRightWheel").GetComponent<WheelCollider>(),
        };
        sync.visuals = new[]
        {
            Vis("RMCar05_WheelFrontLeft"), Vis("RMCar05_WheelFrontRight"),
            Vis("RMCar05_WheelRearLeft"), Vis("RMCar05_WheelRearRight"),
        };
        sb.AppendLine("WheelPoseSync wired (4 wheels)");

        foreach (var fc in Object.FindObjectsByType<CarFollowCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            fc.gameObject.SetActive(false);
            sb.AppendLine("chase camera disabled: " + GetPath(fc.transform));
        }
        var camT = car.Find("DriverCamera");
        if (camT == null)
        {
            var go = new GameObject("DriverCamera");
            camT = go.transform;
            camT.SetParent(car, false);
        }
        camT.localPosition = new Vector3(-0.37f, 1.22f, 0.12f);   // driver eye (LHD seat)
        camT.localRotation = Quaternion.identity;
        var cam = camT.GetComponent<Camera>();
        if (cam == null) cam = camT.gameObject.AddComponent<Camera>();
        cam.nearClipPlane = 0.08f;
        cam.fieldOfView = 60f;
        camT.gameObject.tag = "MainCamera";
        if (camT.GetComponent<AudioListener>() == null) camT.gameObject.AddComponent<AudioListener>();
        if (camT.GetComponent<DriverCamera>() == null) camT.gameObject.AddComponent<DriverCamera>();
        // one listener only
        foreach (var al in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (al.transform != camT) { al.enabled = false; sb.AppendLine("extra AudioListener disabled: " + GetPath(al.transform)); }
        sb.AppendLine("DriverCamera at " + camT.localPosition + " (tag MainCamera)");

        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        sb.AppendLine("scene " + (saved ? "SAVED" : "NOT saved"));
        WriteResult("swapcar DONE\n" + sb);
    }

    static string GetPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    static List<Vector3> TreePositions()
    {
        var trees = new List<Vector3>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.gameObject.layer == TreeLayer) trees.Add(t.position);
        return trees;
    }

    static float DistToPolylineXZ(Vector3 p, List<Vector2> pts)
    {
        float best = float.MaxValue;
        var q = new Vector2(p.x, p.z);
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / len2);
            best = Mathf.Min(best, (q - (a + t * ab)).sqrMagnitude);
        }
        return Mathf.Sqrt(best);
    }

    static void RouteScan()
    {
        float[] radii = { 8f, 15f, 25f };
        var trees = TreePositions();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"trees on layer {TreeLayer}: {trees.Count}; per-route counts at radii {string.Join("/", radii)} m");
        foreach (var opt in RouteCandidatesData.Load(RoutesJson))
        {
            var totals = new int[radii.Length];
            var per = new List<string>();
            foreach (var r in opt.routes)
            {
                var cs = radii.Select(rad => trees.Count(t => DistToPolylineXZ(t, r.pts) <= rad)).ToArray();
                for (int i = 0; i < cs.Length; i++) totals[i] += cs[i];
                per.Add($"R{r.routeIndex}={string.Join("/", cs)}");
            }
            sb.AppendLine($"startNode {opt.startNode}: TOTAL {string.Join("/", totals)}   " + string.Join("  ", per));
        }
        WriteResult(sb.ToString());
    }

    static void ClearTrees(long startNode)
    {
        var opt = RouteCandidatesData.Load(RoutesJson).FirstOrDefault(o => o.startNode == startNode);
        if (opt == null) { WriteResult($"cleartrees: startNode {startNode} not in JSON."); return; }
        int hidden = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.gameObject.layer != TreeLayer) continue;
            foreach (var r in opt.routes)
            {
                if (DistToPolylineXZ(t.position, r.pts) <= ClearRadius)
                {
                    t.gameObject.SetActive(false);
                    hidden++;
                    break;
                }
            }
        }
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"cleartrees: {hidden} trees hidden within {ClearRadius} m of startNode {startNode} routes. " +
                    $"Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void MissingScan()
    {
        var sb = new System.Text.StringBuilder();
        int bad = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var comps = t.GetComponents<Component>();
            int miss = comps.Count(c => c == null);
            if (miss > 0)
            {
                bad++;
                sb.AppendLine($"{GetPath(t)}  ({miss} missing script{(miss > 1 ? "s" : "")}; " +
                              $"present: {string.Join(",", comps.Where(c => c != null).Select(c => c.GetType().Name))})");
            }
        }
        WriteResult($"missing-script scan: {bad} objects affected\n" + sb);
    }

    static void Audit()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== cameras ===");
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            sb.AppendLine($"{GetPath(c.transform)}  activeGO={c.gameObject.activeInHierarchy} camEnabled={c.enabled} tag={c.tag}");
        sb.AppendLine("=== audio listeners ===");
        foreach (var a in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            sb.AppendLine($"{GetPath(a.transform)}  activeGO={a.gameObject.activeInHierarchy} enabled={a.enabled}");
        sb.AppendLine("=== canvases ===");
        foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            sb.AppendLine($"{GetPath(cv.transform)}  mode={cv.renderMode} activeGO={cv.gameObject.activeInHierarchy}");
        sb.AppendLine("=== suspicious legacy roots (depth 2) ===");
        string[] names = { "AutoDriver Route Waypoints", "Intersections", "Gley", "TrafficManager", "Car Camera", "FreeCam URP", "RoadWalls", "RoadBoundaryWalls" };
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!names.Any(n => go.name.Contains(n))) continue;
            sb.AppendLine($"{go.name}  active={go.activeSelf}  children={go.transform.childCount}");
            foreach (Transform c in go.transform)
                sb.AppendLine($"  {c.name}  active={c.gameObject.activeSelf}  children={c.childCount}  " +
                              $"[{string.Join(",", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name))}]");
        }
        var wallsGo = GameObject.Find("RoadBoundaryWalls (generated)");
        sb.AppendLine("wall mesh object: " + (wallsGo == null ? "not found by name (may be under a holder)" : GetPath(wallsGo.transform)));
        WriteResult(sb.ToString());
    }

    static void BuildTrafficLights(int minArms)
    {
#if GLEY_TRAFFIC_SYSTEM
        var matR = MakeUnlit(new Color(0.95f, 0.15f, 0.12f));
        var matY = MakeUnlit(new Color(0.95f, 0.75f, 0.10f));
        var matG = MakeUnlit(new Color(0.15f, 0.90f, 0.25f));
        var matPole = MakeUnlit(new Color(0.15f, 0.15f, 0.17f));
        int converted = 0, heads = 0;
        var sb = new System.Text.StringBuilder();
        var byNode = LoadNetByNode();

        var routePts = new System.Collections.Generic.List<Vector2>();
        foreach (var opt in RouteCandidatesData.Load(RoutesJson))
            foreach (var r in opt.routes) routePts.AddRange(r.pts);
        bool NearRoute(Vector3 p)
        {
            Vector2 q = new(p.x, p.z);
            foreach (var rp in routePts) if (Vector2.Distance(rp, q) < 25f) return true;
            return false;
        }

        foreach (var pri in Object.FindObjectsByType<Gley.TrafficSystem.PriorityIntersectionSettings>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var stops = pri.enterWaypoints;
            if (stops == null) continue;
            bool bigJunction = stops.Count >= minArms;
            // (>= minArms) unchanged.
            bool onRoute = stops.Count >= 2 && NearRoute(pri.transform.position);
            if (!bigJunction && !onRoute) continue;
            var go = pri.gameObject;

            var lights = go.AddComponent<Gley.TrafficSystem.TrafficLightsIntersectionSettings>();
            lights.Initialize();
            lights.stopWaypoints = stops;
            lights.exitWaypoints = pri.exitWaypoints;
            lights.greenLightTime = 4f;
            lights.yellowLightTime = 1f;

            heads += BuildHeadsPerArm(go, stops, ParseNodeId(go.name), byNode, matR, matY, matG, matPole);

            Object.DestroyImmediate(pri);
            go.name = go.name.Replace("PriorityJ", "LightsJ");
            sb.AppendLine($"converted: {go.name} ({stops.Count} arms)");
            converted++;
        }

        if (converted > 0)
        {
            var wpConverter = new Gley.TrafficSystem.Editor.TrafficWaypointsConverter();
            wpConverter.ConvertWaypoints();
            new Gley.TrafficSystem.Editor.IntersectionConverter(wpConverter, null).ConvertAllIntersections();
        }
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"trafficlights: {converted} junctions converted (>= {minArms} arms district-wide, >= 2 enter-arms within 25 m of study routes), {heads} light heads built, " +
                    $"green 8s / yellow 2s. Scene {(saved ? "SAVED" : "NOT saved")}.\n" + sb);
#else
        WriteResult("trafficlights: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    static float HalfWidth(string cls) => cls switch
    {
        "primary" => 4.0f,
        "secondary" => 3.7f,
        "tertiary" => 3.3f,
        _ => 2.8f,
    };

    static System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Newtonsoft.Json.Linq.JToken>> LoadNetByNode()
    {
        var root = Newtonsoft.Json.Linq.JObject.Parse(
            File.ReadAllText(TrafficNet("network_unity.json")));
        var byNode = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Newtonsoft.Json.Linq.JToken>>();
        foreach (var s in root["segments"])
        {
            void Add(long n)
            {
                if (!byNode.TryGetValue(n, out var l)) byNode[n] = l = new();
                l.Add(s);
            }
            Add((long)s["startNode"]);
            Add((long)s["endNode"]);
        }
        return byNode;
    }

    const float PostYawOffsetDeg = 0f;

    static GameObject _lightPostPrefab;
    static GameObject LightPostPrefab()
    {
        if (_lightPostPrefab == null)
            _lightPostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Gley/UrbanAssets/Runtime/Graphics/Environment/Prefabs/TrafficLightPost/TrafficLightPost.prefab");
        return _lightPostPrefab;
    }

    static void RebuildLightHeads()
    {
#if GLEY_TRAFFIC_SYSTEM
        var matR = MakeUnlit(new Color(0.95f, 0.15f, 0.12f));
        var matY = MakeUnlit(new Color(0.95f, 0.75f, 0.10f));
        var matG = MakeUnlit(new Color(0.15f, 0.90f, 0.25f));
        var matPole = MakeUnlit(new Color(0.15f, 0.15f, 0.17f));
        var byNode = LoadNetByNode();
        int junctions = 0, heads = 0;
        foreach (var tl in Object.FindObjectsByType<Gley.TrafficSystem.TrafficLightsIntersectionSettings>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var go = tl.gameObject;
            for (int c = go.transform.childCount - 1; c >= 0; c--)
            {
                var ch = go.transform.GetChild(c);
                if (ch.name.StartsWith("LightHead_")) Object.DestroyImmediate(ch.gameObject);
            }
            heads += BuildHeadsPerArm(go, tl.stopWaypoints, ParseNodeId(go.name), byNode, matR, matY, matG, matPole);
            junctions++;
        }
        var wpConverter = new Gley.TrafficSystem.Editor.TrafficWaypointsConverter();
        wpConverter.ConvertWaypoints();
        new Gley.TrafficSystem.Editor.IntersectionConverter(wpConverter, null).ConvertAllIntersections();
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"lightheads: {junctions} junctions re-headed ({heads} heads), model=study-head v3 (big lenses, high mount). Scene {(saved ? "SAVED" : "NOT saved")}.");
#else
        WriteResult("lightheads: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    const float LensDiameter = 0.42f;
    const float PoleTopY = 3.2f;

    static void MatchArm(Vector3 stopPos, Vector3 junctionPos,
                         System.Collections.Generic.List<Newtonsoft.Json.Linq.JToken> nodeSegs,
                         out Vector3 dir, out Vector3 basePt, out float off, out string key)
    {
        dir = junctionPos - stopPos; dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        basePt = stopPos;
        off = 2.8f + 0.9f;
        string segId = "fb";
        if (nodeSegs != null)
        {
            Newtonsoft.Json.Linq.JToken bestSeg = null;
            int bestIdx = 0; float bestD = 6f; Vector3 bestF = stopPos;
            foreach (var s in nodeSegs)
            {
                var xs = s["xs"].ToObject<float[]>();
                var zs = s["zs"].ToObject<float[]>();
                for (int k = 0; k < xs.Length - 1; k++)
                {
                    Vector2 a = new(xs[k], zs[k]), b = new(xs[k + 1], zs[k + 1]);
                    Vector2 q = new(stopPos.x, stopPos.z);
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                    Vector2 f = a + t * ab;
                    float dd = (q - f).magnitude;
                    if (dd < bestD) { bestD = dd; bestSeg = s; bestIdx = k; bestF = new Vector3(f.x, stopPos.y, f.y); }
                }
            }
            if (bestSeg != null)
            {
                var xs = bestSeg["xs"].ToObject<float[]>();
                var zs = bestSeg["zs"].ToObject<float[]>();
                Vector3 tan = new(xs[bestIdx + 1] - xs[bestIdx], 0f, zs[bestIdx + 1] - zs[bestIdx]);
                Vector3 toJunc = junctionPos - stopPos; toJunc.y = 0f;
                if (Vector3.Dot(tan, toJunc) < 0f) tan = -tan;
                if (tan.sqrMagnitude > 0.01f) dir = tan.normalized;
                basePt = bestF;
                off = HalfWidth((string)bestSeg["cls"]) + 0.9f;
                segId = ((long)bestSeg["startNode"]).ToString() + "_" + ((long)bestSeg["endNode"]);
            }
        }
        int bucket = Mathf.RoundToInt(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg / 90f);
        key = segId + "#" + bucket;
    }

    static void BuildStudyHead(GameObject head, Material matR, Material matY, Material matG, Material matPole,
                               System.Collections.Generic.List<GameObject> reds,
                               System.Collections.Generic.List<GameObject> yels,
                               System.Collections.Generic.List<GameObject> grns)
    {
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(pole.GetComponent<Collider>());
        pole.name = "Pole";
        pole.transform.SetParent(head.transform, false);
        pole.transform.localPosition = new Vector3(0f, PoleTopY * 0.5f, 0f);
        pole.transform.localScale = new Vector3(0.09f, PoleTopY * 0.5f, 0.09f);
        pole.GetComponent<MeshRenderer>().sharedMaterial = matPole;

        var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(housing.GetComponent<Collider>());
        housing.name = "Housing";
        housing.transform.SetParent(head.transform, false);
        housing.transform.localPosition = new Vector3(0f, 3.35f, 0f);
        housing.transform.localScale = new Vector3(0.58f, 1.55f, 0.22f);
        housing.GetComponent<MeshRenderer>().sharedMaterial = matPole;

        GameObject Lens(string n, float y, Material m)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(s.GetComponent<Collider>());
            s.name = n;
            s.transform.SetParent(head.transform, false);
            s.transform.localPosition = new Vector3(0f, y, 0.13f);
            s.transform.localScale = new Vector3(LensDiameter, LensDiameter, 0.10f);
            s.GetComponent<MeshRenderer>().sharedMaterial = m;
            return s;
        }
        reds.Add(Lens("Red", 3.85f, matR));
        yels.Add(Lens("Yellow", 3.35f, matY));
        grns.Add(Lens("Green", 2.85f, matG));
    }

    static int BuildHeadsPerArm(GameObject go,
                                System.Collections.Generic.List<Gley.TrafficSystem.IntersectionStopWaypointsSettings> stops,
                                long node,
                                System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Newtonsoft.Json.Linq.JToken>> byNode,
                                Material matR, Material matY, Material matG, Material matPole)
    {
        byNode.TryGetValue(node, out var nodeSegs);
        int heads = 0;
        for (int i = 0; i < stops.Count; i++)
        {
            var wps = stops[i].roadWaypoints;
            if (wps == null || wps.Count == 0) continue;

            var groups = new System.Collections.Generic.Dictionary<string, (Vector3 dir, Vector3 basePt, float off, float dJunc)>();
            foreach (var w in wps)
            {
                if (w == null) continue;
                Vector3 sp = w.transform.position;
                MatchArm(sp, go.transform.position, nodeSegs, out var dir, out var basePt, out var off, out var key);
                float dj = (sp - go.transform.position).sqrMagnitude;
                if (!groups.TryGetValue(key, out var g) || dj < g.dJunc)
                    groups[key] = (dir, basePt, off, dj);
            }

            var reds = new System.Collections.Generic.List<GameObject>();
            var yels = new System.Collections.Generic.List<GameObject>();
            var grns = new System.Collections.Generic.List<GameObject>();
            int armIdx = 0;
            foreach (var g in groups.Values)
            {
                Vector3 side = Vector3.Cross(Vector3.up, g.dir).normalized;
                var head = new GameObject($"LightHead_{i}_{armIdx++}");
                head.transform.SetParent(go.transform, false);
                head.transform.position = g.basePt + side * g.off;
                head.transform.rotation = Quaternion.LookRotation(-g.dir, Vector3.up);   // face oncoming drivers
                BuildStudyHead(head, matR, matY, matG, matPole, reds, yels, grns);
                heads++;
            }
            stops[i].redLightObjects = reds;
            stops[i].yellowLightObjects = yels;
            stops[i].greenLightObjects = grns;
        }
        return heads;
    }

    static int BuildHeads(GameObject go, System.Collections.Generic.List<Gley.TrafficSystem.IntersectionStopWaypointsSettings> stops,
                          long node,
                          System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Newtonsoft.Json.Linq.JToken>> byNode,
                          Material matR, Material matY, Material matG, Material matPole)
    {
        byNode.TryGetValue(node, out var nodeSegs);
        int heads = 0;
        for (int i = 0; i < stops.Count; i++)
        {
            var wps = stops[i].roadWaypoints;
            if (wps == null || wps.Count == 0) continue;
            var last = wps[wps.Count - 1];
            Vector3 stopPos = last.transform.position;

            Vector3 dir = go.transform.position - stopPos; dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            Vector3 basePt = stopPos;
            float off = 2.8f + 0.9f;

            if (nodeSegs != null)
            {
                Newtonsoft.Json.Linq.JToken bestSeg = null;
                int bestIdx = 0; float bestD = 6f; Vector3 bestF = stopPos;
                foreach (var s in nodeSegs)
                {
                    var xs = s["xs"].ToObject<float[]>();
                    var zs = s["zs"].ToObject<float[]>();
                    for (int k = 0; k < xs.Length - 1; k++)
                    {
                        Vector2 a = new(xs[k], zs[k]), b = new(xs[k + 1], zs[k + 1]);
                        Vector2 q = new(stopPos.x, stopPos.z);
                        Vector2 ab = b - a;
                        float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                        Vector2 f = a + t * ab;
                        float dd = (q - f).magnitude;
                        if (dd < bestD) { bestD = dd; bestSeg = s; bestIdx = k; bestF = new Vector3(f.x, stopPos.y, f.y); }
                    }
                }
                if (bestSeg != null)
                {
                    var xs = bestSeg["xs"].ToObject<float[]>();
                    var zs = bestSeg["zs"].ToObject<float[]>();
                    Vector3 tan = new(xs[bestIdx + 1] - xs[bestIdx], 0f, zs[bestIdx + 1] - zs[bestIdx]);
                    if ((long)bestSeg["startNode"] == node) tan = -tan;
                    if (tan.sqrMagnitude > 0.01f) dir = tan.normalized;
                    basePt = bestF;
                    off = HalfWidth((string)bestSeg["cls"]) + 0.9f;
                }
            }
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            stopPos = basePt;

            var head = new GameObject($"LightHead_{i}");
            head.transform.SetParent(go.transform, false);
            head.transform.position = stopPos + side * off;
            head.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up); // face oncoming drivers

            GameObject red = null, yel = null, grn = null;
            var postPrefab = LightPostPrefab();
            if (postPrefab != null)
            {
                var post = (GameObject)PrefabUtility.InstantiatePrefab(postPrefab);
                post.name = "Post";
                post.transform.SetParent(head.transform, false);
                post.transform.localPosition = Vector3.zero;
                post.transform.localRotation = Quaternion.Euler(0f, PostYawOffsetDeg, 0f);
                foreach (var tr in post.GetComponentsInChildren<Transform>(true))
                {
                    if (tr.GetComponent<Renderer>() == null) continue;
                    string n = tr.name.ToLowerInvariant();
                    if (n.Contains("pedestrian")) continue;
                    if (red == null && n.Contains("red")) red = tr.gameObject;
                    else if (yel == null && (n.Contains("yellow") || n.Contains("amber"))) yel = tr.gameObject;
                    else if (grn == null && n.Contains("green")) grn = tr.gameObject;
                }
                if (red == null || yel == null || grn == null)
                {
                    Debug.LogWarning($"[RunFlag] TrafficLightPost bulbs not found (red={red != null} yellow={yel != null} green={grn != null}) �?procedural fallback.");
                    Object.DestroyImmediate(post);
                    red = yel = grn = null;
                }
            }

            if (red == null)
            {
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(pole.GetComponent<Collider>());
                pole.name = "Pole";
                pole.transform.SetParent(head.transform, false);
                pole.transform.localPosition = new Vector3(0f, 1.4f, 0f);
                pole.transform.localScale = new Vector3(0.07f, 1.4f, 0.07f);
                pole.GetComponent<MeshRenderer>().sharedMaterial = matPole;

                GameObject Ball(string n, float h, Material m)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(s.GetComponent<Collider>());
                    s.name = n;
                    s.transform.SetParent(head.transform, false);
                    s.transform.localPosition = new Vector3(0f, h, 0f);
                    s.transform.localScale = Vector3.one * 0.24f;
                    s.GetComponent<MeshRenderer>().sharedMaterial = m;
                    return s;
                }
                red = Ball("Red", 3.05f, matR);
                yel = Ball("Yellow", 2.78f, matY);
                grn = Ball("Green", 2.51f, matG);
            }

            stops[i].redLightObjects = new System.Collections.Generic.List<GameObject> { red };
            stops[i].yellowLightObjects = new System.Collections.Generic.List<GameObject> { yel };
            stops[i].greenLightObjects = new System.Collections.Generic.List<GameObject> { grn };
            heads++;
        }
        return heads;
    }

    static void FixTrafficPlayer()
    {
#if GLEY_TRAFFIC_SYSTEM
        var tc = Object.FindFirstObjectByType<Gley.TrafficSystem.TrafficComponent>(FindObjectsInactive.Include);
        var drv = Object.FindFirstObjectByType<AutoDriver>(FindObjectsInactive.Include);
        if (tc == null || drv == null) { WriteResult("fixplayer: TrafficComponent or AutoDriver missing."); return; }
        string old = tc.player == null ? "NULL/destroyed" : GetPath(tc.player);
        float oldMin = tc.minDistanceToAdd;
        tc.player = drv.transform;
        tc.minDistanceToAdd = 45f;
        EditorUtility.SetDirty(tc);
        EditorSceneManager.MarkSceneDirty(tc.gameObject.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"fixplayer: TrafficComponent.player was [{old}] -> [{GetPath(drv.transform)}]; " +
                    $"minDistanceToAdd {oldMin} -> 45. Scene {(saved ? "SAVED" : "NOT saved")}.");
#else
        WriteResult("fixplayer: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    static void CarLayer()
    {
        var drv = Object.FindFirstObjectByType<AutoDriver>(FindObjectsInactive.Include);
        if (drv == null) { WriteResult("carlayer: no AutoDriver."); return; }
        var sb = new System.Text.StringBuilder();
        int moved = 0;
        foreach (var col in drv.GetComponentsInChildren<Collider>(true))
        {
            var go2 = col.gameObject;
            sb.AppendLine($"{GetPath(go2.transform)}  layer={go2.layer}({LayerMask.LayerToName(go2.layer)})  {col.GetType().Name}");
            if (go2.layer != 7) { go2.layer = 7; moved++; }
        }
        if (drv.gameObject.layer != 7) { drv.gameObject.layer = 7; moved++; }
        sb.AppendLine($"root layer={drv.gameObject.layer}({LayerMask.LayerToName(drv.gameObject.layer)})");
        bool saved = false;
        if (moved > 0)
        {
            EditorSceneManager.MarkSceneDirty(drv.gameObject.scene);
            saved = EditorSceneManager.SaveOpenScenes();
        }
        WriteResult($"carlayer: {moved} objects moved to layer 7 (Player). Scene {(moved > 0 ? (saved ? "SAVED" : "NOT saved") : "unchanged")}.\n" + sb);
    }

    static void BuildPedestrians(int count)
    {
        var rs = Object.FindFirstObjectByType<RouteSet>(FindObjectsInactive.Include);
        if (rs == null) { WriteResult("pedestrians: no RouteSet in scene."); return; }
        var opt = RouteCandidatesData.Load(RoutesJson).FirstOrDefault(o => o.startNode == rs.startNode);
        if (opt == null) { WriteResult($"pedestrians: startNode {rs.startNode} not in JSON."); return; }

        var old = GameObject.Find("StaticPedestrians");
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("StaticPedestrians");

        var palette = new[]
        {
            new Color(0.75f, 0.22f, 0.17f), new Color(0.16f, 0.32f, 0.60f),
            new Color(0.18f, 0.45f, 0.22f), new Color(0.55f, 0.42f, 0.16f),
            new Color(0.35f, 0.20f, 0.45f), new Color(0.25f, 0.25f, 0.28f),
        };
        var mats = palette.Select(MakeUnlitLit).ToArray();
        var skin = MakeUnlitLit(new Color(0.85f, 0.68f, 0.55f));

        var pedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Gley/PedestrianSystem/Runtime/Graphics/Pedestrian/Prefabs/TestPedestrian.prefab");
        var pedPool = new System.Collections.Generic.List<GameObject>();
        foreach (var g in AssetDatabase.FindAssets("t:Prefab",
                     new[] { "Assets/DenysAlmaral/CityPeople/Prefabs" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (path.Contains("/PROPS/") || path.Contains("/tools/")) continue;
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (pf != null && pf.GetComponentInChildren<SkinnedMeshRenderer>() != null) pedPool.Add(pf);
        }
        if (!AssetDatabase.IsValidFolder("Assets/StudyAssets")) AssetDatabase.CreateFolder("Assets", "StudyAssets");
        if (!AssetDatabase.IsValidFolder("Assets/StudyAssets/Pedestrians")) AssetDatabase.CreateFolder("Assets/StudyAssets", "Pedestrians");
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/StudyAssets/Pedestrians" }))
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (pf != null) pedPool.Add(pf);
        }
        if (pedPool.Count == 0 && pedPrefab != null) pedPool.Add(pedPrefab);
        var palettes = AssetDatabase.FindAssets("people_pal_s t:Material",
                new[] { "Assets/DenysAlmaral/CityPeople/Materials" })
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null).ToArray();
        var poseClips = AssetDatabase.FindAssets("idle t:AnimationClip",
                new[] { "Assets/DenysAlmaral/CityPeople/Animations" })
            .Select(g => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(c => c != null && c.name.ToLowerInvariant().Contains("idle")).ToArray();
        if (poseClips.Length == 0)
            poseClips = AssetDatabase.FindAssets("t:AnimationClip",
                    new[] { "Assets/Gley/PedestrianSystem/Runtime/Graphics/Pedestrian" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null)
                .Where(c => { var n = c.name.ToLowerInvariant();
                              return n.Contains("idle") && !n.Contains("die") && !n.Contains("revive"); })
                .ToArray();

        Random.InitState(87);
        int placed = 0, attempts = 0;
        while (placed < count && attempts < count * 12)
        {
            attempts++;
            int totalPts = opt.routes.Sum(rr => rr.pts.Count);
            int pick = Random.Range(0, totalPts);
            var r = opt.routes[0];
            foreach (var rr in opt.routes) { if (pick < rr.pts.Count) { r = rr; break; } pick -= rr.pts.Count; }
            int k = Mathf.Clamp(pick, 1, r.pts.Count - 2);
            Vector2 prev = r.pts[k - 1], next = r.pts[Mathf.Min(r.pts.Count - 1, k + 1)];
            Vector2 dir = (next - prev).normalized;
            Vector2 perp = new(-dir.y, dir.x);
            float side = Random.value < 0.5f ? -1f : 1f;
            float halfw = k < r.halfw.Count ? r.halfw[k] : 2.8f;
            Vector2 p2 = r.pts[k] + perp * side * (halfw + Random.Range(1.0f, 3.0f));

            if (!Physics.Raycast(new Vector3(p2.x, 300f, p2.y), Vector3.down, out RaycastHit hit, 600f, ~0,
                                 QueryTriggerInteraction.Ignore)) continue;
            bool onPavement = hit.collider.gameObject.layer == 30 && GetPath(hit.transform).Contains("Sidewalks");
            bool onVerge = hit.collider.gameObject.layer == 15;
            if (!onPavement && !onVerge) continue;

            var ped = new GameObject($"Ped_{placed:D2}");
            ped.transform.SetParent(root.transform, false);
            ped.transform.position = hit.point;
            ped.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            // differ, not just scale
            float ph = Random.Range(0.88f, 1.10f);
            ped.transform.localScale = new Vector3(ph * Random.Range(0.94f, 1.06f), ph, ph * Random.Range(0.94f, 1.06f));

            var pickPrefab = pedPool.Count > 0 ? pedPool[Random.Range(0, pedPool.Count)] : null;
            if (pickPrefab != null)
            {
                var model = Object.Instantiate(pickPrefab, ped.transform);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                // mid-turn frame)
                if (poseClips.Length > 0)
                {
                    var clip = poseClips[Random.Range(0, poseClips.Length)];
                    clip.SampleAnimation(model, Random.Range(0f, Mathf.Min(0.2f, clip.length)));
                }
                if (palettes.Length > 0)
                {
                    var pal = palettes[Random.Range(0, palettes.Length)];
                    foreach (var rr in model.GetComponentsInChildren<Renderer>(true))
                    {
                        var cur = rr.sharedMaterial;
                        if (cur != null && cur.name.StartsWith("people_pal"))
                        {
                            var ms = rr.sharedMaterials;
                            for (int mi = 0; mi < ms.Length; mi++)
                                if (ms[mi] != null && ms[mi].name.StartsWith("people_pal")) ms[mi] = pal;
                            rr.sharedMaterials = ms;
                        }
                    }
                }
                // RequireComponent dependency ordering.
                for (int pass = 0; pass < 3; pass++)
                    foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true))
                        if (mb != null) Object.DestroyImmediate(mb);
                foreach (var an in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
                foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                foreach (var cc in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(cc);
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.name = "Body";
                body.transform.SetParent(ped.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.72f, 0f);
                body.transform.localScale = new Vector3(0.42f, 0.72f, 0.42f);
                body.GetComponent<MeshRenderer>().sharedMaterial = mats[placed % mats.Length];

                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(head.GetComponent<Collider>());
                head.name = "Head";
                head.transform.SetParent(ped.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.58f, 0f);
                head.transform.localScale = Vector3.one * 0.27f;
                head.GetComponent<MeshRenderer>().sharedMaterial = skin;
            }

            ped.AddComponent<MarkerTarget>();
            placed++;
        }

        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"pedestrians: {placed}/{count} placed on pavements along startNode {rs.startNode} " +
                    $"({attempts} attempts, seed 87). Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void PairLightPhases()
    {
#if GLEY_TRAFFIC_SYSTEM
        int junctions = 0, before = 0, after = 0;
        foreach (var tl in Object.FindObjectsByType<Gley.TrafficSystem.TrafficLightsIntersectionSettings>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var stops = tl.stopWaypoints;
            if (stops == null || stops.Count <= 2) continue;

            var groups = new List<List<Gley.TrafficSystem.IntersectionStopWaypointsSettings>>();
            var axes = new List<float>();
            foreach (var s in stops)
            {
                if (s.roadWaypoints == null || s.roadWaypoints.Count == 0) continue;
                Vector3 d = tl.transform.position - s.roadWaypoints[s.roadWaypoints.Count - 1].transform.position;
                float axis = ((Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg) % 180f + 180f) % 180f;
                int gi = -1;
                for (int i = 0; i < groups.Count; i++)
                    if (Mathf.Abs(Mathf.DeltaAngle(axes[i] * 2f, axis * 2f)) / 2f < 45f) { gi = i; break; }
                if (gi < 0) { groups.Add(new List<Gley.TrafficSystem.IntersectionStopWaypointsSettings>()); axes.Add(axis); gi = groups.Count - 1; }
                groups[gi].Add(s);
            }
            while (groups.Count > 2)
            {
                var extra = groups[groups.Count - 1];
                groups.RemoveAt(groups.Count - 1);
                float ax = axes[axes.Count - 1];
                axes.RemoveAt(axes.Count - 1);
                int near = Mathf.Abs(Mathf.DeltaAngle(axes[0] * 2f, ax * 2f)) <=
                           Mathf.Abs(Mathf.DeltaAngle(axes[1] * 2f, ax * 2f)) ? 0 : 1;
                groups[near].AddRange(extra);
            }

            if (groups.Count == 1 && groups[0].Count >= 2)
            {
                var only = groups[0];
                var memberAxes = only.Select(s =>
                {
                    Vector3 d = tl.transform.position - s.roadWaypoints[s.roadWaypoints.Count - 1].transform.position;
                    return ((Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg) % 180f + 180f) % 180f;
                }).ToList();
                BestTwoGroups(memberAxes, out var ga, out var gb);
                groups.Clear();
                groups.Add(ga.Select(i => only[i]).ToList());
                groups.Add(gb.Select(i => only[i]).ToList());
            }

            before += stops.Count;
            tl.stopWaypoints = groups.Select(g => new Gley.TrafficSystem.IntersectionStopWaypointsSettings
            {
                roadWaypoints = g.SelectMany(s => s.roadWaypoints).ToList(),
                redLightObjects = g.SelectMany(s => s.redLightObjects).ToList(),
                yellowLightObjects = g.SelectMany(s => s.yellowLightObjects).ToList(),
                greenLightObjects = g.SelectMany(s => s.greenLightObjects).ToList(),
            }).ToList();
            after += tl.stopWaypoints.Count;
            EditorUtility.SetDirty(tl.gameObject);
            junctions++;
        }
        var wpConverter = new Gley.TrafficSystem.Editor.TrafficWaypointsConverter();
        wpConverter.ConvertWaypoints();
        new Gley.TrafficSystem.Editor.IntersectionConverter(wpConverter, null).ConvertAllIntersections();
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"lightphases: {junctions} junctions regrouped {before}->{after} phases (opposing arms share green). " +
                    $"Red now = ONE green + ONE yellow. Scene {(saved ? "SAVED" : "NOT saved")}.");
#else
        WriteResult("lightphases: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    static void FixWheelConfig()
    {
        var car = Object.FindFirstObjectByType<CarController>(FindObjectsInactive.Include);
        if (car == null) { WriteResult("fixwheelcfg: no CarController in scene."); return; }
        car.useWheel = true;
        car.useInputSystemWheel = false;
        car.useWinmmFallback = true;
        car.winmmSteerAxis = 0;
        car.winmmSteerDeadzone = 0.02f;
        car.winmmCombinedPedals = false;      // native mode: separate pedals
        car.winmmThrottleAxis = 2;
        car.winmmBrakeAxis = 3;               // R
        car.steerRange = 1.0f;
        car.steerCurve = 1.6f;
        car.pedalReverseDisabled = true;
        car.winmmForwardButton = 4;           // right paddle = D
        car.winmmReverseButton = 5;           // left paddle = R
        car.wheelDebugOverlay = false;
        EditorUtility.SetDirty(car);
        EditorSceneManager.MarkSceneDirty(car.gameObject.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"fixwheelcfg: scene CarController pinned to the native-G25 config " +
                    $"(steer X, separate pedals Y/R, steerRange 1.0, paddles 4/5). Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void InputDevices()
    {
        var sb = new System.Text.StringBuilder();
        var devs = UnityEngine.InputSystem.InputSystem.devices;
        sb.AppendLine($"InputSystem devices: {devs.Count}");
        foreach (var d in devs)
        {
            sb.AppendLine($"- '{d.displayName}'  layout={d.layout}  interface={d.description.interfaceName}  " +
                          $"product='{d.description.product}'  usages=[{string.Join(",", d.usages)}]  controls={d.allControls.Count}");
        }
        try
        {
            var names = UnityEngine.Input.GetJoystickNames();
            sb.AppendLine($"Legacy Input joysticks: {names.Length}");
            for (int i = 0; i < names.Length; i++) sb.AppendLine($"- [{i}] '{names[i]}'");
        }
        catch (System.Exception e) { sb.AppendLine("Legacy joystick query failed: " + e.Message); }
        WriteResult("inputdevices:\n" + sb);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct JOYINFOEX
    {
        public int dwSize, dwFlags;
        public int dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
        public int dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }
    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    static extern int joyGetPosEx(int uJoyID, ref JOYINFOEX pji);

    static bool _axesProbeRunning;

    static void AxesProbe(string arg)
    {
        if (_axesProbeRunning) { WriteResult("axesprobe: already running."); return; }
        float seconds = float.TryParse(arg, out float s) ? Mathf.Clamp(s, 3f, 90f) : 20f;

        UnityEngine.InputSystem.InputDevice dev = null;
        foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
        {
            string label = (d.description.product ?? "") + "|" + d.displayName;
            if (label.Contains("Racing Wheel") || d.layout.StartsWith("HID::")) { dev = d; break; }
        }
        if (dev == null) { WriteResult("axesprobe: no HID wheel device found (see 'inputdevices')."); return; }

        int joyId = -1;
        for (int id = 0; id < 4 && joyId < 0; id++)
        {
            var ji = new JOYINFOEX { dwSize = System.Runtime.InteropServices.Marshal.SizeOf<JOYINFOEX>(), dwFlags = 0xFF };
            if (joyGetPosEx(id, ref ji) == 0) joyId = id;
        }
        var wMins = new float[6]; var wMaxs = new float[6]; var wStarts = new float[6];
        for (int i = 0; i < 6; i++) { wMins[i] = float.MaxValue; wMaxs[i] = float.MinValue; }

        var controls = dev.allControls;
        int n = controls.Count;
        var mins = new float[n]; var maxs = new float[n]; var starts = new float[n]; var touched = new bool[n];
        for (int i = 0; i < n; i++) { mins[i] = float.MaxValue; maxs[i] = float.MinValue; }
        double endAt = EditorApplication.timeSinceStartup + seconds;
        bool first = true;
        _axesProbeRunning = true;
        WriteResult($"axesprobe SAMPLING for {seconds:F0}s on '{dev.displayName}' �?turn the wheel lock to lock and press EACH pedal fully, one at a time, NOW.");

        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            try
            {
                for (int i = 0; i < n; i++)
                {
                    if (!(controls[i] is UnityEngine.InputSystem.Controls.AxisControl axis)) continue;
                    float v;
                    try { v = axis.ReadValue(); }
                    catch { continue; }
                    if (first) starts[i] = v;
                    if (v < mins[i]) mins[i] = v;
                    if (v > maxs[i]) maxs[i] = v;
                    touched[i] = true;
                }
                if (joyId >= 0)
                {
                    var ji = new JOYINFOEX { dwSize = System.Runtime.InteropServices.Marshal.SizeOf<JOYINFOEX>(), dwFlags = 0xFF };
                    if (joyGetPosEx(joyId, ref ji) == 0)
                    {
                        float[] ax = { ji.dwXpos, ji.dwYpos, ji.dwZpos, ji.dwRpos, ji.dwUpos, ji.dwVpos };
                        for (int i = 0; i < 6; i++)
                        {
                            if (first) wStarts[i] = ax[i];
                            if (ax[i] < wMins[i]) wMins[i] = ax[i];
                            if (ax[i] > wMaxs[i]) wMaxs[i] = ax[i];
                        }
                    }
                }
                first = false;
                if (EditorApplication.timeSinceStartup < endAt) return;

                EditorApplication.update -= cb;
                _axesProbeRunning = false;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"device '{dev.displayName}'  layout={dev.layout}");
                sb.AppendLine("InputSystem controls that MOVED (path  type  rest/min/max):");
                int moved = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!touched[i]) continue;
                    float range = maxs[i] - mins[i];
                    if (range < 0.01f) continue;
                    string p = controls[i].path.StartsWith(dev.path)
                        ? controls[i].path.Substring(dev.path.Length).TrimStart('/')
                        : controls[i].path;
                    sb.AppendLine($"- {p}  ({controls[i].GetType().Name})  rest={starts[i]:F3}  min={mins[i]:F3}  max={maxs[i]:F3}");
                    moved++;
                }
                if (moved == 0) sb.AppendLine("(none �?Unity's HID parser gets no data from this wheel)");
                string[] axName = { "0/X", "1/Y", "2/Z", "3/R", "4/U", "5/V" };
                sb.AppendLine($"winmm joyGetPosEx (id {joyId}) axes that MOVED (axis  rest/min/max, 0..65535):");
                int wMoved = 0;
                for (int i = 0; i < 6; i++)
                {
                    if (wMins[i] == float.MaxValue || wMaxs[i] - wMins[i] < 500f) continue;
                    sb.AppendLine($"- axis {axName[i]}  rest={wStarts[i]:F0}  min={wMins[i]:F0}  max={wMaxs[i]:F0}");
                    wMoved++;
                }
                if (wMoved == 0) sb.AppendLine(joyId < 0 ? "(no winmm joystick found)" : "(no winmm axis moved)");
                WriteResult("axesprobe DONE:\n" + sb);
            }
            catch (System.Exception e)
            {
                EditorApplication.update -= cb;
                _axesProbeRunning = false;
                WriteResult("axesprobe FAILED: " + e);
            }
        };
        EditorApplication.update += cb;
    }

    static void ArmProbe()
    {
#if GLEY_TRAFFIC_SYSTEM
        var targets = new (string node, Vector2 pos)[]
        {
            ("25302974",  new Vector2(1032.2f, 62.0f)),
            ("26592154",  new Vector2(1002.6f, 130.7f)),
            ("108349702", new Vector2(1093.8f, 80.2f)),
            ("619174396", new Vector2(1082.8f, 114.6f)),
        };
        var sb = new System.Text.StringBuilder();
        var pris = Object.FindObjectsByType<Gley.TrafficSystem.PriorityIntersectionSettings>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var lights = Object.FindObjectsByType<Gley.TrafficSystem.TrafficLightsIntersectionSettings>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var (node, pos) in targets)
        {
            Gley.TrafficSystem.PriorityIntersectionSettings bestP = null; float bd = 30f;
            foreach (var p in pris)
            {
                float d = Vector2.Distance(new Vector2(p.transform.position.x, p.transform.position.z), pos);
                if (d < bd) { bd = d; bestP = p; }
            }
            Gley.TrafficSystem.TrafficLightsIntersectionSettings bestL = null; float bdl = 30f;
            foreach (var l in lights)
            {
                float d = Vector2.Distance(new Vector2(l.transform.position.x, l.transform.position.z), pos);
                if (d < bdl) { bdl = d; bestL = l; }
            }
            string line = $"node {node} @ ({pos.x:F1}, {pos.y:F1}): ";
            if (bestP != null && (bestL == null || bd <= bdl))
                line += $"PRIORITY '{bestP.gameObject.name}' at {bd:F1} m, enterWaypoints={(bestP.enterWaypoints == null ? 0 : bestP.enterWaypoints.Count)}";
            else if (bestL != null)
                line += $"LIGHTS '{bestL.gameObject.name}' at {bdl:F1} m, phases={(bestL.stopWaypoints == null ? 0 : bestL.stopWaypoints.Count)}";
            else
                line += "nothing within 30 m";
            sb.AppendLine(line);
        }
        WriteResult("armprobe:\n" + sb);
#else
        WriteResult("armprobe: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    static float AxisDist(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a * 2f, b * 2f)) / 2f;

    static void BestTwoGroups(List<float> axes, out List<int> g0, out List<int> g1)
    {
        int n = axes.Count;
        g0 = new List<int> { 0 };
        g1 = new List<int>();
        for (int i = 1; i < n; i++) g1.Add(i);
        float best = float.MaxValue;
        for (int mask = 1; mask < (1 << n) - 1; mask += 2)
        {
            var a = new List<int>();
            var b = new List<int>();
            for (int i = 0; i < n; i++) { if ((mask & (1 << i)) != 0) a.Add(i); else b.Add(i); }
            if (a.Count == 0 || b.Count == 0) continue;
            float cost = 0f;
            foreach (var grp in new[] { a, b })
                for (int i = 0; i < grp.Count; i++)
                    for (int j = i + 1; j < grp.Count; j++)
                        cost += AxisDist(axes[grp[i]], axes[grp[j]]);
            if (cost < best) { best = cost; g0 = a; g1 = b; }
        }
    }

    static void FixOnePhase()
    {
#if GLEY_TRAFFIC_SYSTEM
        var sb = new System.Text.StringBuilder();
        int fixedCount = 0;
        foreach (var tl in Object.FindObjectsByType<Gley.TrafficSystem.TrafficLightsIntersectionSettings>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tl.stopWaypoints == null || tl.stopWaypoints.Count >= 2) continue;
            var go = tl.gameObject;
            var heads = new List<Transform>();
            foreach (Transform ch in go.transform)
                if (ch.name.StartsWith("LightHead_")) heads.Add(ch);
            if (tl.stopWaypoints.Count == 0 || heads.Count < 2)
            {
                sb.AppendLine($"SKIP {go.name}: {(tl.stopWaypoints == null ? 0 : tl.stopWaypoints.Count)} entries, {heads.Count} heads - cannot split.");
                continue;
            }

            var wpsPerHead = heads.Select(_ => new List<Gley.TrafficSystem.WaypointSettings>()).ToList();
            foreach (var wp in tl.stopWaypoints[0].roadWaypoints)
            {
                int bi = 0; float bd = float.MaxValue;
                for (int i = 0; i < heads.Count; i++)
                {
                    float d = (heads[i].position - wp.transform.position).sqrMagnitude;
                    if (d < bd) { bd = d; bi = i; }
                }
                wpsPerHead[bi].Add(wp);
            }

            var axes = heads.Select(h =>
            {
                Vector3 f = h.forward;
                return ((Mathf.Atan2(f.z, f.x) * Mathf.Rad2Deg) % 180f + 180f) % 180f;
            }).ToList();
            BestTwoGroups(axes, out var gA, out var gB);

            Gley.TrafficSystem.IntersectionStopWaypointsSettings Entry(List<int> grp)
            {
                var e = new Gley.TrafficSystem.IntersectionStopWaypointsSettings
                {
                    roadWaypoints = new List<Gley.TrafficSystem.WaypointSettings>(),
                    redLightObjects = new List<GameObject>(),
                    yellowLightObjects = new List<GameObject>(),
                    greenLightObjects = new List<GameObject>(),
                };
                foreach (int i in grp)
                {
                    e.roadWaypoints.AddRange(wpsPerHead[i]);
                    var red = heads[i].Find("Red");
                    var yel = heads[i].Find("Yellow");
                    var grn = heads[i].Find("Green");
                    if (red != null) e.redLightObjects.Add(red.gameObject);
                    if (yel != null) e.yellowLightObjects.Add(yel.gameObject);
                    if (grn != null) e.greenLightObjects.Add(grn.gameObject);
                }
                return e;
            }
            tl.stopWaypoints = new List<Gley.TrafficSystem.IntersectionStopWaypointsSettings> { Entry(gA), Entry(gB) };
            EditorUtility.SetDirty(go);
            sb.AppendLine($"{go.name}: {heads.Count} heads -> 2 phases " +
                          $"[{string.Join("+", gA.Select(i => heads[i].name))}] | [{string.Join("+", gB.Select(i => heads[i].name))}], " +
                          $"waypoints per head {string.Join("/", wpsPerHead.Select(w => w.Count))}");
            fixedCount++;
        }

        if (fixedCount > 0)
        {
            var wpConverter = new Gley.TrafficSystem.Editor.TrafficWaypointsConverter();
            wpConverter.ConvertWaypoints();
            new Gley.TrafficSystem.Editor.IntersectionConverter(wpConverter, null).ConvertAllIntersections();
        }
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"fixonephase: {fixedCount} junction(s) re-split into 2 phases. Scene {(saved ? "SAVED" : "NOT saved")}.\n" + sb);
#else
        WriteResult("fixonephase: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    static void SetRouteTimes(string arg)
    {
#if GLEY_TRAFFIC_SYSTEM
        var parts = arg.Split(' ');
        float routeGreen = parts.Length > 0 && float.TryParse(parts[0], out float rg) ? rg : 20f;
        float crossGreen = parts.Length > 1 && float.TryParse(parts[1], out float cg) ? cg : 8f;
        float yellow = parts.Length > 2 && float.TryParse(parts[2], out float yy) ? yy : 2f;

        var routes = new System.Collections.Generic.List<Vector2[]>();
        foreach (var opt in RouteCandidatesData.Load(RoutesJson))
            foreach (var r in opt.routes) routes.Add(r.pts.ToArray());

        bool AlignedWithRoute(Vector3 stopPos, Vector3 junctionPos)
        {
            Vector2 q = new(stopPos.x, stopPos.z);
            Vector3 armDir3 = junctionPos - stopPos; armDir3.y = 0f;
            if (armDir3.sqrMagnitude < 0.01f) return false;
            Vector2 armDir = new Vector2(armDir3.x, armDir3.z).normalized;
            foreach (var pts in routes)
                for (int k = 0; k < pts.Length - 1; k++)
                {
                    Vector2 a = pts[k], b = pts[k + 1];
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                    Vector2 f = a + t * ab;
                    if ((q - f).sqrMagnitude > 12f * 12f) continue;
                    if (Mathf.Abs(Vector2.Dot(ab.normalized, armDir)) > 0.6f) return true;   // arm parallel to route
                }
            return false;
        }

        int junctions = 0, routePhases = 0, phases = 0;
        foreach (var tl in Object.FindObjectsByType<Gley.TrafficSystem.TrafficLightsIntersectionSettings>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            tl.greenLightTime = crossGreen;   // intersection-level fallback
            tl.yellowLightTime = yellow;
            foreach (var entry in tl.stopWaypoints)
            {
                bool aligned = false;
                if (entry.roadWaypoints != null)
                    foreach (var wp in entry.roadWaypoints)
                        if (wp != null && AlignedWithRoute(wp.transform.position, tl.transform.position))
                        { aligned = true; break; }
                entry.greenLightTime = aligned ? routeGreen : crossGreen;
                phases++;
                if (aligned) routePhases++;
            }
            EditorUtility.SetDirty(tl.gameObject);
            junctions++;
        }
        var wpConverter = new Gley.TrafficSystem.Editor.TrafficWaypointsConverter();
        wpConverter.ConvertWaypoints();
        new Gley.TrafficSystem.Editor.IntersectionConverter(wpConverter, null).ConvertAllIntersections();
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"routetimes: {junctions} junctions, {routePhases}/{phases} phases route-aligned -> green {routeGreen}s (cross {crossGreen}s), yellow {yellow}s; route-phase red = {crossGreen + yellow}s. Scene {(saved ? "SAVED" : "NOT saved")}.");
#else
        WriteResult("routetimes: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    [MenuItem("Tools/Sustainable Driving/Traffic Light Times...")]
    static void OpenLightTimes() => LightTimesWindow.ShowWindow();

    public class LightTimesWindow : EditorWindow
    {
        float _green = 4f;
        float _yellow = 1f;

        public static void ShowWindow()
        {
            var w = GetWindow<LightTimesWindow>(true, "Traffic Light Times");
            w.minSize = new Vector2(320, 150);
        }

        void OnGUI()
        {
            GUILayout.Label("Applies to ALL signalized junctions.", EditorStyles.wordWrappedLabel);
            _green = EditorGUILayout.FloatField("Green (s)", _green);
            _yellow = EditorGUILayout.FloatField("Yellow (s)", _yellow);
            EditorGUILayout.HelpBox(
                $"Phases are paired (opposite arms green together), so red = green + yellow = {_green + _yellow:F0}s.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Apply + rebake + save scene"))
                {
                    SetLightTimes($"{_green} {_yellow}");
                    ShowNotification(new GUIContent("Applied (details in flag_result.txt)"));
                }
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Exit Play mode first.", MessageType.Warning);
        }
    }

    static void SetLightTimes(string arg)
    {
#if GLEY_TRAFFIC_SYSTEM
        var parts = arg.Split(' ');
        float green = parts.Length > 0 && float.TryParse(parts[0], out float g) ? g : 6f;
        float yellow = parts.Length > 1 && float.TryParse(parts[1], out float y) ? y : 2f;
        int count = 0;
        foreach (var tl in Object.FindObjectsByType<Gley.TrafficSystem.TrafficLightsIntersectionSettings>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            tl.greenLightTime = green;
            tl.yellowLightTime = yellow;
            foreach (var entry in tl.stopWaypoints) entry.greenLightTime = green;
            EditorUtility.SetDirty(tl.gameObject);
            count++;
        }
        var wpConverter = new Gley.TrafficSystem.Editor.TrafficWaypointsConverter();
        wpConverter.ConvertWaypoints();
        new Gley.TrafficSystem.Editor.IntersectionConverter(wpConverter, null).ConvertAllIntersections();
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"lighttimes: {count} junctions set to green {green}s / yellow {yellow}s " +
                    $"(3-arm red �?{2 * (green + yellow):F0}s, 4-arm �?{3 * (green + yellow):F0}s). Scene {(saved ? "SAVED" : "NOT saved")}.");
#else
        WriteResult("lighttimes: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    static void UseFinalRoutes()
    {
        var rs = Object.FindFirstObjectByType<RouteSet>(FindObjectsInactive.Include);
        if (rs == null) { WriteResult("usefinal: no RouteSet in scene."); return; }
        rs.jsonPath = RoutesJson;
        rs.startNode = 1;
        EditorUtility.SetDirty(rs);
        EditorSceneManager.MarkSceneDirty(rs.gameObject.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        var opt = RouteCandidatesData.Load(RoutesJson).FirstOrDefault(o => o.startNode == 1);
        string labels = opt == null ? "FILE MISSING/EMPTY"
            : string.Join("  ", opt.routes.Select(r => $"R{r.routeIndex} {r.lengthM:F0}m"));
        WriteResult($"usefinal: RouteSet -> final_routes.json option 1 ({labels}). " +
                    $"Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static Material MakeUnlitLit(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh);
        m.SetColor("_BaseColor", c);
        return m;
    }

    static void FixWallMask()
    {
        var drv = Object.FindFirstObjectByType<AutoDriver>(FindObjectsInactive.Include);
        if (drv == null) { WriteResult("fixwallmask: no AutoDriver."); return; }
        int old = drv.wallMask.value;
        drv.wallMask = 1 | (1 << 29) | (1 << 30);
        EditorUtility.SetDirty(drv);
        EditorSceneManager.MarkSceneDirty(drv.gameObject.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"fixwallmask: {old} -> {drv.wallMask.value}. Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void BoxProbe(string arg)
    {
        var parts = arg.Split(' ');
        float x = float.Parse(parts[0]), z = float.Parse(parts[1]);
        var center = new Vector3(x, 59.5f, z);
        var hits = Physics.OverlapBox(center, new Vector3(7f, 3.5f, 7f), Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"colliders within ±7m of ({x},{z}) between y 56-63: {hits.Length}");
        foreach (var h in hits)
        {
            var b = h.bounds;
            sb.AppendLine($"{GetPath(h.transform)}  [{h.GetType().Name}] layer={h.gameObject.layer}({LayerMask.LayerToName(h.gameObject.layer)}) " +
                          $"trigger={h.isTrigger}  bounds c={b.center:F1} s={b.size:F1}");
        }
        WriteResult("boxprobe:\n" + sb);
    }

    static void ProbeHit()
    {
#if GLEY_TRAFFIC_SYSTEM
        var sb = new System.Text.StringBuilder();
        int sampled = 0;
        var all = Object.FindObjectsByType<Gley.TrafficSystem.TrafficLightsIntersectionSettings>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.AppendLine($"lights components found: {all.Length}");
        foreach (var tl in all)
        {
            if (sampled >= 3) break;
            var stops = tl.stopWaypoints;
            if (stops == null || stops.Count == 0 || stops[0].roadWaypoints == null || stops[0].roadWaypoints.Count < 1)
                continue;
            var wps = stops[0].roadWaypoints;
            Vector3 stopPos = wps[wps.Count - 1].transform.position;
            Vector3 dir = tl.transform.position - stopPos; dir.y = 0f; dir = dir.normalized;
            Vector3 left = -Vector3.Cross(Vector3.up, dir).normalized;
            sb.AppendLine($"--- {tl.gameObject.name} stop {stopPos:F1}");
            foreach (float d in new[] { 0f, 1.2f, 2f, 3f, 4f, 5f })
            {
                Vector3 probe = stopPos + left * d + Vector3.up * 3f;
                if (Physics.Raycast(probe, Vector3.down, out RaycastHit h, 12f, ~0, QueryTriggerInteraction.Ignore))
                    sb.AppendLine($"  d={d:F1}: {GetPath(h.collider.transform)}  layer={h.collider.gameObject.layer}({LayerMask.LayerToName(h.collider.gameObject.layer)})  y={h.point.y:F2}");
                else
                    sb.AppendLine($"  d={d:F1}: NO HIT");
            }
            sampled++;
        }
        WriteResult("probehit:\n" + sb);
#endif
    }

    static void FixLightPoles()
    {
#if GLEY_TRAFFIC_SYSTEM
        var matR = MakeUnlit(new Color(0.95f, 0.15f, 0.12f));
        var matY = MakeUnlit(new Color(0.95f, 0.75f, 0.10f));
        var matG = MakeUnlit(new Color(0.15f, 0.90f, 0.25f));
        var matPole = MakeUnlit(new Color(0.15f, 0.15f, 0.17f));
        int lights = 0, heads = 0;
        var byNode = LoadNetByNode();
        foreach (var tl in Object.FindObjectsByType<Gley.TrafficSystem.TrafficLightsIntersectionSettings>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var go = tl.gameObject;
            for (int c = go.transform.childCount - 1; c >= 0; c--)
            {
                var ch = go.transform.GetChild(c);
                if (ch.name.StartsWith("LightHead_")) Object.DestroyImmediate(ch.gameObject);
            }
            heads += BuildHeadsPerArm(go, tl.stopWaypoints, ParseNodeId(go.name), byNode, matR, matY, matG, matPole);
            lights++;
        }
        var wpConverter = new Gley.TrafficSystem.Editor.TrafficWaypointsConverter();
        wpConverter.ConvertWaypoints();
        new Gley.TrafficSystem.Editor.IntersectionConverter(wpConverter, null).ConvertAllIntersections();
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"fixlightpoles: rebuilt {heads} kerb-aware light heads on {lights} junctions. Scene {(saved ? "SAVED" : "NOT saved")}.");
#else
        WriteResult("fixlightpoles: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    static long ParseNodeId(string goName)
    {
        var digits = new string(goName.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out long n) ? n : -1;
    }

    static Material MakeUnlit(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(sh) { color = c };
        return m;
    }

    static void SetDensity(int n)
    {
#if GLEY_TRAFFIC_SYSTEM
        var traffic = Object.FindFirstObjectByType<Gley.TrafficSystem.TrafficComponent>(FindObjectsInactive.Include);
        if (traffic == null) { WriteResult("density: TrafficComponent not found."); return; }
        int old = traffic.nrOfVehicles;
        traffic.nrOfVehicles = n;
        EditorUtility.SetDirty(traffic.gameObject);
        EditorSceneManager.MarkSceneDirty(traffic.gameObject.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"density: nrOfVehicles {old} -> {n}. Scene {(saved ? "SAVED" : "NOT saved")}.");
#else
        WriteResult("density: GLEY_TRAFFIC_SYSTEM not defined.");
#endif
    }

    static void RebuildSurvey()
    {
        var old = GameObject.Find("SurveyCanvas_v3") ?? GameObject.Find("SurveyCanvas_v2");
        if (old != null) Object.DestroyImmediate(old);
        bool ok = EditorApplication.ExecuteMenuItem("Tools/Sustainable Driving/Build Survey Panel");
        var q = Object.FindFirstObjectByType<SimpleStudyQuestionnaire>(FindObjectsInactive.Include);
        string wiring = q == null ? "questionnaire MISSING"
            : $"questionnaire on {GetPath(q.transform)}, panelRoot={(q.panelRoot != null ? q.panelRoot.name : "NULL")}";
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"rebuildsurvey: old canvas deleted, menu invoked={ok}; {wiring}. Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void Cleanup()
    {
        var sb = new System.Text.StringBuilder();
        string[] dead = { "AutoDriver Route Waypoints", "Car Camera" };
        foreach (var name in dead)
        {
            var go = UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                .GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (go != null) { Object.DestroyImmediate(go); sb.AppendLine("deleted: " + name); }
            else sb.AppendLine("not found (already gone): " + name);
        }
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult("cleanup:\n" + sb + $"Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void BoState()
    {
        var sb = new System.Text.StringBuilder("bostate: ");
        var managers = Object.FindObjectsByType<BOforUnity.BoForUnityManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.Append($"managers={managers.Length} ");
        foreach (var bo in managers)
        {
            sb.Append($"[go='{bo.gameObject.name}' active={bo.gameObject.activeInHierarchy} enabled={bo.enabled} ");
            var wait = typeof(BOforUnity.BoForUnityManager)
                .GetField("_waitingForPythonProcess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            sb.Append($"waitingForPython={(wait != null ? wait.GetValue(bo) : "?")} ");
            var ps = bo.gameObject.GetComponent<BOforUnity.Scripts.PythonStarter>();
            sb.Append(ps == null ? "pythonStarter=NULL " :
                $"psEnabled={ps.enabled} psRunning={ps.isPythonProcessRunning} psStarted={ps.isSystemStarted} ");
            var mgrPs = typeof(BOforUnity.BoForUnityManager).GetField("pythonStarter");
            var mgrPsVal = mgrPs != null ? mgrPs.GetValue(bo) : null;
            sb.Append($"mgrPsRef={(mgrPsVal == null ? "NULL" : (ReferenceEquals(mgrPsVal, ps) ? "same" : "DIFFERENT!"))} ");
            var sn = bo.gameObject.GetComponent<BOforUnity.Scripts.SocketNetwork>();
            sb.Append(sn == null ? "socketNetwork=NULL] " : $"snEnabled={sn.enabled}] ");
        }
        sb.Append($"isPlaying={Application.isPlaying} isPaused={EditorApplication.isPaused}");
        WriteResult(sb.ToString());
    }

    static void Unpause()
    {
        bool wasPaused = EditorApplication.isPaused;
        EditorApplication.isPaused = false;

        string errorPauseNote = "errorPause=unknown";
        try
        {
            var cw = typeof(Editor).Assembly.GetType("UnityEditor.ConsoleWindow");
            var flagsT = cw?.GetNestedType("ConsoleFlags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var setFlag = cw?.GetMethod("SetFlag", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var hasFlag = cw?.GetMethod("HasFlag", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (flagsT != null && setFlag != null)
            {
                object errorPause = System.Enum.Parse(flagsT, "ErrorPause");
                bool was = hasFlag != null && (bool)hasFlag.Invoke(null, new[] { errorPause });
                setFlag.Invoke(null, new[] { errorPause, (object)false });
                errorPauseNote = $"errorPause was={was} -> OFF";
            }
        }
        catch (System.Exception e) { errorPauseNote = "errorPause reflection failed: " + e.Message; }

        WriteResult($"unpause: wasPaused={wasPaused} -> false; {errorPauseNote}");
    }

    static void MirrorCam(string arg)
    {
        var camGo = GameObject.Find("RearMirrorCamera");
        if (camGo == null) { WriteResult("mirrorcam: RearMirrorCamera not found (enter Play first)."); return; }
        var p = arg.Split(' ');
        if (p.Length < 3) { WriteResult("mirrorcam: need 'x y z [pitch]'."); return; }
        var pos = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
        camGo.transform.localPosition = pos;
        if (p.Length > 3)
            camGo.transform.localRotation = Quaternion.Euler(float.Parse(p[3]), 180f, 0f);
        WriteResult($"mirrorcam: localPos={pos} euler={camGo.transform.localEulerAngles}");
    }

    static void DrawFinal()
    {
        string file = TrafficNet("route_proposals.txt");
        if (!File.Exists(file)) { WriteResult("drawfinal: proposals file missing: " + file); return; }

        var oldH = GameObject.Find("RouteOverlay proposals");
        if (oldH != null) Object.DestroyImmediate(oldH);
        var holder = new GameObject("RouteOverlay proposals");
        var giz = holder.AddComponent<RouteOverlayGizmos>();

        Color[] cols = { Color.red, Color.yellow, Color.green, Color.cyan, Color.magenta,
                         new Color(1f, 0.55f, 0f), Color.white, new Color(0.55f, 0.3f, 1f) };
        int groundMask = (1 << 30) | (1 << 15);
        Bounds frame = new Bounds(); bool first = true;
        var summary = new System.Text.StringBuilder();
        int idx = 0;

        foreach (var line in File.ReadAllLines(file))
        {
            var parts = line.Split('|');
            if (parts.Length < 4) continue;
            string label = parts[0];
            var ptStrs = parts[3].Split(';');
            var v3 = new Vector3[ptStrs.Length];
            for (int i = 0; i < ptStrs.Length; i++)
            {
                var xy = ptStrs[i].Split(',');
                float x = float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture);
                float y = 62f;
                if (Physics.Raycast(new Vector3(x, 300f, z), Vector3.down, out RaycastHit hit, 600f, groundMask))
                    y = hit.point.y;
                v3[i] = new Vector3(x, y + 1.0f, z);
                if (first) { frame = new Bounds(v3[i], Vector3.zero); first = false; } else frame.Encapsulate(v3[i]);
            }

            giz.lines.Add(new RouteOverlayGizmos.RouteLine
            {
                label = label,
                color = cols[idx % cols.Length],
                pts = v3,
                labelPos = v3[v3.Length / 2] + Vector3.up * 8f,
            });
            summary.Append($"{label}({parts[1]}m) ");
            idx++;
        }
        EditorUtility.SetDirty(holder);
        EditorSceneManager.MarkSceneDirty(holder.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();

        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.rotation = Quaternion.Euler(90f, 0f, 0f);
            sv.orthographic = true;
            frame.Expand(80f);
            sv.Frame(frame, false);
        }
        WriteResult($"drawfinal: {idx} proposal routes drawn as Scene-view gizmos (invisible in Play): {summary}" +
                    $"Scene {(saved ? "SAVED" : "NOT saved")}. ('drawroutes off' clears everything)");
    }

    static void WhiteScan()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Tools.visibleLayers = {Tools.visibleLayers:X8} " +
                      $"(MapRoad bit {(LayerMask.NameToLayer("Map Road") >= 0 ? ((Tools.visibleLayers >> LayerMask.NameToLayer("Map Road")) & 1).ToString() : "n/a")}, " +
                      $"MapSurface bit {(LayerMask.NameToLayer("Map Surface") >= 0 ? ((Tools.visibleLayers >> LayerMask.NameToLayer("Map Surface")) & 1).ToString() : "n/a")})");
        var groups = new Dictionary<string, (int n, float y, string layer, string mat, bool hidden)>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            float cy = r.bounds.center.y;
            if (cy < 100f || cy > 400f) continue;
            Transform top = r.transform;
            while (top.parent != null && top.parent.parent != null) top = top.parent;
            string key = GetPath(top);
            string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "NULL";
            bool hid = SceneVisibilityManager.instance.IsHidden(r.gameObject);
            if (groups.TryGetValue(key, out var g))
                groups[key] = (g.n + 1, g.y, g.layer, g.mat, g.hidden && hid);
            else
                groups[key] = (1, cy, LayerMask.LayerToName(r.gameObject.layer), mat, hid);
        }
        sb.AppendLine($"renderer groups with bounds-centre y in [100,400]: {groups.Count}");
        foreach (var kv in groups)
            sb.AppendLine($"  {kv.Key}  n={kv.Value.n}  y≈{kv.Value.y:F0}  layer={kv.Value.layer}  mat={kv.Value.mat}  sceneHidden={kv.Value.hidden}");
        WriteResult("whitescan:\n" + sb);
    }

    static void FarScan()
    {
        var sb = new System.Text.StringBuilder();
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Vector3 p = t.position;
            if (Mathf.Abs(p.x) > 1e5f || Mathf.Abs(p.y) > 1e5f || Mathf.Abs(p.z) > 1e5f)
            {
                if (n++ < 20) sb.AppendLine($"  {GetPath(t)}  pos=({p.x:E2},{p.y:E2},{p.z:E2})");
            }
        }
        var sv = SceneView.lastActiveSceneView;
        string cam = sv == null ? "no scene view" :
            $"near={sv.camera.nearClipPlane:F2} far={sv.camera.farClipPlane:F0} camPos={sv.camera.transform.position} pivot={sv.pivot} size={sv.size:F1}";
        WriteResult($"farscan: {n} transforms beyond 1e5 m\n{sb}scene camera: {cam}");
    }

    static void CleanScene()
    {
        var sb = new System.Text.StringBuilder("cleanscene hidden:\n");
        var vis = SceneVisibilityManager.instance;
        int n = 0;

        void Hide(GameObject go, string why)
        {
            if (go == null) return;
            vis.Hide(go, true);
            sb.AppendLine($"  {GetPath(go.transform)}  layer={LayerMask.LayerToName(go.layer)}  ({why})");
            n++;
        }

        var cg = GameObject.Find("CityGen3D");
        int rOff = 0;
        if (cg != null)
            foreach (Transform c in cg.transform)
                if (c.name.StartsWith("Map"))
                {
                    foreach (var rr in c.GetComponentsInChildren<Renderer>(true))
                        if (rr.enabled) { rr.enabled = false; rOff++; }
                    sb.AppendLine($"  {GetPath(c)}: renderers disabled");
                    n++;
                }
        sb.AppendLine($"  ({rOff} renderers switched off under CityGen3D/Map*)");
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

        Hide(GameObject.Find("SurveyCanvas_v3"), "world-space survey canvas");
        Hide(GameObject.Find("TargetMarkers Canvas"), "marker canvas");

        WriteResult(n == 0 ? "cleanscene: nothing found to hide?!" : sb.ToString()
                    + "(view-only: Hierarchy 眼睛图标可随时恢�? 'drawroutes' 的路线标记不受影�?");
    }

    static void DrawRoutes(string arg)
    {
        if (arg.Trim().ToLowerInvariant() == "off")
        {
            int removed = 0;
            foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (g != null && g.name.StartsWith("RouteOverlay")) { Object.DestroyImmediate(g); removed++; }
            WriteResult($"drawroutes: {removed} overlay holder(s) removed.");
            return;
        }

        int mapRoad = LayerMask.NameToLayer("Map Road");
        int mapSurf = LayerMask.NameToLayer("Map Surface");
        if (mapRoad >= 0) Tools.visibleLayers &= ~(1 << mapRoad);
        if (mapSurf >= 0) Tools.visibleLayers &= ~(1 << mapSurf);

        var rs = Object.FindFirstObjectByType<RouteSet>(FindObjectsInactive.Include);
        if (rs == null) { WriteResult("drawroutes: no RouteSet."); return; }
        long wantNode = rs.startNode;
        if (long.TryParse(arg.Trim(), out long argNode)) wantNode = argNode;
        var routeList = new List<Vector2[]>();
        var labels = new List<string>();
        foreach (var opt in RouteCandidatesData.Load(rs.jsonPath))
        {
            if (opt.startNode != wantNode) continue;
            foreach (var r in opt.routes)
            {
                routeList.Add(r.pts.ToArray());
                labels.Add($"R{r.routeIndex} {r.lengthM:F0}m {r.streets}");
            }
        }
        if (routeList.Count == 0) { WriteResult("drawroutes: no routes for startNode " + wantNode); return; }

        string holderName = $"RouteOverlay {wantNode}";
        var oldHolder = GameObject.Find(holderName);
        if (oldHolder != null) Object.DestroyImmediate(oldHolder);
        bool isActiveSet = wantNode == rs.startNode;
        Color[] cols = isActiveSet
            ? new[] { Color.red, Color.yellow, Color.green, Color.cyan }
            : new[] { new Color(0.2f, 0.5f, 1f), Color.magenta, new Color(1f, 0.55f, 0f), Color.white };
        var holder = new GameObject(holderName);
        var giz = holder.AddComponent<RouteOverlayGizmos>();
        int groundMask = (1 << 30) | (1 << 15);
        Bounds frame = new Bounds();
        bool first = true;

        for (int r = 0; r < routeList.Count; r++)
        {
            var pts = routeList[r];
            var v3 = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                float y = 62f;
                if (Physics.Raycast(new Vector3(pts[i].x, 300f, pts[i].y), Vector3.down, out RaycastHit hit, 600f, groundMask))
                    y = hit.point.y;
                v3[i] = new Vector3(pts[i].x, y + 1.0f + r * 0.4f, pts[i].y);
                if (first) { frame = new Bounds(v3[i], Vector3.zero); first = false; } else frame.Encapsulate(v3[i]);
            }
            giz.lines.Add(new RouteOverlayGizmos.RouteLine
            {
                label = $"R{r + 1}",
                color = cols[r % cols.Length],
                pts = v3,
                labelPos = v3[v3.Length - 1] + Vector3.up * 6f,
            });
        }

        var p0 = routeList[0][0];
        float sy = 62f;
        if (Physics.Raycast(new Vector3(p0.x, 300f, p0.y), Vector3.down, out RaycastHit sh, 600f, groundMask)) sy = sh.point.y;
        giz.drawSharedStart = true;
        giz.sharedStartPos = new Vector3(p0.x, sy + 2f, p0.y);
        EditorUtility.SetDirty(holder);
        EditorSceneManager.MarkSceneDirty(holder.scene);
        bool savedScene = EditorSceneManager.SaveOpenScenes();

        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.rotation = Quaternion.Euler(90f, 0f, 0f);
            sv.orthographic = true;
            frame.Expand(60f);
            sv.Frame(frame, false);
        }

        WriteResult($"drawroutes: {routeList.Count} routes drawn as Scene-view gizmos, invisible in Play ({string.Join("; ", labels)}), " +
                    $"CityGen map layers hidden from Scene view, camera framed top-down. Scene {(savedScene ? "SAVED" : "NOT saved")}. 'drawroutes off' to remove.");
    }

    static void WhatIs(string arg)
    {
        var p = arg.Split(' ');
        if (p.Length < 2) { WriteResult("whatis: need 'x z [radius]'."); return; }
        float x = float.Parse(p[0]), z = float.Parse(p[1]);
        float rad = p.Length > 2 ? float.Parse(p[2]) : 4f;

        var sb = new System.Text.StringBuilder($"whatis ({x},{z}) r={rad} column:\n");
        var hits = Physics.RaycastAll(new Vector3(x, 400f, z), Vector3.down, 800f, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => b.point.y.CompareTo(a.point.y));
        foreach (var h in hits)
            sb.AppendLine($"  y={h.point.y,7:F1}  {GetPath(h.transform)} layer={LayerMask.LayerToName(h.collider.gameObject.layer)}");

        float y = 60f;
        int streetMask = (1 << 30) | (1 << 15);
        if (Physics.Raycast(new Vector3(x, 300f, z), Vector3.down, out RaycastHit ground, 600f, streetMask))
            y = ground.point.y;
        sb.AppendLine($"  streetY={y:F1}; renderers near street level:");

        var seen = new System.Collections.Generic.HashSet<Renderer>();
        foreach (var col in Physics.OverlapSphere(new Vector3(x, y + 1f, z), rad, ~0, QueryTriggerInteraction.Collide))
        {
            foreach (var r in col.GetComponentsInParent<Renderer>())
                seen.Add(r);
        }
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var b = r.bounds;
            Vector3 q = b.ClosestPoint(new Vector3(x, y + 1f, z));
            if ((q - new Vector3(x, y + 1f, z)).sqrMagnitude <= rad * rad) seen.Add(r);
        }
        foreach (var r in seen)
        {
            var mats = r.sharedMaterials;
            string mn = mats == null || mats.Length == 0 ? "NO MATERIAL" :
                string.Join("|", System.Linq.Enumerable.Select(mats, m => m == null ? "NULL" : $"{m.name}<{(m.shader != null ? m.shader.name : "no shader")}>"));
            sb.AppendLine($"  {GetPath(r.transform)} [{r.GetType().Name}] layer={LayerMask.LayerToName(r.gameObject.layer)} " +
                          $"enabled={r.enabled} bounds=({r.bounds.size.x:F1},{r.bounds.size.y:F1},{r.bounds.size.z:F1}) mat={mn}");
        }
        WriteResult(sb.ToString());
    }

    static void RouteShots()
    {
        var rs = Object.FindFirstObjectByType<RouteSet>(FindObjectsInactive.Include);
        if (rs == null) { WriteResult("routeshots: no RouteSet component in the scene."); return; }
        var routeList = new List<Vector2[]>();
        foreach (var opt in RouteCandidatesData.Load(rs.jsonPath))
        {
            if (opt.startNode != rs.startNode) continue;
            foreach (var r in opt.routes) routeList.Add(r.pts.ToArray());
        }
        if (routeList.Count == 0) { WriteResult($"routeshots: no routes for startNode {rs.startNode}."); return; }

        string dir = Path.GetFullPath(Application.dataPath + "/../RouteShots");
        Directory.CreateDirectory(dir);

        var go = new GameObject("RouteShotCam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 76f;
        cam.nearClipPlane = 0.2f;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);

        int shots = 0;
        int groundMask = (1 << 30) | (1 << 15);
        for (int r = 0; r < routeList.Count; r++)
        {
            var pts = routeList[r];
            float acc = 0f;
            for (int i = 1; i < pts.Length; i++)
            {
                acc += (pts[i] - pts[i - 1]).magnitude;
                if (acc < 20f && i != 1) continue;
                acc = 0f;
                Vector2 p = pts[i - 1];
                Vector2 dirXZ = (pts[i] - pts[i - 1]).normalized;
                float y = 60f;
                if (Physics.Raycast(new Vector3(p.x, 300f, p.y), Vector3.down, out RaycastHit hit, 600f, groundMask))
                    y = hit.point.y;
                go.transform.position = new Vector3(p.x, y + 1.4f, p.y);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(dirXZ.x, 0f, dirXZ.y), Vector3.up);

                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"R{r + 1}_{i:D3}.png"), tex.EncodeToPNG());
                shots++;
            }
        }
        Object.DestroyImmediate(go);
        rt.Release();
        WriteResult($"routeshots: {shots} frames -> {dir}");
    }

    static void CarState()
    {
        var ctl = Object.FindFirstObjectByType<CarController>();
        if (ctl == null) { WriteResult("carstate: no CarController."); return; }
        var t = ctl.transform;
        var rb = ctl.GetComponent<Rigidbody>();
        var box = ctl.GetComponent<BoxCollider>();
        var sb = new System.Text.StringBuilder("carstate: ");
        sb.Append($"pos={t.position:F2} yaw={t.eulerAngles.y:F0} pitch={t.eulerAngles.x:F1} ");
        if (rb != null) sb.Append($"vel={rb.linearVelocity.magnitude:F2}m/s kinematic={rb.isKinematic} constraints={rb.constraints} ");
        sb.Append($"throttle={ctl.throttleInput:F2} steer={ctl.steerInput:F2} speed={ctl.currentSpeed:F1}km/h\n");
        if (box != null)
        {
            Vector3 c = t.TransformPoint(box.center);
            Vector3 half = Vector3.Scale(box.size, t.lossyScale) * 0.5f + Vector3.one * 0.05f;
            var hits = Physics.OverlapBox(c, half, t.rotation, ~0, QueryTriggerInteraction.Ignore);
            sb.AppendLine($"touching (box+5cm, {hits.Length} incl. self):");
            foreach (var h in hits)
            {
                if (h.transform.root == t.root) continue;
                Vector3 cp = h.ClosestPoint(c);
                Vector3 lp = t.InverseTransformPoint(cp);
                sb.AppendLine($"  {GetPath(h.transform)} [{h.GetType().Name}] layer={LayerMask.LayerToName(h.gameObject.layer)} " +
                              $"closest car-local=({lp.x:F2},{lp.y:F2},{lp.z:F2})");
            }
        }
        WriteResult(sb.ToString());
    }

    static void AutoDrive(string arg)
    {
        var rc = Object.FindFirstObjectByType<RoundController>(FindObjectsInactive.Include);
        if (rc == null) { WriteResult("autodrive: no RoundController."); return; }
        bool on = arg.Trim().ToLowerInvariant() is "on" or "1" or "true";
        rc.autoDrive = on;
        if (Application.isPlaying && rc.autopilot != null && rc.phase == RoundController.Phase.Driving)
            rc.autopilot.engaged = on;
        if (!Application.isPlaying) EditorUtility.SetDirty(rc);
        WriteResult($"autodrive: {(on ? "ON" : "OFF")} (phase={rc.phase}, playing={Application.isPlaying})");
    }

    static void MirrorScan()
    {
        const string FbxPath = "Assets/RealisticMobileCars - Pro 3D Models/Vehicles/RMCar05/Meshes/RMCar05.FBX";
        var all = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
        if (all == null || all.Length == 0) { WriteResult("mirrorscan: FBX not found at " + FbxPath); return; }

        var sb = new System.Text.StringBuilder("mirrorscan (submesh clusters | mat | centroid | avgNormal | size):\n");
        foreach (var obj in all)
        {
            var go = obj as GameObject;
            if (go == null || !go.name.EndsWith("_LOD0")) continue;
            var mr = go.GetComponent<MeshRenderer>();
            var mf = go.GetComponent<MeshFilter>();
            if (mr == null || mf == null || mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;
            var norms = mesh.normals;
            var mats = mr.sharedMaterials;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string mn = s < mats.Length && mats[s] != null ? mats[s].name : "?";
                var tris = mesh.GetTriangles(s);

                int nT = tris.Length / 3;
                int[] parent = new int[nT];
                for (int i = 0; i < nT; i++) parent[i] = i;
                System.Func<int, int> find = i =>
                {
                    while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                    return i;
                };
                var vertOwner = new System.Collections.Generic.Dictionary<int, int>();
                for (int t = 0; t < nT; t++)
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = tris[t * 3 + k];
                        if (vertOwner.TryGetValue(vi, out int o)) { int a = find(o), b = find(t); if (a != b) parent[a] = b; }
                        else vertOwner[vi] = t;
                    }
                var clusters = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
                for (int t = 0; t < nT; t++)
                {
                    int root = find(t);
                    if (!clusters.TryGetValue(root, out var lst)) clusters[root] = lst = new();
                    lst.Add(t);
                }

                foreach (var kv in clusters)
                {
                    if (kv.Value.Count < 8) continue;
                    Vector3 c = Vector3.zero, n = Vector3.zero,
                            mn3 = new(9e9f, 9e9f, 9e9f), mx3 = new(-9e9f, -9e9f, -9e9f);
                    int cnt = 0;
                    foreach (int t in kv.Value)
                        for (int k = 0; k < 3; k++)
                        {
                            int vi = tris[t * 3 + k];
                            Vector3 wv = go.transform.TransformPoint(verts[vi]);   // node transform applied
                            c += wv; cnt++;
                            if (norms != null && vi < norms.Length) n += go.transform.TransformDirection(norms[vi]);
                            mn3 = Vector3.Min(mn3, wv); mx3 = Vector3.Max(mx3, wv);
                        }
                    c /= cnt; n = n.normalized;
                    Vector3 sz = mx3 - mn3;

                    bool interiorBox = Mathf.Abs(c.x) < 0.35f && c.y > 1.15f && c.y < 1.65f && c.z > 0.1f && c.z < 0.9f;
                    bool doorBox = Mathf.Abs(c.x) > 0.6f && Mathf.Abs(c.x) < 1.25f && c.y > 0.75f && c.y < 1.35f && c.z > 0.2f && c.z < 1.3f;
                    bool glassy = mn.Contains("Glass");
                    if (!interiorBox && !doorBox && !glassy) continue;

                    sb.AppendLine($"  {go.name}[{s}] {mn} {(interiorBox ? "INT" : doorBox ? "DOOR" : "glass")} tris={kv.Value.Count} | ({c.x:F3},{c.y:F3},{c.z:F3}) | ({n.x:F2},{n.y:F2},{n.z:F2}) | ({sz.x:F2},{sz.y:F2},{sz.z:F2})");
                }
            }
        }
        WriteResult(sb.ToString());
    }

    static void MirrorMesh(string arg)
    {
        var drv = Object.FindFirstObjectByType<AutoDriver>();
        if (drv == null) { WriteResult("mirrormesh: no AutoDriver."); return; }
        Transform car = drv.transform;

        if (arg.StartsWith("off "))
        {
            string name = arg.Substring(4).Trim();
            int n = 0;
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
                if (r.gameObject.name == name && r.gameObject.name != "RearMirrorGlass") { r.enabled = false; n++; }
            WriteResult($"mirrormesh: disabled {n} renderer(s) named '{name}'.");
            return;
        }

        if (arg == "subs")
        {
            var sb2 = new System.Text.StringBuilder("mirrormesh subs (renderer/mat | localCenter | size):\n");
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.gameObject.name.Contains("LOD0")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var smb = mesh.GetSubMesh(i).bounds;   // mesh-local
                    Vector3 wc = r.transform.TransformPoint(smb.center);
                    Vector3 lc = car.InverseTransformPoint(wc);
                    Vector3 ws = Vector3.Scale(smb.size, r.transform.lossyScale);
                    string mn = i < mats.Length && mats[i] != null ? mats[i].name : "?";
                    sb2.AppendLine($"  {r.gameObject.name}[{i}] {mn} | ({lc.x:F2},{lc.y:F2},{lc.z:F2}) | ({ws.x:F2},{ws.y:F2},{ws.z:F2})");
                }
            }
            WriteResult(sb2.ToString());
            return;
        }

        var sb = new System.Text.StringBuilder("mirrormesh candidates (name | localPos | size):\n");
        foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.gameObject.name == "RearMirrorGlass") continue;
            Vector3 lp = car.InverseTransformPoint(r.bounds.center);
            Vector3 sz = r.bounds.size;
            bool near = lp.y > 1.0f && lp.y < 1.7f && lp.z > 0.1f && lp.z < 1.1f && Mathf.Abs(lp.x) < 0.6f;
            bool small = sz.magnitude < 1.2f;
            if (near && small)
                sb.AppendLine($"  {r.gameObject.name} | ({lp.x:F2},{lp.y:F2},{lp.z:F2}) | ({sz.x:F2},{sz.y:F2},{sz.z:F2})");
        }
        WriteResult(sb.ToString());
    }

    static void MirrorGlass(string arg)
    {
        var glass = GameObject.Find("RearMirrorGlass");
        if (glass == null) { WriteResult("mirrorglass: RearMirrorGlass not found (enter Play first)."); return; }
        var p = arg.Split(' ');
        if (p.Length < 3) { WriteResult("mirrorglass: need 'x y z [pitch yaw [w h]]'."); return; }
        glass.transform.localPosition = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
        if (p.Length > 4)
            glass.transform.localRotation = Quaternion.Euler(float.Parse(p[3]), float.Parse(p[4]), 0f);
        if (p.Length > 6)
            glass.transform.localScale = new Vector3(float.Parse(p[5]), float.Parse(p[6]), 1f);
        WriteResult($"mirrorglass: pos={glass.transform.localPosition} euler={glass.transform.localEulerAngles} scale={glass.transform.localScale}");
    }

    static void MirrorInfo()
    {
        var sb = new System.Text.StringBuilder("mirrorinfo: ");
        var glass = GameObject.Find("RearMirrorGlass");
        var camGo = GameObject.Find("RearMirrorCamera");
        sb.Append($"glass={(glass != null)} cam={(camGo != null)} ");
        if (glass != null)
        {
            var mr = glass.GetComponent<MeshRenderer>();
            var mat = mr != null ? mr.sharedMaterial : null;
            sb.Append($"glassPos={glass.transform.localPosition} shader={(mat != null && mat.shader != null ? mat.shader.name : "NULL")} ");
            sb.Append($"tex={(mat != null && mat.mainTexture != null ? mat.mainTexture.name : "NULL")} rendererEnabled={mr != null && mr.enabled} ");
        }
        if (camGo != null)
        {
            var c = camGo.GetComponent<Camera>();
            sb.Append($"camEnabled={c != null && c.enabled} rt={(c != null && c.targetTexture != null ? "set" : "NULL")} ");
            if (c != null && c.targetTexture != null)
            {
                var rt = c.targetTexture;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(16, 16, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(rt.width / 2 - 8, rt.height / 2 - 8, 16, 16), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Color avg = Color.black;
                var px = tex.GetPixels();
                foreach (var p in px) avg += p;
                avg /= px.Length;
                Object.DestroyImmediate(tex);
                sb.Append($"rtAvgRGB=({avg.r:F3},{avg.g:F3},{avg.b:F3}) ");
            }
        }
        sb.Append($"isPlaying={Application.isPlaying}");
        WriteResult(sb.ToString());
    }

    static void FixCam(string arg)
    {
        var drv = Object.FindFirstObjectByType<AutoDriver>(FindObjectsInactive.Include);
        var camT = drv != null ? drv.transform.Find("DriverCamera") : null;
        if (camT == null) { WriteResult("fixcam: DriverCamera not found."); return; }
        var parts = arg.Split(' ');
        float y = parts.Length > 0 && float.TryParse(parts[0], out float py) ? py : 1.38f;
        float z = parts.Length > 1 && float.TryParse(parts[1], out float pz) ? pz : 0.22f;
        float fov = parts.Length > 2 && float.TryParse(parts[2], out float pf) ? pf : 76f;
        camT.localPosition = new Vector3(-0.37f, y, z);
        var cam = camT.GetComponent<Camera>();
        if (cam != null) cam.fieldOfView = fov;
        EditorSceneManager.MarkSceneDirty(camT.gameObject.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"fixcam: DriverCamera localPosition = {camT.localPosition}, fov = {fov}. Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void CleanMissing()
    {
        int objs = 0, removed = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.GetComponents<Component>().Any(c => c == null)) continue;
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            objs++;
        }
        EditorSceneManager.MarkAllScenesDirty();
        bool saved = EditorSceneManager.SaveOpenScenes();
        WriteResult($"cleanmiss: removed {removed} missing-script stubs from {objs} objects. Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void PinRoute(long startNode)
    {
        var opt = RouteCandidatesData.Load(RoutesJson).FirstOrDefault(o => o.startNode == startNode);
        if (opt == null) { WriteResult($"pinroute: startNode {startNode} not in JSON."); return; }
        var rs = Object.FindFirstObjectByType<RouteSet>(FindObjectsInactive.Include);
        if (rs == null) { WriteResult("pinroute: no RouteSet in scene."); return; }
        rs.startNode = startNode;
        EditorUtility.SetDirty(rs);
        EditorSceneManager.MarkSceneDirty(rs.gameObject.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        var labels = string.Join("; ", opt.routes.Select(r => $"R{r.routeIndex} {r.lengthM:F0}m {r.streets}"));
        WriteResult($"pinroute: RouteSet.startNode = {startNode} ({opt.routes.Count} routes: {labels}). " +
                    $"Scene {(saved ? "SAVED" : "NOT saved")}.");
    }

    static void ImportCar()
    {
        if (!File.Exists(CarPackage)) { WriteResult("importcar: package not found: " + CarPackage); return; }
        AssetDatabase.importPackageCompleted += OnPkgDone;
        AssetDatabase.importPackageFailed += OnPkgFail;
        AssetDatabase.ImportPackage(CarPackage, false);   // non-interactive
    }

    static void OnPkgDone(string pkg)
    {
        AssetDatabase.importPackageCompleted -= OnPkgDone;
        AssetDatabase.importPackageFailed -= OnPkgFail;
        var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.ToLowerInvariant().Contains("car 05") || p.ToLowerInvariant().Contains("realistic"))
            .ToArray();
        WriteResult("importcar DONE: " + pkg + "\nprefabs found:\n" + string.Join("\n", prefabs));
    }

    static void FixCarMats()
    {
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) { WriteResult("fixcarmat: URP Lit shader not found."); return; }
        var paths = AssetDatabase.FindAssets("t:Material", new[] { "Assets/RealisticMobileCars - Pro 3D Models" })
            .Select(AssetDatabase.GUIDToAssetPath).ToArray();
        var sb = new System.Text.StringBuilder();
        int converted = 0;
        foreach (var p in paths)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            string sn = m.shader != null ? m.shader.name : "";
            if (sn.StartsWith("Universal Render Pipeline")) { sb.AppendLine($"skip (already URP): {m.name}"); continue; }

            Texture albedo = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            Texture metal = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
            float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            float smooth = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
            Texture occ = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;
            Texture emisMap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
            Color emisCol = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            bool emission = m.IsKeywordEnabled("_EMISSION");
            float mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f;
            float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
            bool transparent = mode >= 2f || m.renderQueue >= 3000;

            m.shader = urpLit;
            if (albedo != null) m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", color);
            if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
            if (metal != null) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (occ != null) { m.SetTexture("_OcclusionMap", occ); m.EnableKeyword("_OCCLUSIONMAP"); }
            if (emission)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (emisMap != null) m.SetTexture("_EmissionMap", emisMap);
                m.SetColor("_EmissionColor", emisCol);
            }
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else if (mode == 1f)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cutoff", cutoff);
            }
            EditorUtility.SetDirty(m);
            converted++;
            sb.AppendLine($"converted: {m.name} (was {sn}{(transparent ? ", transparent" : "")}{(emission ? ", emissive" : "")})");
        }
        AssetDatabase.SaveAssets();
        WriteResult($"fixcarmat: {converted}/{paths.Length} materials converted to URP/Lit\n" + sb);
    }

    static void OnPkgFail(string pkg, string err)
    {
        AssetDatabase.importPackageCompleted -= OnPkgDone;
        AssetDatabase.importPackageFailed -= OnPkgFail;
        WriteResult("importcar FAILED: " + err);
    }
}
