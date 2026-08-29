using System;
using UnityEngine;

public abstract class StudyQuestionnaire : MonoBehaviour
{
    public abstract void Show(Action<float, float> onDone);
    public abstract void Hide();
}
