using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks local player avatars for single-player and shared-camera co-op.
/// </summary>
public static class PlayerRegistry
{
    static readonly List<PlayerController> players = new List<PlayerController>(4);

    public static IReadOnlyList<PlayerController> All => players;

    public static int Count => players.Count;

    public static bool AnyAlive
    {
        get
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && !players[i].IsDead)
                    return true;
            }

            return false;
        }
    }

    public static bool AllDead
    {
        get
        {
            if (players.Count == 0)
                return false;

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && !players[i].IsDead)
                    return false;
            }

            return true;
        }
    }

    public static void Register(PlayerController player)
    {
        if (player == null)
            return;

        if (!players.Contains(player))
            players.Add(player);

        players.Sort((a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));
    }

    public static void Unregister(PlayerController player)
    {
        if (player == null)
            return;

        players.Remove(player);
    }

    public static PlayerController GetPrimary()
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null)
                return players[i];
        }

        return Object.FindAnyObjectByType<PlayerController>();
    }

    public static PlayerController GetNearestLiving(Vector3 worldPosition)
    {
        PlayerController best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;

            Vector3 delta = candidate.transform.position - worldPosition;
            delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            best = candidate;
        }

        return best;
    }

    public static PlayerController GetMostDamagedLiving()
    {
        PlayerController worst = null;
        float worstFraction = float.PositiveInfinity;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead || candidate.MaxHealth <= 0.001f)
                continue;

            float fraction = candidate.CurrentHealth / candidate.MaxHealth;
            if (fraction >= worstFraction)
                continue;

            worstFraction = fraction;
            worst = candidate;
        }

        return worst;
    }

    public static Vector3 GetLivingCentroid()
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;

            sum += candidate.transform.position;
            count++;
        }

        if (count == 0)
        {
            PlayerController primary = GetPrimary();
            return primary != null ? primary.transform.position : Vector3.zero;
        }

        return sum / count;
    }

    public static float GetLivingSeparation()
    {
        PlayerController a = null;
        PlayerController b = null;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;

            if (a == null)
            {
                a = candidate;
                continue;
            }

            b = candidate;
            break;
        }

        if (a == null || b == null)
            return 0f;

        Vector3 delta = a.transform.position - b.transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    public static bool AnyLivingLacksUsbDash()
    {
        if (players.Count == 0)
            return true;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;
            if (!candidate.HasUsbDash)
                return true;
        }

        return !AnyAlive;
    }

    public static bool AnyLivingLacksGoatDash()
    {
        if (players.Count == 0)
            return true;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;
            if (!candidate.HasGoatDash)
                return true;
        }

        return !AnyAlive;
    }

    public static bool AnyLivingLacksWeapon(int weaponIndex)
    {
        if (players.Count == 0)
            return true;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;
            if (!candidate.HasWeapon(weaponIndex))
                return true;
        }

        return !AnyAlive;
    }

    public static int LivingCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && !players[i].IsDead)
                    count++;
            }

            return count;
        }
    }

    /// <summary>
    /// True when every living player is within <paramref name="radius"/> of the point.
    /// </summary>
    public static bool AllLivingNear(Vector3 point, float radius)
    {
        float radiusSqr = Mathf.Max(0.01f, radius) * Mathf.Max(0.01f, radius);
        int living = 0;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;

            living++;
            if (FlatSqrDistance(candidate.transform.position, point) > radiusSqr)
                return false;
        }

        return living > 0;
    }

    /// <summary>True when at least one living player is within radius of the point.</summary>
    public static bool AnyLivingNear(Vector3 point, float radius)
    {
        float radiusSqr = Mathf.Max(0.01f, radius) * Mathf.Max(0.01f, radius);

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;

            if (FlatSqrDistance(candidate.transform.position, point) <= radiusSqr)
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when every living player is within <paramref name="radius"/> of
    /// either point A or point B (nearby dual sample, e.g. both faces of a door).
    /// </summary>
    public static bool AllLivingNearEither(Vector3 pointA, Vector3 pointB, float radius)
    {
        float radiusSqr = Mathf.Max(0.01f, radius) * Mathf.Max(0.01f, radius);
        int living = 0;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;

            living++;
            Vector3 pos = candidate.transform.position;
            if (FlatSqrDistance(pos, pointA) > radiusSqr && FlatSqrDistance(pos, pointB) > radiusSqr)
                return false;
        }

        return living > 0;
    }

    /// <summary>True when at least one living player is within radius of either point.</summary>
    public static bool AnyLivingNearEither(Vector3 pointA, Vector3 pointB, float radius)
    {
        float radiusSqr = Mathf.Max(0.01f, radius) * Mathf.Max(0.01f, radius);

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController candidate = players[i];
            if (candidate == null || candidate.IsDead)
                continue;

            Vector3 pos = candidate.transform.position;
            if (FlatSqrDistance(pos, pointA) <= radiusSqr || FlatSqrDistance(pos, pointB) <= radiusSqr)
                return true;
        }

        return false;
    }

    static float FlatSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    public static Vector3 GetCoopSpawnOffset(int playerIndex)
    {
        switch (Mathf.Max(0, playerIndex))
        {
            case 0: return new Vector3(-1.25f, 0f, 0f);
            case 1: return new Vector3(1.25f, 0f, 0f);
            case 2: return new Vector3(0f, 0f, 1.25f);
            default: return new Vector3(0f, 0f, -1.25f);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        players.Clear();
    }
}
