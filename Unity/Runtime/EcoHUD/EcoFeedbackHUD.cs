// =============================================================================
//  EcoFeedbackHUD.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
// =============================================================================

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EcoFeedbackHUD : MonoBehaviour
{
    [Header("Source")]
    public EcoScore eco;                      // auto-found if left null

    [Header("UI references (all optional)")]
    public RectTransform panel;               // scaled by Size
    public CanvasGroup group;                 // alpha for visibility / intermittent flashing
    public TextMeshProUGUI scoreText;         // numeric style
    public Image scoreIcon;                   // symbolic style (leaf / arrow)
    public Image fillImage;                   // coloured by score
    public Slider scoreBar;
    public TextMeshProUGUI labelText;         // framing text (valence)
    public GameObject peerObject;             // peer-comparison element
    public TextMeshProUGUI peerText;

    [Header("Layer-2 design parameters (set by Round Controller / BO)")]
    [Range(0f, 1f)] public float pEcoScoreVisible = 1f;   // >0.5 = show
    [Range(0.6f, 1.6f)] public float pEcoScoreSize = 1f;
    [Range(0f, 1f)] public float pColorSalience = 1f;     // 0 = muted, 1 = strong
    [Range(0f, 1f)] public float pStyle = 0f;             // <0.5 numeric, >0.5 symbolic
    [Range(0f, 1f)] public float pFeedbackRate = 1f;      // 0 intermittent, 1 continuous
    [Range(0f, 1f)] public float pValence = 1f;           // 0 warning, 1 encouraging
    [Range(0f, 1f)] public float pPeerCompare = 0f;       // >0.5 = show peer
    public float peerScore = 70f;                         // the comparison value

    [Header("Colours")]
    public Color highColor = new Color(0.13f, 0.77f, 0.37f);
    public Color midColor  = new Color(0.92f, 0.70f, 0.03f);
    public Color lowColor  = new Color(0.94f, 0.27f, 0.27f);
    public Color neutral   = new Color(0.60f, 0.60f, 0.60f);

    float intermittentTimer;
    float shownScore = 100f;

    void Awake()
    {
        if (eco == null) eco = FindFirstObjectByType<EcoScore>();
    }

    void Update()
    {
        if (eco == null) return;
        float live = eco.ecoScore;
        bool visible = pEcoScoreVisible > 0.5f;

        // --- timing: feedback rate (intermittent <-> continuous) ---
        float alpha = 0f;
        if (visible)
        {
            if (pFeedbackRate > 0.9f)
            {
                alpha = 1f; shownScore = live;            // continuous: always on, tracks live
            }
            else
            {
                float period = Mathf.Lerp(4f, 0.4f, pFeedbackRate); // s between refreshes
                intermittentTimer += Time.deltaTime;
                if (intermittentTimer >= period) { intermittentTimer = 0f; shownScore = live; }
                alpha = Mathf.Clamp01(1.2f - intermittentTimer);    // flash ~1.2s then fade
            }
        }
        if (group != null) group.alpha = alpha;

        // --- display: size ---
        if (panel != null) panel.localScale = Vector3.one * pEcoScoreSize;

        // --- display: colour by score, scaled by salience ---
        Color baseC = shownScore >= 80f ? highColor : (shownScore >= 60f ? midColor : lowColor);
        Color c = Color.Lerp(neutral, baseC, Mathf.Clamp01(pColorSalience));
        if (fillImage != null) fillImage.color = c;
        if (scoreText != null) scoreText.color = c;
        if (scoreIcon != null) scoreIcon.color = c;

        // --- display: style (numeric vs symbolic) ---
        bool symbolic = pStyle > 0.5f;
        if (scoreText != null) scoreText.gameObject.SetActive(visible && !symbolic);
        if (scoreIcon != null) scoreIcon.gameObject.SetActive(visible && symbolic);
        if (scoreText != null) scoreText.text = Mathf.RoundToInt(shownScore).ToString();
        if (scoreBar != null) scoreBar.value = shownScore;

        // --- information: framing (valence) ---
        if (labelText != null)
        {
            labelText.gameObject.SetActive(visible);
            labelText.text = pValence > 0.5f
                ? (shownScore >= 70f ? "Great eco-driving!" : "You can save more")
                : (shownScore >= 70f ? "Efficient" : "High consumption");
        }

        // --- information: peer comparison ---
        bool peer = pPeerCompare > 0.5f;
        if (peerObject != null) peerObject.SetActive(visible && peer);
        if (peer && peerText != null)
        {
            int d = Mathf.RoundToInt(shownScore - peerScore);
            peerText.text = d >= 0 ? $"You +{d} vs peers" : $"Peers +{-d}";
        }
    }

    // The Round Controller / BO calls this each iteration with the 7 proposed values.
    public void ApplyDesignParams(float visible, float size, float salience,
                                  float style, float rate, float valence, float peer)
    {
        pEcoScoreVisible = visible; pEcoScoreSize = size; pColorSalience = salience;
        pStyle = style; pFeedbackRate = rate; pValence = valence; pPeerCompare = peer;
    }
}
