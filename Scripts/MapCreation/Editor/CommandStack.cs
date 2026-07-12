namespace Fableland.MapCreation.Editor;

using System;
using System.Collections.Generic;

/// <summary>
/// GDD §7.7 — one editor action, authored so a whole mouse-down→up stroke (or a
/// layer add/remove/reorder, or a property edit) is a single undoable unit.
/// </summary>
public interface IEditorCommand
{
    string Name { get; }
    void Do();
    void Undo();
}

/// <summary>
/// GDD §7.7 — the undo/redo stack. Deliberately Godot-free (plain C#, no `using Godot`)
/// so it stays headlessly unit-testable, same layering rule as `Scripts/MapCreation/Data/`.
///
/// Model: <see cref="_cursor"/> counts how many of the stored commands are currently
/// "applied" (0..Count). Push executes immediately, clears any redo tail, and appends;
/// Undo/Redo just move the cursor. The unsaved-changes dot is `IsDirty`, comparing the
/// cursor against a marker set by <see cref="MarkSaved"/>.
/// </summary>
public sealed class CommandStack
{
    public const int Capacity = 200;

    private readonly List<IEditorCommand> _commands = new();
    private int _cursor;

    /// <summary>Cursor value at the last <see cref="MarkSaved"/> call. -2 means
    /// "unreachable" — the saved position fell off the trimmed history (capacity drop),
    /// so the document reads as permanently dirty until the next save.</summary>
    private int _savedPosition;

    /// <summary>Fired after every Push/Undo/Redo/MarkSaved so UI can refresh (dirty dot,
    /// rail highlight, etc). Scene-local (dies with the editor scene) — no `_ExitTree`
    /// unsubscribe needed; this isn't an autoload publisher (KNOWLEDGE v0.5.4 caveat is
    /// about that different case).</summary>
    public event Action Changed;

    public bool CanUndo => _cursor > 0;
    public bool CanRedo => _cursor < _commands.Count;
    public bool IsDirty => _cursor != _savedPosition;

    /// <summary>Executes <paramref name="command"/>.Do() immediately, drops any redo
    /// tail, and appends. Drops the OLDEST entry once capacity is exceeded (GDD §7.7:
    /// "capacity 200").</summary>
    public void Push(IEditorCommand command)
    {
        if (command == null) return;

        command.Do();

        if (_cursor < _commands.Count)
            _commands.RemoveRange(_cursor, _commands.Count - _cursor);

        _commands.Add(command);
        _cursor++;

        if (_commands.Count > Capacity)
        {
            _commands.RemoveAt(0);
            _cursor--;

            // Dropping the oldest entry shifts every stored position back by one. A
            // saved marker still inside the retained history shifts with it; a marker
            // that pointed at the now-removed boundary (0) can never be reached again,
            // so it latches to -2 ("unreachable") rather than drifting to -1.
            if (_savedPosition == 0) _savedPosition = -2;
            else if (_savedPosition > 0) _savedPosition--;
        }

        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (!CanUndo) return false;

        _cursor--;
        _commands[_cursor].Undo();
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;

        _commands[_cursor].Do();
        _cursor++;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Records the current cursor position as "saved" — <see cref="IsDirty"/>
    /// is false until the next mutation.</summary>
    public void MarkSaved()
    {
        _savedPosition = _cursor;
        Changed?.Invoke();
    }
}
