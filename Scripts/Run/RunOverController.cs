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
            RunEndKind.Victory => "You reached the center and defeated the dark leader.",
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
    }

    private void OnMenu() => GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");
}
