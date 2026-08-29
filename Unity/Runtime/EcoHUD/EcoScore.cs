using UnityEngine;

[DefaultExecutionOrder(50)]
public class EcoScore : MonoBehaviour
{
    [Header("Source")]
    public CarController car;

    [Header("Vehicle (road-load) parameters")]
    public float mass = 1500f;                // kg
    public float dragCoeff = 0.30f;           // Cd
    public float frontalArea = 2.2f;          // m^2
    public float rollingResist = 0.012f;      // Cr
    public float airDensity = 1.225f;         // kg/m^3
    [Range(0.5f, 1f)] public float drivetrainEff = 0.90f; // eta (motoring)
    [Range(0f, 1f)] public float regenEff = 0.6f;
    const float G = 9.81f;

    [Header("Real-time score (asymmetric Gaussian velocity peak)")]
    public float targetSpeedKmh = 45f;                     // km/h
    public float sigmaLowKmh = 14f;                        // km/h
    public float sigmaHighKmh = 4.5f;                      // km/h
    [Tooltip("Velocity-score weight (alpha in Eq. 3; the paper uses 1).")]
    public float weightVelocity = 1f;
    [Tooltip("Acceleration-score weight (beta in Eq. 3; the paper uses 1).")]
    public float weightAcceleration = 1f;
    public float scoreSmoothing = 3f;
    public float idleSpeed = 0.5f;

    [Header("Attribution (which behaviour is costing points, ~10 s memory)")]
    public float attributionTau = 10f;
    public enum EcoIssue { None, Speed, Accel, Brake }
    public float speedPenalty { get; private set; }
    public float accelPenalty { get; private set; }
    public float brakePenalty { get; private set; }
    [Tooltip("Read-only: true while the current velocity-score loss comes from driving too SLOWLY (city adaptation) — the HUD advice must not say 'slow down' then.")]
    public bool speedLossIsUnder { get; private set; }

    [Header("Read-only state")]
    public float ecoScore = 100f;
    public float energyWh;
    public float distanceKm;                  // cumulative distance
    public float energyPer100km;
    public float instantPowerW;

    void Awake()
    {
        if (car == null) car = FindFirstObjectByType<CarController>();
    }

    void FixedUpdate()
    {
        if (car == null) return;
        float dt = Time.fixedDeltaTime;
        float v = car.currentSpeed / 3.6f;
        float a = car.currentAcceleration;    // m/s^2

        float fRoll = rollingResist * mass * G;
        float fAero = 0.5f * airDensity * dragCoeff * frontalArea * v * v;
        float fResist = fRoll + fAero;
        float fTotal = mass * a + fResist;    // total tractive force
        float power = fTotal * v;             // W at the wheel
        instantPowerW = power;

        float dE = (power >= 0f)
            ? power / Mathf.Max(0.1f, drivetrainEff) * dt
            : regenEff * power * dt;          // negative dE = recovered
        energyWh += dE / 3600f;               // J -> Wh
        distanceKm += v * dt / 1000f;
        if (distanceKm > 1e-4f)
            energyPer100km = (energyWh / 1000f) / distanceKm * 100f; // kWh/100km

        // ---- real-time eco-score --------------------------------------------------
        float vKmh = car.currentSpeed;
        speedLossIsUnder = vKmh < targetSpeedKmh;
        float vScore = VelocityScore(vKmh, targetSpeedKmh, sigmaLowKmh, sigmaHighKmh);
        float aScore = AccelScore(a);

        float inst = ScoreFrom(vKmh, a, targetSpeedKmh, sigmaLowKmh, sigmaHighKmh,
                               weightVelocity, weightAcceleration);
        if (v < idleSpeed) inst = ecoScore;              // idle: hold the needle
        ecoScore = Mathf.Lerp(ecoScore, inst, Mathf.Clamp01(scoreSmoothing * dt));

        float decay = Mathf.Exp(-dt / Mathf.Max(0.1f, attributionTau));
        speedPenalty *= decay; accelPenalty *= decay; brakePenalty *= decay;
        speedPenalty += (100f - vScore) * dt;
        if (a > 0.01f) accelPenalty += (100f - aScore) * dt;
        else if (a < -0.01f) brakePenalty += (100f - aScore) * dt;
    }

    public static float VelocityScore(float vKmh, float targetSpeedKmh, float sigmaLowKmh, float sigmaHighKmh)
    {
        vKmh = Mathf.Max(0f, vKmh);
        float delta = vKmh - targetSpeedKmh;
        float sigma = Mathf.Max(0.1f, vKmh <= targetSpeedKmh ? sigmaLowKmh : sigmaHighKmh);
        return Mathf.Clamp(100f * Mathf.Exp(-(delta * delta) / (2f * sigma * sigma)), 0f, 100f);
    }

    public static float AccelScore(float aMs2)
    {
        float absA = Mathf.Abs(aMs2);
        if (absA < 0.01f) return 100f;
        if (absA > 2.78f) return 0f;
        return 100f - (absA - 0.01f) * 36.1f;
    }

    public static float ScoreFrom(float vKmh, float aMs2, float targetSpeedKmh,
                                  float sigmaLowKmh, float sigmaHighKmh,
                                  float wV = 1f, float wA = 1f)
    {
        float vs = VelocityScore(vKmh, targetSpeedKmh, sigmaLowKmh, sigmaHighKmh);
        float as_ = AccelScore(aMs2);
        return (wV * vs + wA * as_) / Mathf.Max(0.01f, wV + wA);
    }

    public EcoIssue GetDominantIssue()
    {
        float m = Mathf.Max(speedPenalty, Mathf.Max(accelPenalty, brakePenalty));
        if (m < 3f) return EcoIssue.None;
        if (m == speedPenalty) return EcoIssue.Speed;
        return m == accelPenalty ? EcoIssue.Accel : EcoIssue.Brake;
    }

    public void ResetRound()
    {
        energyWh = 0f; distanceKm = 0f; energyPer100km = 0f; ecoScore = 100f;
        speedPenalty = accelPenalty = brakePenalty = 0f;
    }

    public float GetRoundEnergyPer100km() => energyPer100km;
}
