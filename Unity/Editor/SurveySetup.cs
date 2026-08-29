using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class SurveySetup
{
    static readonly string[] TlxLabels =
    {
        "It was mentally demanding",          // NASA-TLX: mental demand
        "It distracted me from the road",     // attention interference
    };

    static readonly string[] AcceptanceLabels =
    {
        "It kept me informed about my driving",
        "It was pleasant to use",             // van der Laan: satisfaction
        "I could read it at a glance",        // readability / interpretability
    };

    static SurveySetup()
    {
        EditorApplication.delayCall += RunSilent;
    }

    [MenuItem("Tools/Sustainable Driving/Build Survey Panel")]
    public static void RunFromMenu() => Run(true);

    static void RunSilent() => Run(false);

    static void Run(bool manual)
    {
        string result = Configure(manual);
        Debug.Log("[SurveySetup] " + result);
        try
        {
            File.WriteAllText(Path.GetFullPath(Application.dataPath + "/../survey_setup_result.txt"),
                              System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + result);
        }
        catch {  }
    }

    static string Configure(bool manual)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "SKIPPED: editor is in Play mode. Exit Play, then Tools > Study > Build Survey Panel.";
        if (!manual && GameObject.Find("TrafficManager") == null)
            return "SKIPPED: this does not look like the Pimlico scene (no 'TrafficManager').";

        var rc = Object.FindFirstObjectByType<RoundController>(FindObjectsInactive.Include);
        if (rc == null)
            return "NOT FOUND: no RoundController in the scene - run Tools > Study > BO Setup first.";

        bool esAdded = false;
        var esGo = GameObject.Find("EventSystem");
        if (esGo == null) { esGo = new GameObject("EventSystem"); esAdded = true; }
        if (esGo.GetComponent<UnityEngine.EventSystems.EventSystem>() == null)
        { esGo.AddComponent<UnityEngine.EventSystems.EventSystem>(); esAdded = true; }
        if (esGo.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>() == null)
        { esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>(); esAdded = true; }

        var legacy = GameObject.Find("SurveyCanvas");
        if (legacy != null) Object.DestroyImmediate(legacy);
        var legacy2 = GameObject.Find("SurveyCanvas_v2");
        if (legacy2 != null) Object.DestroyImmediate(legacy2);
        var existing = GameObject.Find("SurveyCanvas_v3");
        if (existing != null && rc.questionnaire is SimpleStudyQuestionnaire && legacy == null && legacy2 == null)
        {
            bool healed = esAdded;
            if (existing.GetComponent<SurveyVRPlacer>() == null)
            { existing.AddComponent<SurveyVRPlacer>(); healed = true; }
            var exCanvas = existing.GetComponent<Canvas>();
            var exCam = Object.FindFirstObjectByType<Camera>();
            if (exCanvas != null && exCanvas.worldCamera == null && exCam != null)
            { exCanvas.worldCamera = exCam; healed = true; }
            if (healed)
            {
                EditorUtility.SetDirty(existing);
                EditorSceneManager.MarkAllScenesDirty();
                EditorSceneManager.SaveOpenScenes();
                return "OK: canvas healed (SurveyVRPlacer + event camera + EventSystem ensured; VR world-space follow and desktop mouse both work). Scene SAVED.";
            }
            return "OK: SurveyCanvas already built and wired to RoundController. Nothing to do.";
        }
        if (existing != null) Object.DestroyImmediate(existing);

        var res = new DefaultControls.Resources
        {
            standard   = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            knob       = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
        };
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // --- canvas -------------------------------------------------------------
        var canvasGo = new GameObject("SurveyCanvas_v3", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;   // above the eco HUD
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // --- panel --------------------------------------------------------------
        var panel = new GameObject("SurveyPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGo.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(940, 460);
        panel.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.93f);
        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(28, 28, 20, 20);
        v.spacing = 5;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        MakeText(panel.transform, font, "Rate THIS round's display", 28, FontStyle.Bold, 880, 40);
        MakeText(panel.transform, font, "0 = not at all   ...   20 = very much", 20, FontStyle.Normal, 880, 28);
        var tlx = new Slider[TlxLabels.Length];
        for (int i = 0; i < TlxLabels.Length; i++) tlx[i] = MakeRow(panel.transform, res, font, TlxLabels[i]);

        var acc = new Slider[AcceptanceLabels.Length];
        for (int i = 0; i < AcceptanceLabels.Length; i++) acc[i] = MakeRow(panel.transform, res, font, AcceptanceLabels[i]);

        // --- submit button --------------------------------------------------------
        var buttonGo = DefaultControls.CreateButton(res);
        buttonGo.name = "SubmitButton";
        buttonGo.transform.SetParent(panel.transform, false);
        var ble = buttonGo.AddComponent<LayoutElement>();
        ble.preferredWidth = 240; ble.preferredHeight = 46;
        var btnText = buttonGo.GetComponentInChildren<Text>();
        btnText.text = "Submit"; btnText.font = font; btnText.fontSize = 22;

        // --- questionnaire component + wiring --------------------------------------
        var q = canvasGo.AddComponent<SimpleStudyQuestionnaire>();
        q.panelRoot = panel;
        q.submitButton = buttonGo.GetComponent<Button>();
        q.tlxSliders = tlx;
        q.acceptanceSliders = acc;
        rc.questionnaire = q;

        if (canvasGo.GetComponent<SurveyVRPlacer>() == null)
            canvasGo.AddComponent<SurveyVRPlacer>();
        var mainCam = Object.FindFirstObjectByType<Camera>();
        if (mainCam != null) canvas.worldCamera = mainCam;

        EditorUtility.SetDirty(rc);
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        bool saved = EditorSceneManager.SaveOpenScenes();
        return saved
            ? "OK: SurveyCanvas v3 built (5 questions, 0-20 stepped sliders with ticks + Submit) and wired to RoundController; scene SAVED."
            : "PARTIAL: SurveyCanvas built and wired, but the scene save was cancelled/failed - save manually (Ctrl+S).";
    }

    static void MakeText(Transform parent, Font font, string text, int size, FontStyle style, float w, float h)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text; t.font = font; t.fontSize = size; t.fontStyle = style;
        t.color = Color.white; t.alignment = TextAnchor.MiddleLeft;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.minHeight = h; le.preferredHeight = h;
    }

    static Slider MakeRow(Transform parent, DefaultControls.Resources res, Font font, string label)
    {
        var row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        var rle = row.AddComponent<LayoutElement>();
        rle.minHeight = 44; rle.preferredHeight = 44; rle.flexibleWidth = 1;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(row.transform, false);
        var t = labelGo.AddComponent<Text>();
        t.text = label; t.font = font; t.fontSize = 18; t.color = Color.white;
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        var tle = labelGo.AddComponent<LayoutElement>();
        tle.minWidth = 340; tle.preferredWidth = 340; tle.preferredHeight = 42;

        var sliderGo = DefaultControls.CreateSlider(res);
        sliderGo.name = "Slider";
        sliderGo.transform.SetParent(row.transform, false);
        var sle = sliderGo.AddComponent<LayoutElement>();
        sle.minWidth = 300; sle.preferredHeight = 30; sle.flexibleWidth = 1;
        var s = sliderGo.GetComponent<Slider>();
        s.minValue = 0f; s.maxValue = 20f; s.value = 10f; s.wholeNumbers = true;

        var bg = sliderGo.transform.Find("Background") as RectTransform;
        if (bg != null)
        {
            for (int i = 0; i <= 20; i++)
            {
                var tick = new GameObject("Tick", typeof(RectTransform), typeof(Image));
                tick.transform.SetParent(bg, false);
                var tr = (RectTransform)tick.transform;
                float x = i / 20f;
                tr.anchorMin = new Vector2(x, 0f);
                tr.anchorMax = new Vector2(x, 1f);
                tr.sizeDelta = new Vector2(i % 5 == 0 ? 3f : 2f, i % 5 == 0 ? 8f : 2f);
                tr.anchoredPosition = Vector2.zero;
                tick.GetComponent<Image>().color = new Color(1f, 1f, 1f, i % 5 == 0 ? 0.65f : 0.35f);
                tick.GetComponent<Image>().raycastTarget = false;
            }
        }

        var valGo = new GameObject("Value", typeof(RectTransform));
        valGo.transform.SetParent(row.transform, false);
        var vt = valGo.AddComponent<Text>();
        vt.font = font; vt.fontSize = 19; vt.color = Color.white;
        vt.alignment = TextAnchor.MiddleCenter;
        vt.text = "10";
        var vle = valGo.AddComponent<LayoutElement>();
        vle.minWidth = 44; vle.preferredWidth = 44; vle.preferredHeight = 30;
        var mirror = valGo.AddComponent<SliderValueLabel>();
        mirror.slider = s; mirror.label = vt;
        return s;
    }
}
