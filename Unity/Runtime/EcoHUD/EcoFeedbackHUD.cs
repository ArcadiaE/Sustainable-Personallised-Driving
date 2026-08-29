using UnityEngine;
using UnityEngine.UI;

public class EcoFeedbackHUD : MonoBehaviour
{
    [Header("Source")]
    public EcoScore eco;

    [Header("UI references (all optional)")]
    public RectTransform panel;               // scaled by Size
    public CanvasGroup group;
    public Text scoreText;                    // numeric style
    public Image scoreIcon;
    public Image leafBack;
    public Image valenceIcon;
    public Sprite happySprite, sadSprite;
    public Image fillImage;                   // coloured by score
    public GameObject speedGroup;
    public GameObject accelGroup;
    public Slider scoreBar;
    public Text labelText;                    // framing text (valence)

    [Header("Layer-2 design parameters (set by Round Controller / BO)")]
    public float pSizeLeaf = 1f;
    public float pSizeScore = 1f;
    public float pSizeFeedback = 1f;
    public float pSizeSpeed = 1f;      // [0.6, 1.3]
    public float pSizeAccel = 1f;
    public float pSizeLabels = 1f;
    [Range(0f, 1f)] public float pOpacity = 1f;

    [Header("AR markers (auto-found if left null)")]
    public TargetMarkers markers;

    [Header("Colours")]
    public Color highColor = new Color(0.13f, 0.77f, 0.37f);
    public Color midColor  = new Color(0.92f, 0.70f, 0.03f);
    public Color lowColor  = new Color(0.94f, 0.27f, 0.27f);
    public Color neutral   = new Color(0.60f, 0.60f, 0.60f);

    float shownScore = 100f;

    void Awake()
    {
        if (eco == null) eco = FindFirstObjectByType<EcoScore>();
        if (markers == null) markers = FindFirstObjectByType<TargetMarkers>();
    }

    void Update()
    {
        if (eco == null) return;
        float live = eco.ecoScore;
        shownScore = live;
        if (group != null) group.alpha = pOpacity;

        const float HideEps = 0.6f;
        ApplySize(leafBack != null ? leafBack.gameObject : null, pSizeLeaf, HideEps);
        ApplySize(scoreText != null ? scoreText.gameObject : null, pSizeScore, HideEps);
        if (scoreText != null && scoreText.gameObject.activeSelf)
            scoreText.text = Mathf.RoundToInt(shownScore).ToString();
        if (scoreIcon != null)
            scoreIcon.fillAmount = Mathf.Clamp01(shownScore / 100f);
        const float SpeedAlphaMin = 0.4f;
        if (speedGroup != null)
        {
            if (!speedGroup.activeSelf) speedGroup.SetActive(true);
            speedGroup.transform.localScale = Vector3.one * Mathf.Clamp(pSizeSpeed, 0.6f, 1.3f);
            EnsureGroupAlpha(speedGroup).alpha = Mathf.Max(pOpacity, SpeedAlphaMin);
        }
        if (accelGroup != null)
        {
            ApplySize(accelGroup, pSizeAccel, HideEps);
            if (accelGroup.activeSelf) EnsureGroupAlpha(accelGroup).alpha = pOpacity;
        }

        Color c = shownScore >= 80f ? highColor : (shownScore >= 60f ? midColor : lowColor);
        if (fillImage != null) fillImage.color = c;
        if (scoreText != null) scoreText.color = c;
        if (scoreIcon != null) scoreIcon.color = c;

        bool good = shownScore >= 70f;
        bool visible = pSizeFeedback > HideEps;
        if (labelText != null)
        {
            labelText.gameObject.SetActive(visible);
            labelText.transform.localScale = Vector3.one * Mathf.Max(pSizeFeedback, HideEps);
            if (good)
            {
                labelText.text = "Great eco-driving!";
            }
            else
            {
                switch (eco != null ? eco.GetDominantIssue() : EcoScore.EcoIssue.None)
                {
                    case EcoScore.EcoIssue.Speed:
                        labelText.text = (eco != null && eco.speedLossIsUnder)
                            ? "Keep a steady pace" : "Try slowing down a little";
                        break;
                    case EcoScore.EcoIssue.Accel: labelText.text = "Try a gentler throttle"; break;
                    case EcoScore.EcoIssue.Brake: labelText.text = "Try braking earlier, softer"; break;
                    default: labelText.text = "You can save more"; break;
                }
            }
        }
        if (valenceIcon != null)
        {
            valenceIcon.gameObject.SetActive(visible);
            valenceIcon.transform.localScale = Vector3.one * Mathf.Max(pSizeFeedback, HideEps);
            valenceIcon.sprite = good ? happySprite : sadSprite;
            valenceIcon.color = c;
        }

        if (markers != null)
        {
            markers.showVehicleMarkers = pSizeLabels > HideEps;
            markers.markerScale = pSizeLabels;
        }
    }

    static void ApplySize(GameObject go, float size, float eps)
    {
        if (go == null) return;
        bool on = size > eps;
        if (go.activeSelf != on) go.SetActive(on);
        if (on) go.transform.localScale = Vector3.one * size;
    }

    static CanvasGroup EnsureGroupAlpha(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        return cg != null ? cg : go.AddComponent<CanvasGroup>();
    }

    //   5 size_labels, 6 opacity
    // logic above is untouched.
    const float SizeMax = 1.3f;
    const float SpeedMin = 0.6f;                   // legal readout floor
    const float OpacityMin = 0.10f;
    public void ApplyDesignParams(float sizeLeaf, float sizeScore, float sizeFeedback,
                                  float sizeSpeed, float sizeAccel, float sizeLabels,
                                  float opacity)
    {
        pSizeLeaf = sizeLeaf * SizeMax;
        pSizeScore = sizeScore * SizeMax;
        pSizeFeedback = sizeFeedback * SizeMax;
        pSizeSpeed = SpeedMin + Mathf.Clamp01(sizeSpeed) * (SizeMax - SpeedMin);
        pSizeAccel = sizeAccel * SizeMax;
        pSizeLabels = sizeLabels * SizeMax;
        pOpacity = OpacityMin + Mathf.Clamp01(opacity) * (1f - OpacityMin);
    }
}
