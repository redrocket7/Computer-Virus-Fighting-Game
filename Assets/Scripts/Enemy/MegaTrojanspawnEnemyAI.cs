using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Large trojanspawn that drips tiny enemies over time, then splits into
/// 2-3 normal trojanspawns on death.
/// </summary>
public class MegaTrojanspawnEnemyAI : EnemyAI
{
    [Header("Living Spawn")]
    [SerializeField] GameObject tinyEnemyPrefab;
    [SerializeField] float tinySpawnInterval = 4f;
    [SerializeField] int tiniesPerWave = 1;
    [SerializeField] float tinySpawnRadius = 2.5f;
    [SerializeField] float firstSpawnDelay = 1.5f;

    [Header("Death Spawn")]
    [SerializeField] GameObject trojanspawnPrefab;
    [SerializeField] GameObject shotgunEnemyPrefab;
    [Range(0f, 1f)]
    [Tooltip("Chance each death-split enemy is a shotgun enemy instead of a trojanspawn.")]
    [SerializeField] float shotgunSpawnChance = 0.25f;
    [SerializeField] float trojanspawnSpawnRadius = 3f;
    [SerializeField] int minimumTrojanspawns = 2;
    [SerializeField] int maximumTrojanspawns = 3;

    float tinySpawnTimer;
    bool hasSplit;

    protected override void Start()
    {
        base.Start();
        tinySpawnTimer = Mathf.Max(0f, firstSpawnDelay);
        minimumTrojanspawns = Mathf.Max(1, minimumTrojanspawns);
        maximumTrojanspawns = Mathf.Max(minimumTrojanspawns, maximumTrojanspawns);
    }

    protected override void Update()
    {
        base.Update();
        if (!CanAct || !IsAlive)
            return;

        tinySpawnTimer -= Time.deltaTime;
        if (tinySpawnTimer > 0f)
            return;

        tinySpawnTimer = Mathf.Max(0.25f, tinySpawnInterval);
        SpawnRing(tinyEnemyPrefab, Mathf.Max(1, tiniesPerWave), tinySpawnRadius);
    }

    protected override void Die()
    {
        if (!hasSplit)
        {
            hasSplit = true;
            int count = Random.Range(minimumTrojanspawns, maximumTrojanspawns + 1);
            SpawnDeathRing(count);
        }

        base.Die();
    }

    void SpawnDeathRing(int count)
    {
        if (count <= 0)
            return;

        float angleOffset = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            GameObject prefab = ChooseDeathSpawnPrefab();
            if (prefab == null)
                continue;

            float angle = angleOffset + (360f / count) * i;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 desiredPosition = transform.position + direction * trojanspawnSpawnRadius;
            Vector3 spawnPosition = desiredPosition;

            if (NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, trojanspawnSpawnRadius * 2f, NavMesh.AllAreas))
                spawnPosition = hit.position;

            GameObject instance = Instantiate(
                prefab,
                spawnPosition,
                Quaternion.LookRotation(direction, Vector3.up));

            EnemyAI child = instance.GetComponent<EnemyAI>();
            if (child != null && boundEncounter != null)
                boundEncounter.RegisterEnemy(child);
        }
    }

    GameObject ChooseDeathSpawnPrefab()
    {
        if (shotgunEnemyPrefab != null &&
            shotgunSpawnChance > 0f &&
            Random.value <= shotgunSpawnChance)
        {
            return shotgunEnemyPrefab;
        }

        return trojanspawnPrefab;
    }

    void SpawnRing(GameObject prefab, int count, float radius)
    {
        if (prefab == null || count <= 0)
            return;

        float angleOffset = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            float angle = angleOffset + (360f / count) * i;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 desiredPosition = transform.position + direction * radius;
            Vector3 spawnPosition = desiredPosition;

            if (NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, radius * 2f, NavMesh.AllAreas))
                spawnPosition = hit.position;

            GameObject instance = Instantiate(
                prefab,
                spawnPosition,
                Quaternion.LookRotation(direction, Vector3.up));

            EnemyAI child = instance.GetComponent<EnemyAI>();
            if (child != null && boundEncounter != null)
                boundEncounter.RegisterEnemy(child);
        }
    }

#if UNITY_EDITOR
    new void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.85f, 0.35f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, tinySpawnRadius);
        Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, trojanspawnSpawnRadius);
    }
#endif
}
