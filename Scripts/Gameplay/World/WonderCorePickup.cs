using Godot;

/// <summary>
/// A collectible wonder core on the arena floor (replaces the old WonderPage pickup). Bobs
/// gently, and when the player overlaps it emits <see cref="Collected"/> and frees itself.
/// Placement + lifetime (despawn/respawn) are owned by <see cref="Fableland.Missions.CollectionMission"/>;
/// this node is a dumb bobbing pickup.
/// </summary>
public partial class WonderCorePickup : Area2D
{
    [Signal] public delegate void CollectedEventHandler();

    private float _baseY;
    private float _t;
    private bool _collected;

    public override void _Ready()
    {
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
