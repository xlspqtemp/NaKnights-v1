using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the latest digestive or respiratory event icon beneath the wellness bar.
/// </summary>
public sealed class RandomEventIconHistoryUI : MonoBehaviour
{
    private const string IconRowObjectName = "RandomEventIconRow";
    private const string BuiltInFontName = "LegacyRuntime.ttf";
    private const float IconSize = 60f;
    private const float EventNamePanelWidth = 286f;
    private const float EventNamePanelHeight = 34f;
    private const float IconGapBelowBar = 8f;
    private const float IconSlideDistance = 42f;
    private const float IconEnterDuration = 0.22f;
    private const float IconExitDuration = 0.38f;
    private const float PulseSpeed = 4.5f;
    private const float PulseScale = 0.12f;

    private static readonly Dictionary<string, Sprite> IconSpriteCache = new Dictionary<string, Sprite>();

    private readonly Dictionary<int, RandomEventIconData> latestIconByLayer = new Dictionary<int, RandomEventIconData>();
    private RectTransform iconRowRect;
    private Image currentIconImage;
    private CanvasGroup currentIconCanvasGroup;
    private int selectedLayer = 1;

    private void OnEnable()
    {
        RandomEventSystem.OnRandomEventTriggered += HandleRandomEventTriggered;
    }

    private void OnDisable()
    {
        RandomEventSystem.OnRandomEventTriggered -= HandleRandomEventTriggered;
        StopAllCoroutines();
    }

    private void Update()
    {
        if (currentIconImage == null)
            return;

        float pulse = (Mathf.Sin(Time.unscaledTime * PulseSpeed) + 1f) * 0.5f;
        currentIconImage.rectTransform.localScale = Vector3.one * (1f + pulse * PulseScale);
    }

    /// <summary>
    /// Creates the icon row beneath the wellness bar on the gameplay HUD.
    /// </summary>
    public void Initialize(RectTransform canvasRect, RectTransform wellnessBarRect)
    {
        if (canvasRect == null)
            return;

        Transform existingRow = canvasRect.Find(IconRowObjectName);
        if (existingRow != null && existingRow != transform)
            Destroy(existingRow.gameObject);

        GameObject rowObject = new GameObject(IconRowObjectName, typeof(RectTransform));
        rowObject.transform.SetParent(canvasRect, false);
        iconRowRect = rowObject.GetComponent<RectTransform>();
        iconRowRect.anchorMin = new Vector2(0.5f, 1f);
        iconRowRect.anchorMax = new Vector2(0.5f, 1f);
        iconRowRect.pivot = new Vector2(0.5f, 0.5f);
        iconRowRect.sizeDelta = new Vector2(IconSize + EventNamePanelWidth + 32f, IconSize + 12f);

        float barBottom = wellnessBarRect != null
            ? wellnessBarRect.anchoredPosition.y - wellnessBarRect.rect.height
            : -42f;
        iconRowRect.anchoredPosition = new Vector2(0f, barBottom - IconGapBelowBar - IconSize * 0.5f);
    }

    /// <summary>
    /// Updates the layer whose latest event icon is allowed to be visible.
    /// </summary>
    public void SetSelectedLayer(int layerNumber)
    {
        selectedLayer = layerNumber;
        ClearDisplayedIcons();

        if (!latestIconByLayer.TryGetValue(selectedLayer, out RandomEventIconData latestIcon))
            return;

        ShowIconImmediately(latestIcon.iconResource, latestIcon.eventName);
    }

    private void HandleRandomEventTriggered(RandomEventData eventData)
    {
        if (eventData == null)
            return;

        int eventLayer = eventData.eventType == RandomEventType.Respiratory
            ? 4
            : eventData.eventType == RandomEventType.Digestive ? 3 : 0;
        if (eventLayer == 0)
            return;

        string iconResource = GetIconResource(eventData);
        if (string.IsNullOrEmpty(iconResource))
            return;

        RandomEventIconData iconData = new RandomEventIconData(iconResource, eventData.eventName);
        latestIconByLayer[eventLayer] = iconData;
        if (selectedLayer != eventLayer || iconRowRect == null)
            return;

        if (currentIconImage != null)
        {
            currentIconCanvasGroup.interactable = false;
            currentIconCanvasGroup.blocksRaycasts = false;
            StartCoroutine(SlideOutAndRemove(currentIconImage.rectTransform, currentIconCanvasGroup));
        }

        CreateCurrentIcon(iconData, true);
    }

    private string GetIconResource(RandomEventData eventData)
    {
        switch (eventData.eventName)
        {
            case "Ate expired/spoiled food": return "UI/Ate expired food icon";
            case "Junk food binge": return "UI/Junkfood binge icon";
            case "Accidentally Drank Contaminated Water": return "UI/Accidentally drank contaminated water icon";
            case "Accidentally Ate Spoiled Food": return "UI/Accidentally ate spoiled food icon";
            case "Inhaled dust/allergen": return "UI/Inhaled dust allergen icon";
            case "Caught a cold from a sick classmate/coworker": return "UI/Caught cold from sick coworker icon";
            case "Came Into Contact With Someone Who Was Sick": return "UI/Came into contact with someone who was sick icon";
            case "Smoked cigarette": return "UI/Smoking cigarrete icon";
            default: return null;
        }
    }

    private void ShowIconImmediately(string iconResource, string eventName)
    {
        CreateCurrentIcon(new RandomEventIconData(iconResource, eventName), false);
    }

    private void CreateCurrentIcon(RandomEventIconData iconData, bool animateEntry)
    {
        Sprite iconSprite = LoadIconSprite(iconData.iconResource);
        if (iconSprite == null)
            return;

        GameObject iconObject = new GameObject("RandomEventIcon", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(CanvasGroup));
        iconObject.transform.SetParent(iconRowRect, false);

        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(IconSize, IconSize);
        iconRect.anchoredPosition = new Vector2(animateEntry ? IconSlideDistance : 0f, 0f);
        iconRect.localScale = Vector3.one;

        currentIconImage = iconObject.GetComponent<Image>();
        currentIconImage.sprite = iconSprite;
        currentIconImage.preserveAspect = true;
        currentIconImage.raycastTarget = true;

        Button iconButton = iconObject.GetComponent<Button>();
        iconButton.targetGraphic = currentIconImage;
        iconButton.transition = Selectable.Transition.None;
        Text eventNameText = CreateEventNameLabel(iconObject.transform, iconData.eventName);
        iconButton.onClick.AddListener(() =>
        {
            if (eventNameText != null)
                eventNameText.transform.parent.gameObject.SetActive(!eventNameText.transform.parent.gameObject.activeSelf);
        });

        currentIconCanvasGroup = iconObject.GetComponent<CanvasGroup>();
        currentIconCanvasGroup.alpha = animateEntry ? 0f : 1f;
        currentIconCanvasGroup.interactable = true;
        currentIconCanvasGroup.blocksRaycasts = true;

        if (animateEntry)
            StartCoroutine(SlideIn(currentIconImage.rectTransform, currentIconCanvasGroup));
    }

    private Text CreateEventNameLabel(Transform iconTransform, string eventName)
    {
        GameObject panelObject = new GameObject("EventNamePanel", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(iconTransform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(IconSize * 0.5f + 8f, 0f);
        panelRect.sizeDelta = new Vector2(EventNamePanelWidth, EventNamePanelHeight);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.008f, 0.025f, 0.035f, 0.9f);
        panelImage.raycastTarget = false;

        GameObject labelObject = new GameObject("EventName", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(panelObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(9f, 2f);
        labelRect.offsetMax = new Vector2(-9f, -2f);

        Text label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>(BuiltInFontName);
        label.fontSize = 14;
        label.fontStyle = FontStyle.Bold;
        label.color = new Color(0.78f, 0.96f, 0.98f, 1f);
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.text = eventName;
        label.raycastTarget = false;
        panelObject.SetActive(false);
        return label;
    }

    private Sprite LoadIconSprite(string resourcePath)
    {
        if (IconSpriteCache.TryGetValue(resourcePath, out Sprite cachedSprite))
            return cachedSprite;

        Texture2D iconTexture = Resources.Load<Texture2D>(resourcePath);
        if (iconTexture == null)
        {
            Debug.LogWarning("Random event icon texture was not found at Resources/" + resourcePath + ".png");
            return null;
        }

        Sprite iconSprite = Sprite.Create(iconTexture, new Rect(0f, 0f, iconTexture.width, iconTexture.height),
            new Vector2(0.5f, 0.5f), 100f);
        iconSprite.name = resourcePath + " Runtime Sprite";
        IconSpriteCache.Add(resourcePath, iconSprite);
        return iconSprite;
    }

    private IEnumerator SlideIn(RectTransform iconRect, CanvasGroup iconCanvasGroup)
    {
        float elapsed = 0f;
        while (elapsed < IconEnterDuration && iconRect != null && iconCanvasGroup != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / IconEnterDuration);
            float easedProgress = progress * progress * (3f - 2f * progress);
            iconRect.anchoredPosition = new Vector2(Mathf.Lerp(IconSlideDistance, 0f, easedProgress), 0f);
            iconCanvasGroup.alpha = easedProgress;
            yield return null;
        }

        if (iconRect != null)
            iconRect.anchoredPosition = Vector2.zero;
        if (iconCanvasGroup != null)
            iconCanvasGroup.alpha = 1f;
    }

    private IEnumerator SlideOutAndRemove(RectTransform iconRect, CanvasGroup iconCanvasGroup)
    {
        if (iconRect == null || iconCanvasGroup == null)
            yield break;

        float startX = iconRect.anchoredPosition.x;
        float elapsed = 0f;
        while (elapsed < IconExitDuration && iconRect != null && iconCanvasGroup != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / IconExitDuration);
            float easedProgress = progress * progress * (3f - 2f * progress);
            iconRect.anchoredPosition = new Vector2(Mathf.Lerp(startX, -IconSlideDistance, easedProgress), 0f);
            iconCanvasGroup.alpha = 1f - easedProgress;
            yield return null;
        }

        if (iconRect != null)
            Destroy(iconRect.gameObject);
    }

    private void ClearDisplayedIcons()
    {
        StopAllCoroutines();
        currentIconImage = null;
        currentIconCanvasGroup = null;

        if (iconRowRect == null)
            return;

        for (int index = iconRowRect.childCount - 1; index >= 0; index--)
            Destroy(iconRowRect.GetChild(index).gameObject);
    }

    private struct RandomEventIconData
    {
        public readonly string iconResource;
        public readonly string eventName;

        public RandomEventIconData(string iconResource, string eventName)
        {
            this.iconResource = iconResource;
            this.eventName = eventName;
        }
    }
}
