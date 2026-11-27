using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Spawns scheduled food and fluid batches, then routes each item through the digestive pathway.
/// </summary>
public sealed class DigestiveSystemController : MonoBehaviour
{
    private const int ItemsPerBatch = 5;
    private const float SpawnIntervalSeconds = 0.5f;
    private const float ManualCooldownSeconds = 5f;
    private const float DefaultTransitSpeed = 20f;
    private const string OrdersContainerPath = "/Event Handlers/HUDCanvas/DigestiveOrdersContainer";
    private const string SpoiledFoodQTEId = "Ate expired/spoiled food";
    private const string ExpiredFoodEventName = "Ate expired/spoiled food";
    private const string JunkFoodEventName = "Junk food binge";
    private const string AccidentallyAteSpoiledFoodEventName = "Accidentally Ate Spoiled Food";
    private const string ContaminatedWaterEventName = "Accidentally Drank Contaminated Water";
    private const float BarrierDamagePerRandomEvent = 20f;
    private const float BarrierRestorePerAction = 20f;
    private const float BarrierMaximum = 100f;
    private const string BarrierGaugeObjectName = "DigestiveBarrierGauge";
    private const string BreachOverlayObjectName = "DigestiveBarrierBreachOverlay";
    private const string BreachWarningObjectName = "DigestiveBarrierBreachWarning";
    private const string BreachWarningMessage = "BARRIER BREACHED";
    private const string BreachRepairMessage = "BARRIER RESTORING...";
    private const string BreachWarningFontResourcePath = "BankGothicMediumSDF";
    private const float BreachWarningBlinkDuration = 0.6f;
    private const float BarrierGaugeWidth = 600f;
    private const float BarrierGaugeHeight = 600f;
    private const float BarrierGaugeVerticalOffset = -338f;
    private const float DigestiveQteVerticalOffset = BarrierGaugeVerticalOffset;
    private const string VomitButtonIconResourcePath = "UI/Vomit icon final";
    private const float QteButtonIconSize = 280f;
    private const string WindAssistRootObjectName = "DigestiveBarrierWindAssist";
    private const string WindButtonObjectName = "WindHoldButton";
    private const string WindButtonLabelObjectName = "WindButtonLabel";
    private const string WindLockedTooltipObjectName = "WindLockedTooltip";
    private const string WindProgressTrackObjectName = "WindProgressTrack";
    private const string WindProgressFillObjectName = "WindProgressFill";
    private const string WindSweetSpotBandObjectName = "WindSweetSpotBand";
    private const float WindButtonSize = 136f;
    private const float WindAssistRootSize = 190f;
    private const float WindAssistHorizontalOffset = 0f;
    private const float WindHoldSuppressVomitThreshold = 0.18f;
    private const float BreachGraceFloor = 0.001f;
    private const float WarningBadgeThresholdPercent = 35f;
    private const float RedWarningThresholdPercent = 15f;
    private static readonly Vector2 QteButtonSize = new Vector2(QteButtonIconSize, QteButtonIconSize);
    private static readonly Vector2 QteButtonCenter = new Vector2(0.5f, 0.5f);
    private static readonly Color QteButtonNormalColor = Color.white;
    private static readonly Color QteButtonHighlightColor = new Color(0.85f, 0.95f, 1f, 1f);
    private static readonly Color QteButtonPressedColor = new Color(0.65f, 0.82f, 0.95f, 1f);
    private static readonly Color QteButtonDisabledColor = new Color(0.45f, 0.48f, 0.52f, 0.85f);
    private static readonly Color WindButtonNormalColor = new Color(0.015f, 0.16f, 0.2f, 0.92f);
    private static readonly Color WindButtonLockedColor = new Color(0.16f, 0.17f, 0.2f, 0.92f);
    private static readonly Color WindProgressColor = new Color(0.1f, 0.9f, 1f, 1f);
    private static readonly Color WindSweetSpotColor = new Color(1f, 0.72f, 0.15f, 0.9f);
    private static Sprite vomitButtonIconSprite;
    private static Sprite barrierGaugeSprite;
    private static Sprite windButtonCircleSprite;
    private static Sprite windRingSprite;

    [SerializeField] private Image barrierGaugeImage;
    [SerializeField] private TextMeshProUGUI barrierTitleLabel;
    [SerializeField] private TextMeshProUGUI barrierPercentLabel;
    [SerializeField] private Image breachOverlay;
    [SerializeField] private TextMeshProUGUI breachWarningText;

    [Header("Digestive Barrier Assist")]
    [SerializeField] private bool useLegacyDigestiveAssist = false;
    [SerializeField, Min(0f)] private float barrierWindRate = 12f;
    [SerializeField, Range(0f, 1f)] private float respiratoryQteDecayMultiplier = 0.5f;
    [SerializeField, Min(0f)] private float breachGracePeriod = 2f;
    [SerializeField] private KeyCode windKey = KeyCode.Space;
    [SerializeField] private bool enableSweetSpotBand;
    [SerializeField, Range(0f, 100f)] private float sweetSpotStartPercent = 70f;
    [SerializeField, Range(0f, 100f)] private float sweetSpotEndPercent = 85f;
    [SerializeField] private AudioClip barrierWarningTune;
    [SerializeField, Min(1f)] private float warningTuneMaximumPitch = 1.75f;

    private static Sprite breachOverlaySprite;

    private static readonly string[] DefaultPathwayObjectPaths =
    {
        "/Layers/Digestive/Pathway/Stomach",
        "/Layers/Digestive/Pathway/Intestine Entry",
        "/Layers/Digestive/Pathway/Intestine",
        "/Layers/Digestive/Pathway/Intestine (1)",
        "/Layers/Digestive/Pathway/Intestine",
        "/Layers/Digestive/Pathway/Intestine Entry",
        "/Layers/Digestive/Pathway/Large Intestine",
        "/Layers/Digestive/Pathway/Large Intestine (1)",
        "/Layers/Digestive/Pathway/Anus"
    };

    [SerializeField] private GameObject foodPrefab;
    [SerializeField] private GameObject contaminatedFoodPrefab;
    [SerializeField] private GameObject fluidPrefab;
    [SerializeField] private GameObject contaminatedFluidPrefab;
    [SerializeField] private Transform mouthPoint;
    [SerializeField] private Transform anusPoint;
    [SerializeField] private Transform[] pathwayWaypoints;
    [SerializeField] private DayCounterUI dayCounter;

    private readonly Queue<DigestiveBatchRequest> pendingBatches = new Queue<DigestiveBatchRequest>();
    private RoutineSystem routineSystem;
    private Button eatButton;
    private Button vomitButton;
    private Button windButton;
    private Image windButtonBackground;
    private Image windProgressImage;
    private Image windSweetSpotImage;
    private TextMeshProUGUI windButtonLabel;
    private TextMeshProUGUI windLockedTooltip;
    private AudioSource barrierWarningAudioSource;
    private bool barrierWarningTunePaused;
    private bool windPointerHeld;
    private bool suppressVomitClickAfterWind;
    private float windPointerHeldDuration;
    private float breachGraceRemaining = -1f;
    private readonly HashSet<LungInfectionResponse> activeRespiratoryRepairs = new HashSet<LungInfectionResponse>();
    private TextMeshProUGUI eatLabel;
    private TextMeshProUGUI vomitLabel;
    private TextMeshProUGUI vomitSleepingLabel;
    private Coroutine batchQueueRoutine;
    private DigestiveBatchRequest currentBatch;
    private float eatCooldownRemaining;
    private float vomitCooldownRemaining;
    private int lastContaminatedFoodEventDay = int.MinValue;
    private int lastContaminatedFoodEventHour = -1;
    private int lastContaminatedWaterEventDay = int.MinValue;
    private int lastContaminatedWaterEventHour = -1;
    private BarrierHealth barrierHealth;
    private bool isBarrierRepairing;

    private bool IsSleeping => routineSystem != null && routineSystem.CurrentActivity == RoutineActivity.Sleeping;
    private bool IsBatchRunning => batchQueueRoutine != null || pendingBatches.Count > 0;

    private sealed class DigestiveBatchRequest
    {
        public GameObject prefab;
        public bool isScheduledMeal;
        public bool isScheduledWater;
        public int day;
        public int hour;
        public string eventKey;
        public readonly List<GameObject> spawnedItems = new List<GameObject>();
    }

    private void OnEnable()
    {
        RoutineSystem.OnRoutineActivityChanged += HandleRoutineActivityChanged;
        RandomEventSystem.OnRandomEventTriggered += HandleRandomEventTriggered;
        LungInfectionResponse.RepairQTEStateChanged += HandleRespiratoryRepairQteStateChanged;
    }

    private void Start()
    {
        if (mouthPoint == null)
            mouthPoint = GameObject.Find("/Layers/Digestive/Pathway/Mouth")?.transform;
        if (anusPoint == null)
            anusPoint = GameObject.Find("/Layers/Digestive/Pathway/Anus")?.transform;
        if (pathwayWaypoints == null || pathwayWaypoints.Length == 0)
            pathwayWaypoints = FindDefaultPathwayWaypoints();
        if (anusPoint == null && pathwayWaypoints != null && pathwayWaypoints.Length > 0)
            anusPoint = pathwayWaypoints[pathwayWaypoints.Length - 1];
        if (dayCounter == null)
            dayCounter = FindFirstObjectByType<DayCounterUI>();
        routineSystem = RoutineSystem.Instance;
        InitializeBarrierHealth();
        InitializeBreachVisuals();

        GameObject ordersContainer = GameObject.Find(OrdersContainerPath);
        if (ordersContainer != null)
        {
            CenterOrdersContainer(ordersContainer.GetComponent<RectTransform>());
            InitializeBarrierGauge(ordersContainer.GetComponent<RectTransform>());
            InitializeWindAssist(ordersContainer.GetComponent<RectTransform>());
            Transform eatTransform = ordersContainer.transform.Find("EatButton");
            Transform vomitTransform = ordersContainer.transform.Find("VomitButton");
            if (eatTransform != null)
            {
                eatButton = eatTransform.GetComponent<Button>();
                eatLabel = eatTransform.GetComponentInChildren<TextMeshProUGUI>(true);
            }
            if (vomitTransform != null)
            {
                vomitButton = vomitTransform.GetComponent<Button>();
                vomitLabel = vomitTransform.GetComponentInChildren<TextMeshProUGUI>(true);
                if (vomitButton != null)
                    vomitButton.gameObject.SetActive(true);
                CenterQteButton(vomitButton);
                ConfigureVomitButtonIcon();
                CreateVomitSleepingLabel();
                EmbedVomitButtonInsideWindButton();
            }
        }

        if (eatButton != null)
            eatButton.gameObject.SetActive(false);

        if (vomitButton != null)
            vomitButton.onClick.AddListener(HandleEmbeddedVomitPressed);
        else
            Debug.LogError("DigestiveOrdersContainer is missing its VomitButton.", this);

        if (mouthPoint == null || !HasCompletePathway())
            Debug.LogError("DigestiveSystemController requires Mouth and all digestive pathway waypoints through Anus.", this);
        if (foodPrefab == null || contaminatedFoodPrefab == null || fluidPrefab == null || contaminatedFluidPrefab == null)
            Debug.LogError("DigestiveSystemController requires food, contaminated food, fluid, and contaminated fluid prefab references.", this);
        if (dayCounter == null)
            Debug.LogError("DigestiveSystemController could not find the DayCounterUI.", this);

        RefreshActiveRespiratoryRepairs();
        RefreshButtonStates();
        RefreshWindAssistUI();
    }

    private void InitializeBarrierHealth()
    {
        GameObject digestiveRoot = GameObject.Find("/Layers/Digestive");
        if (digestiveRoot == null)
        {
            Debug.LogError("DigestiveSystemController could not find the Digestive layer root for BarrierHealth.", this);
            return;
        }

        barrierHealth = digestiveRoot.GetComponent<BarrierHealth>();
        if (barrierHealth == null)
            barrierHealth = digestiveRoot.AddComponent<BarrierHealth>();
    }

    private void InitializeBarrierGauge(RectTransform panelRect)
    {
        if (panelRect == null)
            return;

        Transform existingGauge = panelRect.Find(BarrierGaugeObjectName);
        if (barrierGaugeImage == null && existingGauge != null)
            barrierGaugeImage = existingGauge.GetComponent<Image>();

        if (barrierGaugeImage == null)
        {
            GameObject gaugeObject = new GameObject(BarrierGaugeObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gaugeObject.transform.SetParent(panelRect, false);
            barrierGaugeImage = gaugeObject.GetComponent<Image>();
        }

        RectTransform gaugeRect = barrierGaugeImage.rectTransform;
        barrierGaugeImage.gameObject.SetActive(true);
        barrierGaugeImage.enabled = true;
        gaugeRect.anchorMin = new Vector2(0.5f, 0.5f);
        gaugeRect.anchorMax = new Vector2(0.5f, 0.5f);
        gaugeRect.pivot = new Vector2(0.5f, 0.5f);
        gaugeRect.sizeDelta = new Vector2(BarrierGaugeWidth, BarrierGaugeHeight);
        gaugeRect.anchoredPosition = new Vector2(0f, BarrierGaugeVerticalOffset);
        barrierGaugeImage.transform.SetAsFirstSibling();
        barrierGaugeImage.sprite = LoadBarrierGaugeSprite();
        barrierGaugeImage.type = Image.Type.Filled;
        barrierGaugeImage.fillMethod = Image.FillMethod.Radial360;
        barrierGaugeImage.fillOrigin = (int)Image.Origin360.Bottom;
        barrierGaugeImage.preserveAspect = true;
        barrierGaugeImage.raycastTarget = false;

        if (barrierTitleLabel == null)
            barrierTitleLabel = FindOrCreateGaugeLabel(barrierGaugeImage.transform, "BarrierTitle", "BARRIER", 18f, 22f);
        if (barrierPercentLabel == null)
            barrierPercentLabel = FindOrCreateGaugeLabel(barrierGaugeImage.transform, "BarrierPercent", "100%", -18f, 26f);
        barrierTitleLabel.gameObject.SetActive(false);
        barrierPercentLabel.gameObject.SetActive(false);

        RefreshBarrierGauge();
    }

    private void InitializeWindAssist(RectTransform panelRect)
    {
        if (panelRect == null)
            return;

        Transform existingRoot = panelRect.Find(WindAssistRootObjectName);
        if (existingRoot != null)
            return;

        GameObject rootObject = new GameObject(WindAssistRootObjectName, typeof(RectTransform));
        rootObject.transform.SetParent(panelRect, false);
        RectTransform rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.anchorMin = QteButtonCenter;
        rootRect.anchorMax = QteButtonCenter;
        rootRect.pivot = QteButtonCenter;
        rootRect.sizeDelta = new Vector2(WindAssistRootSize, WindAssistRootSize);
        rootRect.anchoredPosition = new Vector2(WindAssistHorizontalOffset, DigestiveQteVerticalOffset);

        Image ringTrack = CreateWindAssistImage(WindProgressTrackObjectName, rootRect, GetWindRingSprite(), new Color(0.015f, 0.07f, 0.09f, 0.9f));
        ringTrack.rectTransform.sizeDelta = new Vector2(WindAssistRootSize, WindAssistRootSize);

        if (enableSweetSpotBand)
        {
            Sprite sweetSpotSprite = CreateWindAssistSprite("Digestive Wind Sweet Spot", true,
                sweetSpotStartPercent, sweetSpotEndPercent);
            windSweetSpotImage = CreateWindAssistImage(WindSweetSpotBandObjectName, rootRect, sweetSpotSprite, WindSweetSpotColor);
            windSweetSpotImage.rectTransform.sizeDelta = new Vector2(WindAssistRootSize, WindAssistRootSize);
        }

        windProgressImage = CreateWindAssistImage(WindProgressFillObjectName, rootRect, GetWindRingSprite(), WindProgressColor);
        windProgressImage.rectTransform.sizeDelta = new Vector2(WindAssistRootSize, WindAssistRootSize);
        windProgressImage.type = Image.Type.Filled;
        windProgressImage.fillMethod = Image.FillMethod.Radial360;
        windProgressImage.fillOrigin = (int)Image.Origin360.Top;
        windProgressImage.fillClockwise = true;
        windProgressImage.fillAmount = 1f;

        GameObject buttonObject = new GameObject(WindButtonObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(EventTrigger));
        buttonObject.transform.SetParent(rootRect, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = QteButtonCenter;
        buttonRect.anchorMax = QteButtonCenter;
        buttonRect.pivot = QteButtonCenter;
        buttonRect.sizeDelta = new Vector2(WindButtonSize, WindButtonSize);

        windButtonBackground = buttonObject.GetComponent<Image>();
        windButtonBackground.sprite = GetWindButtonCircleSprite();
        windButtonBackground.color = WindButtonNormalColor;
        windButtonBackground.raycastTarget = true;

        windButton = buttonObject.GetComponent<Button>();
        windButton.targetGraphic = windButtonBackground;
        windButton.transition = Selectable.Transition.ColorTint;
        windButton.colors = CreateWindAssistButtonColors();

        EventTrigger pointerEvents = buttonObject.GetComponent<EventTrigger>();
        pointerEvents.triggers = new List<EventTrigger.Entry>();
        AddWindPointerEvent(pointerEvents, EventTriggerType.PointerDown, HandleWindPointerDown);
        AddWindPointerEvent(pointerEvents, EventTriggerType.PointerUp, HandleWindPointerReleased);
        AddWindPointerEvent(pointerEvents, EventTriggerType.PointerExit, HandleWindPointerReleased);

        windButtonLabel = CreateWindAssistLabel(WindButtonLabelObjectName, buttonRect);
        RectTransform windLabelRect = windButtonLabel.rectTransform;
        windLabelRect.sizeDelta = new Vector2(WindButtonSize - 14f, 28f);
        windLabelRect.anchoredPosition = new Vector2(0f, -42f);
        windButtonLabel.fontSize = 10f;
        windLockedTooltip = CreateWindLockedTooltip(rootRect);

        if (barrierWarningTune != null)
        {
            barrierWarningAudioSource = GetComponent<AudioSource>();
            if (barrierWarningAudioSource == null)
                barrierWarningAudioSource = gameObject.AddComponent<AudioSource>();
            barrierWarningAudioSource.clip = barrierWarningTune;
            barrierWarningAudioSource.playOnAwake = false;
            barrierWarningAudioSource.loop = true;
            barrierWarningAudioSource.spatialBlend = 0f;
            barrierWarningAudioSource.volume = 0.45f;
        }
    }

    private void EmbedVomitButtonInsideWindButton()
    {
        if (vomitButton == null || windButton == null)
            return;

        RectTransform vomitRect = vomitButton.GetComponent<RectTransform>();
        if (vomitRect == null)
            return;

        vomitRect.SetParent(windButton.transform, false);
        vomitRect.anchorMin = QteButtonCenter;
        vomitRect.anchorMax = QteButtonCenter;
        vomitRect.pivot = QteButtonCenter;
        vomitRect.sizeDelta = new Vector2(62f, 62f);
        vomitRect.anchoredPosition = new Vector2(0f, 13f);
        vomitRect.localScale = Vector3.one;

        EventTrigger pointerEvents = vomitButton.GetComponent<EventTrigger>();
        if (pointerEvents == null)
            pointerEvents = vomitButton.gameObject.AddComponent<EventTrigger>();
        pointerEvents.triggers = new List<EventTrigger.Entry>();
        AddWindPointerEvent(pointerEvents, EventTriggerType.PointerDown, HandleWindPointerDown);
        AddWindPointerEvent(pointerEvents, EventTriggerType.PointerUp, HandleWindPointerReleased);
        AddWindPointerEvent(pointerEvents, EventTriggerType.PointerExit, HandleWindPointerReleased);

        if (vomitSleepingLabel != null)
        {
            RectTransform sleepingRect = vomitSleepingLabel.rectTransform;
            sleepingRect.anchorMin = new Vector2(0.5f, 0f);
            sleepingRect.anchorMax = new Vector2(0.5f, 0f);
            sleepingRect.pivot = new Vector2(0.5f, 1f);
            sleepingRect.anchoredPosition = new Vector2(0f, -2f);
            sleepingRect.sizeDelta = new Vector2(88f, 18f);
            vomitSleepingLabel.fontSize = 8f;
        }
    }

    private static Image CreateWindAssistImage(string objectName, RectTransform parent, Sprite sprite, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = QteButtonCenter;
        imageRect.anchorMax = QteButtonCenter;
        imageRect.pivot = QteButtonCenter;
        imageRect.sizeDelta = new Vector2(WindAssistRootSize, WindAssistRootSize);

        Image image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    private TextMeshProUGUI CreateWindAssistLabel(string labelName, RectTransform parent)
    {
        GameObject labelObject = new GameObject(labelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(parent, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = QteButtonCenter;
        labelRect.anchorMax = QteButtonCenter;
        labelRect.pivot = QteButtonCenter;
        labelRect.sizeDelta = new Vector2(WindButtonSize - 12f, 58f);
        labelRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = Resources.Load<TMP_FontAsset>(BreachWarningFontResourcePath) ?? TMP_Settings.defaultFontAsset;
        label.fontSize = 16f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }

    private TextMeshProUGUI CreateWindLockedTooltip(RectTransform parent)
    {
        TextMeshProUGUI tooltip = CreateWindAssistLabel(WindLockedTooltipObjectName, parent);
        RectTransform tooltipRect = tooltip.rectTransform;
        tooltipRect.anchoredPosition = new Vector2(0f, WindAssistRootSize * 0.5f + 14f);
        tooltipRect.sizeDelta = new Vector2(300f, 36f);
        tooltip.fontSize = 14f;
        tooltip.color = new Color(1f, 0.84f, 0.35f, 1f);
        tooltip.text = "Finish the lung repair first";
        tooltip.gameObject.SetActive(false);
        return tooltip;
    }

    private static ColorBlock CreateWindAssistButtonColors()
    {
        return new ColorBlock
        {
            normalColor = WindButtonNormalColor,
            highlightedColor = new Color(0.04f, 0.26f, 0.3f, 0.98f),
            pressedColor = new Color(0.04f, 0.42f, 0.48f, 1f),
            selectedColor = WindButtonNormalColor,
            disabledColor = WindButtonLockedColor,
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
    }

    private static void AddWindPointerEvent(EventTrigger trigger, EventTriggerType eventType, System.Action<BaseEventData> callback)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = eventType };
        entry.callback.AddListener(data => callback(data));
        trigger.triggers.Add(entry);
    }

    private void HandleWindPointerDown(BaseEventData eventData)
    {
        if (activeRespiratoryRepairs.Count == 0)
        {
            windPointerHeld = true;
            windPointerHeldDuration = 0f;
            suppressVomitClickAfterWind = false;
        }
    }

    private void HandleWindPointerReleased(BaseEventData eventData)
    {
        if (windPointerHeld && windPointerHeldDuration >= WindHoldSuppressVomitThreshold)
            suppressVomitClickAfterWind = true;
        windPointerHeld = false;
    }

    private void HandleEmbeddedVomitPressed()
    {
        if (suppressVomitClickAfterWind)
        {
            suppressVomitClickAfterWind = false;
            return;
        }

        HandleVomitPressed();
    }

    private static Sprite GetWindRingSprite()
    {
        if (windRingSprite == null)
            windRingSprite = CreateWindAssistSprite("Digestive Barrier Wind Ring", true, 0f, 100f);
        return windRingSprite;
    }

    private static Sprite GetWindButtonCircleSprite()
    {
        if (windButtonCircleSprite == null)
            windButtonCircleSprite = CreateWindAssistSprite("Digestive Barrier Wind Button", false, 0f, 100f);
        return windButtonCircleSprite;
    }

    private static Sprite CreateWindAssistSprite(string spriteName, bool ring, float startPercent, float endPercent)
    {
        const int textureSize = 128;
        const float innerRadius = 0.72f;
        const float outerRadius = 0.98f;
        Color32[] pixels = new Color32[textureSize * textureSize];
        float center = (textureSize - 1) * 0.5f;
        float radius = textureSize * 0.5f;
        float clampedStart = Mathf.Clamp01(startPercent / 100f);
        float clampedEnd = Mathf.Clamp01(endPercent / 100f);

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float normalizedX = (x - center) / radius;
                float normalizedY = (y - center) / radius;
                float radialDistance = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                float alpha = ring
                    ? Mathf.Clamp01(Mathf.Min((radialDistance - innerRadius) * 24f, (outerRadius - radialDistance) * 24f))
                    : Mathf.Clamp01((1f - radialDistance) * 24f);

                if (ring && alpha > 0f && (clampedStart > 0f || clampedEnd < 1f))
                {
                    float angle = Mathf.Repeat(Mathf.Atan2(normalizedX, normalizedY) / (Mathf.PI * 2f), 1f);
                    bool inBand = clampedStart <= clampedEnd
                        ? angle >= clampedStart && angle <= clampedEnd
                        : angle >= clampedStart || angle <= clampedEnd;
                    if (!inBand)
                        alpha = 0f;
                }

                pixels[y * textureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        texture.name = spriteName + " Texture";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), QteButtonCenter, 100f);
        sprite.name = spriteName;
        return sprite;
    }

    private void UpdateDigestiveBarrier(float gameplayDelta)
    {
        if (barrierHealth == null)
            return;

        bool respiratoryRepairActive = activeRespiratoryRepairs.Count > 0;
        if (windPointerHeld && gameplayDelta > 0f)
            windPointerHeldDuration += gameplayDelta;
        bool windRequested = windPointerHeld || (windKey != KeyCode.None && Input.GetKey(windKey));
        if (gameplayDelta > 0f)
        {
            if (windRequested && !respiratoryRepairActive)
            {
                float currentBarrier = barrierHealth.CurrentBarrier;
                barrierHealth.RestoreBarrier(Mathf.Max(0f, barrierWindRate) * gameplayDelta);
                if (barrierHealth.CurrentBarrier > currentBarrier)
                    breachGraceRemaining = -1f;
            }
            else
            {
                DifficultyStats difficultyStats = DifficultySettings.CurrentStats;
                float decayRate = difficultyStats != null ? Mathf.Max(0f, difficultyStats.digestiveBarrierDecayPerSecond) : 1.6f;
                float qteDecayScale = respiratoryRepairActive ? Mathf.Clamp01(respiratoryQteDecayMultiplier) : 1f;
                ApplyDigestiveBarrierLoss(decayRate * qteDecayScale * gameplayDelta);
            }

            if (barrierHealth.CurrentBarrier <= BreachGraceFloor)
            {
                if (breachGraceRemaining < 0f)
                    breachGraceRemaining = Mathf.Max(0f, breachGracePeriod);
                breachGraceRemaining = Mathf.Max(0f, breachGraceRemaining - gameplayDelta);
                if (breachGraceRemaining <= 0f)
                {
                    breachGraceRemaining = -1f;
                    barrierHealth.TakeDamage(BarrierMaximum);
                }
            }
            else
            {
                breachGraceRemaining = -1f;
            }
        }
    }

    private void ApplyDigestiveBarrierDamage(float requestedDamage)
    {
        DifficultyStats difficultyStats = DifficultySettings.CurrentStats;
        float damageMultiplier = difficultyStats != null ? Mathf.Max(0f, difficultyStats.barrierDamageMultiplier) : 1f;
        ApplyDigestiveBarrierLoss(Mathf.Max(0f, requestedDamage) * damageMultiplier);
    }

    private void ApplyDigestiveBarrierLoss(float requestedLoss)
    {
        if (requestedLoss <= 0f || barrierHealth == null || barrierHealth.CurrentBarrier <= BreachGraceFloor)
            return;

        float damageMultiplier = DifficultySettings.CurrentStats != null
            ? Mathf.Max(0f, DifficultySettings.CurrentStats.barrierDamageMultiplier)
            : 1f;
        if (damageMultiplier <= 0f)
            return;

        float safeLoss = Mathf.Min(requestedLoss, Mathf.Max(0f, barrierHealth.CurrentBarrier - BreachGraceFloor));
        if (safeLoss > 0f)
            barrierHealth.TakeDamage(safeLoss / damageMultiplier);
    }

    private void RefreshWindAssistUI()
    {
        float barrierValue = barrierHealth != null ? Mathf.Clamp(barrierHealth.CurrentBarrier, 0f, BarrierMaximum) : BarrierMaximum;
        float barrierPercent = barrierValue / BarrierMaximum * 100f;
        bool respiratoryRepairActive = activeRespiratoryRepairs.Count > 0;
        bool locked = respiratoryRepairActive;

        if (windProgressImage != null)
            windProgressImage.fillAmount = barrierValue / BarrierMaximum;
        if (windSweetSpotImage != null)
            windSweetSpotImage.enabled = enableSweetSpotBand;
        if (windButton != null)
            windButton.interactable = !locked;
        if (windButtonBackground != null)
            windButtonBackground.color = locked ? WindButtonLockedColor : WindButtonNormalColor;
        if (windButtonLabel != null)
        {
            windButtonLabel.text = locked ? "LOCKED" : Application.isMobilePlatform
                ? "TAP + HOLD"
                : $"CLICK + HOLD\n{windKey.ToString().ToUpperInvariant()}";
            windButtonLabel.color = locked ? new Color(0.76f, 0.78f, 0.82f, 1f) : Color.white;
        }
        if (windLockedTooltip != null)
            windLockedTooltip.gameObject.SetActive(locked);

        LayerSelectionHUD.SetDigestiveBarrierWarning(barrierPercent);
        RefreshBarrierWarningTune(barrierPercent);
    }

    private void RefreshBarrierWarningTune(float barrierPercent)
    {
        if (barrierWarningAudioSource == null)
            return;

        bool shouldPlay = barrierWarningTune != null && barrierPercent <= WarningBadgeThresholdPercent;
        if (!shouldPlay)
        {
            if (barrierWarningAudioSource.isPlaying || barrierWarningTunePaused)
                barrierWarningAudioSource.Stop();
            barrierWarningTunePaused = false;
            return;
        }

        float urgency = Mathf.Clamp01((WarningBadgeThresholdPercent - barrierPercent) / WarningBadgeThresholdPercent);
        barrierWarningAudioSource.pitch = Mathf.Lerp(1f, Mathf.Max(1f, warningTuneMaximumPitch), urgency);
        if (Time.timeScale <= 0f)
        {
            if (barrierWarningAudioSource.isPlaying)
                barrierWarningAudioSource.Pause();
            barrierWarningTunePaused = true;
            return;
        }

        if (barrierWarningTunePaused)
        {
            barrierWarningAudioSource.UnPause();
            barrierWarningTunePaused = false;
        }
        else if (!barrierWarningAudioSource.isPlaying)
        {
            barrierWarningAudioSource.Play();
        }
    }

    private void HandleRespiratoryRepairQteStateChanged(LungInfectionResponse response, bool isActive)
    {
        if (response == null)
            return;

        if (isActive)
        {
            activeRespiratoryRepairs.Add(response);
            windPointerHeld = false;
        }
        else
        {
            activeRespiratoryRepairs.Remove(response);
        }

        RefreshWindAssistUI();
    }

    private void RefreshActiveRespiratoryRepairs()
    {
        activeRespiratoryRepairs.Clear();
        LungInfectionResponse[] responses = FindObjectsByType<LungInfectionResponse>(FindObjectsSortMode.None);
        for (int index = 0; index < responses.Length; index++)
        {
            if (responses[index] != null && responses[index].IsResponseInProgress)
                activeRespiratoryRepairs.Add(responses[index]);
        }
    }

    private static TextMeshProUGUI FindOrCreateGaugeLabel(Transform parent, string labelName, string initialText, float verticalPosition, float fontSize)
    {
        Transform existingLabel = parent.Find(labelName);
        TextMeshProUGUI label = existingLabel != null ? existingLabel.GetComponent<TextMeshProUGUI>() : null;
        if (label == null)
        {
            GameObject labelObject = new GameObject(labelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            label = labelObject.GetComponent<TextMeshProUGUI>();
        }

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(200f, 38f);
        labelRect.anchoredPosition = new Vector2(0f, verticalPosition);
        label.text = initialText;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    private static Sprite LoadBarrierGaugeSprite()
    {
        if (barrierGaugeSprite != null)
            return barrierGaugeSprite;

        barrierGaugeSprite = Resources.Load<Sprite>("UI/Barrier meter");
        if (barrierGaugeSprite != null)
            return barrierGaugeSprite;

        Texture2D gaugeTexture = Resources.Load<Texture2D>("UI/Barrier meter");
        if (gaugeTexture != null)
        {
            barrierGaugeSprite = Sprite.Create(
                gaugeTexture,
                new Rect(0f, 0f, gaugeTexture.width, gaugeTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }
        else
        {
            Debug.LogWarning("Barrier meter texture could not be loaded from Resources/UI/Barrier meter.png.");
        }

        return barrierGaugeSprite;
    }

    private void RefreshBarrierGauge()
    {
        if (barrierHealth == null || barrierGaugeImage == null)
            return;

        float barrierValue = Mathf.Clamp(barrierHealth.CurrentBarrier, 0f, BarrierMaximum);
        barrierGaugeImage.fillAmount = barrierValue / BarrierMaximum;
        barrierGaugeImage.color = barrierValue >= 50f
            ? Color.cyan
            : barrierValue >= 25f ? Color.yellow : Color.red;

        if (barrierPercentLabel != null)
            barrierPercentLabel.text = $"{Mathf.RoundToInt(barrierValue)}%";
    }

    private void InitializeBreachVisuals()
    {
        Canvas canvas = GameObject.Find("/Event Handlers/HUDCanvas")?.GetComponent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return;

        if (breachOverlay == null)
        {
            Transform existingOverlay = canvas.transform.Find(BreachOverlayObjectName);
            breachOverlay = existingOverlay != null ? existingOverlay.GetComponent<Image>() : null;
            if (breachOverlay == null)
            {
                GameObject overlayObject = new GameObject(BreachOverlayObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                overlayObject.transform.SetParent(canvas.transform, false);
                RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.offsetMin = Vector2.zero;
                overlayRect.offsetMax = Vector2.zero;
                breachOverlay = overlayObject.GetComponent<Image>();
            }

            breachOverlay.sprite = LoadBreachOverlaySprite();
            breachOverlay.raycastTarget = false;
        }

        if (breachWarningText == null)
        {
            Transform existingWarning = canvas.transform.Find(BreachWarningObjectName);
            breachWarningText = existingWarning != null ? existingWarning.GetComponent<TextMeshProUGUI>() : null;
            if (breachWarningText == null)
            {
                GameObject warningObject = new GameObject(BreachWarningObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                warningObject.transform.SetParent(canvas.transform, false);
                RectTransform warningRect = warningObject.GetComponent<RectTransform>();
                warningRect.anchorMin = new Vector2(0.5f, 0.5f);
                warningRect.anchorMax = new Vector2(0.5f, 0.5f);
                warningRect.pivot = new Vector2(0.5f, 0.5f);
                warningRect.anchoredPosition = Vector2.zero;
                warningRect.sizeDelta = new Vector2(720f, 90f);
                breachWarningText = warningObject.GetComponent<TextMeshProUGUI>();
                breachWarningText.text = BreachWarningMessage;
                breachWarningText.font = TMP_Settings.defaultFontAsset;
                breachWarningText.fontSize = 38f;
                breachWarningText.fontStyle = FontStyles.Bold;
                breachWarningText.color = Color.white;
                breachWarningText.alignment = TextAlignmentOptions.Center;
                breachWarningText.raycastTarget = false;
            }
        }

        TMP_FontAsset breachWarningFont = Resources.Load<TMP_FontAsset>(BreachWarningFontResourcePath);
        if (breachWarningText != null && breachWarningFont != null)
            breachWarningText.font = breachWarningFont;

        if (breachOverlay != null)
            breachOverlay.enabled = false;
        if (breachWarningText != null)
            breachWarningText.enabled = false;
    }

    private void RefreshBreachVisuals()
    {
        bool layerVisible = IsBreachLayerVisible();
        bool isBreached = barrierHealth != null && barrierHealth.IsBreached;
        float barrierValue = barrierHealth != null ? barrierHealth.CurrentBarrier : BarrierMaximum;
        if (isBreached || barrierValue >= BarrierMaximum)
            isBarrierRepairing = false;

        bool isRepairing = isBarrierRepairing && barrierValue > 0f && barrierValue < BarrierMaximum;
        bool showBreach = layerVisible && isBreached;
        bool showRepair = layerVisible && !isBreached && isRepairing;

        if (breachOverlay != null)
        {
            breachOverlay.enabled = showBreach;
            if (showBreach)
            {
                Color overlayColor = Color.red;
                overlayColor.a = Mathf.PingPong(Time.time, 0.3f) + 0.1f;
                breachOverlay.color = overlayColor;
            }
        }

        if (breachWarningText == null)
            return;

        if (showRepair)
        {
            breachWarningText.text = BreachRepairMessage;
            breachWarningText.color = Color.yellow;
            breachWarningText.enabled = true;
        }
        else if (showBreach)
        {
            breachWarningText.text = BreachWarningMessage;
            breachWarningText.color = Color.red;
            breachWarningText.enabled = Mathf.PingPong(Time.time, BreachWarningBlinkDuration) < BreachWarningBlinkDuration * 0.5f;
        }
        else
        {
            breachWarningText.enabled = false;
        }
    }

    private static bool IsBreachLayerVisible()
    {
        GameObject ordersContainer = GameObject.Find(OrdersContainerPath);
        CanvasGroup ordersCanvasGroup = ordersContainer != null ? ordersContainer.GetComponent<CanvasGroup>() : null;
        return ordersCanvasGroup != null && ordersCanvasGroup.alpha > 0.5f;
    }

    private static Sprite LoadBreachOverlaySprite()
    {
        if (breachOverlaySprite != null)
            return breachOverlaySprite;

        const int textureSize = 512;
        Color32[] pixels = new Color32[textureSize * textureSize];
        for (int column = -1; column <= textureSize / 48 + 1; column++)
        {
            int centerX = column * 48;
            int verticalOffset = (column & 1) == 0 ? 0 : 28;
            for (int row = -1; row <= textureSize / 56 + 1; row++)
            {
                int centerY = row * 56 + verticalOffset;
                DrawHoneycombEdge(pixels, textureSize, centerX, centerY - 28, centerX + 24, centerY - 14);
                DrawHoneycombEdge(pixels, textureSize, centerX + 24, centerY - 14, centerX + 24, centerY + 14);
                DrawHoneycombEdge(pixels, textureSize, centerX + 24, centerY + 14, centerX, centerY + 28);
                DrawHoneycombEdge(pixels, textureSize, centerX, centerY + 28, centerX - 24, centerY + 14);
                DrawHoneycombEdge(pixels, textureSize, centerX - 24, centerY + 14, centerX - 24, centerY - 14);
                DrawHoneycombEdge(pixels, textureSize, centerX - 24, centerY - 14, centerX, centerY - 28);
            }
        }

        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        texture.name = "Runtime Barrier Breach Honeycomb";
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        breachOverlaySprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), 100f);
        return breachOverlaySprite;
    }

    private static void DrawHoneycombEdge(Color32[] pixels, int textureSize, int startX, int startY, int endX, int endY)
    {
        int deltaX = Mathf.Abs(endX - startX);
        int stepX = startX < endX ? 1 : -1;
        int deltaY = -Mathf.Abs(endY - startY);
        int stepY = startY < endY ? 1 : -1;
        int error = deltaX + deltaY;

        while (true)
        {
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int pixelX = startX + offsetX;
                    int pixelY = startY + offsetY;
                    if (pixelX >= 0 && pixelX < textureSize && pixelY >= 0 && pixelY < textureSize)
                        pixels[pixelY * textureSize + pixelX] = new Color32(255, 255, 255, 255);
                }
            }

            if (startX == endX && startY == endY)
                break;

            int doubledError = error * 2;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                startX += stepX;
            }
            if (doubledError <= deltaX)
            {
                error += deltaX;
                startY += stepY;
            }
        }
    }

    private void ConfigureVomitButtonIcon()
    {
        if (vomitButton == null)
            return;

        Texture2D iconTexture = Resources.Load<Texture2D>(VomitButtonIconResourcePath);
        if (iconTexture == null)
        {
            Debug.LogWarning($"DigestiveSystemController could not load Resources/{VomitButtonIconResourcePath}.png; keeping the Vomit label.", this);
            return;
        }

        if (vomitButtonIconSprite == null)
        {
            vomitButtonIconSprite = Sprite.Create(
                iconTexture,
                new Rect(0f, 0f, iconTexture.width, iconTexture.height),
                QteButtonCenter,
                100f);
            vomitButtonIconSprite.name = "Vomit Button Icon";
        }

        Image buttonImage = vomitButton.GetComponent<Image>();
        if (buttonImage == null)
        {
            Debug.LogWarning("DigestiveSystemController could not find the VomitButton Image component; keeping the Vomit label.", this);
            return;
        }

        buttonImage.sprite = vomitButtonIconSprite;
        buttonImage.type = Image.Type.Simple;
        buttonImage.preserveAspect = true;
        buttonImage.color = Color.white;
        buttonImage.raycastTarget = true;
        vomitButton.targetGraphic = buttonImage;

        ColorBlock buttonColors = vomitButton.colors;
        buttonColors.normalColor = QteButtonNormalColor;
        buttonColors.highlightedColor = QteButtonHighlightColor;
        buttonColors.selectedColor = QteButtonNormalColor;
        buttonColors.pressedColor = QteButtonPressedColor;
        buttonColors.disabledColor = QteButtonDisabledColor;
        vomitButton.colors = buttonColors;

        if (vomitLabel != null)
            vomitLabel.gameObject.SetActive(false);
    }

    private void CreateVomitSleepingLabel()
    {
        if (vomitButton == null)
            return;

        Transform existingLabel = vomitButton.transform.Find("SleepingLabel");
        if (existingLabel != null)
        {
            vomitSleepingLabel = existingLabel.GetComponent<TextMeshProUGUI>();
            return;
        }

        GameObject labelObject = new GameObject("SleepingLabel", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(vomitButton.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0f);
        labelRect.anchorMax = new Vector2(0.5f, 0f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -10f);
        labelRect.sizeDelta = new Vector2(320f, 42f);

        vomitSleepingLabel = labelObject.GetComponent<TextMeshProUGUI>();
        vomitSleepingLabel.text = "SLEEPING";
        vomitSleepingLabel.font = Resources.Load<TMP_FontAsset>("BankGothicMediumSDF") ?? TMP_Settings.defaultFontAsset;
        vomitSleepingLabel.fontSize = 22f;
        vomitSleepingLabel.fontStyle = FontStyles.Bold;
        vomitSleepingLabel.color = new Color(0.12f, 0.92f, 0.96f, 1f);
        vomitSleepingLabel.alignment = TextAlignmentOptions.Center;
        vomitSleepingLabel.textWrappingMode = TextWrappingModes.NoWrap;
        vomitSleepingLabel.raycastTarget = false;
        labelObject.SetActive(false);
    }

    private static void CenterQteButton(Button button)
    {
        if (button == null)
            return;

        RectTransform buttonRect = button.GetComponent<RectTransform>();
        if (buttonRect == null)
            return;

        buttonRect.anchorMin = QteButtonCenter;
        buttonRect.anchorMax = QteButtonCenter;
        buttonRect.pivot = QteButtonCenter;
        buttonRect.sizeDelta = QteButtonSize;
        buttonRect.anchoredPosition = new Vector2(0f, DigestiveQteVerticalOffset);
    }

    private static void CenterOrdersContainer(RectTransform panelRect)
    {
        if (panelRect == null)
            return;

        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        Image panelBackground = panelRect.GetComponent<Image>();
        if (panelBackground != null)
            panelBackground.enabled = false;
    }

    private void OnDisable()
    {
        RoutineSystem.OnRoutineActivityChanged -= HandleRoutineActivityChanged;
        RandomEventSystem.OnRandomEventTriggered -= HandleRandomEventTriggered;
        LungInfectionResponse.RepairQTEStateChanged -= HandleRespiratoryRepairQteStateChanged;
        windPointerHeld = false;
        if (barrierWarningAudioSource != null && barrierWarningAudioSource.isPlaying)
            barrierWarningAudioSource.Stop();
        barrierWarningTunePaused = false;
    }

    private void OnDestroy()
    {
        if (eatButton != null)
            eatButton.onClick.RemoveListener(HandleEatPressed);
        if (vomitButton != null)
            vomitButton.onClick.RemoveListener(HandleEmbeddedVomitPressed);
    }

    private void Update()
    {
        if (routineSystem == null)
            routineSystem = RoutineSystem.Instance;

        float gameplayDelta = GameplaySpeed.DeltaTime;
        UpdateDigestiveBarrier(gameplayDelta);
        eatCooldownRemaining = Mathf.Max(0f, eatCooldownRemaining - gameplayDelta);
        vomitCooldownRemaining = Mathf.Max(0f, vomitCooldownRemaining - gameplayDelta);
        RefreshButtonStates();
        RefreshBarrierGauge();
        RefreshBreachVisuals();
        RefreshWindAssistUI();
    }

    private void HandleRoutineActivityChanged(RoutineActivity activity)
    {
        int day = dayCounter != null ? dayCounter.CurrentDay : -1;
        int hour = dayCounter != null ? dayCounter.CurrentHour : -1;

        if (activity == RoutineActivity.EatingBreakfast ||
            activity == RoutineActivity.EatingLunch ||
            activity == RoutineActivity.EatingDinner)
        {
            if (FindScheduledMealBatch(day, hour) == null)
            {
                bool contaminated = day == lastContaminatedFoodEventDay && hour == lastContaminatedFoodEventHour;
                QueueBatch(contaminated ? contaminatedFoodPrefab : foodPrefab, true, false, day, hour);
            }
        }
        else if (activity == RoutineActivity.DrinkingWater)
        {
            if (FindScheduledWaterBatch(day, hour) == null)
            {
                bool contaminated = day == lastContaminatedWaterEventDay && hour == lastContaminatedWaterEventHour;
                QueueBatch(contaminated ? contaminatedFluidPrefab : fluidPrefab, false, true, day, hour);
            }
        }

        RefreshButtonStates();
    }

    private void HandleRandomEventTriggered(RandomEventData eventData)
    {
        if (eventData == null || IsSleeping)
            return;

        if (eventData.eventName == ExpiredFoodEventName ||
            eventData.eventName == JunkFoodEventName ||
            eventData.eventName == AccidentallyAteSpoiledFoodEventName)
        {
            HandleContaminatedFoodEvent(eventData);
        }
        else if (eventData.eventName == ContaminatedWaterEventName)
        {
            HandleContaminatedWaterEvent(eventData);
        }
    }

    private void HandleContaminatedFoodEvent(RandomEventData eventData)
    {
        ApplyDigestiveBarrierDamage(BarrierDamagePerRandomEvent);
        lastContaminatedFoodEventDay = eventData.day;
        lastContaminatedFoodEventHour = eventData.hour;
        string eventId = eventData.eventName == ExpiredFoodEventName
            ? "AteExpiredFood"
            : eventData.eventName == JunkFoodEventName
                ? "JunkFoodBinge"
                : "AccidentallyAteSpoiledFood";
        string eventKey = WellnessManager.BuildEventKey(eventId, eventData.day, eventData.hour);
        if (eventData.triggersQTE)
            QTETracker.Register(QTETracker.DigestiveLayerIndex, eventKey);

        DigestiveBatchRequest scheduledMeal = FindScheduledMealBatch(eventData.day, eventData.hour);
        if (scheduledMeal != null)
        {
            ReplaceBatchPrefab(scheduledMeal, contaminatedFoodPrefab, eventKey);
            return;
        }

        QueueBatch(contaminatedFoodPrefab, true, false, eventData.day, eventData.hour, eventKey);
    }

    private void HandleContaminatedWaterEvent(RandomEventData eventData)
    {
        ApplyDigestiveBarrierDamage(BarrierDamagePerRandomEvent);
        lastContaminatedWaterEventDay = eventData.day;
        lastContaminatedWaterEventHour = eventData.hour;
        string eventKey = WellnessManager.BuildEventKey("ContaminatedWater", eventData.day, eventData.hour);

        DigestiveBatchRequest scheduledWater = FindScheduledWaterBatch(eventData.day, eventData.hour);
        if (scheduledWater != null)
        {
            ReplaceBatchPrefab(scheduledWater, contaminatedFluidPrefab, eventKey);
            return;
        }

        QueueBatch(contaminatedFluidPrefab, false, true, eventData.day, eventData.hour, eventKey);
    }

    private static void ReplaceBatchPrefab(DigestiveBatchRequest batch, GameObject replacementPrefab, string eventKey)
    {
        batch.prefab = replacementPrefab;
        batch.eventKey = eventKey;
        foreach (GameObject spawnedItem in batch.spawnedItems)
        {
            if (spawnedItem != null)
                Destroy(spawnedItem);
        }
        batch.spawnedItems.Clear();
    }

    private void HandleEatPressed()
    {
        if (IsSleeping || IsBatchRunning || eatCooldownRemaining > 0f)
            return;

        eatCooldownRemaining = ManualCooldownSeconds;
        if (useLegacyDigestiveAssist)
            barrierHealth?.RestoreBarrier(BarrierRestorePerAction);
        QueueBatch(foodPrefab, false, false, dayCounter != null ? dayCounter.CurrentDay : -1,
            dayCounter != null ? dayCounter.CurrentHour : -1);
        RefreshButtonStates();
    }

    private void HandleVomitPressed()
    {
        if (IsSleeping || vomitCooldownRemaining > 0f)
            return;

        vomitCooldownRemaining = ManualCooldownSeconds;
        if (useLegacyDigestiveAssist)
            barrierHealth?.RestoreBarrier(BarrierRestorePerAction);
        isBarrierRepairing = barrierHealth != null && barrierHealth.CurrentBarrier > 0f && barrierHealth.CurrentBarrier < BarrierMaximum;
        RefreshBreachVisuals();
        HashSet<string> eventKeys = FindActiveContaminationEventKeys();
        GameplaySpeed.ResolveQTE(SpoiledFoodQTEId);
        StopPendingBatches();
        DestroyDigestiveItems();

        WellnessManager wellnessManager = WellnessManager.Instance;
        if (wellnessManager != null)
        {
            foreach (string eventKey in eventKeys)
            {
                wellnessManager.TryAwardEventResponse(eventKey, "DigestiveResponse", 3f, 5f);
                wellnessManager.ResolveEventQTE(eventKey);
                QTETracker.Unregister(QTETracker.DigestiveLayerIndex, eventKey);
            }
        }

        RefreshButtonStates();
    }

    private HashSet<string> FindActiveContaminationEventKeys()
    {
        HashSet<string> eventKeys = new HashSet<string>();
        if (currentBatch != null && !string.IsNullOrWhiteSpace(currentBatch.eventKey))
            eventKeys.Add(currentBatch.eventKey);

        foreach (DigestiveBatchRequest pendingBatch in pendingBatches)
        {
            if (!string.IsNullOrWhiteSpace(pendingBatch.eventKey))
                eventKeys.Add(pendingBatch.eventKey);
        }

        DigestiveFoodAgent[] activeFoodAgents = GetComponentsInChildren<DigestiveFoodAgent>(true);
        foreach (DigestiveFoodAgent foodAgent in activeFoodAgents)
        {
            if (foodAgent != null && !string.IsNullOrWhiteSpace(foodAgent.WellnessEventKey))
                eventKeys.Add(foodAgent.WellnessEventKey);
        }

        return eventKeys;
    }

    private void QueueBatch(GameObject prefab, bool isScheduledMeal, bool isScheduledWater, int day, int hour, string eventKey = null)
    {
        if (prefab == null || mouthPoint == null || !HasCompletePathway())
            return;

        pendingBatches.Enqueue(new DigestiveBatchRequest
        {
            prefab = prefab,
            isScheduledMeal = isScheduledMeal,
            isScheduledWater = isScheduledWater,
            day = day,
            hour = hour,
            eventKey = eventKey
        });

        if (batchQueueRoutine == null)
            batchQueueRoutine = StartCoroutine(ProcessBatchQueue());

        RefreshButtonStates();
    }

    private IEnumerator ProcessBatchQueue()
    {
        while (pendingBatches.Count > 0)
        {
            currentBatch = pendingBatches.Dequeue();
            for (int index = 0; index < ItemsPerBatch; index++)
            {
                if (currentBatch.prefab == null)
                    break;

                GameObject spawnedItem = SpawnDigestiveItem(currentBatch.prefab, currentBatch.eventKey);
                if (spawnedItem != null)
                    currentBatch.spawnedItems.Add(spawnedItem);

                if (index < ItemsPerBatch - 1)
                    yield return GameplaySpeed.WaitForGameplaySeconds(SpawnIntervalSeconds);
            }

            currentBatch = null;
        }

        batchQueueRoutine = null;
        RefreshButtonStates();
    }

    private GameObject SpawnDigestiveItem(GameObject prefab, string eventKey)
    {
        GameObject item = Instantiate(prefab, mouthPoint.position, mouthPoint.rotation, transform);
        NavMeshAgent navMeshAgent = item.GetComponent<NavMeshAgent>();
        float movementSpeed = navMeshAgent != null ? Mathf.Max(0.01f, navMeshAgent.speed) : DefaultTransitSpeed;
        if (navMeshAgent != null)
            navMeshAgent.enabled = false;

        DigestiveFoodAgent pathFollower = item.GetComponent<DigestiveFoodAgent>();
        if (pathFollower == null)
            pathFollower = item.AddComponent<DigestiveFoodAgent>();
        pathFollower.Initialize(pathwayWaypoints, movementSpeed, eventKey);
        return item;
    }

    private Transform[] FindDefaultPathwayWaypoints()
    {
        Transform[] foundWaypoints = new Transform[DefaultPathwayObjectPaths.Length];
        for (int index = 0; index < DefaultPathwayObjectPaths.Length; index++)
        {
            GameObject waypointObject = GameObject.Find(DefaultPathwayObjectPaths[index]);
            if (waypointObject == null)
                return Array.Empty<Transform>();
            foundWaypoints[index] = waypointObject.transform;
        }

        return foundWaypoints;
    }

    private bool HasCompletePathway()
    {
        if (pathwayWaypoints == null || pathwayWaypoints.Length != DefaultPathwayObjectPaths.Length)
            return false;

        for (int index = 0; index < pathwayWaypoints.Length; index++)
        {
            if (pathwayWaypoints[index] == null)
                return false;
        }

        return anusPoint == null || pathwayWaypoints[pathwayWaypoints.Length - 1] == anusPoint;
    }

    private DigestiveBatchRequest FindScheduledMealBatch(int day, int hour)
    {
        return FindScheduledRoutineBatch(day, hour, true);
    }

    private DigestiveBatchRequest FindScheduledWaterBatch(int day, int hour)
    {
        return FindScheduledRoutineBatch(day, hour, false);
    }

    private DigestiveBatchRequest FindScheduledRoutineBatch(int day, int hour, bool isMeal)
    {
        if (currentBatch != null && MatchesScheduledRoutine(currentBatch, day, hour, isMeal))
            return currentBatch;

        foreach (DigestiveBatchRequest pendingBatch in pendingBatches)
        {
            if (MatchesScheduledRoutine(pendingBatch, day, hour, isMeal))
                return pendingBatch;
        }

        return null;
    }

    private static bool MatchesScheduledRoutine(DigestiveBatchRequest batch, int day, int hour, bool isMeal)
    {
        return (isMeal ? batch.isScheduledMeal : batch.isScheduledWater) && batch.day == day && batch.hour == hour;
    }

    private static void DestroySpawnedItems(DigestiveBatchRequest batch)
    {
        foreach (GameObject spawnedItem in batch.spawnedItems)
        {
            if (spawnedItem != null)
                Destroy(spawnedItem);
        }
        batch.spawnedItems.Clear();
    }

    private void StopPendingBatches()
    {
        pendingBatches.Clear();
        currentBatch = null;
        if (batchQueueRoutine != null)
        {
            StopCoroutine(batchQueueRoutine);
            batchQueueRoutine = null;
        }
    }

    private void DestroyDigestiveItems()
    {
        Transform[] digestiveChildren = GetComponentsInChildren<Transform>(true);
        foreach (Transform candidate in digestiveChildren)
        {
            if (candidate == transform)
                continue;

            bool hasPathFollower = candidate.GetComponent<DigestiveFoodAgent>() != null;
            bool matchesPrefab = HasPrefabName(candidate.name, foodPrefab) ||
                                 HasPrefabName(candidate.name, contaminatedFoodPrefab) ||
                                 HasPrefabName(candidate.name, fluidPrefab) ||
                                 HasPrefabName(candidate.name, contaminatedFluidPrefab);
            if (hasPathFollower || matchesPrefab)
                Destroy(candidate.gameObject);
        }
    }

    private static bool HasPrefabName(string objectName, GameObject prefab)
    {
        if (prefab == null)
            return false;

        return objectName == prefab.name ||
               objectName == prefab.name + "(Clone)" ||
               objectName.StartsWith(prefab.name + " (", StringComparison.Ordinal);
    }

    private void RefreshButtonStates()
    {
        bool sleeping = IsSleeping;
        float eatCooldownRemaining = this.eatCooldownRemaining;
        float vomitCooldownRemaining = this.vomitCooldownRemaining;

        if (eatButton != null)
        {
            eatButton.interactable = !sleeping && !IsBatchRunning && eatCooldownRemaining <= 0f;
            if (eatLabel != null)
            {
                eatLabel.text = sleeping ? "Eat (Sleeping)" :
                    eatCooldownRemaining > 0f ? $"Eat ({Mathf.CeilToInt(eatCooldownRemaining)}s)" :
                    IsBatchRunning ? "Eating..." : "Eat";
            }
        }

        if (vomitButton != null)
        {
            vomitButton.interactable = !sleeping && vomitCooldownRemaining <= 0f;
            if (vomitLabel != null)
            {
                vomitLabel.text = sleeping ? "Vomit (Sleeping)" :
                    vomitCooldownRemaining > 0f ? $"Vomit ({Mathf.CeilToInt(vomitCooldownRemaining)}s)" : "Vomit";
            }
        }

        if (vomitSleepingLabel != null)
            vomitSleepingLabel.gameObject.SetActive(sleeping);
    }
}
