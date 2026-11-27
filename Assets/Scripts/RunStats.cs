using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Observes existing gameplay events and records non-invasive per-run telemetry.</summary>
[DisallowMultipleComponent]
public sealed class RunStats : MonoBehaviour
{
    private const string ExportFileName = "run-stats.jsonl";
    private const float MinutesPerGameDay = 24f * 60f;
    private const string DigestiveBarrierName = "Digestive";
    private const string RespiratoryBarrierName = "Respiratory";

    [Serializable]
    public sealed class InfectionRecord
    {
        public string pathogenType;
        public string pathogenName;
        public string spawnGameTime;
        public float spawnGameMinutes;
        public string firstDeploymentGameTime;
        public float firstDeploymentGameMinutes = -1f;
        public bool escalated;
        public int daysUnresolved;
        public bool resolved;
    }

    [Serializable]
    public sealed class DeploymentRecord
    {
        public string unitType;
        public string pathogenType;
        public bool matched;
        public int unitCount;
        public int unitsLost;
    }

    [Serializable]
    public sealed class HourlyWellnessSample
    {
        public int day;
        public int hour;
        public float wellness;
    }

    /// <summary>Read-only copy of run telemetry for consumers such as end-of-run rating systems.</summary>
    [Serializable]
    public sealed class RatingSnapshot
    {
        public float daysSurvived;
        public float finalWellness;
        public float supplySpent;
        public int woundRepairQteAttempts;
        public int woundRepairQteSuccesses;
        public int lungRepairQteAttempts;
        public int lungRepairQteSuccesses;
        public float digestiveBarrierUptimeFraction;
        public float respiratoryBarrierUptimeFraction;
        public List<InfectionRecord> infections = new List<InfectionRecord>();
        public List<DeploymentRecord> deployments = new List<DeploymentRecord>();
        public List<HourlyWellnessSample> hourlyWellness = new List<HourlyWellnessSample>();
    }

    [Serializable]
    private sealed class RunRecord
    {
        public string difficulty;
        public string outcome = "In Progress";
        public string startedAtGameTime;
        public float daysSurvived;
        public float supplySpent;
        public int woundRepairQteAttempts;
        public int woundRepairQteSuccesses;
        public int lungRepairQteAttempts;
        public int lungRepairQteSuccesses;
        public float gameTimeSeconds;
        public float digestiveBarrierNonBreachSeconds;
        public float respiratoryBarrierNonBreachSeconds;
        public float digestiveBarrierUptimeFraction;
        public float respiratoryBarrierUptimeFraction;
        public List<InfectionRecord> infections = new List<InfectionRecord>();
        public List<DeploymentRecord> deployments = new List<DeploymentRecord>();
        public List<HourlyWellnessSample> hourlyWellness = new List<HourlyWellnessSample>();
    }

    [SerializeField] private bool exportRunStats;

    private static RunStats instance;
    private readonly Dictionary<InfectionSpawner.InfectionMarker, InfectionRecord> infectionRecords =
        new Dictionary<InfectionSpawner.InfectionMarker, InfectionRecord>();
    private readonly Dictionary<Health, DeploymentRecord> deploymentByUnit =
        new Dictionary<Health, DeploymentRecord>();
    private readonly HashSet<Health> lostUnits = new HashSet<Health>();
    private RunRecord run = new RunRecord();
    private DayCounterUI dayCounter;
    private WellnessManager wellnessManager;
    private BarrierHealth[] barriers = Array.Empty<BarrierHealth>();
    private float runStartGameMinutes;
    private bool runStarted;
    private bool runEnded;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private Vector2 debugScrollPosition;
#endif

    /// <summary>Gets the active run-statistics recorder.</summary>
    public static RunStats Instance => instance;

    /// <summary>Returns a detached snapshot of the recorded run values.</summary>
    public RatingSnapshot GetRatingSnapshot()
    {
        InitializeRunClock();
        RatingSnapshot snapshot = new RatingSnapshot
        {
            daysSurvived = Mathf.Max(0f, (GetGameTimeMinutes() - runStartGameMinutes) / MinutesPerGameDay),
            finalWellness = wellnessManager != null ? wellnessManager.CurrentWellness : 0f,
            supplySpent = run.supplySpent,
            woundRepairQteAttempts = run.woundRepairQteAttempts,
            woundRepairQteSuccesses = run.woundRepairQteSuccesses,
            lungRepairQteAttempts = run.lungRepairQteAttempts,
            lungRepairQteSuccesses = run.lungRepairQteSuccesses,
            digestiveBarrierUptimeFraction = run.digestiveBarrierUptimeFraction,
            respiratoryBarrierUptimeFraction = run.respiratoryBarrierUptimeFraction
        };

        for (int index = 0; index < run.infections.Count; index++)
        {
            InfectionRecord source = run.infections[index];
            snapshot.infections.Add(new InfectionRecord
            {
                pathogenType = source.pathogenType,
                pathogenName = source.pathogenName,
                spawnGameTime = source.spawnGameTime,
                spawnGameMinutes = source.spawnGameMinutes,
                firstDeploymentGameTime = source.firstDeploymentGameTime,
                firstDeploymentGameMinutes = source.firstDeploymentGameMinutes,
                escalated = source.escalated,
                daysUnresolved = source.daysUnresolved,
                resolved = source.resolved
            });
        }

        for (int index = 0; index < run.deployments.Count; index++)
        {
            DeploymentRecord source = run.deployments[index];
            snapshot.deployments.Add(new DeploymentRecord
            {
                unitType = source.unitType,
                pathogenType = source.pathogenType,
                matched = source.matched,
                unitCount = source.unitCount,
                unitsLost = source.unitsLost
            });
        }

        for (int index = 0; index < run.hourlyWellness.Count; index++)
        {
            HourlyWellnessSample source = run.hourlyWellness[index];
            snapshot.hourlyWellness.Add(new HourlyWellnessSample
            {
                day = source.day,
                hour = source.hour,
                wellness = source.wellness
            });
        }

        return snapshot;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
        dayCounter = FindFirstObjectByType<DayCounterUI>();
        wellnessManager = FindFirstObjectByType<WellnessManager>();
        RefreshBarriers();
    }

    private void OnEnable()
    {
        if (instance != this)
            return;

        if (dayCounter == null)
            dayCounter = FindFirstObjectByType<DayCounterUI>();
        if (wellnessManager == null)
            wellnessManager = FindFirstObjectByType<WellnessManager>();

        if (dayCounter != null)
            dayCounter.OnHourAdvanced += HandleHourAdvanced;
        if (wellnessManager != null)
        {
            wellnessManager.OnGameWon += HandleGameWon;
            wellnessManager.OnGameLost += HandleGameLost;
        }
        LungInfectionResponse.RepairQTEStateChanged += HandleLungRepairStateChanged;
    }

    private void Start()
    {
        if (instance != this)
            return;

        InitializeRunClock();
    }

    private void InitializeRunClock()
    {
        if (runStarted)
            return;

        if (dayCounter == null)
            dayCounter = FindFirstObjectByType<DayCounterUI>();
        if (wellnessManager == null)
            wellnessManager = FindFirstObjectByType<WellnessManager>();
        run.difficulty = DifficultySettings.CurrentStats != null
            ? DifficultySettings.CurrentStats.difficultyName
            : "Unknown";
        runStartGameMinutes = GetGameTimeMinutes();
        run.startedAtGameTime = FormatGameTime(runStartGameMinutes);
        runStarted = true;
        RefreshBarriers();
    }

    private void OnDisable()
    {
        if (dayCounter != null)
            dayCounter.OnHourAdvanced -= HandleHourAdvanced;
        if (wellnessManager != null)
        {
            wellnessManager.OnGameWon -= HandleGameWon;
            wellnessManager.OnGameLost -= HandleGameLost;
        }
        LungInfectionResponse.RepairQTEStateChanged -= HandleLungRepairStateChanged;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (!runStarted || runEnded)
            return;

        float gameDelta = GameplaySpeed.DeltaTime;
        if (gameDelta <= 0f)
            return;

        run.gameTimeSeconds += gameDelta;
        if (barriers == null || barriers.Length == 0)
            RefreshBarriers();

        BarrierHealth digestiveBarrier = FindBarrier(DigestiveBarrierName);
        if (digestiveBarrier != null && !digestiveBarrier.IsBreached)
            run.digestiveBarrierNonBreachSeconds += gameDelta;

        BarrierHealth respiratoryBarrier = FindBarrier(RespiratoryBarrierName);
        if (respiratoryBarrier != null && !respiratoryBarrier.IsBreached)
            run.respiratoryBarrierNonBreachSeconds += gameDelta;

        RefreshBarrierFractions();
        run.daysSurvived = Mathf.Max(0f, (GetGameTimeMinutes() - runStartGameMinutes) / MinutesPerGameDay);
    }

    /// <summary>Records a successfully created infection marker and its game-clock spawn time.</summary>
    public static void RecordInfectionSpawn(InfectionSpawner.InfectionMarker marker)
    {
        if (instance == null || marker == null || marker.infection == null || instance.infectionRecords.ContainsKey(marker))
            return;

        float gameMinutes = instance.GetGameTimeMinutes();
        InfectionRecord record = new InfectionRecord
        {
            pathogenType = marker.infection.pathogenType.ToString(),
            pathogenName = marker.infection.displayName,
            spawnGameTime = FormatGameTime(gameMinutes),
            spawnGameMinutes = gameMinutes,
            escalated = marker.wasBarrierMutated,
            daysUnresolved = Mathf.Max(0, marker.unresolvedDays)
        };
        instance.infectionRecords.Add(marker, record);
        instance.run.infections.Add(record);
    }

    /// <summary>Records a successful unit deployment and associates its living units with the deployment.</summary>
    public static void RecordDeployment(string unitType, InfectionSpawner.InfectionMarker marker, IEnumerable<GameObject> units)
    {
        if (instance == null)
            return;

        if (marker != null && marker.infection != null)
            RecordInfectionSpawn(marker);

        string pathogenType = marker != null && marker.infection != null
            ? marker.infection.pathogenType.ToString()
            : "None";
        bool matched = marker != null && marker.infection != null && IsCorrectResponder(unitType, marker.infection.correctResponder);
        DeploymentRecord deployment = new DeploymentRecord
        {
            unitType = unitType ?? "Unknown",
            pathogenType = pathogenType,
            matched = matched
        };
        instance.run.deployments.Add(deployment);

        if (marker != null && instance.infectionRecords.TryGetValue(marker, out InfectionRecord infectionRecord) &&
            infectionRecord.firstDeploymentGameMinutes < 0f)
        {
            float gameMinutes = instance.GetGameTimeMinutes();
            infectionRecord.firstDeploymentGameMinutes = gameMinutes;
            infectionRecord.firstDeploymentGameTime = FormatGameTime(gameMinutes);
        }

        if (units == null)
            return;
        foreach (GameObject unit in units)
        {
            if (unit == null)
                continue;
            Health health = unit.GetComponent<Health>();
            if (health != null)
            {
                deployment.unitCount++;
                instance.deploymentByUnit[health] = deployment;
            }
        }
    }

    /// <summary>Updates the unresolved-day count when the existing daily infection pass increments it.</summary>
    public static void RecordUnresolvedDay(InfectionSpawner.InfectionMarker marker)
    {
        if (instance == null || marker == null)
            return;
        RecordInfectionSpawn(marker);
        if (instance.infectionRecords.TryGetValue(marker, out InfectionRecord record))
            record.daysUnresolved = Mathf.Max(0, marker.unresolvedDays);
    }

    public static void RecordInfectionRemoved(InfectionSpawner.InfectionMarker marker, bool resolved)
    {
        if (instance == null || marker == null)
            return;
        RecordInfectionSpawn(marker);
        if (instance.infectionRecords.TryGetValue(marker, out InfectionRecord record))
        {
            record.daysUnresolved = Mathf.Max(0, marker.unresolvedDays);
            record.resolved = resolved;
        }
    }

    /// <summary>Records one accepted wound repair QTE attempt.</summary>
    public static void RecordWoundRepairAttempt()
    {
        if (instance != null && !instance.runEnded)
            instance.run.woundRepairQteAttempts++;
    }

    /// <summary>Records one completed wound repair QTE.</summary>
    public static void RecordWoundRepairSuccess()
    {
        if (instance != null && !instance.runEnded)
            instance.run.woundRepairQteSuccesses++;
    }

    /// <summary>Records one completed lung repair QTE.</summary>
    public static void RecordLungRepairSuccess()
    {
        if (instance != null && !instance.runEnded)
            instance.run.lungRepairQteSuccesses++;
    }

    /// <summary>Records the amount actually subtracted from the current supply balance.</summary>
    public static void RecordSupplySpent(float amount)
    {
        if (instance != null && !instance.runEnded && amount > 0f)
            instance.run.supplySpent += amount;
    }

    /// <summary>Records a deployed unit's lethal damage as a loss for its deployment.</summary>
    public static void RecordUnitLost(Health unit)
    {
        if (instance == null || unit == null || !instance.lostUnits.Add(unit))
            return;
        if (instance.deploymentByUnit.TryGetValue(unit, out DeploymentRecord deployment))
            deployment.unitsLost++;
    }

    private static bool IsCorrectResponder(string unitType, InfectionCorrectResponder correctResponder)
    {
        if (string.Equals(unitType, "Neutrophil", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(unitType, "Macrophage", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(unitType, "WBC", StringComparison.OrdinalIgnoreCase))
            return correctResponder == InfectionCorrectResponder.WBC;
        if (string.Equals(unitType, "T Cell", StringComparison.OrdinalIgnoreCase))
            return correctResponder == InfectionCorrectResponder.KillerT;
        if (string.Equals(unitType, "B Cell", StringComparison.OrdinalIgnoreCase))
            return correctResponder == InfectionCorrectResponder.BCell;
        if (string.Equals(unitType, "Basophil", StringComparison.OrdinalIgnoreCase))
            return correctResponder == InfectionCorrectResponder.Basophil;
        if (string.Equals(unitType, "Eosinophil", StringComparison.OrdinalIgnoreCase))
            return correctResponder == InfectionCorrectResponder.Eosinophil;
        return false;
    }

    private void HandleHourAdvanced(int day, int hour)
    {
        if (runEnded || wellnessManager == null)
            return;

        run.hourlyWellness.Add(new HourlyWellnessSample
        {
            day = day,
            hour = hour,
            wellness = wellnessManager.CurrentWellness
        });
    }

    private void HandleLungRepairStateChanged(LungInfectionResponse response, bool isInProgress)
    {
        if (response != null && isInProgress && response.HasPendingQte && !runEnded)
            run.lungRepairQteAttempts++;
    }

    private void HandleGameWon(WellnessRunResult result)
    {
        FinishRun(result.ToString());
    }

    private void HandleGameLost(WellnessRunResult result)
    {
        FinishRun(result.ToString());
    }

    private void FinishRun(string outcome)
    {
        if (runEnded)
            return;

        InitializeRunClock();
        runEnded = true;
        run.outcome = outcome;
        run.daysSurvived = Mathf.Max(0f, (GetGameTimeMinutes() - runStartGameMinutes) / MinutesPerGameDay);
        RefreshBarrierFractions();
        Debug.Log("[RunStats] " + BuildSummary());

        if (!exportRunStats)
            return;

        try
        {
            string path = Path.Combine(Application.persistentDataPath, ExportFileName);
            File.AppendAllText(path, JsonUtility.ToJson(run) + Environment.NewLine);
            Debug.Log("[RunStats] Appended run statistics to " + path);
        }
        catch (Exception exception)
        {
            Debug.LogError("[RunStats] Could not export run statistics: " + exception.Message);
        }
    }

    private void RefreshBarrierFractions()
    {
        if (run.gameTimeSeconds <= 0f)
        {
            run.digestiveBarrierUptimeFraction = 0f;
            run.respiratoryBarrierUptimeFraction = 0f;
            return;
        }

        run.digestiveBarrierUptimeFraction = Mathf.Clamp01(run.digestiveBarrierNonBreachSeconds / run.gameTimeSeconds);
        run.respiratoryBarrierUptimeFraction = Mathf.Clamp01(run.respiratoryBarrierNonBreachSeconds / run.gameTimeSeconds);
    }

    private void RefreshBarriers()
    {
        barriers = FindObjectsByType<BarrierHealth>(FindObjectsSortMode.None);
    }

    private BarrierHealth FindBarrier(string barrierName)
    {
        if (barriers == null)
            return null;
        foreach (BarrierHealth barrier in barriers)
        {
            if (barrier != null && barrier.isActiveAndEnabled &&
                string.Equals(barrier.gameObject.name, barrierName, StringComparison.OrdinalIgnoreCase))
                return barrier;
        }
        return null;
    }

    private float GetGameTimeMinutes()
    {
        if (dayCounter == null)
            dayCounter = FindFirstObjectByType<DayCounterUI>();
        if (dayCounter == null)
            return 0f;
        return dayCounter.CurrentDay * MinutesPerGameDay + dayCounter.CurrentHour * 60f + dayCounter.CurrentMinute;
    }

    private static string FormatGameTime(float gameMinutes)
    {
        int day = Mathf.FloorToInt(gameMinutes / MinutesPerGameDay);
        int minuteOfDay = Mathf.FloorToInt(gameMinutes - day * MinutesPerGameDay);
        int hour = minuteOfDay / 60;
        int minute = minuteOfDay % 60;
        return $"Day {day}, {hour:00}:{minute:00}";
    }

    private string BuildSummary()
    {
        int unresolvedCount = 0;
        for (int index = 0; index < run.infections.Count; index++)
        {
            if (!run.infections[index].resolved)
                unresolvedCount++;
        }

        StringBuilder summary = new StringBuilder();
        summary.Append("outcome=").Append(run.outcome)
            .Append(", difficulty=").Append(run.difficulty)
            .Append(", daysSurvived=").Append(run.daysSurvived.ToString("0.##"))
            .Append(", infections=").Append(run.infections.Count)
            .Append(" (unresolved ").Append(unresolvedCount).Append(')')
            .Append(", deployments=").Append(run.deployments.Count)
            .Append(", supplySpent=").Append(run.supplySpent.ToString("0.##"))
            .Append(", woundQTE=").Append(run.woundRepairQteSuccesses).Append('/').Append(run.woundRepairQteAttempts)
            .Append(", lungQTE=").Append(run.lungRepairQteSuccesses).Append('/').Append(run.lungRepairQteAttempts)
            .Append(", digestiveUptime=").Append((run.digestiveBarrierUptimeFraction * 100f).ToString("0.##")).Append('%')
            .Append(", respiratoryUptime=").Append((run.respiratoryBarrierUptimeFraction * 100f).ToString("0.##")).Append('%')
            .Append(", wellnessHourlySamples=").Append(run.hourlyWellness.Count)
            .Append(", infectionRecords=[");
        for (int index = 0; index < run.infections.Count; index++)
        {
            InfectionRecord infection = run.infections[index];
            if (index > 0)
                summary.Append(';');
            summary.Append(infection.pathogenName).Append('/').Append(infection.pathogenType)
                .Append(" spawned=").Append(infection.spawnGameTime)
                .Append(" firstDeployment=").Append(string.IsNullOrEmpty(infection.firstDeploymentGameTime) ? "none" : infection.firstDeploymentGameTime)
                .Append(" escalated=").Append(infection.escalated)
                .Append(" unresolvedDays=").Append(infection.daysUnresolved)
                .Append(" resolved=").Append(infection.resolved);
        }
        summary.Append("], deploymentRecords=[");
        for (int index = 0; index < run.deployments.Count; index++)
        {
            DeploymentRecord deployment = run.deployments[index];
            if (index > 0)
                summary.Append(';');
            summary.Append(deployment.unitType).Append('/').Append(deployment.pathogenType)
                .Append(" matched=").Append(deployment.matched)
                .Append(" unitsLost=").Append(deployment.unitsLost);
        }
        summary.Append("], hourlyWellness=[");
        for (int index = 0; index < run.hourlyWellness.Count; index++)
        {
            HourlyWellnessSample sample = run.hourlyWellness[index];
            if (index > 0)
                summary.Append(';');
            summary.Append(sample.day).Append('@').Append(sample.hour.ToString("00"))
                .Append('=').Append(sample.wellness.ToString("0.##"));
        }
        summary.Append(']');
        return summary.ToString();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (instance != this)
            return;

        GUILayout.BeginArea(new Rect(12f, 12f, 610f, Mathf.Max(180f, Screen.height - 24f)), GUI.skin.box);
        GUILayout.Label("RUN STATS — DEBUG");
        GUILayout.Label(BuildSummary());
        GUILayout.Label("Infections: pathogen | spawned | first deployment | escalated | unresolved days | status");
        debugScrollPosition = GUILayout.BeginScrollView(debugScrollPosition);
        for (int index = 0; index < run.infections.Count; index++)
        {
            InfectionRecord infection = run.infections[index];
            string firstDeployment = string.IsNullOrEmpty(infection.firstDeploymentGameTime)
                ? "not deployed"
                : infection.firstDeploymentGameTime;
            GUILayout.Label($"{infection.pathogenName} ({infection.pathogenType}) | {infection.spawnGameTime} | {firstDeployment} | " +
                $"escalated={infection.escalated} | unresolvedDays={infection.daysUnresolved} | " +
                (infection.resolved ? "resolved" : "unresolved"));
        }
        GUILayout.Label("Deployments: unit | pathogen | matched | units lost");
        for (int index = 0; index < run.deployments.Count; index++)
        {
            DeploymentRecord deployment = run.deployments[index];
            GUILayout.Label($"{deployment.unitType} | {deployment.pathogenType} | matched={deployment.matched} | lost={deployment.unitsLost}");
        }
        GUILayout.Label("Hourly wellness samples: " + run.hourlyWellness.Count);
        for (int index = 0; index < run.hourlyWellness.Count; index++)
        {
            HourlyWellnessSample sample = run.hourlyWellness[index];
            GUILayout.Label($"Day {sample.day}, {sample.hour:00}:00 — {sample.wellness:0.##}");
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
#endif
}
