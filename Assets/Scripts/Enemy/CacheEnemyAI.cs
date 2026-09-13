using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Gungeon-style Cache: flees the player and escapes after a short lifetime.
/// Kill it in time for a random unowned upgrade or gun pickup.
/// </summary>
public class CacheEnemyAI : EnemyAI
{
    [Header("Flee")]
    [SerializeField] float fleeDistance = 8f;
    [SerializeField] float fleeSampleRadius = 8f;
    [SerializeField] float fleeSpeedMultiplier = 1.45f;

    [Header("Escape")]
    [Tooltip("Seconds the Cache stays after combat holdoff ends before vanishing with no loot.")]
    [SerializeField] float escapeLifetime = 8f;
    [SerializeField] GameObject escapeEffectPrefab;

    [Header("Loot")]
    [SerializeField] GameObject usbDashPickupPrefab;
    [SerializeField] GameObject goatDashPickupPrefab;
    [SerializeField] GameObject shotgunPickupPrefab;
    [SerializeField] GameObject machineGunPickupPrefab;
    [SerializeField] GameObject rocketLauncherPickupPrefab;
    [Tooltip("When both upgrades and guns are available, chance to roll an upgrade.")]
    [SerializeField, Range(0f, 1f)] float upgradeDropChance = 0.45f;

    static readonly List<LootOption> LootBuffer = new List<LootOption>(8);

    struct LootOption
    {
        public GameObject Prefab;
        public int WeaponIndex; // < 0 = upgrade / non-weapon
    }

    float lifetimeRemaining;
    bool lifetimeStarted;
    bool hasEscaped;
    bool droppedLoot;

    /// <summary>Don't let Transfers farm Cache loot for free.</summary>
    public override bool CanBeTransferDamageTarget => false;

    /// <summary>Spawn in the player's face so the flee window is a real chase.</summary>
    public override bool PrefersNearPlayerSpawn => true;

    protected override void Awake()
    {
        base.Awake();

        fleeDistance = Mathf.Max(1f, fleeDistance);
        fleeSampleRadius = Mathf.Max(1f, fleeSampleRadius);
        escapeLifetime = Mathf.Max(0.5f, escapeLifetime);

        if (fleeSpeedMultiplier > 1f)
            TryApplySpeedBuff(fleeSpeedMultiplier, 1);
    }

    protected override Vector3 GetChaseDestination(Transform chaseTarget)
    {
        Vector3 myPos = transform.position;
        if (chaseTarget == null)
            return myPos;

        Vector3 away = myPos - chaseTarget.position;
        away.y = 0f;
        float magSq = away.sqrMagnitude;

        if (magSq < 0.001f)
        {
            away = transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f)
                away = Vector3.forward;
            magSq = away.sqrMagnitude;
        }

        away *= fleeDistance / Mathf.Sqrt(magSq);
        Vector3 desired = myPos + away;

        int areaMask = Agent != null ? Agent.areaMask : NavMesh.AllAreas;
        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, fleeSampleRadius, areaMask))
            return hit.position;

        return myPos;
    }

    protected override void Update()
    {
        if (hasEscaped)
            return;

        base.Update();

        if (!CanAct || !IsAlive)
            return;

        if (!lifetimeStarted)
        {
            lifetimeStarted = true;
            lifetimeRemaining = escapeLifetime;
        }

        lifetimeRemaining -= Time.deltaTime;
        if (lifetimeRemaining <= 0f)
            Escape();
    }

    protected override void Die()
    {
        if (!droppedLoot && !hasEscaped)
            TryDropLoot();

        base.Die();
    }

    void Escape()
    {
        if (hasEscaped || !IsAlive)
            return;

        hasEscaped = true;
        StopAgentPath();

        if (escapeEffectPrefab != null)
        {
            GameObject effect = Instantiate(escapeEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 2f);
        }
        else
        {
            SpawnDeathEffect();
        }

        // Destroy without Die() so escaping grants no kill / no loot.
        Destroy(gameObject);
    }

    void TryDropLoot()
    {
        droppedLoot = true;

        PlayerController player = PlayerController.Instance;
        LootBuffer.Clear();

        AddUpgradeOption(usbDashPickupPrefab, player == null || !player.HasUsbDash);
        AddUpgradeOption(goatDashPickupPrefab, player == null || !player.HasGoatDash);
        AddWeaponOption(shotgunPickupPrefab, 1, player);
        AddWeaponOption(machineGunPickupPrefab, 2, player);
        AddWeaponOption(rocketLauncherPickupPrefab, 3, player);

        if (LootBuffer.Count == 0)
            return;

        LootOption chosen = ChooseLoot();
        if (chosen.Prefab == null)
            return;

        GameObject instance = Instantiate(chosen.Prefab, transform.position, Quaternion.identity);
        if (chosen.WeaponIndex >= 0)
            instance.GetComponent<WeaponPickup>()?.Configure(chosen.WeaponIndex);
    }

    void AddUpgradeOption(GameObject prefab, bool available)
    {
        if (!available || prefab == null)
            return;

        LootBuffer.Add(new LootOption { Prefab = prefab, WeaponIndex = -1 });
    }

    void AddWeaponOption(GameObject prefab, int weaponIndex, PlayerController player)
    {
        if (prefab == null)
            return;

        if (player != null && player.HasWeapon(weaponIndex))
            return;

        LootBuffer.Add(new LootOption { Prefab = prefab, WeaponIndex = weaponIndex });
    }

    LootOption ChooseLoot()
    {
        bool hasUpgrade = false;
        bool hasWeapon = false;
        for (int i = 0; i < LootBuffer.Count; i++)
        {
            if (LootBuffer[i].WeaponIndex < 0)
                hasUpgrade = true;
            else
                hasWeapon = true;
        }

        bool preferUpgrade = hasUpgrade && (!hasWeapon || Random.value < upgradeDropChance);

        int candidates = 0;
        for (int i = 0; i < LootBuffer.Count; i++)
        {
            bool isUpgrade = LootBuffer[i].WeaponIndex < 0;
            if (preferUpgrade == isUpgrade || !(hasUpgrade && hasWeapon))
                candidates++;
        }

        int pick = Random.Range(0, Mathf.Max(1, candidates));
        for (int i = 0; i < LootBuffer.Count; i++)
        {
            bool isUpgrade = LootBuffer[i].WeaponIndex < 0;
            if (hasUpgrade && hasWeapon && preferUpgrade != isUpgrade)
                continue;

            if (pick-- == 0)
                return LootBuffer[i];
        }

        return LootBuffer[0];
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, fleeDistance);
    }
#endif
}
