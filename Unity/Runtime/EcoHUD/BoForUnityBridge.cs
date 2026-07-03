// =============================================================================
//  BoForUnityBridge.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
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
