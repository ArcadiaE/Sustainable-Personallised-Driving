using System.IO;
using BOforUnity;
using BOforUnity.Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class StudySetup
{
    const int OptimizationIterations = 5;

    static StudySetup()
    {
        EditorApplication.delayCall += RunSilent;
        EditorApplication.update += RetryUntilSceneReady;
    }

    static double _retryNext;
    static bool _retryDone;
    static void RetryUntilSceneReady()
    {
        if (_retryDone || EditorApplication.timeSinceStartup < _retryNext) return;
        _retryNext = EditorApplication.timeSinceStartup + 3.0;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (GameObject.Find("TrafficManager") == null) return;
        _retryDone = true;
        RunSilent();
    }

    [MenuItem("Tools/Sustainable Driving/BO Setup")]
    public static void RunFromMenu() => Run(true);

    static void RunSilent() => Run(false);

    static void Run(bool manual)
    {
        string result = Configure(manual);
        Debug.Log("[StudySetup] " + result);
        try
        {
            File.WriteAllText(Path.GetFullPath(Application.dataPath + "/../bo_setup_result.txt"),
                              System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + result);
        }
        catch {  }
    }

    static string Configure(bool manual)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "SKIPPED: editor is in Play mode. Exit Play, then Tools > Study > BO Setup.";

        if (!manual && GameObject.Find("TrafficManager") == null)
            return "SKIPPED: this does not look like the Pimlico scene (no 'TrafficManager'). Open it and use Tools > Study > BO Setup.";

        bool changed = false;

        var managerGo = GameObject.Find("BoForUnityManager");
        if (managerGo == null) { managerGo = new GameObject("BoForUnityManager"); changed = true; }

        var bo = managerGo.GetComponent<BoForUnityManager>();
        if (bo == null) { bo = managerGo.AddComponent<BoForUnityManager>(); changed = true; }
        if (managerGo.GetComponent<PythonStarter>() == null) { managerGo.AddComponent<PythonStarter>(); changed = true; }
        if (managerGo.GetComponent<SocketNetwork>() == null) { managerGo.AddComponent<SocketNetwork>(); changed = true; }
        if (managerGo.GetComponent<Optimizer>() == null) { managerGo.AddComponent<Optimizer>(); changed = true; }
        if (managerGo.GetComponent<MainThreadDispatcher>() == null) { managerGo.AddComponent<MainThreadDispatcher>(); changed = true; }

        string reEnabled = "";
        foreach (var beh in managerGo.GetComponents<MonoBehaviour>())
        {
            if (beh != null && !beh.enabled)
            {
                beh.enabled = true;
                reEnabled += beh.GetType().Name + " ";
                changed = true;
            }
        }
        if (!managerGo.activeSelf) { managerGo.SetActive(true); reEnabled += "(GameObject was inactive) "; changed = true; }

        if (bo.parameters == null || bo.parameters.Count != OptimizerBridge.ParameterCount)
        {
            bo.parameters = new System.Collections.Generic.List<ParameterEntry>
            {
                new("size_leaf",     new ParameterArgs(0f, 1f)),
                new("size_score",    new ParameterArgs(0f, 1f)),
                new("size_feedback", new ParameterArgs(0f, 1f)),
                new("size_speed",    new ParameterArgs(0f, 1f)),
                new("size_accel",    new ParameterArgs(0f, 1f)),
                new("size_labels",   new ParameterArgs(0f, 1f)),
                new("opacity",       new ParameterArgs(0f, 1f)),
            };
            changed = true;
            Debug.LogWarning("[StudySetup] Parameter list changed (7 params, peer text line removed) — old LogData " +
                             "headers no longer match; stale test logs should be deleted.");
        }
        if (bo.objectives == null || bo.objectives.Count != OptimizerBridge.ObjectiveCount)
        {
            bo.objectives = new System.Collections.Generic.List<ObjectiveEntry>
            {
                // (lowerBound, upperBound, smallerIsBetter, numberOfSubMeasures)
                new("energy",      new ObjectiveArgs(0f, 150f, true,  1)),
                new("taskload",    new ObjectiveArgs(0f, 100f, true,  1)),
                new("accInformed", new ObjectiveArgs(0f, 100f, false, 1)),
                new("accPleasant", new ObjectiveArgs(0f, 100f, false, 1)),
                new("accGlance",   new ObjectiveArgs(0f, 100f, false, 1)),
            };
            changed = true;
        }
        foreach (var p in bo.parameters)
        {
            if (p.value != null && (p.value.lowerBound != 0f || p.value.upperBound != 1f))
            {
                p.value.lowerBound = 0f;
                p.value.upperBound = 1f;
                changed = true;
            }
        }

        if (bo.objectives.Count > 0 && bo.objectives[0].key == "energy"
            && bo.objectives[0].value != null && bo.objectives[0].value.upperBound != 150f)
        {
            bo.objectives[0].value.upperBound = 150f;
            changed = true;
        }

        var so = new SerializedObject(bo);
        var sampEdit = so.FindProperty("enableSamplingEdit");
        if (sampEdit != null && sampEdit.boolValue)
        {
            sampEdit.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        if (!PlayerSettings.runInBackground) { PlayerSettings.runInBackground = true; changed = true; }

        // --- 3. loop configuration ----------------------------------------------
        if (bo.iterationAdvanceMode != BoForUnityManager.IterationAdvanceMode.Automatic)
        { bo.iterationAdvanceMode = BoForUnityManager.IterationAdvanceMode.Automatic; changed = true; }
        if (bo.reloadSceneOnIterationAdvance)
        { bo.reloadSceneOnIterationAdvance = false; changed = true; }
        if (bo.numOptimizationIterations != OptimizationIterations) { bo.numOptimizationIterations = OptimizationIterations; changed = true; }
        if (!bo.enableFinalDesignRound) { bo.enableFinalDesignRound = true; changed = true; }

        // --- 4. bridge + RoundController ----------------------------------------
        var bridge = managerGo.GetComponent<BoForUnityBridge>();
        if (bridge == null) { bridge = managerGo.AddComponent<BoForUnityBridge>(); changed = true; }
        if (bridge.bo != bo) { bridge.bo = bo; changed = true; }

        var rc = Object.FindFirstObjectByType<RoundController>(FindObjectsInactive.Include);
        if (rc == null)
        {
            var sm = GameObject.Find("StudyManager") ?? new GameObject("StudyManager");
            rc = sm.AddComponent<RoundController>();
            changed = true;
        }
        if (rc.optimizer != bridge) { rc.optimizer = bridge; changed = true; }

        var rs = rc.GetComponent<RouteSet>();
        if (rs == null) { rs = rc.gameObject.AddComponent<RouteSet>(); changed = true; }
        if (rc.routeSet != rs) { rc.routeSet = rs; changed = true; }
        const string FinalRoutes = "TrafficNet/final_routes.json";
        if (rs.jsonPath != FinalRoutes) { rs.jsonPath = FinalRoutes; changed = true; }
        if (rs.startNode != 1) { rs.startNode = 1; changed = true; }

        var hudGo = GameObject.Find("EcoHUD");
        if (hudGo == null) { hudGo = new GameObject("EcoHUD"); changed = true; }
        var hud = hudGo.GetComponent<EcoFeedbackHUD>();
        if (hud == null) { hud = hudGo.AddComponent<EcoFeedbackHUD>(); changed = true; }
        if (hudGo.GetComponent<EcoHudAutoBuilder>() == null) { hudGo.AddComponent<EcoHudAutoBuilder>(); changed = true; }
        var mk = hudGo.GetComponent<TargetMarkers>();
        if (mk == null) { mk = hudGo.AddComponent<TargetMarkers>(); changed = true; }
        if (hud.markers != mk) { hud.markers = mk; changed = true; }
        if (hudGo.GetComponent<RearMirror>() == null) { hudGo.AddComponent<RearMirror>(); changed = true; }   // task 7
        if (hudGo.GetComponent<EncounterGuarantee>() == null) { hudGo.AddComponent<EncounterGuarantee>(); changed = true; }
        if (hudGo.GetComponent<SeatAnchorCheck>() == null) { hudGo.AddComponent<SeatAnchorCheck>(); changed = true; }

        var rigGo = GameObject.Find("MotionRig");
        if (rigGo == null) { rigGo = new GameObject("MotionRig"); changed = true; }
        if (rigGo.GetComponent<YawMotion>() == null) { rigGo.AddComponent<YawMotion>(); changed = true; }
        var yawM = rigGo.GetComponent<YawMotion>();
        if (yawM != null && (yawM.pitchPerAccel != 0.5f || yawM.rollPerAccel != 0.7f ||
                             yawM.maxPitchDeg != 3f || yawM.maxRollDeg != 4f ||
                             yawM.maxTiltRateDegS != 8f || yawM.inputSmoothing != 2.5f ||
                             yawM.yawAtLockDeg != 55f || yawM.maxYawDeg != 16f ||
                             yawM.maxYawRateDegS != 30f || yawM.tiltSmoothTime != 0.35f ||
                             yawM.yawSmoothTime != 0.15f || yawM.maxFrameStepS != 0.05f))
        {
            yawM.maxFrameStepS = 0.05f;
            yawM.pitchPerAccel = 0.5f;
            yawM.rollPerAccel = 0.7f;
            yawM.maxPitchDeg = 3f;
            yawM.maxRollDeg = 4f;
            yawM.maxTiltRateDegS = 8f;
            yawM.inputSmoothing = 2.5f;
            yawM.yawAtLockDeg = 55f;
            yawM.maxYawDeg = 16f;
            yawM.maxYawRateDegS = 30f;
            yawM.tiltSmoothTime = 0.35f;
            yawM.yawSmoothTime = 0.15f;
            EditorUtility.SetDirty(yawM);
            changed = true;
        }
        var yawLink = rigGo.GetComponent<YawDirectLink>();
        if (yawLink == null) { yawLink = rigGo.AddComponent<YawDirectLink>(); changed = true; }
        if (!yawLink.autoConnectOnPlay) { yawLink.autoConnectOnPlay = true; changed = true; }
        if (yawLink.yawLimitDeg != 30) { yawLink.yawLimitDeg = 30; EditorUtility.SetDirty(yawLink); changed = true; }
        if (rc.hud != hud) { rc.hud = hud; changed = true; }

        // CollisionLogger unchanged.
        var carCtl = Object.FindFirstObjectByType<CarController>(FindObjectsInactive.Include);
        if (carCtl != null)
        {
            if (carCtl.motorForce != 6500f) { carCtl.motorForce = 6500f; changed = true; }
            if (carCtl.brakeForce != 9000f) { carCtl.brakeForce = 9000f; changed = true; }
            if (carCtl.maxSpeed != 20f) { carCtl.maxSpeed = 20f; changed = true; }
            if (carCtl.drag != 0.1f) { carCtl.drag = 0.1f; changed = true; }
            if (carCtl.GetComponent<CollisionLogger>() == null) { carCtl.gameObject.AddComponent<CollisionLogger>(); changed = true; }
            if (carCtl.GetComponent<CockpitWheelSync>() == null) { carCtl.gameObject.AddComponent<CockpitWheelSync>(); changed = true; }
            if (changed) EditorUtility.SetDirty(carCtl);
        }

        var ecoSc = Object.FindFirstObjectByType<EcoScore>(FindObjectsInactive.Include);
        if (ecoSc != null && (ecoSc.targetSpeedKmh != 45f || ecoSc.sigmaLowKmh != 14f || ecoSc.sigmaHighKmh != 4.5f))
        {
            ecoSc.targetSpeedKmh = 45f;
            ecoSc.sigmaLowKmh = 14f;
            ecoSc.sigmaHighKmh = 4.5f;
            EditorUtility.SetDirty(ecoSc);
            changed = true;
        }

        // so enforce them here.
        var drv = Object.FindFirstObjectByType<AutoDriver>(FindObjectsInactive.Include);
        if (drv != null && (drv.targetSpeedKmh != 40f || drv.cornerSpeedKmh != 15f ||
                            drv.cornerBendDeg != 75f || drv.turnSlowdown != 0.5f))
        {
            drv.targetSpeedKmh = 40f;
            drv.cornerSpeedKmh = 15f;
            drv.cornerBendDeg = 75f;
            drv.turnSlowdown = 0.5f;
            EditorUtility.SetDirty(drv);
            changed = true;
        }

        // --- 5. save --------------------------------------------------------------
        if (!changed)
            return $"OK: BO study loop already configured (manager, {OptimizerBridge.ParameterCount} params, {OptimizerBridge.ObjectiveCount} objectives, bridge on RoundController). Nothing to do.";

        EditorUtility.SetDirty(bo);
        EditorUtility.SetDirty(bridge);
        EditorUtility.SetDirty(rc);
        EditorSceneManager.MarkSceneDirty(managerGo.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        string enableNote = reEnabled.Length > 0 ? $" RE-ENABLED: {reEnabled.Trim()}." : "";
        return saved
            ? $"OK: BO study loop configured (manager + {OptimizerBridge.ParameterCount} params + {OptimizerBridge.ObjectiveCount} objectives + Automatic/no-reload + budget auto(2d+1)+{OptimizationIterations}+final, bridge wired to RoundController, EcoHUD object present) and scene SAVED.{enableNote}"
            : "PARTIAL: configured, but the scene save was cancelled/failed - save manually (Ctrl+S)." + enableNote;
    }
}
