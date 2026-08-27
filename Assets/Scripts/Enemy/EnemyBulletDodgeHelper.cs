using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared player-bullet dodge logic for enemies that sidestep incoming shots.
/// </summary>
public static class EnemyBulletDodgeHelper
{
    public struct State
    {
        public float DodgeTimer;
        public float CooldownTimer;
        public float ThreatScanTimer;
        public bool SpeedCached;
    }

    public struct Settings
    {
        public float DetectRange;
        public float ThreatRadius;
        public float DodgeDistance;
        public float DodgeDuration;
        public float DodgeCooldown;
        public float DodgeSpeed;
        public float ThreatScanInterval;
    }

    /// <summary>
    /// Returns true when the caller should skip the rest of its Update (dodging or just started a dodge).
    /// </summary>
    public static bool Tick(
        EnemyAI owner,
        NavMeshAgent agent,
        Transform self,
        Settings settings,
        ref State state,
        Transform faceTarget,
        Action<Vector3> faceDirection,
        Action updateContactDamage)
    {
        if (agent != null)
            state.SpeedCached = true;

        if (state.CooldownTimer > 0f)
            state.CooldownTimer -= Time.deltaTime;

        if (state.DodgeTimer > 0f)
        {
            state.DodgeTimer -= Time.deltaTime;
            if (state.DodgeTimer <= 0f)
                EndDodge(agent, owner);
            else if (faceTarget != null)
                faceDirection?.Invoke(faceTarget.position - self.position);

            updateContactDamage?.Invoke();
            return true;
        }

        state.ThreatScanTimer -= Time.deltaTime;
        if (state.ThreatScanTimer <= 0f)
        {
            state.ThreatScanTimer = settings.ThreatScanInterval;
            if (TryDodgeIncomingBullet(owner, agent, self, settings, ref state, faceDirection))
            {
                updateContactDamage?.Invoke();
                return true;
            }
        }

        return false;
    }

    static bool TryDodgeIncomingBullet(
        EnemyAI owner,
        NavMeshAgent agent,
        Transform self,
        Settings settings,
        ref State state,
        Action<Vector3> faceDirection)
    {
        if (state.CooldownTimer > 0f || agent == null || !agent.isOnNavMesh)
            return false;

        if (!Projectile.HasPlayerProjectiles)
            return false;

        if (!TryFindThreat(self, settings, out Vector3 incoming))
            return false;

        Vector3 side = Vector3.Cross(incoming, Vector3.up);
        if (side.sqrMagnitude < 0.001f)
            side = self.right;
        side.Normalize();

        if (UnityEngine.Random.value < 0.5f)
            side = -side;

        if (TryDodgeTo(agent, owner, self.position + side * settings.DodgeDistance, settings, ref state))
        {
            faceDirection?.Invoke(incoming);
            return true;
        }

        return TryDodgeTo(agent, owner, self.position - side * settings.DodgeDistance, settings, ref state);
    }

    static bool TryFindThreat(Transform self, Settings settings, out Vector3 incoming)
    {
        incoming = default;
        float detectSqr = settings.DetectRange * settings.DetectRange;
        float threatSqr = settings.ThreatRadius * settings.ThreatRadius;
        float bestScore = float.MaxValue;
        bool found = false;

        foreach (Projectile projectile in Projectile.ActiveProjectiles)
        {
            if (projectile == null || !projectile.IsLaunched || projectile.FiredByEnemy)
                continue;

            Vector3 toEnemy = self.position - projectile.transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > detectSqr)
                continue;

            Vector3 fly = projectile.FlyDirection;
            fly.y = 0f;
            if (fly.sqrMagnitude < 0.001f)
                continue;
            fly.Normalize();

            float along = Vector3.Dot(toEnemy, fly);
            if (along < 0.25f)
                continue;

            Vector3 closest = projectile.transform.position + fly * along;
            Vector3 offset = closest - self.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > threatSqr)
                continue;

            if (along < bestScore)
            {
                bestScore = along;
                incoming = fly;
                found = true;
            }
        }

        return found;
    }

    static bool TryDodgeTo(
        NavMeshAgent agent,
        EnemyAI owner,
        Vector3 desired,
        Settings settings,
        ref State state)
    {
        if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, settings.DodgeDistance, NavMesh.AllAreas))
            return false;

        agent.isStopped = false;
        agent.speed = settings.DodgeSpeed;
        agent.SetDestination(hit.position);
        state.DodgeTimer = settings.DodgeDuration;
        state.CooldownTimer = settings.DodgeCooldown;
        return true;
    }

    static void EndDodge(NavMeshAgent agent, EnemyAI owner)
    {
        if (agent == null || !agent.isOnNavMesh)
            return;

        agent.speed = owner.GetCurrentMoveSpeed();

        if (agent.isStopped && !agent.hasPath)
            return;

        agent.isStopped = true;
        if (agent.hasPath)
            agent.ResetPath();
    }

    public static void RestoreSpeed(NavMeshAgent agent, EnemyAI owner, ref State state)
    {
        if (!state.SpeedCached || agent == null)
            return;

        agent.speed = owner.GetCurrentMoveSpeed();
    }
}
