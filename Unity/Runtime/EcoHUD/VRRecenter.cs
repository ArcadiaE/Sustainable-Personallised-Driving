using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class VRRecenter : MonoBehaviour
{
    public KeyCode key = KeyCode.F9;

    public static bool RecenterNow()
    {
        var subs = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subs);
        int ok = 0;
        foreach (var s in subs)
            if (s != null && s.TryRecenter()) ok++;
        Debug.Log($"[VRRecenter] recenter requested ({ok}/{subs.Count} input subsystems).");
        return ok > 0;
    }

    void Update()
    {
        if (Input.GetKeyDown(key)) RecenterNow();
    }
}
