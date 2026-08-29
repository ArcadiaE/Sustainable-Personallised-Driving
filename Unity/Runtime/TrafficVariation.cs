using UnityEngine;
#if GLEY_TRAFFIC_SYSTEM
using Gley.TrafficSystem;
#endif

public class TrafficVariation : MonoBehaviour
{
    [Header("Speed variation (never above the waypoint limit)")]
    [Tooltip("Biggest personal slowdown. 0.35 = each car cruises at 65-100% of the road limit.")]
    [Range(0f, 0.6f)] public float maxSlowdown = 0.35f;
    [Tooltip("Seconds between re-rolls of a vehicle's personal speed (so it drifts over time).")]
    public float retargetInterval = 15f;

    [Header("Queue dissolver (behind the player)")]
    public bool dissolveQueues = true;
    [Tooltip("Only vehicles within this many metres of the player count as queued.")]
    public float queueRadius = 45f;
    [Tooltip("Vehicle counts as crawling below this speed (km/h).")]
    public float stuckSpeedKmh = 10f;
    [Tooltip("Crawling-behind-the-player time before a vehicle may be relocated.")]
    public float stuckSeconds = 12f;
    [Tooltip("Never relocate more than one vehicle per this many seconds (keeps it invisible).")]
    public float removeCooldown = 4f;

    [Header("Self-healing (anywhere in the district)")]
    [Tooltip("Any vehicle stationary this long (wedged after a collision, blocked forever) is relocated when off camera. Intersection waits are far shorter, so this only catches pathology.")]
    public float stallSeconds = 45f;

    [Header("Gridlock breaker (around a stuck player)")]
    [Tooltip("Player counts as stuck after standing still this long. The study car brakes for same-direction traffic, so a stalled ambient car pins the player (and the round) forever.")]
    public float playerStuckSeconds = 8f;
    [Tooltip("While the player is stuck, stalled vehicles within this radius are relocated EVEN ON CAMERA - a frozen round is worse than a visible despawn.")]
    public float blockRadius = 35f;
    [Tooltip("A nearby vehicle must itself be stationary this long before it is treated as part of the gridlock.")]
    public float blockSeconds = 15f;

#if GLEY_TRAFFIC_SYSTEM
    Transform _player;
    Camera _cam;
    float[] _nextRetarget;
    float[] _stuckTime;
    float[] _stallTime;
    float _lastRemove;
    float _playerStillTime;
    Vector3 _lastPlayerPos;

    void Start()
    {
        var car = FindFirstObjectByType<CarController>();
        if (car != null) _player = car.transform;
        Events.OnVehicleActivated += OnVehicleActivated;
    }

    void OnDestroy()
    {
        Events.OnVehicleActivated -= OnVehicleActivated;
    }

    void OnVehicleActivated(int vehicleIndex, int waypointIndex)
    {
        API.SetSpeedVariationPercentage(vehicleIndex, Random.Range(0f, maxSlowdown), 0f);
        if (_nextRetarget != null && vehicleIndex < _nextRetarget.Length)
            _nextRetarget[vehicleIndex] = Time.time + Random.Range(0.5f, 1.5f) * retargetInterval;
    }

    void Update()
    {
        if (!API.IsInitialized()) return;
        var vehicles = API.GetAllVehicles();
        if (vehicles == null) return;

        if (_nextRetarget == null || _nextRetarget.Length != vehicles.Length)
        {
            _nextRetarget = new float[vehicles.Length];
            _stuckTime = new float[vehicles.Length];
            _stallTime = new float[vehicles.Length];
            for (int i = 0; i < vehicles.Length; i++)
                _nextRetarget[i] = Time.time + Random.Range(0f, retargetInterval);
        }

        if (_cam == null) _cam = Camera.main;

        if (_player != null)
        {
            bool still = (_player.position - _lastPlayerPos).magnitude < 0.5f * Time.deltaTime + 0.02f;
            _playerStillTime = still ? _playerStillTime + Time.deltaTime : 0f;
            _lastPlayerPos = _player.position;
        }

        for (int i = 0; i < vehicles.Length; i++)
        {
            var v = vehicles[i];
            if (v == null || !v.gameObject.activeInHierarchy)
            {
                if (_stuckTime != null) _stuckTime[i] = 0f;
                if (_stallTime != null) _stallTime[i] = 0f;
                continue;
            }

            _stallTime[i] = v.GetCurrentSpeedMS() < 0.5f ? _stallTime[i] + Time.deltaTime : 0f;

            if (_playerStillTime > playerStuckSeconds &&
                _stallTime[i] > blockSeconds &&
                _player != null &&
                (v.transform.position - _player.position).magnitude < blockRadius &&
                Time.time - _lastRemove > 1.5f)
            {
                _stallTime[i] = 0f;
                _lastRemove = Time.time;
                API.RemoveVehicle(i);
                continue;
            }

            if (_stallTime[i] > stallSeconds &&
                Time.time - _lastRemove > removeCooldown &&
                (_player == null || (v.transform.position - _player.position).magnitude > 25f) &&
                !VisibleOnCamera(v.transform.position))
            {
                _stallTime[i] = 0f;
                _lastRemove = Time.time;
                API.RemoveVehicle(i);
                continue;
            }

            if (Time.time >= _nextRetarget[i])
            {
                API.SetSpeedVariationPercentage(i, Random.Range(0f, maxSlowdown), 0f);
                _nextRetarget[i] = Time.time + Random.Range(0.7f, 1.3f) * retargetInterval;
            }

            if (!dissolveQueues || _player == null) continue;

            Vector3 toVehicle = v.transform.position - _player.position;
            bool queued =
                toVehicle.magnitude < queueRadius &&
                Vector3.Dot(_player.forward, toVehicle) < -2f &&            // clearly behind the player
                v.GetCurrentSpeedMS() * 3.6f < stuckSpeedKmh;               // crawling

            _stuckTime[i] = queued ? _stuckTime[i] + Time.deltaTime : 0f;

            if (_stuckTime[i] > stuckSeconds &&
                Time.time - _lastRemove > removeCooldown &&
                !VisibleOnCamera(v.transform.position))
            {
                _stuckTime[i] = 0f;
                _lastRemove = Time.time;
                API.RemoveVehicle(i);
            }
        }
    }

    bool VisibleOnCamera(Vector3 worldPos)
    {
        if (_cam == null) return false;
        Vector3 vp = _cam.WorldToViewportPoint(worldPos);
        return vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
    }
#endif
}
