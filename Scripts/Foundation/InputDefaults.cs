using Godot;

/// <summary>
/// Input actions the game registers itself when <c>project.godot</c> does not define them.
///
/// Every other action lives in <c>project.godot</c>. That file is re-saved by whichever
/// editor opens a checkout and is often left uncommitted, so an action that code reads can
/// reach a checkout without its entry — and reading an action that does not exist logs an
/// error every frame. An action registered here always arrives with the code that reads it.
/// An entry of the same name in <c>project.godot</c> wins, so the keys can still be rebound
/// there (Project Settings → Input Map → add an action with this name).
/// </summary>
public static class InputDefaults
{
    /// <summary>Swim up inside a soft volume: W / Up arrow. Its counterpart is
    /// <c>move_down</c> (S / Down arrow), which <c>project.godot</c> defines.</summary>
    public const string MoveUp = "move_up";

    /// <summary>Registers any missing action. Cheap and repeatable. The player's controls
    /// call it before they first read input (<see cref="PlayerControls"/> — fighters read
    /// their buttons through <c>Controls</c>, never from <c>Input</c>); a test that presses
    /// one of these actions itself calls it first.</summary>
    public static void Ensure() => EnsureKeys(MoveUp, Key.W, Key.Up);

    private static void EnsureKeys(string action, params Key[] physicalKeys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (Key key in physicalKeys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
