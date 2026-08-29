using UnityEngine;

public class CockpitWheelSync : MonoBehaviour
{
    public CarController car;
    [Tooltip("Left empty: found by name under the car.")]
    public Transform wheel;
    public string wheelObjectName = "RMCar05_SteeringWheel";
    [Tooltip("Rim angle at full steerInput. 450 deg matches the G25's 900 deg lock-to-lock with steerRange 1.")]
    public float lockAngleDeg = 450f;          // deg
    [Tooltip("Tick if the modelled rim turns the wrong way.")]
    public bool invert = false;
    [Tooltip("Visual smoothing only. Higher = snappier; the physical rim has no lag, so keep this high.")]
    public float responseRate = 24f;

    Quaternion _rest;
    float _shownDeg;                            // deg
    bool _ready;

    void Awake()
    {
        if (car == null) car = FindFirstObjectByType<CarController>();
        if (wheel == null && car != null)
        {
            foreach (var t in car.GetComponentsInChildren<Transform>(true))
                if (t.name == wheelObjectName) { wheel = t; break; }
        }
        if (wheel != null) { _rest = wheel.localRotation; _ready = true; }
        else Debug.LogWarning("[CockpitWheelSync] '" + wheelObjectName + "' not found under the car.");
    }

    void LateUpdate()
    {
        if (!_ready || car == null) return;
        float s = Mathf.Clamp(car.steerInput, -1f, 1f);
        float curve = Mathf.Max(1f, car.steerCurve);
        float raw = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 1f / curve) * Mathf.Clamp(car.steerRange, 0.05f, 1f);
        float target = (invert ? raw : -raw) * lockAngleDeg;   // deg
        _shownDeg = Mathf.Lerp(_shownDeg, target, Mathf.Clamp01(responseRate * Time.deltaTime));
        wheel.localRotation = _rest * Quaternion.Euler(0f, 0f, _shownDeg);
    }
}
