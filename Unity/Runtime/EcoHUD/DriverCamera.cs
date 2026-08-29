using CesiumForUnity;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class DriverCamera : MonoBehaviour
{
    public CesiumGeoreference georeference;

    CesiumCameraManager _mgr;
    Camera _cam;
    bool _added;

    void OnEnable() => Register();

    void Start()
    {
        Register();
#if GLEY_TRAFFIC_SYSTEM
        StartCoroutine(RegisterGleyCamera());
#endif
    }

#if GLEY_TRAFFIC_SYSTEM
    // it referenced was removed).
    System.Collections.IEnumerator RegisterGleyCamera()
    {
        while (!Gley.TrafficSystem.API.IsInitialized()) yield return null;
        Gley.TrafficSystem.API.SetCamera(transform);
        Debug.Log("[DriverCamera] registered as the traffic system camera.");
    }
#endif

    void Register()
    {
        if (_cam == null) _cam = GetComponent<Camera>();
        if (georeference == null) georeference = FindFirstObjectByType<CesiumGeoreference>();
        if (_cam == null || georeference == null) return;

        _mgr = CesiumCameraManager.GetOrCreate(georeference.gameObject);
        if (_mgr == null || _mgr.additionalCameras.Contains(_cam)) return;
        _mgr.additionalCameras.Add(_cam);
        _added = true;
    }

    void OnDisable()
    {
        if (_added && _mgr != null) _mgr.additionalCameras.Remove(_cam);
        _added = false;
        _mgr = null;
    }
}
