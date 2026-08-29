using System.Collections.Generic;
using UnityEngine;

public class TargetMarkers : MonoBehaviour
{
    [Header("Optimizer-controlled (set via EcoFeedbackHUD each round)")]
    public bool showVehicleMarkers = true;
    [System.Obsolete("Pedestrian markers removed (task 1) — pedestrians are scenery only.")]
    public bool showPedestrianMarkers = false;

    [Header("Windshield aperture (task 8: labels only through the front glass)")]
    [Tooltip("Horizontal half-angle from the CAR's forward axis. Targets beyond it sit behind the A-pillars / side windows and draw no label.")]
    public float apertureYawDeg = 34f;
    public float aperturePitchMinDeg = -12f;   // below = bonnet
    public float aperturePitchMaxDeg = 9f;     // above = roof line

    [Header("Tuning")]
    [Tooltip("A marker only draws with clear line of sight: these layers occlude (CityGen buildings = 29, walls/props = Default).")]
    public LayerMask occluderMask = (1 << 29) | (1 << 0);
    public float maxDistance = 60f;
    [Tooltip("FALLBACK marker height above the target's origin — used only when a vehicle has no colliders to measure. Normally the anchor is that vehicle's collider-bounds top + 0.45 m, so the leaf hovers above every body shape instead of poking through tall ones .")]
    public float roofOffsetM = 1.9f;
    public float nearSizePx = 10f;
    public float farSizePx = 4f;
    [Tooltip("BO size_labels dim : scales the markers continuously; EcoFeedbackHUD turns them off entirely below its hide epsilon.")]
    public float markerScale = 1f;
    public Color vehicleColor = new Color(0.20f, 0.45f, 0.95f, 0.92f);   // blue (eco colouring off)
    public Color pedestrianColor = new Color(0.93f, 0.26f, 0.22f, 0.92f); // red

    [Header("Other-car eco state ")]
    public bool ecoColourVehicles = true;
    [Tooltip("Enter the harsh (red) state above this |accel| (m/s^2).")]
    public float harshEnterBand = 1.3f;
    [Tooltip("Return to the eco (green) state below this |accel| — hysteresis gap stops boundary flicker.")]
    public float harshExitBand = 0.6f;
    [Tooltip("A state holds at least this long before it may switch back (s).")]
    public float minStateHold = 0.8f;
    [Tooltip("Speed/accel are measured over this window, not per frame (s).")]
    public float samplePeriod = 0.25f;
    public Color vehicleEcoColor = new Color(0.25f, 0.80f, 0.35f, 0.92f);   // smooth
    public Color vehicleHarshColor = new Color(0.95f, 0.30f, 0.22f, 0.92f); // harsh pedal
    [Tooltip("The worse the larger : harsh markers scale up by this factor, eco markers keep the base size.")]
    public float harshSizeMul = 1.35f;

    class VehState
    {
        public Vector3 lastPos; public float speed; public float accelEma;
        public float sampleT; public bool harsh; public float stateT; public bool seen;
        public float roofY;
    }
    readonly Dictionary<Transform, VehState> _veh = new();
    readonly List<Transform> _stale = new();

    Camera _cam;
    Transform _carT;
    Transform _holder;
    readonly List<Transform> _pool = new();
    readonly List<SpriteRenderer> _poolSr = new();
    float _spriteWorldH = 1f;

    public static float PeerScore { get; private set; } = -1f;
    [Tooltip("EMA rate for the peer-average (lower = smoother/slower). The 'vs peers' line was jumping too fast .")]
    public float peerSmoothing = 0.8f;
    EcoScore _eco;

    void Start()
    {
        var go = new GameObject("TargetMarkers (world)");
        _holder = go.transform;
        var leaf = EcoHudAutoBuilder.LeafSprite();
        if (leaf != null) _spriteWorldH = leaf.bounds.size.y;
    }

    Transform GetMarker(int i)
    {
        while (_pool.Count <= i)
        {
            var m = new GameObject("Marker", typeof(SpriteRenderer));
            m.transform.SetParent(_holder, false);
            var sr = m.GetComponent<SpriteRenderer>();
            sr.sprite = EcoHudAutoBuilder.LeafSprite();
            sr.sortingOrder = 900;
            _pool.Add(m.transform);
            _poolSr.Add(sr);
        }
        return _pool[i];
    }

    void LateUpdate()
    {
        if (_holder == null) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        int used = 0;

        if (showVehicleMarkers)
        {
#if GLEY_TRAFFIC_SYSTEM
            var vehicles = Gley.TrafficSystem.API.IsInitialized()
                ? Gley.TrafficSystem.API.GetAllVehicles() : null;
            if (vehicles != null)
            {
                foreach (var s in _veh.Values) s.seen = false;
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                if (_eco == null) _eco = FindFirstObjectByType<EcoScore>();
                float peerSum = 0f; int peerN = 0;   // real peer-average accumulator
                foreach (var v in vehicles)
                {
                    if (v == null || !v.gameObject.activeInHierarchy) continue;
                    Color col = vehicleColor;
                    bool harsh = false;
                    if (!_veh.TryGetValue(v.transform, out var st))
                        _veh[v.transform] = st = new VehState
                        {
                            lastPos = v.transform.position,
                            roofY = MeasureRoof(v.gameObject)
                        };
                    st.seen = true;
                    if (ecoColourVehicles)
                    {
                        st.sampleT += dt;
                        if (st.sampleT >= samplePeriod)
                        {
                            float step = (v.transform.position - st.lastPos).magnitude;
                            if (step > 10f)
                            {
                                st.speed = 0f; st.accelEma = 0f;
                            }
                            else
                            {
                                float speed = step / st.sampleT;
                                float accel = (speed - st.speed) / st.sampleT;
                                st.accelEma = Mathf.Lerp(st.accelEma, accel, 0.5f);
                                st.speed = speed;
                            }
                            st.lastPos = v.transform.position;
                            st.sampleT = 0f;
                        }

                        st.stateT += dt;
                        if (st.stateT >= minStateHold)
                        {
                            if (!st.harsh && Mathf.Abs(st.accelEma) > harshEnterBand) { st.harsh = true; st.stateT = 0f; }
                            else if (st.harsh && Mathf.Abs(st.accelEma) < harshExitBand) { st.harsh = false; st.stateT = 0f; }
                        }
                        col = st.harsh ? vehicleHarshColor : vehicleEcoColor;
                        harsh = st.harsh;
                    }

                    if (st.speed > 0.5f && _eco != null)
                    {
                        peerSum += EcoScore.ScoreFrom(st.speed * 3.6f, st.accelEma,
                            _eco.targetSpeedKmh, _eco.sigmaLowKmh, _eco.sigmaHighKmh,
                            _eco.weightVelocity, _eco.weightAcceleration);
                        peerN++;
                    }

                    used = TryPlace(v.transform.position + Vector3.up * st.roofY, col, harsh, used);
                }

                if (peerN > 0)
                {
                    float mean = peerSum / peerN;
                    PeerScore = PeerScore < 0f ? mean
                        : Mathf.Lerp(PeerScore, mean, Mathf.Clamp01(peerSmoothing * dt));
                }
                _stale.Clear();
                foreach (var kv in _veh) if (!kv.Value.seen || kv.Key == null) _stale.Add(kv.Key);
                foreach (var k in _stale) _veh.Remove(k);
            }
#endif
        }

        for (int i = used; i < _pool.Count; i++)
            if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
    }

    float MeasureRoof(GameObject vehicle)
    {
        float topY = float.NegativeInfinity;
        foreach (var r in vehicle.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled) continue;
            if (r.bounds.max.y > topY) topY = r.bounds.max.y;
        }
        if (float.IsNegativeInfinity(topY))
        {
            foreach (var c in vehicle.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger) continue;
                if (c.bounds.max.y > topY) topY = c.bounds.max.y;
            }
        }
        if (float.IsNegativeInfinity(topY)) return roofOffsetM;
        return Mathf.Max(1.4f, topY - vehicle.transform.position.y) + 0.85f;
    }

    int TryPlace(Vector3 anchoredPos, Color color, bool harsh, int used)
    {
        Vector3 p = anchoredPos;

        if (_carT == null)
        {
            var drv = FindFirstObjectByType<AutoDriver>();
            if (drv != null) _carT = drv.transform;
        }
        if (_carT != null)
        {
            Vector3 local = _carT.InverseTransformDirection(p - _cam.transform.position);
            if (local.z <= 0.1f) return used;                        // beside/behind
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(yaw) > apertureYawDeg) return used;        // past the A-pillars
            float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            if (pitch < aperturePitchMinDeg || pitch > aperturePitchMaxDeg) return used;
        }

        if (Physics.Linecast(_cam.transform.position, p, occluderMask, QueryTriggerInteraction.Ignore))
            return used;

        float dist = Vector3.Distance(_cam.transform.position, p);
        if (dist <= 0.5f || dist > maxDistance) return used;

        var rt = GetMarker(used);
        if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);

        float t = Mathf.InverseLerp(8f, maxDistance, dist);
        float sizePx = Mathf.Lerp(nearSizePx, farSizePx, t) * Mathf.Max(0.05f, markerScale);
        if (harsh) sizePx *= harshSizeMul;
        float pxH = _cam.pixelHeight > 0 ? _cam.pixelHeight : Screen.height;
        float worldH = sizePx / pxH * 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float s = worldH / Mathf.Max(0.01f, _spriteWorldH);

        rt.SetPositionAndRotation(p, Quaternion.LookRotation(p - _cam.transform.position, Vector3.up));
        rt.localScale = new Vector3(s, harsh ? -s : s, s);
        _poolSr[used].color = color;
        return used + 1;
    }
}

public class MarkerTarget : MonoBehaviour
{
    public static readonly List<MarkerTarget> All = new();
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }
}
