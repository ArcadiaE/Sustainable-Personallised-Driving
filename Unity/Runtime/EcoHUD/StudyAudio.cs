using UnityEngine;

[DefaultExecutionOrder(160)]
public class StudyAudio : MonoBehaviour
{
    [Header("City bed (2D loop)")]
    [Range(0f, 1f)] public float cityVolume = 0.22f;

    [Header("Crowd chatter (3D at pedestrian clusters)")]
    [Range(0f, 1f)] public float crowdVolume = 0.55f;
    public float crowdMinDistance = 4f;
    public float crowdMaxDistance = 28f;   // inaudible beyond this
    public int   crowdMaxSources = 12;

    [Header("Engine (on the car, follows throttle)")]
    [Range(0f, 1f)] public float engineIdleVolume = 0.18f;
    [Range(0f, 1f)] public float engineFullVolume = 0.55f;
    public float enginePitchIdle = 0.95f;
    public float enginePitchFull = 1.35f;
    public float engineResponse = 4f;

    AudioSource _engine;
    CarController _car;
    float _engineLevel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<StudyAudio>() != null) return;
        var rc = FindFirstObjectByType<RoundController>(FindObjectsInactive.Include);
        if (rc == null) return;
        var go = new GameObject("StudyAudio");
        go.AddComponent<StudyAudio>();
    }

    void Start()
    {
        _car = FindFirstObjectByType<CarController>();

        var city   = Resources.Load<AudioClip>("StudyAudio/city");
        var crowd  = Resources.Load<AudioClip>("StudyAudio/crowd");
        var engine = Resources.Load<AudioClip>("StudyAudio/engine");
        if (city == null && crowd == null && engine == null)
        {
            Debug.LogWarning("[StudyAudio] no clips under Resources/StudyAudio — silent.");
            return;
        }

        if (city != null)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = city; s.loop = true; s.playOnAwake = false;
            s.spatialBlend = 0f; s.volume = cityVolume;
            s.Play();
        }

        if (crowd != null)
        {
            var root = GameObject.Find("StaticPedestrians");
            if (root != null && root.transform.childCount > 0)
            {
                int n = root.transform.childCount;
                int step = Mathf.Max(1, Mathf.CeilToInt(n / (float)crowdMaxSources));
                int made = 0;
                for (int i = 0; i < n; i += step)
                {
                    var t = root.transform.GetChild(i);
                    var s = t.gameObject.AddComponent<AudioSource>();
                    s.clip = crowd; s.loop = true; s.playOnAwake = false;
                    s.spatialBlend = 1f; s.volume = crowdVolume;
                    s.minDistance = crowdMinDistance; s.maxDistance = crowdMaxDistance;
                    s.rolloffMode = AudioRolloffMode.Linear;
                    s.dopplerLevel = 0f;
                    s.time = (crowd.length * (i * 0.37f)) % Mathf.Max(0.1f, crowd.length);
                    s.Play();
                    made++;
                }
                Debug.Log($"[StudyAudio] crowd chatter on {made}/{n} pedestrian nodes.");
            }
        }

        if (engine != null && _car != null)
        {
            _engine = _car.gameObject.AddComponent<AudioSource>();
            _engine.clip = engine; _engine.loop = true; _engine.playOnAwake = false;
            _engine.spatialBlend = 0f;
            _engine.volume = engineIdleVolume;
            _engine.pitch = enginePitchIdle;
            _engine.Play();
        }
    }

    void Update()
    {
        if (_engine == null || _car == null) return;
        float target = Mathf.Clamp01(Mathf.Abs(_car.throttleInput))
                     + Mathf.Clamp01(_car.currentSpeed / 80f) * 0.3f;
        target = Mathf.Clamp01(target);
        _engineLevel = Mathf.MoveTowards(_engineLevel, target,
                                         engineResponse * Time.deltaTime);
        _engine.volume = Mathf.Lerp(engineIdleVolume, engineFullVolume, _engineLevel);
        _engine.pitch  = Mathf.Lerp(enginePitchIdle,  enginePitchFull,  _engineLevel);
    }
}
