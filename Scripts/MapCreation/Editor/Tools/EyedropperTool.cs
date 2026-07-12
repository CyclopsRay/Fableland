namespace Fableland.MapCreation.Editor.Tools;

using Godot;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.2 row "Eyedropper (I)": click an occupied cell to make its tile the
/// current brush (<see cref="EditorState.BrushDefId"/>). Empty cell = no-op. No
/// document mutation, so no command; the router (MapEditor) raises
/// <see cref="EditorState.StateChanged"/> once after this returns so the palette
/// highlight (MC5) can follow.
/// </summary>
public sealed class EyedropperTool : ToolBase
{
    public EyedropperTool(EditorState state, GridView view) : base(state, view) { }

    public override void OnPressed(Vector2I cell, InputEventMouseButton ev)
    {
        var occ = State.OccupancyOf(State.CurrentLayerIndex);
        var tile = occ.TileAt(cell.X, cell.Y);
        if (tile == null) return;
        State.BrushDefId = tile.DefId;
    }
}
