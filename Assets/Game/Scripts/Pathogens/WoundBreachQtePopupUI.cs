using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>Shows a video alert for wound QTEs and can focus the camera on the affected wound.</summary>
[DisallowMultipleComponent]
public sealed class WoundBreachQtePopupUI : MonoBehaviour
{
    private const int CirculatoryLayerNumber = 2;
    private const string HudCanvasObjectName = "HUDCanvas";
    private const string PopupRootObjectName = "Wound Breach QTE Popup";
    private const string VideoResourcePath = "Wound breach pop up";
    private const string FontResourcePath = "BankGothicMediumSDF";
    private const int VideoTextureWidth = 1280;
    private const int VideoTextureHeight = 720;
    private const float MaximumPanelWidth = 900f;
    private const float MaximumPanelHeight = 620f;
    private const float PanelWidthFraction = 0.62f;
    private const float PanelHeightFraction = 0.72f;
    private const float PanelContentPadding = 48f;
    private const float NonVideoPanelHeight = 176f;
    private const float HeaderHeight = 42f;
    private const float ButtonHeight = 58f;
    private const float ButtonBottomInset = 28f;
    private const float HeaderTopInset = 22f;
    private const float VideoHeaderGap = 12f;
    private const float OverlayDimAlpha = 0.72f;
    private static readonly Color OverlayDimColor = new Color(0.002f, 0.012f, 0.022f, OverlayDimAlpha);
    private static readonly Color CyanPanelColor = new Color(0.015f, 0.36f, 0.42f, 0.98f);
    private static readonly Color CyanAccentColor = new Color(0.16f, 0.96f, 1f, 1f);
    private static readonly Color DarkControlColor = new Color(0.005f, 0.035f, 0.05f, 1f);
    private static readonly Color FocusButtonColor = new Color(0.025f, 0.84f, 0.94f, 1f);
    private static readonly Color FocusButtonPulseColor = new Color(0.18f, 0.98f, 1f, 1f);
    private static readonly Color SkipButtonColor = new Color(0.96f, 0.035f, 0.055f, 1f);
    private static readonly Color SkipButtonPulseColor = new Color(1f, 0.22f, 0.18f, 1f);
    private static readonly Color SkipLabelColor = new Color(1f, 1f, 1f, 1f);

    private RectTransform canvasRect;
    private RectTransform panelRect;
    private RectTransform videoRect;
    private GameObject overlayRoot;
    private RawImage videoImage;
    private VideoPlayer videoPlayer;
    private RenderTexture videoTexture;
    private WoundRepairPoint currentWound;
    private Vector2 previousCanvasSize = new Vector2(-1f, -1f);
    private bool videoPreparationRequested;

    /// <summary>Shows the wound alert whenever a wound repair QTE becomes active.</summary>
    public static void ShowFor(WoundRepairPoint wound)
    {
        if (wound == null)
            return;

        GameObject hudCanvasObject = GameObject.Find(HudCanvasObjectName);
        Canvas hudCanvas = hudCanvasObject != null ? hudCanvasObject.GetComponent<Canvas>() : null;
        if (hudCanvas == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas candidate in canvases)
            {
                if (candidate != null && candidate.isActiveAndEnabled &&
                    candidate.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    hudCanvas = candidate;
                    break;
                }
            }
        }

        if (hudCanvas == null)
        {
            Debug.LogWarning("The wound QTE alert could not find a screen-space HUD canvas.", wound);
            return;
        }

        WoundBreachQtePopupUI popup = hudCanvas.GetComponent<WoundBreachQtePopupUI>();
        if (popup == null)
            popup = hudCanvas.gameObject.AddComponent<WoundBreachQtePopupUI>();
        popup.Show(wound);
    }

    /// <summary>Closes the alert when its wound is repaired, untreated, or removed.</summary>
    public static void HideFor(WoundRepairPoint wound)
    {
        if (wound == null)
            return;

        WoundBreachQtePopupUI popup = FindFirstObjectByType<WoundBreachQtePopupUI>();
        if (popup != null && popup.currentWound == wound)
            popup.Hide();
    }

    private void Awake()
    {
        BuildPopup();
    }

    private void LateUpdate()
    {
        if (canvasRect == null)
            return;

        Vector2 canvasSize = canvasRect.rect.size;
        if ((canvasSize - previousCanvasSize).sqrMagnitude > 1f)
            ApplyLayout();
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= HandleVideoPrepared;
            videoPlayer.errorReceived -= HandleVideoError;
        }
        if (videoTexture != null)
        {
            videoTexture.Release();
            Destroy(videoTexture);
        }
    }

    private void BuildPopup()
    {
        canvasRect = transform as RectTransform;
        if (canvasRect == null)
        {
            enabled = false;
            Debug.LogWarning("The wound QTE alert must be attached to a Canvas.", this);
            return;
        }

        overlayRoot = new GameObject(PopupRootObjectName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlayRoot.transform.SetParent(transform, false);
        RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image dimImage = overlayRoot.GetComponent<Image>();
        dimImage.color = OverlayDimColor;
        dimImage.raycastTarget = true;

        GameObject panelObject = CreateUiObject("Cyan Wound Alert Panel", overlayRoot.transform, true);
        panelRect = panelObject.GetComponent<RectTransform>();
        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = CyanPanelColor;
        panelImage.raycastTarget = false;
        Outline panelOutline = panelObject.AddComponent<Outline>();
        panelOutline.effectColor = CyanAccentColor;
        panelOutline.effectDistance = new Vector2(3f, -3f);

        TextMeshProUGUI header = CreateText("Wound Alert Header", panelRect,
            "WOUND BREACH DETECTED", 30f, CyanAccentColor, TextAlignmentOptions.Center);
        header.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        header.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        header.rectTransform.pivot = new Vector2(0.5f, 1f);

        GameObject videoFrameObject = CreateUiObject("Wound Breach Video Frame", panelRect, true);
        videoRect = videoFrameObject.GetComponent<RectTransform>();
        Image videoFrame = videoFrameObject.GetComponent<Image>();
        videoFrame.color = DarkControlColor;
        videoFrame.raycastTarget = false;
        Outline videoOutline = videoFrameObject.AddComponent<Outline>();
        videoOutline.effectColor = CyanAccentColor;
        videoOutline.effectDistance = new Vector2(2f, -2f);

        GameObject videoImageObject = CreateUiObject("Wound Breach Video", videoRect, false);
        RectTransform videoImageRect = videoImageObject.GetComponent<RectTransform>();
        videoImageRect.anchorMin = Vector2.zero;
        videoImageRect.anchorMax = Vector2.one;
        videoImageRect.offsetMin = new Vector2(2f, 2f);
        videoImageRect.offsetMax = new Vector2(-2f, -2f);
        videoImage = videoImageObject.AddComponent<RawImage>();
        videoImage.raycastTarget = false;

        videoTexture = new RenderTexture(VideoTextureWidth, VideoTextureHeight, 0, RenderTextureFormat.ARGB32)
        {
            name = "Wound Breach Popup Video Texture",
            useMipMap = false,
            autoGenerateMips = false
        };
        videoTexture.Create();
        videoImage.texture = videoTexture;

        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.source = VideoSource.VideoClip;
        videoPlayer.clip = Resources.Load<VideoClip>(VideoResourcePath);
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoTexture;
        videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.prepareCompleted += HandleVideoPrepared;
        videoPlayer.errorReceived += HandleVideoError;
        if (videoPlayer.clip == null)
            Debug.LogWarning("Wound breach popup video was not found at Resources/" + VideoResourcePath + ".mp4", this);

        CreateButton(panelRect, "Focus", "FOCUS", new Vector2(-125f, ButtonBottomInset),
            FocusButtonColor, DarkControlColor, FocusButtonPulseColor, HandleFocusClicked);
        CreateButton(panelRect, "Skip", "SKIP", new Vector2(125f, ButtonBottomInset),
            SkipButtonColor, SkipLabelColor, SkipButtonPulseColor, HandleSkipClicked);
        ApplyLayout();
        overlayRoot.SetActive(false);
    }

    private void Show(WoundRepairPoint wound)
    {
        if (wound == null || !wound.isActiveAndEnabled)
            return;

        currentWound = wound;
        ApplyLayout();
        overlayRoot.SetActive(true);
        if (videoPlayer == null || videoPlayer.clip == null)
            return;

        if (videoPlayer.isPrepared)
        {
            videoPlayer.time = 0d;
            videoPlayer.Play();
        }
        else if (!videoPreparationRequested)
        {
            videoPreparationRequested = true;
            videoPlayer.Prepare();
        }
    }

    private void Hide()
    {
        currentWound = null;
        if (overlayRoot != null)
            overlayRoot.SetActive(false);
        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            videoPlayer.Pause();
            videoPlayer.time = 0d;
        }
    }

    private void HandleVideoPrepared(VideoPlayer player)
    {
        videoPreparationRequested = false;
        if (overlayRoot != null && overlayRoot.activeInHierarchy && currentWound != null)
            player.Play();
    }

    private void HandleVideoError(VideoPlayer player, string message)
    {
        videoPreparationRequested = false;
        Debug.LogWarning("Wound breach popup video failed to prepare: " + message, this);
    }

    private void HandleFocusClicked()
    {
        WoundRepairPoint wound = currentWound;
        if (wound != null && wound.isActiveAndEnabled)
        {
            CameraScript cameraController = FindFirstObjectByType<CameraScript>();
            if (cameraController != null)
            {
                if (cameraController.SelectedLayer != CirculatoryLayerNumber)
                    cameraController.SelectLayerWithoutCameraMove(CirculatoryLayerNumber);

                cameraController.ClearFollowTarget();
                cameraController.FocusOnWorldPosition(wound.transform.position);
            }
            else
            {
                Debug.LogWarning("The wound QTE focus button could not find CameraScript.", wound);
            }
        }

        Hide();
    }

    private void HandleSkipClicked()
    {
        Hide();
    }

    private void ApplyLayout()
    {
        if (canvasRect == null || panelRect == null)
            return;

        Rect bounds = canvasRect.rect;
        previousCanvasSize = bounds.size;
        float panelWidth = Mathf.Min(MaximumPanelWidth, bounds.width * PanelWidthFraction);
        float panelHeight = Mathf.Min(MaximumPanelHeight, bounds.height * PanelHeightFraction);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

        Transform headerTransform = panelRect.Find("Wound Alert Header");
        RectTransform headerRect = headerTransform as RectTransform;
        if (headerRect != null)
        {
            headerRect.anchoredPosition = new Vector2(0f, -HeaderTopInset);
            headerRect.sizeDelta = new Vector2(panelWidth - PanelContentPadding, HeaderHeight);
        }

        float availableVideoHeight = Mathf.Max(120f, panelHeight - NonVideoPanelHeight);
        float availableVideoWidth = Mathf.Max(200f, panelWidth - PanelContentPadding);
        float videoWidth = Mathf.Min(availableVideoWidth, availableVideoHeight * 16f / 9f);
        float videoHeight = videoWidth * 9f / 16f;
        videoRect.anchorMin = new Vector2(0.5f, 0.5f);
        videoRect.anchorMax = new Vector2(0.5f, 0.5f);
        videoRect.pivot = new Vector2(0.5f, 0.5f);
        videoRect.sizeDelta = new Vector2(videoWidth, videoHeight);
        videoRect.anchoredPosition = new Vector2(0f,
            panelHeight * 0.5f - HeaderTopInset - HeaderHeight - VideoHeaderGap - videoHeight * 0.5f);

        RectTransform focusButtonRect = panelRect.Find("Focus") as RectTransform;
        RectTransform skipButtonRect = panelRect.Find("Skip") as RectTransform;
        float buttonWidth = Mathf.Min(260f, panelWidth * 0.34f);
        if (focusButtonRect != null)
        {
            focusButtonRect.anchoredPosition = new Vector2(-buttonWidth * 0.58f, ButtonBottomInset);
            focusButtonRect.sizeDelta = new Vector2(buttonWidth, ButtonHeight);
        }
        if (skipButtonRect != null)
        {
            skipButtonRect.anchoredPosition = new Vector2(buttonWidth * 0.58f, ButtonBottomInset);
            skipButtonRect.sizeDelta = new Vector2(buttonWidth, ButtonHeight);
        }
    }

    private void CreateButton(Transform parent, string objectName, string label,
        Vector2 anchoredPosition, Color buttonColor, Color labelColor, Color pulseColor,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(objectName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(240f, ButtonHeight);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = buttonColor;
        buttonImage.raycastTarget = true;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = CreateButtonColors(buttonColor);
        button.onClick.AddListener(onClick);
        WoundBreachButtonHoverPulse hoverPulse = buttonObject.AddComponent<WoundBreachButtonHoverPulse>();
        hoverPulse.Initialize(buttonImage, buttonColor, pulseColor);

        TextMeshProUGUI labelText = CreateText(objectName + " Label", buttonObject.transform,
            label, 22f, labelColor, TextAlignmentOptions.Center);
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private TextMeshProUGUI CreateText(string objectName, Transform parent, string value,
        float fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = Resources.Load<TMP_FontAsset>(FontResourcePath) ?? TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = color;
        text.alignment = alignment;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(12f, fontSize - 4f);
        text.fontSizeMax = fontSize;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent, bool addImage)
    {
        GameObject createdObject = addImage
            ? new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
            : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        createdObject.transform.SetParent(parent, false);
        return createdObject;
    }

    private static ColorBlock CreateButtonColors(Color buttonColor)
    {
        return new ColorBlock
        {
            normalColor = buttonColor,
            highlightedColor = buttonColor,
            pressedColor = buttonColor,
            selectedColor = buttonColor,
            disabledColor = buttonColor,
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };
    }
}

/// <summary>Adds a subtle size and color pulse while a wound alert button is hovered.</summary>
internal sealed class WoundBreachButtonHoverPulse : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float PulseFrequency = 3.4f;
    private const float MaximumScaleIncrease = 0.045f;

    private Image targetImage;
    private Color baseColor;
    private Color pulseColor;
    private Vector3 baseScale;
    private bool isHovered;

    /// <summary>Configures the button image and colors used by the hover pulse.</summary>
    public void Initialize(Image image, Color restingColor, Color highlightedColor)
    {
        targetImage = image;
        baseColor = restingColor;
        pulseColor = highlightedColor;
        baseScale = transform.localScale;
    }

    /// <summary>Starts the pulse when the pointer enters the button.</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
    }

    /// <summary>Restores the button's resting appearance when the pointer leaves.</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
    }

    private void LateUpdate()
    {
        if (targetImage == null)
            return;

        if (!isHovered)
        {
            targetImage.color = baseColor;
            transform.localScale = baseScale;
            return;
        }

        float pulse = (Mathf.Sin(Time.unscaledTime * PulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
        targetImage.color = Color.Lerp(baseColor, pulseColor, pulse);
        transform.localScale = baseScale * (1f + pulse * MaximumScaleIncrease);
    }

    private void OnDisable()
    {
        isHovered = false;
        if (targetImage != null)
            targetImage.color = baseColor;
        transform.localScale = baseScale;
    }
}
