using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chases normally and slowly spawns fork enemies while alive.
/// </summary>
public class BranchEnemyAI : EnemyAI
{
    [Header("Branch Spawn")]
    [SerializeField] GameObject forkEnemyPrefab;
    [SerializeField] float forkSpawnInterval = 6f;
    [SerializeField] int forksPerSpawn = 1;
    [SerializeField] float spawnRadius = 2f;
    [SerializeField] float firstSpawnDelay = 2f;

    float spawnTimer;

    protected override void Start()
    {
        base.Start();
        spawnTimer = Mathf.Max(0f, firstSpawnDelay);
        forksPerSpawn = Mathf.Max(1, forksPerSpawn);
    }

    protected override void Update()
    {
        base.Update();
        if (!CanAct || !IsAlive)
            return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f)
            return;

        spawnTimer = Mathf.Max(0.5f, forkSpawnInterval);
        SpawnForks(Mathf.Max(1, forksPerSpawn));
    }

    void SpawnForks(int count)
    {
        if (forkEnemyPrefab == null || count <= 0)
            return;

        float angleOffset = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            float angle = angleOffset + (360f / count) * i;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 desiredPosition = transform.position + direction * spawnRadius;
            Vector3 spawnPosition = desiredPosition;

            if (NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, spawnRadius * 2f, NavMesh.AllAreas))
                spawnPosition = hit.position;

            GameObject instance = Instantiate(
                forkEnemyPrefab,
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
        Gizmos.color = new Color(0.35f, 0.85f, 0.45f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
#endif
}
