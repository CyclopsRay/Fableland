using System.Collections.Generic;
using Godot;
using Fableland.Map;

namespace Fableland.Missions;

/// <summary>
/// Procedurally varies the arena (S1): keeps the scene's ground/walls/SoftVolume and adds
/// 3–5 one-way platforms + 0–2 hazards, all placed from the fight's deterministic RNG within
/// the ~2000×710 play space. Platform tiers guarantee ≥2 m vertical spacing and an 8 m-jump
/// reach. Returns the platform surface points so missions can place cores/objectives on them.
/// </summary>
public static class ArenaBuilder
{
    // Play space (Arena.tscn: ground top ≈ y560, walls at x≈0 / x≈2000).
    public const float PlayLeft = 260f;
    public const float PlayRight = 1740f;
    public const float GroundTopY = 560f;

    // Platform tiers (px). All within one 8 m (256 px) jump of the tier below and ≥2 m apart.
    private static readonly float[] Tiers = { 480f, 400f, 320f };

    /// <summary>Build platforms + hazards into the given parents. Returns candidate surface
    /// points (platform tops) for mission placement.</summary>
    public static List<Vector2> Build(Node world, Node hazards, DetRandom rng,
                                      Texture2D platformTex, PackedScene fireHazard, PackedScene freezeHazard)
    {
        var surfacePoints = new List<Vector2>();

        int count = rng.Range(3, 5);
        var placed = new List<Rect2>();
        int attempts = 0;
        while (placed.Count < count && attempts < 40)
        {
            attempts++;
            float w = rng.Range(180, 300);
            float y = Tiers[rng.Range(0, Tiers.Length - 1)];
            float x = rng.Range((int)(PlayLeft + w * 0.5f), (int)(PlayRight - w * 0.5f));
            var rect = new Rect2(x - w * 0.5f, y - 40f, w, 80f);   // pad for overlap test

            bool overlaps = false;
            foreach (var r in placed) if (r.Intersects(rect)) { overlaps = true; break; }
            if (overlaps) continue;

            placed.Add(rect);
            AddPlatform(world, platformTex, new Vector2(x, y), w);
            surfacePoints.Add(new Vector2(x, y - 20f));            // a bit above the surface
        }

        int hazardCount = rng.Range(0, 2);
        for (int i = 0; i < hazardCount; i++)
        {
            PackedScene scene = rng.NextDouble() < 0.5 ? fireHazard : freezeHazard;
            if (scene == null) continue;
            float x = rng.Range((int)PlayLeft + 80, (int)PlayRight - 80);
            var haz = scene.Instantiate<Node2D>();
            hazards.AddChild(haz);
            haz.GlobalPosition = new Vector2(x, GroundTopY - 20f);
        }

        return surfacePoints;
    }

    private static void AddPlatform(Node world, Texture2D tex, Vector2 pos, float width)
    {
        var body = new StaticBody2D { Position = pos };
        body.CollisionLayer = 0;
        body.SetCollisionLayerValue(Units.LayerPlatform, true);   // one-way Platform layer
        body.CollisionMask = 0;
        world.AddChild(body);

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(width, 24f) },
            OneWayCollision = true,
        };
        body.AddChild(shape);

        if (tex != null)
        {
            var sprite = new Sprite2D
            {
                Texture = tex,
                Scale = new Vector2(width / 128f, 0.75f),   // tex is 128×32 (see Arena.tscn)
            };
            body.AddChild(sprite);
        }
    }
}
