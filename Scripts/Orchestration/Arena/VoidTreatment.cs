using System.Collections.Generic;
using Godot;
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
    // are already in it; alpha is left as it is. The two numbers are uniforms so they can be
    // tuned on the material — and so ShaderCompiles() has something to ask for.
    private const string ShaderCode = @"shader_type canvas_item;

uniform float colour_kept = 0.22;                 // share of the map's own colour that survives
uniform vec3 shade = vec3(0.50, 0.44, 0.62);      // what is left, darkened toward the VOID's violet

void fragment() {
	float grey = dot(COLOR.rgb, vec3(0.299, 0.587, 0.114));
	COLOR.rgb = mix(vec3(grey), COLOR.rgb, colour_kept) * shade;
}
";

    /// <summary>Drains a built map: its art layers and the backdrop behind them. Returns how
    /// many pieces of art took the treatment (the backdrop is not counted).</summary>
    public static int Apply(IEnumerable<MapParallaxLayer> artLayers, CanvasItem backdrop)
    {
        var material = new ShaderMaterial
        {
            ResourceName = MaterialName,
            Shader = new Shader { Code = ShaderCode },
        };
        if (backdrop != null) backdrop.Material = material;
        return MapVisualBuilder.Restyle(artLayers, material);
    }

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
        return declared.Contains("colour_kept") && declared.Contains("shade");
    }

    /// <summary>True when this item is drawn with the VOID treatment.</summary>
    public static bool IsOn(CanvasItem item) =>
        item != null && item.Material is ShaderMaterial material && material.ResourceName == MaterialName;
}
