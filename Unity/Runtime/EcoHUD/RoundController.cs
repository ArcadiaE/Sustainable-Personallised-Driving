// =============================================================================
//  RoundController.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  Works with MockOptimizerBridge (runs now, random search) or BoForUnityBridge (real BO).
//  References auto-resolve via FindFirstObjectByType, or assign them in the Inspector.
//
// =============================================================================

using UnityEngine;

public class RoundController : MonoBehaviour
{
    [Header("References (auto-found if left empty)")]
    public OptimizerBridge optimizer;
    public EcoScore eco;
    public EcoFeedbackHUD hud;
    public AutoDriver autopilot;
    public StudyQuestionnaire questionnaire;

    [Header("Round settings")]
    public bool autoDrive = true;     // true = autopilot drives the lap (demo); false = participant drives
    public int lapsPerRound = 1;
    public bool startOnPlay = true;

    public enum Phase { Idle, Driving, Survey, WaitingNext, Done }
    public Phase phase { get; private set; } = Phase.Idle;

    int lapsDone;
    float roundEnergy;

    void Awake()
    {
        if (eco == null) eco = FindFirstObjectByType<EcoScore>();
        if (hud == null) hud = FindFirstObjectByType<EcoFeedbackHUD>();
        if (autopilot == null) autopilot = FindFirstObjectByType<AutoDriver>();
        if (questionnaire == null) questionnaire = FindFirstObjectByType<StudyQuestionnaire>();
        if (optimizer == null) optimizer = FindFirstObjectByType<OptimizerBridge>();
    }

    void OnEnable()
    {
        if (optimizer != null) optimizer.OnParametersReady += BeginRound;
        if (autopilot != null) autopilot.onLapComplete += OnLap;
    }

    void OnDisable()
    {
        if (optimizer != null) optimizer.OnParametersReady -= BeginRound;
        if (autopilot != null) autopilot.onLapComplete -= OnLap;
    }

    void Start()
    {
        if (startOnPlay) StartStudy();
    }

    public void StartStudy()
    {
        if (optimizer == null) { Debug.LogError("[RoundController] No OptimizerBridge assigned."); return; }
        optimizer.StartOptimization();   // -> OnParametersReady -> BeginRound
    }

    // A new candidate is ready: apply its parameters and run the round.
    void BeginRound()
    {
        if (hud != null)
            hud.ApplyDesignParams(
                optimizer.GetParameter(0), optimizer.GetParameter(1), optimizer.GetParameter(2),
                optimizer.GetParameter(3), optimizer.GetParameter(4), optimizer.GetParameter(5),
                optimizer.GetParameter(6));

        if (eco != null) eco.ResetRound();
        lapsDone = 0;

        if (autopilot != null)
        {
            autopilot.ResetRoute();
            autopilot.SnapToStart();
            autopilot.engaged = autoDrive;
        }

        phase = Phase.Driving;
        Debug.Log($"[RoundController] round {optimizer.CurrentIteration + 1}/{optimizer.TotalBudget} — driving.");
    }

    void OnLap()
    {
        if (phase != Phase.Driving) return;
        lapsDone++;
        if (lapsDone >= lapsPerRound) EndDriving();
    }

    void EndDriving()
    {
        if (autopilot != null) autopilot.engaged = false;
        roundEnergy = (eco != null) ? eco.GetRoundEnergyPer100km() : 0f;
        phase = Phase.Survey;

        if (questionnaire != null) questionnaire.Show(OnSurveyDone);
        else OnSurveyDone(50f, 50f);   // no questionnaire wired: neutral, keep the loop moving
    }

    // taskLoad: lower = better (MINIMIZE). acceptance: higher = better (MAXIMIZE).
    void OnSurveyDone(float taskLoad, float acceptance)
    {
        optimizer.SetObjective(0, roundEnergy);   // energy kWh/100km -> MINIMIZE
        optimizer.SetObjective(1, taskLoad);      // NASA-TLX         -> MINIMIZE
        optimizer.SetObjective(2, acceptance);    // van der Laan     -> MAXIMIZE

        if (optimizer.CurrentIteration + 1 >= optimizer.TotalBudget)
        {
            phase = Phase.Done;
            Debug.Log("[RoundController] Study complete.");
            return;
        }

        phase = Phase.WaitingNext;
        optimizer.SubmitAndRequestNext();   // -> OnParametersReady -> BeginRound
    }
}
