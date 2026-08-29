using BOforUnity;
using UnityEngine;

public class BoForUnityBridge : OptimizerBridge
{
    [Tooltip("The BO-for-Unity manager. Auto-found if left empty.")]
    public BoForUnityManager bo;

    bool candidateSeen;

    public override int CurrentIteration => bo != null ? Mathf.Max(0, bo.currentIteration - 1) : 0;
    public override int TotalBudget => bo != null ? bo.totalIterations : 0;
    public override bool HasParameters => bo != null && bo.initialized && bo.simulationRunning;

    public override bool IsFinished =>
        bo != null && bo.optimizationFinished &&
        !bo.simulationRunning && !bo.optimizationRunning && !bo.hasNewDesignParameterValues;

    public override float GetParameter(int index)
    {
        if (bo == null || index < 0 || index >= bo.parameters.Count) return 0f;
        return bo.parameters[index].value.Value;
    }

    public override void SetObjective(int index, float value)
    {
        if (bo == null || index < 0 || index >= bo.objectives.Count) return;
        bo.objectives[index].value.values.Add(value);
    }

    public override void StartOptimization()
    {
        if (bo == null) bo = FindFirstObjectByType<BoForUnityManager>();
        if (bo == null)
        {
            Debug.LogError("[BoForUnityBridge] No BoForUnityManager in the scene.");
            return;
        }
        if (bo.parameters.Count != ParameterCount || bo.objectives.Count != ObjectiveCount)
            Debug.LogWarning($"[BoForUnityBridge] Manager declares {bo.parameters.Count} parameters / " +
                             $"{bo.objectives.Count} objectives; expected {ParameterCount}/{ObjectiveCount}. " +
                             "Check the Inspector lists (order matters).");
        if (bo.iterationAdvanceMode != BoForUnityManager.IterationAdvanceMode.Automatic)
            Debug.LogWarning("[BoForUnityBridge] Set Iteration Advance Mode = Automatic on the manager " +
                             "so rounds advance without a UI button.");
        if (bo.reloadSceneOnIterationAdvance)
            Debug.LogWarning("[BoForUnityBridge] Turn OFF 'Reload Scene On Iteration Advance' on the manager; " +
                             "RoundController runs every round inside the live scene.");
    }

    void Update()
    {
        if (bo == null) return;
        if (bo.initialized && bo.simulationRunning && !candidateSeen)
        {
            candidateSeen = true;
            RaiseParametersReady();
        }
        else if (!bo.simulationRunning)
        {
            candidateSeen = false;
        }
    }

    public override void SubmitAndRequestNext()
    {
        if (bo == null) return;
        bo.OptimizationStart();
    }
}
