using Godot;

/// <summary>
/// Title menu. For now just a centered Start button that jumps to the Map scene.
/// </summary>
public partial class MenuController : Control
{
    public override void _Ready()
    {
        GetNode<Button>("Center/StartButton").Pressed += OnStart;
    }

    private void OnStart()
    {
        GetTree().ChangeSceneToFile("res://Scenes/Map.tscn");
    }
}
