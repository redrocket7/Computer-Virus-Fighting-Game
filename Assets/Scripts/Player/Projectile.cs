using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple forward-moving projectile. Add a Collider (Is Trigger) and optionally a Rigidbody.
/// </summary>
public class Projectile : MonoBehaviour
{
    [SerializeField] float speed = 20f;
    [SerializeField] float lifetime = 3f;
    [SerializeField] float damage = 1f;
    [SerializeField] LayerMask hitMask = ~0;

    [Header("Explosion")]
    [SerializeField] bool explodeOnHit;
    [SerializeField] GameObject explosionPrefab;
    [SerializeField] float explosionRadius = 4f;
    [SerializeField] float explosionDamage = 2f;
    [SerializeField] float shakeDuration = 0.3f;
    [SerializeField] float shakeStrength = 0.4f;
    [SerializeField] float shakeRotation = 2f;

    static readonly List<Projectile> activeProjectiles = new List<Projectile>();
    static readonly Collider[] overlapHits = new Collider[48];
    static readonly HashSet<IDamageable> damagedBuffer = new HashSet<IDamageable>();
    static int playerProjectileCount;

    protected Vector3 direction;
    protected LayerMask HitMask => hitMask;

    bool launched;
    bool firedByEnemy;
    bool exploded;
    bool countedAsPlayerProjectile;
    Transform owner;
    float lifeRemaining;
    float outgoingDamage;
    float outgoingExplosionDamage;

    public bool FiredByEnemy => firedByEnemy;
    public bool IsLaunched => launched;
    public Vector3 FlyDirection => direction;
    public float Speed => speed;
    public static IReadOnlyList<Projectile> ActiveProjectiles => activeProjectiles;
    public static bool HasPlayerProjectiles => playerProjectileCount > 0;

    protected Transform Owner => owner;

    public void Launch(Vector3 fireDirection, Transform source = null)
    {
        Launch(fireDirection, source, false);
    }

    public void LaunchAsEnemy(Vector3 fireDirection, Transform source)
    {
        Launch(fireDirection, source, true);
    }

    void Launch(Vector3 fireDirection, Transform source, bool enemyProjectile)
    {
        owner = source;
        firedByEnemy = enemyProjectile;
        outgoingDamage = damage;
        outgoingExplosionDamage = explosionDamage;
        if (enemyProjectile && source != null)
        {
            EnemyAI ownerAi = source.GetComponentInParent<EnemyAI>();
            if (ownerAi != null)
            {
                outgoingDamage = ownerAi.ScaleOutgoingDamage(damage);
                outgoingExplosionDamage = ownerAi.ScaleOutgoingDamage(explosionDamage);
            }
        }

        direction = fireDirection.normalized;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            direction = transform.forward;
        direction.Normalize();

        launched = true;
        exploded = false;
        if (!enemyProjectile)
        {
            playerProjectileCount++;
            countedAsPlayerProjectile = true;
        }

        if (explodeOnHit)
            lifeRemaining = lifetime;
        else
            Destroy(gameObject, lifetime);
    }

    protected virtual void OnEnable()
    {
        activeProjectiles.Add(this);
    }

    protected virtual void OnDisable()
    {
        if (countedAsPlayerProjectile)
        {
            countedAsPlayerProjectile = false;
            if (playerProjectileCount > 0)
                playerProjectileCount--;
        }

        int index = activeProjectiles.IndexOf(this);
        if (index < 0)
            return;

        int last = activeProjectiles.Count - 1;
        activeProjectiles[index] = activeProjectiles[last];
        activeProjectiles.RemoveAt(last);
    }

    void Update()
    {
        if (!launched)
            return;

        if (explodeOnHit)
        {
            lifeRemaining -= Time.deltaTime;
            if (lifeRemaining <= 0f)
            {
                Explode(transform.position);
                return;
            }
        }

        TickMovement(Time.deltaTime);
    }

    /// <summary>
    /// Override to steer or otherwise alter flight. Keep motion on the XZ plane.
    /// </summary>
    protected virtual void TickMovement(float deltaTime)
    {
        transform.position += direction * (speed * deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & hitMask) == 0)
            return;

        // Child mesh colliders (e.g. visual Cube) do not have Projectile on the same object.
        if (other.GetComponentInParent<Projectile>() != null)
            return;

        if (owner != null &&
            (other.transform == owner ||
             other.transform.IsChildOf(owner) ||
             owner.IsChildOf(other.transform)))
            return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            // Player shots damage enemies; enemy shots damage the player.
            if (firedByEnemy && damageable is EnemyAI)
                return;
            if (!firedByEnemy && damageable is PlayerController)
                return;

            if (!firedByEnemy && PacketLossCombatEffect.ShouldFizzlePlayerShot())
            {
                FizzleFromPacketLoss();
                return;
            }

            if (explodeOnHit)
            {
                Explode(transform.position);
                return;
            }

            damageable.TakeDamage(outgoingDamage);
            Destroy(gameObject);
            return;
        }

        // Room encounter volumes and open doors are triggers the bullet must pass through.
        if (other.isTrigger)
            return;

        HitObstacle(other);
    }

    protected virtual void HitObstacle(Collider other)
    {
        if (explodeOnHit)
            Explode(transform.position);
        else
            Destroy(gameObject);
    }

    void FizzleFromPacketLoss()
    {
        PacketLossFizzleEffect.Play(transform.position, direction);
        Destroy(gameObject);
    }

    void Explode(Vector3 position)
    {
        if (exploded)
            return;

        exploded = true;
        launched = false;

        ScreenShake.Shake(shakeDuration, shakeStrength, shakeRotation);

        if (explosionPrefab != null)
        {
            GameObject effect = Instantiate(explosionPrefab, position, Quaternion.identity);
            Destroy(effect, ExplosionEffect.GetPoolLifetime(effect));
        }

        damagedBuffer.Clear();
        int hitCount = Physics.OverlapSphereNonAlloc(
            position,
            explosionRadius,
            overlapHits,
            hitMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            if (hit == null)
                continue;

            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target == null || !damagedBuffer.Add(target))
                continue;

            if (firedByEnemy && target is EnemyAI)
                continue;
            if (!firedByEnemy && target is PlayerController)
                continue;

            target.TakeDamage(outgoingExplosionDamage);
        }

        Destroy(gameObject);
    }
}
