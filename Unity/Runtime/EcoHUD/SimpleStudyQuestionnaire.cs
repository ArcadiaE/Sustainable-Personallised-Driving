using System;
using UnityEngine;
using UnityEngine.UI;

public class SimpleStudyQuestionnaire : StudyQuestionnaire
{
    [Header("Panel")]
    public GameObject panelRoot;
    public Button submitButton;

    [Header("NASA-TLX sliders (averaged -> task load 0-100, MINIMIZE)")]
    public Slider[] tlxSliders;

    [Header("Acceptance sliders / van der Laan (averaged -> acceptance 0-100, MAXIMIZE)")]
    public Slider[] acceptanceSliders;
    [System.NonSerialized] public float[] lastTlxRaw;
    [System.NonSerialized] public float[] lastAccRaw;

    [Header("Auto-complete with neutral scores if no UI is wired (pipeline testing)")]
    public bool autoCompleteIfNoUI = true;

    [Header("Reset every slider to its midpoint each time the survey opens")]
    public bool resetSlidersOnShow = true;

    Action<float, float> done;

    [Header("BO data quality: Submit stays disabled until EVERY slider has been moved — each item must be actively rated, not left at the default. Drag away and back still counts as moving that one.")]
    public bool requireSliderTouch = true;
    readonly System.Collections.Generic.HashSet<UnityEngine.UI.Slider> _touchedSet = new();
    int _sliderTotal;
    bool _listenersArmed;

    void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        ArmTouchListeners();
    }

    void ArmTouchListeners()
    {
        if (_listenersArmed) return;
        _listenersArmed = true;
        _sliderTotal = 0;
        foreach (var arr in new[] { tlxSliders, acceptanceSliders })
        {
            if (arr == null) continue;
            foreach (var s in arr)
            {
                if (s == null) continue;
                _sliderTotal++;
                var slider = s;   // capture per-iteration
                slider.onValueChanged.AddListener(_ => MarkTouched(slider));
            }
        }
    }

    void MarkTouched(UnityEngine.UI.Slider s)
    {
        _touchedSet.Add(s);
        if (submitButton != null)
            submitButton.interactable = !requireSliderTouch || _touchedSet.Count >= _sliderTotal;
    }

    public override void Show(Action<float, float> onDone)
    {
        done = onDone;
        ArmTouchListeners();
        if (resetSlidersOnShow)
        {
            ResetSliders(tlxSliders);
            ResetSliders(acceptanceSliders);
        }
        _touchedSet.Clear();
        if (panelRoot != null) panelRoot.SetActive(true);

        if (submitButton != null)
        {
            submitButton.interactable = !requireSliderTouch;
            submitButton.onClick.RemoveListener(Submit);
            submitButton.onClick.AddListener(Submit);
        }
        else if (autoCompleteIfNoUI)
        {
            Submit();
        }
    }

    public override void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    void Submit()
    {
        if (requireSliderTouch && submitButton != null && _touchedSet.Count < _sliderTotal) return;
        lastTlxRaw = Snapshot(tlxSliders);
        lastAccRaw = Snapshot(acceptanceSliders);
        float taskLoad = Average(tlxSliders, 50f);
        float acceptance = Average(acceptanceSliders, 50f);
        if (submitButton != null) submitButton.onClick.RemoveListener(Submit);
        Hide();
        Action<float, float> cb = done; done = null;
        cb?.Invoke(taskLoad, acceptance);
    }

    static float[] Snapshot(Slider[] sliders)
    {
        if (sliders == null) return null;
        var v = new float[sliders.Length];
        for (int i = 0; i < sliders.Length; i++)
            v[i] = sliders[i] != null ? sliders[i].value * 5f : 0f;
        return v;
    }

    static void ResetSliders(Slider[] sliders)
    {
        if (sliders == null) return;
        foreach (Slider s in sliders)
            if (s != null) s.SetValueWithoutNotify((s.minValue + s.maxValue) * 0.5f);
    }

    static float Average(Slider[] sliders, float fallback)
    {
        if (sliders == null || sliders.Length == 0) return fallback;
        float sum = 0f; int n = 0;
        foreach (Slider s in sliders)
            if (s != null) { sum += Mathf.InverseLerp(s.minValue, s.maxValue, s.value) * 100f; n++; }
        return n == 0 ? fallback : sum / n;
    }
}
