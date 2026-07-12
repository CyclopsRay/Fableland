namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// GDD §2.2 (T20 registry pattern) — the code-defined table of tile kinds.
/// Adding a tile kind means adding an entry here, never editing a switch
/// (T00 rule 1). Built once in the static constructor; read-only after boot.
/// Pure C# — no Godot.
/// </summary>
public static class TileRegistry
{
    private static readonly List<TileDef> _order;
    private static readonly Dictionary<string, TileDef> _defs;

    /// <summary>Ordered enumeration for the palette (GDD §7.1).</summary>
    public static IReadOnlyList<TileDef> All => _order;

    static TileRegistry()
    {
        _order = new List<TileDef>
        {
            new()
            {
                Id = "ground.grass", DisplayName = "Grass", Category = TileCategory.Ground,
                EditorColor = "#4CAF50",
            },
            new()
            {
                Id = "ground.stone", DisplayName = "Stone", Category = TileCategory.Ground,
                EditorColor = "#8D8D8D",
            },

            new()
            {
                Id = "platform.wood", DisplayName = "Wood Platform", Category = TileCategory.Platform,
                EditorColor = "#A0693D",
            },
            new()
            {
                Id = "platform.vine", DisplayName = "Vine Platform", Category = TileCategory.Platform,
                EditorColor = "#6B8E23",
            },

            new()
            {
                Id = "softvolume.bush1x1", DisplayName = "Bush", Category = TileCategory.SoftVolume,
                EditorColor = "#2E8B57",
            },
            new()
            {
                Id = "softvolume.cloud1x1", DisplayName = "Cloud (1x1)", Category = TileCategory.SoftVolume,
                EditorColor = "#B0E0E6",
            },
            new()
            {
                Id = "softvolume.cloud2x1", DisplayName = "Cloud (2x1)", Category = TileCategory.SoftVolume,
                FootprintW = 2, FootprintH = 1, EditorColor = "#ADD8E6",
            },

            new()
            {
                Id = "hazard.bonfire", DisplayName = "Bonfire", Category = TileCategory.Hazard,
                EditorColor = "#FF4500",
                // Centered on the 1x1 footprint; radius 0.75 m.
                EffectArea = ShapeDef.Circle(Units.PixelsPerMeter * 0.5f, Units.PixelsPerMeter * 0.5f, Units.PixelsPerMeter * 0.75f),
            },
            new()
            {
                Id = "hazard.freezepit", DisplayName = "Freeze Pit", Category = TileCategory.Hazard,
                EditorColor = "#66CCFF",
                EffectArea = ShapeDef.Rect(0f, 0f, Units.PixelsPerMeter, Units.PixelsPerMeter),
            },

            new()
            {
                Id = "spawn.enemy", DisplayName = "Enemy Spawn", Category = TileCategory.EnemySpawn,
                EditorColor = "#B22222",
                Props = new Dictionary<string, string> { ["foeTable"] = "default" },
            },
            new()
            {
                Id = "spawn.respawn", DisplayName = "Respawn Point", Category = TileCategory.Respawn,
                EditorColor = "#FFD700",
            },
            new()
            {
                Id = "goal.level", DisplayName = "Level Goal", Category = TileCategory.LevelGoal,
                EditorColor = "#9932CC",
            },
            new()
            {
                Id = "spawn.character", DisplayName = "Character Spawn", Category = TileCategory.Character,
                EditorColor = "#1E90FF",
            },

            new()
            {
                Id = "deco.flower", DisplayName = "Flower", Category = TileCategory.Decoration,
                AllowedRoles = LayerRoleMask.Any, EditorColor = "#FF69B4",
            },
            new()
            {
                Id = "deco.rock", DisplayName = "Rock", Category = TileCategory.Decoration,
                AllowedRoles = LayerRoleMask.Any, EditorColor = "#708090",
            },

            // GDD §6's worked example, verbatim.
            new()
            {
                Id = "rule.cloudZone", DisplayName = "Cloud Zone (rule)", Category = TileCategory.Rule,
                AllowedRoles = LayerRoleMask.Battlefield | LayerRoleMask.Farview,
                EditorColor = "#DDA0DD",
                RuleProps = new RuleProps
                {
                    SpawnTable = new List<(string DefId, int Weight)>
                    {
                        ("softvolume.cloud2x1", 3),
                        ("softvolume.cloud1x1", 1),
                    },
                    CountMin = 2,
                    CountMax = 4,
                    ReserveW = 3,
                    ReserveH = 4,
                    Tags = Array.Empty<string>(),
                },
            },
        };

        _defs = _order.ToDictionary(d => d.Id, d => d);
    }

    public static bool TryGet(string id, out TileDef def)
    {
        if (id == null)
        {
            def = null;
            return false;
        }
        return _defs.TryGetValue(id, out def);
    }

    /// <summary>Pure validation (no Godot): duplicate ids can't happen (dictionary keys), so
    /// this checks rule spawnTable defIds resolve, footprints &gt;= 1, and weights &gt; 0.</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();

        foreach (var def in _order)
        {
            if (def.FootprintW < 1 || def.FootprintH < 1)
                problems.Add($"{def.Id}: footprint must be >= 1x1 (got {def.FootprintW}x{def.FootprintH})");

            if (def.Category != TileCategory.Rule) continue;

            if (def.RuleProps == null)
            {
                problems.Add($"{def.Id}: Rule-category tile has no RuleProps");
                continue;
            }

            foreach (var (spawnDefId, weight) in def.RuleProps.SpawnTable)
            {
                if (!_defs.ContainsKey(spawnDefId))
                    problems.Add($"{def.Id}: spawnTable references unknown def '{spawnDefId}'");
                if (weight <= 0)
                    problems.Add($"{def.Id}: spawnTable weight for '{spawnDefId}' must be > 0 (got {weight})");
            }
        }

        return problems;
    }
}
