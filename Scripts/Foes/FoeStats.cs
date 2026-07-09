using Godot;

/// <summary>
/// Static per-level scaling table for foes (FOES.gdd §2). Pure data + helpers — no
/// Godot node types, only <see cref="Mathf"/> for clamping. All multipliers are
/// <b>base-relative</b> (a level-8 foe is exactly baseHP×5), never cumulative/chained.
/// </summary>
public static class FoeStats
{
    public struct LevelStats
    {
        public int Level;
        public float HpMult;
        public float AtkMult;
        public float SpdMult;
    }

    /// <summary>Base-relative multipliers for foe level 1..8 (clamped).</summary>
    public static LevelStats ForLevel(int level)
    {
        int l = Mathf.Clamp(level, 1, 8);
        return l switch
        {
            1 => new LevelStats { Level = 1, HpMult = 1f,   AtkMult = 1f,   SpdMult = 1f },
            2 => new LevelStats { Level = 2, HpMult = 1.2f, AtkMult = 1.2f, SpdMult = 1.2f },
            3 => new LevelStats { Level = 3, HpMult = 1.5f, AtkMult = 1.5f, SpdMult = 1.2f },
            4 => new LevelStats { Level = 4, HpMult = 2f,   AtkMult = 1.5f, SpdMult = 1.2f },
            5 => new LevelStats { Level = 5, HpMult = 3f,   AtkMult = 1.5f, SpdMult = 1.5f },
            6 => new LevelStats { Level = 6, HpMult = 3f,   AtkMult = 2f,   SpdMult = 1.5f },
            7 => new LevelStats { Level = 7, HpMult = 4f,   AtkMult = 3f,   SpdMult = 1.5f },
            _ => new LevelStats { Level = 8, HpMult = 5f,   AtkMult = 3f,   SpdMult = 2f },
        };
    }

    // Skill / feature gating by level (FOES §2).
    public static bool HasAggro(int level)         => level >= 2;   // sight/aggro FSM
    public static bool HasImprovedSight(int level) => level >= 3;   // larger sight shape
    public static bool HasSkill1(int level)        => level >= 4;   // crab Spawn / gull Poop
    public static bool HasSkill2(int level)        => level >= 6;   // crab Jump / gull Dash

    /// <summary>Difficulty display name per foe level (FOES §2).</summary>
    public static string DifficultyName(int level) => Mathf.Clamp(level, 1, 8) switch
    {
        1 => "Fairytale",
        2 => "Mundane",
        3 => "Mundane",
        4 => "Challenging",
        5 => "Challenging",
        6 => "Demonic",
        7 => "Hellish",
        _ => "God-Forbid",
    };

    /// <summary>Foe level from the current day (FOES §2): 1–10→1, 11–20→2, 21–30→3,
    /// 31–35→4, 36–40→5, 41–45→6; clamps to 1 below day 1 and to 6 past day 45.
    /// (Zone-6 levels 7–8 are node-driven, not day-driven — set explicitly there.)</summary>
    public static int LevelForDay(int day)
    {
        if (day <= 10) return 1;
        if (day <= 20) return 2;
        if (day <= 30) return 3;
        if (day <= 35) return 4;
        if (day <= 40) return 5;
        return 6;
    }
}
