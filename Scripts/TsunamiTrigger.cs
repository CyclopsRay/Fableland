using Godot;

/// <summary>
/// A pressure-plate "button": when the player walks into it, spawns a
/// <see cref="TsunamiHazard"/> at <see cref="SpawnPosition"/> (world space,
/// typically the arena's right edge) so it can sweep left across the map.
/// </summary>
public partial class TsunamiTrigger : Area2D
{
    [Export] public PackedScene TsunamiScene;
    [Export] public Vector2 SpawnPosition;
    [Export] public float Cooldown = 4f;
    [Export] public Vector2 BoxSize = new Vector2(48f, 48f);

    private float _cd;

    public override void _Ready()
    {
        SetCollisionLayerValue(Units.LayerHazard, true);
        SetCollisionMaskValue(Units.LayerPlayer, true);
        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = BoxSize } });
        BodyEntered += OnBodyEntered;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_cd > 0f) _cd -= (float)delta;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_cd > 0f || body is not CharacterController || TsunamiScene == null) return;
        _cd = Cooldown;

        var wave = TsunamiScene.Instantiate<Node2D>();
        GetParent().AddChild(wave);
        wave.GlobalPosition = SpawnPosition;
    }

    public override void _Draw()
    {
        var rect = new Rect2(-BoxSize / 2f, BoxSize);
        DrawRect(rect, new Color(0.9f, 0.75f, 0.1f, 0.9f));
        DrawRect(rect, new Color(1f, 0.9f, 0.3f, 1f), false, 3f);
    }
}
