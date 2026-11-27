using Godot;

namespace Fableland.Run;

/// <summary>
/// The run-over / victory screen. Reads the final <see cref="RunState"/> (end reason + counters)
/// and offers a button back to the menu. Null-tolerant: F5'able (shows debug zeros).
/// </summary>
public partial class RunOverController : Control
{
    public override void _Ready()
    {
        var rs = RunState.Instance;
        var kind = rs?.LastEndKind ?? RunEndKind.Death;

        string title = kind == RunEndKind.Victory ? "VICTORY!" : "RUN OVER";
        string reason = kind switch
        {
            RunEndKind.Death => "You died in combat.",
            RunEndKind.VoidDevoured => "The VOID devoured the ground beneath you.",
            RunEndKind.BossTimer => "The boss timer expired — the darkness took you.",
            RunEndKind.Victory => VictoryLine(rs),
            _ => "",
        };

        GetNode<Label>("Center/Box/TitleLabel").Text = title;
        GetNode<Label>("Center/Box/ReasonLabel").Text = reason;
        GetNode<Label>("Center/Box/StatsLabel").Text =
            $"Day reached: {(rs?.InVoid ?? false ? "??? (in the VOID)" : (rs?.Day ?? 0).ToString())}\n" +
            $"Nodes traversed: {rs?.NodesTraversed ?? 0}\n" +
            $"Worlds visited: {rs?.WorldsVisited ?? 0}\n" +
            $"Goals succeeded: {rs?.GoalsSucceeded ?? 0}\n" +
            $"Protagonists: {rs?.ProtagonistsCollected ?? 0}\n" +
            $"Wonder items: {rs?.ItemsCollected ?? 0}";

        GetNode<Button>("Center/Box/MenuButton").Pressed += OnMenu;


        // flow/runover.enter: the run-over screen is up. Tag = how the run ended.
        Fableland.UI.UiCues.MarkBack(GetNode<Button>("Center/Box/MenuButton"));
        Fableland.UI.UiCues.Attach(this);
        string how = kind switch
        {
            RunEndKind.Victory => "victory",
            RunEndKind.VoidDevoured => "void_devoured",
            RunEndKind.BossTimer => "boss_timer",
            _ => "death",
        };
        CueBus.EmitAt(CueOwners.Flow, "runover.enter", Vector2.Zero, tag: how);
    }

    private void OnMenu() => GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");

    /// <summary>The win, said for the run's own dark leader (Docs/Zone6.gdd §4.2, Mara.gdd §11).</summary>
    private static string VictoryLine(RunState rs)
    {
        string variant = rs?.Zone6?.VariantId ?? "";
        if (variant == Fableland.Data.Zone6Table.AviciId) return "You reached the centre and stopped Māra's wheel. You step off it.";
        if (variant == Fableland.Data.Zone6Table.VoidFrontierId) return "You reached the centre and faced the Unwritten — a story never begun, ended.";
        return "You reached the centre and defeated the dark leader.";
    }
}
