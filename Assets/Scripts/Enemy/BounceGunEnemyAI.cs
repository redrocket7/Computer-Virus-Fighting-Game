using UnityEngine;

/// <summary>
/// Ranged bounce-gun enemy that sidesteps incoming player bullets like the dodge enemy.
/// </summary>
public class BounceGunEnemyAI : GunEnemyAI
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

    protected override void Update()
    {
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
            DodgeDistance = dodgeDistance,
            DodgeDuration = dodgeDuration,
            DodgeCooldown = dodgeCooldown,
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
