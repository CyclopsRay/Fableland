using System.Collections.Generic;
using Godot;

namespace Fableland.UI;

/// <summary>
/// What the keys do, read from the InputMap at the moment it is shown, so a rebinding in
/// <c>project.godot</c> shows up here with nothing else to change (audit v0.11.1 #1: no
/// controls were shown anywhere). The pause menu shows the whole card; the arena's countdown
/// shows one line of it.
/// </summary>
public static class ControlsCard
{
    private static readonly (string Action, string What)[] Rows =
    {
        ("move_left", "Move left"),
        ("move_right", "Move right"),
        ("jump", "Jump"),
        ("move_down", "Drop through a platform · swim down"),
        ("move_up", "Swim up"),
        ("attack", "Basic attack (aim with the mouse)"),
        ("skill1", "Skill 1"),
        ("skill2", "Skill 2"),
        ("ult", "Ultimate, once its ring is full (PumpKing: the Black Prison, again to blink into it · the others: not built yet)"),
        ("use_item", "Use the held item"),
        ("switch_protagonist", "Switch fighter"),
        ("pause", "Pause"),
        (InputDefaults.Interact, "Use a station (in a shelter)"),
    };

    /// <summary>"A / Left" — every key or button bound to an action, as the engine names them.</summary>
    public static string KeysOf(string action)
    {
        if (string.IsNullOrEmpty(action) || !InputMap.HasAction(action)) return "—";
        var keys = new List<string>();
        foreach (InputEvent bound in InputMap.ActionGetEvents(action))
        {
            string text = KeyName(bound);
            if (text.Length > 0 && !keys.Contains(text)) keys.Add(text);
        }
        return keys.Count > 0 ? string.Join(" / ", keys) : "—";
    }

    /// <summary>The first key bound to an action ("E"), for a prompt; "?" when none is.</summary>
    public static string FirstKeyOf(string action)
    {
        if (string.IsNullOrEmpty(action) || !InputMap.HasAction(action)) return "?";
        foreach (InputEvent bound in InputMap.ActionGetEvents(action))
        {
            string text = KeyName(bound);
            if (text.Length > 0) return text;
        }
        return "?";
    }

    /// <summary>A key as a player reads it. The engine marks a physical key "E - Physical"
    /// (Godot 4.3 on; "E (Physical)" before) — which key it is matters, how it is matched
    /// does not (v0.12.9: every key on the card and the countdown read "… - Physical").</summary>
    private static string KeyName(InputEvent bound) =>
        bound.AsText().Replace(" - Physical", "").Replace(" (Physical)", "").Trim();

    /// <summary>The whole card, one line per action: "Jump — Space".</summary>
    public static string Text()
    {
        var lines = new List<string>();
        foreach ((string action, string what) in Rows) lines.Add($"{what} — {KeysOf(action)}");
        return string.Join("\n", lines);
    }

    /// <summary>The fight's essentials on one line, for the countdown.</summary>
    public static string OneLine() =>
        $"{KeysOf("attack")} attack · {KeysOf("skill1")} / {KeysOf("skill2")} skills · {KeysOf("use_item")} item · " +
        $"{KeysOf("switch_protagonist")} switch · {KeysOf("pause")} pause";
}
