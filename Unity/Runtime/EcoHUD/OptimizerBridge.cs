// =============================================================================
//  OptimizerBridge.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
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
