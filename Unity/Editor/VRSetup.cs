#if UNITY_EDITOR
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

[InitializeOnLoad]
public static class VRSetup
{
    static VRSetup()
    {
        EditorApplication.delayCall += RunSilent;
    }

    [MenuItem("Tools/Sustainable Driving/VR Setup")]
    public static void RunFromMenu() => Run(true);
    static void RunSilent() => Run(false);

    static void Run(bool manual)
    {
        string result = Configure(manual);
        Debug.Log("[VRSetup] " + result);
        try
        {
            File.WriteAllText(Path.GetFullPath(Application.dataPath + "/../vr_setup_result.txt"),
                              System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + result);
        }
        catch {  }
    }

    static string Configure(bool manual)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "SKIPPED: editor is in Play mode. Exit Play, then Tools > Sustainable Driving > VR Setup.";
        if (!manual && GameObject.Find("TrafficManager") == null)
            return "SKIPPED: not the study scene (no 'TrafficManager'). Open Pimlico, then Tools > Sustainable Driving > VR Setup.";

        bool changed = false;
        string xr = ConfigureXrSettings(ref changed);
        string rig = ConfigureSceneRig(ref changed);
        if (changed) EditorSceneManager.SaveOpenScenes();
        return $"{xr}; {rig}; scene {(changed ? "SAVED" : "unchanged")}";
    }

    static string ConfigureXrSettings(ref bool changed)
    {
        var group = BuildTargetGroup.Standalone;

        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            changed = true;
        }

        var settings = perTarget.SettingsForBuildTarget(group);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            settings.name = "Standalone Settings";
            perTarget.SetSettingsForBuildTarget(group, settings);
            AssetDatabase.AddObjectToAsset(settings, perTarget);
            changed = true;
        }
        if (settings.Manager == null)
        {
            var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            manager.name = "Standalone Providers";
            settings.Manager = manager;
            AssetDatabase.AddObjectToAsset(manager, perTarget);
            changed = true;
        }
        if (!settings.InitManagerOnStart) { settings.InitManagerOnStart = true; changed = true; }

        bool hasOpenXr = false;
        foreach (var l in settings.Manager.activeLoaders)
            if (l != null && l.GetType().FullName == "UnityEngine.XR.OpenXR.OpenXRLoader") hasOpenXr = true;
        if (!hasOpenXr)
        {
            XRPackageMetadataStore.AssignLoader(settings.Manager,
                "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            changed = true;
        }

        var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        string feat;
        if (oxr == null)
        {
            feat = "OpenXR feature settings not created yet (rerun converges)";
        }
        else
        {
            int on = 0;
            on += Enable(oxr.GetFeature<OculusTouchControllerProfile>(), ref changed);
            on += Enable(oxr.GetFeature<HandTracking>(), ref changed);
            on += Enable(oxr.GetFeature<MetaHandTrackingAim>(), ref changed);
            EditorUtility.SetDirty(oxr);
            feat = $"features on: {on}/3 (Touch, HandTracking, MetaAim)";
        }
        AssetDatabase.SaveAssets();
        return "XR: OpenXR loader assigned, " + feat;
    }

    static int Enable(UnityEngine.XR.OpenXR.Features.OpenXRFeature f, ref bool changed)
    {
        if (f == null) return 0;
        if (!f.enabled) { f.enabled = true; changed = true; }
        return 1;
    }

    static string ConfigureSceneRig(ref bool changed)
    {
        if (GameObject.Find("XR Origin (Driver)") != null) return "rig: present";

        var oldDrv = Object.FindFirstObjectByType<DriverCamera>(FindObjectsInactive.Include);
        if (oldDrv == null) return "rig: SKIPPED (no DriverCamera in scene)";
        var oldGo = oldDrv.gameObject;
        var oldCam = oldGo.GetComponent<Camera>();

        var origin = new GameObject("XR Origin (Driver)");
        origin.transform.SetParent(oldGo.transform.parent, false);
        origin.transform.localPosition = oldGo.transform.localPosition;
        origin.transform.localRotation = oldGo.transform.localRotation;

        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(origin.transform, false);

        var camGo = new GameObject("Main Camera (XR)");
        camGo.transform.SetParent(offset.transform, false);
        var cam = camGo.AddComponent<Camera>();
        if (oldCam != null)
        {
            cam.clearFlags = oldCam.clearFlags;
            cam.backgroundColor = oldCam.backgroundColor;
            cam.cullingMask = oldCam.cullingMask;
            cam.nearClipPlane = Mathf.Min(oldCam.nearClipPlane, 0.05f);
            cam.farClipPlane = oldCam.farClipPlane;
            cam.depth = oldCam.depth;
        }
        camGo.AddComponent<AudioListener>();

        var tpd = camGo.AddComponent<TrackedPoseDriver>();
        tpd.positionInput = new InputActionProperty(new InputAction(binding: "<XRHMD>/centerEyePosition"));
        tpd.rotationInput = new InputActionProperty(new InputAction(binding: "<XRHMD>/centerEyeRotation"));
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        var xro = origin.AddComponent<XROrigin>();
        xro.CameraFloorOffsetObject = offset;
        xro.Camera = cam;
        xro.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;  // seated
        xro.CameraYOffset = 0f;

        var drv = camGo.AddComponent<DriverCamera>();
        drv.georeference = oldDrv.georeference;

        origin.AddComponent<VRRecenter>();
        if (Object.FindFirstObjectByType<HandRayUI>(FindObjectsInactive.Include) == null)
            origin.AddComponent<HandRayUI>();

        var survey = Object.FindFirstObjectByType<SimpleStudyQuestionnaire>(FindObjectsInactive.Include);
        if (survey != null)
        {
            var cv = survey.GetComponentInParent<Canvas>(true);
            if (cv != null && cv.GetComponent<SurveyVRPlacer>() == null)
                cv.gameObject.AddComponent<SurveyVRPlacer>();
        }

        camGo.tag = "MainCamera";
        oldGo.tag = "Untagged";
        oldGo.SetActive(false);

        changed = true;
        return "rig: XR Origin built at the driver seat (old cockpit cam deactivated)";
    }
}
#endif
