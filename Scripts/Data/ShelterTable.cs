using System;
using System.Collections.Generic;

namespace Fableland.Data;

/// <summary>The three kinds of shelter the map makes (NODES.gdd §5): a Transportation Hub in
/// every outer realm, the Eidolon Shelter at a TwistedReality bridge's midpoint, and the
/// zone-6 Last Shelter past the singularity.</summary>
public enum ShelterKind
{
    Hub,
    Eidolon,
    Last,
}

/// <summary>Which kinds of shelter a station stands in.</summary>
[Flags]
public enum ShelterKinds
{
    None = 0,
    Hub = 1,
    Eidolon = 2,
    Last = 4,
    All = Hub | Eidolon | Last,
}

/// <summary>
/// One station of a shelter (NODES.gdd §5.0): a place in the room the player walks up to and
/// uses with the interact key. This row is what a station IS — its name, the action the old
/// button panel offered, where it may stand and how wide it is. What it DOES is a behaviour
/// registered under the same id (<c>ShelterStations</c>, orchestration): a station with no
/// behaviour is not built, so a layout can reserve a spot before its action exists.
/// </summary>
public sealed class ShelterStationDef
{
    /// <summary>Stable snake_case id (T20 §1) — what a layout spot and a behaviour name.</summary>
    public string Id { get; init; }

    /// <summary>The place, as the player reads it above the station ("Campfire").</summary>
    public string Name { get; init; }

    /// <summary>The action it offers, as the old button said it ("Rest").</summary>
    public string Action { get; init; }

    /// <summary>The kinds of shelter that have this station.</summary>
    public ShelterKinds OfferedAt { get; init; } = ShelterKinds.All;

    /// <summary>Width of the station in metres: its placeholder's footprint and the stretch
    /// of floor where the player stands to use it.</summary>
    public float WidthM { get; init; } = 3f;

    /// <summary>What the code draws until its art exists — the TODO(art) line for it.</summary>
    public string Placeholder { get; init; } = "";

    public bool StandsIn(ShelterKind kind) => (OfferedAt & KindFlag(kind)) != 0;

    public static ShelterKinds KindFlag(ShelterKind kind) => kind switch
    {
        ShelterKind.Hub => ShelterKinds.Hub,
        ShelterKind.Eidolon => ShelterKinds.Eidolon,
        ShelterKind.Last => ShelterKinds.Last,
        _ => ShelterKinds.None,
    };
}

/// <summary>Where one station stands in a layout: its centre, metres from the left wall.</summary>
public readonly record struct ShelterSpot(string StationId, float XM);

/// <summary>
/// One shelter's room: how big it is, where the player comes in, and where each station
/// stands. A world may have a layout of its own (<see cref="WorldAbbr"/>); every other world
/// uses the default. Metres, floor at 0, x from the left wall (32 px/m — <c>Units</c>).
/// </summary>
public sealed class ShelterLayoutDef
{
    public string Id { get; init; }

    /// <summary>The world whose shelters use this room; empty for the default.</summary>
    public string WorldAbbr { get; init; } = "";

    public float WidthM { get; init; }

    /// <summary>Floor to ceiling.</summary>
    public float HeightM { get; init; }

    /// <summary>Where the player stands on arrival — just inside, by the road.</summary>
    public float SpawnXM { get; init; }

    public IReadOnlyList<ShelterSpot> Spots { get; init; } = Array.Empty<ShelterSpot>();
}

/// <summary>
/// Read-only registry of shelter stations and rooms (T20 §1; NODES.gdd §5.0). One room now —
/// every world's shelter will differ later, and a world's own is one more row in
/// <see cref="Layouts"/>. Adding a station is a row in <see cref="Stations"/>, a spot in a
/// layout and a behaviour registered under its id; nothing else changes.
/// </summary>
public static class ShelterTable
{
    // The stations behind today's six buttons.
    public const string Road = "road";
    public const string TeamBoard = "team_board";
    public const string Lantern = "lantern";
    public const string Campfire = "campfire";
    public const string Whetstone = "whetstone";
    public const string Anvil = "anvil";
    // Reserved: their actions arrive later (items: planting, possession enchanting — ITEMS.gdd;
    // the wanderer's joust — NODES §5.4). Until a behaviour is registered they are not built.
    public const string Plantation = "plantation";
    public const string EnchantingAltar = "enchanting_altar";
    public const string Wanderer = "wanderer";

    private static readonly ShelterStationDef[] StationRows =
    {
        new() { Id = Road, Name = "The road", Action = "Back to the map", WidthM = 3f,
            Placeholder = "a doorway in the left wall and a signpost (drawn in code)" },
        new() { Id = TeamBoard, Name = "Team board", Action = "Team Build", WidthM = 3.5f,
            Placeholder = "a notice board pinned with the team's own mugshots (drawn in code; the mugshots are real art)" },
        new() { Id = Lantern, Name = "Lantern", Action = "Finish the Day", WidthM = 2.5f,
            OfferedAt = ShelterKinds.Hub | ShelterKinds.Last,
            Placeholder = "a lantern hung on a post (drawn in code)" },
        new() { Id = Campfire, Name = "Campfire", Action = "Rest", WidthM = 3.5f,
            Placeholder = "two crossed logs, a flame of three triangles and a bedroll (drawn in code)" },
        new() { Id = Whetstone, Name = "Whetstone", Action = "Sharpen Weapon", WidthM = 3f,
            Placeholder = "a grinding wheel on a wooden frame (drawn in code)" },
        new() { Id = Anvil, Name = "Anvil", Action = "Sharpen Armor", WidthM = 3f,
            Placeholder = "an anvil on a stump (drawn in code)" },
        new() { Id = Plantation, Name = "Plantation", Action = "Tend the plots", WidthM = 5f,
            OfferedAt = ShelterKinds.Hub,
            Placeholder = "two plots of tilled earth (drawn in code once its behaviour exists)" },
        new() { Id = EnchantingAltar, Name = "Enchanting altar", Action = "Enchant", WidthM = 3f,
            OfferedAt = ShelterKinds.Hub,
            Placeholder = "a stone altar with a glyph (drawn in code once its behaviour exists)" },
        new() { Id = Wanderer, Name = "Wanderer", Action = "Challenge to a joust", WidthM = 3f,
            OfferedAt = ShelterKinds.Hub,
            Placeholder = "the wanderer's own fighter, standing (once the joust exists)" },
    };

    /// <summary>The default room: 48 m by 16 m, entered by the road on the left. The everyday
    /// stations sit in the first half (the road, the team board, the lantern, then the three
    /// Blessings); the reserved spots fill the right half.</summary>
    public static readonly ShelterLayoutDef Default = new()
    {
        Id = "shelter_default",
        WorldAbbr = "",
        WidthM = 48f,
        HeightM = 16f,
        SpawnXM = 5.5f,
        Spots = new ShelterSpot[]
        {
            new(Road, 2.5f),
            new(TeamBoard, 8f),
            new(Lantern, 12.5f),
            new(Campfire, 17f),
            new(Whetstone, 22f),
            new(Anvil, 27f),
            new(Plantation, 33f),
            new(EnchantingAltar, 39f),
            new(Wanderer, 44f),
        },
    };

    private static readonly ShelterLayoutDef[] LayoutRows = { Default };

    public static IReadOnlyList<ShelterStationDef> Stations => StationRows;
    public static IReadOnlyList<ShelterLayoutDef> Layouts => LayoutRows;

    /// <summary>The station row with this id, or null.</summary>
    public static ShelterStationDef Station(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (ShelterStationDef row in StationRows)
            if (string.Equals(row.Id, id, StringComparison.Ordinal)) return row;
        return null;
    }

    /// <summary>The room a world's shelters use: its own, else the default.</summary>
    public static ShelterLayoutDef ForWorld(string worldAbbr)
    {
        if (!string.IsNullOrWhiteSpace(worldAbbr))
            foreach (ShelterLayoutDef row in LayoutRows)
                if (string.Equals(row.WorldAbbr, worldAbbr.Trim(), StringComparison.OrdinalIgnoreCase)) return row;
        return Default;
    }

    /// <summary>Boot validation (T10 §5): every violation, not just the first.</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShelterStationDef row in StationRows)
        {
            string name = string.IsNullOrWhiteSpace(row.Id) ? "(no id)" : row.Id;
            if (string.IsNullOrWhiteSpace(row.Id)) problems.Add("ShelterTable: a station has no id");
            else if (!ids.Add(row.Id)) problems.Add($"ShelterTable: station '{row.Id}' is listed twice");
            if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Action)) problems.Add($"ShelterTable {name}: no name or no action");
            if (row.OfferedAt == ShelterKinds.None) problems.Add($"ShelterTable {name}: stands in no kind of shelter");
            if (!(row.WidthM >= 1f)) problems.Add($"ShelterTable {name}: a station is at least 1 m wide");
            if (string.IsNullOrWhiteSpace(row.Placeholder)) problems.Add($"ShelterTable {name}: no TODO(art) placeholder line");
        }

        var worlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ShelterLayoutDef layout in LayoutRows)
        {
            string name = string.IsNullOrWhiteSpace(layout.Id) ? "(no id)" : layout.Id;
            if (!worlds.Add(layout.WorldAbbr ?? "")) problems.Add($"ShelterTable {name}: world '{layout.WorldAbbr}' already has a room");
            if (!(layout.WidthM >= 20f) || !(layout.HeightM >= 10f)) problems.Add($"ShelterTable {name}: a room is at least 20 m by 10 m (a fighter jumps 8 m)");
            if (layout.SpawnXM < 1f || layout.SpawnXM > layout.WidthM - 1f) problems.Add($"ShelterTable {name}: the arrival point is not inside the room");

            var placed = new List<(string Id, float Left, float Right)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShelterSpot spot in layout.Spots ?? Array.Empty<ShelterSpot>())
            {
                ShelterStationDef station = Station(spot.StationId);
                if (station == null) { problems.Add($"ShelterTable {name}: '{spot.StationId}' is not a station"); continue; }
                if (!seen.Add(spot.StationId)) problems.Add($"ShelterTable {name}: '{spot.StationId}' stands twice");
                float left = spot.XM - station.WidthM / 2f, right = spot.XM + station.WidthM / 2f;
                if (left < 0.5f || right > layout.WidthM - 0.5f) problems.Add($"ShelterTable {name}: '{spot.StationId}' ({left:0.#}–{right:0.#} m) is not inside the room's walls");
                foreach ((string other, float otherLeft, float otherRight) in placed)
                    if (left < otherRight && otherLeft < right) problems.Add($"ShelterTable {name}: '{spot.StationId}' and '{other}' overlap — standing there would mean both");
                placed.Add((spot.StationId, left, right));
            }
            // Every kind of shelter needs its way out and its Blessings in every room.
            foreach (string needed in new[] { Road, TeamBoard, Lantern, Campfire, Whetstone, Anvil })
                if (!seen.Contains(needed)) problems.Add($"ShelterTable {name}: has no '{needed}' — a shelter's own actions must all have a place");
        }
        return problems;
    }
}
