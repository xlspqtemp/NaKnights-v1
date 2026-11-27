using System;
using UnityEngine;

/// <summary>Calculates a weighted, survival-capped run rating from recorded telemetry.</summary>
public static class RatingCalculator
{
    /// <summary>Contains the five component scores and the final run-rating values.</summary>
    public sealed class RatingResult
    {
        public float WellnessScore { get; internal set; }
        public float ResponsivenessScore { get; internal set; }
        public float NeglectScore { get; internal set; }
        public float EfficiencyScore { get; internal set; }
        public float DefenseScore { get; internal set; }
        public float PerformanceScore { get; internal set; }
        public string Letter { get; internal set; }
        public int TotalScore { get; internal set; }
        public string WeakestComponent { get; internal set; }
    }

    private static RatingConfig defaultConfig;

    /// <summary>Evaluates the current run using default rating configuration values.</summary>
    public static RatingResult Evaluate(RunStats runStats, string difficulty, float targetDays)
    {
        return Evaluate(runStats, difficulty, targetDays, GetDefaultConfig());
    }

    /// <summary>Evaluates the current run using the supplied tunable rating configuration.</summary>
    public static RatingResult Evaluate(RunStats runStats, string difficulty, float targetDays, RatingConfig config)
    {
        return Evaluate(runStats != null ? runStats.GetRatingSnapshot() : null, difficulty, targetDays, config);
    }

    /// <summary>Evaluates a detached run snapshot, also used by the RatingConfig sample menu.</summary>
    public static RatingResult Evaluate(RunStats.RatingSnapshot snapshot, string difficulty, float targetDays, RatingConfig config)
    {
        if (config == null)
            config = GetDefaultConfig();

        float wellnessScore = CalculateWellnessScore(snapshot);
        float responsivenessScore = CalculateResponsivenessScore(snapshot, config);
        float neglectScore = CalculateNeglectScore(snapshot, config);
        float efficiencyScore = CalculateEfficiencyScore(snapshot);
        float defenseScore = CalculateDefenseScore(snapshot);

        float totalWeight = Mathf.Max(0f,
            config.wellnessWeight + config.responsivenessWeight + config.neglectWeight +
            config.efficiencyWeight + config.defenseWeight);
        float weightedScore = wellnessScore * Mathf.Max(0f, config.wellnessWeight) +
            responsivenessScore * Mathf.Max(0f, config.responsivenessWeight) +
            neglectScore * Mathf.Max(0f, config.neglectWeight) +
            efficiencyScore * Mathf.Max(0f, config.efficiencyWeight) +
            defenseScore * Mathf.Max(0f, config.defenseWeight);
        float performanceScore = totalWeight > 0f
            ? Mathf.Clamp01(weightedScore / totalWeight) * 100f
            : 0f;

        float daysSurvived = snapshot != null ? Mathf.Max(0f, snapshot.daysSurvived) : 0f;
        float effectiveTargetDays = targetDays > 0f ? targetDays : Mathf.Max(1f, config.defaultTargetDays);
        string scoreGrade = GetScoreGrade(performanceScore, config);
        string survivalCap = GetSurvivalGradeCap(daysSurvived, effectiveTargetDays, config);
        string letter = LowerGrade(scoreGrade, survivalCap);
        float difficultyMultiplier = GetDifficultyMultiplier(difficulty, config);
        int totalScore = Mathf.RoundToInt(performanceScore * 10f * difficultyMultiplier) +
            Mathf.RoundToInt(daysSurvived * config.dayBonus);
        string weakestComponent = GetWeakestComponent(
            wellnessScore,
            responsivenessScore,
            neglectScore,
            efficiencyScore,
            defenseScore);

        return new RatingResult
        {
            WellnessScore = wellnessScore,
            ResponsivenessScore = responsivenessScore,
            NeglectScore = neglectScore,
            EfficiencyScore = efficiencyScore,
            DefenseScore = defenseScore,
            PerformanceScore = performanceScore,
            Letter = letter,
            TotalScore = totalScore,
            WeakestComponent = weakestComponent
        };
    }

    private static float CalculateWellnessScore(RunStats.RatingSnapshot snapshot)
    {
        if (snapshot == null)
            return 0f;

        float finalWellness = Mathf.Clamp01(snapshot.finalWellness / 100f);
        float averageWellness = finalWellness;
        if (snapshot.hourlyWellness != null && snapshot.hourlyWellness.Count > 0)
        {
            float wellnessSum = 0f;
            for (int index = 0; index < snapshot.hourlyWellness.Count; index++)
                wellnessSum += Mathf.Clamp(snapshot.hourlyWellness[index].wellness, 0f, 100f);
            averageWellness = wellnessSum / snapshot.hourlyWellness.Count / 100f;
        }

        return Mathf.Clamp01(0.6f * averageWellness + 0.4f * finalWellness);
    }

    private static float CalculateResponsivenessScore(RunStats.RatingSnapshot snapshot, RatingConfig config)
    {
        if (snapshot == null || snapshot.infections == null || snapshot.infections.Count == 0)
            return 1f;

        float totalScore = 0f;
        float fullMarksSeconds = Mathf.Max(0f, config.fullMarksResponseSeconds);
        float zeroMarksSeconds = Mathf.Max(fullMarksSeconds, config.zeroMarksResponseSeconds);
        for (int index = 0; index < snapshot.infections.Count; index++)
        {
            RunStats.InfectionRecord infection = snapshot.infections[index];
            float infectionScore = 0f;
            if (infection != null && infection.firstDeploymentGameMinutes >= 0f)
            {
                // RunStats stores game-minute precision, so this is a minute-resolution estimate.
                float responseSeconds = Mathf.Max(0f,
                    (infection.firstDeploymentGameMinutes - infection.spawnGameMinutes) * 60f);
                if (responseSeconds <= fullMarksSeconds)
                    infectionScore = 1f;
                else if (responseSeconds < zeroMarksSeconds && zeroMarksSeconds > fullMarksSeconds)
                    infectionScore = 1f - (responseSeconds - fullMarksSeconds) / (zeroMarksSeconds - fullMarksSeconds);
            }
            totalScore += Mathf.Clamp01(infectionScore);
        }

        return Mathf.Clamp01(totalScore / snapshot.infections.Count);
    }

    private static float CalculateNeglectScore(RunStats.RatingSnapshot snapshot, RatingConfig config)
    {
        if (snapshot == null || snapshot.infections == null || snapshot.infections.Count == 0)
            return 1f;

        float unresolvedInfectionDays = 0f;
        int escalations = 0;
        for (int index = 0; index < snapshot.infections.Count; index++)
        {
            RunStats.InfectionRecord infection = snapshot.infections[index];
            if (infection == null)
                continue;
            unresolvedInfectionDays += Mathf.Max(0, infection.daysUnresolved);
            if (infection.escalated)
                escalations++;
        }

        float denominator = Mathf.Max(0f, config.neglectScalePerInfection) * snapshot.infections.Count;
        if (denominator <= 0f)
            return unresolvedInfectionDays <= 0f && escalations == 0 ? 1f : 0f;
        float neglect = (unresolvedInfectionDays + Mathf.Max(0f, config.neglectScalePerEscalation) * escalations) / denominator;
        return 1f - Mathf.Clamp01(neglect);
    }

    private static float CalculateEfficiencyScore(RunStats.RatingSnapshot snapshot)
    {
        if (snapshot == null || snapshot.deployments == null)
            return 0f;

        int deployedUnitCount = 0;
        int matchedUnitCount = 0;
        int lostUnitCount = 0;
        for (int index = 0; index < snapshot.deployments.Count; index++)
        {
            RunStats.DeploymentRecord deployment = snapshot.deployments[index];
            if (deployment == null)
                continue;
            int unitCount = Mathf.Max(0, deployment.unitCount);
            deployedUnitCount += unitCount;
            lostUnitCount += Mathf.Clamp(deployment.unitsLost, 0, unitCount);
            if (deployment.matched)
                matchedUnitCount += unitCount;
        }

        if (deployedUnitCount == 0)
            return 0f;

        float matchedUnitRate = (float)matchedUnitCount / deployedUnitCount;
        float unitSurvivalRate = 1f - (float)lostUnitCount / deployedUnitCount;
        return Mathf.Clamp01(0.5f * matchedUnitRate + 0.5f * unitSurvivalRate);
    }

    private static float CalculateDefenseScore(RunStats.RatingSnapshot snapshot)
    {
        if (snapshot == null)
            return 0f;

        int qteAttempts = Mathf.Max(0, snapshot.woundRepairQteAttempts) + Mathf.Max(0, snapshot.lungRepairQteAttempts);
        int qteSuccesses = Mathf.Clamp(snapshot.woundRepairQteSuccesses, 0, snapshot.woundRepairQteAttempts) +
            Mathf.Clamp(snapshot.lungRepairQteSuccesses, 0, snapshot.lungRepairQteAttempts);
        float qteSuccessRate = qteAttempts == 0 ? 1f : (float)qteSuccesses / qteAttempts;
        float averageBarrierUptime = 0.5f * Mathf.Clamp01(snapshot.digestiveBarrierUptimeFraction) +
            0.5f * Mathf.Clamp01(snapshot.respiratoryBarrierUptimeFraction);
        return Mathf.Clamp01(0.5f * qteSuccessRate + 0.5f * averageBarrierUptime);
    }

    private static string GetScoreGrade(float score, RatingConfig config)
    {
        if (score >= config.sGradeThreshold)
            return "S";
        if (score >= config.aGradeThreshold)
            return "A";
        if (score >= config.bGradeThreshold)
            return "B";
        if (score >= config.cGradeThreshold)
            return "C";
        return "D";
    }

    private static string GetSurvivalGradeCap(float daysSurvived, float targetDays, RatingConfig config)
    {
        float survivalRate = targetDays > 0f ? Mathf.Clamp01(daysSurvived / targetDays) : 0f;
        if (survivalRate >= config.sSurvivalCap)
            return "S";
        if (survivalRate >= config.aSurvivalCap)
            return "A";
        if (survivalRate >= config.bSurvivalCap)
            return "B";
        if (survivalRate >= config.cSurvivalCap)
            return "C";
        return "D";
    }

    private static string LowerGrade(string firstGrade, string secondGrade)
    {
        return GradeRank(firstGrade) <= GradeRank(secondGrade) ? firstGrade : secondGrade;
    }

    private static int GradeRank(string grade)
    {
        switch (grade)
        {
            case "S": return 4;
            case "A": return 3;
            case "B": return 2;
            case "C": return 1;
            default: return 0;
        }
    }

    private static float GetDifficultyMultiplier(string difficulty, RatingConfig config)
    {
        if (string.Equals(difficulty, "Easy", StringComparison.OrdinalIgnoreCase))
            return config.easyDifficultyMultiplier;
        if (string.Equals(difficulty, "Normal", StringComparison.OrdinalIgnoreCase))
            return config.normalDifficultyMultiplier;
        if (string.Equals(difficulty, "Medium", StringComparison.OrdinalIgnoreCase))
            return config.mediumDifficultyMultiplier;
        if (string.Equals(difficulty, "Hard", StringComparison.OrdinalIgnoreCase))
            return config.hardDifficultyMultiplier;
        return 1f;
    }

    private static string GetWeakestComponent(float wellness, float responsiveness, float neglect, float efficiency, float defense)
    {
        string weakest = "Wellness";
        float lowestScore = wellness;
        if (responsiveness < lowestScore)
        {
            weakest = "Responsiveness";
            lowestScore = responsiveness;
        }
        if (neglect < lowestScore)
        {
            weakest = "Neglect";
            lowestScore = neglect;
        }
        if (efficiency < lowestScore)
        {
            weakest = "Efficiency";
            lowestScore = efficiency;
        }
        if (defense < lowestScore)
            weakest = "Defense";
        return weakest;
    }

    private static RatingConfig GetDefaultConfig()
    {
        if (defaultConfig == null)
        {
            defaultConfig = ScriptableObject.CreateInstance<RatingConfig>();
            defaultConfig.hideFlags = HideFlags.HideAndDontSave;
        }
        return defaultConfig;
    }
}
