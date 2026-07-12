namespace Fableland.MapCreation.Editor.Tools;

using System.Collections.Generic;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.7 "one stroke = one command" — the single reusable mutation command for
/// every tile-list add/remove verb (paint, erase, rect fill, bucket fill, delete,
/// cut, paste). Holds the exact <see cref="PlacedTile"/> REFERENCES it adds/removes
/// so Undo re-adds the very same instances a live <see cref="EditorState.Selection"/>
/// (or another command still on the stack) may be pointing at, never fresh copies.
///
/// Move is the one verb with a different shape (see <see cref="MoveCommand"/>): it
/// repositions existing tiles in place rather than swapping instances, which for
/// free keeps Selection pointed at the right objects across the move without any
/// extra bookkeeping here.
/// </summary>
public sealed class TileBatchCommand : IEditorCommand
{
    public string Name { get; }

    private readonly EditorState _state;
    private readonly MapLayerData _layer;
    private readonly List<PlacedTile> _toAdd;
    private readonly List<PlacedTile> _toRemove;

    public TileBatchCommand(EditorState state, MapLayerData layer, string name,
        List<PlacedTile> toAdd, List<PlacedTile> toRemove)
    {
        _state = state;
        _layer = layer;
        Name = name;
        _toAdd = toAdd ?? new List<PlacedTile>();
        _toRemove = toRemove ?? new List<PlacedTile>();
    }

    public void Do()
    {
        foreach (var t in _toRemove)
        {
            _layer.Tiles.Remove(t);
            // Orchestrator fix (F-MC1): a removed tile must leave the live Selection too,
            // or erase/bucket/paste-overwrite of a selected tile leaves a dangling selected
            // ref (stale "Sel: N", selection outline on a tile that no longer exists).
            // Undo deliberately does NOT re-select (accepted MC4 decision).
            _state.Selection.Remove(t);
        }
        foreach (var t in _toAdd) _layer.Tiles.Add(t);
        _state.InvalidateOccupancy();
    }

    public void Undo()
    {
        foreach (var t in _toAdd) _layer.Tiles.Remove(t);
        foreach (var t in _toRemove) _layer.Tiles.Add(t);
        _state.InvalidateOccupancy();
    }
}

/// <summary>
/// GDD §7.2 "Move" row / Q-MC3 overwrite semantics — repositions an already-selected
/// batch of tiles by a fixed cell delta, removing whatever non-selected tiles
/// occupied the destination footprints (captured once, at release, by
/// <see cref="MoveTool"/>). Mutates the SAME <see cref="PlacedTile"/> instances'
/// X/Y in place rather than remove+re-add, so <see cref="EditorState.Selection"/>
/// (a reference-identity set) keeps pointing at the moved tiles for free.
/// </summary>
public sealed class MoveCommand : IEditorCommand
{
    public string Name => "Move";

    private readonly EditorState _state;
    private readonly MapLayerData _layer;
    private readonly List<(PlacedTile Tile, int OldX, int OldY)> _moved;
    private readonly int _dx;
    private readonly int _dy;
    private readonly List<PlacedTile> _overwritten;

    public MoveCommand(EditorState state, MapLayerData layer,
        List<(PlacedTile Tile, int OldX, int OldY)> moved, int dx, int dy, List<PlacedTile> overwritten)
    {
        _state = state;
        _layer = layer;
        _moved = moved;
        _dx = dx;
        _dy = dy;
        _overwritten = overwritten ?? new List<PlacedTile>();
    }

    public void Do()
    {
        foreach (var t in _overwritten) _layer.Tiles.Remove(t);
        foreach (var (tile, oldX, oldY) in _moved)
        {
            tile.X = oldX + _dx;
            tile.Y = oldY + _dy;
        }
        _state.InvalidateOccupancy();
    }

    public void Undo()
    {
        foreach (var (tile, oldX, oldY) in _moved)
        {
            tile.X = oldX;
            tile.Y = oldY;
        }
        foreach (var t in _overwritten)
            if (!_layer.Tiles.Contains(t)) _layer.Tiles.Add(t);
        _state.InvalidateOccupancy();
    }
}
