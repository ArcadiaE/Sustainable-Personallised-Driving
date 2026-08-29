using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

public class HandRayUI : MonoBehaviour
{
    [Tooltip("Max ray length to the panel (m).")]
    public float maxRayM = 3f;
    [Tooltip("Pinch strength to press (and 0.15 lower to release: hysteresis).")]
    public float pinchThreshold = 0.7f;
    public Color rayColor = new Color(0.30f, 0.90f, 1.00f, 0.85f);

    XROrigin _origin;
    SurveyVRPlacer _survey;
    LineRenderer _line;
    Transform _dot;

    PointerEventData _pointer;
    readonly List<RaycastResult> _hits = new();
    GameObject _hoverTarget, _pressTarget, _dragTarget;
    bool _pinched;

    enum AimSource { None, RightHand, LeftHand, RightController, LeftController }
    AimSource _source = AimSource.None;
    bool _logSourceNow = true;
    class CtrlControls
    {
        public Vector3Control pos;
        public QuaternionControl rot;
        public ButtonControl pressed;
        public AxisControl trigger;
        public ButtonControl primary;
    }
    readonly Dictionary<XRController, CtrlControls> _ctrlCache = new();

    Vector2 _lastScreen;
    bool _lastScreenValid;

    enum InputPreference { Hands, Controller }
    InputPreference _pref = InputPreference.Hands;
    bool _trigRearmed = true;
    GameObject _lastHover;
    Vector2 _lastHoverScreen;
    float _lastHoverTime = -999f;
    RaycastResult _lastHoverHit;

    Vector3 _fPos;
    Quaternion _fRot;
    bool _fInit;
    AimSource _fFor = AimSource.None;

    static readonly Vector2[] _snapOffsets =
    {
        new(14f, 0f), new(-14f, 0f), new(0f, 14f), new(0f, -14f),
        new(10f, 10f), new(-10f, 10f), new(10f, -10f), new(-10f, -10f),
    };

    void Start()
    {
        _origin = GetComponentInParent<XROrigin>();
        if (EventSystem.current == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        _pointer = new PointerEventData(EventSystem.current);

        var lineGo = new GameObject("HandRay");
        lineGo.transform.SetParent(transform, false);
        _line = lineGo.AddComponent<LineRenderer>();
        _line.material = new Material(Shader.Find("Sprites/Default"));
        _line.startWidth = 0.006f;
        _line.endWidth = 0.002f;
        _line.startColor = rayColor;
        _line.endColor = rayColor;
        _line.positionCount = 2;
        _line.enabled = false;

        var dotGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotGo.name = "HandRayDot";
        Destroy(dotGo.GetComponent<Collider>());
        dotGo.transform.localScale = Vector3.one * 0.012f;
        dotGo.GetComponent<Renderer>().material = _line.material;
        _dot = dotGo.transform;
        _dot.gameObject.SetActive(false);
    }

    void Update()
    {
        Canvas canvas = ActiveSurveyCanvas();
        if (canvas == null || !XRSettings.isDeviceActive)
        {
            _logSourceNow = true;
            _lastScreenValid = false;
            SetVisible(false);
            ReleaseAll();
            return;
        }

        if (!TryGetAim(out Vector3 aimPos, out Quaternion aimRot, out float pinch, out AimSource src))
        {
            NoteSource(AimSource.None);
            _fInit = false;
            _lastScreenValid = false;
            SetVisible(false);
            ReleaseAll();
            return;
        }
        NoteSource(src);

        if (src == AimSource.RightHand || src == AimSource.LeftHand)
            FilterHandAim(ref aimPos, ref aimRot, src);
        else
            _fInit = false;

        bool pinchedNow = _pinched ? pinch > pinchThreshold - 0.15f : pinch > pinchThreshold;

        var cam = _origin != null && _origin.Camera != null ? _origin.Camera : Camera.main;
        if (cam == null) return;
        if (canvas.worldCamera == null) canvas.worldCamera = cam;

        var gr = canvas.GetComponent<GraphicRaycaster>();
        if (gr == null)
        {
            gr = canvas.gameObject.AddComponent<GraphicRaycaster>();
            Debug.LogError("[HandRayUI] SurveyCanvas has NO GraphicRaycaster — added one at runtime. Add it to the canvas in the scene for a permanent fix.");
        }

        var ray = new Ray(aimPos, aimRot * Vector3.forward);
        var plane = new Plane(canvas.transform.forward, canvas.transform.position);
        if (!plane.Raycast(ray, out float enter) || enter <= 0f || enter > maxRayM)
        {
            if (pinchedNow != _pinched)
                Debug.Log("[HandRayUI] " + (pinchedNow ? "press" : "release")
                    + " OFF-PANEL (ray does not reach the canvas plane) v=" + pinch.ToString("F2")
                    + " source=" + SourceLabel(_source));
            SetVisible(false);
            if (!pinchedNow) ReleaseAll();
            _pinched = pinchedNow;
            return;
        }

        Vector3 hitWorld = ray.GetPoint(enter);
        Vector2 screen = cam.WorldToScreenPoint(hitWorld);

        bool handSrc = _source == AimSource.RightHand || _source == AimSource.LeftHand;
        if (_pinched && handSrc && _lastScreenValid)
            screen = Vector2.MoveTowards(_lastScreen, screen, 2500f * Mathf.Max(Time.deltaTime, 1e-4f));
        _lastScreen = screen;
        _lastScreenValid = true;

        _pointer.position = screen;

        _hits.Clear();
        gr.Raycast(_pointer, _hits);
        GameObject target = _hits.Count > 0 ? _hits[0].gameObject : null;

        if (target == null && (_source == AimSource.RightHand || _source == AimSource.LeftHand))
        {
            Vector2 centre = screen;
            foreach (Vector2 off in _snapOffsets)
            {
                _pointer.position = centre + off;
                _hits.Clear();
                gr.Raycast(_pointer, _hits);
                if (_hits.Count > 0)
                {
                    target = _hits[0].gameObject;
                    screen = centre + off;
                    _lastScreen = screen;
                    break;
                }
            }
            if (target == null) _pointer.position = centre;
        }

        if (pinchedNow && !_pinched && target == null && handSrc &&
            _lastHover != null && Time.time - _lastHoverTime < 0.3f)
        {
            target = _lastHover;
            screen = _lastHoverScreen;
            _pointer.position = screen;
            _hits.Clear();
            _hits.Add(_lastHoverHit);
        }
        if (target != null)
        {
            _lastHover = target;
            _lastHoverScreen = screen;
            _lastHoverTime = Time.time;
            _lastHoverHit = _hits[0];
        }

        if (pinchedNow != _pinched)
            Debug.Log("[HandRayUI] " + (pinchedNow ? "press" : "release")
                + " v=" + pinch.ToString("F2")
                + " source=" + SourceLabel(_source)
                + " hits=" + _hits.Count
                + " target=" + (target != null ? target.name : "NONE")
                + " screen=" + screen.ToString("F0")
                + " local=" + LocalOnCanvas(canvas, hitWorld));

        SetVisible(true);
        _line.SetPosition(0, ray.origin);
        _line.SetPosition(1, hitWorld);
        _dot.position = hitWorld;

        if (target != _hoverTarget)
        {
            if (_hoverTarget != null)
                ExecuteEvents.ExecuteHierarchy(_hoverTarget, _pointer, ExecuteEvents.pointerExitHandler);
            if (target != null)
                ExecuteEvents.ExecuteHierarchy(target, _pointer, ExecuteEvents.pointerEnterHandler);
            _hoverTarget = target;
        }

        if (pinchedNow && !_pinched && target != null)
        {
            _pointer.pressPosition = screen;
            _pointer.pointerPressRaycast = _hits[0];
            _pressTarget = ExecuteEvents.ExecuteHierarchy(target, _pointer, ExecuteEvents.pointerDownHandler);
            if (_pressTarget == null) _pressTarget = target;
            _pointer.pointerPress = _pressTarget;

            _dragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            if (_dragTarget != null)
            {
                _pointer.pointerDrag = _dragTarget;
                ExecuteEvents.Execute(_dragTarget, _pointer, ExecuteEvents.initializePotentialDrag);
                ExecuteEvents.Execute(_dragTarget, _pointer, ExecuteEvents.beginDragHandler);
            }
        }
        else if (pinchedNow && _pinched && _dragTarget != null)
        {
            ExecuteEvents.Execute(_dragTarget, _pointer, ExecuteEvents.dragHandler);
        }
        else if (!pinchedNow && _pinched)
        {
            if (_dragTarget != null)
                ExecuteEvents.Execute(_dragTarget, _pointer, ExecuteEvents.endDragHandler);
            if (_pressTarget != null)
            {
                ExecuteEvents.ExecuteHierarchy(_pressTarget, _pointer, ExecuteEvents.pointerUpHandler);
                var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(_pressTarget);
                if (click != null && target != null &&
                    click == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target))
                    ExecuteEvents.Execute(click, _pointer, ExecuteEvents.pointerClickHandler);
            }
            _pressTarget = null;
            _dragTarget = null;
            _pointer.pointerPress = null;
            _pointer.pointerDrag = null;
        }
        _pinched = pinchedNow;
    }

    bool TryGetAim(out Vector3 pos, out Quaternion rot, out float pinch, out AimSource source)
    {
        pos = default; rot = default; pinch = 0f; source = AimSource.None;

        bool rTrig = IsTracked(XRController.rightHand) && TriggerValue(XRController.rightHand) > pinchThreshold;
        bool lTrig = IsTracked(XRController.leftHand) && TriggerValue(XRController.leftHand) > pinchThreshold;
        bool backBtn = PrimaryPressedThisFrame(XRController.rightHand) || PrimaryPressedThisFrame(XRController.leftHand);
        if (backBtn)
        {
            SetPreference(InputPreference.Hands);
            _trigRearmed = false;
        }
        else if (rTrig || lTrig)
        {
            if (_trigRearmed) SetPreference(InputPreference.Controller);
        }
        else
        {
            _trigRearmed = true;
            if (_pref == InputPreference.Controller &&
                ((ValidAim(MetaAimHand.right) && MetaAimHand.right.pinchStrengthIndex.ReadValue() > pinchThreshold) ||
                 (ValidAim(MetaAimHand.left) && MetaAimHand.left.pinchStrengthIndex.ReadValue() > pinchThreshold)))
                SetPreference(InputPreference.Hands);
        }

        if (_pref == InputPreference.Controller)
        {
            var latched = IsTracked(XRController.rightHand) ? XRController.rightHand
                        : IsTracked(XRController.leftHand) ? XRController.leftHand : null;
            if (latched != null) return ControllerAim(latched, ref pos, ref rot, ref pinch, ref source);
        }

        var hand = ValidAim(MetaAimHand.right) ? MetaAimHand.right
                 : ValidAim(MetaAimHand.left) ? MetaAimHand.left : null;
        if (hand != null)
        {
            ToWorld(hand.devicePosition.ReadValue(), hand.deviceRotation.ReadValue(), out pos, out rot);
            pinch = hand.pinchStrengthIndex.ReadValue();
            source = hand == MetaAimHand.right ? AimSource.RightHand : AimSource.LeftHand;
            return true;
        }

        var ctrl = IsTracked(XRController.rightHand) ? XRController.rightHand
                 : IsTracked(XRController.leftHand) ? XRController.leftHand : null;
        if (ctrl != null) return ControllerAim(ctrl, ref pos, ref rot, ref pinch, ref source);

        return false;
    }

    void SetPreference(InputPreference p)
    {
        if (_pref == p) return;
        _pref = p;
        Debug.Log("[HandRayUI] input preference -> " + (p == InputPreference.Controller
            ? "CONTROLLER (trigger pulled; press A/X to hand back)"
            : "HANDS (A/X button, or pinch with triggers idle)"));
    }

    bool PrimaryPressedThisFrame(XRController c)
    {
        if (!IsTracked(c)) return false;
        var cc = Controls(c);
        return cc.primary != null && cc.primary.wasPressedThisFrame;
    }

    bool ControllerAim(XRController ctrl, ref Vector3 pos, ref Quaternion rot, ref float pinch, ref AimSource source)
    {
        CtrlControls cc = Controls(ctrl);
        Vector3 p = cc.pos != null ? cc.pos.ReadValue() : ctrl.devicePosition.ReadValue();
        Quaternion r = cc.rot != null ? cc.rot.ReadValue() : ctrl.deviceRotation.ReadValue();
        ToWorld(p, r, out pos, out rot);
        pinch = TriggerValue(ctrl);
        source = ctrl == XRController.rightHand ? AimSource.RightController : AimSource.LeftController;
        return true;
    }

    float TriggerValue(XRController c)
    {
        CtrlControls cc = Controls(c);
        float btn = cc.pressed != null && cc.pressed.isPressed ? 1f : 0f;
        float axis = cc.trigger != null ? cc.trigger.ReadValue() : 0f;
        return Mathf.Max(btn, axis);
    }

    static bool ValidAim(MetaAimHand h)
    {
        if (h == null || !h.added) return false;
        var flags = (MetaAimFlags)h.aimFlags.ReadValue();
        return (flags & MetaAimFlags.Valid) != 0;
    }

    static bool IsTracked(XRController c) => c != null && c.added && c.isTracked.isPressed;

    void FilterHandAim(ref Vector3 pos, ref Quaternion rot, AimSource src)
    {
        if (!_fInit || _fFor != src)
        {
            _fPos = pos; _fRot = rot;
            _fInit = true; _fFor = src;
            return;
        }
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float speed = (pos - _fPos).magnitude / dt;              // m/s
        float angSpeed = Quaternion.Angle(_fRot, rot) / dt;      // deg/s
        float cutoffHz = 1.5f + 0.6f * speed + 0.02f * angSpeed;
        if (_pinched) cutoffHz *= 0.5f;
        float k = 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz * dt);
        _fPos = Vector3.Lerp(_fPos, pos, k);
        _fRot = Quaternion.Slerp(_fRot, rot, k);
        pos = _fPos;
        rot = _fRot;
    }

    void ToWorld(Vector3 trackingPos, Quaternion trackingRot, out Vector3 pos, out Quaternion rot)
    {
        Transform originT = _origin != null
            ? (_origin.Origin != null ? _origin.Origin.transform : _origin.transform)
            : transform;
        pos = originT.TransformPoint(trackingPos);
        rot = originT.rotation * trackingRot;
    }

    CtrlControls Controls(XRController c)
    {
        if (_ctrlCache.TryGetValue(c, out CtrlControls cc)) return cc;
        cc = new CtrlControls
        {
            pos = c.TryGetChildControl<Vector3Control>("pointerPosition")
               ?? c.TryGetChildControl<Vector3Control>("pointer/position"),
            rot = c.TryGetChildControl<QuaternionControl>("pointerRotation")
               ?? c.TryGetChildControl<QuaternionControl>("pointer/rotation"),
            pressed = c.TryGetChildControl<ButtonControl>("triggerPressed"),
            trigger = c.TryGetChildControl<AxisControl>("trigger"),
            primary = c.TryGetChildControl<ButtonControl>("primaryButton"),
        };
        _ctrlCache[c] = cc;
        Debug.Log("[HandRayUI] controller controls (" + c.name + "): pose="
            + (cc.pos != null ? cc.pos.path : "grip-fallback(devicePosition)")
            + " press=" + (cc.pressed != null ? "triggerPressed" : "none")
            + " axis=" + (cc.trigger != null ? "trigger" : "none")
            + " handsBtn=" + (cc.primary != null ? "primaryButton(A/X)" : "none"));
        return cc;
    }

    void NoteSource(AimSource s)
    {
        if (s == _source && !_logSourceNow) return;
        _source = s;
        _logSourceNow = false;
        Debug.Log("[HandRayUI] source: " + SourceLabel(s)
            + " | MetaAimHand right: " + DescribeHand(MetaAimHand.right)
            + ", left: " + DescribeHand(MetaAimHand.left)
            + " | XRController right: " + DescribeController(XRController.rightHand)
            + ", left: " + DescribeController(XRController.leftHand));
    }

    static string SourceLabel(AimSource s) => s switch
    {
        AimSource.RightHand => "right hand",
        AimSource.LeftHand => "left hand",
        AimSource.RightController => "right controller",
        AimSource.LeftController => "left controller",
        _ => "none",
    };

    static string DescribeHand(MetaAimHand h) =>
        h == null ? "MISSING (device never created)"
        : !h.added ? "removed"
        : "aimFlags=" + (MetaAimFlags)h.aimFlags.ReadValue();

    static string DescribeController(XRController c) =>
        c == null ? "none"
        : c.isTracked.isPressed ? "tracked" : "untracked";

    static string LocalOnCanvas(Canvas canvas, Vector3 world)
    {
        var rt = canvas.transform as RectTransform;
        if (rt == null) return "n/a";
        Vector2 p = rt.InverseTransformPoint(world);
        return p.ToString("F0") + " of rect " + rt.rect.size.ToString("F0");
    }

    Canvas ActiveSurveyCanvas()
    {
        if (_survey == null)
        {
            _survey = FindFirstObjectByType<SurveyVRPlacer>(FindObjectsInactive.Include);
            if (_survey == null) return null;
        }
        return _survey.Showing ? _survey.GetComponent<Canvas>() : null;
    }

    void SetVisible(bool on)
    {
        if (_line != null && _line.enabled != on) _line.enabled = on;
        if (_dot != null && _dot.gameObject.activeSelf != on) _dot.gameObject.SetActive(on);
    }

    void ReleaseAll()
    {
        if (_hoverTarget != null)
        {
            ExecuteEvents.ExecuteHierarchy(_hoverTarget, _pointer, ExecuteEvents.pointerExitHandler);
            _hoverTarget = null;
        }
        if (_dragTarget != null)
        {
            ExecuteEvents.Execute(_dragTarget, _pointer, ExecuteEvents.endDragHandler);
            _dragTarget = null;
        }
        if (_pressTarget != null)
        {
            ExecuteEvents.ExecuteHierarchy(_pressTarget, _pointer, ExecuteEvents.pointerUpHandler);
            _pressTarget = null;
        }
        if (_pointer != null)
        {
            _pointer.pointerPress = null;
            _pointer.pointerDrag = null;
        }
        _pinched = false;
    }
}
