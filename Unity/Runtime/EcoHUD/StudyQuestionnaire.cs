// =============================================================================
//  StudyQuestionnaire.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  The seam between the study loop and whatever collects the post-round subjective
//  ratings. RoundController calls Show(...) after each lap and reads back two scores:
//    - task load   (NASA-TLX style, 0-100, lower = better)  -> BO objective, MINIMIZE
//    - acceptance  (van der Laan,   0-100, higher = better)  -> BO objective, MAXIMIZE
//  Keeping it abstract lets you use the bundled SimpleStudyQuestionnaire or a wrapper
//  around the QuestionnaireToolkit asset, without touching RoundController.
//
// =============================================================================

using System;
using UnityEngine;

public abstract class StudyQuestionnaire : MonoBehaviour
{
    // Show the survey; invoke onDone(taskLoad, acceptance) (both 0-100) when submitted.
    public abstract void Show(Action<float, float> onDone);
    public abstract void Hide();
}
