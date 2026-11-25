using UnityEngine;

/// <summary>Applies the selected graphics profile's budget to short-lived combat and repair visual effects.</summary>
public static class GraphicsQualityEffects
{
    private const string GraphicsQualityPreferenceKey = "GraphicsQuality";
    private const int DefaultGraphicsQuality = 1;
    private const int LowQualityIndex = 0;
    private const int NormalQualityIndex = 1;
    private const int MediumQualityIndex = 2;
    private const int HighQualityIndex = 3;

    private static readonly float[] ParticleBudgetMultipliers = { 0.35f, 0.6f, 0.82f, 1f };
    private static readonly int[] RepairDroneCounts = { 2, 3, 4, 4 };

    /// <summary>Reduces particle capacity and continuous emission on an instantiated effect for the active profile.</summary>
    public static void ApplyTo(GameObject effectRoot)
    {
        if (effectRoot == null)
            return;

        float multiplier = ParticleBudgetMultipliers[GetCurrentQualityIndex()];
        ParticleSystem[] particleSystems = effectRoot.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particleSystem in particleSystems)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(main.maxParticles * multiplier));

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTimeMultiplier *= multiplier;
            emission.rateOverDistanceMultiplier *= multiplier;
        }
    }

    /// <summary>Returns the number of animated platelet drones for the active wound-repair graphics profile.</summary>
    public static int GetRepairDroneCount()
    {
        return RepairDroneCounts[GetCurrentQualityIndex()];
    }

    private static int GetCurrentQualityIndex()
    {
        return Mathf.Clamp(
            PlayerPrefs.GetInt(GraphicsQualityPreferenceKey, DefaultGraphicsQuality),
            LowQualityIndex,
            HighQualityIndex);
    }
}
