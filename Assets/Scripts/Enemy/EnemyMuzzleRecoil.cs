using UnityEngine;

/// <summary>
/// Pulls a muzzle/fire-point along a local axis briefly, then eases it to rest.
/// </summary>
public sealed class EnemyMuzzleRecoil
{
    Transform muzzle;
    Vector3 restLocalPosition;
    Vector3 kickOffset;
    float recoverDuration;
    float timer;
    bool bound;

    public void Bind(Transform firePoint)
    {
        if (firePoint == null || bound)
            return;

        muzzle = firePoint;
        restLocalPosition = firePoint.localPosition;
        bound = true;
    }

    /// <param name="localDirection">Kick direction in the fire point's local space. Defaults to back (-Z).</param>
    public void Play(float distance, float duration, Vector3 localDirection)
    {
        if (!bound || muzzle == null)
            return;

        if (localDirection.sqrMagnitude < 0.0001f)
            localDirection = Vector3.back;

        kickOffset = localDirection.normalized * Mathf.Max(0.01f, distance);
        recoverDuration = Mathf.Max(0.02f, duration);
        timer = recoverDuration;
        muzzle.localPosition = restLocalPosition + kickOffset;
    }

    public void Play(float distance, float duration)
    {
        Play(distance, duration, Vector3.back);
    }

    public void Tick(float deltaTime)
    {
        if (!bound || muzzle == null || timer <= 0f)
            return;

        timer -= deltaTime;
        float t = 1f - Mathf.Clamp01(timer / recoverDuration);
        // Ease-out return to rest.
        float eased = 1f - (1f - t) * (1f - t);
        muzzle.localPosition = Vector3.Lerp(restLocalPosition + kickOffset, restLocalPosition, eased);

        if (timer <= 0f)
            muzzle.localPosition = restLocalPosition;
    }
}
