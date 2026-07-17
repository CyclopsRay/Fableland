using System.Collections.Generic;
using Godot;

namespace Fableland.Debug;

/// <summary>
/// Debug-layer registry of every implemented protagonist scene, keyed by an Id that equals the
/// character scene's root node <c>Name</c> (each listed scene is rooted on a node named
/// exactly like its Id). This is NOT the real protagonist-grant economy
/// (<c>RunState.Owned</c>) — it exists so debug mode can spawn any implemented body regardless of
/// what a real run has unlocked. When the real economy grows to cover every character, this
/// registry may graduate into it or simply stay as the debug-only source of truth.
///
/// Future characters (Pixolotl, Sifu Pangda) append one line to <see cref="Entries"/>.
/// </summary>
public static class ProtagonistRoster
{
    public static readonly (string Id, string ScenePath)[] Entries =
    {
        ("Pomegraknight", "res://Scenes/Pomegraknight.tscn"),
        ("PumpKing", "res://Scenes/PumpKing.tscn"),
        ("Cleopastar", "res://Scenes/Cleopastar.tscn"),
        ("Pixolotl", "res://Scenes/Pixolotl.tscn"),
    };

    private static readonly Dictionary<string, PackedScene> _cache = new();

    /// <summary>Lazy-load + cache the scene for a roster Id. Returns null for an unknown Id — never throws.</summary>
    public static PackedScene GetScene(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cache.TryGetValue(id, out var cached)) return cached;

        foreach (var entry in Entries)
        {
            if (entry.Id != id) continue;
            var scene = GD.Load<PackedScene>(entry.ScenePath);
            _cache[id] = scene;
            return scene;
        }

        return null;
    }
}
