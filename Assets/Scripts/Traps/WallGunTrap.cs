using UnityEngine;

/// <summary>
/// Fixed wall turret that fires straight ahead on an interval. Does not track the player.
/// </summary>
public class WallGunTrap : MonoBehaviour
{
    [Header("Shooting")]
    [SerializeField] Projectile projectilePrefab;
    [SerializeField] Transform firePoint;
    [SerializeField] float fireInterval = 1.25f;
    [SerializeField] float firstShotDelay = 0.35f;
    [SerializeField] Vector3 localFireDirection = Vector3.forward;

    [Header("Room Activation")]
    [Tooltip("When enabled, the trap stays idle until the parent room encounter begins.")]
    [SerializeField] bool waitForRoomEncounter = true;

    RoomEncounter roomEncounter;
    float fireTimer;
    bool isActive;
    bool subscribedToEncounter;

    void Awake()
    {
        if (firePoint == null)
            firePoint = transform;

        roomEncounter = GetComponentInParent<RoomEncounter>();
    }

    void OnEnable()
    {
        TrySubscribeToEncounter();
        ApplyActivationState(ShouldStartActive());
    }

    void Start()
    {
        TrySubscribeToEncounter();
        ApplyActivationState(ShouldStartActive());
    }

    void OnDisable()
    {
        UnsubscribeFromEncounter();
    }

    bool ShouldStartActive()
    {
        if (!waitForRoomEncounter || roomEncounter == null)
            return true;

        return roomEncounter.IsInProgress;
    }

    void TrySubscribeToEncounter()
    {
        if (!waitForRoomEncounter || roomEncounter == null || subscribedToEncounter)
            return;

        roomEncounter.Started += Activate;
        subscribedToEncounter = true;
    }

    void UnsubscribeFromEncounter()
    {
        if (!subscribedToEncounter || roomEncounter == null)
            return;

        roomEncounter.Started -= Activate;
        subscribedToEncounter = false;
    }

    public void Activate()
    {
        if (isActive)
            return;

        isActive = true;
        fireTimer = Mathf.Max(0f, firstShotDelay);
    }

    public void Deactivate()
    {
        isActive = false;
    }

    void ApplyActivationState(bool active)
    {
        if (active)
            Activate();
        else
            Deactivate();
    }

    void Update()
    {
        if (!isActive || projectilePrefab == null)
            return;

        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f)
            return;

        fireTimer = Mathf.Max(0.1f, fireInterval);
        Fire();
    }

    void Fire()
    {
        Vector3 direction = firePoint.TransformDirection(localFireDirection);
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = firePoint.forward;

        direction.Normalize();

        Projectile projectile = Instantiate(
            projectilePrefab,
            firePoint.position,
            Quaternion.LookRotation(direction, Vector3.up));

        projectile.LaunchAsEnemy(direction, transform);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Transform origin = firePoint != null ? firePoint : transform;
        Vector3 direction = origin.TransformDirection(localFireDirection);
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = origin.forward;
        direction.Normalize();

        Gizmos.color = Color.red;
        Gizmos.DrawRay(origin.position, direction * 4f);
    }
#endif
}
