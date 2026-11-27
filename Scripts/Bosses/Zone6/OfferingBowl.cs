using System;
using Godot;

namespace Fableland.Bosses;

/// <summary>
/// The Tempter's Offering (Docs/Mara.gdd §7.5): a gold bowl Māra sets on the throne for a few
/// seconds. A fighter of the player's side who touches it is healed to full — and takes one
/// more stack of a poison, which the bowl names before it is touched. It frees itself when
/// taken or when its time is up. NOTE(art): placeholder drawing.
/// </summary>
public partial class OfferingBowl : Area2D
{
    public const float WidthM = 1.2f;
    public const float HeightM = 1.4f;

    private Action<CharacterController> _taken;
    private float _left;
    private bool _done;
    private Label _price;

    /// <summary>The poison the bowl puts into whoever takes it.</summary>
    public string PoisonId { get; private set; } = "";
    public string Price { get; private set; } = "";
    public bool Taken { get; private set; }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCollisionMaskValue(Units.LayerPlayer, true);
        Monitorable = false;
        ZIndex = 15;
        ZAsRelative = false;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(Units.Px(WidthM), Units.Px(HeightM)) },
            Position = new Vector2(0f, -Units.Px(HeightM) * 0.5f),
        });
        _price = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-Units.Px(4f), -Units.Px(HeightM) - 30f),
            Size = new Vector2(Units.Px(8f), 24f),
        };
        _price.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_price);
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Call right after AddChild, with the bowl's foot where it stands.</summary>
    public void Init(string poisonId, string price, float seconds, Action<CharacterController> taken)
    {
        PoisonId = poisonId ?? "";
        Price = price ?? "";
        _left = seconds;
        _taken = taken;
        if (_price != null) _price.Text = Price;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        _left -= (float)delta;
        if (_left <= 0f)
        {
            _done = true;
            QueueFree();
        }
    }

    /// <summary>Hands the bowl to a fighter as if it had walked into it (checks use this too).</summary>
    public void TakeBy(CharacterController fighter)
    {
        if (_done || fighter == null || fighter.Side != CombatSide.Player || fighter.IsDefeated) return;
        _done = true;
        Taken = true;
        _taken?.Invoke(fighter);
        QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is CharacterController fighter) TakeBy(fighter);
    }

    public override void _Draw()
    {
        float w = Units.Px(WidthM), h = Units.Px(HeightM) * 0.45f;
        var gold = new Color(0.96f, 0.78f, 0.28f);
        DrawColoredPolygon(new[] { new Vector2(-w * 0.5f, -h), new Vector2(w * 0.5f, -h), new Vector2(w * 0.3f, 0f), new Vector2(-w * 0.3f, 0f) }, gold);
        DrawRect(new Rect2(-w * 0.45f, -h - 5f, w * 0.9f, 5f), new Color(0.6f, 0.1f, 0.08f));
        DrawArc(new Vector2(0f, -h), w * 0.55f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 12, new Color(1f, 0.9f, 0.55f, 0.7f), 2f);
    }
}
