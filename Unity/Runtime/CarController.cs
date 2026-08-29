using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Driving Settings")]
    public float motorForce = 8000f;        // 引擎力（N）
    public float brakeForce = 15000f;       // 刹车力
    public float maxSpeed = 25f;            // 最大前进速度 (m/s)
    public float maxReverseSpeed = 10f;     // 最大倒车速度

    [Header("Steering")]
    public float maxSteerAngle = 35f;       // 最大前轮转角（度）
    public float steeringResponse = 5f;     // 方向盘响应速度
    public float turnRadiusFactor = 4f;     // 转弯半径系数（越小半径越小）

    [Header("Physics")]
    public float drag = 0.15f;
    public float groundedDrag = 1.5f;
    public float sidewaysFriction = 5f;     // 侧向摩擦（每秒消除侧滑的比例系数）
    public float absoluteMaxSpeed = 80f;    // 速度硬上限（m/s），防物理爆炸

    [Header("Auto Collider Setup")]
    public bool autoSetupColliders = true;  // 自动禁用 WheelCollider 并生成车身 BoxCollider
    public float bodyFriction = 0.1f;       // 车身碰撞体摩擦系数

    [Header("Steering wheel + pedals (G25 family)")]
    [Tooltip("Analog wheel/pedal input, merged with the keyboard (larger magnitude wins). Control paths come from RunFlag 'axesprobe'.")]
    public bool useWheel = true;
    [Tooltip("Read the wheel through Unity's Input System HID path. OFF by default: on the G25 the HID state never streams, but its one-off init sync (stick/x jumping 0 -> -1) slipped past the liveliness gate and pinned the steering at full left (OS-level winmm probe proved the hardware itself is healthy: X/Y both centred). winmm below is the working path.")]
    public bool useInputSystemWheel = false;
    [Tooltip("Substring matched against the Input System device name/product (see RunFlag 'inputdevices').")]
    public string wheelDeviceMatch = "Racing Wheel";
    public string steerControl = "stick/x";
    [Tooltip("Steering axis is signed (-1..1, e.g. stick/x). Untick for unsigned 0..1 axes.")]
    public bool steerSigned = true;
    [Tooltip("Fraction of the full AXIS range that maps to full steerInput. NATIVE mode spans ±450° (900°), so 1.0 = full lock at ±450° — a real car's ~2.5 turns lock-to-lock (user request). Lower it if that feels too heavy in the study (0.6 = ±270°).")]
    [Range(0.1f, 1f)] public float steerRange = 1.0f;
    [Tooltip("Wheel response curve: 1 = linear, >1 = calmer around centre while full lock at the ends stays reachable (sensitivity still too high with the range already at 100%). Try 1.4-2.2.")]
    [Range(1f, 2.5f)] public float steerCurve = 1.6f;
    public float steerDeadzone = 0.03f;
    [Tooltip("Separate pedal axes (throttle + brake). If the probe shows ONE shared axis instead, tick combinedPedals.")]
    public string throttleControl = "stick/y";
    public string brakeControl = "rz";
    [Tooltip("Raw axis value with the pedal fully RELEASED (G25 pedals rest at 1).")]
    public float pedalRestValue = 1f;
    [Tooltip("Raw axis value with the pedal fully PRESSED.")]
    public float pedalFullValue = 0f;
    [Tooltip("Old G25 default without the Logitech profiler: both pedals share ONE signed axis (throttleControl). +1 = throttle, -1 = brake; tick invertCombined if reversed.")]
    public bool combinedPedals = false;
    public bool invertCombined = false;
    [Tooltip("Windows-native winmm joystick path (joyGetPosEx). Reads the driver directly — works when Unity's generic HID parser cannot decode the wheel's reports (G25, field). Axes: 0=X 1=Y 2=Z 3=R 4=U 5=V. Live from the first frame — no wake-up gestures needed.")]
    public bool useWinmmFallback = true;
    [Tooltip("Steering axis. 0 = X, pinned by evidence (G25 NATIVE mode/PID C299: X rests centred at ~32.3k). -1 = auto-detect by movement — do NOT use in native mode: the unloaded clutch axis (Z) floats across the full range and would steal the binding.")]
    public int winmmSteerAxis = 0;
    [Tooltip("Raw-space steering deadzone (fraction of half-range). Applied BEFORE the steerRange mapping so a slightly off-centre wheel cannot pull the car.")]
    public float winmmSteerDeadzone = 0.02f;
    [Tooltip("A real brake pedal only BRAKES: its negative input is zeroed once the car has stopped, so holding it cannot reverse. Reverse is a GEAR (paddle shifters below) or the keyboard S key.")]
    public bool pedalReverseDisabled = true;
    [Tooltip("Paddle shifters as a two-gear box : RIGHT paddle = Drive, LEFT paddle = Reverse. Indices into the winmm dwButtons bitmask — if these paddles light different bits on this wheel, read 'btn' in the overlay while pulling one and set the numbers here.")]
    public int winmmForwardButton = 4;
    public int winmmReverseButton = 5;
    [Tooltip("Combined single-axis pedals: only the case in the G25's no-driver COMPATIBILITY mode (PID C294). In NATIVE mode (PID C299, switched via HID F8 10) pedals are separate — leave this off.")]
    public bool winmmCombinedPedals = false;
    public int winmmCombinedAxis = 1;
    [Tooltip("Tick if throttle and brake come out swapped.")]
    public bool winmmInvertCombined = false;
    [Tooltip("Separate-pedal axes (native mode, action-fingerprinted): throttle = 2/Z, brake = 3/R, clutch = 1/Y (unused). All rest at 65535, pressed toward 0.")]
    public int winmmThrottleAxis = 2;
    public int winmmBrakeAxis = 3;
    [Tooltip("The clutch pedal doubles as a SECOND brake : its spring is far softer than the brake's, so braking with the left pedal needs much less leg force. Either pedal brakes - the deeper press wins; the 8% deadzone keeps a floating unloaded axis from phantom-braking. Untick to make the clutch inert again.")]
    public bool clutchActsAsBrake = true;
    public int winmmClutchAxis = 1;
    [Tooltip("On-screen live readout of the wheel values (top-left, Game view). Off for the study (mapping); re-enable when diagnosing wheel input.")]
    public bool wheelDebugOverlay = false;

    [Header("Current State (Read-Only)")]
    public float currentSpeed;              // km/h
    public float currentAcceleration;
    public float throttleInput;             // -1..1
    public float steerInput;                // -1..1
    public float currentSteerAngle;         // 实际前轮转角

    private Rigidbody rb;
    private float previousSpeed;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearDamping = drag;
        rb.angularDamping = 3f;
        rb.centerOfMass = new Vector3(0, -0.5f, 0);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (autoSetupColliders)
        {
            SetupColliders();
        }
    }

    // 会抵抗 AddForce 推进并产生翘头力矩），改用一个包住整车的低摩擦 BoxCollider。
    void SetupColliders()
    {
        // 1. 禁用所有 WheelCollider
        WheelCollider[] wheels = GetComponentsInChildren<WheelCollider>(true);
        foreach (WheelCollider wc in wheels)
        {
            wc.enabled = false;
        }
        if (wheels.Length > 0)
        {
            Debug.Log("[CarController] Disabled " + wheels.Length + " WheelCollider(s).");
        }

        // 2. 低摩擦物理材质（抓地与减速由脚本的侧向摩擦和发动机制动接管）
        PhysicsMaterial bodyMat = new PhysicsMaterial("CarBodyAuto");
        bodyMat.dynamicFriction = bodyFriction;
        bodyMat.staticFriction = bodyFriction;
        bodyMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        bodyMat.bounceCombine = PhysicsMaterialCombine.Minimum;
        bodyMat.bounciness = 0f;

        // 粒子系统、车门后视镜壳全被算进去），平底大盒的四角在起伏路面上蹭地形，
        // 镜面覆盖层，宽度钳制到车身实宽（镜壳并入 Body 网格无法单独剔除）。
        BoxCollider body = GetComponent<BoxCollider>();
        if (body == null)
        {
            body = gameObject.AddComponent<BoxCollider>();

            Renderer[] renderers = GetComponentsInChildren<Renderer>(false);
            Bounds worldBounds = default;
            bool first = true;
            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                string n = r.gameObject.name;
                if (n.Contains("Shadow") || n.Contains("MirrorGlass")) continue;
                if (first) { worldBounds = r.bounds; first = false; }
                else worldBounds.Encapsulate(r.bounds);
            }
            if (!first)
            {
                // 世界包围盒转回本物体局部空间（车未旋转时进行，Start 阶段成立）
                body.center = transform.InverseTransformPoint(worldBounds.center);
                Vector3 lossy = transform.lossyScale;
                Vector3 size = new Vector3(
                    worldBounds.size.x / Mathf.Max(lossy.x, 0.0001f),
                    worldBounds.size.y / Mathf.Max(lossy.y, 0.0001f),
                    worldBounds.size.z / Mathf.Max(lossy.z, 0.0001f));
                size.x = Mathf.Min(size.x, 1.90f);   // 车身实宽，剔除外后视镜壳

                // 标准街机方案：盒底抬高 0.30m 只管墙/车，四个球形"车轮"负责贴地——
                // 球面能平滑滚过坡度、台阶和地形瓦片接缝。
                float lift = 0.30f;
                Vector3 c2 = body.center; c2.y += lift * 0.5f;
                size.y -= lift;
                body.center = c2;
                body.size = size;
                Debug.Log("[CarController] Auto BoxCollider center=" + body.center + " size=" + body.size);

                float wheelR = 0.34f;
                Vector3[] wheelPos =
                {
                    new(-0.78f, wheelR, 1.45f), new(0.78f, wheelR, 1.45f),
                    new(-0.78f, wheelR, -1.45f), new(0.78f, wheelR, -1.45f)
                };
                // 有轮子网格就用真实轮位（更准），找不到再用上面的默认值
                string[] wheelNames = { "RMCar05WheelFrontLeft", "RMCar05WheelFrontRight",
                                        "RMCar05WheelRearLeft", "RMCar05WheelRearRight" };
                for (int i = 0; i < 4; i++)
                {
                    foreach (Transform tr in GetComponentsInChildren<Transform>(true))
                        if (tr.name == wheelNames[i])
                        {
                            Vector3 lp = transform.InverseTransformPoint(tr.position);
                            wheelPos[i] = new Vector3(lp.x, wheelR, lp.z);
                            break;
                        }
                    var s = gameObject.AddComponent<SphereCollider>();
                    s.center = wheelPos[i];
                    s.radius = wheelR;
                    s.material = bodyMat;
                }
                Debug.Log("[CarController] 4 sphere wheels at " + wheelPos[0] + " ... " + wheelPos[3]);
            }
        }
        body.material = bodyMat;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        throttleInput = 0f;
        steerInput = 0f;

        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    throttleInput = 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  throttleInput = -1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  steerInput = -1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steerInput = 1f;

        // 回退）。语义与键盘一致：刹车踩到停后继续踩 = 倒车（手动倒挡保留）。
        // AutoDriver engaged 时照旧覆盖这些输入，BO 轮次不受影响。
        // 活性门控（同日现场修）：每个轴必须先被观察到真的动过（相对绑定时的
        // 成 -1，曾在按 W 时把车拽向左；死轴现在永远出局。没插方向盘的机器上
        // 一切轴永不激活，行为与从前完全一致。
        if (useWheel)
        {
            if (useInputSystemWheel && (_wheel == null || !_wheel.added)) BindWheel();

            float wheelSteer = 0f, wheelPedals = 0f;

            // 且其一次性初始化同步能骗过活性门控造成满舵；见字段说明）
            if (useInputSystemWheel && _steerCtl != null)
            {
                float raw = ReadAxis(_steerCtl);
                if (!_isSteerAlive && !float.IsNaN(_isSteerRest) && Mathf.Abs(raw - _isSteerRest) > 0.08f) _isSteerAlive = true;
                if (_isSteerAlive)
                {
                    float s = steerSigned ? raw : raw * 2f - 1f;
                    s = Mathf.Clamp(s / Mathf.Max(0.05f, steerRange), -1f, 1f);
                    if (Mathf.Abs(s) < steerDeadzone) s = 0f;
                    wheelSteer = s;
                }
            }
            if (useInputSystemWheel && combinedPedals)
            {
                if (_thrCtl != null)
                {
                    float raw = ReadAxis(_thrCtl);
                    if (!_isThrAlive && !float.IsNaN(_thrRest) && Mathf.Abs(raw - _thrRest) > 0.08f) _isThrAlive = true;
                    if (_isThrAlive) wheelPedals = Mathf.Clamp(invertCombined ? -raw : raw, -1f, 1f);
                }
            }
            else if (useInputSystemWheel)
            {
                float thr = 0f, brk = 0f;
                if (_thrCtl != null)
                {
                    float raw = ReadAxis(_thrCtl);
                    if (!_isThrAlive && !float.IsNaN(_thrRest) && Mathf.Abs(raw - _thrRest) > 0.08f) _isThrAlive = true;
                    if (_isThrAlive) thr = PedalDepth01(raw, _thrRest);
                }
                if (_brkCtl != null)
                {
                    float raw = ReadAxis(_brkCtl);
                    if (!_isBrkAlive && !float.IsNaN(_brkRest) && Mathf.Abs(raw - _brkRest) > 0.08f) _isBrkAlive = true;
                    if (_isBrkAlive) brk = PedalDepth01(raw, _brkRest);
                }
                wheelPedals = Mathf.Clamp(thr - brk, -1f, 1f);
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (useWinmmFallback) ReadWinmm(ref wheelSteer, ref wheelPedals);
#endif

            if (Mathf.Abs(wheelSteer) > Mathf.Abs(steerInput)) steerInput = wheelSteer;
            if (Mathf.Abs(wheelPedals) > 0.02f && Mathf.Abs(wheelPedals) > Mathf.Abs(throttleInput))
                throttleInput = wheelPedals;
        }
    }

    InputDevice _wheel;
    InputControl _steerCtl, _thrCtl, _brkCtl;
    float _wheelRetryAt;
    float _isSteerRest = float.NaN;                      // bind-time snapshots
    float _thrRest = float.NaN, _brkRest = float.NaN;
    bool _isSteerAlive, _isThrAlive, _isBrkAlive;        // proof-of-life flags

    void BindWheel()
    {
        if (Time.unscaledTime < _wheelRetryAt) return;
        _wheelRetryAt = Time.unscaledTime + 2f;
        _wheel = null; _steerCtl = _thrCtl = _brkCtl = null;
        foreach (var d in InputSystem.devices)
        {
            if (d is Keyboard || d is Mouse || d is Pointer) continue;
            string label = (d.description.product ?? "") + "|" + d.displayName;
            if (!label.Contains(wheelDeviceMatch)) continue;
            _wheel = d;
            _steerCtl = string.IsNullOrEmpty(steerControl) ? null : d.TryGetChildControl(steerControl);
            _thrCtl = string.IsNullOrEmpty(throttleControl) ? null : d.TryGetChildControl(throttleControl);
            _brkCtl = string.IsNullOrEmpty(brakeControl) ? null : d.TryGetChildControl(brakeControl);
            _isSteerRest = _steerCtl != null ? ReadAxis(_steerCtl) : float.NaN;
            _thrRest = _thrCtl != null ? ReadAxis(_thrCtl) : float.NaN;
            _brkRest = _brkCtl != null ? ReadAxis(_brkCtl) : float.NaN;
            _isSteerAlive = _isThrAlive = _isBrkAlive = false;
            Debug.Log($"[CarController] wheel bound: '{d.displayName}'  steer={(_steerCtl != null ? steerControl : "MISSING")}  " +
                      $"throttle={(_thrCtl != null ? throttleControl : "MISSING")}  brake={(_brkCtl != null ? brakeControl : "MISSING")}" +
                      (combinedPedals ? "  (combined pedals mode)" : ""));
            return;
        }
    }

    static float ReadAxis(InputControl c) => c is AxisControl a ? a.ReadValue() : 0f;

    float PedalDepth01(float raw, float observedRest)
    {
        float rest = !float.IsNaN(observedRest) && Mathf.Abs(observedRest - pedalRestValue) > 0.25f
            ? observedRest : pedalRestValue;
        return Mathf.Clamp01(Mathf.InverseLerp(rest, pedalFullValue, raw));
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct JOYINFOEX
    {
        public int dwSize, dwFlags;
        public int dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
        public int dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }
    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    static extern int joyGetPosEx(int uJoyID, ref JOYINFOEX pji);

    int _joyId = -1;
    float _joyRetryAt;
    readonly float[] _axRaw = new float[6];              // live raw values (overlay)
    readonly float[] _axMin = new float[6];              // observed ranges (auto-detect)
    readonly float[] _axMax = new float[6];
    int _steerAxisBound = -1;                            // resolved steering axis
    int _gear = 1;
    int _prevButtons;
    int _dbgButtons;

    static JOYINFOEX NewJoyInfo() => new JOYINFOEX
    {
        dwSize = System.Runtime.InteropServices.Marshal.SizeOf<JOYINFOEX>(),
        dwFlags = 0xFF,   // JOY_RETURN X|Y|Z|R|U|V|POV|BUTTONS
    };

    static float JoyAxis(ref JOYINFOEX j, int idx) => idx switch
    {
        0 => j.dwXpos, 1 => j.dwYpos, 2 => j.dwZpos,
        3 => j.dwRpos, 4 => j.dwUpos, 5 => j.dwVpos, _ => 0f,
    };

    void ReadWinmm(ref float steer, ref float pedals)
    {
        if (_joyId < 0)
        {
            if (Time.unscaledTime < _joyRetryAt) return;
            _joyRetryAt = Time.unscaledTime + 2f;
            for (int id = 0; id < 4 && _joyId < 0; id++)
            {
                var probe = NewJoyInfo();
                if (joyGetPosEx(id, ref probe) == 0) _joyId = id;
            }
            if (_joyId < 0) return;
            for (int i = 0; i < 6; i++) { _axMin[i] = float.MaxValue; _axMax[i] = float.MinValue; }
            _steerAxisBound = winmmSteerAxis >= 0 ? winmmSteerAxis : -1;
            Debug.Log($"[CarController] winmm joystick {_joyId} online — steering axis " +
                      (_steerAxisBound >= 0 ? _steerAxisBound.ToString() : "AUTO (turn the wheel once)") + ", " +
                      (winmmCombinedPedals ? $"combined pedals on axis {winmmCombinedAxis}" : $"pedals on axes {winmmThrottleAxis}/{winmmBrakeAxis}"));
        }

        var j = NewJoyInfo();
        if (joyGetPosEx(_joyId, ref j) != 0) { _joyId = -1; return; }
        for (int i = 0; i < 6; i++)
        {
            _axRaw[i] = JoyAxis(ref j, i);
            if (_axRaw[i] < _axMin[i]) _axMin[i] = _axRaw[i];
            if (_axRaw[i] > _axMax[i]) _axMax[i] = _axRaw[i];
        }

        if (_steerAxisBound < 0)
        {
            for (int i = 0; i < 6; i++)
            {
                if (winmmCombinedPedals && i == winmmCombinedAxis) continue;
                if (!winmmCombinedPedals && (i == winmmThrottleAxis || i == winmmBrakeAxis)) continue;
                if (_axMax[i] - _axMin[i] > 8000f)
                {
                    _steerAxisBound = i;
                    Debug.Log($"[CarController] steering axis auto-bound: {i} (range {_axMin[i]:F0}..{_axMax[i]:F0})");
                    break;
                }
            }
        }

        if (_steerAxisBound >= 0)
        {
            float sN = (_axRaw[_steerAxisBound] - 32767.5f) / 32767.5f;
            float dz = Mathf.Clamp(winmmSteerDeadzone, 0f, 0.2f);
            if (Mathf.Abs(sN) < dz) sN = 0f;
            else sN = Mathf.Sign(sN) * (Mathf.Abs(sN) - dz) / (1f - dz);
            float s = Mathf.Clamp(sN / Mathf.Max(0.05f, steerRange), -1f, 1f);
            s = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), Mathf.Max(1f, steerCurve));
            if (Mathf.Abs(s) > Mathf.Abs(steer)) steer = s;
        }

        _dbgButtons = j.dwButtons;
        bool fwdEdge = (j.dwButtons & (1 << winmmForwardButton)) != 0 && (_prevButtons & (1 << winmmForwardButton)) == 0;
        bool revEdge = (j.dwButtons & (1 << winmmReverseButton)) != 0 && (_prevButtons & (1 << winmmReverseButton)) == 0;
        _prevButtons = j.dwButtons;
        if (fwdEdge && _gear != 1) { _gear = 1; Debug.Log("[CarController] gear -> D (right paddle)"); }
        if (revEdge && _gear != -1) { _gear = -1; Debug.Log("[CarController] gear -> R (left paddle)"); }

        // pedals
        float p;
        if (winmmCombinedPedals)
        {
            float raw = JoyAxis(ref j, winmmCombinedAxis);
            p = (32767.5f - raw) / 32767.5f;
            if (winmmInvertCombined) p = -p;
        }
        else
        {
            float thr = 1f - Mathf.Clamp01(JoyAxis(ref j, winmmThrottleAxis) / 65535f);
            float brk = 1f - Mathf.Clamp01(JoyAxis(ref j, winmmBrakeAxis) / 65535f);
            if (clutchActsAsBrake)
            {
                float clu = 1f - Mathf.Clamp01(JoyAxis(ref j, winmmClutchAxis) / 65535f);
                brk = Mathf.Max(brk, Mathf.InverseLerp(0.08f, 1f, clu));
            }
            p = thr - brk;
        }
        if (Mathf.Abs(p) < 0.04f) p = 0f;   // pedal deadzone
        p = Mathf.Clamp(p, -1f, 1f);

        float fwdVel = rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;
        if (_gear > 0)
        {
            if (pedalReverseDisabled && p < 0f && fwdVel < 0.3f) p = 0f;
        }
        else
        {
            p = -p;
            if (p > 0f && fwdVel > -0.3f) p = 0f;
        }

        if (Mathf.Abs(p) > Mathf.Abs(pedals)) pedals = p;
    }

    void OnGUI()
    {
        if (!wheelDebugOverlay || !useWheel) return;
        string txt;
        if (_joyId < 0)
        {
            txt = "[wheel] winmm: no joystick";
        }
        else
        {
            string bind = _steerAxisBound >= 0 ? $"axis {_steerAxisBound}" : "UNBOUND — turn the wheel";
            txt = $"[wheel] gear={(_gear > 0 ? "D" : "R")}  steer={steerInput:F2} ({bind})  throttle={throttleInput:F2}  btn=0x{_dbgButtons:X}   " +
                  $"raw 0X={_axRaw[0]:F0} 1Y={_axRaw[1]:F0} 2Z={_axRaw[2]:F0} 3R={_axRaw[3]:F0}";
        }
        GUI.Box(new Rect(8, 8, 760, 24), "");
        GUI.Label(new Rect(14, 10, 750, 20), txt);
    }
#endif

    void FixedUpdate()
    {
        // —— 1. 计算当前速度（考虑前进/后退）——
        Vector3 velocity = rb.linearVelocity;
        float forwardSpeed = Vector3.Dot(velocity, transform.forward);   // 正=前进，负=后退
        currentSpeed = velocity.magnitude * 3.6f;

        currentAcceleration = (velocity.magnitude - previousSpeed) / Time.fixedDeltaTime;
        previousSpeed = velocity.magnitude;

        // 旧逻辑：速度一到 maxSpeed 就完全停止施力（硬顶），且全程恒力，没有真车
        // 手感。现在：牵引力随速度衰减（低挡扭矩强、接近上限渐弱，自然逼近而非
        // 一刀切），超过 maxSpeed 仍保留一小段递减推力（可短暂超速），风阻/发动机
        // 制动最终把车稳定在略高于 maxSpeed 的平衡速度。
        if (throttleInput > 0f)
        {
            // 归一化速度：0 = 静止，1 = maxSpeed。低速给满扭矩，越接近上限推力越小。
            float sN = Mathf.Clamp01(forwardSpeed / Mathf.Max(1f, maxSpeed));
            // 牵引力系数：1.0 → 0.15（到 maxSpeed），二次衰减模拟高挡乏力
            float pull = Mathf.Lerp(1f, 0.15f, sN * sN);
            if (forwardSpeed > maxSpeed)
            {
                // 允许超速：上限之上推力继续快速衰减，60 km/h 附近归零
                float over = (forwardSpeed - maxSpeed) / Mathf.Max(1f, maxSpeed);
                pull = Mathf.Max(0f, 0.15f - over * 0.5f);
            }
            rb.AddForce(transform.forward * motorForce * throttleInput * pull, ForceMode.Force);
        }
        else if (throttleInput < 0f)
        {
            if (forwardSpeed > 0.5f)
            {
                // 还在前进中，S 先起刹车作用
                rb.AddForce(-transform.forward * brakeForce, ForceMode.Force);
            }
            else if (forwardSpeed > -maxReverseSpeed)
            {
                // 停住或已经在倒车，继续倒车
                rb.AddForce(transform.forward * motorForce * throttleInput * 0.6f, ForceMode.Force);
            }
        }

        // 发动机制动（没踩油门时自然减速）
        rb.linearDamping = (throttleInput == 0f) ? groundedDrag : drag;

        // —— 3. 手刹 ——
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.isPressed)
        {
            rb.linearVelocity *= 0.92f;
        }

        // —— 4. 方向盘平滑响应 ——
        float targetSteerAngle = steerInput * maxSteerAngle;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle,
                                       steeringResponse * Time.fixedDeltaTime);

        // —— 5. 基于行驶方向的真实转向 ——
        if (Mathf.Abs(forwardSpeed) > 0.3f)
        {
            float turnRateRadPerSec = (forwardSpeed / turnRadiusFactor)
                                    * Mathf.Tan(currentSteerAngle * Mathf.Deg2Rad);

            float deltaDegrees = turnRateRadPerSec * Mathf.Rad2Deg * Time.fixedDeltaTime;

            Quaternion turnRotation = Quaternion.Euler(0f, deltaDegrees, 0f);
            rb.MoveRotation(rb.rotation * turnRotation);
        }

        // —— 6. 侧向摩擦（防止车打滑）——
        // 否则每帧消除量超过侧向速度本身会反向过冲并指数放大（旧版崩溃根因）。
        Vector3 rightVelocity = transform.right * Vector3.Dot(velocity, transform.right);
        float grip = Mathf.Clamp01(sidewaysFriction * Time.fixedDeltaTime);
        rb.AddForce(-rightVelocity * grip, ForceMode.VelocityChange);

        // —— 7. 速度安全钳制（防物理爆炸传播）——
        if (rb.linearVelocity.magnitude > absoluteMaxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * absoluteMaxSpeed;
        }
    }
}
