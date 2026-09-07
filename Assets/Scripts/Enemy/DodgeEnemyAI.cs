using UnityEngine;

/// <summary>
/// Chases the player and sidesteps incoming player bullets.
/// </summary>
public class DodgeEnemyAI : EnemyAI
{
    [Header("Dodge")]
    [SerializeField] float detectRange = 16f;
    [SerializeField] float threatRadius = 2.4f;
    [SerializeField] float dodgeDistance = 6f;
    [SerializeField] float dodgeDuration = 0.22f;
    [SerializeField] float dodgeCooldown = 0.4f;
    [SerializeField] float dodgeSpeed = 10f;
    [SerializeField] float threatScanInterval = 0.05f;

    EnemyBulletDodgeHelper.State dodgeState;
    int dodgeDistanceBuffStacks;
    int dodgeCooldownBuffStacks;
    float dodgeDistanceMultiplier = 1f;
    float dodgeCooldownMultiplier = 1f;

    public override bool CanReceiveDodgeDistanceBuff(int maxStacks) =>
        dodgeDistanceBuffStacks < Mathf.Max(1, maxStacks);

    public override bool CanReceiveDodgeCooldownBuff(int maxStacks) =>
        dodgeCooldownBuffStacks < Mathf.Max(1, maxStacks);

    public override bool TryApplyDodgeDistanceBuff(float multiplier, int maxStacks)
    {
        if (multiplier <= 0f || dodgeDistanceBuffStacks >= Mathf.Max(1, maxStacks))
            return false;

        dodgeDistanceBuffStacks++;
        dodgeDistanceMultiplier *= multiplier;
        return true;
    }

    public override bool TryApplyDodgeCooldownBuff(float multiplier, int maxStacks)
    {
        if (multiplier <= 0f || dodgeCooldownBuffStacks >= Mathf.Max(1, maxStacks))
            return false;

        dodgeCooldownBuffStacks++;
        dodgeCooldownMultiplier *= multiplier;
        return true;
    }

    public override bool TryRemoveDodgeDistanceBuff(float multiplier)
    {
        if (dodgeDistanceBuffStacks <= 0 || multiplier <= 0.0001f)
            return false;

        dodgeDistanceBuffStacks--;
        dodgeDistanceMultiplier /= multiplier;
        return true;
    }

    public override bool TryRemoveDodgeCooldownBuff(float multiplier)
    {
        if (dodgeCooldownBuffStacks <= 0 || multiplier <= 0.0001f)
            return false;

        dodgeCooldownBuffStacks--;
        dodgeCooldownMultiplier /= multiplier;
        return true;
    }

    protected override void Update()
    {
        TickHoldoff();
        if (!CanAct)
            return;

        if (EnemyBulletDodgeHelper.Tick(
                this,
                Agent,
                transform,
                GetDodgeSettings(),
                ref dodgeState,
                Player,
                FaceDirection,
                UpdateContactDamage))
        {
            return;
        }

        base.Update();
    }

    protected override void OnDestroy()
    {
        EnemyBulletDodgeHelper.RestoreSpeed(Agent, this, ref dodgeState);
        base.OnDestroy();
    }

    EnemyBulletDodgeHelper.Settings GetDodgeSettings()
    {
        return new EnemyBulletDodgeHelper.Settings
        {
            DetectRange = detectRange,
            ThreatRadius = threatRadius,
            DodgeDistance = dodgeDistance * Mathf.Max(0.01f, dodgeDistanceMultiplier),
            DodgeDuration = dodgeDuration,
            DodgeCooldown = dodgeCooldown / Mathf.Max(0.01f, dodgeCooldownMultiplier),
            DodgeSpeed = dodgeSpeed,
            ThreatScanInterval = threatScanInterval
        };
    }

#if UNITY_EDITOR
    new void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, threatRadius);
    }
#endif
}
