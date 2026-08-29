#if UNITY_EDITOR_WIN
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class G25EditorInit
{
    const string AutoRunKey = "G25EditorInit.autoRan";

    static G25EditorInit()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.afterAssemblyReload += AfterReload;
    }

    static void AfterReload()
    {
        if (SessionState.GetBool(AutoRunKey, false)) return;
        SessionState.SetBool(AutoRunKey, true);
        Run(false);
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode) Run(true);
    }

    [MenuItem("Tools/Sustainable Driving/Reinitialize Steering Wheel")]
    static void RunFromMenu()
    {
        string report = RunCore();
        EditorUtility.DisplayDialog("Steering wheel", report, "OK");
    }

    static void Run(bool enteringPlay)
    {
        string report = RunCore();
        if (!G25AutoInit.WheelPresent) return;
        if (G25AutoInit.NativeReady)
        {
            Debug.Log("[G25] " + report);
            return;
        }
        Debug.LogError("[G25] " + report);
        if (enteringPlay)
            EditorUtility.DisplayDialog("Steering wheel not ready",
                report + "\n\nSteering and pedals will NOT match the study configuration this session.",
                "Continue anyway");
    }

    static string RunCore()
    {
        bool switching = !G25AutoInit.NativePresent() && G25AutoInit.CompatPresent();
        try
        {
            if (switching)
                EditorUtility.DisplayProgressBar("Steering wheel",
                    "Switching the G25 to native mode (900 deg, separate pedals)...", 0.5f);
            return G25AutoInit.EnsureNative();
        }
        finally
        {
            if (switching) EditorUtility.ClearProgressBar();
        }
    }
}
#endif
