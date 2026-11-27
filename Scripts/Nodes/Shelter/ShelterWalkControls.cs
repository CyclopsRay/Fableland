using Godot;

namespace Fableland.Shelter;

/// <summary>
/// What the team's lead may do inside a shelter (NODES §5.0): walk, jump, drop and nothing
/// else. It wraps the player's own controls (or a check's <see cref="VirtualPad"/>) and holds
/// every attack, skill and the ultimate shut, so the fighter's kit — which reads
/// <c>Controls</c> like any kit — never fires in a shelter, and E is free to be the interact
/// key. While a dialog or Team Build is open the walker stands still: the same keys are
/// moving a dialog's focus.
/// </summary>
public sealed class ShelterWalkControls : ICharacterControls
{
    private readonly ICharacterControls _source;

    public ShelterWalkControls(ICharacterControls source) => _source = source ?? PlayerControls.Instance;

    /// <summary>True while a dialog or panel has the keys: nothing reaches the fighter.</summary>
    public bool Frozen { get; set; }

    /// <summary>The buttons a walker has: moving and jumping, never fighting.</summary>
    public static bool Passes(ControlAction action) => action is ControlAction.MoveLeft or ControlAction.MoveRight
        or ControlAction.MoveUp or ControlAction.MoveDown or ControlAction.Jump;

    public bool Held(ControlAction action) => !Frozen && Passes(action) && _source.Held(action);
    public bool JustPressed(ControlAction action) => !Frozen && Passes(action) && _source.JustPressed(action);
    public bool JustReleased(ControlAction action) => Passes(action) && _source.JustReleased(action);

    /// <summary>Nobody aims in a shelter: straight ahead of the body.</summary>
    public Vector2 AimPoint(Node2D body) => body.GlobalPosition + new Vector2(64f, 0f);

    public float FacingHint => Frozen ? 0f : _source.FacingHint;
}
