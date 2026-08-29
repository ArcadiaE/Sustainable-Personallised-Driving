using UnityEngine;

public class YawMotion : MonoBehaviour
{
    [Header("Source (auto-found)")]
    public CarController car;
    public AutoDriver driver;

    [Header("Tilt coordination")]
    [Tooltip("Degrees of backward pitch per m/s^2 of forward acceleration.")]
    public float pitchPerAccel = 0.5f;
    [Tooltip("Degrees of roll per m/s^2 of lateral acceleration.")]
    public float rollPerAccel = 0.7f;
    public float maxPitchDeg = 3f;      // deg
    public float maxRollDeg = 4f;       // deg

    [Header("Comfort filtering")]
    [Tooltip("Low-pass rate for the acceleration inputs (higher = snappier).")]
    public float inputSmoothing = 2.5f;
    [Tooltip("Max platform angular speed for pitch/roll (deg/s).")]
    public float maxTiltRateDegS = 8f;  // deg/s
    [Tooltip("Output easing time constant (s): the pose is critically damped toward its target, so every move starts and ends without a jolt. Return-to-neutral (washout) is the same glide toward zero.")]
    public float tiltSmoothTime = 0.35f;    // s
    public float yawSmoothTime = 0.15f;     // s

    [Header("Yaw (washout — cables cannot take unbounded rotation)")]
    [Tooltip("Degrees of platform yaw per deg/s of the car's turn rate.")]
    // the chair hold flat.
    public float yawAtLockDeg = 55f;
    [Tooltip("Hard limit on platform yaw either side of home. The headset and wheel cables bound this, not the Yaw3.")]
    public float maxYawDeg = 16f;           // deg
    [Tooltip("Max platform yaw speed (deg/s).")]
    public float maxYawRateDegS = 30f;      // deg/s

    [Header("Stall guard")]
    [Tooltip("Upper bound on the frame step fed to the output filter (s). The rate caps above are enforced PER FRAME, so a stalled frame spends the whole cap in one step: at Unity's default 0.333 s maximumDeltaTime the 30 deg/s yaw cap becomes a 10 deg jump, which the platform then executes at ITS top speed, not at 30 deg/s. Clamping the step trades a little lag during a hitch for a pose that can never jump.")]
    public float maxFrameStepS = 0.05f;     // s

    [Header("Diagnostics")]
    public bool debugLog = false;

    float _longAccel, _latAccel;            // smoothed inputs
    float _pitch, _roll, _yaw;              // current output pose
    float _targetPitch, _targetRoll, _targetYaw;
    float _pitchVel, _rollVel, _yawVel;     // SmoothDamp state, deg/s
    float _suppressUntil;                   // s
    Vector3 _prevVel;
    float _logTimer;

    public void SuppressForSeconds(float seconds)
    {
        _suppressUntil = Time.time + Mathf.Max(0f, seconds);
        _targetPitch = _targetRoll = _targetYaw = 0f;
        _longAccel = _latAccel = 0f;
        _prevVel = Vector3.zero;
    }

    void Awake()
    {
        if (car == null) car = FindFirstObjectByType<CarController>();
        if (driver == null) driver = FindFirstObjectByType<AutoDriver>();
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, Mathf.Max(0.001f, maxFrameStepS));
        _pitch = Mathf.SmoothDampAngle(_pitch, _targetPitch, ref _pitchVel, tiltSmoothTime, maxTiltRateDegS, dt);
        _roll = Mathf.SmoothDampAngle(_roll, _targetRoll, ref _rollVel, tiltSmoothTime, maxTiltRateDegS, dt);
        _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVel, yawSmoothTime, maxYawRateDegS, dt);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, _roll);
    }

    void FixedUpdate()
    {
        if (car == null || driver == null) return;
        Transform carT = driver.transform;
        var rb = carT.GetComponent<Rigidbody>();
        if (rb == null) return;
        if (Time.time < _suppressUntil)
        {
            _prevVel = rb.linearVelocity;
            return;
        }

        Vector3 vel = rb.linearVelocity;
        Vector3 accelWorld = (vel - _prevVel) / Mathf.Max(0.0001f, Time.fixedDeltaTime);
        _prevVel = vel;
        float rawLong = Vector3.Dot(accelWorld, carT.forward);
        float rawLat = Vector3.Dot(accelWorld, carT.right);

        float k = Mathf.Clamp01(inputSmoothing * Time.fixedDeltaTime);
        _longAccel = Mathf.Lerp(_longAccel, rawLong, k);
        _latAccel = Mathf.Lerp(_latAccel, rawLat, k);

        float targetPitch = Mathf.Clamp(-_longAccel * pitchPerAccel, -maxPitchDeg, maxPitchDeg);
        float targetRoll = Mathf.Clamp(_latAccel * rollPerAccel, -maxRollDeg, maxRollDeg);

        _targetPitch = targetPitch;
        _targetRoll = targetRoll;

        float vLong = Vector3.Dot(rb.linearVelocity, carT.forward);                 // m/s, signed
        float speedFactor = Mathf.Clamp01(Mathf.Abs(vLong) / 2f);
        float sIn = Mathf.Clamp(car.steerInput, -1f, 1f);
        float rim = Mathf.Sign(sIn) * Mathf.Pow(Mathf.Abs(sIn), 1f / Mathf.Max(1f, car.steerCurve));
        float headingDir = vLong < 0f ? -1f : 1f;
        _targetYaw = Mathf.Clamp(rim * yawAtLockDeg, -maxYawDeg, maxYawDeg) * speedFactor * headingDir;

        if (debugLog)
        {
            _logTimer += Time.fixedDeltaTime;
            if (_logTimer > 0.5f)
            {
                _logTimer = 0f;
                Debug.Log($"[YawMotion] yaw={_yaw:F1} pitch={_pitch:F1} roll={_roll:F1} " +
                          $"(steer={car.currentSteerAngle:F0} deg  long={_longAccel:F2} lat={_latAccel:F2} m/s²)");
            }
        }
    }
}
