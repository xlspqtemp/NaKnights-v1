using System;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Drives the "DAY COUNTER" panel (mock region #1):
/// DAY [n], ACTIVE badge, HH:MM AM/PM, weather label.
///
/// Fully tunable via ProgressionMode:
///   - RealTime      : day/clock ticks up on its own (classic sim clock)
///   - Manual        : nothing advances automatically; call AdvanceDay() /
///                     TickMinutes() yourself (good for testing or turn-based flow)
///   - ObjectiveBased: clock does NOT run; day only advances when you call
///                     CompleteObjective() (e.g. sector cleared)
///   - Hybrid        : clock ticks like RealTime, but you can also force an
///                     early day-advance via CompleteObjective()
///
/// Attach to an empty GameObject in your HUD scene and wire the
/// TMP fields in the Inspector.
/// </summary>
public class DayCounterUI : MonoBehaviour
{
    private const string BankGothicFontResourcePath = "BankGothicMediumSDF";

    public enum ProgressionMode { RealTime, Manual, ObjectiveBased, Hybrid }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI dayLabel;      // "DAY 23"
    [SerializeField] private TextMeshProUGUI statusBadge;   // "ACTIVE"
    [SerializeField] private TextMeshProUGUI timeLabel;     // "06:42 AM"
    [SerializeField] private TextMeshProUGUI weatherLabel;  // "Clear Weather"
    [SerializeField] private GameObject statusBadgeBG;      // optional colored pill behind the badge
    [SerializeField] private RawImage dayIcon;
    [SerializeField] private RawImage moonIcon;

    [Header("Day/Night Icon")]
    [SerializeField, Range(0, 23)] private int dayStartsAtHour = 6;
    [SerializeField, Range(0, 23)] private int eveningStartsAtHour = 18;
    [SerializeField, Min(0.01f)] private float iconTransitionDuration = 0.45f;
    [SerializeField, Min(0f)] private float iconTransitionDistance = 24f;

    [Header("Progression Mode")]
    [Tooltip("RealTime: ticks on its own. Manual: you call AdvanceDay()/TickMinutes(). " +
             "ObjectiveBased: only advances via CompleteObjective(). Hybrid: ticks AND can be force-advanced.")]
    [SerializeField] private ProgressionMode progressionMode = ProgressionMode.RealTime;

    [Header("Real-Time Tuning")]
    [Tooltip("If enabled, set 'Day Length In Real Seconds' below and the per-minute tick rate is calculated for you.")]
    [SerializeField] private bool useDayLengthShortcut = true;
    [Tooltip("How many real seconds a full in-game day should take (RealTime/Hybrid only).")]
    [SerializeField] private float dayLengthInRealSeconds = 180f;
    [Tooltip("Manual tick rate: real seconds per in-game minute. Only used if the shortcut above is off.")]
    [SerializeField] private float secondsPerGameMinute = 1f;
    [Tooltip("Global speed multiplier you can nudge at runtime (e.g. a fast-forward button).")]
    [Range(0.1f, 10f)]
    [SerializeField] private float timeScale = 1f;

    [Header("Objective-Based Tuning")]
    [Tooltip("If true, CompleteObjective() only advances the day once progress reaches the threshold below.")]
    [SerializeField] private bool requireFullObjective = true;
    [Range(0f, 1f)]
    [SerializeField] private float objectiveCompletionThreshold = 1f;

    [Header("Run Length")]
    [SerializeField, Min(1)] private int totalGameDays = 14;

    [Header("Start State")]
    [SerializeField] private bool isRunning = true;
    [SerializeField] private int startDay = 1;
    [SerializeField] private int startHour = 6;
    [SerializeField] private int startMinute = 0;
    [SerializeField] private string currentWeather = "Clear Weather";

    public event Action<int> OnDayAdvanced;
    public event Action<int, int> OnTimeChanged; // hour, minute
    public event Action<int, int> OnHourAdvanced; // day, hour

    private int currentDay;
    private int currentHour;
    private int currentMinute;
    private float minuteTimer;
    private float runtimeSecondsPerGameMinute;
    private bool dayNightIconStateInitialized;
    private bool daytimeIconShown;
    private bool dayNightIconTransitioning;
    private float dayNightIconTransitionElapsed;
    private Vector2 dayIconHomePosition;
    private Vector2 moonIconHomePosition;

    private const int MINUTES_PER_DAY = 24 * 60;
    private const float MIN_ICON_TRANSITION_DURATION = 0.01f;

    private void Awake()
    {
        GameplaySpeed.ResetForNewRun();
        currentDay = startDay;
        currentHour = startHour;
        currentMinute = startMinute;
        RecalculateTickRate();
    }

    private void Start()
    {
        ConfigureCompactHeader();
        RefreshAll();
        if (GetComponent<GameplaySpeedButton>() == null)
            gameObject.AddComponent<GameplaySpeedButton>();
    }

    private void ConfigureCompactHeader()
    {
        VerticalLayoutGroup layoutGroup = GetComponent<VerticalLayoutGroup>();
        if (layoutGroup != null)
            layoutGroup.enabled = false;

        TMP_FontAsset bankGothicFont = Resources.Load<TMP_FontAsset>(BankGothicFontResourcePath);
        ApplyHeaderFont(dayLabel, bankGothicFont);
        ApplyHeaderFont(statusBadge, bankGothicFont);
        ApplyHeaderFont(timeLabel, bankGothicFont);
        ApplyHeaderFont(weatherLabel, bankGothicFont);

        ConfigureHeaderLabel(dayLabel, new Vector2(12f, 0f), new Vector2(84f, 0f));
        ConfigureHeaderLabel(timeLabel, new Vector2(98f, 0f), new Vector2(94f, 0f));

        if (dayLabel != null)
        {
            dayLabel.fontSize = 16f;
            dayLabel.fontStyle = FontStyles.Normal;
            dayLabel.alignment = TextAlignmentOptions.MidlineLeft;
            dayLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }

        if (timeLabel != null)
        {
            timeLabel.fontSize = 16f;
            timeLabel.alignment = TextAlignmentOptions.MidlineLeft;
            timeLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    private static void ApplyHeaderFont(TextMeshProUGUI label, TMP_FontAsset font)
    {
        if (label != null && font != null)
            label.font = font;
    }

    private static void ConfigureHeaderLabel(TextMeshProUGUI label, Vector2 position, Vector2 size)
    {
        if (label == null)
            return;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = position;
        labelRect.sizeDelta = size;
    }

    private void OnValidate()
    {
        RecalculateTickRate();
    }

    private void RecalculateTickRate()
    {
        runtimeSecondsPerGameMinute = useDayLengthShortcut
            ? Mathf.Max(0.001f, dayLengthInRealSeconds / MINUTES_PER_DAY)
            : Mathf.Max(0.001f, secondsPerGameMinute);
    }

    private void Update()
    {
        UpdateDayNightIconTransition();

        bool clockShouldRun = isRunning &&
            (progressionMode == ProgressionMode.RealTime || progressionMode == ProgressionMode.Hybrid);

        if (!clockShouldRun) return;

        minuteTimer += Time.unscaledDeltaTime * Mathf.Max(0.01f, timeScale) * GameplaySpeed.Multiplier;
        if (minuteTimer >= runtimeSecondsPerGameMinute)
        {
            minuteTimer -= runtimeSecondsPerGameMinute;
            TickMinutes(1);
        }
    }

    /// <summary>Manually advance the clock by N in-game minutes. Works in any mode
    /// (useful for Manual mode, turn-based ticks, or debug skip buttons).</summary>
    public void TickMinutes(int minutes)
    {
        for (int i = 0; i < minutes; i++)
        {
            currentMinute++;
            if (currentMinute >= 60)
            {
                currentMinute = 0;
                currentHour++;
                if (currentHour >= 24)
                {
                    currentHour = 0;
                    AdvanceDay();
                }

                OnHourAdvanced?.Invoke(currentDay, currentHour);
            }
        }

        OnTimeChanged?.Invoke(currentHour, currentMinute);
        RefreshTime();
    }

    /// <summary>Force the day to advance by one, regardless of mode.
    /// Always available for debug/testing.</summary>
    public void AdvanceDay()
    {
        currentDay++;
        OnDayAdvanced?.Invoke(currentDay);
        RefreshDay();

        // Optional: auto-log to the console.
        // ConsoleLogUI.Instance?.Log($"Day {currentDay} began.", ConsoleLogUI.LogType.System);
    }

    /// <summary>Call this when a sector/objective's progress changes.
    /// In ObjectiveBased mode, this is what actually drives day advancement.
    /// In Hybrid mode, it lets you force an early advance on top of the running clock.
    /// In RealTime/Manual mode, this is a no-op (clock/manual calls drive it instead).</summary>
    /// <param name="progress01">Completion progress from 0 to 1 (e.g. 0.68 for 68%).</param>
    public void CompleteObjective(float progress01)
    {
        if (progressionMode != ProgressionMode.ObjectiveBased && progressionMode != ProgressionMode.Hybrid)
            return;

        bool meetsThreshold = requireFullObjective
            ? progress01 >= objectiveCompletionThreshold
            : progress01 > 0f;

        if (meetsThreshold)
            AdvanceDay();
    }

    public void SetActive(bool active)
    {
        isRunning = active;
        if (statusBadge != null)
            statusBadge.text = active ? "ACTIVE" : "PAUSED";
        if (statusBadgeBG != null)
        {
            var img = statusBadgeBG.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                img.color = active ? new Color(0.15f, 0.55f, 0.3f) : new Color(0.5f, 0.15f, 0.15f);
        }
    }

    public void SetWeather(string weather)
    {
        currentWeather = weather;
        if (weatherLabel != null)
            weatherLabel.text = weather;
    }

    public void SetDay(int day)
    {
        currentDay = day;
        RefreshDay();
    }

    public void SetTime(int hour, int minute)
    {
        currentHour = Mathf.Clamp(hour, 0, 23);
        currentMinute = Mathf.Clamp(minute, 0, 59);
        RefreshTime();
    }

    /// <summary>Runtime speed control, e.g. wire to a fast-forward/slow-mo button.</summary>
    public void SetTimeScale(float scale)
    {
        timeScale = Mathf.Max(0.01f, scale);
    }

    /// <summary>Switch modes at runtime if needed (e.g. lock the clock during cutscenes).</summary>
    public void SetProgressionMode(ProgressionMode mode)
    {
        progressionMode = mode;
    }

    private void RefreshAll()
    {
        RefreshDay();
        RefreshTime();
        SetActive(isRunning);
        SetWeather(currentWeather);
    }

    private void RefreshDay()
    {
        if (dayLabel != null)
            dayLabel.text = $"Day {currentDay} |";
    }

    private void RefreshTime()
    {
        RefreshDayNightIcon();
        if (timeLabel == null) return;

        int displayHour = currentHour % 12;
        if (displayHour == 0) displayHour = 12;
        string ampm = currentHour < 12 ? "AM" : "PM";
        timeLabel.text = $"{displayHour} {ampm}";
    }

    private bool IsDaytime(int hour)
    {
        if (dayStartsAtHour == eveningStartsAtHour)
            return true;

        return dayStartsAtHour < eveningStartsAtHour
            ? hour >= dayStartsAtHour && hour < eveningStartsAtHour
            : hour >= dayStartsAtHour || hour < eveningStartsAtHour;
    }

    private void RefreshDayNightIcon()
    {
        if (dayIcon == null || moonIcon == null)
            return;

        if (!dayNightIconStateInitialized)
        {
            dayIconHomePosition = dayIcon.rectTransform.anchoredPosition;
            moonIconHomePosition = moonIcon.rectTransform.anchoredPosition;
            daytimeIconShown = IsDaytime(currentHour);
            dayNightIconStateInitialized = true;
            dayNightIconTransitioning = false;
            SetIconPose(dayIcon, dayIconHomePosition, daytimeIconShown ? 1f : 0f);
            SetIconPose(moonIcon, moonIconHomePosition, daytimeIconShown ? 0f : 1f);
            return;
        }

        bool shouldShowDaytimeIcon = IsDaytime(currentHour);
        if (shouldShowDaytimeIcon == daytimeIconShown)
            return;

        daytimeIconShown = shouldShowDaytimeIcon;
        dayNightIconTransitionElapsed = 0f;
        dayNightIconTransitioning = true;

        RawImage incomingIcon = daytimeIconShown ? dayIcon : moonIcon;
        Vector2 incomingHomePosition = daytimeIconShown ? dayIconHomePosition : moonIconHomePosition;
        SetIconPose(incomingIcon, incomingHomePosition - Vector2.up * iconTransitionDistance, 0f);
    }

    private void UpdateDayNightIconTransition()
    {
        if (!dayNightIconTransitioning || dayIcon == null || moonIcon == null)
            return;

        dayNightIconTransitionElapsed += Time.unscaledDeltaTime;
        float transitionDuration = Mathf.Max(MIN_ICON_TRANSITION_DURATION, iconTransitionDuration);
        float progress = Mathf.Clamp01(dayNightIconTransitionElapsed / transitionDuration);
        float easedProgress = progress * progress * (3f - 2f * progress);

        RawImage incomingIcon = daytimeIconShown ? dayIcon : moonIcon;
        RawImage outgoingIcon = daytimeIconShown ? moonIcon : dayIcon;
        Vector2 incomingHomePosition = daytimeIconShown ? dayIconHomePosition : moonIconHomePosition;
        Vector2 outgoingHomePosition = daytimeIconShown ? moonIconHomePosition : dayIconHomePosition;

        SetIconPose(outgoingIcon, outgoingHomePosition + Vector2.up * iconTransitionDistance * easedProgress, 1f - easedProgress);
        SetIconPose(incomingIcon, incomingHomePosition - Vector2.up * iconTransitionDistance * (1f - easedProgress), easedProgress);

        if (progress >= 1f)
        {
            dayNightIconTransitioning = false;
            SetIconPose(dayIcon, dayIconHomePosition, daytimeIconShown ? 1f : 0f);
            SetIconPose(moonIcon, moonIconHomePosition, daytimeIconShown ? 0f : 1f);
        }
    }

    private static void SetIconPose(RawImage icon, Vector2 position, float alpha)
    {
        RectTransform iconRect = icon.rectTransform;
        iconRect.anchoredPosition = position;

        Color iconColor = icon.color;
        iconColor.a = alpha;
        icon.color = iconColor;
    }

    public int CurrentDay => currentDay;
    public int CurrentHour => currentHour;
    public int CurrentMinute => currentMinute;
    public bool IsRunning => isRunning;
    public ProgressionMode CurrentMode => progressionMode;
    public int TotalGameDays => totalGameDays;
}
