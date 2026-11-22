using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays queued viral adaptive-response resolution messages on the HUD.</summary>
[RequireComponent(typeof(CanvasGroup), typeof(AudioSource))]
public sealed class AdaptiveResponsePopup : MonoBehaviour
{
    private const float FadeInDuration = 0.3f;
    private const float HoldDuration = 3f;
    private const float FadeOutDuration = 0.5f;
    private const float MinimumWellnessGap = 30f;
    private const float HudSfxVolume = 0.65f;
    private const string BankGothicResourcePath = "BankGothicMediumSDF";
    private const string BacterialIconResourcePath = "UI/Red bacteria icon";
    private const string ViralIconResourcePath = "UI/Blue virus icon";
    private const string FungalIconResourcePath = "UI/Neon_Circuit_Mushroom_Emblem-removebg-preview";
    private const string AllergenIconResourcePath = "UI/Allergen icon";
    private const string ParasiteIconResourcePath = "UI/Parasite icon";
    private const float PathogenIconSize = 58f;
    private static readonly Dictionary<InfectionPathogenType, Sprite> PathogenIconCache = new Dictionary<InfectionPathogenType, Sprite>();
    private static readonly Vector2 PopupSize = new Vector2(700f, 90f);
    private static readonly Color BackgroundColor = new Color(0.025f, 0.045f, 0.065f, 0.96f);
    private static readonly Color AccentColor = new Color(0.15f, 0.78f, 0.8f, 1f);

    [SerializeField] private AudioClip resolvedSoundClip;

    private sealed class PopupMessage
    {
        public string title;
        public string subtitle;
        public string message;
        public Sprite icon;

        public PopupMessage(string title, string subtitle)
        {
            this.title = title;
            this.subtitle = subtitle;
        }

        public PopupMessage(string message, Sprite icon)
        {
            this.message = message;
            this.icon = icon;
        }

        public bool IsInlineMessage => !string.IsNullOrEmpty(message);
    }

    private readonly Queue<PopupMessage> pendingMessages = new Queue<PopupMessage>();
    private CanvasGroup popupCanvasGroup;
    private AudioSource audioSource;
    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI subtitleLabel;
    private TextMeshProUGUI messageLabel;
    private Image pathogenIconImage;
    private float animationElapsed;
    private PopupPhase phase;

    private enum PopupPhase
    {
        Hidden,
        FadingIn,
        Holding,
        FadingOut
    }

    /// <summary>Builds the HUD popup, positions it below the wellness bar, and configures its resolve sound.</summary>
    public void Initialize(Canvas hudCanvas, float wellnessGap)
    {
        if (hudCanvas == null || !(hudCanvas.transform is RectTransform canvasRect))
            return;

        popupCanvasGroup = GetComponent<CanvasGroup>();
        audioSource = GetComponent<AudioSource>();
        if (popupCanvasGroup == null || audioSource == null)
            return;

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f;
        popupCanvasGroup.alpha = 0f;
        popupCanvasGroup.interactable = false;
        popupCanvasGroup.blocksRaycasts = false;

        RectTransform popupRect = transform as RectTransform;
        popupRect.anchorMin = new Vector2(0.5f, 1f);
        popupRect.anchorMax = new Vector2(0.5f, 1f);
        popupRect.pivot = new Vector2(0.5f, 1f);
        popupRect.sizeDelta = PopupSize;
        PositionBelowWellnessBar(popupRect, canvasRect, wellnessGap);
        CreatePopupVisuals(popupRect);
        gameObject.SetActive(false);
    }

    /// <summary>Queues the pathogen appearance message using its matching HUD icon.</summary>
    public void ShowPathogenAppeared(string pathogenName, InfectionPathogenType pathogenType)
    {
        CameraScript cameraController = FindFirstObjectByType<CameraScript>();
        if (cameraController == null || cameraController.SelectedLayer != 2)
            return;

        string resolvedName = string.IsNullOrWhiteSpace(pathogenName) ? "Pathogen" : pathogenName;
        EnqueueMessage("A " + resolvedName + " has appeared!", LoadPathogenIcon(pathogenType));
    }

    /// <summary>Queues the antigen collection message using the same icon-first HUD popup.</summary>
    public void ShowAntigenCollected(string pathogenName, InfectionPathogenType pathogenType)
    {
        string resolvedName = string.IsNullOrWhiteSpace(pathogenName) ? "Pathogen" : pathogenName;
        EnqueueMessage("A " + resolvedName + " antigen has been collected!", LoadPathogenIcon(pathogenType));
    }

    /// <summary>Queues an adaptive-response resolution message and plays its HUD sound once when shown.</summary>
    public void Show(string displayName)
    {
        string resolvedName = string.IsNullOrWhiteSpace(displayName) ? "Viral infection" : displayName;
        EnqueueMessage("ADAPTIVE RESPONSE CLEARED", $"{resolvedName} resolved by the body. Good work, soldier.");
    }

    /// <summary>Queues a viral antigen collection message using the shared icon-first HUD popup.</summary>
    public void ShowAntigenCollected(string virusName)
    {
        ShowAntigenCollected(virusName, InfectionPathogenType.Viral);
    }

    private void EnqueueMessage(string title, string subtitle)
    {
        pendingMessages.Enqueue(new PopupMessage(title, subtitle));
        if (phase == PopupPhase.Hidden)
            BeginNextPopup();
    }

    private void EnqueueMessage(string message, Sprite icon)
    {
        pendingMessages.Enqueue(new PopupMessage(message, icon));
        if (phase == PopupPhase.Hidden)
            BeginNextPopup();
    }

    private static Sprite LoadPathogenIcon(InfectionPathogenType pathogenType)
    {
        if (PathogenIconCache.TryGetValue(pathogenType, out Sprite cachedIcon))
            return cachedIcon;

        string resourcePath;
        switch (pathogenType)
        {
            case InfectionPathogenType.Viral: resourcePath = ViralIconResourcePath; break;
            case InfectionPathogenType.Fungal: resourcePath = FungalIconResourcePath; break;
            case InfectionPathogenType.Allergen: resourcePath = AllergenIconResourcePath; break;
            case InfectionPathogenType.Parasite: resourcePath = ParasiteIconResourcePath; break;
            default: resourcePath = BacterialIconResourcePath; break;
        }

        Sprite icon = Resources.Load<Sprite>(resourcePath);
        if (icon == null)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture != null)
            {
                icon = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                icon.name = resourcePath + " HUD Sprite";
            }
        }

        if (icon == null)
            Debug.LogWarning("Pathogen message icon was not found at Resources/" + resourcePath + ".png");
        else
            PathogenIconCache[pathogenType] = icon;
        return icon;
    }

    private void Update()
    {
        if (phase == PopupPhase.Hidden || popupCanvasGroup == null)
            return;

        animationElapsed += Time.unscaledDeltaTime;
        switch (phase)
        {
            case PopupPhase.FadingIn:
                popupCanvasGroup.alpha = Mathf.Clamp01(animationElapsed / FadeInDuration);
                if (animationElapsed >= FadeInDuration)
                {
                    phase = PopupPhase.Holding;
                    animationElapsed = 0f;
                }
                break;
            case PopupPhase.Holding:
                popupCanvasGroup.alpha = 1f;
                if (animationElapsed >= HoldDuration)
                {
                    phase = PopupPhase.FadingOut;
                    animationElapsed = 0f;
                }
                break;
            case PopupPhase.FadingOut:
                popupCanvasGroup.alpha = 1f - Mathf.Clamp01(animationElapsed / FadeOutDuration);
                if (animationElapsed >= FadeOutDuration)
                {
                    popupCanvasGroup.alpha = 0f;
                    phase = PopupPhase.Hidden;
                    BeginNextPopup();
                }
                break;
        }
    }

    private void BeginNextPopup()
    {
        if (pendingMessages.Count == 0)
        {
            phase = PopupPhase.Hidden;
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        PopupMessage message = pendingMessages.Dequeue();
        bool showInlineMessage = message.IsInlineMessage;
        titleLabel.gameObject.SetActive(!showInlineMessage);
        subtitleLabel.gameObject.SetActive(!showInlineMessage);
        messageLabel.gameObject.SetActive(showInlineMessage);
        pathogenIconImage.gameObject.SetActive(showInlineMessage && message.icon != null);
        if (showInlineMessage)
        {
            messageLabel.text = message.message;
            pathogenIconImage.sprite = message.icon;
        }
        else
        {
            titleLabel.text = message.title;
            subtitleLabel.text = message.subtitle;
        }
        popupCanvasGroup.alpha = 0f;
        animationElapsed = 0f;
        phase = PopupPhase.FadingIn;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        if (resolvedSoundClip != null && audioSource != null)
            audioSource.PlayOneShot(resolvedSoundClip, HudSfxVolume);
    }

    private void CreatePopupVisuals(RectTransform popupRect)
    {
        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backgroundObject.transform.SetParent(transform, false);
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image backgroundImage = backgroundObject.GetComponent<Image>();
        backgroundImage.color = BackgroundColor;
        backgroundImage.raycastTarget = false;

        GameObject accentObject = new GameObject("Cyan Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        accentObject.transform.SetParent(transform, false);
        RectTransform accentRect = accentObject.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(5f, 0f);
        Image accentImage = accentObject.GetComponent<Image>();
        accentImage.color = AccentColor;
        accentImage.raycastTarget = false;

        titleLabel = CreateLabel("Title", new Vector2(0f, 0.5f), Vector2.one,
            new Vector2(18f, 0f), new Vector2(-14f, -4f), 18f, FontStyles.Bold, AccentColor,
            TextAlignmentOptions.MidlineLeft, false);
        subtitleLabel = CreateLabel("Subtitle", Vector2.zero, new Vector2(1f, 0.5f),
            new Vector2(18f, 4f), new Vector2(-14f, 0f), 14f, FontStyles.Normal, Color.white,
            TextAlignmentOptions.MidlineLeft, true);
        subtitleLabel.gameObject.SetActive(false);

        GameObject pathogenIconObject = new GameObject("Pathogen Icon", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        pathogenIconObject.transform.SetParent(transform, false);
        RectTransform pathogenIconRect = pathogenIconObject.GetComponent<RectTransform>();
        pathogenIconRect.anchorMin = new Vector2(0f, 0.5f);
        pathogenIconRect.anchorMax = new Vector2(0f, 0.5f);
        pathogenIconRect.pivot = new Vector2(0f, 0.5f);
        pathogenIconRect.anchoredPosition = new Vector2(18f, 0f);
        pathogenIconRect.sizeDelta = new Vector2(PathogenIconSize, PathogenIconSize);
        pathogenIconImage = pathogenIconObject.GetComponent<Image>();
        pathogenIconImage.preserveAspect = true;
        pathogenIconImage.raycastTarget = false;
        pathogenIconObject.SetActive(false);

        messageLabel = CreateLabel("Pathogen Message", Vector2.zero, Vector2.one,
            new Vector2(92f, 0f), new Vector2(-18f, 0f), 20f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, false);
        messageLabel.gameObject.SetActive(false);
    }

    private TextMeshProUGUI CreateLabel(string objectName, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, float fontSize, FontStyles fontStyle, Color color,
        TextAlignmentOptions alignment, bool wrap)
    {
        GameObject labelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = anchorMin;
        labelRect.anchorMax = anchorMax;
        labelRect.offsetMin = offsetMin;
        labelRect.offsetMax = offsetMax;
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = Resources.Load<TMP_FontAsset>(BankGothicResourcePath) ?? TMP_Settings.defaultFontAsset;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = color;
        label.alignment = alignment;
        label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    private static void PositionBelowWellnessBar(RectTransform popupRect, RectTransform canvasRect, float wellnessGap)
    {
        float wellnessBottomLocalY = float.PositiveInfinity;
        RectTransform wellnessBarRect = canvasRect.Find("GameProgressBar") as RectTransform;
        RectTransform wellnessValueRect = canvasRect.Find("ProgressValueLabel") as RectTransform;
        if (wellnessBarRect != null)
            wellnessBottomLocalY = GetBottomEdgeCanvasLocalY(wellnessBarRect, canvasRect);
        if (wellnessValueRect != null)
            wellnessBottomLocalY = Mathf.Min(wellnessBottomLocalY, GetBottomEdgeCanvasLocalY(wellnessValueRect, canvasRect));

        float verticalGap = Mathf.Max(MinimumWellnessGap, wellnessGap);
        float yFromCanvasTop = verticalGap;
        if (!float.IsPositiveInfinity(wellnessBottomLocalY))
            yFromCanvasTop = wellnessBottomLocalY - canvasRect.rect.yMax - verticalGap;
        popupRect.anchoredPosition = new Vector2(0f, yFromCanvasTop);
    }

    private static float GetBottomEdgeCanvasLocalY(RectTransform targetRect, RectTransform canvasRect)
    {
        Vector3[] corners = new Vector3[4];
        targetRect.GetWorldCorners(corners);
        float bottomLeftY = canvasRect.InverseTransformPoint(corners[0]).y;
        float bottomRightY = canvasRect.InverseTransformPoint(corners[3]).y;
        return Mathf.Min(bottomLeftY, bottomRightY);
    }
}
