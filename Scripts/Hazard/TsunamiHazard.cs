using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A one-shot moving wave: a pyramid-shaped hitbox that sweeps leftward from its
/// spawn point, hits each body at most once with a heavy shove and a hard,
/// fixed-duration no-control window, then despawns once past the arena.
/// </summary>
public partial class TsunamiHazard : Area2D
{
    public event Action Finished;
    [Export] public float Width = Units.Px(16f);  // art contract: 16 map cells / 512 px
    [Export] public float Height = Units.Px(8f);  // art contract: 8 map cells / 256 px
    [Export] public float Speed = 480f;          // leftward sweep speed
    [Export] public float DespawnX = -300f;      // freed once swept past here
    [Export] public float DamagePercentMaxHP = 0.35f;
    [Export] public float NoControlDuration = 1f; // static, fixed hitstun (not damage-scaled)

    private readonly HashSet<ulong> _hit = new();
    private bool _hasSpriteArt;
    private bool _finished;

    private const string SpriteSheetPath =
        "res://Assets/Sprites/Tiles/VanillaKingdom/Hazard/hazard_tsunami_sheet_2x2.png";
    private const string TravelAnimation = "travel";
    private const int SheetColumns = 2;
    private const int SheetRows = 2;
    private const float AnimationFps = 6f;

    public override void _Ready()
    {
        SetCollisionLayerValue(Units.LayerHazard, true);
        SetCollisionMaskValue(Units.LayerPlayer, true);
        SetCollisionMaskValue(Units.LayerFoes, true);

        AddChild(new CollisionPolygon2D { Polygon = TrianglePoints() });
        AddAnimatedSprite();

        BodyEntered += OnBodyEntered;
        QueueRedraw();
    }

    private void AddAnimatedSprite()
    {
        var texture = GD.Load<Texture2D>(SpriteSheetPath);
        if (texture == null)
        {
            GD.PushWarning($"Tsunami art missing at {SpriteSheetPath}; using debug polygon");
            return;
        }

        int frameW = texture.GetWidth() / SheetColumns;
        int frameH = texture.GetHeight() / SheetRows;
        if (frameW <= 0 || frameH <= 0) return;

        var frames = new SpriteFrames();
        frames.AddAnimation(TravelAnimation);
        frames.SetAnimationSpeed(TravelAnimation, AnimationFps);
        frames.SetAnimationLoopMode(TravelAnimation, SpriteFrames.LoopMode.Linear);

        // Reading order is the art contract: swell, rise, crest, crash.
        for (int row = 0; row < SheetRows; row++)
        for (int column = 0; column < SheetColumns; column++)
        {
            var atlas = new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(column * frameW, row * frameH, frameW, frameH),
            };
            frames.AddFrame(TravelAnimation, atlas);
        }

        var sprite = new AnimatedSprite2D
        {
            SpriteFrames = frames,
            Animation = TravelAnimation,
            Position = new Vector2(0f, -Height * 0.5f), // sheet baseline sits on hitbox y=0
            Scale = new Vector2(Width / frameW, Height / frameH),
        };
        AddChild(sprite);
        sprite.Play(TravelAnimation);
        _hasSpriteArt = true;
    }

    private Vector2[] TrianglePoints() => new[]
    {
        new Vector2(-Width * 0.5f, 0f),
        new Vector2(Width * 0.5f, 0f),
        new Vector2(0f, -Height),
    };

    public override void _PhysicsProcess(double delta)
    {
        Position += new Vector2(-Speed * (float)delta, 0f);
        if (GlobalPosition.X < DespawnX) Finish();
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        Finished?.Invoke();
        QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        ulong id = body.GetInstanceId();
        if (_hit.Contains(id)) return;
        _hit.Add(id);

        Vector2 dir = new Vector2(-1f, -0.15f).Normalized();   // swept left, slightly up
        float damping = body switch
        {
            CharacterController c => c.ExternalDamping,
            BaseFoe e => e.ExternalDamping,
            _ => 900f,
        };
        // Solve v0 from d = v0² / (2·damping) so the shove travels ~15m before the
        // target's own knockback friction eats it, regardless of that rate.
        float v0 = Mathf.Sqrt(2f * damping * Units.Px(15f));
        Vector2 impulse = dir * v0;

        if (body is CharacterController cc)
            cc.TakeHit(new HitInfo(cc.MaxHP * DamagePercentMaxHP, impulse, NoControlDuration));
        else if (body is BaseFoe en)
            en.TakeHit(new HitInfo(en.MaxHP * DamagePercentMaxHP, impulse, NoControlDuration), GlobalPosition);
    }

    public override void _Draw()
    {
        if (_hasSpriteArt) return;
        Vector2[] pts = TrianglePoints();
        DrawColoredPolygon(pts, new Color(0.2f, 0.5f, 0.85f, 0.6f));
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[0] }, new Color(0.6f, 0.85f, 1f, 0.95f), 3f);
    }
}
