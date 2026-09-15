/// <summary>
/// Shared room-modifier and enemy roster copy used by the main menu and pause catalog.
/// Keep in sync with gameplay behavior in <see cref="RoomEncounter"/> / enemy prefabs.
/// </summary>
public static class GameCatalogData
{
    public struct ModifierInfo
    {
        public string Name;
        public string Description;
    }

    public struct EnemyInfo
    {
        public string Category;
        public string Name;
        public string Description;
    }

    public static readonly ModifierInfo[] Modifiers =
    {
        new ModifierInfo
        {
            Name = "RAID Array",
            Description =
                "Guarantees Repair, Overclock, and Shielder supports in the room.\n\n" +
                "Supports buff or protect other enemies - take them out early when you can."
        },
        new ModifierInfo
        {
            Name = "Boot Loop",
            Description =
                "After the first pack dies, another enemy wave spawns.\n\n" +
                "Don't relax after the first clear - the doors stay locked until the reinforcement wave is gone too."
        },
        new ModifierInfo
        {
            Name = "Packet Loss",
            Description =
                "While the fight is active, some of your shots may fizzle before they hit.\n\n" +
                "Keep firing. Missed packets are normal in these rooms."
        },
        new ModifierInfo
        {
            Name = "Corrupted Save",
            Description =
                "One enemy respawns once after death - weaker, but faster.\n\n" +
                "Expect a second engagement with the corrupted copy before the room unlocks."
        },
        new ModifierInfo
        {
            Name = "Fork Bomb",
            Description =
                "Tiny enemies keep spawning until all non-tiny enemies are dead.\n\n" +
                "Focus the larger threats first to stop the drip, then clean up leftover tinies."
        },
        new ModifierInfo
        {
            Name = "Critical Process",
            Description =
                "Guarantees a mega enemy in this room. Entering shows a silhouette and the mega's name before the doors lock.\n\n" +
                "Leave during the preview to cancel. At most one Critical Process room appears per run."
        }
    };

    public static readonly EnemyInfo[] Enemies =
    {
        new EnemyInfo
        {
            Category = "SUPPORT",
            Name = "Repair",
            Description =
                "Support unit that stays near allies and heals damaged processes in range.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Category = "SUPPORT",
            Name = "Overclock",
            Description =
                "Support unit that buffs random allies - damage, health, speed, fire rate, and specialty stats.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Category = "SUPPORT",
            Name = "Shielder",
            Description =
                "Support unit that starts shielded and periodically grants shields to unprotected allies.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Transfer",
            Description =
                "Redirects incoming damage to the lowest-HP ally in the room until its transfer meter overloads.\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Small",
            Description =
                "Basic chase process. Low health, closes distance, and deals contact damage.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Fat",
            Description =
                "Tanky chase process with high health and low speed.\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Long",
            Description =
                "Elongated chase process with medium health and high speed.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Fork",
            Description =
                "Chase process spawned by Branch enemies.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Tiny",
            Description =
                "Small pack hunters that try to stick together in groups of about 3-4.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Dodge",
            Description =
                "Melee chaser that sidesteps incoming player bullets.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Ram",
            Description =
                "Chases up close. From longer range it winds up, then charges in a straight ram.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Bomb",
            Description =
                "Chases normally, then arms a short fuse when close or when damaged.\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Zip Bomb",
            Description =
                "Absorbs your damage into a growing body. When storage is full, the next hit detonates it.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Branch",
            Description =
                "Chase enemy that slowly spawns Fork processes while alive.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Trojanspawn",
            Description =
                "Chase enemy that releases 2-4 Tiny enemies on death (3 is most common).\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Category = "CHASE",
            Name = "Cache",
            Description =
                "Spawns near you, flees, and vanishes after a few seconds if not killed.\n\n" +
                "Kill it in time for a random upgrade or gun you do not already own.\n\n" +
                "Difficulty: 1"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Gun",
            Description =
                "Ranged enemy that keeps mid-range and fires single projectiles.\n\n" +
                "Difficulty: 2"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Bounce Gun",
            Description =
                "Gunner that fires bouncing shots and sidesteps incoming bullets like Dodge.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Machine Gun",
            Description =
                "Keeps distance and sprays a rapid stream of bullets.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Shotgun",
            Description =
                "Holds closer range and fires a short spread of pellets.\n\n" +
                "Difficulty: 5"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Split Gun",
            Description =
                "Slow, heavy ranged attacker. Its shots split into fragments when they hit walls.\n\n" +
                "Difficulty: 5"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Homing Rocket",
            Description =
                "Keeps distance and fires missiles that home toward you.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Cannon",
            Description =
                "Slow ranged enemy that lobbs large cannonballs from long range.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Mortar",
            Description =
                "Keeps distance and arcs explosive shells that detonate on arrival.\n\n" +
                "Difficulty: 3"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Gun Absorb",
            Description =
                "Absorbs your bullet damage into storage, then spends it on charged shots.\n\n" +
                "Difficulty: 5"
        },
        new EnemyInfo
        {
            Category = "SHOOT",
            Name = "Link Gun",
            Description =
                "Ranged Transfer variant. Fires predicted three-round bursts while keeping its distance.\n\n" +
                "Redirects incoming damage to the lowest-HP ally until its transfer meter overloads, then must take damage itself.\n\n" +
                "Difficulty: 4"
        },
        new EnemyInfo
        {
            Category = "MEGA",
            Name = "Mega Explosive",
            Description =
                "Larger version of the mortar that fires shells in short bursts.\n\n" +
                "On death it starts a countdown, then explodes."
        },
        new EnemyInfo
        {
            Category = "MEGA",
            Name = "Mega Trojanspawn",
            Description =
                "Huge trojanspawn that spawns Tiny enemies while alive, then splits into 2-3 Trojanspawns on death.\n\n" +
                "Has a chance to split into a shotgun process with 2 Trojanspawns on death."
        },
        new EnemyInfo
        {
            Category = "MEGA",
            Name = "Mega Transfer",
            Description =
                "Mega chase Transfer. Tanks hits until half health, then summons Damage Containers and redirects all further damage into them with no overload.\n\n" +
                "Kill the containers carefully - they store unlimited damage and explode like Zip Bombs if you get close."
        },
        new EnemyInfo
        {
            Category = "MEGA",
            Name = "Damage Container",
            Description =
                "Unlimited-storage Zip Bomb sponge spawned by Mega Transfer.\n\n" +
                "Absorbs redirected and direct hits without a storage cap, grows as it fills, flees the player, and can proximity-fuse into a larger blast."
        }
    };
}
