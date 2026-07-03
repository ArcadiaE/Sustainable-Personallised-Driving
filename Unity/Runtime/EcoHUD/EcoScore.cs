// =============================================================================
//  EcoScore.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  Eco-driving energy metric, from the STANDARD road-load (longitudinal vehicle
//  dynamics) model, NOT a hand-made scoring formula:
//      F = m*a + Cr*m*g + 0.5*rho*Cd*A*v^2     (inertia + rolling + aero)
//      P = F*v                                  (wheel power)
//      E = integral( P / eta ) dt , with regen recovery on braking (EV)
//  Inputs come from CarController (currentSpeed km/h, currentAcceleration m/s^2).
//  This produces TWO things:
//    1. a real-time EcoScore (0-100, smoothed) for the HUD to display;
//    2. a cumulative energy intensity (kWh/100km) over a round, which is the
//       behavioural OBJECTIVE written back to the Bayesian optimizer.
//  Only the parameter values and the application are adapted to this simulation
//  (an arcade-physics car), so it serves as a standardized eco-driving proxy.
//
//  Drive comparability: run the SAME fixed route each round so kWh/100km is comparable.
//
// =============================================================================

using UnityEngine;

[DefaultExecutionOrder(50)]
public class EcoScore : MonoBehaviour
{
    [Header("Source")]
    public CarController car;                 // auto-found if left null

    [Header("Vehicle (road-load) parameters")]
    public float mass = 1500f;                // kg
    public float dragCoeff = 0.30f;           // Cd
    public float frontalArea = 2.2f;          // m^2
    public float rollingResist = 0.012f;      // Cr
    public float airDensity = 1.225f;         // kg/m^3
    [Range(0.5f, 1f)] public float drivetrainEff = 0.90f; // eta (motoring)
    [Range(0f, 1f)] public float regenEff = 0.6f;         // braking recovery (0 = ICE, no regen)
    const float G = 9.81f;

    [Header("Real-time score mapping (for the HUD)")]
    public float scoreSmoothing = 3f;         // higher = snappier needle
    public float wasteSpan = 2.0f;            // extra-power ratio that drops the score to 0
    public float idleSpeed = 0.5f;            // m/s, below this treat as idle

    [Header("Read-only state")]
    public float ecoScore = 100f;             // 0-100 smoothed, for the HUD
    public float energyWh;                    // cumulative energy (Wh), net of regen
    public float distanceKm;                  // cumulative distance
    public float energyPer100km;              // kWh/100km, the round objective source
    public float instantPowerW;               // current wheel power (debug)

    void Awake()
    {
        if (car == null) car = FindFirstObjectByType<CarController>();
    }

    void FixedUpdate()
    {
        if (car == null) return;
        float dt = Time.fixedDeltaTime;
        float v = car.currentSpeed / 3.6f;    // km/h -> m/s
        float a = car.currentAcceleration;    // m/s^2

        float fRoll = rollingResist * mass * G;
        float fAero = 0.5f * airDensity * dragCoeff * frontalArea * v * v;
        float fResist = fRoll + fAero;        // force just to hold speed
        float fTotal = mass * a + fResist;    // total tractive force
        float power = fTotal * v;             // W at the wheel
        instantPowerW = power;

        // cumulative energy: motoring divided by efficiency; braking partly recovered (regen)
        float dE = (power >= 0f)
            ? power / Mathf.Max(0.1f, drivetrainEff) * dt
            : regenEff * power * dt;          // negative dE = recovered
        energyWh += dE / 3600f;               // J -> Wh
        distanceKm += v * dt / 1000f;
        if (distanceKm > 1e-4f)
            energyPer100km = (energyWh / 1000f) / distanceKm * 100f; // kWh/100km

        // real-time eco-score: how much MORE power than needed to hold speed (the controllable waste)
        float inst;
        if (v < idleSpeed)
        {
            inst = ecoScore;                  // idle: hold the needle
        }
        else
        {
            float pResist = Mathf.Max(50f, fResist * v);  // power to cruise (floored)
            float ratio = power / pResist;                // 1 = cruise, >1 accel, <1 coast/regen
            float waste = Mathf.Max(0f, ratio - 1f);
            inst = 100f * Mathf.Clamp01(1f - waste / Mathf.Max(0.1f, wasteSpan));
        }
        ecoScore = Mathf.Lerp(ecoScore, inst, Mathf.Clamp01(scoreSmoothing * dt));
    }

    // Start a new optimization round.
    public void ResetRound()
    {
        energyWh = 0f; distanceKm = 0f; energyPer100km = 0f; ecoScore = 100f;
    }

    // Behavioural objective for the optimizer: energy intensity over the round (MINIMIZE).
    public float GetRoundEnergyPer100km() => energyPer100km;
}
