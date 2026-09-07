using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks run stats for the end-of-run infection report shown on game over.
/// Resets automatically whenever a scene loads.
/// </summary>
public static class InfectionReport
{
    const float NearDeathHealthFraction = 0.25f;

    static readonly Dictionary<RoomModifierType, int> ModifiersCleared = new Dictionary<RoomModifierType, int>();
    static readonly List<string> UpgradeNames = new List<string>(8);

    static float runStartTime;
    static int enemiesKilled;
    static int roomsEntered;
    static int roomsCleared;
    static int maxDepthReached;
    static float damageTaken;
    static int nearDeaths;
    static int hitsTaken;
    static bool finalized;
    static string cachedSummary = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (runStartTime <= 0f)
            BeginRun();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BeginRun();
    }

    public static void BeginRun()
    {
        Clear();
        runStartTime = Time.unscaledTime;
    }

    static void Clear()
    {
        enemiesKilled = 0;
        roomsEntered = 0;
        roomsCleared = 0;
        maxDepthReached = 0;
        damageTaken = 0f;
        nearDeaths = 0;
        hitsTaken = 0;
        finalized = false;
        cachedSummary = string.Empty;
        ModifiersCleared.Clear();
        UpgradeNames.Clear();
        runStartTime = 0f;
    }

    public static void RecordEnemyKill()
    {
        if (finalized)
            return;

        enemiesKilled++;
    }

    public static void RecordRoomEntered(int depth, RoomModifierType modifier)
    {
        if (finalized)
            return;

        roomsEntered++;
        if (depth > maxDepthReached)
            maxDepthReached = depth;
    }

    public static void RecordRoomCleared(int depth, RoomModifierType modifier)
    {
        if (finalized)
            return;

        roomsCleared++;
        if (depth > maxDepthReached)
            maxDepthReached = depth;

        if (modifier == RoomModifierType.None)
            return;

        if (ModifiersCleared.TryGetValue(modifier, out int count))
            ModifiersCleared[modifier] = count + 1;
        else
            ModifiersCleared[modifier] = 1;
    }

    public static void RecordDamageTaken(float amount, float healthAfter, float maxHealth)
    {
        if (finalized || amount <= 0f)
            return;

        damageTaken += amount;
        hitsTaken++;

        float threshold = Mathf.Max(0.01f, maxHealth * NearDeathHealthFraction);
        float healthBefore = healthAfter + amount;
        if (healthBefore > threshold && healthAfter > 0f && healthAfter <= threshold)
            nearDeaths++;
    }

    public static void RecordUpgrade(string upgradeName)
    {
        if (finalized || string.IsNullOrWhiteSpace(upgradeName))
            return;

        for (int i = 0; i < UpgradeNames.Count; i++)
        {
            if (string.Equals(UpgradeNames[i], upgradeName, System.StringComparison.OrdinalIgnoreCase))
                return;
        }

        UpgradeNames.Add(upgradeName.Trim());
    }

    public static string FinalizeAndFormat()
    {
        if (!finalized)
        {
            finalized = true;
            cachedSummary = FormatSummary();
        }

        return cachedSummary;
    }

    public static string FormatSummary()
    {
        var sb = new StringBuilder(256);
        float elapsed = Mathf.Max(0f, Time.unscaledTime - runStartTime);

        sb.AppendLine("INFECTION REPORT");
        sb.AppendLine(FormatDuration(elapsed));
        sb.Append("Depth reached: ").Append(maxDepthReached).AppendLine();
        sb.Append("Rooms cleared: ").Append(roomsCleared);
        if (roomsEntered > roomsCleared)
            sb.Append(" / ").Append(roomsEntered).Append(" entered");
        sb.AppendLine();
        sb.Append("Processes terminated: ").Append(enemiesKilled).AppendLine();
        sb.Append("Damage taken: ").Append(FormatNumber(damageTaken));
        sb.Append("  (").Append(hitsTaken).Append(" hits)").AppendLine();
        sb.Append("Near-crashes: ").Append(nearDeaths).AppendLine();
        sb.Append("Modifiers survived: ").Append(FormatModifiers()).AppendLine();
        sb.Append("Upgrades: ").Append(FormatUpgrades());
        return sb.ToString();
    }

    static string FormatDuration(float seconds)
    {
        int totalSeconds = Mathf.FloorToInt(seconds);
        int minutes = totalSeconds / 60;
        int secs = totalSeconds % 60;
        return $"Runtime: {minutes}:{secs:00}";
    }

    static string FormatNumber(float value)
    {
        if (Mathf.Approximately(value, Mathf.Round(value)))
            return Mathf.RoundToInt(value).ToString();

        return value.ToString("0.#");
    }

    static string FormatModifiers()
    {
        if (ModifiersCleared.Count == 0)
            return "none";

        var parts = new List<string>(ModifiersCleared.Count);
        foreach (KeyValuePair<RoomModifierType, int> pair in ModifiersCleared)
            parts.Add($"{ModifierDisplayName(pair.Key)}×{pair.Value}");

        parts.Sort();
        return string.Join(", ", parts);
    }

    static string FormatUpgrades()
    {
        if (UpgradeNames.Count == 0)
            return "none";

        return string.Join(", ", UpgradeNames);
    }

    static string ModifierDisplayName(RoomModifierType type)
    {
        return type switch
        {
            RoomModifierType.RaidArray => "RAID",
            RoomModifierType.BootLoop => "Boot Loop",
            RoomModifierType.PacketLoss => "Packet Loss",
            RoomModifierType.CorruptedSave => "Corrupted Save",
            _ => type.ToString()
        };
    }
}
