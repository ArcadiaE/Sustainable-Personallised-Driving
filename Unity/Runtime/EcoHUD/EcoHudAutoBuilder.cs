using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-50)]
public class EcoHudAutoBuilder : MonoBehaviour
{
    const string Version = "v6-glasslayout";

    [Header("Target (auto-found if left empty)")]
    public EcoFeedbackHUD hud;

    [Header("Build")]
    [Tooltip("Only build if EcoFeedbackHUD has no panel wired yet (so a hand-built UI is not overwritten).")]
    public bool onlyIfUnwired = true;
    [Tooltip("Show the 'Round N/M · phase' status label. Hidden for participants; enable while debugging the BO loop.")]
    public bool showRoundStatus = false;
    public Vector2 anchoredPosition = new Vector2(28f, -28f);   // from top-left
    [Tooltip("Dark plate over the instrument binnacle, behind the turn arrow. The dial meshes are switched off outright; this also covers dial art baked into the interior shell. Untick if the bare dashboard already looks clean.")]
    public bool coverInstrumentCluster = true;
    public Vector2 panelSize = new Vector2(340f, 170f);

    void Awake()
    {
        if (hud == null) hud = FindFirstObjectByType<EcoFeedbackHUD>();
    }

    string _buildOutcome = "Start() never ran";
    Text _status;
    Text _speed;
    RoundController _rc;
    CarController _car;
    EcoScore _eco;
    Text _accel;
    Image _spIcon, _accIcon;             // eco-reactive colouring targets
    Image _accNeedle, _accArrow;
    Text _kmhUnit;
    Image _guideArrow;                   // route guidance (fixed element)
    Text _guideDist;
    AutoDriver _guideDrv;
    float _speedEma = -1f;               // display-side smoothing state
    float _accelEma;
    [Tooltip("Display smoothing for the speed readout (EMA rate; higher = snappier). Only the DISPLAY is smoothed — EcoScore and logging read the raw speed.")]
    public float speedDisplaySmoothing = 3f;
    [Tooltip("Display smoothing for the acceleration readout (raw accel is very noisy).")]
    public float accelDisplaySmoothing = 1.6f;

    void Update()
    {
        if (_speed != null)
        {
            if (_car == null) _car = FindFirstObjectByType<CarController>();
            if (_car != null)
            {
                float raw = Mathf.Max(0f, _car.currentSpeed);
                _speedEma = _speedEma < 0f ? raw
                    : Mathf.Lerp(_speedEma, raw, Mathf.Clamp01(speedDisplaySmoothing * Time.deltaTime));
                _speed.text = Mathf.RoundToInt(_speedEma).ToString();
                _accelEma = Mathf.Lerp(_accelEma, _car.currentAcceleration,
                                       Mathf.Clamp01(accelDisplaySmoothing * Time.deltaTime));

                float sal = 1f;
                var neutral = new Color(1f, 1f, 1f, 0.9f);
                var ecoCol = Color.Lerp(neutral, new Color(0.45f, 0.95f, 0.5f, 1f), sal);
                var badCol = Color.Lerp(neutral, new Color(1f, 0.42f, 0.34f, 1f), sal);

                if (_eco == null) _eco = FindFirstObjectByType<EcoScore>();
                float vSc = _eco != null ? EcoScore.VelocityScore(_speedEma, _eco.targetSpeedKmh, _eco.sigmaLowKmh, _eco.sigmaHighKmh) : 100f;
                float vt = Mathf.Clamp01(vSc / 100f);
                Color full = Color.HSVToRGB(vt * (120f / 360f), 1f, 1f);   // deg
                full = Color.Lerp(Color.white, full, Mathf.Clamp01(_speedEma / 2f));   // km/h
                Color sc = Color.Lerp(neutral, full, sal);
                _speed.color = sc;
                if (_spIcon != null) _spIcon.color = sc;
                if (_kmhUnit != null) _kmhUnit.color = new Color(sc.r, sc.g, sc.b, 0.8f);

                bool accEco = Mathf.Abs(_accelEma) < 1.0f;     // harsh pedal threshold
                if (_accIcon != null)
                    _accIcon.color = accEco ? ecoCol : badCol;   // badge fill = eco state
                if (_accNeedle != null)
                {
                    float t = Mathf.Clamp01(Mathf.Abs(_accelEma) / 3f);
                    _accNeedle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 55f - 110f * t);
                }
                if (_accArrow != null)
                    _accArrow.rectTransform.localRotation =
                        _accelEma < -0.15f ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
            }
        }

        if (_guideArrow != null)
        {
            if (_guideDrv == null) _guideDrv = FindFirstObjectByType<AutoDriver>();
            if (_guideDrv != null)
            {
                if (_guideDrv.GetNextTurn(out float distM, out float angDeg))
                {
                    _guideArrow.rectTransform.localRotation =
                        Quaternion.Euler(0f, 0f, angDeg > 0f ? -90f : 90f);
                    _guideDist.text = $"{Mathf.Max(0f, distM):0} m";
                }
                else
                {
                    float rem = _guideDrv.GetRemainingM();
                    _guideArrow.rectTransform.localRotation = Quaternion.identity;
                    _guideDist.text = rem > 1f ? $"{rem:0} m to finish" : "";
                }
            }
        }
        if (_status == null) return;
        if (!showRoundStatus)
        {
            if (_status.gameObject.activeSelf) _status.gameObject.SetActive(false);
            return;
        }
        if (!_status.gameObject.activeSelf) _status.gameObject.SetActive(true);
        if (_rc == null) { _rc = FindFirstObjectByType<RoundController>(); if (_rc == null) return; }
        var opt = _rc.optimizer;
        if (opt == null || _rc.phase == RoundController.Phase.Idle)
        {
            _status.text = "waiting for optimizer...";
            return;
        }
        _status.text = $"Round {opt.CurrentIteration + 1}/{opt.TotalBudget} · {_rc.phase}";
    }

    void Start()
    {
        Debug.Log($"[EcoHudAutoBuilder] {Version} starting. hud={(hud != null ? "found" : "NULL")}");
        if (hud == null)
        {
            _buildOutcome = "FAILED: no EcoFeedbackHUD found in the scene";
            Debug.LogError("[EcoHudAutoBuilder] No EcoFeedbackHUD in the scene; nothing to build.");
        }
        else if (onlyIfUnwired && hud.panel != null)
        {
            _buildOutcome = "skipped: HUD already wired";
            Debug.Log("[EcoHudAutoBuilder] HUD already wired; not rebuilding.");
        }
        else
        {
            try
            {
                Build();
                _buildOutcome = "SUCCESS";
                Debug.Log("[EcoHudAutoBuilder] SUCCESS: runtime HUD built and wired into EcoFeedbackHUD.");
            }
            catch (System.Exception e)
            {
                _buildOutcome = "BUILD FAILED: " + e;
                Debug.LogError("[EcoHudAutoBuilder] BUILD FAILED: " + e);
            }
        }
        StartCoroutine(DumpStatus());
    }

    System.Collections.IEnumerator DumpStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"EcoHudAutoBuilder {Version}  session start {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} (new header mid-run = SCENE RELOADED)");
        var bo = FindFirstObjectByType<BOforUnity.BoForUnityManager>();
        var rc = FindFirstObjectByType<RoundController>();
        for (int i = 0; i < 400; i++)
        {
            yield return new WaitForSeconds(8f);
            if (sb.Length > 24000)
                sb.Remove(0, sb.Length / 2);
            sb.AppendLine($"--- t={Time.timeSinceLevelLoad:F0}s  frame={Time.frameCount} ---");
            sb.AppendLine($"buildOutcome: {_buildOutcome}");
            var canvas = GameObject.Find("EcoHUD Canvas");
            sb.AppendLine($"canvas: {(canvas == null ? "MISSING" : "exists")}" +
                          $"  alpha={(hud != null && hud.group != null ? hud.group.alpha.ToString("F2") : "?")}" +
                          $"  scale={(hud != null && hud.panel != null ? hud.panel.localScale.x.ToString("F2") : "?")}");
            if (hud != null)
                sb.AppendLine($"params: leaf={hud.pSizeLeaf:F2} score={hud.pSizeScore:F2} fb={hud.pSizeFeedback:F2} " +
                              $"spd={hud.pSizeSpeed:F2} acc={hud.pSizeAccel:F2} lbl={hud.pSizeLabels:F2} " +
                              $"op={hud.pOpacity:F2}");
            if (rc != null)
                sb.AppendLine($"round: phase={rc.phase} laps={(rc.autopilot != null ? rc.autopilot.laps : -1)} " +
                              $"questionnaire={(rc.questionnaire != null ? "wired" : "NULL")}");
            if (bo == null) bo = FindFirstObjectByType<BOforUnity.BoForUnityManager>();
            if (bo != null)
                sb.AppendLine($"bo: iter={bo.currentIteration}/{bo.totalIterations} init={bo.initialized} " +
                              $"simRunning={bo.simulationRunning} optRunning={bo.optimizationRunning} " +
                              $"newParams={bo.hasNewDesignParameterValues} finished={bo.optimizationFinished} " +
                              $"RELOADSCENE={bo.reloadSceneOnIterationAdvance}");
            else
                sb.AppendLine("bo: BoForUnityManager NOT FOUND");
            var drv = rc != null ? rc.autopilot : FindFirstObjectByType<AutoDriver>();
            if (drv != null && drv.car != null)
            {
                var tp = drv.car.transform.position;
                sb.AppendLine($"car: pos=({tp.x:F1},{tp.y:F1},{tp.z:F1}) speed={drv.car.currentSpeed:F1}km/h " +
                              $"engaged={drv.engaged} throttle={drv.car.throttleInput:F2} steer={drv.car.steerInput:F2}");
            }
#if GLEY_TRAFFIC_SYSTEM
            try
            {
                if (Gley.TrafficSystem.API.IsInitialized())
                {
                    var vs = Gley.TrafficSystem.API.GetAllVehicles();
                    int moving = 0, near = 0;
                    var cp = drv != null && drv.car != null ? drv.car.transform.position : Vector3.zero;
                    if (vs != null)
                        foreach (var v in vs)
                        {
                            if (v == null) continue;
                            var rb2 = v.GetComponent<Rigidbody>();
                            if (rb2 != null && rb2.linearVelocity.magnitude > 0.5f) moving++;
                            if (Vector3.Distance(v.transform.position, cp) < 120f) near++;
                        }
                    int crashPairs = 0;
                    string worst = "";
                    float worstD = float.MaxValue;
                    if (vs != null)
                        for (int a = 0; a < vs.Length; a++)
                        {
                            if (vs[a] == null || !vs[a].gameObject.activeInHierarchy) continue;
                            for (int b2 = a + 1; b2 < vs.Length; b2++)
                            {
                                if (vs[b2] == null || !vs[b2].gameObject.activeInHierarchy) continue;
                                float dd = Vector3.Distance(vs[a].transform.position, vs[b2].transform.position);
                                if (dd < 2.5f)
                                {
                                    crashPairs++;
                                    if (dd < worstD) { worstD = dd; worst = $" worst {dd:F1}m at {vs[a].transform.position:F0}"; }
                                }
                            }
                        }
                    sb.AppendLine($"traffic: total={(vs != null ? vs.Length : 0)} moving={moving} within120m={near} " +
                                  $"CRASHPAIRS={crashPairs}{worst}");
                }
                else sb.AppendLine("traffic: NOT initialized");
            }
            catch (System.Exception te) { sb.AppendLine("traffic: probe error " + te.Message); }
#endif
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.GetFullPath(Application.dataPath + "/../hud_debug.txt"), sb.ToString());
            }
            catch {  }
        }
    }

    static Font UiFont()
    {
        var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 20);
        return f;
    }

    void Build()
    {
        var font = UiFont();
        Debug.Log($"[EcoHudAutoBuilder] font={(font != null ? font.name : "NULL")}");

        var canvasGO = new GameObject("EcoHUD Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var canvasRT = (RectTransform)canvasGO.transform;
        canvasRT.sizeDelta = new Vector2(1200f, 440f);
        var carDrv = FindFirstObjectByType<AutoDriver>();
        if (carDrv != null)
        {
            canvasRT.SetParent(carDrv.transform, false);
            // bottom of the glass.
            canvasRT.localPosition = new Vector3(-0.10f, 1.39f, 0.97f);
            canvasRT.localRotation = Quaternion.Euler(-18f, 0f, 0f);
        }
        canvasRT.localScale = Vector3.one * 0.001f;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 3f;

        var panelRT = NewRect("Panel", canvasGO.transform);
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0f, 0f);
        panelRT.pivot = new Vector2(0.830f, 0.793f);
        panelRT.anchoredPosition = new Vector2(282f, 120f);
        panelRT.sizeDelta = new Vector2(330f, 150f);
        var group = panelRT.gameObject.AddComponent<CanvasGroup>();

        var labelText = MakeText("Label", panelRT, font, 20, "Eco-driving");
        labelText.alignment = TextAnchor.MiddleCenter;
        StretchTop((RectTransform)labelText.transform, top: 74f, height: 24f, sidePad: 62f);

        var scoreText = MakeText("ScoreText", panelRT, font, 42, "100");
        scoreText.alignment = TextAnchor.MiddleCenter;
        var scoreRT = (RectTransform)scoreText.transform;
        scoreRT.anchorMin = scoreRT.anchorMax = new Vector2(0.5f, 1f);
        scoreRT.pivot = new Vector2(0.5f, 0.5f);
        scoreRT.anchoredPosition = new Vector2(8f, -31f);
        scoreRT.sizeDelta = new Vector2(100f, 54f);

        var leafBackRT = NewRect("LeafGauge", panelRT);
        leafBackRT.anchorMin = leafBackRT.anchorMax = new Vector2(0.5f, 1f);
        leafBackRT.pivot = new Vector2(0.5f, 0.5f);
        leafBackRT.anchoredPosition = new Vector2(-70f, -31f);
        leafBackRT.sizeDelta = new Vector2(54f, 54f);
        var leafBack = leafBackRT.gameObject.AddComponent<Image>();
        leafBack.sprite = LeafSprite();
        leafBack.preserveAspect = true;
        leafBack.color = new Color(1f, 1f, 1f, 0.25f);
        var iconRT = NewRect("ScoreIcon", leafBackRT);
        Stretch(iconRT);
        var scoreIcon = iconRT.gameObject.AddComponent<Image>();
        scoreIcon.sprite = LeafSprite();
        scoreIcon.preserveAspect = true;
        scoreIcon.type = Image.Type.Filled;
        scoreIcon.fillMethod = Image.FillMethod.Vertical;
        scoreIcon.fillOrigin = (int)Image.OriginVertical.Bottom;
        scoreIcon.color = Color.white;

        var valRT = NewRect("ValenceIcon", panelRT);
        valRT.anchorMin = valRT.anchorMax = new Vector2(0.5f, 1f);
        valRT.pivot = new Vector2(0.5f, 0.5f);
        valRT.anchoredPosition = new Vector2(88f, -31f);
        valRT.sizeDelta = new Vector2(42f, 42f);
        var valenceIcon = valRT.gameObject.AddComponent<Image>();
        valenceIcon.preserveAspect = true;

        _status = MakeText("StatusLine", canvasGO.transform, font, 16, "waiting for optimizer...");
        var srt = (RectTransform)_status.transform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 0f);
        srt.anchoredPosition = new Vector2(12f, 10f);
        srt.sizeDelta = new Vector2(700f, 22f);
        _status.color = new Color(1f, 1f, 1f, 0.75f);

        var speedoRT = NewRect("Speedo", canvasGO.transform);
        speedoRT.anchorMin = speedoRT.anchorMax = new Vector2(0.5f, 0f);
        speedoRT.pivot = new Vector2(0.5f, 0.700f);
        speedoRT.anchoredPosition = new Vector2(-40f, 120f);
        speedoRT.sizeDelta = new Vector2(240f, 110f);
        var spIconRT = NewRect("SpeedIcon", speedoRT);
        spIconRT.anchorMin = spIconRT.anchorMax = spIconRT.pivot = new Vector2(0f, 0.5f);
        spIconRT.anchoredPosition = new Vector2(40f, 22f);
        spIconRT.sizeDelta = new Vector2(38f, 38f);
        var spIcon = spIconRT.gameObject.AddComponent<Image>();
        spIcon.sprite = DialSprite();
        spIcon.preserveAspect = true;
        spIcon.color = new Color(1f, 1f, 1f, 0.9f);
        _spIcon = spIcon;
        _speed = MakeText("Speed", speedoRT, font, 46, "0");
        _speed.alignment = TextAnchor.MiddleCenter;
        StretchTop((RectTransform)_speed.transform, top: 0f, height: 66f, sidePad: 34f);
        var kmh = MakeText("Unit", speedoRT, font, 18, "km/h");
        kmh.alignment = TextAnchor.MiddleCenter;
        StretchTop((RectTransform)kmh.transform, top: 70f, height: 24f, sidePad: 10f);
        kmh.color = new Color(1f, 1f, 1f, 0.8f);
        _kmhUnit = kmh;

        var accRT = NewRect("Accel", canvasGO.transform);
        accRT.anchorMin = accRT.anchorMax = new Vector2(0.5f, 0f);
        accRT.pivot = new Vector2(0.218f, 0.555f);
        accRT.anchoredPosition = new Vector2(129f, 120f);
        accRT.sizeDelta = new Vector2(110f, 110f);
        var accIconRT = NewRect("AccelIcon", accRT);
        accIconRT.anchorMin = accIconRT.anchorMax = accIconRT.pivot = new Vector2(0.5f, 0.5f);
        accIconRT.anchoredPosition = new Vector2(0f, 6f);
        accIconRT.sizeDelta = new Vector2(62f, 62f);
        var accIcon = accIconRT.gameObject.AddComponent<Image>();
        accIcon.sprite = GaugeBadgeSprite();
        accIcon.preserveAspect = true;
        accIcon.color = new Color(1f, 1f, 1f, 0.9f);
        _accIcon = accIcon;

        var accMarksRT = NewRect("AccelGaugeMarks", accIconRT);
        accMarksRT.anchorMin = Vector2.zero; accMarksRT.anchorMax = Vector2.one;
        accMarksRT.offsetMin = accMarksRT.offsetMax = Vector2.zero;
        var accMarks = accMarksRT.gameObject.AddComponent<Image>();
        accMarks.sprite = GaugeMarksSprite();
        accMarks.raycastTarget = false;

        var accNeedleRT = NewRect("AccelGaugeNeedle", accIconRT);
        accNeedleRT.anchorMin = accNeedleRT.anchorMax = accNeedleRT.pivot = new Vector2(0.5f, 0.5f);
        accNeedleRT.anchoredPosition = new Vector2(0f, -5f);
        accNeedleRT.sizeDelta = new Vector2(62f, 62f);
        _accNeedle = accNeedleRT.gameObject.AddComponent<Image>();
        _accNeedle.sprite = GaugeNeedleSprite();
        _accNeedle.raycastTarget = false;
        _accNeedle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 55f);   // rest: low-left

        var accArrowRT = NewRect("AccelArrow", accIconRT);
        accArrowRT.anchorMin = accArrowRT.anchorMax = accArrowRT.pivot = new Vector2(0.5f, 0.5f);
        accArrowRT.anchoredPosition = new Vector2(20f, 24f);
        accArrowRT.sizeDelta = new Vector2(16f, 16f);
        _accArrow = accArrowRT.gameObject.AddComponent<Image>();
        _accArrow.sprite = ArrowSprite();
        _accArrow.preserveAspect = true;
        _accArrow.raycastTarget = false;

        _accel = null;

        var clusterGO = new GameObject("Cluster HUD", typeof(Canvas), typeof(CanvasScaler));
        clusterGO.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        clusterGO.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        var clusterRT = (RectTransform)clusterGO.transform;
        clusterRT.sizeDelta = new Vector2(300f, 210f);
        if (carDrv != null)
        {
            clusterRT.SetParent(carDrv.transform, false);
            var at = new Vector3(-0.37f, 1.06f, 0.73f);   // m, car space
            var cam = Camera.main;
            Vector3 eye = cam != null ? carDrv.transform.InverseTransformPoint(cam.transform.position)
                                      : new Vector3(-0.37f, 1.38f, 0.22f);   // m
            clusterRT.localPosition = at;
            clusterRT.localRotation = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
            HideInstrumentCluster(carDrv.transform);
        }
        clusterRT.localScale = Vector3.one * 0.0007f;

        if (coverInstrumentCluster)
        {
            var plateRT = NewRect("ClusterPlate", clusterRT);
            Stretch(plateRT);
            var plate = plateRT.gameObject.AddComponent<Image>();
            plate.color = new Color(0.02f, 0.02f, 0.03f, 1f);
            plate.raycastTarget = false;
        }

        // UV region is untouched.
        if (coverInstrumentCluster && carDrv != null)
            BlackoutBinnacleTexture(carDrv.transform);

        var guideRT = NewRect("RouteGuide", clusterRT);
        guideRT.anchorMin = guideRT.anchorMax = guideRT.pivot = new Vector2(0.5f, 1f);
        guideRT.anchoredPosition = new Vector2(0f, -58f);
        guideRT.sizeDelta = new Vector2(210f, 130f);
        var gaRT = NewRect("GuideArrow", guideRT);
        gaRT.anchorMin = gaRT.anchorMax = new Vector2(0.5f, 1f);
        gaRT.pivot = new Vector2(0.5f, 0.5f);
        gaRT.anchoredPosition = new Vector2(-82f, -28f);
        gaRT.sizeDelta = new Vector2(36f, 36f);
        _guideArrow = gaRT.gameObject.AddComponent<Image>();
        _guideArrow.sprite = ArrowSprite();
        _guideArrow.preserveAspect = true;
        _guideArrow.color = new Color(0.35f, 0.85f, 1f, 0.95f);   // guidance cyan
        _guideDist = MakeText("GuideDist", guideRT, font, 26, "");
        _guideDist.alignment = TextAnchor.MiddleLeft;
        var gdRT = (RectTransform)_guideDist.transform;
        gdRT.anchorMin = gdRT.anchorMax = new Vector2(0.5f, 1f);
        gdRT.pivot = new Vector2(0f, 0.5f);
        gdRT.anchoredPosition = new Vector2(-46f, -28f);
        gdRT.sizeDelta = new Vector2(150f, 40f);
        _guideDist.color = new Color(0.75f, 0.93f, 1f, 0.95f);

        hud.panel = panelRT;
        hud.group = group;
        hud.labelText = labelText;
        hud.scoreText = scoreText;
        hud.scoreIcon = scoreIcon;
        hud.leafBack = leafBack;
        hud.valenceIcon = valenceIcon;
        hud.happySprite = FaceSprite(true);
        hud.sadSprite = FaceSprite(false);
        hud.speedGroup = speedoRT.gameObject;   // task 9: element switches
        hud.accelGroup = accRT.gameObject;
    }

    static Sprite _leaf;
    public static Sprite LeafSprite()
    {
        if (_leaf != null) return _leaf;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f;
                float v = (y + 0.5f) / N - 0.5f;
                float ax = (u - v) * 0.70710678f;
                float ay = (u + v) * 0.70710678f;
                float a = 0f;
                const float r = 0.55f, d = 0.34f, cy = 0.06f;
                float yy = ay - cy;
                float d1 = Mathf.Sqrt((ax - d) * (ax - d) + yy * yy);
                float d2 = Mathf.Sqrt((ax + d) * (ax + d) + yy * yy);
                float inside = r - Mathf.Max(d1, d2);
                if (inside > 0f)
                {
                    a = Mathf.Clamp01(inside * N * 0.5f);   // anti-aliased edge
                    if (Mathf.Abs(ax) < 0.012f && yy > -0.30f && yy < 0.30f)
                        a *= 0.25f;                          // central vein
                }
                // stem below the blade
                float sy = ay + 0.34f;
                if (Mathf.Abs(ax) < 0.03f && sy > -0.12f && sy < 0.16f)
                    a = Mathf.Max(a, Mathf.Clamp01((0.03f - Mathf.Abs(ax)) * N * 0.5f));
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        _leaf = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _leaf;
    }

    static bool FitWindshieldPlane(Transform car, out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = default;
        MeshFilter mf = null;
        foreach (var f in car.GetComponentsInChildren<MeshFilter>(true))
            if (f.gameObject.name.Contains("Windows_LOD0")) { mf = f; break; }
        if (mf == null || mf.sharedMesh == null) return false;
        Vector3[] verts;
        try { verts = mf.sharedMesh.vertices; }
        catch { return false; }   // mesh not CPU-readable

        var pts = new System.Collections.Generic.List<Vector3>();
        foreach (var v in verts)
        {
            Vector3 p = car.InverseTransformPoint(mf.transform.TransformPoint(v));
            if (p.z > 0.85f && p.z < 1.45f && p.y > 1.05f && p.y < 1.55f && Mathf.Abs(p.x) < 0.60f)
                pts.Add(p);
        }
        if (pts.Count < 12) return false;

        float sy = 0f, sz = 0f, syy = 0f, syz = 0f; int n = pts.Count;
        float ymin = float.MaxValue, ymax = float.MinValue;
        foreach (var p in pts)
        {
            sy += p.y; sz += p.z; syy += p.y * p.y; syz += p.y * p.z;
            ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y);
        }
        float denom = n * syy - sy * sy;
        if (Mathf.Abs(denom) < 1e-5f) return false;
        float m = (n * syz - sy * sz) / denom;
        float b = (sz - m * sy) / n;

        float rakeDeg = Mathf.Atan(Mathf.Abs(m)) * Mathf.Rad2Deg;
        Debug.Log($"[EcoHudAutoBuilder] glass fit: {n} pts, y {ymin:F2}-{ymax:F2}, m={m:F2}, rake={rakeDeg:F1} deg");
        if (rakeDeg < 15f || rakeDeg > 72f) return false;

        Vector3 nrm = new Vector3(0f, -m, 1f).normalized;
        rot = Quaternion.LookRotation(nrm, Vector3.up);

        float yC = Mathf.Lerp(ymin, ymax, 0.48f);
        float zC = m * yC + b;
        pos = new Vector3(0f, yC, zC) - nrm * 0.015f;
        return true;
    }

    static Sprite _arrow;
    static Sprite ArrowSprite()
    {
        if (_arrow != null) return _arrow;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f;
                float v = (y + 0.5f) / N - 0.5f;
                float a = 0f;
                // shaft
                if (Mathf.Abs(u) < 0.09f && v > -0.42f && v < 0.10f)
                    a = 1f;
                if (v >= 0.05f && v <= 0.44f)
                {
                    float half = Mathf.Lerp(0.26f, 0.02f, Mathf.InverseLerp(0.05f, 0.44f, v));
                    if (Mathf.Abs(u) < half) a = 1f;
                }
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _arrow = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _arrow;
    }

    static Sprite _dial;
    static Sprite DialSprite()
    {
        if (_dial != null) return _dial;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f;
                float v = (y + 0.5f) / N - 0.5f;
                float a = 0f;
                float r = Mathf.Sqrt(u * u + v * v);
                bool bottomGap = v < -0.18f && Mathf.Abs(u) < 0.26f;
                if (!bottomGap)
                    a = Mathf.Max(a, Mathf.Clamp01((0.05f - Mathf.Abs(r - 0.42f)) * N * 0.5f));
                if (u >= 0f && v >= 0f && u < 0.34f && Mathf.Abs(v - u) < 0.05f)
                    a = Mathf.Max(a, Mathf.Clamp01((0.05f - Mathf.Abs(v - u)) * N * 0.5f));
                a = Mathf.Max(a, Mathf.Clamp01((0.09f - r) * N * 0.5f));
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _dial = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _dial;
    }

    const float GaugeHubV = -0.078f;
    static Sprite _gaugeBadge, _gaugeMarks, _gaugeNeedle;

    static Sprite GaugeBadgeSprite()
    {
        if (_gaugeBadge != null) return _gaugeBadge;
        _gaugeBadge = MakeSprite((u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01((0.46f - r) * 128f * 0.5f);
            return new Color(1f, 1f, 1f, a);
        });
        return _gaugeBadge;
    }

    static Sprite GaugeMarksSprite()
    {
        if (_gaugeMarks != null) return _gaugeMarks;
        _gaugeMarks = MakeSprite((u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            // dark rim ring
            float rim = Mathf.Clamp01((0.022f - Mathf.Abs(r - 0.445f)) * 128f * 0.5f);
            Color c = new Color(0.10f, 0.10f, 0.11f, rim);
            float hu = u, hv = v - GaugeHubV;
            float hr = Mathf.Sqrt(hu * hu + hv * hv);
            if (hr > 0.26f && hr < 0.36f)
            {
                float ang = Mathf.Atan2(hv, hu) * Mathf.Rad2Deg;          // 0 = right, CCW
                for (int k = 0; k < 5; k++)
                {
                    float tickAng = 160f - k * 35f;                        // 160..20 across the top
                    float d = Mathf.Abs(Mathf.DeltaAngle(ang, tickAng)) * Mathf.Deg2Rad * hr;
                    float w = Mathf.Clamp01((0.016f - d) * 128f * 0.5f);
                    if (w > c.a) c = new Color(1f, 1f, 1f, w);
                }
            }
            // white hub cap
            float hub = Mathf.Clamp01((0.055f - hr) * 128f * 0.5f);
            if (hub > c.a) c = new Color(1f, 1f, 1f, hub);
            return c;
        });
        return _gaugeMarks;
    }

    static Sprite GaugeNeedleSprite()
    {
        if (_gaugeNeedle != null) return _gaugeNeedle;
        _gaugeNeedle = MakeSprite((u, v) =>
        {
            float a = 0f;
            if (v > -0.02f && v < 0.33f)
            {
                float halfw = Mathf.Lerp(0.024f, 0.008f, Mathf.InverseLerp(0f, 0.33f, v));   // tapers to the tip
                a = Mathf.Clamp01((halfw - Mathf.Abs(u)) * 128f * 0.5f);
            }
            return new Color(1f, 1f, 1f, a);
        });
        return _gaugeNeedle;
    }

    static Sprite MakeSprite(System.Func<float, float, Color> shade)
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f;
                float v = (y + 0.5f) / N - 0.5f;
                px[y * N + x] = shade(u, v);
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite _happy, _sad, _warn;
    static Sprite FaceSprite(bool happy)
    {
        if (happy && _happy != null) return _happy;
        if (!happy && _sad != null) return _sad;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f;
                float v = (y + 0.5f) / N - 0.5f;
                float a = 0f;
                float r = Mathf.Sqrt(u * u + v * v);
                a = Mathf.Max(a, Mathf.Clamp01((0.045f - Mathf.Abs(r - 0.44f)) * N * 0.5f));     // head ring
                float de1 = Mathf.Sqrt((u - 0.16f) * (u - 0.16f) + (v - 0.13f) * (v - 0.13f));
                float de2 = Mathf.Sqrt((u + 0.16f) * (u + 0.16f) + (v - 0.13f) * (v - 0.13f));
                a = Mathf.Max(a, Mathf.Clamp01((0.055f - Mathf.Min(de1, de2)) * N * 0.5f));      // eyes
                float cy = happy ? 0.10f : -0.46f;
                float mr = happy ? 0.30f : 0.32f;
                float dm = Mathf.Abs(Mathf.Sqrt(u * u + (v - cy) * (v - cy)) - mr);
                bool band = happy ? v < -0.10f : v > -0.20f && v < -0.02f;
                if (band) a = Mathf.Max(a, Mathf.Clamp01((0.04f - dm) * N * 0.5f));
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        if (happy) _happy = s; else _sad = s;
        return s;
    }

    static Sprite WarnSprite()
    {
        if (_warn != null) return _warn;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        Vector2 A = new(0f, 0.46f), B = new(-0.46f, -0.36f), C = new(0.46f, -0.36f);
        float Half(Vector2 p, Vector2 e1, Vector2 e2)
        {
            Vector2 d = (e2 - e1).normalized;
            Vector2 n = new(-d.y, d.x);
            return Vector2.Dot(p - e1, n);
        }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f);
                float inside = Mathf.Min(Half(p, A, B), Mathf.Min(Half(p, B, C), Half(p, C, A)));
                float a = Mathf.Clamp01(inside * N * 0.5f);
                if (Mathf.Abs(p.x) < 0.05f && p.y > -0.02f && p.y < 0.26f) a = 0f;               // bar
                if (Mathf.Sqrt(p.x * p.x + (p.y + 0.16f) * (p.y + 0.16f)) < 0.055f) a = 0f;      // dot
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _warn = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _warn;
    }

    // ---- small helpers -------------------------------------------------------

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void BlackoutBinnacleTexture(Transform car)
    {
        MeshFilter mf = null; Transform wheelT = null;
        foreach (var f in car.GetComponentsInChildren<MeshFilter>(true))
            if (mf == null && f.name.StartsWith("RMCar05_Hud") && f.sharedMesh != null) mf = f;
        foreach (var t in car.GetComponentsInChildren<Transform>(true))
            if (wheelT == null && t.name == "RMCar05_SteeringWheel") wheelT = t;
        if (mf == null || wheelT == null) { Debug.LogWarning("[EcoHudAutoBuilder] blackout: hud/wheel not found"); return; }
        Mesh m = mf.sharedMesh;
        Bounds b = m.bounds; Vector3 e = b.extents;
        int axL = (e.x >= e.y && e.x >= e.z) ? 0 : (e.y >= e.z ? 1 : 2);
        Vector3 uLv = Vector3.zero; uLv[axL] = 1f;
        float wheelSide = Mathf.Sign(Vector3.Dot(
            wheelT.position - mf.transform.TransformPoint(b.center),
            mf.transform.TransformDirection(uLv)));
        Vector3[] vs = m.vertices; Vector2[] uvs = m.uv;
        if (uvs == null || uvs.Length != vs.Length) { Debug.LogWarning("[EcoHudAutoBuilder] blackout: no usable UVs"); return; }
        Vector2 uvMin = new Vector2(2f, 2f), uvMax = new Vector2(-1f, -1f);
        for (int i = 0; i < vs.Length; i++)
        {
            if (Mathf.Sign(vs[i][axL] - b.center[axL]) != wheelSide) continue;
            uvMin = Vector2.Min(uvMin, uvs[i]); uvMax = Vector2.Max(uvMax, uvs[i]);
        }
        if (uvMax.x < uvMin.x) { Debug.LogWarning("[EcoHudAutoBuilder] blackout: no wheel-side verts"); return; }
        var rend = mf.GetComponent<Renderer>();
        var mat = rend.material;   // instanced
        var dark = new Color32(4, 4, 6, 255);
        bool did = false;
        foreach (var prop in new[] { "_BaseMap", "_MainTex", "_EmissionMap" })
        {
            if (!mat.HasProperty(prop)) continue;
            var src = mat.GetTexture(prop) as Texture2D;
            if (src == null) continue;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(uvMin.x * src.width) - 2, 0, src.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(uvMax.x * src.width) + 2, 0, src.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(uvMin.y * src.height) - 2, 0, src.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(uvMax.y * src.height) + 2, 0, src.height - 1);
            var px = new Color32[(x1 - x0 + 1)];
            for (int i = 0; i < px.Length; i++) px[i] = dark;
            for (int y = y0; y <= y1; y++) copy.SetPixels32(x0, y, px.Length, 1, px);
            copy.Apply(false, false);
            mat.SetTexture(prop, copy);
            did = true;
        }
        foreach (var r in car.GetComponentsInChildren<Renderer>(true))
            if (r != rend && r.name.StartsWith("RMCar05_Hud")) r.sharedMaterial = mat;
        Debug.Log($"[EcoHudAutoBuilder] binnacle texture blackout: uv=({uvMin.x:F2},{uvMin.y:F2})-({uvMax.x:F2},{uvMax.y:F2}) applied={did}");
    }

    static void CoverBinnacleExact(Transform car)
    {
        MeshFilter mf = null; Transform wheelT = null;
        foreach (var f in car.GetComponentsInChildren<MeshFilter>(true))
            if (mf == null && f.name.StartsWith("RMCar05_Hud") && f.sharedMesh != null) mf = f;
        foreach (var t in car.GetComponentsInChildren<Transform>(true))
            if (wheelT == null && t.name == "RMCar05_SteeringWheel") wheelT = t;
        if (mf == null || wheelT == null) { Debug.LogWarning("[EcoHudAutoBuilder] exact cover: hud/wheel not found"); return; }
        Mesh m = mf.sharedMesh;
        Bounds b = m.bounds; Vector3 e = b.extents;
        int axN = (e.x <= e.y && e.x <= e.z) ? 0 : (e.y <= e.z ? 1 : 2);
        int axL = (e.x >= e.y && e.x >= e.z) ? 0 : (e.y >= e.z ? 1 : 2);
        int axH = 3 - axN - axL;
        Vector3 uL = Vector3.zero; uL[axL] = 1f;
        float wheelSide = Mathf.Sign(Vector3.Dot(
            wheelT.position - mf.transform.TransformPoint(b.center),
            mf.transform.TransformDirection(uL)));
        float minL = float.MaxValue, maxL = float.MinValue,
              minH = float.MaxValue, maxH = float.MinValue;
        foreach (var v in m.vertices)
        {
            if (Mathf.Sign(v[axL] - b.center[axL]) != wheelSide) continue;
            if (v[axL] < minL) minL = v[axL]; if (v[axL] > maxL) maxL = v[axL];
            if (v[axH] < minH) minH = v[axH]; if (v[axH] > maxH) maxH = v[axH];
        }
        if (minL > maxL) { Debug.LogWarning("[EcoHudAutoBuilder] exact cover: no wheel-side vertices"); return; }
        Vector3 uN = Vector3.zero; uN[axN] = 1f;
        var camx = Camera.main;
        Vector3 centreLocal = Vector3.zero;
        centreLocal[axL] = (minL + maxL) * 0.5f;
        centreLocal[axH] = (minH + maxH) * 0.5f;
        centreLocal[axN] = b.center[axN];
        Vector3 centreW0 = mf.transform.TransformPoint(centreLocal);
        Vector3 nW = mf.transform.TransformDirection(uN).normalized;
        if (camx != null && Vector3.Dot(nW, camx.transform.position - centreW0) < 0f) nW = -nW;
        Vector3 faceLocal = centreLocal; faceLocal[axN] = b.center[axN];
        Vector3 faceW = centreW0 + nW * (mf.transform.TransformVector(uN * e[axN]).magnitude + 0.004f);
        float widthW = mf.transform.TransformVector(uL * ((maxL - minL) * 0.5f)).magnitude * 2f * 1.3f;
        Vector3 uH = Vector3.zero; uH[axH] = 1f;
        float heightW = mf.transform.TransformVector(uH * ((maxH - minH) * 0.5f)).magnitude * 2f * 1.1f;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "BinnacleCoverExact";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(car, true);
        Vector3 upW = mf.transform.TransformDirection(uH).normalized;
        if (Vector3.Dot(upW, Vector3.up) < 0f) upW = -upW;
        quad.transform.SetPositionAndRotation(faceW, Quaternion.LookRotation(-nW, upW));
        quad.transform.localScale = new Vector3(widthW, heightW, 1f);
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(sh != null ? sh : Shader.Find("Unlit/Color"));
        mat.color = new Color(0.015f, 0.015f, 0.02f, 1f);
        quad.GetComponent<Renderer>().sharedMaterial = mat;
        Debug.Log($"[EcoHudAutoBuilder] EXACT binnacle cover: face={faceW:F3} w={widthW:F3} h={heightW:F3} n={nW:F2}");
    }

    static void CoverBinnacleOnCanvas(RectTransform canvas, Transform car)
    {
        MeshFilter mf = null; Transform wheelT = null;
        foreach (var f in car.GetComponentsInChildren<MeshFilter>(true))
            if (mf == null && f.name.StartsWith("RMCar05_Hud") && f.sharedMesh != null) mf = f;
        foreach (var t in car.GetComponentsInChildren<Transform>(true))
            if (wheelT == null && t.name == "RMCar05_SteeringWheel") wheelT = t;
        if (mf == null) { Debug.LogWarning("[EcoHudAutoBuilder] hud MeshFilter not found for binnacle cover"); return; }
        var hr = mf.GetComponent<Renderer>();
        Debug.Log($"[EcoHudAutoBuilder] hud mesh '{mf.name}': subMeshCount={mf.sharedMesh.subMeshCount} " +
                  $"materials={(hr != null ? hr.sharedMaterials.Length : 0)} " +
                  $"names={(hr != null ? string.Join(",", System.Array.ConvertAll(hr.sharedMaterials, m => m != null ? m.name : "null")) : "-")}");
        Bounds b = mf.sharedMesh.bounds;
        Vector3 e = b.extents;
        int axN = (e.x <= e.y && e.x <= e.z) ? 0 : (e.y <= e.z ? 1 : 2);
        int axL = (e.x >= e.y && e.x >= e.z) ? 0 : (e.y >= e.z ? 1 : 2);
        int axH = 3 - axN - axL;
        Vector3 uL = Vector3.zero; uL[axL] = 1f;
        Vector3 uH = Vector3.zero; uH[axH] = 1f;
        Vector3 endA = mf.transform.TransformPoint(b.center + uL * (e[axL] * 0.5f));
        Vector3 endB = mf.transform.TransformPoint(b.center - uL * (e[axL] * 0.5f));
        Vector3 wheelPos = wheelT != null ? wheelT.position : car.position;
        Vector3 centreW = (Vector3.Distance(endA, wheelPos) <= Vector3.Distance(endB, wheelPos)) ? endA : endB;
        float widthW = mf.transform.TransformVector(uL * e[axL]).magnitude;    // binnacle width (half strip)
        float heightW = mf.transform.TransformVector(uH * e[axH]).magnitude * 2f;   // full height
        Vector3 local = canvas.InverseTransformPoint(centreW);   // already in canvas units
        var plateRT = NewRect("ClusterPlate", canvas);
        plateRT.anchorMin = plateRT.anchorMax = plateRT.pivot = new Vector2(0.5f, 0.5f);
        float s = Mathf.Max(1e-6f, canvas.localScale.x);
        plateRT.anchoredPosition = new Vector2(local.x, local.y + 95f);
        plateRT.sizeDelta = new Vector2(widthW / s * 1.5f, heightW / s * 1.35f);
        var plate = plateRT.gameObject.AddComponent<Image>();
        plate.color = new Color(0.02f, 0.02f, 0.03f, 1f);
        plate.raycastTarget = false;
        Debug.Log($"[EcoHudAutoBuilder] binnacle canvas cover: local=({local.x:F0},{local.y:F0}) size={plateRT.sizeDelta}");
    }

    static void HideInstrumentCluster(Transform car)
    {
        string[] dials = { "RMCar05_rpmArrowMesh", "RMCar05_SpeedArrowMesh" };
        foreach (var t in car.GetComponentsInChildren<Transform>(true))
            foreach (var d in dials)
                if (t.name == d && t.gameObject.activeSelf) t.gameObject.SetActive(false);
    }

    static Text MakeText(string name, Transform parent, Font font, int size, string content)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleLeft;
        t.color = Color.white;
        t.text = content;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void StretchTop(RectTransform rt, float top, float height, float sidePad)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(sidePad, -(top + height));   // left, bottom
        rt.offsetMax = new Vector2(-sidePad, -top);             // right, top
    }
}
