using System.Collections;
using UnityEngine;

/// <summary>Plays a small fire or ice hit effect only while a supported unit's attack animation is active.</summary>
[DisallowMultipleComponent]
public sealed class UnitAttackImpactEffect : MonoBehaviour
{
    private const string AttackParameter = "Attack";
    private const string MovingParameter = "IsMoving";
    private const string FireEffectResourcePath = "CombatEffects/CFXR4 Sword Hit FIRE (Cross)";
    private const string IceEffectResourcePath = "CombatEffects/CFXR4 Sword Hit ICE (Cross)";
    private const float AnimationWaitTimeoutSeconds = 1.5f;
    private const float MaximumEffectScale = 0.28f;
    private const float MinimumEffectScale = 0.08f;
    private const float EffectScalePerUnitHeight = 0.16f;
    private const float EffectCleanupDelaySeconds = 3f;
    private static readonly int AttackParameterHash = Animator.StringToHash(AttackParameter);
    private static readonly int MovingParameterHash = Animator.StringToHash(MovingParameter);

    /// <summary>Triggers a supported unit's Attack animation and schedules its matching impact effect.</summary>
    public static void TriggerAttackAnimationAndPlayEffect(GameObject attacker, GameObject pathogen)
    {
        if (attacker == null || pathogen == null)
            return;

        Animator animator = attacker.GetComponentInChildren<Animator>(true);
        if (animator == null)
            return;

        animator.enabled = true;
        if (!HasParameter(animator, AttackParameterHash, AnimatorControllerParameterType.Trigger))
            return;

        if (HasParameter(animator, MovingParameterHash, AnimatorControllerParameterType.Bool))
            animator.SetBool(MovingParameterHash, false);
        animator.SetTrigger(AttackParameterHash);
        PlayForTriggeredAttack(attacker, pathogen);
    }

    /// <summary>Schedules the unit-specific impact effect after an attack animation is triggered.</summary>
    public static void PlayForTriggeredAttack(GameObject attacker, GameObject pathogen)
    {
        if (attacker == null || pathogen == null)
            return;

        string resourcePath = GetEffectResourcePath(attacker);
        if (string.IsNullOrEmpty(resourcePath))
            return;

        Animator animator = attacker.GetComponentInChildren<Animator>(true);
        if (animator == null)
            return;

        UnitAttackImpactEffect effectPlayer = attacker.GetComponent<UnitAttackImpactEffect>();
        if (effectPlayer == null)
            effectPlayer = attacker.AddComponent<UnitAttackImpactEffect>();
        effectPlayer.StartCoroutine(effectPlayer.PlayWhenAttackAnimationBegins(animator, pathogen.transform, resourcePath));
    }

    private static string GetEffectResourcePath(GameObject attacker)
    {
        if (attacker.GetComponent<MacrophageScript>() != null)
            return IceEffectResourcePath;
        if (attacker.GetComponent<NeutrophilScript>() != null || attacker.GetComponent<KillerTCellCombatStats>() != null)
            return FireEffectResourcePath;
        return null;
    }

    private static bool HasParameter(Animator animator, int parameterHash, AnimatorControllerParameterType parameterType)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == parameterHash && parameter.type == parameterType)
                return true;
        }

        return false;
    }

    private IEnumerator PlayWhenAttackAnimationBegins(Animator animator, Transform pathogen, string resourcePath)
    {
        float deadline = Time.unscaledTime + AnimationWaitTimeoutSeconds;
        while (animator != null && animator.isActiveAndEnabled && Time.unscaledTime < deadline)
        {
            if (IsAttackAnimationActive(animator))
            {
                GameObject effectPrefab = Resources.Load<GameObject>(resourcePath);
                if (effectPrefab == null)
                    yield break;

                Vector3 targetPosition = pathogen != null ? GetPathogenCenter(pathogen) : transform.position;
                Vector3 direction = targetPosition - transform.position;
                direction.y = 0f;
                Quaternion rotation = direction.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                    : transform.rotation;
                GameObject effect = Instantiate(effectPrefab, targetPosition, rotation);
                float unitHeight = GetRendererBoundsHeight(gameObject);
                float effectScale = Mathf.Clamp(unitHeight * EffectScalePerUnitHeight,
                    MinimumEffectScale, MaximumEffectScale);
                effect.transform.localScale = Vector3.one * effectScale;
                GraphicsQualityEffects.ApplyTo(effect);
                Destroy(effect, EffectCleanupDelaySeconds);
                yield break;
            }

            yield return null;
        }
    }

    private bool IsAttackAnimationActive(Animator animator)
    {
        if (animator.layerCount == 0)
            return false;

        if (IsAttackState(animator.GetCurrentAnimatorStateInfo(0)))
            return true;
        return animator.IsInTransition(0) && IsAttackState(animator.GetNextAnimatorStateInfo(0));
    }

    private bool IsAttackState(AnimatorStateInfo stateInfo)
    {
        if (GetComponent<MacrophageScript>() != null)
        {
            return stateInfo.IsName("Attack") || stateInfo.IsName("Base Layer.Attack") ||
                stateInfo.IsName("Mutant swiping") || stateInfo.IsName("Base Layer.Mutant swiping") ||
                stateInfo.IsName("Cross punch") || stateInfo.IsName("Base Layer.Cross punch");
        }

        return stateInfo.IsName("Attack") || stateInfo.IsName("Base Layer.Attack");
    }

    private static Vector3 GetPathogenCenter(Transform pathogen)
    {
        Collider pathogenCollider = pathogen.GetComponentInChildren<Collider>();
        if (pathogenCollider != null)
            return pathogenCollider.bounds.center;

        Renderer pathogenRenderer = pathogen.GetComponentInChildren<Renderer>();
        return pathogenRenderer != null ? pathogenRenderer.bounds.center : pathogen.position;
    }

    private static float GetRendererBoundsHeight(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return 1f;

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return Mathf.Max(bounds.size.y, 0.1f);
    }
}
