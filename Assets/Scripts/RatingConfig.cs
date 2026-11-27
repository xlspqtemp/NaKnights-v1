using UnityEngine;

/// <summary>Stores tunable component weights, response scoring, grade cutoffs, survival caps, and difficulty values.</summary>
[CreateAssetMenu(fileName = "RatingConfig", menuName = "Game/Run Rating Config")]
public sealed class RatingConfig : ScriptableObject
{
    [Header("Performance Weights")]
    [Range(0f, 1f)] public float wellnessWeight = 0.25f;
    [Range(0f, 1f)] public float responsivenessWeight = 0.25f;
    [Range(0f, 1f)] public float neglectWeight = 0.20f;
    [Range(0f, 1f)] public float efficiencyWeight = 0.15f;
    [Range(0f, 1f)] public float defenseWeight = 0.15f;

    [Header("Response Time Bounds (Game Seconds)")]
    [Min(0f)] public float fullMarksResponseSeconds = 20f;
    [Min(0f)] public float zeroMarksResponseSeconds = 90f;

    [Header("Neglect Scale")]
    [Min(0f)] public float neglectScalePerInfection = 3f;
    [Min(0f)] public float neglectScalePerEscalation = 2f;

    [Header("Performance Grade Thresholds")]
    [Range(0f, 100f)] public float sGradeThreshold = 90f;
    [Range(0f, 100f)] public float aGradeThreshold = 75f;
    [Range(0f, 100f)] public float bGradeThreshold = 60f;
    [Range(0f, 100f)] public float cGradeThreshold = 40f;

    [Header("Difficulty Multipliers")]
    [Min(0f)] public float easyDifficultyMultiplier = 0.8f;
    [Min(0f)] public float normalDifficultyMultiplier = 1f;
    [Min(0f)] public float mediumDifficultyMultiplier = 1.1f;
    [Min(0f)] public float hardDifficultyMultiplier = 1.25f;

    [Header("Survival Grade Caps")]
    [Range(0f, 1f)] public float cSurvivalCap = 0.35f;
    [Range(0f, 1f)] public float bSurvivalCap = 0.60f;
    [Range(0f, 1f)] public float aSurvivalCap = 0.85f;
    [Range(0f, 1f)] public float sSurvivalCap = 1f;
    [Min(1f)] public float defaultTargetDays = 14f;

    [Header("Total Score")]
    [Min(0f)] public float dayBonus = 50f;

#if UNITY_EDITOR
    /// <summary>Calculates and logs sample rating letters for the built-in scenarios.</summary>
    [ContextMenu("Print Sample Rating Letters")]
    private void PrintSampleRatingLetters()
    {
        const float targetDays = 14f;
        PrintSample("Excellent", CreateSample(14f, 98f, 96f, 10f, 0, 0, 5, 0, 5, 5, 1f), targetDays);
        PrintSample("Good", CreateSample(12f, 82f, 76f, 50f, 1, 2, 4, 2, 4, 3, 0.8f), targetDays);
        PrintSample("Average", CreateSample(9f, 68f, 61f, 60f, 2, 2, 3, 4, 3, 2, 0.6f), targetDays);
        PrintSample("Poor", CreateSample(5f, 38f, 32f, 85f, 3, 5, 1, 12, 4, 1, 0.3f), targetDays);
        PrintSample("Terrible", CreateSample(1f, 10f, 5f, 90f, 5, 5, 0, 0, 0, 0, 0.05f), targetDays);
        PrintSample("Died on day 3", CreateSample(2f, 18f, 0f, 90f, 3, 5, 1, 5, 2, 0, 0.1f), targetDays);
    }

    private void PrintSample(string scenario, RunStats.RatingSnapshot snapshot, float targetDays)
    {
        RatingCalculator.RatingResult result = RatingCalculator.Evaluate(snapshot, "Normal", targetDays, this);
        Debug.Log($"[Rating Sample] {scenario}: {result.Letter} (performance {result.PerformanceScore:0.0}, total {result.TotalScore}, weakest {result.WeakestComponent})", this);
    }

    private static RunStats.RatingSnapshot CreateSample(
        float daysSurvived,
        float averageWellness,
        float finalWellness,
        float responseSeconds,
        int unresolvedDaysPerInfection,
        int escalationCount,
        int matchedDeploymentCount,
        int totalUnitsLost,
        int qteAttempts,
        int qteSuccesses,
        float barrierUptime)
    {
        const int infectionCount = 5;
        const int deploymentsCount = 5;
        const int unitsPerDeployment = 5;
        RunStats.RatingSnapshot snapshot = new RunStats.RatingSnapshot
        {
            daysSurvived = daysSurvived,
            finalWellness = finalWellness,
            digestiveBarrierUptimeFraction = barrierUptime,
            respiratoryBarrierUptimeFraction = barrierUptime,
            woundRepairQteAttempts = qteAttempts,
            woundRepairQteSuccesses = qteSuccesses
        };

        for (int infectionIndex = 0; infectionIndex < infectionCount; infectionIndex++)
        {
            bool deployed = infectionIndex < deploymentsCount;
            snapshot.infections.Add(new RunStats.InfectionRecord
            {
                pathogenName = "Sample Pathogen " + (infectionIndex + 1),
                pathogenType = "Bacterial",
                spawnGameMinutes = 0f,
                firstDeploymentGameMinutes = deployed && infectionIndex < matchedDeploymentCount
                    ? responseSeconds / 60f
                    : deployed ? responseSeconds / 60f : -1f,
                escalated = infectionIndex < escalationCount,
                daysUnresolved = unresolvedDaysPerInfection
            });
            snapshot.hourlyWellness.Add(new RunStats.HourlyWellnessSample
            {
                day = infectionIndex + 1,
                hour = 12,
                wellness = averageWellness
            });
        }

        for (int deploymentIndex = 0; deploymentIndex < deploymentsCount; deploymentIndex++)
        {
            int deploymentLosses = Mathf.Clamp(totalUnitsLost - deploymentIndex * unitsPerDeployment, 0, unitsPerDeployment);
            snapshot.deployments.Add(new RunStats.DeploymentRecord
            {
                unitType = "Sample Responder",
                pathogenType = "Bacterial",
                matched = deploymentIndex < matchedDeploymentCount,
                unitCount = unitsPerDeployment,
                unitsLost = deploymentLosses
            });
        }

        return snapshot;
    }
#endif
}
