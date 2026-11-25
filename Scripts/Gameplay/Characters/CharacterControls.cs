using Godot;

/// <summary>
/// Everything a fighter's controller can ask of its body. The player's keys map onto
/// these; a boss's brain presses the same ones (FOES.gdd §8: a BOSS fights with the kit
/// the player will recruit, so it uses the same buttons).
/// </summary>
public enum ControlAction
{
    MoveLeft,
    MoveRight,
    MoveUp,
    MoveDown,
    Jump,
    Attack,
    Skill1,
    Skill2,
    Ult,
}

/// <summary>
/// Where a <see cref="CharacterController"/> reads its input from. Kits never touch the
/// <c>Input</c> singleton or the mouse: they ask their body's
/// <see cref="CharacterController.Controls"/>, so one kit can be worn by the player
/// (<see cref="PlayerControls"/>) or driven by an AI (<see cref="VirtualPad"/>).
///
/// Edges follow Godot's own rule: "just pressed" is true for exactly one frame of the loop
/// that asks — one physics tick for movement, one process frame for abilities.
/// </summary>
public interface ICharacterControls
{
    bool Held(ControlAction action);
    bool JustPressed(ControlAction action);
    bool JustReleased(ControlAction action);

    /// <summary>The world-space point this fighter is aiming at (the mouse, for a player).</summary>
    Vector2 AimPoint(Node2D body);

    /// <summary>−1 / +1 turns a fighter that is not moving to face left / right; 0 leaves
    /// its facing alone. A player's facing follows their movement keys, so theirs is 0.</summary>
    float FacingHint { get; }
}

/// <summary>The player's keyboard and mouse, through the project's InputMap.</summary>
public sealed class PlayerControls : ICharacterControls
{
    public static readonly PlayerControls Instance = new();

    // Indexed by ControlAction. Every one of these is defined in project.godot except
    // "move_up" (swim up, v0.11.1), which the game registers itself: see InputDefaults.
    private static readonly StringName[] Names =
    {
        "move_left", "move_right", InputDefaults.MoveUp, "move_down", "jump", "attack", "skill1", "skill2", "ult",
    };

    // This is the one class that asks the InputMap for a fighter's actions, so it is the
    // one that makes sure they are all there before the first read: asking for an action
    // the InputMap does not have logs an error every frame. Every CharacterController
    // touches Instance when it is constructed, a BOSS included.
    private PlayerControls() => InputDefaults.Ensure();

    public static StringName ActionName(ControlAction action) => Names[(int)action];

    public bool Held(ControlAction action) => Input.IsActionPressed(Names[(int)action]);
    public bool JustPressed(ControlAction action) => Input.IsActionJustPressed(Names[(int)action]);
    public bool JustReleased(ControlAction action) => Input.IsActionJustReleased(Names[(int)action]);

    public Vector2 AimPoint(Node2D body) => body.GetGlobalMousePosition();
    public float FacingHint => 0f;
}

/// <summary>
/// A controller with no hands on it: something else (a boss brain, a test) says which
/// buttons are down. Pure state — whoever owns it calls <see cref="StepProcess"/> once per
/// process frame and <see cref="StepPhysics"/> once per physics tick, BEFORE the body it
/// drives runs, and that is what turns "held" into edges.
///
/// Two ways to press: <see cref="Hold"/> keeps a button down until it is let go, and
/// <see cref="Tap"/> presses it for one frame of each loop. A tap is owed to both loops
/// separately, so a jump tapped between two physics ticks is still seen by the next tick
/// (the two loops do not run in step).
/// </summary>
public sealed class VirtualPad : ICharacterControls
{
    private const int Count = (int)ControlAction.Ult + 1;

    private readonly bool[] _wanted = new bool[Count];
    private readonly bool[] _tapProcess = new bool[Count];
    private readonly bool[] _tapPhysics = new bool[Count];
    private readonly bool[] _nowProcess = new bool[Count];
    private readonly bool[] _wasProcess = new bool[Count];
    private readonly bool[] _nowPhysics = new bool[Count];
    private readonly bool[] _wasPhysics = new bool[Count];

    /// <summary>The world point the driver is aiming at.</summary>
    public Vector2 Aim { get; set; }

    /// <summary>−1 / +1 / 0 — see <see cref="ICharacterControls.FacingHint"/>.</summary>
    public float Facing { get; set; }

    public void Hold(ControlAction action, bool held = true) => _wanted[(int)action] = held;

    public void Tap(ControlAction action)
    {
        _tapProcess[(int)action] = true;
        _tapPhysics[(int)action] = true;
    }

    /// <summary>Lets go of every held button. Taps already issued still land.</summary>
    public void ReleaseHolds()
    {
        for (int i = 0; i < Count; i++) _wanted[i] = false;
    }

    /// <summary>Lets go of everything, taps included, and forgets where it was looking.</summary>
    public void ReleaseAll()
    {
        for (int i = 0; i < Count; i++)
        {
            _wanted[i] = false;
            _tapProcess[i] = false;
            _tapPhysics[i] = false;
        }
        Facing = 0f;
    }

    public void StepProcess() => Step(_nowProcess, _wasProcess, _tapProcess);
    public void StepPhysics() => Step(_nowPhysics, _wasPhysics, _tapPhysics);

    private void Step(bool[] now, bool[] was, bool[] tap)
    {
        for (int i = 0; i < Count; i++)
        {
            was[i] = now[i];
            now[i] = _wanted[i] || tap[i];
            tap[i] = false;
        }
    }

    private static bool InPhysics => Engine.IsInPhysicsFrame();

    public bool Held(ControlAction action) => (InPhysics ? _nowPhysics : _nowProcess)[(int)action];

    public bool JustPressed(ControlAction action)
    {
        int i = (int)action;
        return InPhysics ? _nowPhysics[i] && !_wasPhysics[i] : _nowProcess[i] && !_wasProcess[i];
    }

    public bool JustReleased(ControlAction action)
    {
        int i = (int)action;
        return InPhysics ? !_nowPhysics[i] && _wasPhysics[i] : !_nowProcess[i] && _wasProcess[i];
    }

    public Vector2 AimPoint(Node2D body) => Aim;
    public float FacingHint => Facing;

    /// <summary>Structural guard over the edge rules, driving both loops by hand.</summary>
    public static System.Collections.Generic.List<string> SelfTest()
    {
        var failures = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("VirtualPad mismatch: " + what); }

        // Outside a physics tick the pad answers for the process loop.
        var pad = new VirtualPad();
        Check(!pad.Held(ControlAction.Jump) && !pad.JustPressed(ControlAction.Jump), "a new pad holds nothing");

        pad.Hold(ControlAction.MoveLeft);
        Check(!pad.Held(ControlAction.MoveLeft), "a press is not seen until the pad is stepped");
        pad.StepProcess();
        Check(pad.Held(ControlAction.MoveLeft) && pad.JustPressed(ControlAction.MoveLeft), "first frame of a hold is just-pressed");
        pad.StepProcess();
        Check(pad.Held(ControlAction.MoveLeft) && !pad.JustPressed(ControlAction.MoveLeft), "a hold is just-pressed for one frame only");
        pad.Hold(ControlAction.MoveLeft, false);
        pad.StepProcess();
        Check(!pad.Held(ControlAction.MoveLeft) && pad.JustReleased(ControlAction.MoveLeft), "letting go is just-released");
        pad.StepProcess();
        Check(!pad.JustReleased(ControlAction.MoveLeft), "just-released lasts one frame");

        pad.Tap(ControlAction.Attack);
        pad.StepProcess();
        Check(pad.JustPressed(ControlAction.Attack), "a tap is a press on the next frame");
        pad.StepProcess();
        Check(!pad.Held(ControlAction.Attack) && pad.JustReleased(ControlAction.Attack), "a tap lets go on the frame after");

        // A tap is owed to each loop: stepping one loop many times must not use up the other's.
        pad.Tap(ControlAction.Jump);
        pad.StepProcess();
        pad.StepProcess();
        pad.StepProcess();
        Check(pad._tapPhysics[(int)ControlAction.Jump], "a tap stays owed to the physics loop however many process frames pass");
        pad.StepPhysics();
        Check(pad._nowPhysics[(int)ControlAction.Jump] && !pad._wasPhysics[(int)ControlAction.Jump], "the physics loop then sees the tap as a press");
        pad.StepPhysics();
        Check(!pad._nowPhysics[(int)ControlAction.Jump], "and sees it exactly once");

        // Re-asserting a hold every decision (release, then hold again, in one go) is not an edge.
        pad.Hold(ControlAction.MoveRight);
        pad.StepProcess();
        pad.StepProcess();
        pad.ReleaseHolds();
        pad.Hold(ControlAction.MoveRight);
        pad.StepProcess();
        Check(pad.Held(ControlAction.MoveRight) && !pad.JustPressed(ControlAction.MoveRight) && !pad.JustReleased(ControlAction.MoveRight),
            "releasing and re-holding between two steps is one unbroken hold");

        pad.Tap(ControlAction.Skill1);
        pad.ReleaseAll();
        pad.StepProcess();
        Check(!pad.Held(ControlAction.Skill1) && !pad.Held(ControlAction.MoveRight), "ReleaseAll drops holds and pending taps");
        return failures;
    }
}
