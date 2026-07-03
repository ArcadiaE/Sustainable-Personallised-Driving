// =============================================================================
//  BoForUnityBridge.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  Adapter to the "Bayesian Optimization for Unity" asset (Jansen & Colley; BoTorch,
//  qEHVI for multi-objective). Fill the TODO bodies with the asset's real API, then
//  put THIS on the RoundController.optimizer slot instead of MockOptimizerBridge.
//  Everything else (RoundController, HUD, autopilot, questionnaire) stays unchanged.
//
//  The asset talks to a Python/BoTorch server asynchronously, so a new candidate does
//  not arrive on the same frame you request it. Subscribe to the asset's "sample ready"
//  callback and call RaiseParametersReady() from there (see StartOptimization TODO).
//
//  Wiring checklist:
//   - declare 7 parameters and 3 objectives in the asset's manager,
//   - same order as OptimizerBridge (0 visible..6 peer ; 0 energy,1 load,2 acceptance),
//   - set objective directions: energy MIN, load MIN, acceptance MAX,
//   - set the iteration budget there too.
//
// =============================================================================

using UnityEngine;

public class BoForUnityBridge : OptimizerBridge
{
    [Tooltip("Drag the BO-for-Unity manager component here (e.g. BOForUnityManager).")]
    public MonoBehaviour boManager;

    bool warned;

    public override int CurrentIteration => 0;   // TODO: return the asset's current iteration index
    public override int TotalBudget => 0;         // TODO: return the asset's total iteration budget

    public override bool HasParameters => false;  // TODO: true when a fresh candidate is available

    // TODO: return bo.parameters[index].value.Value
    public override float GetParameter(int index) { WarnOnce(); return 0f; }

    // TODO: bo.objectives[index].value.Value = value;
    public override void SetObjective(int index, float value) { WarnOnce(); }

    public override void StartOptimization()
    {
        // TODO: initialize the asset and subscribe to its "new sample ready" event;
        //       from that event handler call RaiseParametersReady() so RoundController
        //       picks up the candidate. Then kick off the first iteration.
        WarnOnce();
    }

    public override void SubmitAndRequestNext()
    {
        // TODO: bo.RequestNextIteration();
        WarnOnce();
    }

    void WarnOnce()
    {
        if (warned) return; warned = true;
        Debug.LogWarning("[BoForUnityBridge] Not wired yet: fill the TODOs to connect the " +
                         "BO-for-Unity asset. Use MockOptimizerBridge to test the loop meanwhile.");
    }
}
