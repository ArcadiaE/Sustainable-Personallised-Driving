#if GLEY_TRAFFIC_SYSTEM
using Gley.TrafficSystem;
using Unity.Mathematics;

public class EdgePull : VehicleBehaviour
{
    const float TargetSpeedMS = 15f / 3.6f;

    protected override void OnBecomeActive()
    {
        base.OnBecomeActive();
        VehicleComponent.MovementInfo.SetOffset(1f);
    }

    protected override void OnBecameInactive()
    {
        base.OnBecameInactive();
        VehicleComponent.MovementInfo.ResetOffset();
    }

    public override BehaviourResult Execute(MovementInfo knownWaypointsList, float requiredBrakePower, bool stopTargetReached, float3 stopPosition, int currentGear)
    {
        var result = new BehaviourResult();
        PerformForwardMovement(ref result, TargetSpeedMS, TargetSpeedMS,
            stopPosition, requiredBrakePower, 0.5f, VehicleComponent.distanceToStop);
        return result;
    }

    public override void OnDestroy() { }
}
#endif
