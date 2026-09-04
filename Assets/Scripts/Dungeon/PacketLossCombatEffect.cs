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
        if (activeEncounters == 0)
            fizzleChance = 0f;
    }

    public static void Reset()
    {
        activeEncounters = 0;
        fizzleChance = 0f;
    }

    public static bool ShouldFizzlePlayerShot()
    {
        return activeEncounters > 0 && Random.value < fizzleChance;
    }
}
