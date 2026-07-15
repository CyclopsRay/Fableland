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
                EditorColor = "#76B947", AutotileGroup = "terrain.coastal_grass",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/ground_grass_seamless.png",
                SpriteFillFootprint = true,
                Props = new Dictionary<string, string>
                {
                    ["artSource"] = "res://Sprites/MapCreation/Beach/Generated/terrain_beach_atlas.png",
                },
            },
            new()
            {
                Id = "ground.sand", DisplayName = "Beach Sand", Category = TileCategory.Ground,
                EditorColor = "#E8C878", AutotileGroup = "terrain.sand_hill",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/ground_sand_seamless.png",
                SpriteFillFootprint = true,
                Props = new Dictionary<string, string>
                {
                    ["artSource"] = "res://Sprites/MapCreation/Beach/Generated/terrain_sand_hill_atlas.png",
                    ["autotileKind"] = "layered_hill",
                },
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
                Id = "platform.bench", DisplayName = "Beach Bench", Category = TileCategory.Platform,
                FootprintW = 2, FootprintH = 1, EditorColor = "#B88755",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/platform_bench.png",
            },
            new()
            {
                Id = "platform.sun_lounger", DisplayName = "Sun Lounger", Category = TileCategory.Platform,
                FootprintW = 3, FootprintH = 1, EditorColor = "#C8A675",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/platform_sun_lounger.png",
            },
            new()
            {
                Id = "platform.lifeguard_tower", DisplayName = "Lifeguard Tower", Category = TileCategory.Platform,
                FootprintW = 4, FootprintH = 8, EditorColor = "#4F9B9B",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/platform_lifeguard_tower.png",
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
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/softvolume_cloud_small.png",
                SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = 0.3f },
            },
            new()
            {
                Id = "softvolume.cloud2x1", DisplayName = "Cloud (2x1)", Category = TileCategory.SoftVolume,
                FootprintW = 2, FootprintH = 1, EditorColor = "#ADD8E6",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/softvolume_cloud_medium.png",
                SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = 0.3f },
            },
            new()
            {
                Id = "softvolume.cloud3x2", DisplayName = "Cloud (3x2)", Category = TileCategory.SoftVolume,
                FootprintW = 3, FootprintH = 2, EditorColor = "#A9D7E3",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/softvolume_cloud_large.png",
                SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = 0.3f },
            },
            new()
            {
                Id = "softvolume.palm_tree", DisplayName = "Palm Tree", Category = TileCategory.SoftVolume,
                FootprintW = 3, FootprintH = 4, EditorColor = "#4E9A51",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/softvolume_palm_tree_v2.png",
                SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = 0.1f },
            },

            new()
            {
                Id = "hazard.bonfire", DisplayName = "Bonfire", Category = TileCategory.Hazard,
                FootprintW = 2, FootprintH = 1, EditorColor = "#FF4500",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/hazard_bonfire_flat.png",
                // Deliberately catches only the hot center, not the full 2 m art width.
                EffectArea = ShapeDef.Circle(MapGrid.PixelsPerCell, MapGrid.PixelsPerCell * 0.65f,
                    MapGrid.PixelsPerCell * 0.35f),
            },
            new()
            {
                Id = "hazard.freezepit", DisplayName = "Freeze Pit", Category = TileCategory.Hazard,
                EditorColor = "#66CCFF",
                EffectArea = ShapeDef.Rect(0f, 0f, MapGrid.PixelsPerCell, MapGrid.PixelsPerCell),
            },
            new()
            {
                Id = "hazard.tsunami_trigger", DisplayName = "Tsunami Trigger", Category = TileCategory.Hazard,
                EditorColor = "#287DB5",
                EffectArea = ShapeDef.Rect(0f, 0f, MapGrid.PixelsPerCell, MapGrid.PixelsPerCell),
                Props = new Dictionary<string, string>
                {
                    ["scene"] = "res://Scenes/TsunamiTrigger.tscn",
                    ["spawnHazard"] = "hazard.tsunami",
                },
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
            new()
            {
                Id = "deco.caution_sign", DisplayName = "Caution Sign", Category = TileCategory.Decoration,
                FootprintW = 1, FootprintH = 2, AllowedRoles = LayerRoleMask.Any,
                EditorColor = "#E66B42",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/deco_caution_monkey.png",
            },
            new()
            {
                Id = "deco.sand_castle", DisplayName = "Sand Castle", Category = TileCategory.Decoration,
                FootprintW = 2, FootprintH = 2, AllowedRoles = LayerRoleMask.Any,
                EditorColor = "#DDBB68",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Legacy/deco_sand_castle.png",
            },
            new()
            {
                Id = "deco.sun", DisplayName = "Sun", Category = TileCategory.Decoration,
                FootprintW = 2, FootprintH = 2, AllowedRoles = LayerRoleMask.Any,
                EditorColor = "#FFD24A",
                SpriteSlot = "res://Sprites/MapCreation/Beach/Generated/deco_sun_chibi.png",
            },
            // A tall foreground occluder for the closeview "hide-behind" band (GDD §1 rework):
            // a Roman-style column. No art yet — renders as its editorColor quad until one lands.
            new()
            {
                Id = "deco.column", DisplayName = "Roman Column", Category = TileCategory.Decoration,
                FootprintW = 1, FootprintH = 3, AllowedRoles = LayerRoleMask.Any,
                EditorColor = "#CFC7B0",
            },

            // All three authored cloud silhouettes participate in deterministic generation.
            new()
            {
                Id = "rule.cloudZone", DisplayName = "Cloud Zone (rule)", Category = TileCategory.Rule,
                AllowedRoles = LayerRoleMask.Any,
                EditorColor = "#DDA0DD",
                RuleProps = new RuleProps
                {
                    SpawnTable = new List<(string DefId, int Weight)>
                    {
                        ("softvolume.cloud2x1", 3),
                        ("softvolume.cloud1x1", 1),
                        ("softvolume.cloud3x2", 1),
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

            if (def.SoftVolumeTuning != null)
            {
                if (def.Category != TileCategory.SoftVolume)
                    problems.Add($"{def.Id}: SoftVolumeTuning is valid only for SoftVolume tiles");

                if (def.SoftVolumeTuning.StagnationIndex.HasValue &&
                    (def.SoftVolumeTuning.StagnationIndex.Value < 0f ||
                     def.SoftVolumeTuning.StagnationIndex.Value > 1f))
                    problems.Add($"{def.Id}: SoftVolumeTuning.StagnationIndex must be within 0..1");
            }

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
