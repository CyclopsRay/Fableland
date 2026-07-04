using Godot;

/// <summary>
/// Autoload singleton that pops floating damage/heal numbers in world space.
/// Red = damage, green = heal. Font size scales with magnitude, clamped so 20
/// reads as the smallest and 100 as the largest.
/// </summary>
public partial class DamageNumberManager : Node
{
    public static DamageNumberManager Instance { get; private set; }

    [Export] public float MinFont = 18f;   // at |amount| <= 20
    [Export] public float MaxFont = 44f;   // at |amount| >= 100
    [Export] public float RiseHeight = 46f;
    [Export] public float Lifetime = 0.7f;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public void Pop(Vector2 worldPos, float amount, bool heal)
    {
        int shown = Mathf.RoundToInt(amount);
        if (shown <= 0) return;

        var label = new Label2D
        {
            Text = (heal ? "+" : "") + shown,
            ZIndex = 100,
        };

        float mag = Mathf.Clamp(Mathf.Abs(amount), 20f, 100f);
        var settings = new LabelSettings
        {
            FontSize = Mathf.RoundToInt(Mathf.Lerp(MinFont, MaxFont, (mag - 20f) / 80f)),
            FontColor = heal ? new Color(0.35f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f),
            OutlineColor = new Color(0f, 0f, 0f, 0.85f),
            OutlineSize = 5,
        };
        label.LabelSettings = settings;

        AddChild(label);
        label.GlobalPosition = worldPos + new Vector2((float)GD.RandRange(-12.0, 12.0), -8f);

        Tween tween = label.CreateTween();
        tween.TweenProperty(label, "position", label.Position + new Vector2(0f, -RiseHeight), Lifetime)
             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, Lifetime)
             .SetDelay(Lifetime * 0.35f);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }
}
