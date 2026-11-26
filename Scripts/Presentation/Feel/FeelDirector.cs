using Godot;
using Fableland.Data;

/// <summary>
/// The feel director (<c>Docs/Feedback.gdd</c> §5) — a PRESENTATION autoload. It listens to
/// the cue bus and turns hits and a few named moments into camera shake, a directional kick,
/// a small roll, a zoom punch and the hit flash, from the numbers in <see cref="FeelTable"/>.
///
/// It is one of three directors gameplay never names (feel here; VFX and audio plug into the
/// same bus the same way): gameplay raises a cue and forgets it. Nothing in this file can
/// change a fight — it reads cues, moves the camera, and asks a body to flash. Hit-stop, the
/// one part of feel that moves bodies in time, is gameplay's own (<c>FeedbackTable</c>).
///
/// Justification as an autoload (T10 §1): it must outlive every arena scene, the way the
/// bus it listens to does. It allocates nothing per cue (T40 §2).
/// </summary>
public partial class FeelDirector : Node
{
    public static FeelDirector Instance { get; private set; }

    private readonly FlashLimiter _flashes = new();
    private Viewport _viewport;

    // Shake coalescing: every hit that lands in one frame is one camera event.
    private ulong _shakeFrame = ulong.MaxValue;
    private float _frameShakeAdd;
    private float _frameShakeCap;
    private int _frameExtras;

    /// <summary>True when the arena being played has a pale backdrop: bodies flash dark.</summary>
    public bool ArenaIsLight { get; private set; }
    /// <summary>The world the arena's map was built for, as it said so ("" if it did not).</summary>
    public string ArenaWorld { get; private set; } = "";
    /// <summary>The colour a hit flash uses on this arena.</summary>
    public Color FlashColour => ArenaIsLight ? FeelTable.DarkFlash : FeelTable.LightFlash;
    /// <summary>How many full flashes the limits turned into tints since boot — for checks.</summary>
    public int FlashesDemoted { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
        // Paired with _ExitTree — the bus is static and outlives everything (T10 §1).
        CueBus.Raised += OnCue;
        CueBus.HitLanded += OnHit;
    }

    public override void _ExitTree()
    {
        CueBus.Raised -= OnCue;
        CueBus.HitLanded -= OnHit;
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        _viewport = GetViewport();
        SetProcess(false);   // everything here happens on a cue
        if (!OS.IsDebugBuild()) return;
        // Boot validation (T10 §5): the tables this director and the bodies run on.
        foreach (string problem in FeelTable.SelfTest()) GD.PushError("[FeelTable] " + problem);
        foreach (string problem in FeedbackTable.SelfTest()) GD.PushError("[FeedbackTable] " + problem);
    }

    // ── Hits ────────────────────────────────────────────────────────────────────────
    private void OnHit(HitCue hit)
    {
        FeelTable.WeightFeel feel = FeelTable.For(hit.Weight);
        // Being hit is felt more than hitting: the camera reads one tier up for the player.
        FeelTable.WeightFeel camera = hit.VictimIsPlayer ? FeelTable.For(FeelTable.Heavier(hit.Weight)) : feel;

        ShakeCamera2D view = ShakeCamera2D.Instance;
        if (view != null)
        {
            float add = camera.ShakeAdd, cap = camera.ShakeCap;
            if (hit.Killed)
            {
                add += FeelTable.KillShakeAdd;
                cap = Mathf.Min(1f, cap + FeelTable.KillShakeAdd);
            }
            Shake(view, add, cap);
            if (camera.KickPx > 0f) view.Kick(hit.Direction, camera.KickPx);
            if (camera.Roll > 0f) view.AddRoll(camera.Roll);
            float zoom = hit.Killed ? Mathf.Max(camera.Zoom, FeelTable.KillZoom) : camera.Zoom;
            if (zoom > 0f) view.Punch(zoom, FeelTable.ZoomInSeconds, FeelTable.ZoomHoldSeconds, FeelTable.ZoomOutSeconds);
        }

        Flash(hit.VictimBody, feel.FlashSeconds, feel.FullFlash);
    }

    // ── Named moments ───────────────────────────────────────────────────────────────
    private void OnCue(Cue cue)
    {
        if (cue.Owner == CueOwners.Flow && cue.Action == "arena.enter") EnterArena(cue);

        FeelTable.CueFeel feel = FeelTable.ByCue.Resolve(cue);
        if (feel == null) return;
        if (feel.PlayerOnly && cue.Side != CombatSide.Player) return;
        if (cue.Magnitude < feel.MinMagnitude) return;

        ShakeCamera2D view = ShakeCamera2D.Instance;
        if (view != null)
        {
            if (feel.ShakeAdd > 0f && IsDue(ref feel.ShakeReadyAt, feel.ShakeEverySeconds)) Shake(view, feel.ShakeAdd, feel.ShakeCap);
            if (feel.Roll > 0f) view.AddRoll(feel.Roll);
            if (feel.Zoom > 0f) view.Punch(feel.Zoom, feel.ZoomIn, feel.ZoomHold, feel.ZoomOut);
        }
        if (feel.FlashSeconds > 0f && IsDue(ref feel.FlashReadyAt, feel.FlashEverySeconds))
            Flash(cue.Source as IFeedbackBody, feel.FlashSeconds, feel.FullFlash);
    }

    /// <summary>What the arena says about itself: which world, and how light its backdrop
    /// is — a white flash is invisible on bare paper, a dark one on a night sky.</summary>
    private void EnterArena(in Cue cue)
    {
        ArenaWorld = cue.Tag ?? "";
        ArenaIsLight = cue.Magnitude >= FeelTable.LightArenaLuminance;   // unknown (−1) keeps the light flash
    }

    /// <summary>A row's own cooldown, in real seconds: true (and re-armed) when it may fire.</summary>
    private static bool IsDue(ref double readyAt, float everySeconds)
    {
        if (everySeconds <= 0f) return true;
        double now = Now();
        if (now < readyAt) return false;
        readyAt = now + everySeconds;
        return true;
    }

    /// <summary>Real seconds: what the eye sees, whatever the engine's time scale is.</summary>
    private static double Now() => Time.GetTicksMsec() * 0.001;

    /// <summary>
    /// One camera event per frame, however many hits it holds: the first adds its tier's
    /// trauma, a heavier one tops that up to its own, and each further victim adds a sliver —
    /// so a swing through a crowd cannot sum past the cap of the heaviest hit in it.
    /// </summary>
    private void Shake(ShakeCamera2D view, float add, float cap)
    {
        if (add <= 0f) return;
        ulong frame = Engine.GetProcessFrames();
        if (frame != _shakeFrame)
        {
            _shakeFrame = frame;
            _frameShakeAdd = 0f;
            _frameShakeCap = 0f;
            _frameExtras = 0;
        }
        _frameShakeCap = Mathf.Max(_frameShakeCap, cap);
        if (add > _frameShakeAdd)
        {
            view.AddTrauma(add - _frameShakeAdd, _frameShakeCap);
            _frameShakeAdd = add;
        }
        else if (_frameExtras < FeelTable.MaxExtraVictims)
        {
            _frameExtras++;
            view.AddTrauma(FeelTable.ExtraVictimShake, _frameShakeCap);
        }
    }

    /// <summary>Flash a body in the arena's flash colour — at the strength the player's
    /// setting asks for, and never past the photosensitivity limits: what they refuse is
    /// shown as a tint.</summary>
    private void Flash(IFeedbackBody body, float seconds, bool full)
    {
        if (body == null || seconds <= 0f) return;
        float strength = FeelTable.TintStrength;
        if (full)
        {
            float wanted = Mathf.Lerp(FeelTable.TintStrength, 1f, FeedbackSettings.Flash);
            if (wanted > FeelTable.TintStrength)
            {
                if (_flashes.AllowFull(ViewShare(body), Now())) strength = wanted;
                else FlashesDemoted++;
            }
        }
        Color colour = FlashColour;
        body.FlashTint(new Color(colour.R, colour.G, colour.B, strength), seconds);
    }

    /// <summary>The share of the view a body covers, taking it as a disc of its radius.</summary>
    private float ViewShare(IFeedbackBody body)
    {
        float radius = body.FeedbackRadius;
        Vector2 view = _viewport != null ? _viewport.GetVisibleRect().Size : new Vector2(1152f, 648f);
        float area = view.X * view.Y;
        return area > 1f ? Mathf.Pi * radius * radius / area : 1f;
    }
}
