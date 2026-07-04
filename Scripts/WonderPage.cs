using Godot;

/// <summary>
/// A collectible WonderPage. When the player overlaps it, it notifies the
/// GameManager and frees itself. Bobs gently so it reads as a pickup.
/// </summary>
public partial class WonderPage : Area2D
{
    [Signal] public delegate void CollectedEventHandler();

    private Sprite2D _sprite;
    private float _baseY;
    private float _t;
    private bool _collected;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _baseY = Position.Y;
        BodyEntered += OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        Position = new Vector2(Position.X, _baseY + Mathf.Sin(_t * 3f) * 5f);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_collected || !body.IsInGroup("player")) return;
        _collected = true;
        EmitSignal(SignalName.Collected);
        QueueFree();
    }
}
