using UnityEngine;
using UnityEngine.UI;

public class SliderValueLabel : MonoBehaviour
{
    public Slider slider;
    public Text label;

    void Update()
    {
        if (slider != null && label != null) label.text = ((int)slider.value).ToString();
    }
}
