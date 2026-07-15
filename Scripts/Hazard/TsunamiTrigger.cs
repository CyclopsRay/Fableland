using Godot;
using System;

/// <summary>
/// A pressure-plate request for the arena's <see cref="ArenaEnvironmentController"/>.
/// It owns only player contact and its 1x1-m collision shape; the controller owns the
/// tsunami's warning, presentation, wind, wave spawn, restoration, and cooldown.
/// </summary>
public partial class TsunamiTrigger : Area2D
{
    /// <summary>Raised once when an armed player contact requests the tsunami event.
    /// The arena controller subscribes and must call <see cref="SetArmed"/> after its
    /// own cooldown finishes. Scene-bound trigger → arena wiring is the intended edge
    /// for this C# event; the controller unsubscribes in its _ExitTree.</summary>
    public event Action<TsunamiTrigger> Activated;

    [Export] public PackedScene TsunamiScene;
    [Export] public Vector2 SpawnPosition;
    [Export] public Vector2 BoxSize = new(Units.Px(1f), Units.Px(1f));

    private bool _armed = true;

    public override void _Ready()
    {
        SetCollisionLayerValue(Units.LayerHazard, true);
        SetCollisionMaskValue(Units.LayerPlayer, true);
        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = BoxSize } });
        BodyEntered += OnBodyEntered;
        QueueRedraw();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!_armed || body is not CharacterController || TsunamiScene == null) return;
        if (Activated == null)
        {
            GD.PushError("TsunamiTrigger: activated without an ArenaEnvironmentController subscriber.");
            return;
        }

        _armed = false;
        Activated.Invoke(this);
        QueueRedraw();
    }

    /// <summary>Controller-only arming gate. The trigger never owns duration/cooldown data.</summary>
    public void SetArmed(bool armed) { _armed = armed; QueueRedraw(); }

    public override void _Draw()
    {
        var rect = new Rect2(-BoxSize / 2f, BoxSize);
        Color fill = _armed ? new Color(0.9f, 0.75f, 0.1f, 0.9f) : new Color(0.35f, 0.35f, 0.35f, 0.65f);
        Color edge = _armed ? new Color(1f, 0.9f, 0.3f, 1f) : new Color(0.65f, 0.65f, 0.65f, 0.85f);
        DrawRect(rect, fill);
        DrawRect(rect, edge, false, 3f);
    }
}
