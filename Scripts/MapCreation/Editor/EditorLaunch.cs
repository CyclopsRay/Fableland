namespace Fableland.MapCreation.Editor;

/// <summary>
/// Tiny static handoff for the Browser → Editor scene change (`ChangeSceneToFile` can't
/// carry constructor args, T10 §2 scene/script contract). The browser sets <see cref="MapId"/>
/// then swaps to <c>MapEditor.tscn</c>; the editor (Phase MC3) reads it in its own
/// <c>_Ready</c>/init step and clears it once consumed.
///
/// <c>null</c> means the editor was F5-launched directly (no browser involved) and must
/// open a fresh unsaved document instead of failing to find a map — the same
/// null-tolerant rule the orchestration handshake uses elsewhere (T10 §3).
/// </summary>
public static class EditorLaunch
{
    public static string MapId;
}
