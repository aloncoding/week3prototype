using UnityEngine;
using UnityEngine.UI;

//--------------------------------------------------------------------
//Top-left score readout for the endless climb, built entirely in code so the
//scene needs no authored Canvas hierarchy.
//
//The score counts up toward its true value rather than snapping, which reads as
//momentum while climbing. Crossing a milestone punches the readout and throws a
//short-lived banner underneath it.
//--------------------------------------------------------------------
public class ScoreHud : MonoBehaviour
{
    [Header("References")]
    [SerializeField] EndlessRunManager m_RunManager;
    [SerializeField] ProceduralSfx m_Sfx;

    [Header("Layout")]
    [SerializeField] Vector2 m_Margin = new Vector2(24.0f, 20.0f);
    [SerializeField] int m_ScoreFontSize = 38;
    [SerializeField] int m_BannerFontSize = 26;
    [SerializeField] Vector2 m_ReferenceResolution = new Vector2(1280.0f, 720.0f);

    [Header("Style")]
    [SerializeField] Color m_ScoreColor = Color.white;
    [Tooltip("Flashed on the score when a milestone lands.")]
    [SerializeField] Color m_MilestoneColor = new Color(1.0f, 0.85f, 0.30f);
    [SerializeField] bool m_ShowShadow = true;

    [Header("Feel")]
    [Tooltip("How fast the displayed number chases the real score.")]
    [SerializeField] float m_CountUpSpeed = 8.0f;
    [Tooltip("Extra scale added to the score text the moment a milestone lands.")]
    [Range(0.0f, 1.5f)] [SerializeField] float m_MilestonePunch = 0.55f;
    [Tooltip("How long the milestone banner stays up.")]
    [SerializeField] float m_BannerLifetime = 1.1f;
    [SerializeField] float m_PunchStiffness = 220.0f;
    [SerializeField] float m_PunchDamping = 16.0f;

    Text m_ScoreText;
    Text m_BannerText;
    RectTransform m_ScoreRect;
    RectTransform m_BannerRect;

    float m_Displayed;
    float m_Punch;
    float m_PunchVel;
    float m_FlashAmount;
    float m_BannerTimer;
    bool m_RunOver;

    void Start()
    {
        if (m_RunManager == null)
        {
            m_RunManager = FindAnyObjectByType<EndlessRunManager>();
        }
        if (m_RunManager == null)
        {
            Debug.LogError("ScoreHud: no EndlessRunManager found.");
            enabled = false;
            return;
        }

        Build();

        m_RunManager.OnScoreChanged += HandleScoreChanged;
        m_RunManager.OnMilestone += HandleMilestone;
        m_RunManager.OnRunEnded += HandleRunEnded;
    }

    void OnDestroy()
    {
        if (m_RunManager == null)
        {
            return;
        }
        m_RunManager.OnScoreChanged -= HandleScoreChanged;
        m_RunManager.OnMilestone -= HandleMilestone;
        m_RunManager.OnRunEnded -= HandleRunEnded;
    }

    void Update()
    {
        if (m_ScoreText == null)
        {
            return;
        }

        int target = m_RunManager.GetScore();
        m_Displayed = Mathf.Lerp(m_Displayed, target, 1.0f - Mathf.Exp(-m_CountUpSpeed * Time.deltaTime));
        //Snap the last fraction so the readout can actually settle on the real number
        if (Mathf.Abs(target - m_Displayed) < 1.0f)
        {
            m_Displayed = target;
        }

        m_ScoreText.text = m_RunOver
            ? Mathf.RoundToInt(m_Displayed).ToString()
            : Mathf.FloorToInt(m_Displayed).ToString();

        //Spring the punch back to rest
        float accel = (-m_PunchStiffness * m_Punch) - (m_PunchDamping * m_PunchVel);
        m_PunchVel += accel * Time.deltaTime;
        m_Punch += m_PunchVel * Time.deltaTime;
        m_ScoreRect.localScale = Vector3.one * (1.0f + m_Punch);

        m_FlashAmount = Mathf.MoveTowards(m_FlashAmount, 0.0f, Time.deltaTime * 1.6f);
        m_ScoreText.color = Color.Lerp(m_ScoreColor, m_MilestoneColor, m_FlashAmount);

        UpdateBanner();
    }

    void UpdateBanner()
    {
        if (m_BannerText == null)
        {
            return;
        }
        if (m_BannerTimer <= 0.0f)
        {
            if (m_BannerText.enabled)
            {
                m_BannerText.enabled = false;
            }
            return;
        }

        m_BannerTimer -= Time.deltaTime;
        float t = 1.0f - Mathf.Clamp01(m_BannerTimer / m_BannerLifetime);

        //Rise and fade out
        Color c = m_MilestoneColor;
        c.a = Mathf.Clamp01(1.0f - (t * t));
        m_BannerText.color = c;

        Vector2 pos = m_BannerRect.anchoredPosition;
        pos.y = -(m_Margin.y + m_ScoreFontSize + 6.0f) - (t * 14.0f);
        m_BannerRect.anchoredPosition = pos;

        float pop = 1.0f + (Mathf.Exp(-9.0f * t) * 0.35f);
        m_BannerRect.localScale = Vector3.one * pop;
    }

    void HandleScoreChanged(int a_Score)
    {
    }

    void HandleMilestone(int a_Value, int a_Index)
    {
        m_PunchVel += m_MilestonePunch * m_PunchStiffness * 0.1f;
        m_FlashAmount = 1.0f;

        if (m_BannerText != null)
        {
            m_BannerText.text = a_Value.ToString() + "!";
            m_BannerText.enabled = true;
            m_BannerTimer = m_BannerLifetime;
        }

        //Reuse the light-form swap chirp as a bright "achievement" ping
        if (m_Sfx != null)
        {
            m_Sfx.PlaySwap(true);
        }
    }

    void HandleRunEnded(int a_Score)
    {
        m_RunOver = true;
        if (m_BannerText == null)
        {
            return;
        }
        m_BannerText.text = "FELL - " + a_Score;
        m_BannerText.enabled = true;
        m_BannerTimer = m_BannerLifetime * 2.0f;
    }

    //----------------------------------------------------------------
    //UI construction
    //----------------------------------------------------------------
    void Build()
    {
        GameObject canvasGo = new GameObject("ScoreCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = m_ReferenceResolution;
        //Favour width so the readout keeps its size on wide windows
        scaler.matchWidthOrHeight = 0.5f;

        m_ScoreText = MakeText(canvasGo.transform, "Score", m_ScoreFontSize, m_ScoreColor,
            -m_Margin.y, out m_ScoreRect);
        m_ScoreText.text = "0";

        m_BannerText = MakeText(canvasGo.transform, "Milestone", m_BannerFontSize, m_MilestoneColor,
            -(m_Margin.y + m_ScoreFontSize + 6.0f), out m_BannerRect);
        m_BannerText.enabled = false;
    }

    Text MakeText(Transform a_Parent, string a_Name, int a_FontSize, Color a_Color,
        float a_AnchoredY, out RectTransform a_Rect)
    {
        GameObject go = new GameObject(a_Name);
        go.transform.SetParent(a_Parent, false);

        a_Rect = go.AddComponent<RectTransform>();
        //Anchor and pivot to the top-left so layout is resolution independent
        a_Rect.anchorMin = new Vector2(0.0f, 1.0f);
        a_Rect.anchorMax = new Vector2(0.0f, 1.0f);
        a_Rect.pivot = new Vector2(0.0f, 1.0f);
        a_Rect.anchoredPosition = new Vector2(m_Margin.x, a_AnchoredY);
        a_Rect.sizeDelta = new Vector2(420.0f, a_FontSize + 12.0f);

        Text text = go.AddComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = a_FontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = a_Color;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        if (m_ShowShadow)
        {
            Shadow shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.55f);
            shadow.effectDistance = new Vector2(2.0f, -2.0f);
        }
        return text;
    }

    //Arial.ttf was removed from the built-in resources in newer Unity versions
    static Font ResolveFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        if (font == null)
        {
            Debug.LogWarning("ScoreHud: no built-in font available, score text may not render.");
        }
        return font;
    }
}
