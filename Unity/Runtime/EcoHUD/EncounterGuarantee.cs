using UnityEngine;
#if GLEY_TRAFFIC_SYSTEM
using Gley.TrafficSystem;
using VehicleTypes = Gley.TrafficSystem.User.VehicleTypes;
#endif

public class EncounterGuarantee : MonoBehaviour
{
    [Tooltip("Player car. Found automatically when left empty.")]
    public Transform player;

    [Header("Aperture (mirrors TargetMarkers)")]
    public float apertureYawDeg = 34f;          // deg
    public float maxDistanceM = 60f;            // m
    public LayerMask occluderMask = (1 << 29) | (1 << 0);

    [Header("Spawning")]
    [Tooltip("No vehicle in view for this long -> put one on the road ahead.")]
    public float drySeconds = 12f;              // s
    [Tooltip("How far ahead to look for a spawn waypoint.")]
    public float spawnAheadM = 85f;             // m
    [Tooltip("Shortest gap between two forced spawns.")]
    public float respawnCooldown = 20f;         // s

    float _drySince;
    float _nextSpawnAt;

    void Awake()
    {
        if (player == null)
        {
            var car = FindFirstObjectByType<CarController>();
            if (car != null) player = car.transform;
        }
        _drySince = Time.time;
    }

    void Update()
    {
#if GLEY_TRAFFIC_SYSTEM
        if (player == null || !API.IsInitialized()) return;

        if (AnyVehicleInView())
        {
            _drySince = Time.time;
            return;
        }
        if (Time.time - _drySince < drySeconds || Time.time < _nextSpawnAt) return;

        _nextSpawnAt = Time.time + respawnCooldown;
        _drySince = Time.time;
        SpawnAhead();
#endif
    }

#if GLEY_TRAFFIC_SYSTEM
    bool AnyVehicleInView()
    {
        var all = API.GetAllVehicles();
        if (all == null) return false;
        Vector3 eye = player.position + Vector3.up * 1.3f;   // m
        foreach (var v in all)
        {
            if (v == null || !v.gameObject.activeInHierarchy) continue;
            Vector3 local = player.InverseTransformPoint(v.transform.position);
            if (local.z <= 1f) continue;
            float dist = local.magnitude;
            if (dist > maxDistanceM) continue;
            if (Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg) > apertureYawDeg) continue;
            Vector3 target = v.transform.position + Vector3.up * 0.9f;   // m
            if (Physics.Linecast(eye, target, occluderMask, QueryTriggerInteraction.Ignore)) continue;
            return true;
        }
        return false;
    }

    void SpawnAhead()
    {
        Vector3 ahead = player.position + player.forward * spawnAheadM;
        var wp = API.GetClosestWaypointInDirection(ahead, player.forward);
        if (wp == null) return;
        API.InstantiateVehicle(wp.Position, VehicleTypes.Car);
    }
#endif
}
