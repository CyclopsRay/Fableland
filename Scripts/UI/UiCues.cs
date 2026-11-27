using Godot;

namespace Fableland.UI;

/// <summary>
/// The UI's cues (<c>Docs/Feedback.gdd</c> §2, <c>Docs/Audio.gdd</c> §2): every button in the
/// game raises <c>ui</c>/<c>hover</c> when the pointer enters it and <c>ui</c>/<c>press</c>
/// when it is pressed — <c>ui</c>/<c>back</c> instead, for a button marked as the way out.
/// A screen calls <see cref="Attach"/> once in <c>_Ready</c>; buttons made later (a dialog,
/// a list filled at run time) are wired as they enter the tree.
///
/// The UI raises and forgets, exactly as gameplay does: what a press sounds or looks like is
/// a row in a director's table. Position = the button's centre on screen, size = its width.
/// </summary>
public static class UiCues
{
    private static readonly StringName WiredMeta = "ui_cues_wired";
    private static readonly StringName BackMeta = "ui_cue_back";
    private static bool _watching;

    /// <summary>Wire every button under <paramref name="screen"/>, and every button that
    /// enters the tree from now on.</summary>
    public static void Attach(Node screen)
    {
        if (screen == null || !screen.IsInsideTree()) return;
        if (!_watching)
        {
            // The scene tree outlives every screen and this handler is static: nothing to
            // unsubscribe, nothing kept alive (the caveat is for scene nodes — KNOWLEDGE v0.5.4).
            _watching = true;
            screen.GetTree().NodeAdded += OnNodeAdded;
        }
        Wire(screen);
    }

    /// <summary>This button leaves the screen it is on: it raises <c>back</c>, not <c>press</c>.</summary>
    public static void MarkBack(BaseButton button) => button?.SetMeta(BackMeta, true);

    private static void OnNodeAdded(Node node)
    {
        if (node is BaseButton button) WireButton(button);
    }

    private static void Wire(Node node)
    {
        if (node is BaseButton button) WireButton(button);
        foreach (Node child in node.GetChildren(true)) Wire(child);
    }

    private static void WireButton(BaseButton button)
    {
        if (button.HasMeta(WiredMeta)) return;
        button.SetMeta(WiredMeta, true);
        button.MouseEntered += () => { if (!button.Disabled) Raise(button, "hover"); };
        button.Pressed += () => Raise(button, button.HasMeta(BackMeta) ? "back" : "press");
    }

    private static void Raise(Control control, string action) =>
        CueBus.EmitAt(CueOwners.Ui, action, control.GetGlobalRect().GetCenter(), size: control.Size.X);
}
