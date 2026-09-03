using UnityEngine;

/// <summary>
/// Tracks active Packet Loss room modifiers and rolls whether player shots fizzle.
/// </summary>
public static class PacketLossCombatEffect
{
    static int activeEncounters;
    static float fizzleChance;

    public static bool IsActive => activeEncounters > 0;

    public static void Begin(float chance)
    {
        fizzleChance = Mathf.Clamp01(chance);
        activeEncounters++;
    }

    public static void End()
    {
        activeEncounters = Mathf.Max(0, activeEncounters - 1);
    }

    public static bool ShouldFizzlePlayerShot()
    {
        return activeEncounters > 0 && Random.value < fizzleChance;
    }
}
