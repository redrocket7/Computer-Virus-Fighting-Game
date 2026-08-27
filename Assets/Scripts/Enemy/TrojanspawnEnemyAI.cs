using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chases normally and releases 2-4 tiny enemies on death.
/// Three tiny enemies is the most likely result.
/// </summary>
public class TrojanspawnEnemyAI : EnemyAI
{
    [Header("Trojanspawn")]
    [SerializeField] GameObject tinyEnemyPrefab;
    [SerializeField] float spawnRadius = 1.5f;

    protected override void Die()
    {
        SpawnTinyEnemies();
        base.Die();
    }

    void SpawnTinyEnemies()
    {
        if (tinyEnemyPrefab == null)
            return;

        int spawnCount = ChooseSpawnCount();
        float angleOffset = Random.Range(0f, 360f);

        for (int i = 0; i < spawnCount; i++)
        {
            float angle = angleOffset + (360f / spawnCount) * i;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 desiredPosition = transform.position + direction * spawnRadius;
            Vector3 spawnPosition = desiredPosition;

            if (NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, spawnRadius * 2f, NavMesh.AllAreas))
                spawnPosition = hit.position;

            GameObject instance = Instantiate(tinyEnemyPrefab, spawnPosition, Quaternion.LookRotation(direction, Vector3.up));
            EnemyAI child = instance.GetComponent<EnemyAI>();
            if (child != null && boundEncounter != null)
                boundEncounter.RegisterEnemy(child);
        }
    }

    static int ChooseSpawnCount()
    {
        float roll = Random.value;
        if (roll < 0.2f)
            return 2;
        if (roll < 0.8f)
            return 3;
        return 4;
    }
}
