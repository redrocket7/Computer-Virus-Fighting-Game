using UnityEngine;

/// <summary>
/// Run upgrade pickup: unlocks Goat Dash (ram damage while dashing).
/// </summary>
public class GoatDashPickup : MonoBehaviour
{
    [SerializeField] float pickupHeight = 0.75f;
    [SerializeField] bool destroyIfAlreadyOwned = true;

    void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        if (player.HasGoatDash)
        {
            if (destroyIfAlreadyOwned)
                Destroy(gameObject);
            return;
        }

        player.GrantGoatDash();
        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.85f, 0.55f, 0.2f, 0.9f);
        Vector3 center = transform.position + Vector3.up * pickupHeight;
        Gizmos.DrawWireSphere(center, 0.55f);
    }
#endif
}
