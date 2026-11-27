using System;
using System.Collections.Generic;
using Fableland.Data;
using Fableland.Run;

namespace Fableland.Shelter;

/// <summary>
/// Everything a station's behaviour may ask about the shelter it stands in, computed once per
/// refresh by the shelter screen. <see cref="Exits"/> is <c>ShelterController.Exits</c> — the
/// one rule for which ways out are open — so a station never re-derives it.
/// </summary>
public sealed class ShelterVisit
{
    /// <summary>The screen: its dialogs, Team Build, the status line, a panel of your own.</summary>
    public global::ShelterController Shelter { get; init; }

    /// <summary>The run; null in a shelter launched by itself (F5), where actions do nothing.</summary>
    public RunState Run { get; init; }

    public string NodeId { get; init; } = "";
    public ShelterKind Kind { get; init; }
    public bool Blessed { get; init; }

    /// <summary>An Eidolon Shelter whose Blessing can still carry you across (always false
    /// elsewhere), and why not when it cannot.</summary>
    public bool CanCross { get; init; }
    public string CrossBlocked { get; init; }

    public int Stamina { get; init; }
    public (bool Back, bool Finish, bool Blessings) Exits { get; init; }
}

/// <summary>What a station offers now: whether it can be used, the line that says what
/// using it does, and — when it cannot — why not.</summary>
public readonly record struct StationOffer(bool Enabled, string Line, string Reason = "")
{
    public static StationOffer Open(string line) => new(true, line);
    public static StationOffer Closed(string line, string reason) => new(false, line, reason);
}

/// <summary>What a station does: what it offers in this visit, and what using it does.
/// <see cref="LeavesScreen"/> marks the way out (its use sounds like a Back button).</summary>
public sealed class ShelterStationBehaviour
{
    public Func<ShelterVisit, StationOffer> Offer { get; init; }
    public Action<ShelterVisit> Use { get; init; }
    public bool LeavesScreen { get; init; }
}

/// <summary>
/// The behaviours behind the stations of <see cref="ShelterTable"/> (NODES §5.0), by station
/// id. A station is built in a shelter only when its row stands in that kind of shelter, its
/// layout gives it a spot, and a behaviour is registered here — so the reserved spots (the
/// plantation, the enchanting altar, the wanderer) wait empty until their systems register
/// one: a line in <see cref="Builtins"/>, or <see cref="Register"/> from their own code.
/// </summary>
public static class ShelterStations
{
    public const string BlessingEndsTheDay = "uses the Blessing and ends the day";
    public const string BlessingCrosses = "uses the Blessing and crosses the bridge";

    private static readonly Dictionary<string, ShelterStationBehaviour> Builtins = new(StringComparer.Ordinal)
    {
        [ShelterTable.Road] = new() { Offer = OfferRoad, Use = visit => visit.Shelter?.RequestLeave(), LeavesScreen = true },
        [ShelterTable.TeamBoard] = new()
        {
            Offer = _ => StationOffer.Open("Team Build — who fights next, and what they hold"),
            Use = visit => visit.Shelter?.OpenTeamBuild(),
        },
        [ShelterTable.Lantern] = new()
        {
            Offer = visit => visit.Exits.Finish ? StationOffer.Open("Put out the lantern — Finish the Day")
                : StationOffer.Closed("Finish the Day", "The day cannot end here."),
            Use = visit => visit.Shelter?.RequestFinishDay(),
        },
        [ShelterTable.Campfire] = Blessing("Rest — restore 30% max HP (excess → permanent max HP)", run => run.RestBlessing()),
        [ShelterTable.Whetstone] = Blessing("Sharpen Weapon — permanently +10 ATK", run => run.AddAtk(10)),
        [ShelterTable.Anvil] = Blessing("Sharpen Armor — permanently +10 DEF", run => run.AddDef(10)),
        // The wonder items' stations (ITEMS.gdd §4.3, §7.1; v0.12.9): each opens a screen of its
        // own over the room; neither spends the Blessing or ends the day.
        [ShelterTable.Plantation] = new()
        {
            Offer = OfferPlantation,
            Use = visit => visit.Shelter?.ShowPanel(PlantationPanel.Create(visit)),
        },
        [ShelterTable.EnchantingAltar] = new()
        {
            Offer = OfferAltar,
            Use = visit => visit.Shelter?.ShowPanel(EnchantPanel.Create(visit)),
        },
        // Reserved spot (ShelterTable): the Wanderer — its behaviour arrives with the joust
        // (NODES §5.4).
    };

    /// <summary>The plantation: always open at a hub; its line says how the plots stand.</summary>
    private static StationOffer OfferPlantation(ShelterVisit visit)
    {
        if (visit.Run == null) return StationOffer.Closed("Tend the plots", "There is no run to plant for.");
        int ripe = 0, growing = 0, empty = 0;
        foreach (PlotState plot in visit.Run.Ledger.PlotsAt(visit.NodeId))
        {
            if (plot.Empty) empty++;
            else if (plot.Ripe) ripe++;
            else growing++;
        }
        var parts = new List<string>();
        if (ripe > 0) parts.Add($"{ripe} ripe");
        if (growing > 0) parts.Add($"{growing} growing");
        if (empty > 0) parts.Add($"{empty} empty");
        return StationOffer.Open($"Tend the plots — {string.Join(", ", parts)}");
    }

    /// <summary>The enchanting altar: open while a team member holds the Blessing of Shieldon.</summary>
    private static StationOffer OfferAltar(ShelterVisit visit)
    {
        const string line = "Enchant Possession onto a wonder item — spends the Blessing of Shieldon";
        if (visit.Run == null) return StationOffer.Closed(line, "There is no run.");
        return visit.Run.TeamHolderOf(RunState.BlessingOfShieldonId) != null
            ? StationOffer.Open(line)
            : StationOffer.Closed(line, "Equip the Blessing of Shieldon on a fighter in the team (at the team board) to use the altar.");
    }

    private static readonly Dictionary<string, ShelterStationBehaviour> Registered = new(StringComparer.Ordinal);

    /// <summary>The behaviour of a station, or null — and a station with none is not built.</summary>
    public static ShelterStationBehaviour Get(string stationId)
    {
        if (string.IsNullOrWhiteSpace(stationId)) return null;
        if (Registered.TryGetValue(stationId, out ShelterStationBehaviour behaviour)) return behaviour;
        return Builtins.TryGetValue(stationId, out behaviour) ? behaviour : null;
    }

    /// <summary>Gives a station its behaviour (a system that brings its own: the plantation, the
    /// enchanting altar). Wins over a built-in of the same id.</summary>
    public static void Register(string stationId, ShelterStationBehaviour behaviour)
    {
        if (string.IsNullOrWhiteSpace(stationId) || behaviour?.Offer == null || behaviour.Use == null)
            throw new ArgumentException($"A shelter station needs an id, an offer and a use ('{stationId}').");
        Registered[stationId] = behaviour;
    }

    /// <summary>Takes back what <see cref="Register"/> gave (a check cleans up through here).</summary>
    public static void Unregister(string stationId)
    {
        if (!string.IsNullOrWhiteSpace(stationId)) Registered.Remove(stationId);
    }

    /// <summary>Every built-in behaviour must have a station row — validation for checks.</summary>
    public static IEnumerable<string> BuiltinIds => Builtins.Keys;

    /// <summary>The way out: back to the map (a hub), into the VOID (the Last Shelter), or back
    /// over the near shore (an Eidolon Shelter, once its crossing is spent or blocked).</summary>
    private static StationOffer OfferRoad(ShelterVisit visit)
    {
        string line = visit.Kind == ShelterKind.Last ? "Continue into the VOID" : "Back to the map";
        if (visit.Exits.Back) return StationOffer.Open(line);
        return StationOffer.Closed(line, visit.Kind == ShelterKind.Eidolon
            ? "This bridge's Blessing is the way across — rest or sharpen."
            : "No stamina left — finish the day at the lantern.");
    }

    /// <summary>Rest and both Sharpens: the shelter's Blessing. At a hub or the Last Shelter it
    /// ends the day; at an Eidolon Shelter it carries you across (the screen decides which).</summary>
    private static ShelterStationBehaviour Blessing(string effect, Action<RunState> apply) => new()
    {
        Offer = visit =>
        {
            string line = $"{effect} · {(visit.Kind == ShelterKind.Eidolon ? BlessingCrosses : BlessingEndsTheDay)}";
            if (visit.Exits.Blessings) return StationOffer.Open(line);
            string reason = visit.Kind != ShelterKind.Eidolon ? "This shelter's Blessing is spent."
                : visit.Blessed ? visit.CrossBlocked ?? "The bridge cannot be crossed."
                : "The bridge has been crossed.";
            return StationOffer.Closed(line, reason);
        },
        Use = visit => visit.Shelter?.RequestBlessing(effect, () => { if (visit.Run != null) apply(visit.Run); }),
    };
}
