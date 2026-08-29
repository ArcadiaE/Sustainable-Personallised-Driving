using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(Canvas))]
public class SurveyVRPlacer : MonoBehaviour
{
    [Tooltip("Panel centre distance ahead of the eyes (m).")]
    public float distanceM = 1.15f;
    [Tooltip("World-space canvas scale. Overlay canvases are ~1920 px wide; 0.0007 gives ~1.3 m.")]
    public float scale = 0.0007f;
    [Tooltip("Vertical offset below eye height (m); slightly under the eye line reads naturally.")]
    public float eyeDropM = 0.08f;

    Canvas _canvas;
    SimpleStudyQuestionnaire _survey;
    XROrigin _origin;
    RenderMode _origMode;
    Vector3 _origScale;
    bool _wasShowing;

    Vector3 _prevOriginPos;
    bool _prevPosValid;
    Vector3 _travelDir;
    int _travelSamples;

    Transform _anchor;
    Vector3 _anchorLocalPos;
    Quaternion _anchorLocalRot;
    bool _following;

    public bool Showing
    {
        get
        {
            if (_survey == null) _survey = GetComponentInChildren<SimpleStudyQuestionnaire>(true);
            if (_survey != null && _survey.panelRoot != null)
                return _survey.panelRoot.activeInHierarchy;
            return gameObject.activeInHierarchy;   // fallback: whole canvas toggles
        }
    }

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _origMode = _canvas.renderMode;
        _origScale = transform.localScale;
    }

    void LateUpdate()
    {
        SampleTravel();

        bool show = Showing;
        if (show && !_wasShowing)
        {
            Place();
        }
        else if (show && _following && _anchor != null)
        {
            transform.SetPositionAndRotation(
                _anchor.TransformPoint(_anchorLocalPos),
                _anchor.rotation * _anchorLocalRot);
        }
        if (!show) _following = false;
        _wasShowing = show;
    }

    void SampleTravel()
    {
        if (_origin == null) _origin = FindFirstObjectByType<XROrigin>();
        if (_origin == null) return;
        Vector3 p = _origin.transform.position;
        if (!_prevPosValid) { _prevOriginPos = p; _prevPosValid = true; return; }
        Vector3 v = (p - _prevOriginPos) / Mathf.Max(Time.deltaTime, 1e-4f);
        _prevOriginPos = p;
        v.y = 0f;
        float speed = v.magnitude;
        if (speed < 2f || speed > 40f) return;
        Vector3 dir = v / speed;
        _travelDir = _travelSamples == 0 ? dir : Vector3.Slerp(_travelDir, dir, 0.05f).normalized;
        _travelSamples++;
    }

    void Place()
    {
        if (!XRSettings.isDeviceActive)
        {
            _canvas.renderMode = _origMode;
            transform.localScale = _origScale;
            _following = false;
            return;
        }

        var cam = Camera.main;
        if (cam == null) return;

        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = cam;
        transform.localScale = Vector3.one * scale;

        if (_origin == null) _origin = FindFirstObjectByType<XROrigin>();
        Transform seat = _origin != null ? _origin.transform : cam.transform;
        Vector3 fwd = seat.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 fwdRaw = fwd;
        Vector3 camFlat = cam.transform.forward;
        camFlat.y = 0f;
        camFlat = camFlat.sqrMagnitude > 1e-4f ? camFlat.normalized : fwd;
        float seatDot = Vector3.Dot(fwdRaw, camFlat);
        bool usedTravel = _travelSamples >= 30;
        bool flipped = usedTravel
            ? Vector3.Dot(fwdRaw, _travelDir) < 0f
            : seatDot < 0f;
        if (flipped) fwd = -fwd;

        Vector3 eye = seat.position + Vector3.down * eyeDropM;
        Vector3 pos = eye + fwd * distanceM;
        transform.SetPositionAndRotation(pos,
            Quaternion.LookRotation(pos - eye, Vector3.up));

        _anchor = seat;
        _anchorLocalPos = _anchor.InverseTransformPoint(pos);
        _anchorLocalRot = Quaternion.Inverse(_anchor.rotation) * transform.rotation;
        _following = true;
    }
}
