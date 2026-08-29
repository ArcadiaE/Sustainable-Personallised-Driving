using System.Collections.Generic;
using UnityEngine;
#if GLEY_TRAFFIC_SYSTEM
using Gley.TrafficSystem;
#endif

public class MeetingResolver : MonoBehaviour
{
#if GLEY_TRAFFIC_SYSTEM
    [Header("Head-on detection")]
    public float scanInterval = 0.3f;
    [Tooltip("Engage range. 20 m reacted too late at ~10 m/s closing speeds — cars met mid-pull; 28 m buys ~1 s more of edging.")]
    public float detectDist = 28f;
    public float releaseDist = 33f;
    public float headOnDot = -0.6f;
    [Tooltip("Projected passing gap that counts as a conflict. 2.2 missed real scrapes started from wider offsets on bends; 2.6 engages those too.")]
    public float lateralLimit = 2.6f;

    [Header("Deadlock backstop (never reverse, never wait forever)")]
    public float deadlockDist = 6f;
    public float deadlockSpeedMS = 0.5f;
    public float deadlockSeconds = 8f;

    float _nextScan;
    bool _injected;
    Camera _cam;
    readonly HashSet<long> _activePairs = new();
    readonly Dictionary<long, float> _stillSince = new();
    readonly Dictionary<int, int> _pullRefs = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var tc = FindFirstObjectByType<TrafficComponent>(FindObjectsInactive.Include);
        if (tc == null) return;                       // scene without ambient traffic
        if (tc.GetComponent<MeetingResolver>() == null)
            tc.gameObject.AddComponent<MeetingResolver>();
    }

    void Update()
    {
        if (!API.IsInitialized()) return;

        if (!_injected)
        {
            _injected = true;
            API.SetAllVehiclesBehaviours(new StudyVehicleBehaviours());
            StartCoroutine(RestartBaseline());
            Debug.Log("[MeetingResolver] behaviour list injected: stock defaults + EdgePull");
        }

        if (Time.time < _nextScan) return;
        _nextScan = Time.time + scanInterval;

        var vehicles = API.GetAllVehicles();
        if (vehicles == null) return;
        if (_cam == null) _cam = Camera.main;

        for (int i = 0; i < vehicles.Length; i++)
        {
            var a = vehicles[i];
            if (a == null || !a.gameObject.activeInHierarchy) { ClearVehicle(i); continue; }
            for (int j = i + 1; j < vehicles.Length; j++)
            {
                var b = vehicles[j];
                if (b == null || !b.gameObject.activeInHierarchy) continue;

                long key = ((long)i << 20) | (uint)j;
                bool active = _activePairs.Contains(key);

                Vector3 pa = a.transform.position, pb = b.transform.position;
                Vector3 d = pb - pa;
                float dist = d.magnitude;
                if (dist > (active ? releaseDist : detectDist))
                {
                    if (active) Release(key, i, j);
                    continue;
                }

                Vector3 fa = a.transform.forward, fb = b.transform.forward;
                bool headOn = Vector3.Dot(fa, fb) < headOnDot;
                bool closing = Vector3.Dot(d, fa) > 0f && Vector3.Dot(d, fb) < 0f;
                Vector3 right = Vector3.Cross(Vector3.up, fa).normalized;
                float lateral = Mathf.Abs(Vector3.Dot(d, right));

                if (!active)
                {
                    if (headOn && closing && lateral < lateralLimit)
                    {
                        _activePairs.Add(key);
                        _stillSince[key] = -1f;
                        AddPull(i);
                        AddPull(j);
                        Debug.Log($"[MeetingResolver] head-on pair {i}/{j}: dist {dist:F1} m, lateral {lateral:F1} m -> EdgePull both");
                    }
                    continue;
                }

                if (!headOn || !closing)
                {
                    Release(key, i, j);
                    continue;
                }

                // deadlock backstop
                if (dist < deadlockDist &&
                    a.GetCurrentSpeedMS() < deadlockSpeedMS &&
                    b.GetCurrentSpeedMS() < deadlockSpeedMS)
                {
                    if (_stillSince[key] < 0f) _stillSince[key] = Time.time;
                    if (Time.time - _stillSince[key] > deadlockSeconds &&
                        !Visible(pa) && !Visible(pb))
                    {
                        Debug.Log($"[MeetingResolver] deadlocked pair {i}/{j} off camera -> removing {j} (density respawns it elsewhere)");
                        Release(key, i, j);
                        API.RemoveVehicle(j);
                    }
                }
                else
                {
                    _stillSince[key] = -1f;
                }
            }
        }
    }

    // BehaviourManager.UpdateActiveBehaviours threw ArgumentException
    System.Collections.IEnumerator RestartBaseline()
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        var all = API.GetAllVehicles();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].gameObject.activeInHierarchy)
            {
                API.StartVehicleBehaviour<Forward>(i);
                API.StartVehicleBehaviour<CurveSlowDown>(i);
            }
        }
        Debug.Log("[MeetingResolver] baseline behaviours re-armed for pre-injection vehicles");
    }

    void AddPull(int idx)
    {
        _pullRefs.TryGetValue(idx, out int n);
        _pullRefs[idx] = n + 1;
        if (n == 0) API.StartVehicleBehaviour<EdgePull>(idx);
    }

    void RemovePull(int idx)
    {
        if (!_pullRefs.TryGetValue(idx, out int n)) return;
        n--;
        if (n <= 0)
        {
            _pullRefs.Remove(idx);
            API.StopVehicleBehaviour<EdgePull>(idx);
        }
        else
        {
            _pullRefs[idx] = n;
        }
    }

    void Release(long key, int i, int j)
    {
        _activePairs.Remove(key);
        _stillSince.Remove(key);
        RemovePull(i);
        RemovePull(j);
    }

    void ClearVehicle(int idx)
    {
        if (_activePairs.Count > 0)
        {
            List<long> stale = null;
            foreach (var key in _activePairs)
            {
                int i = (int)(key >> 20), j = (int)(key & 0xFFFFF);
                if (i == idx || j == idx) (stale ??= new List<long>()).Add(key);
            }
            if (stale != null)
                foreach (var key in stale)
                    Release(key, (int)(key >> 20), (int)(key & 0xFFFFF));
        }
        _pullRefs.Remove(idx);
    }

    bool Visible(Vector3 worldPos)
    {
        if (_cam == null) return false;
        Vector3 vp = _cam.WorldToViewportPoint(worldPos);
        return vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
    }
#endif
}

#if GLEY_TRAFFIC_SYSTEM
// whole per-vehicle table.
public class StudyVehicleBehaviours : IBehaviourList
{
    public VehicleBehaviour[] GetBehaviours()
    {
        return new VehicleBehaviour[]
        {
            new Stop(),
            new TempStop(),
            new AvoidReverse(),
            new StopInDistance(),
            new StopInPoint(),
            new GiveWay(),
            new Overtake(),
            new FollowVehicle(),
            new Decelerate(),
            new NoWaypoints(),
            new Forward(),
            new FollowPlayer(),
            new OvertakePlayer(),
            new ChangeLane(),
            new DriveOnSide(),
            new SlowDownAndStop(),
            new CurveSlowDown(),
            new Reverse(),
            new ClearPath(),
            new IgnoreTrafficRules(),
            new OvertakeStationaryPlayer(),
            new EdgePull(),
        };
    }
}
#endif
