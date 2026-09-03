using UnityEngine;

/// <summary>
/// Restores player health when collected.
/// </summary>
public class HealthPickup : MonoBehaviour
{
    [SerializeField] float healAmount = 1f;
    [SerializeField] float pickupHeight = 0.75f;

    float configuredHealAmount = -1f;

    public void Configure(float amount)
    {
        configuredHealAmount = Mathf.Max(0f, amount);
    }

    float GetHealAmount()
    {
        return configuredHealAmount >= 0f ? configuredHealAmount : healAmount;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        float amount = GetHealAmount();
        if (amount <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (!player.TryHeal(amount))
            return;

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.95f, 0.35f, 0.85f);
        Vector3 center = transform.position + Vector3.up * pickupHeight;
        Gizmos.DrawWireSphere(center, 0.55f);
    }
#endif
}
