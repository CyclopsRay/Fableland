namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// GDD §2.2 (T20 registry pattern) — the code-defined table of tile kinds.
/// Adding a tile kind means adding an entry, never editing a switch (T00 rule 1).
/// Built once in the static constructor; read-only after boot. Pure C# — no Godot.
///
/// The table is this file's shared/beach list followed by one realm tile set per file
/// under `Data/TileSets/` (see <see cref="TileSets"/> and `Docs/Art/RealmStyleBible.md`).
/// The set list in the constructor is explicit and ordered: a realm's tiles are live only
/// once its `Defs()` is named there — still no scan-on-boot.
/// </summary>
public static class TileRegistry
{
    private static readonly List<TileDef> _order;
    private static readonly Dictionary<string, TileDef> _defs;
    private static readonly List<string> _duplicateIds = new();

    /// <summary>Ordered enumeration for the palette (GDD §7.1).</summary>
    public static IReadOnlyList<TileDef> All => _order;

    static TileRegistry()
    {
        _order = new List<TileDef>
        {
            new()
            {
                Id = "ground.grass", DisplayName = "Grass", Category = TileCategory.Ground,
                TileSet = TileSets.VanillaKingdom,
                EditorColor = "#76B947", AutotileGroup = "terrain.coastal_grass",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Ground/ground_grass_seamless.png",
                SpriteFillFootprint = true,
                Props = new Dictionary<string, string>
                {
                    [TileProps.ArtSource] = "res://Assets/Sprites/Tiles/VanillaKingdom/Ground/terrain_beach_atlas.png",
                },
            },
            new()
            {
                Id = "ground.sand", DisplayName = "Beach Sand", Category = TileCategory.Ground,
                TileSet = TileSets.VanillaKingdom,
                EditorColor = "#E8C878", AutotileGroup = "terrain.sand_hill",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Ground/ground_sand_seamless.png",
                SpriteFillFootprint = true,
                Props = new Dictionary<string, string>
                {
                    [TileProps.ArtSource] = "res://Assets/Sprites/Tiles/VanillaKingdom/Ground/terrain_sand_hill_atlas.png",
                    [TileProps.AutotileKind] = HillAutotile.KindSheet,
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
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 2, FootprintH = 1, EditorColor = "#B88755",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Platform/platform_bench.png",
            },
            new()
            {
                Id = "platform.sun_lounger", DisplayName = "Sun Lounger", Category = TileCategory.Platform,
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 3, FootprintH = 1, EditorColor = "#C8A675",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Platform/platform_sun_lounger.png",
            },
            new()
            {
                Id = "platform.lifeguard_tower", DisplayName = "Lifeguard Tower", Category = TileCategory.Platform,
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 4, FootprintH = 8, EditorColor = "#4F9B9B",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Platform/platform_lifeguard_tower.png",
            },

            new()
            {
                Id = "softvolume.bush1x1", DisplayName = "Bush", Category = TileCategory.SoftVolume,
                EditorColor = "#2E8B57",
            },
            new()
            {
                Id = "softvolume.cloud1x1", DisplayName = "Cloud (1x1)", Category = TileCategory.SoftVolume,
                TileSet = TileSets.VanillaKingdom,
                EditorColor = "#B0E0E6",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/SoftVolume/softvolume_cloud_small.png",
                SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = 0.3f },
            },
            new()
            {
                Id = "softvolume.cloud2x1", DisplayName = "Cloud (2x1)", Category = TileCategory.SoftVolume,
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 2, FootprintH = 1, EditorColor = "#ADD8E6",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/SoftVolume/softvolume_cloud_medium.png",
                SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = 0.3f },
            },
            new()
            {
                Id = "softvolume.cloud3x2", DisplayName = "Cloud (3x2)", Category = TileCategory.SoftVolume,
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 3, FootprintH = 2, EditorColor = "#A9D7E3",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/SoftVolume/softvolume_cloud_large.png",
                SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = 0.3f },
            },
            new()
            {
                Id = "softvolume.palm_tree", DisplayName = "Palm Tree", Category = TileCategory.SoftVolume,
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 3, FootprintH = 4, EditorColor = "#4E9A51",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/SoftVolume/softvolume_palm_tree_v2.png",
                SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = 0.1f },
            },

            new()
            {
                Id = "hazard.bonfire", DisplayName = "Bonfire", Category = TileCategory.Hazard,
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 2, FootprintH = 1, EditorColor = "#FF4500",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Hazard/hazard_bonfire_flat.png",
                // Deliberately catches only the hot center, not the full 2 m art width.
                EffectArea = ShapeDef.Circle(MapGrid.PixelsPerCell, MapGrid.PixelsPerCell * 0.65f,
                    MapGrid.PixelsPerCell * 0.35f),
                Props = new Dictionary<string, string> { [TileProps.Scene] = "res://Scenes/FireHazard.tscn" },
            },
            new()
            {
                Id = "hazard.freezepit", DisplayName = "Freeze Pit", Category = TileCategory.Hazard,
                EditorColor = "#66CCFF",
                EffectArea = ShapeDef.Rect(0f, 0f, MapGrid.PixelsPerCell, MapGrid.PixelsPerCell),
                Props = new Dictionary<string, string> { [TileProps.Scene] = "res://Scenes/FreezeHazard.tscn" },
            },
            new()
            {
                Id = "hazard.tsunami_trigger", DisplayName = "Tsunami Trigger", Category = TileCategory.Hazard,
                TileSet = TileSets.VanillaKingdom,
                EditorColor = "#287DB5",
                EffectArea = ShapeDef.Rect(0f, 0f, MapGrid.PixelsPerCell, MapGrid.PixelsPerCell),
                Props = new Dictionary<string, string>
                {
                    [TileProps.Scene] = "res://Scenes/TsunamiTrigger.tscn",
                    [TileProps.SpawnHazard] = "hazard.tsunami",
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
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 1, FootprintH = 2, AllowedRoles = LayerRoleMask.Any,
                EditorColor = "#E66B42",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Decoration/deco_caution_monkey.png",
            },
            new()
            {
                Id = "deco.sand_castle", DisplayName = "Sand Castle", Category = TileCategory.Decoration,
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 2, FootprintH = 2, AllowedRoles = LayerRoleMask.Any,
                EditorColor = "#DDBB68",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Decoration/deco_sand_castle.png",
            },
            new()
            {
                Id = "deco.sun", DisplayName = "Sun", Category = TileCategory.Decoration,
                TileSet = TileSets.VanillaKingdom,
                FootprintW = 2, FootprintH = 2, AllowedRoles = LayerRoleMask.Any,
                EditorColor = "#FFD24A",
                SpriteSlot = "res://Assets/Sprites/Tiles/VanillaKingdom/Decoration/deco_sun_chibi.png",
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
                TileSet = TileSets.VanillaKingdom,
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

        // Realm tile sets, in TileSets.All order.
        _order.AddRange(StarlandTileSet.Defs());
        _order.AddRange(WulinTileSet.Defs());
        _order.AddRange(DungeonTileSet.Defs());
        _order.AddRange(DesertedTileSet.Defs());
        _order.AddRange(LoongsNestTileSet.Defs());
        _order.AddRange(LoongTileSet.Defs());
        _order.AddRange(HorrorTileSet.Defs());
        _order.AddRange(ForgottenTileSet.Defs());

        // A duplicate id must not take the whole registry down with a type-initializer
        // exception (T00 rule 4): first registration wins, Validate() reports the rest.
        _defs = new Dictionary<string, TileDef>(_order.Count);
        for (int i = _order.Count - 1; i >= 0; i--)
        {
            TileDef def = _order[i];
            if (def == null || string.IsNullOrEmpty(def.Id)) { _order.RemoveAt(i); continue; }
        }
        for (int i = 0; i < _order.Count; i++)
        {
            if (_defs.TryAdd(_order[i].Id, _order[i])) continue;
            _duplicateIds.Add(_order[i].Id);
            _order.RemoveAt(i--);
        }
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

    /// <summary>Pure validation (no Godot): duplicate ids, unknown tile sets, hazards with no
    /// runtime scene, malformed strip-atlas tiles, footprints &gt;= 1, soft-volume tuning ranges,
    /// and that rule spawnTable defIds resolve with weights &gt; 0.</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();

        foreach (string id in _duplicateIds)
            problems.Add($"{id}: registered more than once; only the first definition is live");

        foreach (var def in _order)
        {
            if (def.FootprintW < 1 || def.FootprintH < 1)
                problems.Add($"{def.Id}: footprint must be >= 1x1 (got {def.FootprintW}x{def.FootprintH})");

            if (!TileSets.TryGet(def.TileSet, out _))
                problems.Add($"{def.Id}: unknown tile set '{def.TileSet}' (add it to TileSets.All)");

            // A hazard with no scene would paint in the editor and then do nothing in play.
            if (def.Category == TileCategory.Hazard &&
                (def.Props == null || !def.Props.TryGetValue(TileProps.Scene, out string scene) || string.IsNullOrWhiteSpace(scene)))
                problems.Add($"{def.Id}: Hazard tile declares no Props[\"{TileProps.Scene}\"] runtime scene");

            if (def.Props != null && def.Props.TryGetValue(TileProps.AutotileKind, out string kind) &&
                kind == HillAutotile.KindStrip)
            {
                if (def.FootprintW != 1 || def.FootprintH != 1)
                    problems.Add($"{def.Id}: a strip-atlas tile must be 1x1 (got {def.FootprintW}x{def.FootprintH})");
                if (string.IsNullOrEmpty(def.AutotileGroup))
                    problems.Add($"{def.Id}: a strip-atlas tile needs an AutotileGroup");
                if (!def.Props.ContainsKey(TileProps.ArtSource))
                    problems.Add($"{def.Id}: a strip-atlas tile needs Props[\"{TileProps.ArtSource}\"]");
                if (!def.Props.ContainsKey(TileProps.AutotileVariants))
                    problems.Add($"{def.Id}: a strip-atlas tile needs Props[\"{TileProps.AutotileVariants}\"]");
            }

            // A slope block (RealmTiles.Slope) is ground with a shaped top. It must say which
            // terrain it is part of, or the cells beside and under it draw a crest and a flank
            // against it; and its shape must fill its footprint's width, or blocks do not join.
            if (def.Category == TileCategory.Ground && def.EffectArea?.Kind == ShapeDef.KindPolygon)
            {
                if (string.IsNullOrEmpty(def.AutotileGroup))
                    problems.Add($"{def.Id}: a shaped Ground tile (a slope) needs the AutotileGroup of the terrain it belongs to");
                float[] pts = def.EffectArea.Points ?? System.Array.Empty<float>();
                float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i + 1 < pts.Length; i += 2)
                {
                    minX = System.Math.Min(minX, pts[i]);
                    maxX = System.Math.Max(maxX, pts[i]);
                    maxY = System.Math.Max(maxY, pts[i + 1]);
                }
                float cellPx = MapGrid.PixelsPerCell;
                if (pts.Length < 6 || System.Math.Abs(minX) > 0.01f || System.Math.Abs(maxX - def.FootprintW * cellPx) > 0.01f ||
                    System.Math.Abs(maxY - def.FootprintH * cellPx) > 0.01f)
                    problems.Add($"{def.Id}: a shaped Ground tile's polygon must span its footprint's full width and reach its bottom");
            }

            if (def.SoftVolumeTuning != null)
            {
                if (def.Category != TileCategory.SoftVolume)
                    problems.Add($"{def.Id}: SoftVolumeTuning is valid only for SoftVolume tiles");

                if (def.SoftVolumeTuning.StagnationIndex.HasValue &&
                    (def.SoftVolumeTuning.StagnationIndex.Value < 0f ||
                     def.SoftVolumeTuning.StagnationIndex.Value > 1f))
                    problems.Add($"{def.Id}: SoftVolumeTuning.StagnationIndex must be within 0..1");
                if (def.SoftVolumeTuning.GravityScale is float gravityScale && (gravityScale < 0f || gravityScale > 1f))
                    problems.Add($"{def.Id}: SoftVolumeTuning.GravityScale must be within 0..1");
                if (!def.SoftVolumeTuning.Medium && def.SoftVolumeTuning.FlowX == 0f && def.SoftVolumeTuning.FlowY == 0f)
                    problems.Add($"{def.Id}: a SoftVolume that is not a medium must carry (FlowX / FlowY), or it does nothing");
                if (def.SoftVolumeTuning.Drag is float drag && (drag <= 0f || drag > 16f))
                    problems.Add($"{def.Id}: SoftVolumeTuning.Drag must be above 0 and at most 16");
                if (def.SoftVolumeTuning.SinkSpeed is float sinkSpeed)
                {
                    if (sinkSpeed <= 0f || sinkSpeed > 32f)
                        problems.Add($"{def.Id}: SoftVolumeTuning.SinkSpeed must be above 0 and at most 32 m/s");
                    if (!def.SoftVolumeTuning.Medium)
                        problems.Add($"{def.Id}: only a medium can hold a sink (SinkSpeed on a pure current does nothing)");
                    // The medium holds the sink only while its resistance, g·(1−s)·drag, is at
                    // least the gravity it lets through, g·scale. Otherwise bodies outrun the cap.
                    float hold = (1f - (def.SoftVolumeTuning.StagnationIndex ?? 0.5f)) * (def.SoftVolumeTuning.Drag ?? 1f);
                    if (hold < (def.SoftVolumeTuning.GravityScale ?? 1f))
                        problems.Add($"{def.Id}: SinkSpeed cannot hold — (1 − StagnationIndex) × Drag = {hold:0.##} is below GravityScale; raise Drag or lower GravityScale");
                }
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
