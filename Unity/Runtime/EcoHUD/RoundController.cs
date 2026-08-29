using UnityEngine;

public class RoundController : MonoBehaviour
{
    [Header("References (auto-found if left empty)")]
    public OptimizerBridge optimizer;
    public EcoScore eco;
    public EcoFeedbackHUD hud;
    public AutoDriver autopilot;
    public StudyQuestionnaire questionnaire;
    public RouteSet routeSet;

    [Header("Round settings")]
    public bool autoDrive = true;
    public int lapsPerRound = 1;
    public bool startOnPlay = true;
    [Tooltip("Watchdog for a ~200 m route: a driving phase longer than this is stuck (wedged on geometry, hopeless jam) — the round is force-ended so the STUDY never hangs. Scaled up proportionally for longer routes (R3 Harrods is 402 m).")]
    public float maxDrivingSeconds = 240f;
    float _maxDriveThisRound = 240f;
    [Tooltip("Autopilot only: pushing without moving for this long with NO traffic ahead (geometry wedge) ends the round early — the autopilot never reverses.")]
    public float stuckAbortSeconds = 30f;
    [Tooltip("Autopilot only: patience when the stall has TRAFFIC ahead (red-light queues can legitimately hold 30-60 s).")]
    public float stuckTrafficAbortSeconds = 90f;
    float _driveStart;

    public enum Phase { Idle, Driving, Survey, WaitingNext, Done }
    public Phase phase { get; private set; } = Phase.Idle;

    int lapsDone;
    float roundEnergy;

    void Awake()
    {
        if (eco == null) eco = FindFirstObjectByType<EcoScore>();
        if (hud == null) hud = FindFirstObjectByType<EcoFeedbackHUD>();
        if (autopilot == null) autopilot = FindFirstObjectByType<AutoDriver>();
        if (questionnaire == null) questionnaire = FindFirstObjectByType<StudyQuestionnaire>();
        if (optimizer == null) optimizer = FindFirstObjectByType<OptimizerBridge>();
        if (routeSet == null) routeSet = FindFirstObjectByType<RouteSet>();
    }

    void OnEnable()
    {
        if (optimizer != null) optimizer.OnParametersReady += BeginRound;
        if (autopilot != null)
        {
            autopilot.onLapComplete += OnLap;
            autopilot.onRouteComplete += OnRouteDone;
        }
    }

    void OnDisable()
    {
        if (optimizer != null) optimizer.OnParametersReady -= BeginRound;
        if (autopilot != null)
        {
            autopilot.onLapComplete -= OnLap;
            autopilot.onRouteComplete -= OnRouteDone;
        }
    }

    void Start()
    {
        // Python boot.
        if (autopilot != null)
        {
            autopilot.engaged = false;
            if (routeSet != null && routeSet.Count > 0)
            {
                autopilot.SetRoute(routeSet.GetRoute(0), routeSet.GetHalfWidths(0), false);
                autopilot.SnapToStart();
            }
        }
        if (startOnPlay) StartStudy();
    }

    public void StartStudy()
    {
        if (optimizer == null) { Debug.LogError("[RoundController] No OptimizerBridge assigned."); return; }
        optimizer.StartOptimization();   // -> OnParametersReady -> BeginRound
    }

    void BeginRound()
    {
        if (phase == Phase.Done) return;
        if (hud != null)
            hud.ApplyDesignParams(
                optimizer.GetParameter(0), optimizer.GetParameter(1), optimizer.GetParameter(2),
                optimizer.GetParameter(3), optimizer.GetParameter(4), optimizer.GetParameter(5),
                optimizer.GetParameter(6));

        if (eco != null) eco.ResetRound();
        lapsDone = 0;

        string routeInfo = "";
        _maxDriveThisRound = maxDrivingSeconds;
        if (routeSet != null && routeSet.Count > 0 && autopilot != null)
        {
            int idx = Mathf.Abs(optimizer.CurrentIteration) % routeSet.Count;
            var pts = routeSet.GetRoute(idx);
            autopilot.SetRoute(pts, routeSet.GetHalfWidths(idx), false);
            autopilot.SnapToStart();
            routeInfo = " · route " + routeSet.GetLabel(idx);

            float len = 0f;
            for (int i = 1; i < pts.Length; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
            _maxDriveThisRound = maxDrivingSeconds * Mathf.Max(1f, len / 200f);
        }
        if (autopilot != null) autopilot.engaged = autoDrive;

        phase = Phase.Driving;
        _driveStart = Time.time;
        Debug.Log($"[RoundController] round {optimizer.CurrentIteration + 1}/{optimizer.TotalBudget} — driving{routeInfo}.");
    }

    void OnLap()
    {
        if (phase != Phase.Driving) return;
        lapsDone++;
        if (lapsDone >= lapsPerRound) EndDriving();
    }

    void OnRouteDone()
    {
        if (phase != Phase.Driving) return;
        EndDriving();
    }

    void EndDriving()
    {
        if (autopilot != null) autopilot.engaged = false;
        roundEnergy = (eco != null) ? eco.GetRoundEnergyPer100km() : 0f;
        phase = Phase.Survey;

        if (questionnaire != null) questionnaire.Show(OnSurveyDone);
        else OnSurveyDone(50f, 50f);
    }

    void OnSurveyDone(float taskLoad, float acceptance)
    {
        StudyLogger.NoteSurvey(taskLoad, acceptance, roundEnergy);
        var sq = questionnaire as SimpleStudyQuestionnaire;
        float[] acc = sq != null ? sq.lastAccRaw : null;
        optimizer.SetObjective(0, roundEnergy);
        optimizer.SetObjective(1, taskLoad);
        optimizer.SetObjective(2, acc != null && acc.Length > 0 ? acc[0] : acceptance);   // informed -> MAX
        optimizer.SetObjective(3, acc != null && acc.Length > 1 ? acc[1] : acceptance);   // pleasant -> MAX
        optimizer.SetObjective(4, acc != null && acc.Length > 2 ? acc[2] : acceptance);   // glance   -> MAX

        phase = Phase.WaitingNext;
        optimizer.SubmitAndRequestNext();
    }

    void Update()
    {
        if (phase == Phase.WaitingNext && optimizer != null && optimizer.IsFinished)
        {
            phase = Phase.Done;
            Debug.Log("[RoundController] Study complete.");
        }

        if (phase == Phase.Driving && Time.time - _driveStart > _maxDriveThisRound)
        {
            Debug.LogWarning($"[RoundController] driving exceeded {_maxDriveThisRound:F0}s — force-ending the round (car stuck?).");
            EndDriving();
        }

        if (phase == Phase.Driving && autoDrive && autopilot != null)
        {
            float limit = autopilot.stuckTrafficAhead ? stuckTrafficAbortSeconds : stuckAbortSeconds;
            if (autopilot.stuckSeconds > limit)
            {
                Debug.LogWarning($"[RoundController] autopilot stuck {autopilot.stuckSeconds:F0}s " +
                                 $"({(autopilot.stuckTrafficAhead ? "queue never cleared" : "geometry wedge")}) — ending the round early (data suspect).");
                EndDriving();
            }
        }
    }
}
