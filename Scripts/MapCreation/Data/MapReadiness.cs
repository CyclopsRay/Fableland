namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// Small read-only questions about a <see cref="MapDocument"/> that several callers ask
/// (the browser's cards, the thumbnail renderer, the content check), kept in one place so
/// they cannot answer differently. Pure C# — no Godot.
/// </summary>
public static class MapDocumentQueries
{
    /// <summary>The battlefield layer, or null. (`MapJson.Load` always injects one.)</summary>
    public static MapLayerData Battlefield(MapDocument doc)
    {
        if (doc?.Layers == null) return null;
        foreach (MapLayerData layer in doc.Layers)
            if (layer != null && layer.Role == MapLayerData.RoleBattlefield) return layer;
        return null;
    }

    /// <summary>Cell of the first Character Spawn on the battlefield, if any.</summary>
    public static bool TryGetCharacterSpawnCell(MapDocument doc, out int x, out int y)
    {
        x = y = 0;
        if (!TryGetCharacterSpawn(doc, out PlacedTile tile, out _)) return false;
        x = tile.X;
        y = tile.Y;
        return true;
    }

    /// <summary>Where the entering character is placed, in battlefield pixels: the first
    /// Character Spawn's marker point (<see cref="TilePlacement.Marker"/>), nudge included.
    /// It is also the parallax anchor, so the Playtest, the arena and the map cards must
    /// all read it from here.</summary>
    public static bool TryGetCharacterSpawnPoint(MapDocument doc, out float x, out float y)
    {
        x = y = 0f;
        if (!TryGetCharacterSpawn(doc, out PlacedTile tile, out TileDef def)) return false;
        (x, y) = TilePlacement.Marker(tile, def);
        return true;
    }

    private static bool TryGetCharacterSpawn(MapDocument doc, out PlacedTile spawn, out TileDef spawnDef)
    {
        spawn = null;
        spawnDef = null;
        MapLayerData field = Battlefield(doc);
        if (field?.Tiles == null) return false;
        foreach (PlacedTile tile in field.Tiles)
        {
            if (tile == null || !TileRegistry.TryGet(tile.DefId, out TileDef def) ||
                def.Category != TileCategory.Character) continue;
            spawn = tile;
            spawnDef = def;
            return true;
        }
        return false;
    }

    /// <summary>Total placed tiles across every layer.</summary>
    public static int TileCount(MapDocument doc)
    {
        int count = 0;
        if (doc?.Layers == null) return 0;
        foreach (MapLayerData layer in doc.Layers)
            if (layer?.Tiles != null) count += layer.Tiles.Count;
        return count;
    }
}

/// <summary>
/// Is this map ready to be fought on? The one definition of "finished" (MapCreation.gdd
/// §7.8): the browser hides maps that are not, its clean-up archives them, and the headless
/// content check fails a built-in map that is not. Reasons are short phrases meant to be read
/// on a card ("no character spawn"). An empty list means ready.
///
/// It checks what a fight cannot do without, and nothing about quality: something to stand
/// on, where the player starts, where foes come from, and — for every goal except Slaughter,
/// which has no use for one — a Level Goal marker (§9).
/// </summary>
public static class MapReadiness
{
    public static List<string> Problems(MapDocument doc)
    {
        var problems = new List<string>();
        MapLayerData field = MapDocumentQueries.Battlefield(doc);
        if (field == null)
        {
            problems.Add("no battlefield");
            return problems;
        }

        int total = 0, ground = 0, spawns = 0, nests = 0, goals = 0;
        if (field.Tiles != null)
        {
            foreach (PlacedTile tile in field.Tiles)
            {
                if (tile == null || !TileRegistry.TryGet(tile.DefId, out TileDef def)) continue;
                total++;
                switch (def.Category)
                {
                    case TileCategory.Ground: ground++; break;
                    case TileCategory.Character: spawns++; break;
                    case TileCategory.EnemySpawn: nests++; break;
                    case TileCategory.LevelGoal: goals++; break;
                }
            }
        }

        if (total == 0)
        {
            problems.Add("empty");
            return problems;
        }
        if (ground == 0) problems.Add("no ground");
        if (spawns == 0) problems.Add("no character spawn");
        else if (spawns > 1) problems.Add("more than one character spawn");
        if (nests == 0) problems.Add("no enemy spawn");

        bool wantsGoalMarker = doc.Goals == null || doc.Goals.Count == 0 ||
            doc.Goals.Exists(goal => goal != CombatMapGoals.Slaughter);
        if (wantsGoalMarker && goals == 0) problems.Add("no level goal marker");
        return problems;
    }

    public static bool IsReady(MapDocument doc) => Problems(doc).Count == 0;

    /// <summary>Structural guard over the cases the browser relies on.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("MapReadiness mismatch: " + what); }

        var empty = MapDocument.CreateNew("empty");
        Check(Problems(empty).Contains("empty"), "a new map is empty");

        var pieces = MapDocument.CreateNew("pieces");
        MapLayerData field = MapDocumentQueries.Battlefield(pieces);
        field.Tiles.Add(new PlacedTile { DefId = "ground.stone", X = 0, Y = 10 });
        List<string> found = Problems(pieces);
        Check(found.Contains("no character spawn") && found.Contains("no enemy spawn") &&
              found.Contains("no level goal marker") && !found.Contains("no ground"),
            "ground only: missing spawn, nest and goal");

        field.Tiles.Add(new PlacedTile { DefId = "spawn.character", X = 1, Y = 9 });
        field.Tiles.Add(new PlacedTile { DefId = "spawn.enemy", X = 5, Y = 9 });
        Check(Problems(pieces).Count == 1 && Problems(pieces)[0] == "no level goal marker",
            "claim map without a goal marker");

        pieces.Goals = new List<string> { CombatMapGoals.Slaughter };
        Check(IsReady(pieces), "a slaughter-only map needs no goal marker");

        pieces.Goals = new List<string> { CombatMapGoals.Claim };
        field.Tiles.Add(new PlacedTile { DefId = "goal.level", X = 3, Y = 9 });
        Check(IsReady(pieces), "ground + spawn + nest + goal is ready");
        Check(MapDocumentQueries.TryGetCharacterSpawnCell(pieces, out int sx, out int sy) && sx == 1 && sy == 9,
            "character spawn cell");
        return failures;
    }
}
