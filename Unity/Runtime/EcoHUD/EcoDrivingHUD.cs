using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class EcoDrivingHUD : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI scoreText;
    public Slider scoreBar;
    public Image fillImage;

    [Header("Score Settings")]
    [Range(0f, 100f)]
    public float ecoScore = 86f;

    [Header("Color Thresholds")]
    public Color highScoreColor = new Color(0.13f, 0.77f, 0.37f);  // 绿
    public Color midScoreColor  = new Color(0.92f, 0.70f, 0.03f);  // 黄
    public Color lowScoreColor  = new Color(0.94f, 0.27f, 0.27f);  // 红

    void Update()
    {
        scoreText.text = Mathf.RoundToInt(ecoScore).ToString(); //EcoScore Update
        scoreBar.value = ecoScore;//Slide Bar

        Color targetColor;
        if (ecoScore >= 80f)       targetColor = highScoreColor;
        else if (ecoScore >= 60f)  targetColor = midScoreColor;
        else                       targetColor = lowScoreColor; //Color Defined by Score

        scoreText.color = targetColor;
        if (fillImage != null) fillImage.color = targetColor;
    }
}