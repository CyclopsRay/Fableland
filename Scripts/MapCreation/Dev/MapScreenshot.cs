namespace Fableland.MapCreation.Dev;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Runtime;

/// <summary>
/// Dev tool: renders built-in combat maps to PNG through the REAL runtime path
/// (<see cref="CombatMapRuntime"/>, the same builder a run uses) so a map's look can be
/// reviewed — and regressions in art, parallax or draw order caught — without playing to it.
/// `Tools/render_map_preview.py` is the fast offline approximation; this is the ground truth.
///
///     Godot --path . res://Scenes/MapCreation/MapScreenshot.tscn -- --out=/abs/dir [--map=wulin]
///
/// For each map it saves `&lt;map&gt;_spawn.png` (the opening screen, with Pomegraknight standing
/// at the spawn for scale and contrast) and `_mid` / `_high` / `_far` views that exercise
/// camera parallax. Needs a real window (it cannot run `--headless`); it quits by itself.
/// </summary>
public partial class MapScreenshot : Node2D
{
    private const string CharacterScene = "res://Scenes/Pomegraknight.tscn";

    public override async void _Ready()
    {
        string outDir = null, only = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out=")) outDir = arg.Substring("--out=".Length);
            else if (arg.StartsWith("--map=")) only = arg.Substring("--map=".Length);
        }
        if (string.IsNullOrWhiteSpace(outDir))
        {
            GD.PushError("MapScreenshot: pass an output directory:  -- --out=/absolute/dir");
            GetTree().Quit(2);
            return;
        }
        Directory.CreateDirectory(outDir);

        string[] paths = Directory.GetFiles(ProjectSettings.GlobalizePath("res://Maps"), "*.json");
        Array.Sort(paths, StringComparer.Ordinal);
        int shots = 0;
        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (only != null && !name.Contains(only)) continue;
            MapDocument doc = MapJson.Load(path, out _);
            if (doc == null) continue;
            shots += await Capture(doc, name, outDir);
        }

        GD.Print($"MapScreenshot: wrote {shots} image(s) to {outDir}");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task<int> Capture(MapDocument doc, string name, string outDir)
    {
        foreach (Node child in GetChildren()) child.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var world = new Node2D { Name = "World" };
        var hazards = new Node2D { Name = "Hazards" };
        var canvas = new ColorRect { Name = "Canvas", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(canvas);
        AddChild(world);
        AddChild(hazards);

        CombatMapBuild build = CombatMapRuntime.Build(doc, world, hazards);
        Vector2 size = new(build.WidthPx, build.HeightPx);
        canvas.Position = -size;
        canvas.Size = size * 3f;
        try { canvas.Color = new Color(doc.Canvas?.Color ?? "#87CEEB"); } catch { canvas.Color = Colors.Black; }

        Vector2 spawn = build.CharacterSpawns.Count > 0 ? build.CharacterSpawns[0] : size * 0.5f;
        build.AnchorParallax(spawn);

        var camera = new Camera2D
        {
            LimitLeft = 0, LimitTop = 0,
            LimitRight = Mathf.CeilToInt(size.X), LimitBottom = Mathf.CeilToInt(size.Y),
        };
        AddChild(camera);
        camera.MakeCurrent();

        // The protagonist, frozen, purely as a scale and contrast reference.
        if (ResourceLoader.Load<PackedScene>(CharacterScene)?.Instantiate() is Node2D hero)
        {
            hero.ProcessMode = ProcessModeEnum.Disabled;
            hero.Position = spawn;
            AddChild(hero);
            if (hero.GetNodeOrNull<Camera2D>("Camera2D") is Camera2D heroCamera) heroCamera.Enabled = false;
            camera.MakeCurrent();
        }

        var views = new List<(string Tag, Vector2 At)>
        {
            ("spawn", spawn),
            ("mid", new Vector2(size.X * 0.5f, size.Y * 0.62f)),
            ("high", new Vector2(size.X * 0.5f, size.Y * 0.25f)),
            ("far", new Vector2(size.X * 0.88f, size.Y * 0.70f)),
        };

        int written = 0;
        foreach ((string tag, Vector2 at) in views)
        {
            camera.GlobalPosition = at;
            camera.ResetSmoothing();
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            Image image = GetViewport().GetTexture().GetImage();
            string file = Path.Combine(outDir, $"{name}_{tag}.png");
            Error error = image.SavePng(file);
            if (error == Error.Ok) written++;
            else GD.PushError($"MapScreenshot: could not write {file}: {error}");
        }
        return written;
    }
}
