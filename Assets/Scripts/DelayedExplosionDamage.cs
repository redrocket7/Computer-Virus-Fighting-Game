using System.Collections;
using UnityEngine;

/// <summary>
/// Delays explosion damage to the player so the blast can be seen/dodged.
/// Non-player targets still take damage immediately.
/// </summary>
public static class DelayedExplosionDamage
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        runner = null;
    }

    /// <summary>Time for the blast wave to reach the edge of the radius.</summary>
    const float ExpandDuration = 0.4f;

    /// <summary>Even point-blank hits wait at least this long.</summary>
    const float MinDelay = 0.12f;

    static Runner runner;

    public static void Apply(IDamageable target, float damage, Vector3 origin, float radius)
    {
        if (target == null || damage <= 0f)
            return;

        if (target is PlayerController player)
        {
            float delay = ComputePlayerDelay(origin, player.transform.position, radius);
            EnsureRunner().Schedule(player, damage, origin, Mathf.Max(0.01f, radius), delay);
            return;
        }

        target.TakeDamage(damage);
    }

    static float ComputePlayerDelay(Vector3 origin, Vector3 targetPosition, float radius)
    {
        Vector3 offset = targetPosition - origin;
        offset.y = 0f;
        float normalized = Mathf.Clamp01(offset.magnitude / Mathf.Max(0.01f, radius));

        // Inverse of EaseOut growth: wave reaches this distance at this normalized time.
        float easeT = 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - normalized));
        return Mathf.Max(MinDelay, ExpandDuration * easeT);
    }

    static Runner EnsureRunner()
    {
        if (runner != null)
            return runner;

        var go = new GameObject("DelayedExplosionDamage");
        Object.DontDestroyOnLoad(go);
        runner = go.AddComponent<Runner>();
        return runner;
    }

    sealed class Runner : MonoBehaviour
    {
        public void Schedule(PlayerController player, float damage, Vector3 origin, float radius, float delay)
        {
            StartCoroutine(ApplyAfterDelay(player, damage, origin, radius, delay));
        }

        IEnumerator ApplyAfterDelay(
            PlayerController player,
            float damage,
            Vector3 origin,
            float radius,
            float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            if (player == null)
                yield break;

            Vector3 offset = player.transform.position - origin;
            offset.y = 0f;
            if (offset.sqrMagnitude > radius * radius)
                yield break;

            player.TakeDamage(damage);
        }
    }
}
