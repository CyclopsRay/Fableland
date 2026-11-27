using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace Fableland.Run;

/// <summary>
/// The player's settings on disk (Docs/Feedback.gdd §7): <see cref="FeedbackSettings"/> — shake,
/// punch, flash, hit-stop, particles and the four volumes — kept in <c>settings.json</c> beside
/// the save slots. It honours <see cref="SaveGameService.DirectoryOverride"/>, so a check that
/// redirects its saves never touches the player's own settings either. Reading a missing or
/// broken file leaves the defaults; nothing here ever throws into the game.
/// </summary>
public static class SettingsStore
{
    private static bool _loaded;

    /// <summary>Where the file lives now.</summary>
    public static string FilePath => string.IsNullOrWhiteSpace(SaveGameService.DirectoryOverride)
        ? ProjectSettings.GlobalizePath("user://settings.json")
        : Path.Combine(SaveGameService.DirectoryOverride, "settings.json");

    /// <summary>Loads the file once per session (the title, the arena and the map all ask).</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Load();
    }

    public static bool Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return false;
            var values = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(path));
            if (values == null) return false;
            float Get(string key, float fallback) => values.TryGetValue(key, out float v) ? v : fallback;
            FeedbackSettings.ScreenShake = Get("screenShake", FeedbackSettings.ScreenShake);
            FeedbackSettings.CameraPunch = Get("cameraPunch", FeedbackSettings.CameraPunch);
            FeedbackSettings.Flash = Get("flash", FeedbackSettings.Flash);
            FeedbackSettings.HitStop = Get("hitStop", FeedbackSettings.HitStop);
            FeedbackSettings.Particles = Get("particles", FeedbackSettings.Particles);
            FeedbackSettings.MasterVolume = Get("masterVolume", FeedbackSettings.MasterVolume);
            FeedbackSettings.MusicVolume = Get("musicVolume", FeedbackSettings.MusicVolume);
            FeedbackSettings.SfxVolume = Get("sfxVolume", FeedbackSettings.SfxVolume);
            FeedbackSettings.UiVolume = Get("uiVolume", FeedbackSettings.UiVolume);
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SettingsStore] could not read the settings ({e.Message}); keeping the defaults.");
            return false;
        }
    }

    /// <summary>Writes the current settings (to a temporary file first, then over the old one).</summary>
    public static bool Save()
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var values = new Dictionary<string, float>
            {
                ["screenShake"] = FeedbackSettings.ScreenShake,
                ["cameraPunch"] = FeedbackSettings.CameraPunch,
                ["flash"] = FeedbackSettings.Flash,
                ["hitStop"] = FeedbackSettings.HitStop,
                ["particles"] = FeedbackSettings.Particles,
                ["masterVolume"] = FeedbackSettings.MasterVolume,
                ["musicVolume"] = FeedbackSettings.MusicVolume,
                ["sfxVolume"] = FeedbackSettings.SfxVolume,
                ["uiVolume"] = FeedbackSettings.UiVolume,
            };
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SettingsStore] could not save the settings: {e.Message}");
            return false;
        }
    }
}
