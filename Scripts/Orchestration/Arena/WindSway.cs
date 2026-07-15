using Godot;

/// <summary>
/// Presentation-only response for scenery affected by arena wind. It moves a visual
/// child, never the collision/SoftVolume root, so the art can sway without changing
/// physics. It is deliberately harmless outside an arena: no controller means rest pose.
/// </summary>
public partial class WindSway : Node
{
    [Export] public NodePath VisualPath = "../Sprite2D";
    [Export] public float MaxAngleDegrees = 7f;
    [Export] public float MaxOffsetPixels = 8f;
    [Export] public float CyclesPerSecond = 1.7f;
    [Export] public float PhaseOffset = 0f;

    private Node2D _visual;
    private ArenaEnvironmentController _environment;
    private Vector2 _restPosition;
    private float _restRotation;
    private float _time;

    public override void _Ready()
    {
        _visual = GetNodeOrNull<Node2D>(VisualPath);
        if (_visual == null)
        {
            GD.PushError("WindSway: VisualPath does not resolve to a Node2D.");
            return;
        }

        _restPosition = _visual.Position;
        _restRotation = _visual.Rotation;
        _environment = GetTree().GetFirstNodeInGroup("arena_environment") as ArenaEnvironmentController;
    }

    public override void _Process(double delta)
    {
        if (_visual == null || !IsInstanceValid(_visual)) return;
        if (_environment == null || !IsInstanceValid(_environment))
            _environment = GetTree().GetFirstNodeInGroup("arena_environment") as ArenaEnvironmentController;

        _time += (float)delta;
        float strength = _environment?.VisualWindStrength ?? 0f;
        float direction = _environment?.WindDirection ?? 0f;
        float wave = Mathf.Sin((_time * CyclesPerSecond + PhaseOffset) * Mathf.Pi * 2f);

        _visual.Rotation = _restRotation + Mathf.DegToRad(MaxAngleDegrees) * strength * wave * direction;
        _visual.Position = _restPosition + new Vector2(direction * MaxOffsetPixels * strength * wave, 0f);
    }

    public override void _ExitTree()
    {
        if (_visual != null && IsInstanceValid(_visual))
        {
            _visual.Position = _restPosition;
            _visual.Rotation = _restRotation;
        }
    }
}
