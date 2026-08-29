using System;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(150)]
public class StudyLogger : MonoBehaviour
{
    public static StudyLogger Instance { get; private set; }

    RoundController _rc;
    CarController _car;
    AutoDriver _drv;
    EcoScore _eco;
    StreamWriter _rounds, _traj, _console, _avg;
    string _dir;
    int _round = -1;
    float _tStart, _maxSpeed, _sumSpeed, _maxStuck;
    float _avgNext, _avgSumV, _avgSumA, _avgSumAbsA, _avgSumEco;
    float _sumEco;
    int _avgN;
    int _nSpeed, _collisions;
    RoundController.Phase _lastPhase = RoundController.Phase.Idle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var rc = FindFirstObjectByType<RoundController>(FindObjectsInactive.Include);
        if (rc == null) return;
        if (rc.GetComponent<StudyLogger>() == null)
            rc.gameObject.AddComponent<StudyLogger>();
    }

    public bool stripWarningStackTraces = true;

    void Awake()
    {
        if (stripWarningStackTraces)
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

        Instance = this;
        _rc = GetComponent<RoundController>();
        _car = FindFirstObjectByType<CarController>();
        _drv = FindFirstObjectByType<AutoDriver>();
        _eco = FindFirstObjectByType<EcoScore>();

        string root = Path.GetFullPath(Application.dataPath + "/../StudyLogs");
        Directory.CreateDirectory(root);
        int n = Directory.GetDirectories(root).Length + 1;
        _dir = Path.Combine(root, $"P{n:D2}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(_dir);

        _rounds = new StreamWriter(Path.Combine(_dir, "rounds.csv"), false) { AutoFlush = true };
        _rounds.WriteLine("round;route;endedAt;durationS;avgSpeedKmh;maxSpeedKmh;collisions;maxStuckS;" +
                          "energyKwh100km;avgEcoScore;taskLoad;acceptance;p0;p1;p2;p3;p4;p5;p6;" +
                          "tlxMental;tlxDistraction;accInformed;accPleasant;accGlance");
        _console = new StreamWriter(Path.Combine(_dir, "console.log"), false) { AutoFlush = true };
        Application.logMessageReceived += OnLog;
        Debug.Log("[StudyLogger] session folder: " + _dir);
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        _traj?.Dispose(); _avg?.Dispose();
        _rounds?.Dispose();
        _console?.Dispose();
        if (Instance == this) Instance = null;
    }

    void OnLog(string msg, string stack, LogType type)
    {
        if (_console == null) return;
        if (type != LogType.Log || (msg.Length > 0 && msg[0] == '['))
        {
            _console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{type}] {msg}");
            if (type == LogType.Exception) _console.WriteLine(stack);
        }
        if (msg.StartsWith("[CollisionLogger] HIT")) _collisions++;
    }

    void Update()
    {
        if (_rc == null) return;
        var ph = _rc.phase;
        if (ph != _lastPhase)
        {
            if (ph == RoundController.Phase.Driving) BeginRound();
            _lastPhase = ph;
        }
    }

    void FixedUpdate()
    {
        if (_rc == null || _rc.phase != RoundController.Phase.Driving || _car == null) return;
        float v = _car.currentSpeed;
        float a = _car.currentAcceleration;
        float e = _eco != null ? _eco.ecoScore : -1f;
        _maxSpeed = Mathf.Max(_maxSpeed, v);
        _sumSpeed += v;
        _sumEco += e;
        _nSpeed++;
        if (_drv != null) _maxStuck = Mathf.Max(_maxStuck, _drv.stuckSeconds);
        if (_traj != null)
        {
            Vector3 p = _car.transform.position;
            _traj.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2};{1:F2};{2:F2};{3:F1};{4:F3};{5:F2};{6:F2};{7:F1}",
                Time.time - _tStart, p.x, p.z, v, a, _car.throttleInput, _car.steerInput, e));
        }
        _avgSumV += v; _avgSumA += a; _avgSumAbsA += Mathf.Abs(a); _avgSumEco += e; _avgN++;
        if (Time.time >= _avgNext && _avg != null && _avgN > 0)
        {
            _avg.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F0};{1:F1};{2:F3};{3:F3};{4:F1}",
                Time.time - _tStart, _avgSumV / _avgN, _avgSumA / _avgN, _avgSumAbsA / _avgN, _avgSumEco / _avgN));
            _avgSumV = _avgSumA = _avgSumAbsA = _avgSumEco = 0f; _avgN = 0;
            _avgNext = Time.time + 1f;
        }
    }

    void BeginRound()
    {
        _round = _rc.optimizer != null ? _rc.optimizer.CurrentIteration : _round + 1;
        _tStart = Time.time;
        _maxSpeed = 0f; _sumSpeed = 0f; _sumEco = 0f; _nSpeed = 0; _collisions = 0; _maxStuck = 0f;
        _avgSumV = _avgSumA = _avgSumAbsA = _avgSumEco = 0f; _avgN = 0; _avgNext = Time.time + 1f;
        _traj?.Dispose(); _avg?.Dispose();
        _traj = new StreamWriter(Path.Combine(_dir, $"trajectory_round{_round + 1:D2}.csv"), false);
        _traj.WriteLine("t;x;z;speedKmh;accelMs2;throttle;steer;ecoScore");
        _avg?.Dispose();
        _avg = new StreamWriter(Path.Combine(_dir, $"averages_round{_round + 1:D2}.csv"), false);
        _avg.WriteLine("t;avgSpeedKmh;avgAccelMs2;avgAbsAccelMs2;avgEcoScore");
    }

    static void AppendRaw(ref string acc, float[] vals, int count)
    {
        for (int k = 0; k < count; k++)
            acc += ";" + (vals != null && k < vals.Length
                ? vals[k].ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "");
    }

    public static void NoteSurvey(float taskLoad, float acceptance, float energy)
    {
        var i = Instance;
        if (i == null || i._rounds == null) return;
        i._traj?.Flush();

        var opt = i._rc != null ? i._rc.optimizer : null;
        string route = "loop";
        try
        {
            if (i._rc != null && i._rc.routeSet != null && i._rc.routeSet.Count > 0 && opt != null)
                route = i._rc.routeSet.GetLabel(Mathf.Abs(opt.CurrentIteration) % i._rc.routeSet.Count);
        }
        catch { route = "?"; }

        string pars = "";
        for (int k = 0; k < OptimizerBridge.ParameterCount; k++)
            pars += ";" + (opt != null ? opt.GetParameter(k).ToString("F4", System.Globalization.CultureInfo.InvariantCulture) : "");

        string raw = "";
        var q = UnityEngine.Object.FindFirstObjectByType<SimpleStudyQuestionnaire>();
        AppendRaw(ref raw, q != null ? q.lastTlxRaw : null, 2);
        AppendRaw(ref raw, q != null ? q.lastAccRaw : null, 3);

        float avg = i._nSpeed > 0 ? i._sumSpeed / i._nSpeed : 0f;
        float avgEco = i._nSpeed > 0 ? i._sumEco / i._nSpeed : -1f;
        i._rounds.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0};{1};{2:HH:mm:ss};{3:F1};{4:F1};{5:F1};{6};{7:F1};{8:F2};{9:F1};{10:F1};{11:F1}{12}{13}",
            i._round + 1, route, DateTime.Now, Time.time - i._tStart, avg, i._maxSpeed,
            i._collisions, i._maxStuck, energy, avgEco, taskLoad, acceptance, pars, raw));
        Debug.Log($"[StudyLogger] round {i._round + 1} logged ({route}, {Time.time - i._tStart:F0}s, {i._collisions} collisions).");
    }
}
