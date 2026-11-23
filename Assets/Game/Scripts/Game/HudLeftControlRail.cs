using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Provides the left-side gameplay icon rail and animated snapshot/console panels.</summary>
public sealed class HudLeftControlRail : MonoBehaviour
{
    private const string LayerIconResourcePath = "UI/Layer selection icon";
    private const string SnapshotIconResourcePath = "UI/Human snapshot icon button";
    private const string ConsoleIconResourcePath = "UI/Console icon";
    private const string LayerIconObjectName = "LayerSelectionIcon";
    private const string SnapshotButtonObjectName = "HumanSnapshotIconButton";
    private const string ConsoleButtonObjectName = "ConsoleLogIconButton";
    private const string SnapshotContainerResourcePath = "UI/Human snapshot container";
    private const string SnapshotCloseButtonObjectName = "HumanSnapshotCloseButton";
    private const float SnapshotWidth = 380f;
    private const float SnapshotHeight = 534f;
    private const float IconButtonSize = 72f;
    private const float IconSize = 62f;
    private const float RailTopOffset = 100f;
    private const float RailItemSpacing = 116f;
    private const float SnapshotTopOffset = 84f;
    private const float PanelTransitionDuration = 0.28f;
    private const float RowsWidth = 300f;
    private const float LayerRowHeight = 40f;
    private const float LayerRowGap = 6f;
    private static readonly Dictionary<string, Sprite> LoadedIconSprites = new Dictionary<string, Sprite>();
    private static Sprite closeArrowSprite;

    private Canvas hudCanvas;
    private RectTransform canvasRect;
    private RectTransform snapshotRect;
    private RectTransform consoleRect;
    private RectTransform dayPanelRect;
    private RectTransform controlsRect;
    private RectTransform layerToggleRect;
    private RectTransform layerRowsRect;
    private CanvasGroup snapshotCanvasGroup;
    private CanvasGroup consoleCanvasGroup;
    private CanvasGroup closeButtonCanvasGroup;
    private RectTransform closeButtonRect;
    private Button closeButton;
    private Button snapshotIconButton;
    private Button consoleIconButton;
    private Button layerSelectionButton;
    private LayerSelectionDropdownController layerDropdownController;
    private bool panelsAvailable;
    private bool snapshotOpen;
    private bool consoleOpen;
    private bool snapshotTransitioning;
    private bool consoleTransitioning;
    private float snapshotTransitionElapsed;
    private float consoleTransitionElapsed;
    private float snapshotTransitionStartAlpha;
    private float snapshotTransitionTargetAlpha;
    private float consoleTransitionStartAlpha;
    private float consoleTransitionTargetAlpha;
    private Vector2 snapshotTransitionStartPosition;
    private Vector2 snapshotTransitionTargetPosition;
    private Vector2 snapshotOpenPosition;
    private Vector2 snapshotClosedPosition;
    private float snapshotScale = 1f;
    private float previousCanvasWidth = -1f;
    private float previousCanvasHeight = -1f;

    /// <summary>Initializes the icon rail around the existing gameplay HUD panels.</summary>
    public void Initialize(Canvas canvas, RectTransform snapshotPanel, RectTransform consolePanel,
        RectTransform dayPanel, RectTransform controlsRoot, RectTransform layerToggle, RectTransform layerRows)
    {
        hudCanvas = canvas;
        canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        snapshotRect = snapshotPanel;
        consoleRect = consolePanel;
        dayPanelRect = dayPanel;
        controlsRect = controlsRoot;
        layerToggleRect = layerToggle;
        layerRowsRect = layerRows;

        if (hudCanvas == null || canvasRect == null || snapshotRect == null || consoleRect == null ||
            controlsRect == null || layerToggleRect == null || layerRowsRect == null)
        {
            Debug.LogWarning("The left HUD control rail could not find all required HUD objects.");
            enabled = false;
            return;
        }

        snapshotCanvasGroup = GetOrAddCanvasGroup(snapshotRect.gameObject);
        consoleCanvasGroup = GetOrAddCanvasGroup(consoleRect.gameObject);
        Image snapshotBackground = snapshotRect.GetComponent<Image>();
        if (snapshotBackground != null)
        {
            snapshotBackground.sprite = LoadIconSprite(SnapshotContainerResourcePath);
            snapshotBackground.color = Color.white;
            snapshotBackground.preserveAspect = false;
            snapshotBackground.raycastTarget = false;
            if (snapshotBackground.sprite == null)
                Debug.LogWarning("Human snapshot container resource was not found: " + SnapshotContainerResourcePath);
        }

        Transform existingCloseButton = canvasRect.Find("HumanSnapshotCloseButton");
        if (existingCloseButton != null)
            Destroy(existingCloseButton.gameObject);

        snapshotRect.gameObject.SetActive(true);
        consoleRect.gameObject.SetActive(true);

        ConfigureLayerIcon();
        layerSelectionButton = layerToggleRect.GetComponent<Button>();
        LayerSelectionDropdownController nextLayerDropdown = layerToggleRect.GetComponent<LayerSelectionDropdownController>();
        if (layerDropdownController != null)
            layerDropdownController.ExpandedStateChanged -= HandleLayerDropdownExpandedStateChanged;
        layerDropdownController = nextLayerDropdown;
        if (layerDropdownController != null)
            layerDropdownController.ExpandedStateChanged += HandleLayerDropdownExpandedStateChanged;

        snapshotIconButton = CreateIconButton(SnapshotButtonObjectName, SnapshotIconResourcePath, ToggleSnapshot);
        consoleIconButton = CreateIconButton(ConsoleButtonObjectName, ConsoleIconResourcePath, ToggleConsole);
        AddHoverTooltip(layerToggleRect.gameObject, "Layer Selection");
        AddHoverTooltip(snapshotIconButton.gameObject, "Human Snapshot");
        AddHoverTooltip(consoleIconButton.gameObject, "Console");

        snapshotRect.SetAsLastSibling();
        panelsAvailable = false;
        snapshotOpen = false;
        consoleOpen = false;
        ApplyLayout(true);
        SetSnapshotVisibilityImmediately(false);
        SetConsoleVisibilityImmediately(false);
        RefreshControlAvailability();
    }
    private void OnDestroy()
    {
        if (layerDropdownController != null)
            layerDropdownController.ExpandedStateChanged -= HandleLayerDropdownExpandedStateChanged;
    }

    private void HandleLayerDropdownExpandedStateChanged(bool isExpanded)
    {
        RefreshControlAvailability();
    }

    private bool IsLayerDropdownExpanded => layerDropdownController != null && layerDropdownController.IsExpanded;

    private void RefreshControlAvailability()
    {
        if (snapshotIconButton == null || consoleIconButton == null || layerSelectionButton == null)
            return;

        bool snapshotOwnsInput = snapshotOpen || snapshotTransitioning;
        bool consoleOwnsInput = consoleOpen || consoleTransitioning;
        bool layerSelectorOwnsInput = IsLayerDropdownExpanded;
        if (snapshotOwnsInput)
        {
            snapshotIconButton.interactable = true;
            consoleIconButton.interactable = false;
            layerSelectionButton.interactable = false;
            return;
        }

        if (consoleOwnsInput)
        {
            snapshotIconButton.interactable = false;
            consoleIconButton.interactable = true;
            layerSelectionButton.interactable = false;
            return;
        }

        if (layerSelectorOwnsInput)
        {
            snapshotIconButton.interactable = false;
            consoleIconButton.interactable = false;
            layerSelectionButton.interactable = true;
            return;
        }

        snapshotIconButton.interactable = panelsAvailable;
        consoleIconButton.interactable = panelsAvailable;
        layerSelectionButton.interactable = true;
    }



    /// <summary>Enables or disables panel access for the currently selected body-system layer.</summary>
    public void SetPanelsAvailable(bool isAvailable)
    {
        panelsAvailable = isAvailable;
        if (!isAvailable)
        {
            BeginSnapshotTransition(false);
            BeginConsoleTransition(false);
        }
        RefreshControlAvailability();
    }

    private void LateUpdate()
    {
        if (canvasRect == null)
            return;

        Rect canvasBounds = canvasRect.rect;
        if (Mathf.Abs(canvasBounds.width - previousCanvasWidth) > 1f ||
            Mathf.Abs(canvasBounds.height - previousCanvasHeight) > 1f)
        {
            ApplyLayout(false);
        }

        UpdateSnapshotTransition();
        UpdateConsoleTransition();
        UpdateCloseButtonPosition();
        RefreshControlAvailability();
    }

    private void ConfigureLayerIcon()
    {
        controlsRect.anchorMin = new Vector2(0f, 1f);
        controlsRect.anchorMax = new Vector2(0f, 1f);
        controlsRect.pivot = new Vector2(0f, 1f);
        controlsRect.localScale = Vector3.one;

        layerToggleRect.anchorMin = new Vector2(0f, 1f);
        layerToggleRect.anchorMax = new Vector2(0f, 1f);
        layerToggleRect.pivot = new Vector2(0f, 1f);
        layerToggleRect.anchoredPosition = Vector2.zero;
        layerToggleRect.sizeDelta = new Vector2(IconButtonSize, IconButtonSize);

        Transform existingIcon = layerToggleRect.Find(LayerIconObjectName);
        if (existingIcon != null)
            Destroy(existingIcon.gameObject);

        GameObject iconObject = new GameObject(LayerIconObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.transform.SetParent(layerToggleRect, false);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(IconSize, IconSize);
        Image iconImage = iconObject.GetComponent<Image>();
        iconImage.sprite = LoadIconSprite(LayerIconResourcePath);
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        if (iconImage.sprite == null)
            Debug.LogWarning("Layer selection icon resource was not found: " + LayerIconResourcePath);

        Transform label = layerToggleRect.Find("LayerToggleLabel");
        if (label != null)
            label.gameObject.SetActive(false);

        CanvasGroup rowsGroup = layerRowsRect.GetComponent<CanvasGroup>();
        if (rowsGroup != null)
        {
            rowsGroup.alpha = 0f;
            rowsGroup.interactable = false;
            rowsGroup.blocksRaycasts = false;
        }

        layerRowsRect.anchorMin = new Vector2(0f, 1f);
        layerRowsRect.anchorMax = new Vector2(0f, 1f);
        layerRowsRect.pivot = new Vector2(0f, 1f);
        layerRowsRect.anchoredPosition = new Vector2(IconButtonSize + 10f, 0f);
        ArrangeLayerRows(Mathf.Min(RowsWidth, Mathf.Max(180f, canvasRect.rect.width - 180f)));
    }

    private Button CreateIconButton(string objectName, string resourcePath, UnityEngine.Events.UnityAction onClick)
    {
        Transform existingButton = canvasRect.Find(objectName);
        if (existingButton != null)
            Destroy(existingButton.gameObject);

        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(canvasRect, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.sizeDelta = new Vector2(IconButtonSize, IconButtonSize);

        Image iconImage = buttonObject.GetComponent<Image>();
        iconImage.sprite = LoadIconSprite(resourcePath);
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = true;
        if (iconImage.sprite == null)
            Debug.LogWarning("HUD icon resource was not found: " + resourcePath);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = iconImage;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = CreateIconButtonColors();
        button.onClick.AddListener(onClick);
        return button;
    }

    private void AddHoverTooltip(GameObject targetObject, string label)
    {
        if (targetObject == null)
            return;

        HudHoverTooltip hoverTooltip = targetObject.GetComponent<HudHoverTooltip>();
        if (hoverTooltip == null)
            hoverTooltip = targetObject.AddComponent<HudHoverTooltip>();
        hoverTooltip.Initialize(canvasRect, targetObject.transform as RectTransform, label);
    }

    private Button CreateCloseButton()
    {
        Transform existingButton = canvasRect.Find(SnapshotCloseButtonObjectName);
        if (existingButton != null)
            Destroy(existingButton.gameObject);

        GameObject buttonObject = new GameObject(SnapshotCloseButtonObjectName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(CanvasGroup));
        buttonObject.transform.SetParent(canvasRect, false);
        closeButtonRect = buttonObject.GetComponent<RectTransform>();
        closeButtonRect.anchorMin = new Vector2(0f, 1f);
        closeButtonRect.anchorMax = new Vector2(0f, 1f);
        closeButtonRect.pivot = new Vector2(0f, 1f);
        closeButtonRect.sizeDelta = new Vector2(72f, 142f);

        Image hitTarget = buttonObject.GetComponent<Image>();
        hitTarget.color = new Color(1f, 1f, 1f, 0f);
        hitTarget.raycastTarget = true;
        closeButton = buttonObject.GetComponent<Button>();
        closeButton.targetGraphic = hitTarget;
        closeButton.transition = Selectable.Transition.None;
        closeButton.onClick.AddListener(() => BeginSnapshotTransition(false));
        closeButtonCanvasGroup = buttonObject.GetComponent<CanvasGroup>();
        closeButtonCanvasGroup.alpha = 0f;
        closeButtonCanvasGroup.interactable = false;
        closeButtonCanvasGroup.blocksRaycasts = false;

        GameObject arrowObject = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        arrowObject.transform.SetParent(buttonObject.transform, false);
        RectTransform arrowRect = arrowObject.GetComponent<RectTransform>();
        arrowRect.anchorMin = Vector2.zero;
        arrowRect.anchorMax = Vector2.one;
        arrowRect.offsetMin = new Vector2(14f, 18f);
        arrowRect.offsetMax = new Vector2(-14f, -18f);
        Image arrow = arrowObject.GetComponent<Image>();
        arrow.sprite = GetCloseArrowSprite();
        arrow.color = Color.white;
        arrow.preserveAspect = true;
        arrow.raycastTarget = false;
        buttonObject.SetActive(false);
        return closeButton;
    }

    private void ApplyLayout(bool initializePositions)
    {
        Rect bounds = canvasRect.rect;
        previousCanvasWidth = bounds.width;
        previousCanvasHeight = bounds.height;
        float leftMargin = Mathf.Clamp(bounds.width * 0.018f, 28f, 40f);
        float dayMargin = Mathf.Clamp(Mathf.Min(bounds.width, bounds.height) * 0.025f, 14f, 28f);
        float topOffset = Mathf.Clamp(bounds.height * 0.078f, 48f, 84f);
        float railTop = Mathf.Clamp(bounds.height * 0.092f, 56f, RailTopOffset);
        snapshotScale = Mathf.Min(1f, Mathf.Max(0.55f, (bounds.height - topOffset - 40f) / SnapshotHeight));
        snapshotOpenPosition = new Vector2(leftMargin + IconButtonSize + 18f, -topOffset);
        snapshotClosedPosition = new Vector2(-(SnapshotWidth * snapshotScale + 24f), -topOffset);

        if (dayPanelRect != null)
        {
            dayPanelRect.anchorMin = new Vector2(0f, 1f);
            dayPanelRect.anchorMax = new Vector2(0f, 1f);
            dayPanelRect.pivot = new Vector2(0f, 1f);
            dayPanelRect.anchoredPosition = new Vector2(dayMargin, -dayMargin);
            dayPanelRect.sizeDelta = new Vector2(204f, 64f);
            dayPanelRect.localScale = Vector3.one;
        }

        controlsRect.anchoredPosition = new Vector2(leftMargin, -railTop);
        controlsRect.sizeDelta = new Vector2(IconButtonSize, IconButtonSize);
        layerToggleRect.sizeDelta = new Vector2(IconButtonSize, IconButtonSize);
        layerRowsRect.anchoredPosition = new Vector2(IconButtonSize + 10f, 0f);
        ArrangeLayerRows(Mathf.Min(RowsWidth, Mathf.Max(180f, bounds.width - leftMargin - IconButtonSize - 32f)));

        PositionRailButton(snapshotIconButton, leftMargin, railTop + RailItemSpacing);
        PositionRailButton(consoleIconButton, leftMargin, railTop + RailItemSpacing * 2f);

        snapshotRect.anchorMin = new Vector2(0f, 1f);
        snapshotRect.anchorMax = new Vector2(0f, 1f);
        snapshotRect.pivot = new Vector2(0f, 1f);
        snapshotRect.sizeDelta = new Vector2(SnapshotWidth, SnapshotHeight);
        snapshotRect.localScale = new Vector3(snapshotScale, snapshotScale, 1f);
        snapshotRect.anchoredPosition = initializePositions
            ? snapshotClosedPosition
            : snapshotOpen ? snapshotOpenPosition : snapshotClosedPosition;

        if (consoleRect != null)
        {
            float consoleWidth = Mathf.Min(bounds.width * 0.75f, bounds.width - leftMargin * 2f);
            float consoleHeight = Mathf.Min(bounds.height * 0.65f, bounds.height - topOffset * 2f);
            consoleRect.anchorMin = Vector2.zero;
            consoleRect.anchorMax = Vector2.zero;
            consoleRect.pivot = Vector2.zero;
            consoleRect.anchoredPosition = new Vector2(bounds.width * 0.059f, bounds.height * 0.257f);
            consoleRect.sizeDelta = new Vector2(consoleWidth, consoleHeight);
            consoleRect.localScale = Vector3.one;
        }

        UpdateCloseButtonPosition();
    }

    private void ArrangeLayerRows(float width)
    {
        if (layerRowsRect == null)
            return;

        layerRowsRect.sizeDelta = new Vector2(width, 4f * LayerRowHeight + 3f * LayerRowGap);
        for (int index = 0; index < layerRowsRect.childCount; index++)
        {
            RectTransform rowRect = layerRowsRect.GetChild(index) as RectTransform;
            if (rowRect == null)
                continue;

            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -index * (LayerRowHeight + LayerRowGap));
            rowRect.sizeDelta = new Vector2(width, LayerRowHeight);
        }
    }

    private static void PositionRailButton(Button button, float left, float top)
    {
        if (button == null)
            return;

        RectTransform buttonRect = button.transform as RectTransform;
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = new Vector2(left, -top);
        buttonRect.sizeDelta = new Vector2(IconButtonSize, IconButtonSize);
    }

    private void ToggleSnapshot()
    {
        if (snapshotOpen || snapshotTransitioning)
        {
            BeginSnapshotTransition(!snapshotOpen);
            return;
        }

        if (panelsAvailable && !consoleOpen && !consoleTransitioning && !IsLayerDropdownExpanded)
            BeginSnapshotTransition(true);
    }

    private void ToggleConsole()
    {
        if (consoleOpen || consoleTransitioning)
        {
            BeginConsoleTransition(!consoleOpen);
            return;
        }

        if (panelsAvailable && !snapshotOpen && !snapshotTransitioning && !IsLayerDropdownExpanded)
            BeginConsoleTransition(true);
    }

    private void BeginSnapshotTransition(bool open)
    {
        if (!open && !snapshotOpen && !snapshotTransitioning)
            return;
        if (open && !panelsAvailable)
            return;

        snapshotOpen = open;
        snapshotTransitionStartPosition = snapshotRect.anchoredPosition;
        snapshotTransitionTargetPosition = open ? snapshotOpenPosition : snapshotClosedPosition;
        snapshotTransitionStartAlpha = snapshotCanvasGroup.alpha;
        snapshotTransitionTargetAlpha = open ? 1f : 0f;
        snapshotTransitionElapsed = 0f;
        snapshotTransitioning = true;
        RefreshControlAvailability();
        snapshotRect.gameObject.SetActive(true);
        snapshotCanvasGroup.interactable = false;
        snapshotCanvasGroup.blocksRaycasts = false;
        if (closeButton != null && closeButtonCanvasGroup != null)
        {
            closeButton.gameObject.SetActive(open || snapshotTransitionStartAlpha > 0f);
            closeButtonCanvasGroup.alpha = snapshotTransitionStartAlpha;
            closeButtonCanvasGroup.interactable = false;
            closeButtonCanvasGroup.blocksRaycasts = false;
        }
        UpdateCloseButtonPosition();
    }

    private void BeginConsoleTransition(bool open)
    {
        if (!open && !consoleOpen && !consoleTransitioning)
            return;
        if (open && !panelsAvailable)
            return;

        consoleOpen = open;
        consoleTransitionStartAlpha = consoleCanvasGroup.alpha;
        consoleTransitionTargetAlpha = open ? 1f : 0f;
        consoleTransitionElapsed = 0f;
        consoleTransitioning = true;
        RefreshControlAvailability();
        consoleRect.gameObject.SetActive(true);
        consoleCanvasGroup.interactable = false;
        consoleCanvasGroup.blocksRaycasts = false;
    }

    private void UpdateSnapshotTransition()
    {
        if (!snapshotTransitioning)
            return;

        snapshotTransitionElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(snapshotTransitionElapsed / PanelTransitionDuration);
        float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
        snapshotRect.anchoredPosition = Vector2.Lerp(snapshotTransitionStartPosition, snapshotTransitionTargetPosition, easedProgress);
        snapshotCanvasGroup.alpha = Mathf.Lerp(snapshotTransitionStartAlpha, snapshotTransitionTargetAlpha, easedProgress);
        if (closeButtonCanvasGroup != null)
            closeButtonCanvasGroup.alpha = snapshotCanvasGroup.alpha;
        if (progress < 1f)
            return;

        snapshotTransitioning = false;
        snapshotCanvasGroup.interactable = snapshotOpen;
        snapshotCanvasGroup.blocksRaycasts = snapshotOpen;
        if (closeButtonCanvasGroup != null)
        {
            closeButtonCanvasGroup.interactable = snapshotOpen;
            closeButtonCanvasGroup.blocksRaycasts = snapshotOpen;
        }
        if (!snapshotOpen && closeButton != null)
            closeButton.gameObject.SetActive(false);
        RefreshControlAvailability();
    }

    private void UpdateConsoleTransition()
    {
        if (!consoleTransitioning)
            return;

        consoleTransitionElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(consoleTransitionElapsed / PanelTransitionDuration);
        float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
        consoleCanvasGroup.alpha = Mathf.Lerp(consoleTransitionStartAlpha, consoleTransitionTargetAlpha, easedProgress);
        if (progress < 1f)
            return;

        consoleTransitioning = false;
        consoleCanvasGroup.interactable = consoleOpen;
        consoleCanvasGroup.blocksRaycasts = consoleOpen;
        RefreshControlAvailability();
    }

    private void SetSnapshotVisibilityImmediately(bool isVisible)
    {
        snapshotOpen = isVisible;
        snapshotTransitioning = false;
        snapshotRect.anchoredPosition = isVisible ? snapshotOpenPosition : snapshotClosedPosition;
        snapshotCanvasGroup.alpha = isVisible ? 1f : 0f;
        snapshotCanvasGroup.interactable = isVisible;
        snapshotCanvasGroup.blocksRaycasts = isVisible;
        if (closeButton != null && closeButtonCanvasGroup != null)
        {
            closeButton.gameObject.SetActive(false);
            closeButtonCanvasGroup.alpha = 0f;
            closeButtonCanvasGroup.interactable = false;
            closeButtonCanvasGroup.blocksRaycasts = false;
        }
        UpdateCloseButtonPosition();
    }

    private void SetConsoleVisibilityImmediately(bool isVisible)
    {
        consoleOpen = isVisible;
        consoleTransitioning = false;
        consoleCanvasGroup.alpha = isVisible ? 1f : 0f;
        consoleCanvasGroup.interactable = isVisible;
        consoleCanvasGroup.blocksRaycasts = isVisible;
    }

    private void UpdateCloseButtonPosition()
    {
        if (closeButtonRect == null || snapshotRect == null)
            return;

        closeButtonRect.anchoredPosition = new Vector2(
            snapshotRect.anchoredPosition.x + SnapshotWidth * snapshotScale + 12f,
            snapshotRect.anchoredPosition.y - 2f);
    }

    private static Sprite LoadIconSprite(string resourcePath)
    {
        if (LoadedIconSprites.TryGetValue(resourcePath, out Sprite cachedSprite) && cachedSprite != null)
            return cachedSprite;

        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite == null)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture != null)
                sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        if (sprite != null)
            LoadedIconSprites[resourcePath] = sprite;
        return sprite;
    }

    private static Sprite GetCloseArrowSprite()
    {
        if (closeArrowSprite != null)
            return closeArrowSprite;

        const int textureWidth = 40;
        const int textureHeight = 80;
        Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
        {
            name = "RuntimeSnapshotCloseArrowTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[textureWidth * textureHeight];
        float verticalCenter = (textureHeight - 1) * 0.5f;
        for (int y = 0; y < textureHeight; y++)
        {
            float distanceFromCenter = Mathf.Abs(y - verticalCenter) / verticalCenter;
            int firstFilledPixel = Mathf.RoundToInt((textureWidth - 1) * distanceFromCenter);
            for (int x = firstFilledPixel; x < textureWidth; x++)
                pixels[y * textureWidth + x] = new Color32(255, 255, 255, 255);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        closeArrowSprite = Sprite.Create(texture, new Rect(0f, 0f, textureWidth, textureHeight),
            new Vector2(0.5f, 0.5f), 100f);
        closeArrowSprite.name = "RuntimeSnapshotCloseArrow";
        closeArrowSprite.hideFlags = HideFlags.HideAndDontSave;
        return closeArrowSprite;
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
            group = target.AddComponent<CanvasGroup>();
        return group;
    }

    private static ColorBlock CreateIconButtonColors()
    {
        return new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.65f, 1f, 1f, 1f),
            pressedColor = new Color(0.2f, 0.9f, 0.95f, 1f),
            selectedColor = Color.white,
            disabledColor = new Color(1f, 1f, 1f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.12f
        };
    }
}


/// <summary>Displays a compact label while a HUD control is hovered.</summary>
internal sealed class HudHoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float TooltipWidth = 184f;
    private const float TooltipHeight = 40f;
    private const float TooltipGap = 10f;
    private const float CanvasEdgePadding = 8f;
    private static readonly Color TooltipBackgroundColor = new Color(0.012f, 0.028f, 0.038f, 0.98f);
    private static readonly Color TooltipTextColor = new Color(0.92f, 0.96f, 0.97f, 1f);

    private RectTransform canvasRect;
    private RectTransform targetRect;
    private RectTransform tooltipRect;

    internal void Initialize(RectTransform targetCanvas, RectTransform target, string label)
    {
        canvasRect = targetCanvas;
        targetRect = target;
        if (canvasRect == null || targetRect == null)
            return;

        if (tooltipRect != null)
            Destroy(tooltipRect.gameObject);

        GameObject tooltipObject = new GameObject("HudHoverTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        tooltipObject.transform.SetParent(canvasRect, false);
        tooltipRect = tooltipObject.GetComponent<RectTransform>();
        tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
        tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRect.pivot = new Vector2(0f, 0.5f);
        tooltipRect.sizeDelta = new Vector2(TooltipWidth, TooltipHeight);

        Image background = tooltipObject.GetComponent<Image>();
        background.color = TooltipBackgroundColor;
        background.raycastTarget = false;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(tooltipObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 4f);
        labelRect.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 13f;
        text.color = TooltipTextColor;
        text.alignment = TextAlignmentOptions.Center;
        text.text = label;
        text.raycastTarget = false;
        tooltipObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (canvasRect == null || targetRect == null || tooltipRect == null)
            return;

        Rect bounds = canvasRect.rect;
        float width = Mathf.Min(TooltipWidth, Mathf.Max(80f, bounds.width - CanvasEdgePadding * 2f));
        Vector3[] corners = new Vector3[4];
        targetRect.GetWorldCorners(corners);
        Vector2 rightCenter = canvasRect.InverseTransformPoint((corners[2] + corners[3]) * 0.5f);
        Vector2 leftCenter = canvasRect.InverseTransformPoint((corners[0] + corners[1]) * 0.5f);
        float rightSpace = bounds.xMax - rightCenter.x;
        float leftSpace = leftCenter.x - bounds.xMin;
        bool placeRight = rightSpace >= width + TooltipGap || rightSpace >= leftSpace;

        tooltipRect.pivot = new Vector2(placeRight ? 0f : 1f, 0.5f);
        tooltipRect.sizeDelta = new Vector2(width, TooltipHeight);
        float x = placeRight ? rightCenter.x + TooltipGap : leftCenter.x - TooltipGap;
        x = placeRight
            ? Mathf.Clamp(x, bounds.xMin + CanvasEdgePadding, bounds.xMax - width - CanvasEdgePadding)
            : Mathf.Clamp(x, bounds.xMin + width + CanvasEdgePadding, bounds.xMax - CanvasEdgePadding);
        float y = Mathf.Clamp(rightCenter.y,
            bounds.yMin + TooltipHeight * 0.5f + CanvasEdgePadding,
            bounds.yMax - TooltipHeight * 0.5f - CanvasEdgePadding);
        tooltipRect.anchoredPosition = new Vector2(x, y);
        tooltipRect.gameObject.SetActive(true);
        tooltipRect.SetAsLastSibling();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipRect != null)
            tooltipRect.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (tooltipRect != null)
            Destroy(tooltipRect.gameObject);
    }
}

