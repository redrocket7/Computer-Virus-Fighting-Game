using UnityEngine;

/// <summary>
/// Unlocks a loadout weapon by index when collected (Shotgun=1, Machine Gun=2, Rocket=3).
/// </summary>
public class WeaponPickup : MonoBehaviour
{
    [SerializeField] int weaponIndex = 1;
    [SerializeField] float pickupHeight = 0.75f;
    [SerializeField] bool destroyIfAlreadyOwned = true;

    int configuredWeaponIndex = -1;

    public void Configure(int index)
    {
        configuredWeaponIndex = Mathf.Max(0, index);
    }

    int GetWeaponIndex()
    {
        return configuredWeaponIndex >= 0 ? configuredWeaponIndex : weaponIndex;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        int index = GetWeaponIndex();
        if (player.HasWeapon(index))
        {
            if (destroyIfAlreadyOwned)
                Destroy(gameObject);
            return;
        }

        if (!player.GrantWeapon(index))
            return;

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.95f, 0.75f, 0.2f, 0.9f);
        Vector3 center = transform.position + Vector3.up * pickupHeight;
        Gizmos.DrawWireCube(center, new Vector3(0.9f, 0.35f, 0.45f));
    }
#endif
}
