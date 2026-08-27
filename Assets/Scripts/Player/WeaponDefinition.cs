using System;
using UnityEngine;

public enum WeaponFireMode
{
    SemiAutomatic,
    Automatic
}

[Serializable]
public class WeaponDefinition
{
    public string displayName = "Weapon";
    public Projectile projectilePrefab;
    public WeaponFireMode fireMode = WeaponFireMode.SemiAutomatic;
    public float fireCooldown = 0.2f;
    [Min(1)] public int pelletCount = 1;
    public float spreadAngle = 0f;
    public float spreadJitter = 0f;
}
