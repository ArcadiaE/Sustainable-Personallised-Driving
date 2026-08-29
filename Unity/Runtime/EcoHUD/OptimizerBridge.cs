using System;
using UnityEngine;

public abstract class OptimizerBridge : MonoBehaviour
{
    public const int ParameterCount = 7;
    public const int ObjectiveCount = 5;

    public abstract int CurrentIteration { get; }
    public abstract int TotalBudget { get; }

    public abstract bool HasParameters { get; }
    public virtual bool IsFinished => false;
    public abstract float GetParameter(int index);
    public abstract void SetObjective(int index, float value);

    public abstract void StartOptimization();       // produce the first candidate
    public abstract void SubmitAndRequestNext();

    public event Action OnParametersReady;
    protected void RaiseParametersReady() => OnParametersReady?.Invoke();
}
