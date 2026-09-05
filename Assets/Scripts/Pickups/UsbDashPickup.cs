using UnityEngine;

/// <summary>
/// Run upgrade pickup: unlocks USB Dash (visual trail + firewall phase).
/// </summary>
public class UsbDashPickup : MonoBehaviour
{
    [SerializeField] float pickupHeight = 0.75f;
    [SerializeField] bool destroyIfAlreadyOwned = true;

    void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        if (player.HasUsbDash)
        {
            if (destroyIfAlreadyOwned)
                Destroy(gameObject);
            return;
        }

        player.GrantUsbDash();
        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
        Vector3 center = transform.position + Vector3.up * pickupHeight;
        Gizmos.DrawWireCube(center, new Vector3(1.1f, 0.35f, 0.55f));
    }
#endif
}
