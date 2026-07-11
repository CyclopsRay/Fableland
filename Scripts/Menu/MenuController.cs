using Godot;
using Fableland.Map;
using Fableland.Run;

/// <summary>
/// Title menu. A centered Start button that begins a fresh run (RunState.NewRun on a random seed)
/// and jumps to the Map scene. There is no seed entry field on the menu yet — the seed can still
/// be changed on the map (SeedEdit) which restarts the run on that exact seed.
/// </summary>
public partial class MenuController : Control
{
    public override void _Ready()
    {
        GetNode<Button>("Center/VBox/MapCreationButton").Pressed += OnMapCreation;
        GetNode<Button>("Center/VBox/StartButton").Pressed += OnStart;
    }

    private void OnMapCreation()
    {
        GetTree().ChangeSceneToFile("res://Scenes/MapCreation/MapBrowser.tscn");
    }

    private void OnStart()
    {
        RunState.Instance?.NewRun(DetRandom.NewSeed());
        GetTree().ChangeSceneToFile("res://Scenes/Map.tscn");
    }
}
