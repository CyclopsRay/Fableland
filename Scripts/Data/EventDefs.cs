using System;

namespace Fableland.Data;

/// <summary>
/// Pure data for "?" (question-mark) node events (T30 §7 / NODES §6). NO Godot scene types here —
/// an <see cref="EventDef"/> is an ordered list of <see cref="EventPage"/>s; each page offers
/// <see cref="EventChoice"/>s that apply a small verb union (<see cref="EventEffect"/>) and either
/// end the event or advance to another page. One generic interpreter (Scripts/Nodes/EventController.cs)
/// renders whatever is authored here — an event author composes verbs, never writes code.
/// </summary>
public enum EventEffectKind { GrantCores, HealParty, GrantItem, Nothing, HurtParty, SpendCores, GrantAtk, GrantDef }

/// <summary>One verb. Prototype set (T30 §7): GrantCores(int), HealParty(float ratio),
/// GrantItem(string defId), Nothing (an explicit no-op choice, e.g. "walk away"). v0.12.6 adds
/// a cost and a price: HurtParty(float ratio) — every fighter loses that share of its max HP,
/// never below 5 % (an event never kills); SpendCores(int) — the choice can only be taken with
/// that many wonder cores, which it spends (the currency's first use, TBD #11); and a lasting
/// GrantAtk / GrantDef(int) for the whole roster, as a Slaughter's choice gives.</summary>
public sealed class EventEffect
{
    public EventEffectKind Kind;
    public int IntArg;       // GrantCores / SpendCores: wonder cores · GrantAtk / GrantDef: points
    public float FloatArg;   // HealParty / HurtParty: HP ratio (0..1)
    public string StringArg; // GrantItem: item DefId

    public static EventEffect GrantCores(int amount) => new() { Kind = EventEffectKind.GrantCores, IntArg = amount };
    public static EventEffect HealParty(float ratio) => new() { Kind = EventEffectKind.HealParty, FloatArg = ratio };
    public static EventEffect GrantItem(string defId) => new() { Kind = EventEffectKind.GrantItem, StringArg = defId };
    public static EventEffect Nothing() => new() { Kind = EventEffectKind.Nothing };
    public static EventEffect HurtParty(float ratio) => new() { Kind = EventEffectKind.HurtParty, FloatArg = ratio };
    public static EventEffect SpendCores(int amount) => new() { Kind = EventEffectKind.SpendCores, IntArg = amount };
    public static EventEffect GrantAtk(int points) => new() { Kind = EventEffectKind.GrantAtk, IntArg = points };
    public static EventEffect GrantDef(int points) => new() { Kind = EventEffectKind.GrantDef, IntArg = points };
}

/// <summary>One button on an <see cref="EventPage"/>.</summary>
public sealed class EventChoice
{
    public string Label;
    public EventEffect[] Effects = Array.Empty<EventEffect>();
    public string ResultText;
    public int NextPage = -1; // -1 (default) = the event ends after this choice

    /// <summary>Wonder cores this choice spends: it can only be taken with that many.</summary>
    public int CoresCost
    {
        get
        {
            int cost = 0;
            foreach (EventEffect fx in Effects) if (fx?.Kind == EventEffectKind.SpendCores) cost += fx.IntArg;
            return cost;
        }
    }
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
/// (c) GrantItem + an explicit "Nothing" walk-away choice. v0.12.6 adds twelve more — stray pages
/// of other storybooks (audit v0.11.1 #18: a run has 20–30 "?" nodes). Picked deterministically
/// per node via <c>RunState.Instance.Rng.Sub("event:" + nodeId)</c> (EventController).
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
                            Effects = new[] { EventEffect.GrantItem("pome_bravery") },
                            ResultText = "You find Pome's Bravery, warm with a sleeping spark.",
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

        // ── v0.12.6: stray pages of other storybooks, drifting through the fused worlds ──
        // Every page keeps one choice that costs nothing, so a broke or battered party is
        // never stuck (Validate checks it). Append only: the list's order is part of the pick.

        One("hare_and_tortoise", "A Race on the Road",
            "A hare and a tortoise are about to race. A crowd is taking wagers.",
            Choice("Back the hare (5 cores)", "The hare naps under a tree. The tortoise crosses the line. Your 5 cores are gone.",
                EventEffect.SpendCores(5)),
            Choice("Back the tortoise (5 cores)", "Slow and steady. The tortoise wins — you collect 15 wonder cores.",
                EventEffect.SpendCores(5), EventEffect.GrantCores(15)),
            Choice("Just watch", "You watch the race from the roadside and move on.", EventEffect.Nothing())),

        One("goldilocks", "Three Bowls on a Table",
            "A cottage in the woods, its door open. Three bowls of porridge steam on the table.",
            Choice("Taste the big bowl", "Too hot! It scalds the party. −10% HP.", EventEffect.HurtParty(0.10f)),
            Choice("Taste the middle bowl", "Too cold, and too salty. Nothing comes of it.", EventEffect.Nothing()),
            Choice("Taste the little bowl", "Just right. The warmth spreads through the party. +25% HP.", EventEffect.HealParty(0.25f))),

        One("stone_soup", "Stone Soup",
            "Travellers stir a pot with only a stone in it. \"It would be perfect with one more thing,\" they say.",
            Choice("Add something of yours (3 cores)", "Everyone adds a little; the soup is wonderful. +30% HP.",
                EventEffect.SpendCores(3), EventEffect.HealParty(0.30f)),
            Choice("Share what is already there", "Thin, but warm. +10% HP.", EventEffect.HealParty(0.10f))),

        One("fox_and_grapes", "Grapes on a High Vine",
            "Gold-skinned grapes hang from a vine far above the road.",
            Choice("Leap for them", "A hard fall, a full fist: the grapes are solid gold. −10% HP, +10 wonder cores.",
                EventEffect.HurtParty(0.10f), EventEffect.GrantCores(10)),
            Choice("They're probably sour", "You decide they are sour anyway, and walk on.", EventEffect.Nothing())),

        One("emperors_tailor", "A Tailor of Invisible Cloth",
            "A tailor offers armour \"so fine that fools cannot see it\". You see nothing at all.",
            Choice("Buy it (8 cores)", "You wear it proudly. It is exactly as protective as it looks. Your 8 cores are gone.",
                EventEffect.SpendCores(8)),
            Choice("Say there is nothing there", "A child in the crowd laughs with you; the tailor flees and drops his purse. +6 wonder cores.",
                EventEffect.GrantCores(6))),

        One("match_girl", "A Girl Selling Matches",
            "A small girl in the snow holds out a box of matches.",
            Choice("Buy a box (2 cores)", "You warm your hands together by the first match. +15% HP.",
                EventEffect.SpendCores(2), EventEffect.HealParty(0.15f)),
            Choice("Walk past", "You walk on into the cold.", EventEffect.Nothing())),

        One("pied_piper", "The Piper's Fee",
            "A piper in motley says he cleared this road of rats. He wants paying.",
            Choice("Pay him (4 cores)", "He tips his hat and plays you on your way. +10% HP.",
                EventEffect.SpendCores(4), EventEffect.HealParty(0.10f)),
            Choice("Refuse", "He shrugs and plays a different tune. Your feet ache all the way to the next road. −10% HP.",
                EventEffect.HurtParty(0.10f))),

        One("old_master", "An Old Master in the Bamboo",
            "An old master offers to train the party — hard, and only once.",
            Choice("Train with her", "Bruised but sharper. −15% HP, +5 ATK.", EventEffect.HurtParty(0.15f), EventEffect.GrantAtk(5)),
            Choice("Meditate with her", "Calmer, steadier. +5 DEF.", EventEffect.GrantDef(5)),
            Choice("Bow and go", "You bow and leave her to her bamboo.", EventEffect.Nothing())),

        One("wandering_merchant", "A Wandering Merchant",
            "A merchant with a cart of curiosities from a dozen storybooks. Everything has a price in wonder cores.",
            Choice("FanChen's Heart (12 cores)", "A cold, steady heart in a jar. FanChen's Heart joins your backpack.",
                EventEffect.SpendCores(12), EventEffect.GrantItem("fanchens_heart")),
            Choice("Yukai's Rope (10 cores)", "A coil of rope that always finds the wall. Yukai's Rope joins your backpack.",
                EventEffect.SpendCores(10), EventEffect.GrantItem("yukais_rope")),
            Choice("A healing draught (6 cores)", "Bitter, and it works. +35% HP.", EventEffect.SpendCores(6), EventEffect.HealParty(0.35f)),
            Choice("Just browse", "You look, and leave with nothing.", EventEffect.Nothing())),

        One("crossroads_smith", "A Smith at the Crossroads",
            "A smith works an anvil where four roads meet. \"Steel or plate — I've time for one.\"",
            Choice("Sharpen the weapons (10 cores)", "Every edge in the party bites harder. +5 ATK.", EventEffect.SpendCores(10), EventEffect.GrantAtk(5)),
            Choice("Temper the armour (10 cores)", "Every plate sits truer. +5 DEF.", EventEffect.SpendCores(10), EventEffect.GrantDef(5)),
            Choice("Not today", "You leave the smith to her anvil.", EventEffect.Nothing())),

        One("golden_goose", "A Goose That Glitters",
            "A goose with golden feathers waddles out of the reeds and stares at you.",
            Choice("Pluck a feather", "It honks, bites, and flees — but you keep the feather. −5% HP, +8 wonder cores.",
                EventEffect.HurtParty(0.05f), EventEffect.GrantCores(8)),
            Choice("Leave it be", "The goose watches you go.", EventEffect.Nothing())),

        One("sleeping_beauty_rose", "A Hedge of Thorns",
            "A wall of roses and thorns. Something glints in the tangle — and the scent makes you drowsy.",
            Choice("Cut through to the glint", "Thorns and a long sleep, then the glint: The Forgotten Kashaya. −20% HP.",
                EventEffect.HurtParty(0.20f), EventEffect.GrantItem("forgotten_kashaya")),
            Choice("Rest in the scent", "You doze in the roses. +20% HP.", EventEffect.HealParty(0.20f))),
    };

    private static EventDef One(string id, string title, string text, params EventChoice[] choices) => new()
    {
        Id = id,
        Title = title,
        Pages = new[] { new EventPage { Text = text, Choices = choices } },
    };

    private static EventChoice Choice(string label, string result, params EventEffect[] effects) => new()
    {
        Label = label,
        ResultText = result,
        Effects = effects,
    };

    /// <summary>
    /// Boot validation (T10 §5): every event has a page; every choice's next page exists; every
    /// item it grants is in the catalog; ids are unique; and every page keeps a choice that
    /// costs no cores, so a party with none is never stuck on it.
    /// </summary>
    public static System.Collections.Generic.List<string> Validate()
    {
        var problems = new System.Collections.Generic.List<string>();
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (EventDef def in All)
        {
            string name = def?.Id ?? "(no id)";
            if (def == null || string.IsNullOrWhiteSpace(def.Id)) { problems.Add("EventDefs: an event has no id"); continue; }
            if (!ids.Add(def.Id)) problems.Add($"EventDefs: '{def.Id}' is listed twice");
            if (def.Pages == null || def.Pages.Length == 0) { problems.Add($"EventDefs {name}: no pages"); continue; }
            for (int p = 0; p < def.Pages.Length; p++)
            {
                EventPage page = def.Pages[p];
                if (page?.Choices == null || page.Choices.Length == 0) { problems.Add($"EventDefs {name} page {p}: no choices"); continue; }
                bool free = false;
                foreach (EventChoice choice in page.Choices)
                {
                    if (choice.CoresCost == 0) free = true;
                    if (choice.NextPage >= def.Pages.Length) problems.Add($"EventDefs {name}: '{choice.Label}' leads to page {choice.NextPage}, which does not exist");
                    foreach (EventEffect fx in choice.Effects)
                        if (fx?.Kind == EventEffectKind.GrantItem && !Fableland.Items.ItemCatalog.Contains(fx.StringArg))
                            problems.Add($"EventDefs {name}: '{choice.Label}' grants unknown item '{fx.StringArg}'");
                }
                if (!free) problems.Add($"EventDefs {name} page {p}: every choice costs cores — a party with none would be stuck");
            }
        }
        return problems;
    }
}
