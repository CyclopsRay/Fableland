using System.Collections.Generic;
using Godot;
using Fableland.Data;
using Fableland.MapCreation.Runtime;

/// <summary>
/// How a map looks once the VOID has swallowed its world (MapGDD §7). Zone 6 has no maps of
/// its own yet (TBD #33), so its fights are dealt a map of one of the run's worlds, and this
/// is what says "you are not there any more": the picture keeps its shapes and loses its
/// colour — drained almost to grey and darkened toward the VOID's violet.
///
/// One shared material, no new art. It goes on the backdrop and on every piece of the map's
/// art (<see cref="MapVisualBuilder.Restyle"/>), and on nothing else: fighters, foes,
/// projectiles, pickups, hazard sprites, telegraphs and the HUD keep their full colour, which
/// against a drained world makes them easier to read, not harder. It is a material rather
/// than a tint because a tint (<c>Modulate</c>) can only darken — it cannot take colour out —
/// and because <c>Background.Color</c> and <c>World.Modulate</c> already belong to
/// <see cref="ArenaEnvironmentController"/>'s storm.
/// </summary>
public static class VoidTreatment
{
    /// <summary>The material's name: how a check tells this treatment from any other material.</summary>
    public const string MaterialName = "VoidTreatment";

    // COLOR arrives as texture × vertex colour, so region clipping, flips and a layer's tint
    // are already in it; alpha is left as it is. The numbers are uniforms so each zone-6
    // variant sets its own (Zone6Table: Zone6Look) — and so ShaderCompiles() has something to
    // ask for. The defaults are the look every zone-6 map had before variants (v0.11.3).
    private const string ShaderCode = @"shader_type canvas_item;

uniform float colour_kept = 0.22;                 // share of the map's own colour that survives
uniform vec3 shade = vec3(0.50, 0.44, 0.62);      // what is left, darkened toward the VOID's violet
uniform vec3 lift = vec3(0.0);                    // a wash added after: a mist that pales it

void fragment() {
	float grey = dot(COLOR.rgb, vec3(0.299, 0.587, 0.114));
	COLOR.rgb = mix(vec3(grey), COLOR.rgb, colour_kept) * shade + lift;
}
";

    /// <summary>Drains a built map: its art layers and the backdrop behind them, in a zone-6
    /// variant's look (<paramref name="look"/>; null = the default). Returns how many pieces
    /// of art took the treatment (the backdrop is not counted).</summary>
    public static int Apply(IEnumerable<MapParallaxLayer> artLayers, CanvasItem backdrop, Zone6Look look = null)
    {
        look ??= Zone6Table.DefaultLook;
        var material = new ShaderMaterial
        {
            ResourceName = MaterialName,
            Shader = new Shader { Code = ShaderCode },
        };
        material.SetShaderParameter("colour_kept", look.ColourKept);
        material.SetShaderParameter("shade", new Vector3(look.ShadeR, look.ShadeG, look.ShadeB));
        material.SetShaderParameter("lift", new Vector3(look.LiftR, look.LiftG, look.LiftB));
        if (backdrop != null) backdrop.Material = material;
        return MapVisualBuilder.Restyle(artLayers, material);
    }

    /// <summary>The colour the shader draws <paramref name="c"/> as under <paramref name="look"/> —
    /// the same arithmetic on the CPU, so the arena can report how light its drained backdrop
    /// really is (flow/arena.enter's magnitude) without reading pixels back.</summary>
    public static Color Drawn(Color c, Zone6Look look = null)
    {
        look ??= Zone6Table.DefaultLook;
        float grey = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        float k = look.ColourKept;
        return new Color(
            Mathf.Clamp((grey + (c.R - grey) * k) * look.ShadeR + look.LiftR, 0f, 1f),
            Mathf.Clamp((grey + (c.G - grey) * k) * look.ShadeG + look.LiftG, 0f, 1f),
            Mathf.Clamp((grey + (c.B - grey) * k) * look.ShadeB + look.LiftB, 0f, 1f),
            c.A);
    }

    /// <summary>How light <paramref name="c"/> is once drawn under <paramref name="look"/> (0–1).</summary>
    public static float DrawnLightness(Color c, Zone6Look look = null) => Drawn(c, look).Luminance;

    /// <summary>
    /// True when the engine accepts the shader. A shader that fails to compile declares no
    /// uniforms, and the headless renderer parses shaders too — so a headless check can ask
    /// this, where otherwise the failure is one `Shader compilation failed` line in a log
    /// nobody's exit code reads, and a map drawn wrong.
    /// </summary>
    public static bool ShaderCompiles()
    {
        var shader = new Shader { Code = ShaderCode };
        var declared = new HashSet<string>();
        foreach (Variant uniform in shader.GetShaderUniformList())
            declared.Add(uniform.AsGodotDictionary()["name"].AsString());
        return declared.Contains("colour_kept") && declared.Contains("shade") && declared.Contains("lift");
    }

    /// <summary>True when this item is drawn with the VOID treatment.</summary>
    public static bool IsOn(CanvasItem item) =>
        item != null && item.Material is ShaderMaterial material && material.ResourceName == MaterialName;
}
