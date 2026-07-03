// =============================================================================
//  SimpleStudyQuestionnaire.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  A minimal in-engine post-round survey, so the study loop is runnable without any
//  external questionnaire asset. Wire a panel with sliders and a Submit button:
//    - tlxSliders        : NASA-TLX subscales (6: mental, physical, temporal,
//                          performance, effort, frustration). Averaged -> task load 0-100.
//    - acceptanceSliders : van der Laan items (e.g. 9). Averaged -> acceptance 0-100.
//  Any slider min/max is fine; values are normalized to 0-100. If nothing is wired and
//  autoCompleteIfNoUI is on, it returns neutral scores so the loop still cycles (for
//  testing the pipeline). Swap this for a QuestionnaireToolkit wrapper for the real study.
//
// =============================================================================

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

    [Header("Auto-complete with neutral scores if no UI is wired (pipeline testing)")]
    public bool autoCompleteIfNoUI = true;

    Action<float, float> done;

    void Awake() { if (panelRoot != null) panelRoot.SetActive(false); }

    public override void Show(Action<float, float> onDone)
    {
        done = onDone;
        if (panelRoot != null) panelRoot.SetActive(true);

        if (submitButton != null)
        {
            submitButton.onClick.RemoveListener(Submit);
            submitButton.onClick.AddListener(Submit);
        }
        else if (autoCompleteIfNoUI)
        {
            Submit();   // no UI: return neutral scores so the round can close
        }
    }

    public override void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    void Submit()
    {
        float taskLoad = Average(tlxSliders, 50f);
        float acceptance = Average(acceptanceSliders, 50f);
        if (submitButton != null) submitButton.onClick.RemoveListener(Submit);
        Hide();
        Action<float, float> cb = done; done = null;
        cb?.Invoke(taskLoad, acceptance);
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
