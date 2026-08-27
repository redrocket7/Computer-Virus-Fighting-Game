using UnityEngine;

/// <summary>
/// Enemy missile that continuously steers toward a target (usually the player).
/// </summary>
public class HomingMissile : Projectile
{
    [Header("Homing")]
    [SerializeField] float turnRateDegrees = 160f;
    [SerializeField] float seekHeight = 0.5f;
    [Tooltip("Seconds before steering begins. Lets the missile clear the muzzle first.")]
    [SerializeField] float armDelay = 0.08f;

    Transform seekTarget;
    float armTimer;

    public void LaunchAsEnemyHoming(Vector3 fireDirection, Transform source, Transform target)
    {
        seekTarget = target;
        armTimer = Mathf.Max(0f, armDelay);
        LaunchAsEnemy(fireDirection, source);
    }

    protected override void TickMovement(float deltaTime)
    {
        if (armTimer > 0f)
            armTimer -= deltaTime;
        else
            SteerTowardTarget(deltaTime);

        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        base.TickMovement(deltaTime);
    }

    void SteerTowardTarget(float deltaTime)
    {
        if (seekTarget == null)
            return;

        Vector3 toTarget = seekTarget.position + Vector3.up * seekHeight - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f)
            return;

        Vector3 desired = toTarget.normalized;
        float maxRadians = turnRateDegrees * Mathf.Deg2Rad * deltaTime;
        direction = Vector3.RotateTowards(direction, desired, maxRadians, 0f);
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            direction = desired;
        else
            direction.Normalize();
    }
}
