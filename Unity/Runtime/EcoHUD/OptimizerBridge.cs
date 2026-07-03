// =============================================================================
//  OptimizerBridge.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  The seam between the study loop (RoundController) and the optimizer. Keeping it
//  abstract lets the same RoundController run against either the runnable stand-in
//  (MockOptimizerBridge) or the real "Bayesian Optimization for Unity" asset
//  (BoForUnityBridge), without changing any other code.
//
//  Design space (7 continuous parameters, in this fixed order everywhere):
//    0 visible · 1 size · 2 salience · 3 style · 4 rate · 5 valence · 6 peer
//  Objectives (3, multi-objective EHVI):
//    0 energy kWh/100km (MINIMIZE) · 1 task load NASA-TLX (MINIMIZE) · 2 acceptance van der Laan (MAXIMIZE)
//
// =============================================================================

using System;
using UnityEngine;

public abstract class OptimizerBridge : MonoBehaviour
{
    public const int ParameterCount = 7;
    public const int ObjectiveCount = 3;

    public abstract int CurrentIteration { get; }   // 0-based index of the round being run
    public abstract int TotalBudget { get; }        // total rounds (the optimization budget)

    public abstract bool HasParameters { get; }     // a candidate is ready to read
    public abstract float GetParameter(int index);  // candidate value for design parameter [index]
    public abstract void SetObjective(int index, float value); // record one objective for this round

    public abstract void StartOptimization();       // produce the first candidate
    public abstract void SubmitAndRequestNext();    // commit the objectives, ask for the next candidate

    // Raised when a new candidate's parameters are ready to apply.
    public event Action OnParametersReady;
    protected void RaiseParametersReady() => OnParametersReady?.Invoke();
}
