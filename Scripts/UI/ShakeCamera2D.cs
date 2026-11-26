using Godot;

/// <summary>
/// The fighter's camera, and the four things that can move it for a moment
/// (<c>Docs/Feedback.gdd</c> §5):
///
///   • SHAKE — trauma in [0,1], decaying linearly; the offset scales with trauma² and is read
///     from smooth noise along time, so it wanders instead of jumping to a new random spot
///     every frame (and looks the same at any frame rate).
///   • KICK — a shove along a direction that springs straight back: the part of a hit that
///     says which way it went.
///   • ROLL — a degree or less of rotation, for the heaviest hits only.
///   • PUNCH — a small zoom in and back out.
///
/// It decides nothing: <c>FeelDirector</c> listens to the cue bus and calls in here with
/// numbers from its table. The player's comfort settings scale everything this class does
/// (<see cref="FeedbackSettings.ScreenShake"/>, <see cref="FeedbackSettings.CameraPunch"/>),
/// the tsunami's sustained channel included.
/// </summary>
public partial class ShakeCamera2D : Camera2D
{
    // Single-camera prototype: lets non-owning scripts trigger a shake without needing a
    // reference to the player's CharacterController. Only a Player-side body has one.
    public static ShakeCamera2D Instance { get; private set; }

    [Export] public float MaxOffset = 30f;      // px at full trauma
    [Export] public float Decay = 3f;           // trauma lost per second
    /// <summary>How fast the shake wanders. Tuned so the offset reverses about twenty times
    /// a second (<c>FeedbackCheck</c> measures it).</summary>
    [Export] public float NoiseSpeed = 10.5f;
    [Export] public float MaxRoll = 0.015f;     // rad at full roll trauma — under a degree
    [Export] public float MaxKick = 14f;        // px a kick can never exceed
    [Export] public float KickReturn = 16f;     // 1/s — a kick is half gone in ~45 ms

    private const int NoiseSeed = 20261125;     // presentation's own; never a run's stream

    private FastNoiseLite _noise;
    private float _time;
    private float _trauma;
    private float _sustainedTrauma;
    private float _rollTrauma;
    private Vector2 _kick;
    private Vector2 _offset;
    private bool _rolling;

    // Zoom punch: rise to the peak, hold, fall back.
    private enum PunchPhase { Idle, In, Hold, Out }
    private PunchPhase _punchPhase;
    private float _punch;                       // current extra zoom (0.02 = 1.02×)
    private float _punchPeak;
    private float _punchIn, _punchHold, _punchOut, _punchClock;
    private Vector2 _restZoom = Vector2.One;

    /// <summary>Decaying impact trauma, 0–1.</summary>
    public float Trauma => _trauma;
    /// <summary>Roll trauma, 0–1.</summary>
    public float RollTrauma => _rollTrauma;
    /// <summary>The kick still to spring back, px.</summary>
    public Vector2 KickOffset => _kick;
    /// <summary>The extra zoom of the punch in progress (0.02 = 1.02×).</summary>
    public float PunchAmount => _punch;
    /// <summary>The offset this camera applied last frame (shake + kick), px.</summary>
    public Vector2 AppliedOffset => _offset;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Ready() => _noise = NewNoise();

    private static FastNoiseLite NewNoise() => new()
    {
        NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        FractalType = FastNoiseLite.FractalTypeEnum.None,
        Frequency = 1f,
        Seed = NoiseSeed,
    };

    /// <summary>Add trauma with no ceiling but 1 — the legacy call a kit that is not on the
    /// cue bus yet still uses. Prefer the capped form.</summary>
    public void AddTrauma(float amount) => _trauma = Mathf.Clamp(_trauma + amount, 0f, 1f);

    /// <summary>Add trauma, but never past <paramref name="cap"/>: a tier of hit has a
    /// ceiling it cannot shake above however many land, and it never lowers a shake a
    /// heavier hit already started.</summary>
    public void AddTrauma(float amount, float cap)
    {
        if (amount <= 0f) return;
        cap = Mathf.Clamp(cap, 0f, 1f);
        if (_trauma >= cap) return;
        _trauma = Mathf.Min(cap, _trauma + amount);
    }

    /// <summary>Presentation effects such as the tsunami can hold a continuous shake
    /// without fighting the normal decaying impact-trauma channel.</summary>
    public void SetSustainedTrauma(float amount) =>
        _sustainedTrauma = Mathf.Clamp(amount, 0f, 1f);

    /// <summary>Shove the view <paramref name="pixels"/> along <paramref name="direction"/>;
    /// it springs back by itself. Kicks add, up to <see cref="MaxKick"/>.</summary>
    public void Kick(Vector2 direction, float pixels)
    {
        if (pixels <= 0f || direction.LengthSquared() < 0.0001f) return;
        _kick = (_kick + direction.Normalized() * pixels).LimitLength(MaxKick);
    }

    /// <summary>Add roll trauma (0–1); the roll scales with its square, like the shake.</summary>
    public void AddRoll(float amount)
    {
        if (amount > 0f) _rollTrauma = Mathf.Clamp(_rollTrauma + amount, 0f, 1f);
    }

    /// <summary>Zoom in by <paramref name="amount"/> (0.02 = 1.02×) over
    /// <paramref name="inSeconds"/>, hold, and ease back out. A punch never interrupts a
    /// bigger one, and several in one moment are one punch, not a sum.</summary>
    public void Punch(float amount, float inSeconds, float holdSeconds, float outSeconds)
    {
        if (amount <= 0f) return;
        if (_punchPhase != PunchPhase.Idle && amount <= _punchPeak) return;
        if (_punchPhase == PunchPhase.Idle) _restZoom = Zoom;   // whatever the scene authored
        _punchPeak = amount;
        _punchIn = Mathf.Max(inSeconds, 0.001f);
        _punchHold = Mathf.Max(holdSeconds, 0f);
        _punchOut = Mathf.Max(outSeconds, 0.001f);
        _punchPhase = PunchPhase.In;
    }

    /// <summary>The shake direction at a moment in time, each axis in about [-1, 1] — the
    /// smooth noise the camera reads. Static and pure, so a check can measure how fast it
    /// reverses without a camera.</summary>
    public static Vector2 ShakeDirection(FastNoiseLite noise, float time, float speed)
    {
        float t = time * speed;
        return new Vector2(noise.GetNoise2D(t, 11.5f), noise.GetNoise2D(47.25f, t));
    }

    /// <summary>The noise a camera shakes with — for checks.</summary>
    public static FastNoiseLite CreateShakeNoise() => NewNoise();

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        float scale = FeedbackSettings.ScreenShake;

        // Shake: the stronger of the decaying impacts and whatever is being held.
        float active = Mathf.Max(_trauma, _sustainedTrauma);
        if (_trauma > 0f) _trauma = Mathf.Max(0f, _trauma - Decay * dt);
        Vector2 offset = Vector2.Zero;
        if (active > 0f && scale > 0f)
            offset = ShakeDirection(_noise, _time, NoiseSpeed) * (MaxOffset * active * active * scale);

        // Kick: springs back exponentially, so it is frame-rate independent.
        if (_kick != Vector2.Zero)
        {
            offset += _kick * scale;
            _kick *= Mathf.Exp(-KickReturn * dt);
            if (_kick.LengthSquared() < 0.04f) _kick = Vector2.Zero;
        }
        if (offset != _offset)
        {
            _offset = offset;
            Offset = offset;
        }

        // Roll. A camera ignores rotation by default; it is switched on only while rolling,
        // and set globally so nothing the body does can leak into the view.
        float roll = 0f;
        if (_rollTrauma > 0f)
        {
            if (scale > 0f)
                roll = MaxRoll * _rollTrauma * _rollTrauma * scale * _noise.GetNoise2D(_time * NoiseSpeed, 83.75f);
            _rollTrauma = Mathf.Max(0f, _rollTrauma - Decay * dt);
        }
        if (roll != 0f)
        {
            if (!_rolling) { _rolling = true; IgnoreRotation = false; }
            GlobalRotation = roll;
        }
        else if (_rolling)
        {
            _rolling = false;
            Rotation = 0f;
            IgnoreRotation = true;
        }

        UpdatePunch(dt);
    }

    private void UpdatePunch(float dt)
    {
        if (_punchPhase == PunchPhase.Idle) return;
        switch (_punchPhase)
        {
            case PunchPhase.In:
                _punch = Mathf.MoveToward(_punch, _punchPeak, _punchPeak / _punchIn * dt);
                if (_punch >= _punchPeak) { _punchPhase = PunchPhase.Hold; _punchClock = _punchHold; }
                break;
            case PunchPhase.Hold:
                _punchClock -= dt;
                if (_punchClock <= 0f) _punchPhase = PunchPhase.Out;
                break;
            case PunchPhase.Out:
                _punch = Mathf.MoveToward(_punch, 0f, _punchPeak / _punchOut * dt);
                if (_punch <= 0f) { _punch = 0f; _punchPeak = 0f; _punchPhase = PunchPhase.Idle; }
                break;
        }
        Zoom = _restZoom * (1f + _punch * FeedbackSettings.CameraPunch);
    }
}
