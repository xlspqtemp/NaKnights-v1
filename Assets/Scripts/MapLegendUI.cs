using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds the gameplay HUD map-legend button and categorized icon guide.</summary>
public sealed class MapLegendUI : MonoBehaviour
{
    private const string MapLegendIconResourcePath = "UI/Map legend icon";
    private const string BuiltInFontName = "LegacyRuntime.ttf";
    private const float ToolbarIconGap = 8f;
    private const float PopupWidth = 1080f;
    private const float PopupHeight = 680f;
    private const float IconCellWidth = 184f;
    private const float IconCellHeight = 150f;
    private const float CellSpacing = 8f;
    private const int GridColumnCount = 5;
    private const float EntryIconSize = 72f;

    private static readonly Color AccentColor = new Color(0.12f, 0.82f, 0.86f, 1f);
    private static readonly Color PanelColor = new Color(0.008f, 0.025f, 0.035f, 0.97f);
    private static readonly Color CellColor = new Color(0.025f, 0.075f, 0.09f, 0.98f);
    private static readonly Color TabColor = new Color(0.025f, 0.075f, 0.09f, 0.98f);
    private static readonly Color SelectedTabColor = new Color(0.035f, 0.34f, 0.38f, 1f);

    private static readonly string[] TabNames = { "Units", "Pathogens", "Perks", "Random events", "Barriers" };

    private static readonly LegendEntry[][] EntriesByTab =
    {
        new[]
        {
            new LegendEntry("Neutrophil", "UI/WBC HEADSHOT ICON"),
            new LegendEntry("Macrophage", "UI/Macrophage Headshot icon"),
            new LegendEntry("T Cell", "UI/Killet t cell headshot icon"),
            new LegendEntry("B Cell", "UI/B cell headshot icon"),
            new LegendEntry("Eosinophil", "UI/WBC HEADSHOT ICON", new Color(1f, 0.38f, 0.2f, 1f)),
            new LegendEntry("Basophil", "UI/WBC HEADSHOT ICON", new Color(0.58f, 0.42f, 1f, 1f))
        },
        new[]
        {
            new LegendEntry("Bacteria", "UI/Red bacteria icon"),
            new LegendEntry("Virus", "UI/Blue virus icon"),
            new LegendEntry("Fungus", "UI/Neon_Circuit_Mushroom_Emblem-removebg-preview"),
            new LegendEntry("Allergen", "UI/Allergen icon"),
            new LegendEntry("Parasite", "UI/Parasite icon")
        },
        new[]
        {
            new LegendEntry("Oxygen Boost", "UI/Oxygen Boost perk"),
            new LegendEntry("Shield Perk", "UI/Shield Perk")
        },
        new[]
        {
            new LegendEntry("Ate expired/spoiled food", "UI/Ate expired food icon"),
            new LegendEntry("Junk food binge", "UI/Junkfood binge icon"),
            new LegendEntry("Drank contaminated water", "UI/Accidentally drank contaminated water icon"),
            new LegendEntry("Ate spoiled food", "UI/Accidentally ate spoiled food icon"),
            new LegendEntry("Inhaled dust/allergen", "UI/Inhaled dust allergen icon"),
            new LegendEntry("Caught a cold", "UI/Caught cold from sick coworker icon"),
            new LegendEntry("Contact with sick person", "UI/Came into contact with someone who was sick icon"),
            new LegendEntry("Smoked cigarette", "UI/Smoking cigarrete icon")
        },
        new[]
        {
            new LegendEntry("Cough QTE", "UI/Cough icon final"),
            new LegendEntry("Vomit QTE", "UI/Vomit icon final"),
            new LegendEntry("Barrier taking hit", "UI/Barrier taking hit"),
            new LegendEntry("Breached barrier", "UI/Breach icon")
        }
    };

    private static readonly Dictionary<string, Sprite> IconCache = new Dictionary<string, Sprite>();

    private Image[] tabBackgrounds;
    private RectTransform entriesContent;
    private GameObject popupRoot;
    private int selectedTabIndex;

    /// <summary>Creates the legend button beside the manual icon and prepares its categorized popup.</summary>
    public void Initialize(Transform hudCanvas, RectTransform pauseButtonRect)
    {
        if (hudCanvas == null || pauseButtonRect == null || hudCanvas.Find("MapLegendButton") != null)
            return;

        Texture2D legendTexture = Resources.Load<Texture2D>(MapLegendIconResourcePath);
        if (legendTexture == null)
        {
            Debug.LogWarning("Map legend icon was not found at Resources/" + MapLegendIconResourcePath + ".jpg");
            return;
        }

        Sprite legendSprite = CreateSprite(legendTexture, "Map Legend HUD Icon");
        RectTransform manualRect = hudCanvas.Find("ManualEntryButton") as RectTransform;
        float iconSize = Mathf.Min(pauseButtonRect.rect.width, pauseButtonRect.rect.height);
        float rightEdge = manualRect != null
            ? manualRect.anchoredPosition.x - manualRect.rect.width - ToolbarIconGap
            : pauseButtonRect.anchoredPosition.x - pauseButtonRect.rect.width * 2f - ToolbarIconGap * 2f;

        GameObject buttonObject = new GameObject("MapLegendButton", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(hudCanvas, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = Vector2.one;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.pivot = Vector2.one;
        buttonRect.anchoredPosition = new Vector2(rightEdge, pauseButtonRect.anchoredPosition.y);
        buttonRect.sizeDelta = new Vector2(iconSize, iconSize);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.sprite = legendSprite;
        buttonImage.preserveAspect = true;
        buttonImage.raycastTarget = true;
        Button legendButton = buttonObject.GetComponent<Button>();
        legendButton.targetGraphic = buttonImage;
        legendButton.transition = Selectable.Transition.ColorTint;
        legendButton.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.72f, 1f, 1f, 1f),
            pressedColor = new Color(0.48f, 0.9f, 0.92f, 1f),
            selectedColor = new Color(0.72f, 1f, 1f, 1f),
            disabledColor = new Color(0.45f, 0.5f, 0.52f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.12f
        };
        legendButton.onClick.AddListener(TogglePopup);
        BuildPopup(hudCanvas);
    }

    private void Update()
    {
        if (popupRoot != null && popupRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            ClosePopup();
    }

    private void TogglePopup()
    {
        if (popupRoot == null)
            return;

        if (popupRoot.activeSelf)
        {
            ClosePopup();
            return;
        }

        popupRoot.SetActive(true);
        popupRoot.transform.SetAsLastSibling();
        BuildEntries(selectedTabIndex);
    }

    private void ClosePopup()
    {
        if (popupRoot != null)
            popupRoot.SetActive(false);
    }

    private void BuildPopup(Transform hudCanvas)
    {
        popupRoot = new GameObject("MapLegendPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        popupRoot.transform.SetParent(hudCanvas, false);
        RectTransform rootRect = popupRoot.GetComponent<RectTransform>();
        SetRect(rootRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image dimImage = popupRoot.GetComponent<Image>();
        dimImage.color = new Color(0f, 0.008f, 0.016f, 0.82f);
        dimImage.raycastTarget = true;

        GameObject panelObject = new GameObject("LegendPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(popupRoot.transform, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        SetRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PopupWidth, PopupHeight));
        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = PanelColor;
        panelImage.raycastTarget = true;

        CreateText("LegendTitle", panelRect, "MAP LEGEND", 28, AccentColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(28f, -28f), new Vector2(370f, 44f), TextAnchor.MiddleLeft);
        CreateText("LegendSubtitle", panelRect, "ICONS AND FIELD REFERENCES", 12, new Color(0.62f, 0.82f, 0.85f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(30f, -59f), new Vector2(370f, 24f), TextAnchor.MiddleLeft);

        Button closeButton = CreateButton(panelRect, "CloseLegendButton", "X", new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f),
            new Vector2(48f, 42f), PanelColor, 16);
        closeButton.onClick.AddListener(ClosePopup);

        tabBackgrounds = new Image[TabNames.Length];
        for (int index = 0; index < TabNames.Length; index++)
        {
            int tabIndex = index;
            GameObject tabObject = new GameObject(TabNames[index] + "Tab", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            tabObject.transform.SetParent(panelRect, false);
            RectTransform tabRect = tabObject.GetComponent<RectTransform>();
            float anchorMinX = (float)index / TabNames.Length;
            float anchorMaxX = (float)(index + 1) / TabNames.Length;
            tabRect.anchorMin = new Vector2(anchorMinX, 1f);
            tabRect.anchorMax = new Vector2(anchorMaxX, 1f);
            tabRect.pivot = new Vector2(0.5f, 1f);
            tabRect.offsetMin = new Vector2(5f, -133f);
            tabRect.offsetMax = new Vector2(-5f, -88f);

            Image tabImage = tabObject.GetComponent<Image>();
            tabImage.color = TabColor;
            Button tabButton = tabObject.GetComponent<Button>();
            tabButton.targetGraphic = tabImage;
            tabButton.transition = Selectable.Transition.ColorTint;
            tabButton.onClick.AddListener(() => SelectTab(tabIndex));
            tabBackgrounds[index] = tabImage;
            CreateText("TabLabel", tabRect, TabNames[index].ToUpperInvariant(), 14, Color.white,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                TextAnchor.MiddleCenter);
        }

        GameObject viewportObject = new GameObject("LegendEntriesViewport", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.transform.SetParent(panelRect, false);
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(24f, 22f);
        viewportRect.offsetMax = new Vector2(-24f, -148f);
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.12f);
        viewportImage.raycastTarget = true;
        viewportObject.GetComponent<Mask>().showMaskGraphic = false;

        GameObject contentObject = new GameObject("LegendEntries", typeof(RectTransform),
            typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        entriesContent = contentObject.GetComponent<RectTransform>();
        entriesContent.anchorMin = new Vector2(0f, 1f);
        entriesContent.anchorMax = new Vector2(1f, 1f);
        entriesContent.pivot = new Vector2(0.5f, 1f);
        entriesContent.anchoredPosition = Vector2.zero;
        entriesContent.sizeDelta = Vector2.zero;
        GridLayoutGroup grid = contentObject.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(IconCellWidth, IconCellHeight);
        grid.spacing = new Vector2(CellSpacing, CellSpacing);
        grid.padding = new RectOffset(12, 12, 12, 12);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = GridColumnCount;
        ContentSizeFitter contentFitter = contentObject.GetComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = viewportObject.GetComponent<ScrollRect>();
        scrollRect.content = entriesContent;
        scrollRect.viewport = viewportRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;

        popupRoot.SetActive(false);
    }

    private void SelectTab(int tabIndex)
    {
        selectedTabIndex = Mathf.Clamp(tabIndex, 0, TabNames.Length - 1);
        for (int index = 0; index < tabBackgrounds.Length; index++)
            tabBackgrounds[index].color = index == selectedTabIndex ? SelectedTabColor : TabColor;
        BuildEntries(selectedTabIndex);
    }

    private void BuildEntries(int tabIndex)
    {
        if (entriesContent == null)
            return;

        for (int index = entriesContent.childCount - 1; index >= 0; index--)
            Destroy(entriesContent.GetChild(index).gameObject);

        LegendEntry[] entries = EntriesByTab[tabIndex];
        for (int index = 0; index < entries.Length; index++)
            CreateLegendEntry(entriesContent, entries[index]);

        if (tabBackgrounds != null)
        {
            for (int index = 0; index < tabBackgrounds.Length; index++)
                tabBackgrounds[index].color = index == tabIndex ? SelectedTabColor : TabColor;
        }
    }

    private void CreateLegendEntry(Transform parent, LegendEntry entry)
    {
        GameObject cellObject = new GameObject(entry.label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        cellObject.transform.SetParent(parent, false);
        Image cellImage = cellObject.GetComponent<Image>();
        cellImage.color = CellColor;
        cellImage.raycastTarget = false;

        Sprite iconSprite = LoadIcon(entry.resourcePath);
        if (iconSprite != null)
        {
            GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(cellObject.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            SetRect(iconRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(EntryIconSize, EntryIconSize));
            Image iconImage = iconObject.GetComponent<Image>();
            iconImage.sprite = iconSprite;
            iconImage.preserveAspect = true;
            iconImage.color = entry.tint;
            iconImage.raycastTarget = false;
        }
        else
        {
            Debug.LogWarning("Map legend icon was not found at Resources/" + entry.resourcePath + ".png");
        }

        CreateText("IconLabel", cellObject.transform, entry.label, 13, Color.white,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 8f), new Vector2(-14f, 50f), TextAnchor.MiddleCenter);
    }

    private static Sprite LoadIcon(string resourcePath)
    {
        if (IconCache.TryGetValue(resourcePath, out Sprite cachedIcon))
            return cachedIcon;

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null)
            return null;

        Sprite icon = CreateSprite(texture, resourcePath + " Legend Sprite");
        IconCache.Add(resourcePath, icon);
        return icon;
    }

    private static Sprite CreateSprite(Texture2D texture, string spriteName)
    {
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f);
        sprite.name = spriteName;
        return sprite;
    }

    private static Button CreateButton(Transform parent, string objectName, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition,
        Vector2 size, Color color, int fontSize)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        SetRect(buttonRect, anchorMin, anchorMax, pivot, anchoredPosition, size);
        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = color;
        buttonImage.raycastTarget = true;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        button.transition = Selectable.Transition.ColorTint;
        CreateText("Label", buttonObject.transform, label, fontSize, Color.white,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            TextAnchor.MiddleCenter);
        return button;
    }

    private static Text CreateText(string objectName, Transform parent, string value, int fontSize,
        Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition,
        Vector2 sizeDelta, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetRect(rect, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>(BuiltInFontName);
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.localScale = Vector3.one;
    }

    private struct LegendEntry
    {
        public readonly string label;
        public readonly string resourcePath;
        public readonly Color tint;

        public LegendEntry(string label, string resourcePath)
            : this(label, resourcePath, Color.white)
        {
        }

        public LegendEntry(string label, string resourcePath, Color tint)
        {
            this.label = label;
            this.resourcePath = resourcePath;
            this.tint = tint;
        }
    }
}
