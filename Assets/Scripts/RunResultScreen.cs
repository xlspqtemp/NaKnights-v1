using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Displays a full-screen rating after a run ends, without replacing the existing HUD layout.</summary>
[DisallowMultipleComponent]
public sealed class RunResultScreen : MonoBehaviour
{
    private const string HudCanvasObjectName = "HUDCanvas";
    private const string RatingConfigResourceName = "RatingConfig";
    private const string PrefabAssetPath = "Assets/Resources/RunResultScreen.prefab";
    private const float MinimumTapTargetSize = 72f;
    private const float LetterRevealDuration = 0.7f;

    private static readonly Color ScreenBackground = new Color(0.012f, 0.025f, 0.04f, 0.97f);
    private static readonly Color SecondaryTextColor = new Color(0.69f, 0.83f, 0.88f, 1f);
    private static readonly Color BarBackgroundColor = new Color(0.075f, 0.13f, 0.17f, 1f);
    private static readonly Color ButtonColor = new Color(0.04f, 0.35f, 0.4f, 1f);
    private static readonly Color[] ComponentColors =
    {
        new Color(0.1f, 0.82f, 0.71f, 1f),
        new Color(0.15f, 0.72f, 0.95f, 1f),
        new Color(0.92f, 0.62f, 0.18f, 1f),
        new Color(0.65f, 0.62f, 0.96f, 1f),
        new Color(0.93f, 0.43f, 0.35f, 1f)
    };

    private static readonly string[] ComponentNames =
    {
        "Wellness",
        "Responsiveness",
        "Neglect",
        "Efficiency",
        "Defense"
    };

    private static readonly string[] WeakestComponentTips =
    {
        "Clear infections before they drain wellness.",
        "Respond sooner: tap new infections right away.",
        "Infections left alone escalate every day.",
        "Match the cell to the pathogen.",
        "Hold your barriers and finish the QTEs."
    };

    private RectTransform screenRoot;
    private TextMeshProUGUI gradeLabel;
    private CanvasGroup gradeCanvasGroup;
    private TMP_FontAsset displayFont;
    private bool initialized;

    /// <summary>Creates the result screen prefab instance and shows the calculated run rating.</summary>
    public static void Show(WellnessManager wellnessManager, WellnessRunResult runResult)
    {
        if (wellnessManager == null)
        {
            Debug.LogWarning("[RunResultScreen] Cannot show results without a WellnessManager.");
            return;
        }

        RunStats runStats = RunStats.Instance;
        RatingConfig ratingConfig = Resources.Load<RatingConfig>(RatingConfigResourceName);
        DayCounterUI dayCounter = FindFirstObjectByType<DayCounterUI>();
        string difficulty = DifficultySettings.CurrentStats != null
            ? DifficultySettings.CurrentStats.difficultyName
            : "Unknown";
        float targetDays = dayCounter != null ? dayCounter.TotalGameDays : 14f;
        RatingCalculator.RatingResult rating = RatingCalculator.Evaluate(
            runStats,
            difficulty,
            targetDays,
            ratingConfig);

        GameObject canvasObject = GameObject.Find(HudCanvasObjectName);
        Canvas canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
        if (canvas == null)
        {
            Debug.LogWarning("[RunResultScreen] HUDCanvas was not found; results were calculated but cannot be displayed.");
            return;
        }

        GameObject prefab = Resources.Load<GameObject>("RunResultScreen");
        if (prefab == null)
        {
            Debug.LogError("[RunResultScreen] Resources/RunResultScreen.prefab was not found.");
            return;
        }

        RunResultScreen screen = Instantiate(prefab).GetComponent<RunResultScreen>();
        if (screen == null)
        {
            Debug.LogError("[RunResultScreen] The result prefab requires a RunResultScreen component.");
            return;
        }

        screen.transform.SetParent(canvas.transform, false);
        screen.Initialize(wellnessManager, runResult, rating, difficulty, targetDays);
    }

    private void Initialize(
        WellnessManager wellnessManager,
        WellnessRunResult runResult,
        RatingCalculator.RatingResult rating,
        string difficulty,
        float targetDays)
    {
        if (initialized)
            return;
        initialized = true;

        screenRoot = transform as RectTransform;
        if (screenRoot == null)
        {
            Debug.LogError("[RunResultScreen] The prefab root must have a RectTransform.", this);
            return;
        }
        screenRoot.anchorMin = Vector2.zero;
        screenRoot.anchorMax = Vector2.one;
        screenRoot.pivot = new Vector2(0.5f, 0.5f);
        screenRoot.anchoredPosition = Vector2.zero;
        screenRoot.offsetMin = Vector2.zero;
        screenRoot.offsetMax = Vector2.zero;

        displayFont = Resources.Load<TMP_FontAsset>("BankGothicMediumSDF");
        CreateVisuals(wellnessManager, runResult, rating, difficulty, targetDays);
        StartCoroutine(RevealGrade());
    }

    private void CreateVisuals(
        WellnessManager wellnessManager,
        WellnessRunResult runResult,
        RatingCalculator.RatingResult rating,
        string difficulty,
        float targetDays)
    {
        Image background = gameObject.GetComponent<Image>();
        if (background == null)
            background = gameObject.AddComponent<Image>();
        background.color = ScreenBackground;
        background.raycastTarget = true;

        CreateText("RUN COMPLETE", 34f, SecondaryTextColor, new Vector2(0f, 465f), new Vector2(1200f, 58f), FontStyles.Bold, TextAlignmentOptions.Center);

        gradeCanvasGroup = CreateUiObject("Grade", screenRoot).AddComponent<CanvasGroup>();
        RectTransform gradeRect = gradeCanvasGroup.GetComponent<RectTransform>();
        SetCenteredRect(gradeRect, new Vector2(-555f, 115f), new Vector2(430f, 390f));
        gradeLabel = gradeCanvasGroup.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(gradeLabel);
        gradeLabel.text = rating.Letter;
        gradeLabel.fontSize = 320f;
        gradeLabel.fontStyle = FontStyles.Bold;
        gradeLabel.alignment = TextAlignmentOptions.Center;
        gradeLabel.color = GetGradeColor(rating.Letter);
        gradeLabel.raycastTarget = false;
        gradeCanvasGroup.alpha = 0f;

        CreateText("PERFORMANCE SCORE", 28f, SecondaryTextColor, new Vector2(115f, 300f), new Vector2(700f, 50f), FontStyles.Bold, TextAlignmentOptions.Left);
        CreateText(rating.PerformanceScore.ToString("0.0") + " / 100", 72f, Color.white, new Vector2(115f, 230f), new Vector2(700f, 86f), FontStyles.Bold, TextAlignmentOptions.Left);
        CreateText("TOTAL SCORE", 26f, SecondaryTextColor, new Vector2(115f, 154f), new Vector2(310f, 44f), FontStyles.Bold, TextAlignmentOptions.Left);
        CreateText(rating.TotalScore.ToString("N0"), 42f, Color.white, new Vector2(420f, 154f), new Vector2(390f, 50f), FontStyles.Bold, TextAlignmentOptions.Left);

        RunStats runStats = RunStats.Instance;
        float daysSurvived = runStats != null ? runStats.GetRatingSnapshot().daysSurvived : 0f;
        string detailLine = $"DIFFICULTY  {difficulty}      DAYS SURVIVED  {daysSurvived:0.##} / {targetDays:0.##}";
        CreateText(detailLine, 26f, SecondaryTextColor, new Vector2(115f, 100f), new Vector2(830f, 48f), FontStyles.Bold, TextAlignmentOptions.Left);

        float[] componentScores =
        {
            rating.WellnessScore,
            rating.ResponsivenessScore,
            rating.NeglectScore,
            rating.EfficiencyScore,
            rating.DefenseScore
        };
        for (int index = 0; index < ComponentNames.Length; index++)
            CreateRatingBar(ComponentNames[index], componentScores[index], ComponentColors[index], new Vector2(60f, 15f - index * 66f));

        string weakestTip = GetWeakestComponentTip(rating.WeakestComponent);
        CreateText("FOCUS NEXT RUN: " + rating.WeakestComponent.ToUpperInvariant(), 26f, GetWeakestColor(rating.WeakestComponent),
            new Vector2(60f, -320f), new Vector2(1100f, 44f), FontStyles.Bold, TextAlignmentOptions.Left);
        CreateText(weakestTip, 25f, Color.white, new Vector2(60f, -365f), new Vector2(1100f, 42f), FontStyles.Normal, TextAlignmentOptions.Left);
        CreateText("Focus on improving this component next run.", 23f, SecondaryTextColor,
            new Vector2(60f, -408f), new Vector2(1100f, 38f), FontStyles.Normal, TextAlignmentOptions.Left);

        CreateButton("RETRY", new Vector2(-150f, -480f), () => wellnessManager.RestartRun());
        CreateButton("MAIN MENU", new Vector2(150f, -480f), () => wellnessManager.ReturnToMainMenu());

        string outcome = runResult == WellnessRunResult.PerfectWin || runResult == WellnessRunResult.Win
            ? "VICTORY"
            : runResult.ToString().ToUpperInvariant();
        CreateText(outcome, 28f, GetGradeColor(rating.Letter), new Vector2(0f, 405f), new Vector2(900f, 48f), FontStyles.Bold, TextAlignmentOptions.Center);
    }

    private void CreateRatingBar(string label, float score, Color fillColor, Vector2 position)
    {
        CreateText(label.ToUpperInvariant(), 25f, Color.white, position + new Vector2(0f, 22f), new Vector2(310f, 42f), FontStyles.Bold, TextAlignmentOptions.Left);
        CreateText(Mathf.RoundToInt(Mathf.Clamp01(score) * 100f) + "%", 24f, SecondaryTextColor,
            position + new Vector2(760f, 22f), new Vector2(90f, 42f), FontStyles.Bold, TextAlignmentOptions.Right);

        RectTransform barRect = CreateRect("Bar - " + label, screenRoot);
        SetCenteredRect(barRect, position + new Vector2(375f, -16f), new Vector2(720f, 20f));
        Image barBackground = barRect.gameObject.AddComponent<Image>();
        barBackground.color = BarBackgroundColor;
        barBackground.raycastTarget = false;

        RectTransform fillRect = CreateRect("Fill", barRect);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(Mathf.Clamp01(score), 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.color = fillColor;
        fill.raycastTarget = false;
    }

    private void CreateButton(string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = CreateUiObject(label + " Button", screenRoot);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        SetCenteredRect(buttonRect, position, new Vector2(280f, MinimumTapTargetSize));
        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = ButtonColor;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(action);

        TextMeshProUGUI buttonLabel = CreateText(label, 30f, Color.white, Vector2.zero,
            new Vector2(264f, MinimumTapTargetSize - 4f), FontStyles.Bold, TextAlignmentOptions.Center, buttonRect);
        buttonLabel.raycastTarget = false;
    }

    private TextMeshProUGUI CreateText(
        string objectName,
        float fontSize,
        Color color,
        Vector2 position,
        Vector2 size,
        FontStyles style,
        TextAlignmentOptions alignment,
        RectTransform parent = null)
    {
        RectTransform parentRect = parent != null ? parent : screenRoot;
        GameObject textObject = CreateUiObject(objectName, parentRect);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        SetCenteredRect(textRect, position, size);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(text);
        text.text = objectName;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private void ApplyFont(TMP_Text text)
    {
        if (displayFont != null)
            text.font = displayFont;
    }

    private static GameObject CreateUiObject(string objectName, RectTransform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        child.transform.SetParent(parent, false);
        return child;
    }

    private static RectTransform CreateRect(string objectName, RectTransform parent)
    {
        return CreateUiObject(objectName, parent).GetComponent<RectTransform>();
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private IEnumerator RevealGrade()
    {
        float elapsed = 0f;
        while (elapsed < LetterRevealDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / LetterRevealDuration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            gradeCanvasGroup.alpha = eased;
            gradeCanvasGroup.transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1f, eased);
            yield return null;
        }
        gradeCanvasGroup.alpha = 1f;
        gradeCanvasGroup.transform.localScale = Vector3.one;
    }

    private static Color GetGradeColor(string grade)
    {
        switch (grade)
        {
            case "S": return new Color(1f, 0.78f, 0.18f, 1f);
            case "A": return new Color(0.12f, 0.88f, 0.65f, 1f);
            case "B": return new Color(0.16f, 0.76f, 0.98f, 1f);
            case "C": return new Color(1f, 0.62f, 0.22f, 1f);
            default: return new Color(0.98f, 0.28f, 0.3f, 1f);
        }
    }

    private static Color GetWeakestColor(string weakestComponent)
    {
        for (int index = 0; index < ComponentNames.Length; index++)
        {
            if (string.Equals(ComponentNames[index], weakestComponent, System.StringComparison.OrdinalIgnoreCase))
                return ComponentColors[index];
        }
        return Color.white;
    }

    private static string GetWeakestComponentTip(string weakestComponent)
    {
        for (int index = 0; index < ComponentNames.Length; index++)
        {
            if (string.Equals(ComponentNames[index], weakestComponent, System.StringComparison.OrdinalIgnoreCase))
                return WeakestComponentTips[index];
        }
        return "Focus on improving your weakest component.";
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void EnsurePrefabAssetExists()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath) != null)
            return;

        GameObject prefabRoot = new GameObject(
            "RunResultScreen",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(RunResultScreen));
        prefabRoot.layer = 5;
        Image background = prefabRoot.GetComponent<Image>();
        background.color = ScreenBackground;
        background.raycastTarget = true;
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabAssetPath);
        DestroyImmediate(prefabRoot);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
#endif
}
