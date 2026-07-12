namespace Fableland.MapCreation.Editor.Tools;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §10 "Tools/ one class per tool, registry-listed" — the shape every editor tool
/// implements. MapEditor owns one instance per <see cref="EditorState.EditorTool"/>
/// value (built once by <see cref="ToolRegistry"/>) and routes GridView's
/// CellPressed/CellDragged/CellReleased events to whichever tool is currently active,
/// looked up fresh by <see cref="EditorState.ActiveTool"/> every event (no stale refs).
///
/// Tools mutate <see cref="EditorState"/> (Document tiles via <see cref="EditorState.Commands"/>,
/// Selection, Clipboard, BrushDefId) but never raise <see cref="EditorState.StateChanged"/>
/// themselves — the router (MapEditor's Cell* handlers) raises it exactly once per
/// routed input event after delegating, so nothing here double-raises.
/// </summary>
public abstract class ToolBase
{
    protected readonly EditorState State;
    protected readonly GridView View;

    protected ToolBase(EditorState state, GridView view)
    {
        State = state;
        View = view;
    }

    public virtual void OnPressed(Vector2I cell, InputEventMouseButton ev) { }
    public virtual void OnDragged(Vector2I cell) { }
    public virtual void OnReleased(Vector2I cell) { }

    /// <summary>Clears any in-progress stroke/drag/lasso state. Called when the rail
    /// (or a shortcut) switches away from this tool, or Esc cancels mid-gesture.</summary>
    public virtual void OnDeactivated() { }

    /// <summary>Null = nothing to draw this frame.</summary>
    public virtual GhostInfo GetGhost() => null;

    /// <summary>GDD §1 role string ("farview"/"battlefield"/"closeview") to the flags
    /// enum a TileDef.AllowedRoles is authored against. Unknown/empty role -> None
    /// (nothing is legal there) rather than throwing. `internal` (not just
    /// `protected`) so MapEditor's paste-mode pseudo-tool — not itself a
    /// <see cref="ToolBase"/> subclass — can reuse the exact same mapping instead of
    /// re-deriving it.</summary>
    internal static LayerRoleMask RoleMaskOf(string role) => role switch
    {
        MapLayerData.RoleFarview => LayerRoleMask.Farview,
        MapLayerData.RoleBattlefield => LayerRoleMask.Battlefield,
        MapLayerData.RoleCloseview => LayerRoleMask.Closeview,
        _ => LayerRoleMask.None,
    };

    /// <summary>GDD §2.2 placement legality, role half only (occupancy/bounds are the
    /// caller's job since they need the concrete cell/footprint being tested).</summary>
    protected bool IsLegalOnCurrentLayer(TileDef def)
    {
        var layer = State.CurrentLayer;
        if (layer == null || def == null) return false;
        return (def.AllowedRoles & RoleMaskOf(layer.Role)) != 0;
    }
}

/// <summary>
/// GDD §7.2 — everything a tool wants GridView to draw for its current gesture:
/// footprint-rect ghost(s) (paint/erase/bucket/eyedropper-n/a/move-preview/paste
/// stamp preview), a drag rectangle (rect fill / marquee), or an in-progress
/// freehand polyline (lasso). Deliberately one shared shape (design freedom per the
/// brief) — every tool fills in only the parts it needs and leaves the rest
/// null/empty; GridView draws whichever parts are present.
/// </summary>
public sealed class GhostInfo
{
    /// <summary>Footprint-rect ghosts, one entry per placed/moved/pasted tile preview.</summary>
    public List<(Vector2I cell, int W, int H, string DefId)> Cells = new();

    /// <summary>Legal (green) vs illegal (red) tint for every rect-shaped ghost above.
    /// A single flag for the whole ghost (not per-cell) — simplest shape that still
    /// reads clearly; see BuildPasteGhost/tool GetGhost() for how mixed legality
    /// collapses to one bool.</summary>
    public bool Valid = true;

    /// <summary>Lasso's in-progress freehand polyline, world px, open (not closed).</summary>
    public List<Vector2> LassoPointsWorld;

    /// <summary>Rect fill / marquee drag rectangle, in cell coordinates.</summary>
    public Rect2I? RectCells;
}

/// <summary>
/// GDD §10 "registry-listed" — maps <see cref="EditorState.EditorTool"/> to the one
/// tool instance for that value, built once by MapEditor. Adding a tool means adding
/// an entry here, never editing a switch (T00 rule 1 / T20 extensibility).
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<EditorState.EditorTool, ToolBase> _byTool;

    public ToolRegistry(EditorState state, GridView view)
    {
        _byTool = new Dictionary<EditorState.EditorTool, ToolBase>
        {
            [EditorState.EditorTool.Paint] = new PaintTool(state, view),
            [EditorState.EditorTool.Erase] = new EraseTool(state, view),
            [EditorState.EditorTool.Rect] = new RectTool(state, view),
            [EditorState.EditorTool.Marquee] = new MarqueeTool(state, view),
            [EditorState.EditorTool.Lasso] = new LassoTool(state, view),
            [EditorState.EditorTool.Move] = new MoveTool(state, view),
            [EditorState.EditorTool.Eyedropper] = new EyedropperTool(state, view),
            [EditorState.EditorTool.Bucket] = new BucketTool(state, view),
        };
    }

    public ToolBase Get(EditorState.EditorTool tool) => _byTool.TryGetValue(tool, out var t) ? t : null;
}
