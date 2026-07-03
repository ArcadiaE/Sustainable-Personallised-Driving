// =============================================================================
//  MockOptimizerBridge.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  A runnable stand-in for the optimizer so the WHOLE study loop works end-to-end
//  before the real Bayesian-Optimization-for-Unity asset is wired in. It draws each
//  round's 7 parameters at random within their ranges (random search) and logs the
//  objectives. Use it to test RoundController + HUD + autopilot + questionnaire, then
//  swap in BoForUnityBridge for the real personalization.
//
// =============================================================================

using UnityEngine;

public class MockOptimizerBridge : OptimizerBridge
{
    [Header("Budget")]
    public int totalBudget = 15;

    [Header("Parameter ranges [min,max], order: 0 visible,1 size,2 salience,3 style,4 rate,5 valence,6 peer")]
    public Vector2[] ranges = {
        new(0f, 1f), new(0.6f, 1.6f), new(0f, 1f), new(0f, 1f), new(0f, 1f), new(0f, 1f), new(0f, 1f),
    };

    int iter = -1;
    readonly float[] current = new float[ParameterCount];
    bool ready;

    public override int CurrentIteration => Mathf.Max(0, iter);
    public override int TotalBudget => totalBudget;
    public override bool HasParameters => ready;
    public override float GetParameter(int index) => current[Mathf.Clamp(index, 0, ParameterCount - 1)];

    public override void SetObjective(int index, float value)
        => Debug.Log($"[MockBO] round {iter + 1} objective[{index}] = {value:F3}");

    public override void StartOptimization() { iter = -1; Next(); }
    public override void SubmitAndRequestNext() { Next(); }

    void Next()
    {
        iter++;
        if (iter >= totalBudget) { ready = false; Debug.Log("[MockBO] budget reached."); return; }
        for (int i = 0; i < ParameterCount; i++)
        {
            Vector2 r = (i < ranges.Length) ? ranges[i] : new Vector2(0f, 1f);
            current[i] = Random.Range(r.x, r.y);
        }
        ready = true;
        RaiseParametersReady();
    }
}
