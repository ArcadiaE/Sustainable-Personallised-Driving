using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

public class SeatAnchorCheck : MonoBehaviour
{
    [Tooltip("Warn when the headset sits further than this from the driver anchor.")]
    public float warnAtM = 0.20f;    // m
    [Tooltip("Clear the warning when it comes back within this (hysteresis).")]
    public float clearAtM = 0.12f;   // m
    [Tooltip("Deviation must persist this long before warning (rules out glances and lean-overs).")]
    public float sustainS = 2f;      // s

    public static string Warning { get; private set; }

    XROrigin _origin;
    Camera _cam;
    float _offSince = -1f;
    bool _logged;

    void Update()
    {
        Warning = null;
        if (!XRSettings.isDeviceActive) { _offSince = -1f; _logged = false; return; }
        if (_origin == null) _origin = FindFirstObjectByType<XROrigin>();
        if (_cam == null) _cam = Camera.main;
        if (_origin == null || _cam == null) return;

        float d = Vector3.Distance(_cam.transform.position, _origin.transform.position);
        if (d > warnAtM) { if (_offSince < 0f) _offSince = Time.time; }
        else if (d < clearAtM) { _offSince = -1f; _logged = false; }

        if (_offSince >= 0f && Time.time - _offSince > sustainS)
        {
            Warning = $"VIEW OFF SEAT {d:0.00} m — press F9 to recenter";
            if (!_logged)
            {
                _logged = true;
                Debug.LogWarning($"[SeatAnchorCheck] headset {d:F2} m from the driver anchor — the car will look displaced in VR. Press F9 (or check the chair was already at height when the view was centred).");
            }
        }
    }
}
