using System;

namespace Fableland.Data;

/// <summary>
/// Pure data for "?" (question-mark) node events (T30 §7 / NODES §6). NO Godot scene types here —
/// an <see cref="EventDef"/> is an ordered list of <see cref="EventPage"/>s; each page offers
/// <see cref="EventChoice"/>s that apply a small verb union (<see cref="EventEffect"/>) and either
/// end the event or advance to another page. One generic interpreter (Scripts/Nodes/EventController.cs)
/// renders whatever is authored here — an event author composes verbs, never writes code.
/// </summary>
public enum EventEffectKind { GrantCores, HealParty, GrantItem, Nothing }

/// <summary>One verb. Prototype set (T30 §7): GrantCores(int), HealParty(float ratio),
/// GrantItem(string defId), Nothing (an explicit no-op choice, e.g. "walk away").</summary>
public sealed class EventEffect
{
    public EventEffectKind Kind;
    public int IntArg;       // GrantCores: wonder-core amount
    public float FloatArg;   // HealParty: HP ratio (0..1)
    public string StringArg; // GrantItem: item DefId

    public static EventEffect GrantCores(int amount) => new() { Kind = EventEffectKind.GrantCores, IntArg = amount };
    public static EventEffect HealParty(float ratio) => new() { Kind = EventEffectKind.HealParty, FloatArg = ratio };
    public static EventEffect GrantItem(string defId) => new() { Kind = EventEffectKind.GrantItem, StringArg = defId };
    public static EventEffect Nothing() => new() { Kind = EventEffectKind.Nothing };
}

/// <summary>One button on an <see cref="EventPage"/>.</summary>
public sealed class EventChoice
{
    public string Label;
    public EventEffect[] Effects = Array.Empty<EventEffect>();
    public string ResultText;
    public int NextPage = -1; // -1 (default) = the event ends after this choice
}

/// <summary>One page of an event: body text + the choices offered on it.</summary>
public sealed class EventPage
{
    public string Text;
    public EventChoice[] Choices = Array.Empty<EventChoice>();
}

/// <summary>An authored "?" event: id + title + ordered pages.</summary>
public sealed class EventDef
{
    public string Id;
    public string Title;
    public EventPage[] Pages = Array.Empty<EventPage>();
}

/// <summary>
/// The event registry. Prototype content (v0.5.0): 3 placeholder events exercising the pipeline —
/// (a) single-page 2-choice (cores vs heal — the original traveller event), (b) 2-page chaining,
/// (c) GrantItem + an explicit "Nothing" walk-away choice. Picked deterministically per node via
/// <c>RunState.Instance.Rng.Sub("event:" + nodeId)</c> (EventController).
/// </summary>
public static class EventDefs
{
    public static readonly EventDef[] All =
    {
        // (a) single page, 2 choices — the original traveller event.
        new EventDef
        {
            Id = "traveller",
            Title = "A Traveller's Offer",
            Pages = new[]
            {
                new EventPage
                {
                    Text = "A traveller offers you a choice at the crossroads.",
                    Choices = new[]
                    {
                        new EventChoice
                        {
                            Label = "Take the coin pouch (+5 wonder cores)",
                            Effects = new[] { EventEffect.GrantCores(5) },
                            ResultText = "You take the coin pouch.  +5 wonder cores.",
                        },
                        new EventChoice
                        {
                            Label = "Accept the healing draught (+20% HP)",
                            Effects = new[] { EventEffect.HealParty(0.20f) },
                            ResultText = "You accept the healing draught.  +20% HP.",
                        },
                    },
                },
            },
        },

        // (b) 2 pages — proves page chaining: the first choice leads to a second page whose
        // choices differ from the first; the "leave it be" choice ends the event immediately.
        new EventDef
        {
            Id = "old_shrine",
            Title = "The Old Shrine",
            Pages = new[]
            {
                new EventPage
                {
                    Text = "You find a moss-covered shrine humming with faint light. Do you approach?",
                    Choices = new[]
                    {
                        new EventChoice
                        {
                            Label = "Approach the shrine",
                            Effects = new[] { EventEffect.Nothing() },
                            ResultText = "You step closer; the humming grows louder.",
                            NextPage = 1,
                        },
                        new EventChoice
                        {
                            Label = "Leave it be",
                            Effects = new[] { EventEffect.Nothing() },
                            ResultText = "You decide not to tempt fate and walk on.",
                        },
                    },
                },
                new EventPage
                {
                    Text = "The shrine offers two paths: a blessing, or a boon of cores.",
                    Choices = new[]
                    {
                        new EventChoice
                        {
                            Label = "Accept the blessing (+15% HP)",
                            Effects = new[] { EventEffect.HealParty(0.15f) },
                            ResultText = "Warmth spreads through the party.  +15% HP.",
                        },
                        new EventChoice
                        {
                            Label = "Take the boon (+8 wonder cores)",
                            Effects = new[] { EventEffect.GrantCores(8) },
                            ResultText = "Cores spill into your pouch.  +8 wonder cores.",
                        },
                    },
                },
            },
        },

        // (c) GrantItem + an explicit Nothing "walk away" choice.
        new EventDef
        {
            Id = "abandoned_cart",
            Title = "An Abandoned Cart",
            Pages = new[]
            {
                new EventPage
                {
                    Text = "An overturned cart lies in the road, its cargo scattered across the dirt.",
                    Choices = new[]
                    {
                        new EventChoice
                        {
                            Label = "Search the wreckage",
                            Effects = new[] { EventEffect.GrantItem("placeholder") },
                            ResultText = "You find a curious trinket among the debris.",
                        },
                        new EventChoice
                        {
                            Label = "Walk away",
                            Effects = new[] { EventEffect.Nothing() },
                            ResultText = "You leave the wreckage undisturbed.",
                        },
                    },
                },
            },
        },
    };
}
