using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Builds a runtime slideshow manual overlay using textures supplied by the caller.
/// </summary>
public static class SlideshowManualScreen
{
    private const string BuiltInFontName = "LegacyRuntime.ttf";
    private const float MenuIconSize = 112f;
    private const float MenuIconInset = 64f;
    private const float HomeArtworkSize = 820f;
    private const float SlideArtworkWidth = 1320f;
    private const float SlideArtworkHeight = 780f;
    private const float TopButtonInset = 64f;
    private const float TopButtonHeight = 58f;
    private const float MainMenuButtonWidth = 220f;
    private const float ManualBackButtonWidth = 178f;
    private const float ArrowButtonSize = 86f;

    private static readonly Color OverlayColor = new Color(0.008f, 0.023f, 0.035f, 0.9f);
    private static readonly Color PanelColor = new Color(0.025f, 0.09f, 0.115f, 0.96f);
    private static readonly Color AccentColor = new Color(0.12f, 0.82f, 0.84f, 1f);
    private static readonly Color AccentDimColor = new Color(0.035f, 0.28f, 0.34f, 0.96f);
    private static readonly Color TextColor = new Color(0.91f, 0.98f, 0.99f, 1f);
    private static readonly Color MutedTextColor = new Color(0.55f, 0.72f, 0.76f, 1f);
    private static readonly Color HitboxNormalColor = new Color(0.08f, 0.72f, 0.78f, 0.015f);
    private static readonly Color HitboxHoverColor = new Color(0.08f, 0.72f, 0.78f, 0.22f);
    private static readonly Color HitboxPressedColor = new Color(0.08f, 0.72f, 0.78f, 0.34f);

    private static readonly string[] UnitSlideNames =
    {
        "Neutrophil", "Basophil", "Eosinophil", "Macrophage", "KTC", "B-Cell"
    };

    private static readonly string[] MapSlideNames =
    {
        "Lymphatic", "Circulatory", "Digestive", "Respiratory"
    };

    private static readonly string[] PathogenSlideNames =
    {
        "Parasite", "Bacteria", "Fungi", "Allergen", "Virus"
    };

    private static readonly string[] GameplaySlideNames =
    {
        "Wellness Bar", "DayTime", "Human Snapshot", "Layers", "Routine", "Random Events",
        "Control Console", "Training Console"
    };

    private static GameObject overlayRoot;
    private static GameObject pageRoot;
    private static RawImage currentSlideImage;
    private static Text currentSlideCaption;
    private static Button previousArrowButton;
    private static Button nextArrowButton;
    private static List<SlideEntry> activeSlides;
    private static int activeSlideIndex;
    private static Texture2D homeTexture;
    private static Texture2D[] unitTextures;
    private static Texture2D[] mapTextures;
    private static Texture2D[] pathogenTextures;
    private static Texture2D[] gameplayTextures;
    private static Texture2D menuIconTexture;

    /// <summary>
    /// Stores the manual artwork so the gameplay HUD can open the same slideshow.
    /// </summary>
    public static void ConfigureManual(Texture2D iconTexture, Texture2D manualHomeTexture,
        Texture2D[] unitSlides, Texture2D[] mapSlides, Texture2D[] pathogenSlides, Texture2D[] gameplaySlides)
    {
        menuIconTexture = iconTexture;
        homeTexture = manualHomeTexture;
        unitTextures = unitSlides;
        mapTextures = mapSlides;
        pathogenTextures = pathogenSlides;
        gameplayTextures = gameplaySlides;
    }

    /// <summary>
    /// Adds a manual icon immediately to the left of the gameplay pause button.
    /// </summary>
    public static void AddGameMenuEntry(Transform canvas, RectTransform pauseButtonRect)
    {
        if (canvas == null || pauseButtonRect == null || menuIconTexture == null || homeTexture == null ||
            canvas.Find("ManualEntryButton") != null)
        {
            return;
        }

        GameObject entryObject = new GameObject("ManualEntryButton", typeof(RectTransform), typeof(RawImage), typeof(Button));
        entryObject.transform.SetParent(canvas, false);

        float iconSize = Mathf.Min(pauseButtonRect.rect.width, pauseButtonRect.rect.height);
        RectTransform entryRect = entryObject.GetComponent<RectTransform>();
        SetRect(entryRect, Vector2.one, Vector2.one, Vector2.one,
            new Vector2(pauseButtonRect.anchoredPosition.x - pauseButtonRect.rect.width - 10f,
                pauseButtonRect.anchoredPosition.y), new Vector2(iconSize, iconSize));

        RawImage iconImage = entryObject.GetComponent<RawImage>();
        iconImage.texture = menuIconTexture;
        iconImage.color = Color.white;
        iconImage.raycastTarget = true;

        Button entryButton = entryObject.GetComponent<Button>();
        ConfigureButton(entryButton, iconImage, () => ShowConfigured(canvas), Color.white,
            new Color(0.72f, 1f, 1f, 1f), new Color(0.48f, 0.9f, 0.92f, 1f));
    }

    /// <summary>
    /// Opens the configured manual slideshow over the supplied canvas.
    /// </summary>
    public static void ShowConfigured(Transform canvas)
    {
        Show(canvas, homeTexture, unitTextures, mapTextures, pathogenTextures, gameplayTextures);
    }

    /// <summary>
    /// Adds the supplied manual icon as a clickable entry at the top-right of the canvas.
    /// </summary>
    public static void AddMenuEntry(Transform canvas, UnityAction openManual, Texture2D iconTexture)
    {
        if (canvas == null || canvas.Find("ManualEntryButton") != null)
        {
            return;
        }

        GameObject entryObject = new GameObject("ManualEntryButton", typeof(RectTransform), typeof(RawImage), typeof(Button));
        entryObject.transform.SetParent(canvas, false);
        SetRect(entryObject.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-MenuIconInset, -MenuIconInset), new Vector2(MenuIconSize, MenuIconSize));

        RawImage iconImage = entryObject.GetComponent<RawImage>();
        iconImage.texture = iconTexture;
        iconImage.color = Color.white;
        iconImage.raycastTarget = true;

        Button entryButton = entryObject.GetComponent<Button>();
        ConfigureButton(entryButton, iconImage, openManual, Color.white, new Color(0.72f, 1f, 1f, 1f),
            new Color(0.48f, 0.9f, 0.92f, 1f));
    }

    /// <summary>
    /// Shows the manual home and its category slideshows over the supplied menu canvas.
    /// </summary>
    public static void Show(Transform canvas, Texture2D manualHomeTexture, Texture2D[] unitSlides,
        Texture2D[] mapSlides, Texture2D[] pathogenSlides, Texture2D[] gameplaySlides)
    {
        if (canvas == null)
        {
            return;
        }

        if (overlayRoot != null)
        {
            Close();
        }

        homeTexture = manualHomeTexture;
        unitTextures = unitSlides;
        mapTextures = mapSlides;
        pathogenTextures = pathogenSlides;
        gameplayTextures = gameplaySlides;

        overlayRoot = new GameObject("SlideshowManualOverlay", typeof(RectTransform), typeof(Image));
        overlayRoot.transform.SetParent(canvas, false);
        overlayRoot.transform.SetAsLastSibling();
        SetRect(overlayRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        Image overlayImage = overlayRoot.GetComponent<Image>();
        overlayImage.color = OverlayColor;
        overlayImage.raycastTarget = true;
        BuildManualHome();
    }

    private static void BuildManualHome()
    {
        ClearPage();
        pageRoot = CreateStretchObject(overlayRoot.transform, "ManualHomePage");
        CreateMainMenuButton(pageRoot.transform);

        RectTransform artworkFrame = CreateCenteredRect(pageRoot.transform, "ManualArtworkFrame",
            Vector2.zero, new Vector2(HomeArtworkSize, HomeArtworkSize));
        float artworkAspect = GetTextureAspect(homeTexture);

        RawImage artwork = CreateRawImage(artworkFrame, "ManualHomeArtwork", homeTexture);
        SetRect(artwork.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        AddAspectFitter(artwork.gameObject, artworkAspect);

        GameObject hitAreaRoot = CreateStretchObject(artworkFrame, "ManualCategoryHitAreas");
        AddAspectFitter(hitAreaRoot, artworkAspect);
        CreateCategoryHitArea(hitAreaRoot.transform, "UnitsCategoryHitArea", new Vector2(0.5f, 0.66f),
            OpenUnits);
        CreateCategoryHitArea(hitAreaRoot.transform, "MapCategoryHitArea", new Vector2(0.5f, 0.53f),
            OpenMap);
        CreateCategoryHitArea(hitAreaRoot.transform, "PathogensCategoryHitArea", new Vector2(0.5f, 0.40f),
            OpenPathogens);
        CreateCategoryHitArea(hitAreaRoot.transform, "GameplayCategoryHitArea", new Vector2(0.5f, 0.27f),
            OpenGameplay);
    }

    private static void CreateCategoryHitArea(Transform parent, string objectName, Vector2 anchor,
        UnityAction onClick)
    {
        GameObject hitAreaObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        hitAreaObject.transform.SetParent(parent, false);
        SetRect(hitAreaObject.GetComponent<RectTransform>(), anchor, anchor, anchor, Vector2.zero,
            new Vector2(370f, 82f));

        Image hitAreaImage = hitAreaObject.GetComponent<Image>();
        hitAreaImage.color = HitboxNormalColor;
        hitAreaImage.raycastTarget = true;
        Button hitAreaButton = hitAreaObject.GetComponent<Button>();
        ConfigureButton(hitAreaButton, hitAreaImage, onClick, HitboxNormalColor, HitboxHoverColor,
            HitboxPressedColor);
    }

    private static void CreateMainMenuButton(Transform parent)
    {
        CreateTextButton(parent, "MainMenuButton", "MAIN MENU", new Vector2(1f, 1f),
            new Vector2(-TopButtonInset, -TopButtonInset),
            new Vector2(MainMenuButtonWidth, TopButtonHeight), Close, PanelColor);
    }

    private static void CreateManualBackButton(Transform parent)
    {
        CreateTextButton(parent, "BackToManualButton", "‹  MANUAL", new Vector2(0f, 1f),
            new Vector2(TopButtonInset, -TopButtonInset),
            new Vector2(ManualBackButtonWidth, TopButtonHeight), BuildManualHome, PanelColor);
    }

    private static void BuildSectionPage(string sectionTitle, string[] names, Texture2D[] textures)
    {
        ClearPage();
        pageRoot = CreateStretchObject(overlayRoot.transform, "ManualSectionPage");
        CreateManualBackButton(pageRoot.transform);
        CreateMainMenuButton(pageRoot.transform);
        CreateText(pageRoot.transform, "ManualSectionTitle", sectionTitle, 30, FontStyle.Bold,
            AccentColor, TextAnchor.MiddleCenter, new Vector2(0f, -TopButtonInset),
            new Vector2(600f, 62f));

        activeSlides = BuildAvailableSlides(names, textures);
        activeSlideIndex = 0;
        if (activeSlides.Count == 0)
        {
            CreateText(pageRoot.transform, "NoSlidesMessage", "NO SLIDES AVAILABLE", 26,
                FontStyle.Bold, MutedTextColor, TextAnchor.MiddleCenter, Vector2.zero,
                new Vector2(620f, 80f));
            return;
        }

        RectTransform slideFrame = CreateCenteredRect(pageRoot.transform, "ManualSlideFrame",
            new Vector2(0f, -12f), new Vector2(SlideArtworkWidth, SlideArtworkHeight));
        currentSlideImage = CreateRawImage(slideFrame, "ManualSlideArtwork", activeSlides[0].texture);
        SetRect(currentSlideImage.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        AddAspectFitter(currentSlideImage.gameObject, GetTextureAspect(activeSlides[0].texture));

        CreateArrowButton(pageRoot.transform, "PreviousSlideButton", "‹", new Vector2(0f, 0.5f),
            new Vector2(ArrowButtonSize * 0.5f + 34f, 0f), PreviousSlide, out previousArrowButton);
        CreateArrowButton(pageRoot.transform, "NextSlideButton", "›", new Vector2(1f, 0.5f),
            new Vector2(-ArrowButtonSize * 0.5f - 34f, 0f), NextSlide, out nextArrowButton);
        currentSlideCaption = CreateText(pageRoot.transform, "ManualSlideCaption", string.Empty, 24,
            FontStyle.Bold, TextColor, TextAnchor.MiddleCenter, new Vector2(0f, -478f),
            new Vector2(760f, 52f));
        UpdateSlideDisplay();
    }

    private static List<SlideEntry> BuildAvailableSlides(string[] names, Texture2D[] textures)
    {
        List<SlideEntry> result = new List<SlideEntry>();
        if (names == null || textures == null)
        {
            return result;
        }

        int itemCount = Mathf.Min(names.Length, textures.Length);
        for (int index = 0; index < itemCount; index++)
        {
            if (textures[index] != null)
            {
                result.Add(new SlideEntry(names[index], textures[index]));
            }
        }

        return result;
    }

    private static void CreateArrowButton(Transform parent, string objectName, string glyph,
        Vector2 anchor, Vector2 anchoredPosition, UnityAction onClick, out Button createdButton)
    {
        GameObject arrowObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        arrowObject.transform.SetParent(parent, false);
        SetRect(arrowObject.GetComponent<RectTransform>(), anchor, anchor, new Vector2(0.5f, 0.5f),
            anchoredPosition, new Vector2(ArrowButtonSize, ArrowButtonSize));

        Image arrowImage = arrowObject.GetComponent<Image>();
        arrowImage.color = AccentDimColor;
        arrowImage.raycastTarget = true;
        createdButton = arrowObject.GetComponent<Button>();
        ConfigureButton(createdButton, arrowImage, onClick, AccentDimColor, AccentColor,
            new Color(0.08f, 0.56f, 0.62f, 1f));
        CreateText(arrowObject.transform, "ArrowGlyph", glyph, 58, FontStyle.Bold, TextColor,
            TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, Vector2.zero, true);
    }

    private static void PreviousSlide()
    {
        if (activeSlides == null || activeSlides.Count == 0)
        {
            return;
        }

        activeSlideIndex = Mathf.Clamp(activeSlideIndex - 1, 0, activeSlides.Count - 1);
        UpdateSlideDisplay();
    }

    private static void NextSlide()
    {
        if (activeSlides == null || activeSlides.Count == 0)
        {
            return;
        }

        activeSlideIndex = Mathf.Clamp(activeSlideIndex + 1, 0, activeSlides.Count - 1);
        UpdateSlideDisplay();
    }

    private static void UpdateSlideDisplay()
    {
        if (activeSlides == null || activeSlides.Count == 0 || currentSlideImage == null)
        {
            return;
        }

        SlideEntry slide = activeSlides[activeSlideIndex];
        currentSlideImage.texture = slide.texture;
        AspectRatioFitter fitter = currentSlideImage.GetComponent<AspectRatioFitter>();
        if (fitter != null)
        {
            fitter.aspectRatio = GetTextureAspect(slide.texture);
        }

        if (currentSlideCaption != null)
        {
            currentSlideCaption.text = slide.name + "  |  " + (activeSlideIndex + 1) + " / " + activeSlides.Count;
        }

        if (previousArrowButton != null)
        {
            previousArrowButton.interactable = activeSlideIndex > 0;
        }

        if (nextArrowButton != null)
        {
            nextArrowButton.interactable = activeSlideIndex < activeSlides.Count - 1;
        }
    }

    private static void OpenUnits()
    {
        BuildSectionPage("UNITS", UnitSlideNames, unitTextures);
    }

    private static void OpenMap()
    {
        BuildSectionPage("MAP", MapSlideNames, mapTextures);
    }

    private static void OpenPathogens()
    {
        BuildSectionPage("PATHOGENS", PathogenSlideNames, pathogenTextures);
    }

    private static void OpenGameplay()
    {
        BuildSectionPage("GAMEPLAY", GameplaySlideNames, gameplayTextures);
    }

    private static void Close()
    {
        if (overlayRoot != null)
        {
            Object.Destroy(overlayRoot);
        }

        overlayRoot = null;
        pageRoot = null;
        currentSlideImage = null;
        currentSlideCaption = null;
        previousArrowButton = null;
        nextArrowButton = null;
        activeSlides = null;
        activeSlideIndex = 0;
        homeTexture = null;
        unitTextures = null;
        mapTextures = null;
        pathogenTextures = null;
        gameplayTextures = null;
    }

    private static void ClearPage()
    {
        if (pageRoot != null)
        {
            Object.Destroy(pageRoot);
        }

        pageRoot = null;
        currentSlideImage = null;
        currentSlideCaption = null;
        previousArrowButton = null;
        nextArrowButton = null;
        activeSlides = null;
        activeSlideIndex = 0;
    }

    private static GameObject CreateStretchObject(Transform parent, string objectName)
    {
        GameObject createdObject = new GameObject(objectName, typeof(RectTransform));
        createdObject.transform.SetParent(parent, false);
        SetRect(createdObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return createdObject;
    }

    private static RectTransform CreateCenteredRect(Transform parent, string objectName,
        Vector2 anchoredPosition, Vector2 size)
    {
        GameObject createdObject = new GameObject(objectName, typeof(RectTransform));
        createdObject.transform.SetParent(parent, false);
        RectTransform rect = createdObject.GetComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), anchoredPosition, size);
        return rect;
    }

    private static RawImage CreateRawImage(Transform parent, string objectName, Texture2D texture)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(parent, false);
        RawImage rawImage = imageObject.GetComponent<RawImage>();
        rawImage.texture = texture;
        rawImage.color = Color.white;
        rawImage.raycastTarget = false;
        return rawImage;
    }

    private static void AddAspectFitter(GameObject targetObject, float aspectRatio)
    {
        AspectRatioFitter fitter = targetObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = aspectRatio;
    }

    private static float GetTextureAspect(Texture2D texture)
    {
        if (texture == null || texture.height <= 0)
        {
            return 1f;
        }

        return (float)texture.width / texture.height;
    }

    private static void CreateTextButton(Transform parent, string objectName, string label,
        Vector2 anchor, Vector2 anchoredPosition, Vector2 size, UnityAction onClick, Color backgroundColor)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        SetRect(buttonObject.GetComponent<RectTransform>(), anchor, anchor, anchor, anchoredPosition, size);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = backgroundColor;
        buttonImage.raycastTarget = true;
        Button button = buttonObject.GetComponent<Button>();
        ConfigureButton(button, buttonImage, onClick, backgroundColor, AccentColor, AccentDimColor);
        CreateText(buttonObject.transform, "Label", label, 20, FontStyle.Bold, TextColor,
            TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, Vector2.zero, true);
    }

    private static Text CreateText(Transform parent, string objectName, string content, int fontSize,
        FontStyle fontStyle, Color color, TextAnchor alignment, Vector2 anchoredPosition, Vector2 size,
        Vector2? anchor = null, bool stretch = false)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        Vector2 resolvedAnchor = anchor ?? new Vector2(0.5f, 0.5f);
        SetRect(textRect, stretch ? Vector2.zero : resolvedAnchor, stretch ? Vector2.one : resolvedAnchor,
            stretch ? new Vector2(0.5f, 0.5f) : resolvedAnchor, anchoredPosition, size);

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>(BuiltInFontName);
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private static void ConfigureButton(Button button, Graphic targetGraphic, UnityAction onClick,
        Color normalColor, Color highlightedColor, Color pressedColor)
    {
        button.targetGraphic = targetGraphic;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = normalColor,
            highlightedColor = highlightedColor,
            pressedColor = pressedColor,
            selectedColor = highlightedColor,
            disabledColor = MutedTextColor,
            colorMultiplier = 1f,
            fadeDuration = 0.12f
        };
        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private struct SlideEntry
    {
        public readonly string name;
        public readonly Texture2D texture;

        public SlideEntry(string slideName, Texture2D slideTexture)
        {
            name = slideName;
            texture = slideTexture;
        }
    }
}
