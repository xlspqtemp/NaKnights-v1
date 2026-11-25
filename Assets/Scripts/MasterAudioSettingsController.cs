using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Creates persistent sound and graphics settings tabs for the main menu and pause menu.</summary>
public class MasterAudioSettingsController : MonoBehaviour
{
    private const string MasterVolumePreferenceKey = "MasterVolume";
    private const string GraphicsQualityPreferenceKey = "GraphicsQuality";
    private const string BankGothicFontResourcePath = "BankGothicMediumSDF";
    private const string MasterVolumeParameterName = "MasterVolume";
    private const float DefaultMasterVolume = 1f;
    private const int DefaultGraphicsQuality = 1;
    private const float MinimumMasterDecibels = -80f;
    private const float VolumeRowAnchorY = 0.56f;
    private const float VoiceLineVolumeRowAnchorY = 0.31f;
    private const float VolumeSliderWidth = 360f;
    private const float VolumeSliderHorizontalOffset = -68f;
    private const float VolumeControlsVerticalOffset = -24f;
    private const float VolumeSliderHeight = 30f;
    private const float VolumeSliderTrackInset = 12f;
    private const float SoundLabelGap = 14f;
    private const float SoundLabelHeight = 24f;
    private const float VolumePercentageWidth = 120f;
    private const float VolumePercentageHorizontalOffset = 180f;

    private const int LowQualityIndex = 0;
    private const int NormalQualityIndex = 1;
    private const int MediumQualityIndex = 2;
    private const int HighQualityIndex = 3;

    private static readonly string[] GraphicsQualityNames = { "LOW", "NORMAL", "MEDIUM", "HIGH" };
    private static readonly string[] GraphicsQualityDescriptions =
    {
        "Performance focused",
        "Balanced settings",
        "Sharper image and shadows",
        "Best available visuals"
    };
    private static readonly float[] GraphicsRenderScales = { 0.75f, 0.85f, 0.95f, 1f };
    private static readonly float[] GraphicsShadowDistances = { 20f, 30f, 40f, 50f };
    private static readonly int[] GraphicsShadowCascades = { 1, 1, 2, 4 };
    private static readonly int[] GraphicsShadowResolutions = { 512, 1024, 2048, 4096 };
    private static readonly int[] GraphicsMsaaSamples = { 1, 1, 2, 4 };
    private static readonly float[] GraphicsLodBiases = { 0.75f, 1f, 1.5f, 2f };

    private static readonly Color OverlayColor = new Color(0.005f, 0.015f, 0.025f, 0.88f);
    private static readonly Color PanelColor = new Color(0.02f, 0.055f, 0.075f, 0.99f);
    private static readonly Color AccentColor = new Color(0.05f, 0.88f, 0.95f, 1f);
    private static readonly Color ButtonColor = new Color(0.08f, 0.22f, 0.27f, 1f);
    private static readonly Color TextColor = new Color(0.92f, 0.97f, 1f, 1f);

    [Header("Audio")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioClip buttonClickClip;
    [SerializeField] private AudioClip buttonHoverClip;

    [Header("Pause Menu")]
    [SerializeField] private GameObject pauseMenuPanel;

    private Canvas targetCanvas;
    private AudioMixerGroup masterAudioGroup;
    private GameObject settingsPanel;
    private GameObject soundsTabPanel;
    private GameObject graphicsTabPanel;
    private Slider masterVolumeSlider;
    private Slider voiceLineVolumeSlider;
    private TextMeshProUGUI percentageLabel;
    private TextMeshProUGUI voiceLinePercentageLabel;
    private Button soundsTabButton;
    private Button graphicsTabButton;
    private TMP_FontAsset settingsFont;
    private readonly Button[] graphicsQualityButtons = new Button[4];
    private bool returnToPauseMenu;
    private bool masterParameterAvailable;
    private int selectedGraphicsQuality = DefaultGraphicsQuality;

    private void Awake()
    {
        targetCanvas = GetComponentInParent<Canvas>();
        if (targetCanvas == null)
            targetCanvas = FindFirstObjectByType<Canvas>();
    }

    private void Start()
    {
        InitializeMixerRouting();
        CreateSettingsPanel();

        float savedVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePreferenceKey, DefaultMasterVolume));
        if (masterVolumeSlider != null)
            masterVolumeSlider.SetValueWithoutNotify(savedVolume);
        UpdatePercentageLabel(percentageLabel, savedVolume);
        ApplyMasterVolume(savedVolume, false);

        float savedVoiceLineVolume = VoiceLineAudioPlayer.GetVolume();
        if (voiceLineVolumeSlider != null)
            voiceLineVolumeSlider.SetValueWithoutNotify(savedVoiceLineVolume);
        UpdatePercentageLabel(voiceLinePercentageLabel, savedVoiceLineVolume);
        ApplyVoiceLineVolume(savedVoiceLineVolume);

        selectedGraphicsQuality = Mathf.Clamp(
            PlayerPrefs.GetInt(GraphicsQualityPreferenceKey, DefaultGraphicsQuality),
            LowQualityIndex,
            HighQualityIndex);
        ApplyGraphicsQuality(selectedGraphicsQuality, false);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    /// <summary>Opens settings over the main menu.</summary>
    public void OpenSettingsFromMenu()
    {
        returnToPauseMenu = false;
        ShowSettingsPanel();
    }

    /// <summary>Opens settings from the pause menu while keeping the game paused.</summary>
    public void OpenSettingsFromPause()
    {
        returnToPauseMenu = true;
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(false);

        ShowSettingsPanel();
    }

    /// <summary>Closes settings and returns to the view from which it was opened.</summary>
    public void CloseSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        if (returnToPauseMenu && pauseMenuPanel != null)
            pauseMenuPanel.SetActive(true);
    }

    /// <summary>Displays the sound settings tab.</summary>
    public void ShowSoundsTab()
    {
        SetActiveTab(true);
    }

    /// <summary>Displays the graphics settings tab.</summary>
    public void ShowGraphicsTab()
    {
        SetActiveTab(false);
    }

    /// <summary>Applies and saves one of the four graphics quality profiles.</summary>
    public void SelectLowGraphicsQuality() => SelectGraphicsQuality(LowQualityIndex);

    /// <summary>Applies and saves one of the four graphics quality profiles.</summary>
    public void SelectNormalGraphicsQuality() => SelectGraphicsQuality(NormalQualityIndex);

    /// <summary>Applies and saves one of the four graphics quality profiles.</summary>
    public void SelectMediumGraphicsQuality() => SelectGraphicsQuality(MediumQualityIndex);

    /// <summary>Applies and saves one of the four graphics quality profiles.</summary>
    public void SelectHighGraphicsQuality() => SelectGraphicsQuality(HighQualityIndex);

    private void ShowSettingsPanel()
    {
        if (settingsPanel == null)
            return;

        settingsPanel.SetActive(true);
        settingsPanel.transform.SetAsLastSibling();
        RouteUnassignedAudioSources();
    }

    private void SetActiveTab(bool showSounds)
    {
        if (soundsTabPanel != null)
            soundsTabPanel.SetActive(showSounds);
        if (graphicsTabPanel != null)
            graphicsTabPanel.SetActive(!showSounds);

        SetTabButtonSelected(soundsTabButton, showSounds);
        SetTabButtonSelected(graphicsTabButton, !showSounds);
    }

    private static void SetTabButtonSelected(Button button, bool isSelected)
    {
        if (button == null)
            return;

        button.transition = Selectable.Transition.None;
        if (button.targetGraphic != null)
            button.targetGraphic.color = isSelected ? AccentColor : ButtonColor;

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.color = isSelected ? PanelColor : TextColor;
    }

    private void InitializeMixerRouting()
    {
        if (audioMixer != null)
        {
            AudioMixerGroup[] masterGroups = audioMixer.FindMatchingGroups("Master");
            if (masterGroups.Length > 0)
                masterAudioGroup = masterGroups[0];

            masterParameterAvailable = audioMixer.GetFloat(MasterVolumeParameterName, out _);
        }

        VoiceLineAudioPlayer.SetOutputMixerGroup(masterAudioGroup);
        RouteUnassignedAudioSources();
    }

    private void RouteUnassignedAudioSources()
    {
        if (masterAudioGroup == null)
            return;

        AudioSource[] audioSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (AudioSource source in audioSources)
        {
            if (source != null && source.outputAudioMixerGroup == null)
                source.outputAudioMixerGroup = masterAudioGroup;
        }
    }

    private void ApplyMasterVolume(float normalizedVolume, bool savePreference)
    {
        float clampedVolume = Mathf.Clamp01(normalizedVolume);
        if (audioMixer != null && masterParameterAvailable)
        {
            float decibels = clampedVolume <= 0f
                ? MinimumMasterDecibels
                : Mathf.Clamp(Mathf.Log10(clampedVolume) * 20f, MinimumMasterDecibels, 0f);
            if (!audioMixer.SetFloat(MasterVolumeParameterName, decibels))
                AudioListener.volume = clampedVolume;
        }
        else
        {
            AudioListener.volume = clampedVolume;
        }

        UpdatePercentageLabel(percentageLabel, clampedVolume);
        if (savePreference)
        {
            PlayerPrefs.SetFloat(MasterVolumePreferenceKey, clampedVolume);
            PlayerPrefs.Save();
        }
    }

    private void ApplyVoiceLineVolume(float normalizedVolume)
    {
        float clampedVolume = Mathf.Clamp01(normalizedVolume);
        VoiceLineAudioPlayer.SetVolume(clampedVolume);
        UpdatePercentageLabel(voiceLinePercentageLabel, clampedVolume);
    }

    private void HandleMasterVolumeChanged(float value)
    {
        ApplyMasterVolume(value, true);
    }

    private void HandleVoiceLineVolumeChanged(float value)
    {
        ApplyVoiceLineVolume(value);
    }

    private void SelectGraphicsQuality(int qualityIndex)
    {
        selectedGraphicsQuality = Mathf.Clamp(qualityIndex, LowQualityIndex, HighQualityIndex);
        ApplyGraphicsQuality(selectedGraphicsQuality, true);
    }

    private void ApplyGraphicsQuality(int qualityIndex, bool savePreference)
    {
        int clampedIndex = Mathf.Clamp(qualityIndex, LowQualityIndex, HighQualityIndex);
        UniversalRenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        QualitySettings.lodBias = GraphicsLodBiases[clampedIndex];
        QualitySettings.shadowDistance = GraphicsShadowDistances[clampedIndex];
        QualitySettings.antiAliasing = GraphicsMsaaSamples[clampedIndex] > 1
            ? GraphicsMsaaSamples[clampedIndex]
            : 0;

        if (pipeline != null)
        {
            pipeline.renderScale = GraphicsRenderScales[clampedIndex];
            pipeline.shadowDistance = GraphicsShadowDistances[clampedIndex];
            pipeline.shadowCascadeCount = GraphicsShadowCascades[clampedIndex];
            pipeline.mainLightShadowmapResolution = GraphicsShadowResolutions[clampedIndex];
            pipeline.additionalLightsShadowmapResolution = GraphicsShadowResolutions[clampedIndex];
            pipeline.msaaSampleCount = GraphicsMsaaSamples[clampedIndex];
        }
        else
        {
            Debug.LogWarning("[Graphics Settings] Active render pipeline is not URP; URP profile settings could not be applied.");
        }

        UpdateGraphicsQualityButtonVisuals();
        if (savePreference)
        {
            PlayerPrefs.SetInt(GraphicsQualityPreferenceKey, clampedIndex);
            PlayerPrefs.Save();
        }
    }

    private void UpdateGraphicsQualityButtonVisuals()
    {
        for (int index = 0; index < graphicsQualityButtons.Length; index++)
        {
            Button button = graphicsQualityButtons[index];
            if (button == null)
                continue;

            button.transition = Selectable.Transition.None;
            if (button.targetGraphic != null)
                button.targetGraphic.color = index == selectedGraphicsQuality ? AccentColor : ButtonColor;

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.color = index == selectedGraphicsQuality ? PanelColor : TextColor;
        }
    }

    private static void UpdatePercentageLabel(TextMeshProUGUI label, float normalizedVolume)
    {
        if (label != null)
            label.text = $"{Mathf.RoundToInt(Mathf.Clamp01(normalizedVolume) * 100f)}%";
    }

    private void CreateSettingsPanel()
    {
        if (targetCanvas == null)
        {
            Debug.LogWarning("[Settings] No Canvas was found; settings UI cannot be created.");
            return;
        }

        settingsFont = Resources.Load<TMP_FontAsset>(BankGothicFontResourcePath);
        if (settingsFont == null)
            settingsFont = TMP_Settings.defaultFontAsset;

        settingsPanel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
        settingsPanel.transform.SetParent(targetCanvas.transform, false);
        settingsPanel.transform.SetAsLastSibling();
        RectTransform overlayRect = settingsPanel.GetComponent<RectTransform>();
        SetRect(overlayRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image overlayImage = settingsPanel.GetComponent<Image>();
        overlayImage.color = OverlayColor;
        overlayImage.raycastTarget = true;

        GameObject card = CreateImageObject(settingsPanel.transform, "SettingsCard", PanelColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(680f, 500f), true);
        CreateText(card.transform, "SettingsTitle", "SETTINGS", 34, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(520f, 54f), TextAlignmentOptions.Center);

        soundsTabButton = CreateButton(card.transform, "SoundsTabButton", "SOUNDS", new Vector2(0.5f, 1f),
            new Vector2(-124f, -112f), new Vector2(220f, 48f), ShowSoundsTab);
        graphicsTabButton = CreateButton(card.transform, "GraphicsTabButton", "GRAPHICS", new Vector2(0.5f, 1f),
            new Vector2(124f, -112f), new Vector2(220f, 48f), ShowGraphicsTab);

        soundsTabPanel = new GameObject("SoundsTab", typeof(RectTransform));
        soundsTabPanel.transform.SetParent(card.transform, false);
        SetRect(soundsTabPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        percentageLabel = CreateVolumePercentageLabel(soundsTabPanel.transform, "MasterVolumePercentage", VolumeRowAnchorY);
        masterVolumeSlider = CreateVolumeSlider(soundsTabPanel.transform, "MasterVolumeSlider", VolumeRowAnchorY,
            HandleMasterVolumeChanged);
        CreateVolumeLabel(soundsTabPanel.transform, "MasterVolumeLabel", "MASTER VOLUME", VolumeRowAnchorY);

        voiceLinePercentageLabel = CreateVolumePercentageLabel(soundsTabPanel.transform,
            "VoiceLineVolumePercentage", VoiceLineVolumeRowAnchorY);
        voiceLineVolumeSlider = CreateVolumeSlider(soundsTabPanel.transform, "VoiceLineVolumeSlider",
            VoiceLineVolumeRowAnchorY, HandleVoiceLineVolumeChanged);
        CreateVolumeLabel(soundsTabPanel.transform, "VoiceLineVolumeLabel", "VOICE LINES", VoiceLineVolumeRowAnchorY);

        graphicsTabPanel = new GameObject("GraphicsTab", typeof(RectTransform));
        graphicsTabPanel.transform.SetParent(card.transform, false);
        SetRect(graphicsTabPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        CreateGraphicsQualityButton(graphicsTabPanel.transform, LowQualityIndex, new Vector2(-126f, 18f));
        CreateGraphicsQualityButton(graphicsTabPanel.transform, NormalQualityIndex, new Vector2(126f, 18f));
        CreateGraphicsQualityButton(graphicsTabPanel.transform, MediumQualityIndex, new Vector2(-126f, -96f));
        CreateGraphicsQualityButton(graphicsTabPanel.transform, HighQualityIndex, new Vector2(126f, -96f));
        CreateText(graphicsTabPanel.transform, "GraphicsQualityDescription",
            "Applied now and saved for your next run.", 14, FontStyles.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -151f), new Vector2(560f, 28f), TextAlignmentOptions.Center);

        CreateButton(card.transform, "SettingsBackButton", "BACK", new Vector2(0.5f, 0f),
            new Vector2(0f, 12f), new Vector2(200f, 52f), CloseSettings);

        SetActiveTab(true);
        UpdateGraphicsQualityButtonVisuals();
        settingsPanel.SetActive(false);
    }

    private void CreateGraphicsQualityButton(Transform parent, int qualityIndex, Vector2 anchoredPosition)
    {
        string buttonLabel = $"{GraphicsQualityNames[qualityIndex]}\n{GraphicsQualityDescriptions[qualityIndex]}";
        Button button = CreateButton(parent, $"GraphicsQuality{GraphicsQualityNames[qualityIndex]}Button", buttonLabel,
            new Vector2(0.5f, 0.5f), anchoredPosition, new Vector2(228f, 82f),
            () => SelectGraphicsQuality(qualityIndex));
        graphicsQualityButtons[qualityIndex] = button;

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.fontSize = 16;
            label.textWrappingMode = TextWrappingModes.Normal;
        }
    }

    private TextMeshProUGUI CreateVolumePercentageLabel(Transform parent, string objectName, float rowAnchorY)
    {
        TextMeshProUGUI label = CreateText(parent, objectName, "100%", 22, FontStyles.Bold,
            new Vector2(0.5f, rowAnchorY), new Vector2(VolumePercentageHorizontalOffset, VolumeControlsVerticalOffset),
            new Vector2(VolumePercentageWidth, 44f), TextAlignmentOptions.Right);
        label.overflowMode = TextOverflowModes.Overflow;
        return label;
    }

    private void CreateVolumeLabel(Transform parent, string objectName, string labelText, float rowAnchorY)
    {
        TextMeshProUGUI label = CreateText(parent, objectName, labelText, 18, FontStyles.Bold,
            new Vector2(0.5f, rowAnchorY),
            new Vector2(VolumeSliderHorizontalOffset - VolumeSliderWidth * 0.5f + VolumeSliderTrackInset,
                VolumeControlsVerticalOffset + VolumeSliderHeight * 0.5f + SoundLabelGap),
            new Vector2(VolumeSliderWidth, SoundLabelHeight), TextAlignmentOptions.Left);
        label.rectTransform.pivot = Vector2.zero;
    }

    private Slider CreateVolumeSlider(Transform parent, string objectName, float rowAnchorY,
        UnityEngine.Events.UnityAction<float> onValueChanged)
    {
        GameObject sliderObject = new GameObject(objectName, typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(parent, false);
        SetRect(sliderObject.GetComponent<RectTransform>(), new Vector2(0.5f, rowAnchorY),
            new Vector2(0.5f, rowAnchorY), new Vector2(0.5f, 0.5f),
            new Vector2(VolumeSliderHorizontalOffset, VolumeControlsVerticalOffset),
            new Vector2(VolumeSliderWidth, VolumeSliderHeight));

        Image background = CreateImage(sliderObject.transform, "Background", new Color(0.035f, 0.09f, 0.11f, 1f),
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, true);
        RectTransform fillArea = CreateRectTransform(sliderObject.transform, "FillArea",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24f, 0f));
        Image fill = CreateImage(fillArea, "Fill", AccentColor,
            new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, false);
        RectTransform handleArea = CreateRectTransform(sliderObject.transform, "HandleArea",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24f, 0f));
        Image handle = CreateImage(handleArea, "Handle", TextColor,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 0f), true);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;
        slider.onValueChanged.AddListener(onValueChanged);
        background.raycastTarget = true;
        fill.raycastTarget = false;
        handle.raycastTarget = true;
        return slider;
    }

    private Button CreateButton(Transform parent, string objectName, string label, Vector2 anchor,
        Vector2 anchoredPosition, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(AudioSource));
        buttonObject.transform.SetParent(parent, false);
        SetRect(buttonObject.GetComponent<RectTransform>(), anchor, anchor, anchor, anchoredPosition, size);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = ButtonColor;
        buttonImage.raycastTarget = true;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = ButtonColor,
            highlightedColor = AccentColor,
            pressedColor = new Color(0.04f, 0.12f, 0.16f, 1f),
            selectedColor = AccentColor,
            disabledColor = new Color(0.2f, 0.25f, 0.28f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };
        button.onClick.AddListener(onClick);

        TextMeshProUGUI text = CreateText(buttonObject.transform, "Label", label, 20, FontStyles.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        AudioSource audioSource = buttonObject.GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        ButtonSFX buttonSfx = buttonObject.AddComponent<ButtonSFX>();
        buttonSfx.ConfigureFeedback(buttonClickClip, buttonHoverClip, masterAudioGroup);
        return button;
    }

    private static GameObject CreateImageObject(Transform parent, string objectName, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, bool raycastTarget)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        SetRect(imageObject.GetComponent<RectTransform>(), anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return imageObject;
    }

    private static Image CreateImage(Transform parent, string objectName, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, bool raycastTarget)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        SetRect(imageObject.GetComponent<RectTransform>(), anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private static RectTransform CreateRectTransform(Transform parent, string objectName,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject rectObject = new GameObject(objectName, typeof(RectTransform));
        rectObject.transform.SetParent(parent, false);
        RectTransform rect = rectObject.GetComponent<RectTransform>();
        SetRect(rect, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
        return rect;
    }

    private TextMeshProUGUI CreateText(Transform parent, string objectName, string content, int fontSize,
        FontStyles fontStyle, Vector2 anchor, Vector2 anchoredPosition, Vector2 sizeDelta, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetRect(rect, anchor, anchor, anchor, anchoredPosition, sizeDelta);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = settingsFont != null ? settingsFont : TMP_Settings.defaultFontAsset;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.fontWeight = FontWeight.Bold;
        text.color = TextColor;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.localScale = Vector3.one;
    }
}
