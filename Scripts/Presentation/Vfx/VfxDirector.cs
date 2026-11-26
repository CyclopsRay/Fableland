namespace Fableland.Vfx;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.Data;

/// <summary>
/// The VFX director (<c>Docs/VFX.gdd</c>) — a PRESENTATION autoload, sibling of
/// <c>FeelDirector</c>. It listens to the cue bus and turns each cue into a pooled effect
/// drawn in the art medium of whoever caused it: an attack in its attacker's medium, a
/// body's own effects in that body's, and dust, water, hazards and ambience in the arena's.
///
/// Gameplay never names it and it never calls gameplay: nothing here can change a fight. It
/// draws its own randomness (never a run's <c>DetRandom</c>), allocates nothing per cue
/// (T40 §2), and keeps a budget — live marks, live emitters, live particles, effects started
/// per frame — that drops decoration first and never an effect that tells the player
/// something.
///
/// Justification as an autoload (T10 §1): like the bus it listens to, it must outlive every
/// arena. Its pooled nodes are its own children, so no scene ever owns or frees them.
/// </summary>
public partial class VfxDirector : Node2D
{
    public static VfxDirector Instance { get; private set; }

    /// <summary>Above every actor (z 0), under the map's closeview layers (z 20): an effect
    /// hides behind foreground the way a fighter does.</summary>
    public const int WorldZ = 10;
    /// <summary>Above the closeview layers: what the player must be able to read.</summary>
    public const int MustReadZ = 30;
    public const int AmbientZ = 5;

    // ── pools ────────────────────────────────────────────────────────────────────
    private readonly List<VfxMark> _marks = new(VfxBudget.Marks);
    private readonly List<VfxBurst> _bursts = new(VfxBudget.Bursts);
    private readonly List<VfxMark> _freeMarks = new(VfxBudget.Marks);
    private readonly List<VfxBurst> _freeBursts = new(VfxBudget.Bursts);
    private readonly VfxAmbient[] _ambient = new VfxAmbient[VfxBudget.Ambient];
    private CanvasItemMaterial _additive;

    // ── loops: what is running, and for whom ─────────────────────────────────────
    private struct LoopSlot
    {
        public int Id;
        public Node2D Source;
        public bool HasSource;
        public string Owner;
        public string Action;
    }
    private readonly LoopSlot[] _loops = new LoopSlot[VfxBudget.Loops];
    private int _nextLoopId = 1;

    // ── the arena ────────────────────────────────────────────────────────────────
    /// <summary>The medium of the world being fought in.</summary>
    public VfxStyle ArenaStyle { get; private set; }
    public string ArenaWorld { get; private set; } = "";
    public VfxCanvas ArenaCanvas { get; private set; } = VfxCanvas.Mid;
    public bool InArena { get; private set; }
    private Node2D _arena;
    private bool _arenaHasSource;
    private Node _arenaScene;

    // ── the context blackboard: how the fight is going ───────────────────────────
    /// <summary>Combat intensity, 0–1: rises with every hit and cue, cools in seconds.</summary>
    public float Intensity { get; private set; }
    /// <summary>Hits the player's side has landed without a pause.</summary>
    public int Combo { get; private set; }
    /// <summary>0 no boss · 1 a boss is up · 2 it is at low health.</summary>
    public int BossPhase { get; private set; }
    private Node2D _boss;
    private double _lastComboHit = -100.0;
    private double _now;

    // ── the budget, as counted ───────────────────────────────────────────────────
    public int LiveMarks { get; private set; }
    public int LiveBursts { get; private set; }
    /// <summary>Particles alive in bursts and ambience.</summary>
    public int LiveParticles { get; private set; }
    public int LiveLoops { get; private set; }
    public int LiveAmbient { get; private set; }
    public int PeakMarks { get; private set; }
    public int PeakBursts { get; private set; }
    public int PeakParticles { get; private set; }
    /// <summary>Effects started / refused since boot, by priority (index = (int)VfxPriority).</summary>
    public readonly int[] Started = new int[3];
    public readonly int[] Dropped = new int[3];
    /// <summary>Pieces a Critical effect took from a lesser one because the pool was full.</summary>
    public int Stolen { get; private set; }
    public int PoolNodes => _marks.Count + _bursts.Count + _ambient.Length;
    private ulong _spawnFrame = ulong.MaxValue;
    private int _spawnsThisFrame;
    private ulong _viewFrame = ulong.MaxValue;
    private Rect2 _view;

    // ── switches for tools and checks (never read by the game) ───────────────────
    /// <summary>Draw every effect in this medium, whoever owns it.</summary>
    public string ForceStyle;
    /// <summary>Lift the budget and let the pools grow — the gallery, and coverage checks.</summary>
    public bool Unlimited;
    /// <summary>Do not start decoration that is off screen.</summary>
    public bool CullOffscreen = true;
    /// <summary>Hold every effect on its current frame.</summary>
    public bool Frozen { get; private set; }
    /// <summary>Bytes the director's own per-frame work and its marks' drawing allocated,
    /// while <see cref="MeasureAllocation"/> is on.</summary>
    public long AllocatedInUpdate { get; private set; }
    public bool MeasureAllocation;

    private uint _rng = 0x9E3779B9u;
    private long _serial;

    // ── lifetime ─────────────────────────────────────────────────────────────────
    public override void _EnterTree()
    {
        Instance = this;
        // Paired with _ExitTree — the bus is static and outlives everything (T10 §1).
        CueBus.Raised += OnCue;
        CueBus.HitLanded += OnHit;
        CueBus.LoopStarted += OnLoopStarted;
        CueBus.LoopStopped += OnLoopStopped;
        FeedbackSettings.Changed += OnSettingsChanged;
    }

    public override void _ExitTree()
    {
        CueBus.Raised -= OnCue;
        CueBus.HitLanded -= OnHit;
        CueBus.LoopStarted -= OnLoopStarted;
        CueBus.LoopStopped -= OnLoopStopped;
        FeedbackSettings.Changed -= OnSettingsChanged;
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        _rng ^= (uint)Time.GetTicksUsec() | 1u;
        ArenaStyle = VfxTable.Style(VfxTable.DefaultStyle);
        _additive = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        for (int i = 0; i < VfxBudget.Marks; i++) _freeMarks.Add(NewMark());
        for (int i = 0; i < VfxBudget.Bursts; i++) _freeBursts.Add(NewBurst());
        for (int i = 0; i < _ambient.Length; i++)
        {
            var ambient = new VfxAmbient { Name = "Ambient" + i, Emitting = false, Visible = false, ZIndex = AmbientZ, ZAsRelative = false };
            ambient.Tones = TwoStops();
            AddChild(ambient);
            _ambient[i] = ambient;
        }
        // Everything a cue may need is built now, not on the first hit (T40 §4).
        foreach (string id in VfxTextures.All) VfxTextures.Get(id);
        foreach (VfxStyle style in VfxTable.Styles) ResolveRecipes(style);
        foreach (VfxDef def in VfxTable.AllDefs()) FillBag(def);

        if (!OS.IsDebugBuild()) return;
        foreach (string problem in VfxTable.SelfTest()) GD.PushError("[VfxTable] " + problem);
    }

    private static Gradient TwoStops() => new() { Offsets = new[] { 0f, 1f }, Colors = new[] { Colors.White, Colors.White } };

    private VfxMark NewMark()
    {
        var mark = new VfxMark { Visible = false, ZAsRelative = false };
        AddChild(mark);
        _marks.Add(mark);
        return mark;
    }

    private VfxBurst NewBurst()
    {
        var burst = new VfxBurst { Emitting = false, OneShot = true, Visible = false, ZAsRelative = false, LocalCoords = false };
        burst.Tones = TwoStops();
        AddChild(burst);
        _bursts.Add(burst);
        return burst;
    }

    private static void ResolveRecipes(VfxStyle style)
    {
        for (int kind = 0; kind < 4; kind++)
        {
            VfxBurstRecipe recipe = style.Particles((VfxBits)kind);
            if (recipe == null || recipe.Resolved) continue;
            recipe.TextureResource = VfxTextures.Get(recipe.Texture);
            recipe.Resolved = true;
        }
    }

    // ── the bus ──────────────────────────────────────────────────────────────────
    private void OnCue(Cue cue)
    {
        Intensity = Mathf.Min(1f, Intensity + VfxTable.IntensityPerCue);
        if (cue.Owner == CueOwners.Flow)
        {
            if (cue.Action == "arena.enter") EnterArena(cue);
            else if (cue.Action == "boss.intro") { BossPhase = 1; _boss = cue.Source; }
            else if (cue.Action == "boss.defeated") { BossPhase = 0; _boss = null; }
        }
        VfxDef def = VfxTable.Cues.Resolve(cue);
        if (def == null) return;
        var input = new VfxInput(cue);
        Play(VfxTable.ForTag(def, cue.Tag), in input, 0);
    }

    private void OnLoopStarted(Cue cue)
    {
        VfxDef def = VfxTable.Cues.Resolve(cue);
        if (def == null) return;
        int slot = FindLoop(cue.Source, cue.Owner, cue.Action);
        if (slot >= 0) return;                               // already running: a restart is the same loop
        slot = FreeLoopSlot();
        if (slot < 0) return;
        int id = _nextLoopId++;
        if (_nextLoopId == int.MaxValue) _nextLoopId = 1;
        _loops[slot] = new LoopSlot { Id = id, Source = cue.Source, HasSource = cue.Source != null, Owner = cue.Owner, Action = cue.Action };
        LiveLoops++;
        var input = new VfxInput(cue);
        Play(VfxTable.ForTag(def, cue.Tag), in input, id);
    }

    private void OnLoopStopped(Cue cue)
    {
        int slot = FindLoop(cue.Source, cue.Owner, cue.Action);
        if (slot >= 0) EndLoop(slot);
    }

    private void OnHit(HitCue hit)
    {
        Intensity = Mathf.Min(1f, Intensity + VfxTable.IntensityPerHit);
        if (!hit.VictimIsPlayer)
        {
            Combo = _now - _lastComboHit <= VfxTable.ComboWindow ? Combo + 1 : 1;
            _lastComboHit = _now;
        }
        VfxDef def = VfxTable.Hits.ResolveAttack(hit) ?? VfxTable.Hits.Resolve(hit.AttackOwner, hit.AttackClass, "");
        if (def != null)
        {
            var input = new VfxInput(hit, false);
            Play(def, in input, 0);
        }
        if (hit.Weight >= VfxTable.ReactFrom || hit.Killed)
        {
            var react = new VfxInput(hit, true);
            Play(VfxTable.React, in react, 0);
        }
    }

    private void OnSettingsChanged()
    {
        if (InArena) StartAmbience();
    }

    // ── the arena ────────────────────────────────────────────────────────────────
    /// <summary>What the arena says about itself: which world (its medium), and how light
    /// its backdrop is. Everything the last arena left is recalled first.</summary>
    private void EnterArena(in Cue cue)
    {
        LeaveArena();
        ArenaWorld = cue.Tag ?? "";
        ArenaStyle = VfxTable.ArenaStyle(ArenaWorld);
        ArenaCanvas = CanvasFor(cue.Magnitude);
        _arena = cue.Source;
        _arenaHasSource = cue.Source != null;
        _arenaScene = GetTree().CurrentScene;
        InArena = true;
        WarmUp();
        StartAmbience();
    }

    /// <summary>The arena is gone: its ambience stops, every live effect returns to the pool.</summary>
    public void LeaveArena()
    {
        ClearAll();
        InArena = false;
        _arena = null;
        _arenaHasSource = false;
        _arenaScene = null;
        _boss = null;
        BossPhase = 0;
        Combo = 0;
        Intensity = 0f;
        ArenaWorld = "";
        ArenaStyle = VfxTable.Style(VfxTable.DefaultStyle);
        ArenaCanvas = VfxCanvas.Mid;
    }

    public static VfxCanvas CanvasFor(float lightness) =>
        lightness >= FeelTable.LightArenaLuminance ? VfxCanvas.Light
        : lightness >= 0f && lightness < 0.30f ? VfxCanvas.Dark
        : VfxCanvas.Mid;                                      // a mid-tone, or unknown (−1)

    /// <summary>Recall every live piece, stop every loop and the ambience. Pools keep their nodes.</summary>
    public void ClearAll()
    {
        for (int i = 0; i < _marks.Count; i++) if (_marks[i].Live) Recycle(_marks[i]);
        for (int i = 0; i < _bursts.Count; i++) if (_bursts[i].Live) Recycle(_bursts[i]);
        for (int i = 0; i < _loops.Length; i++) _loops[i] = default;
        LiveLoops = 0;
        StopAmbience();
    }

    /// <summary>One invisible mark per pipeline (plain, textured, additive) so the first
    /// real effect of a fight compiles nothing (T40 §4).</summary>
    private void WarmUp()
    {
        Vector2 at = ViewRect().GetCenter();
        for (int i = 0; i < 2; i++)
        {
            VfxMark mark = RentMark(VfxPriority.Critical);
            if (mark == null) return;
            mark.Style = ArenaStyle;
            mark.S = new VfxMarkState { Kind = VfxMarkKind.Warm, Life = 0.25f };
            mark.Priority = VfxPriority.Cosmetic;
            mark.Position = at;
            mark.Material = i == 1 ? _additive : null;
            mark.ZIndex = WorldZ;
            mark.Visible = true;
            mark.Live = true;
            mark.QueueRedraw();
        }
    }

    private void StartAmbience()
    {
        StopAmbience();
        VfxAmbientRecipe[] recipes = ArenaStyle.Ambience;
        bool pale = ArenaCanvas == VfxCanvas.Light;
        for (int i = 0; i < recipes.Length && i < _ambient.Length; i++)
        {
            VfxAmbientRecipe r = recipes[i];
            VfxAmbient a = _ambient[i];
            int amount = Math.Max(1, Mathf.RoundToInt(r.Amount * FeedbackSettings.Particles));
            Texture2D texture = VfxTextures.Get(r.Texture);
            float unit = texture != null ? texture.GetWidth() : 1f;
            a.Amount = amount;
            a.Lifetime = r.Lifetime;
            a.OneShot = false;
            a.Explosiveness = 0f;
            a.Randomness = 1f;
            a.LocalCoords = false;
            a.Preprocess = r.Lifetime * 0.6f;                  // the air is already full when the fight opens
            a.Texture = texture;
            a.Material = r.Additive && !pale ? _additive : null;
            a.TextureFilter = ArenaStyle.Nearest ? TextureFilterEnum.Nearest : TextureFilterEnum.ParentNode;
            a.FixedFps = (int)r.Fps;
            a.Direction = r.Direction.Normalized();
            a.Spread = r.SpreadDeg;
            a.InitialVelocityMin = r.SpeedMin;
            a.InitialVelocityMax = r.SpeedMax;
            a.Gravity = r.Gravity;
            a.ScaleAmountMin = r.SizeMin * VfxTable.AmbientScale / unit;
            a.ScaleAmountMax = r.SizeMax * VfxTable.AmbientScale / unit;
            a.AngleMin = r.SpinDeg > 0f ? -180f : 0f;
            a.AngleMax = r.SpinDeg > 0f ? 180f : 0f;
            a.AngularVelocityMin = -r.SpinDeg;
            a.AngularVelocityMax = r.SpinDeg;
            a.SetParticleFlag(CpuParticles2D.ParticleFlags.AlignYToVelocity, r.AlignToVelocity);
            a.Tones.SetColor(0, pale ? r.ALight : r.A);
            a.Tones.SetColor(1, pale ? r.BLight : r.B);
            a.ColorInitialRamp = a.Tones;
            a.ColorRamp = r.Ramp;
            a.EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle;
            a.BottomBand = r.BottomBand;
            a.Particles = amount;
            a.Live = true;
            a.Visible = true;
            PlaceAmbient(a);
            a.Restart();
            a.Emitting = true;
            LiveAmbient++;
            LiveParticles += amount;
        }
    }

    private void StopAmbience()
    {
        for (int i = 0; i < _ambient.Length; i++)
        {
            VfxAmbient a = _ambient[i];
            if (a == null || !a.Live) continue;
            a.Live = false;
            a.Emitting = false;
            a.Visible = false;
            LiveParticles -= a.Particles;
            a.Particles = 0;
            LiveAmbient--;
        }
    }

    /// <summary>The emitter rides the view: its box is what the camera sees, plus a margin.</summary>
    private void PlaceAmbient(VfxAmbient a)
    {
        Rect2 view = ViewRect();
        Vector2 half = view.Size * 0.5f;
        Vector2 centre = view.GetCenter();
        if (a.BottomBand > 0f)
        {
            float bandHalf = half.Y * a.BottomBand;
            a.Position = new Vector2(centre.X, view.End.Y - bandHalf);
            a.EmissionRectExtents = new Vector2(half.X + 60f, bandHalf);
        }
        else
        {
            a.Position = centre;
            a.EmissionRectExtents = new Vector2(half.X + 80f, half.Y + 60f);
        }
    }

    // ── per frame ────────────────────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        long before = MeasureAllocation ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        Advance((float)delta);
        if (MeasureAllocation) AllocatedInUpdate += GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private void Advance(float dt)
    {
        if (Frozen) return;
        _now += dt;
        Intensity *= Mathf.Pow(0.5f, dt / VfxTable.IntensityHalfLife);
        if (Combo > 0 && _now - _lastComboHit > VfxTable.ComboWindow) Combo = 0;

        if (InArena)
        {
            // The arena that raised arena.enter left the tree — or, when it named none, the
            // scene changed under us. Either way its effects go with it.
            bool gone = _arenaHasSource
                ? !IsInstanceValid(_arena) || !_arena.IsInsideTree()
                : GetTree().CurrentScene != _arenaScene;
            if (gone) LeaveArena();
        }
        if (BossPhase >= 1 && _boss != null)
        {
            if (!IsInstanceValid(_boss)) _boss = null;
            else if (_boss is IFeedbackBody boss && (boss.FeedbackState & FeedbackState.LowHp) != 0) BossPhase = 2;
        }

        // A loop whose source left the tree is ended here (Feedback.gdd §2).
        for (int i = 0; i < _loops.Length; i++)
        {
            if (_loops[i].Id == 0 || !_loops[i].HasSource) continue;
            Node2D source = _loops[i].Source;
            if (!IsInstanceValid(source) || !source.IsInsideTree()) EndLoop(i);
        }

        for (int i = 0; i < _marks.Count; i++) if (_marks[i].Live) Step(_marks[i], dt);
        for (int i = 0; i < _bursts.Count; i++)
        {
            VfxBurst burst = _bursts[i];
            if (!burst.Live) continue;
            burst.Age += dt;
            if (burst.Age >= burst.Life) Recycle(burst);
        }
        for (int i = 0; i < _ambient.Length; i++) if (_ambient[i].Live) PlaceAmbient(_ambient[i]);
    }

    /// <summary>Advance one mark's clock; redraw it only when its medium's frame changes.</summary>
    private void Step(VfxMark m, float dt)
    {
        m.Age += dt;
        if (m.S.Kind == VfxMarkKind.Warm) m.Position = ViewRect().GetCenter();     // must be on screen to compile anything
        if (m.Follow != null)
        {
            if (IsInstanceValid(m.Follow) && m.Follow.IsInsideTree()) m.Position = m.Follow.GlobalPosition + m.FollowOffset;
            else
            {
                m.Follow = null;
                if (m.Looping && !m.Releasing) Release(m);
            }
        }
        if (m.Age < 0f) return;
        if (m.Releasing)
        {
            m.OutAge += dt;
            float seconds = m.Style.ReleaseSeconds;
            m.Out = seconds > 0f ? m.OutAge / seconds : 1f;
            if (m.Out >= 1f) { Recycle(m); return; }
        }
        else if (!m.Looping && m.Age >= m.S.Life) { Recycle(m); return; }
        Clock(m);
    }

    private static void Clock(VfxMark m)
    {
        float fps = m.Style.Fps;
        int step = fps > 0f ? (int)(m.Age * fps) : -1;
        if (fps > 0f && step == m.Step && !m.Releasing) return;
        m.Step = step;
        // A stepped medium shows the middle of each held frame: its first frame is already
        // a third written, never an empty one.
        m.Time = fps > 0f ? (step + 0.5f) / fps : m.Age;
        m.T = m.Looping ? Mathf.PosMod(m.Time, m.S.Life) / m.S.Life : VfxPen.Sat(m.Time / m.S.Life);
        m.Boil = m.Style.Boils ? VfxPen.Hash(m.S.Seed + (uint)step * 2654435761u) : m.S.Seed;
        m.QueueRedraw();
    }

    // ── loops ────────────────────────────────────────────────────────────────────
    private int FindLoop(Node2D source, string owner, string action)
    {
        for (int i = 0; i < _loops.Length; i++)
        {
            if (_loops[i].Id == 0) continue;
            if (ReferenceEquals(_loops[i].Source, source) && _loops[i].Owner == owner && _loops[i].Action == action) return i;
        }
        return -1;
    }

    private int FreeLoopSlot()
    {
        for (int i = 0; i < _loops.Length; i++) if (_loops[i].Id == 0) return i;
        return -1;
    }

    private void EndLoop(int slot)
    {
        int id = _loops[slot].Id;
        _loops[slot] = default;
        LiveLoops--;
        for (int i = 0; i < _marks.Count; i++)
        {
            VfxMark m = _marks[i];
            if (m.Live && m.LoopId == id && !m.Releasing) Release(m);
        }
        for (int i = 0; i < _bursts.Count; i++)
            if (_bursts[i].Live && _bursts[i].LoopId == id) Recycle(_bursts[i]);
    }

    /// <summary>A loop's mark leaves: at once if it never drew or its medium has no fade.</summary>
    private void Release(VfxMark m)
    {
        if (!m.Looping || m.Age <= 0f || m.Style.ReleaseSeconds <= 0f) { Recycle(m); return; }
        m.Releasing = true;
        m.OutAge = 0f;
        m.Out = 0f;
    }

    // ── pools ────────────────────────────────────────────────────────────────────
    private VfxMark RentMark(VfxPriority priority)
    {
        if (!Unlimited)
        {
            if (priority == VfxPriority.Cosmetic && LiveMarks >= VfxBudget.CosmeticMarks) return null;
            if (priority == VfxPriority.Confirm && LiveMarks >= VfxBudget.ConfirmMarks) return null;
        }
        VfxMark mark;
        if (_freeMarks.Count > 0)
        {
            mark = _freeMarks[_freeMarks.Count - 1];
            _freeMarks.RemoveAt(_freeMarks.Count - 1);
        }
        else if (Unlimited) mark = NewMark();
        else if (priority == VfxPriority.Critical)
        {
            mark = StealMark();
            if (mark == null) return null;
        }
        else return null;
        mark.Uses++;
        mark.Serial = ++_serial;
        LiveMarks++;
        if (LiveMarks > PeakMarks) PeakMarks = LiveMarks;
        return mark;
    }

    /// <summary>The pool is full and a Critical effect needs a mark: take the oldest one
    /// that is not Critical.</summary>
    private VfxMark StealMark()
    {
        VfxMark oldest = null;
        for (int i = 0; i < _marks.Count; i++)
        {
            VfxMark m = _marks[i];
            if (!m.Live || m.Priority == VfxPriority.Critical) continue;
            if (oldest == null || m.Age > oldest.Age) oldest = m;
        }
        if (oldest == null) return null;
        Recycle(oldest);
        _freeMarks.RemoveAt(_freeMarks.Count - 1);
        Stolen++;
        return oldest;
    }

    private void Recycle(VfxMark m)
    {
        if (!m.Live) return;
        m.ResetForPool();
        m.QueueRedraw();                                      // drop its cached drawing
        _freeMarks.Add(m);
        LiveMarks--;
    }

    private VfxBurst RentBurst(VfxPriority priority, int particles)
    {
        if (!Unlimited)
        {
            if (priority == VfxPriority.Cosmetic && (LiveBursts >= VfxBudget.CosmeticBursts || LiveParticles + particles > VfxBudget.CosmeticParticles)) return null;
            if (priority == VfxPriority.Confirm && (LiveBursts >= VfxBudget.ConfirmBursts || LiveParticles + particles > VfxBudget.ConfirmParticles)) return null;
            if (LiveParticles + particles > VfxBudget.Particles) return null;
        }
        VfxBurst burst;
        if (_freeBursts.Count > 0)
        {
            burst = _freeBursts[_freeBursts.Count - 1];
            _freeBursts.RemoveAt(_freeBursts.Count - 1);
        }
        else if (Unlimited) burst = NewBurst();
        else if (priority == VfxPriority.Critical)
        {
            burst = null;
            for (int i = 0; i < _bursts.Count; i++)
            {
                VfxBurst b = _bursts[i];
                if (!b.Live || b.Priority == VfxPriority.Critical) continue;
                if (burst == null || b.Age > burst.Age) burst = b;
            }
            if (burst == null) return null;
            Recycle(burst);
            _freeBursts.RemoveAt(_freeBursts.Count - 1);
            Stolen++;
        }
        else return null;
        burst.Uses++;
        burst.Serial = ++_serial;
        LiveBursts++;
        if (LiveBursts > PeakBursts) PeakBursts = LiveBursts;
        return burst;
    }

    private void Recycle(VfxBurst b)
    {
        if (!b.Live) return;
        LiveParticles -= b.Particles;
        b.ResetForPool();
        _freeBursts.Add(b);
        LiveBursts--;
    }

    // ── playing a row ────────────────────────────────────────────────────────────
    /// <summary>What a row is played with: a cue or a hit, flattened to one shape.</summary>
    private readonly struct VfxInput
    {
        public readonly string Owner;            // whose medium (for a hit: the attack's)
        public readonly Node2D Source;           // the body the effect belongs to
        public readonly Vector2 Position;
        public readonly Vector2 Direction;
        public readonly float Size;
        public readonly Vector2 Extents;
        public readonly float Magnitude;
        public readonly int Variant;
        public readonly string Tag;
        public readonly CombatSide Side;
        public readonly bool IsHit;
        public readonly HitWeight Weight;
        public readonly bool Killed;
        public readonly string VictimOwner;

        public VfxInput(in Cue cue)
        {
            Owner = cue.Owner;
            Source = cue.Source;
            Position = cue.Position;
            Direction = cue.Direction;
            Size = cue.Size;
            Extents = cue.Extents;
            Magnitude = cue.Magnitude;
            Variant = cue.Variant;
            Tag = cue.Tag;
            Side = cue.Side;
            IsHit = false;
            Weight = HitWeight.Auto;
            Killed = false;
            VictimOwner = null;
        }

        /// <summary>A hit. Its impact belongs to the attack (the attacker's medium); its
        /// reaction to the victim.</summary>
        public VfxInput(in HitCue hit, bool reaction)
        {
            // An unnamed attack — a kit not wired yet — still has an attacker somewhere: the
            // body that swung if the hit carried it, else the player when a foe was hit.
            string attacker = hit.AttackOwner != CueOwners.Any ? hit.AttackOwner
                : hit.Attacker is IFeedbackBody body ? body.FeedbackId
                : !hit.VictimIsPlayer ? CharacterController.LocalPlayer?.FeedbackId
                : null;
            float radius = hit.VictimBody?.FeedbackRadius ?? 20f;
            Owner = reaction ? hit.VictimOwner : attacker;
            Source = reaction ? hit.Victim : hit.Attacker;
            // The contact point: the near side of the victim, not its centre.
            Position = reaction ? hit.Position : hit.Position - hit.Direction * (radius * 0.55f);
            Direction = hit.Direction;
            Size = radius;
            Extents = Vector2.Zero;
            Magnitude = hit.Damage;
            Variant = hit.AttackVariant;
            Tag = null;
            Side = hit.VictimIsPlayer ? CombatSide.Player : CombatSide.Foe;
            IsHit = true;
            Weight = hit.Weight;
            Killed = hit.Killed;
            VictimOwner = hit.VictimOwner;
        }
    }

    private void Play(VfxDef def, in VfxInput input, int loopId)
    {
        if (def == null || def.Archetype == VfxArchetype.Ambient) return;
        if (input.Magnitude < def.MinMagnitude) return;
        Node2D source = input.Source != null && IsInstanceValid(input.Source) && input.Source.IsInsideTree() ? input.Source : null;
        if (loopId == 0 && !CooldownOk(def, source)) return;

        var x = new VfxCtx { Def = def, LoopId = loopId, CountMul = def.Count, ScaleMul = 1f, LifeMul = def.Life, SpeedMul = 1f };
        var body = source as IFeedbackBody;
        FeedbackState state = body?.FeedbackState ?? FeedbackState.None;
        x.BodyRadius = input.IsHit ? input.Size : body?.FeedbackRadius ?? 20f;     // a hit is sized by its victim
        x.Canvas = ArenaCanvas;
        x.Flags = def.Flags;
        x.Priority = def.Priority;
        if (input.Killed) x.Flags |= VfxFlags.Killed;
        bool playerSide = input.Side == CombatSide.Player;
        if (playerSide && body != null && ReferenceEquals(source, CharacterController.LocalPlayer)) x.Flags |= VfxFlags.Player;
        if (def.CriticalForPlayer && playerSide) x.Priority = VfxPriority.Critical;

        // Whose medium, and whose colours.
        VfxLook look = def.StyleFrom == VfxStyleFrom.Arena ? null : VfxTable.LookOf(input.Owner);
        VfxStyle style = VfxTable.Style(ForceStyle) ?? VfxTable.Style(def.Style) ?? VfxTable.Style(look?.Style) ?? ArenaStyle;
        x.Style = style;
        VfxElement element = def.Element;
        bool ownColours = def.OwnerColours && look != null && look.HasColours && look.Style == style.Id;

        // State and pace: every modifier whose conditions all hold.
        VfxDef extra = null;
        VfxModifier[] modifiers = def.Modifiers;
        if (modifiers != null)
        {
            for (int i = 0; i < modifiers.Length; i++)
            {
                VfxModifier mod = modifiers[i];
                if (mod.State != FeedbackState.None && (state & mod.State) == 0) continue;
                if (mod.Tag != null && mod.Tag != input.Tag) continue;
                if (mod.MinCombo > 0 && Combo < mod.MinCombo) continue;
                if (mod.MinIntensity > 0f && Intensity < mod.MinIntensity) continue;
                if (mod.MinBossPhase > 0 && BossPhase < mod.MinBossPhase) continue;
                if (mod.Killed && !input.Killed) continue;
                if (mod.MinWeight != HitWeight.Auto && input.Weight < mod.MinWeight) continue;
                if (mod.Stage > 0 && input.Variant != mod.Stage) continue;
                x.CountMul *= mod.Count;
                x.ScaleMul *= mod.Scale;
                x.LifeMul *= mod.Life;
                x.SpeedMul *= mod.Speed;
                x.Flags |= mod.Flags;
                if (mod.Element.HasValue) { element = mod.Element.Value; ownColours = false; }
                if (mod.Add != null) extra = mod.Add;
            }
        }
        x.P = ownColours ? style.Look(look, x.Canvas) : style.Element(element, x.Canvas);

        // Size: the row's rule applied to the cue's own numbers.
        x.Power = input.IsHit ? VfxTable.Power(input.Weight) : 0.5f;
        x.Extents = input.Extents * def.Scale;
        switch (def.SizeFrom)
        {
            case VfxSizeFrom.CueSize: x.Size = input.Size * def.Scale; break;
            case VfxSizeFrom.CueExtents: x.Size = Mathf.Max(input.Extents.X, input.Extents.Y) * def.Scale; break;
            case VfxSizeFrom.BodyRadius: x.Size = x.BodyRadius * def.Scale; break;
            case VfxSizeFrom.Weight:
                x.Size = VfxTable.WeightSize[Mathf.Clamp((int)input.Weight, 1, 5)] * def.Scale * Mathf.Clamp(x.BodyRadius / 22f, 0.85f, 1.6f);
                break;
            case VfxSizeFrom.Magnitude:
                x.Power = Mathf.Clamp((input.Magnitude - def.MagLo) / (def.MagHi - def.MagLo), 0f, 1f);
                x.Size = Mathf.Lerp(def.SizeMin, def.SizeMax, x.Power);
                break;
            default: x.Size = def.Size; break;
        }
        if (!input.IsHit && def.SizeFrom != VfxSizeFrom.Magnitude && def.MagHi > def.MagLo && def.MagLo > 0f)
            x.Power = Mathf.Clamp((input.Magnitude - def.MagLo) / (def.MagHi - def.MagLo), 0f, 1f);
        if (x.Size <= 0f) x.Size = def.Size;

        // The fight's own pace: a combo escalates, a crowd thins decoration.
        if (input.IsHit && input.Side != CombatSide.Player && Combo > 0)
        {
            int steps = Math.Min(3, Combo / VfxTable.ComboStep);
            x.ScaleMul *= 1f + VfxTable.ComboScalePerStep * steps;
            x.CountMul *= 1f + VfxTable.ComboCountPerStep * steps;
        }
        if (x.Priority != VfxPriority.Critical)
        {
            x.CountMul *= FeedbackSettings.Particles;         // decoration is what the setting scales
            if (Intensity >= VfxTable.BusyIntensity) { x.CountMul *= VfxTable.BusyCount; x.LifeMul *= VfxTable.BusyLife; }
        }

        // Never the same effect twice: a shape from the bag, and jitter inside the row's ranges.
        uint seed = NextRandom();
        x.Seed = seed;
        x.Variant = NextVariant(def);
        x.Stage = def.Stage >= 0 ? def.Stage : input.Variant;
        x.Size *= x.ScaleMul * (1f + VfxPen.Rs(seed, 1) * def.ScaleJitter);
        x.LifeMul *= 1f + VfxPen.Rs(seed, 2) * def.TimeJitter;
        x.Dir = input.Direction;
        if (def.RotJitterDeg > 0f && x.Dir.LengthSquared() > 0.01f)
            x.Dir = x.Dir.Rotated(Mathf.DegToRad(VfxPen.Rs(seed, 3) * def.RotJitterDeg));
        if (def.HueJitter > 0f) x.P.Main = x.P.Main.Lerp(x.P.Alt, VfxPen.R(seed, 4) * def.HueJitter);
        if (def.FlourishOneIn > 0 && seed % (uint)def.FlourishOneIn == 0u) x.Flags |= VfxFlags.Flourish;
        if (input.Tag == "burning") x.Flags |= VfxFlags.Burning;

        x.Arc = input.IsHit ? 0f : CueShapes.ConeHalfAngle(input.Size, input.Extents);
        x.Magnitude = input.Magnitude;
        x.Pos = input.Position + new Vector2(0f, def.Anchor * x.BodyRadius);
        if (def.Follow && source != null) x.Follow = source;

        if (!Admit(ref x)) { Dropped[(int)x.Priority]++; return; }
        Started[(int)x.Priority]++;

        Texture2D sheet = VfxTextures.Sheet(style.Id, def.Id);
        if (sheet != null) PlaySheet(ref x, sheet);
        else style.Play(def.Archetype, ref x, this);

        VfxDef[] layers = def.Layers;
        if (layers != null) for (int i = 0; i < layers.Length; i++) Play(layers[i], in input, loopId);
        if (extra != null) Play(extra, in input, 0);
    }

    /// <summary>May this effect start now? Critical always may. The rest wait their turn:
    /// a few per frame, and nothing off screen.</summary>
    private bool Admit(ref VfxCtx x)
    {
        if (Unlimited || x.Priority == VfxPriority.Critical) return true;
        ulong frame = Engine.GetProcessFrames();
        if (frame != _spawnFrame) { _spawnFrame = frame; _spawnsThisFrame = 0; }
        if (_spawnsThisFrame >= VfxBudget.SpawnsPerFrame) return false;
        if (CullOffscreen && !ViewRect().Grow(VfxBudget.OffscreenMarginPx + x.Size).HasPoint(x.Pos)) return false;
        _spawnsThisFrame++;
        return true;
    }

    /// <summary>What the camera sees, in world px — worked out once a frame.</summary>
    public Rect2 ViewRect()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame == _viewFrame) return _view;
        _viewFrame = frame;
        Viewport viewport = GetViewport();
        if (viewport == null) return _view = new Rect2(0f, 0f, 1152f, 648f);
        Transform2D toWorld = viewport.GetCanvasTransform().AffineInverse();
        Vector2 size = viewport.GetVisibleRect().Size;
        Vector2 a = toWorld * Vector2.Zero, b = toWorld * size;
        return _view = new Rect2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y));
    }

    private bool CooldownOk(VfxDef def, Node2D source)
    {
        if (def.Cooldown <= 0f) return true;
        ulong id = source != null ? source.GetInstanceId() : 0ul;
        ulong[] sources = def.CooldownSource;
        double[] until = def.CooldownUntil;
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] != id || until[i] == 0.0) continue;
            if (_now < until[i]) return false;
            until[i] = _now + def.Cooldown;
            return true;
        }
        int slot = def.CooldownNext;
        def.CooldownNext = (slot + 1) % sources.Length;
        sources[slot] = id;
        until[slot] = _now + def.Cooldown;
        return true;
    }

    private uint NextRandom()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return _rng;
    }

    private static void FillBag(VfxDef def)
    {
        if (def.Bag != null || def.Variants <= 1) return;
        def.Bag = new byte[def.Variants];
        def.BagLeft = 0;
    }

    /// <summary>A shuffle-bag: every variant once before any repeats, and never the same
    /// one twice running across a refill.</summary>
    private int NextVariant(VfxDef def)
    {
        if (def.Variants <= 1 || def.Bag == null) return 0;
        byte[] bag = def.Bag;
        if (def.BagLeft == 0)
        {
            for (int i = 0; i < bag.Length; i++) bag[i] = (byte)i;
            for (int i = bag.Length - 1; i > 0; i--)
            {
                int j = (int)(NextRandom() % (uint)(i + 1));
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            if (bag[bag.Length - 1] == def.LastVariant) (bag[0], bag[bag.Length - 1]) = (bag[bag.Length - 1], bag[0]);
            def.BagLeft = bag.Length;
        }
        int variant = bag[--def.BagLeft];
        def.LastVariant = variant;
        return variant;
    }

    // ── pieces: what a style composes an effect from ─────────────────────────────
    /// <summary>Rent one drawn mark. Null when the budget refuses it — the composition
    /// simply has one piece fewer.</summary>
    public VfxMark Mark(ref VfxCtx x, VfxMarkKind kind, Vector2 pos, float rot, float size, float life, float delay = 0f)
    {
        VfxMark m = RentMark(x.Priority);
        if (m == null) return null;
        VfxStyle style = x.Style;
        m.Style = style;
        m.S = new VfxMarkState
        {
            Kind = kind, P = x.P, Size = size, Life = Mathf.Max(0.04f, life * style.Tempo * x.LifeMul),
            Seed = x.Seed + (uint)m.Uses * 977u + (uint)kind * 131u, Variant = x.Variant, Stage = x.Stage, Flags = x.Flags,
        };
        m.Canvas = x.Canvas;
        m.Priority = x.Priority;
        m.LoopId = x.LoopId;
        m.Looping = x.LoopId != 0 && kind is VfxMarkKind.Aura or VfxMarkKind.Zone or VfxMarkKind.Field;
        m.Age = -delay;
        m.Position = VfxPen.Snap(pos, style.Grid);
        if (style.KeepsGrid) m.S.Rot = rot;
        else m.Rotation = rot;
        if (x.Follow != null)
        {
            m.Follow = x.Follow;
            m.FollowOffset = m.Position - x.Follow.GlobalPosition;
        }
        m.ZIndex = x.Priority == VfxPriority.Critical ? MustReadZ : WorldZ;
        m.Material = style.Additive(kind, x.Canvas) ? _additive : null;
        m.TextureFilter = style.Nearest ? TextureFilterEnum.Nearest : TextureFilterEnum.ParentNode;
        m.Visible = true;
        m.Live = true;
        if (delay <= 0f) Clock(m);
        return m;
    }

    /// <summary>Throw small pieces: a pooled emitter where the medium moves continuously, a
    /// drawn, stepped mark where it is stamped.</summary>
    public void Bits(ref VfxCtx x, VfxBits kind, Vector2 pos, Vector2 dir, int count, float speed, float spreadDeg, float size, float life)
    {
        int n = Mathf.RoundToInt(count * x.CountMul);
        if (n <= 0) return;
        VfxStyle style = x.Style;
        speed *= x.SpeedMul;
        VfxBurstRecipe recipe = style.Particles(kind);
        if (recipe == null)
        {
            VfxMark m = Mark(ref x, VfxMarkKind.Bits, pos, 0f, size, life);
            if (m == null) return;
            m.S.Count = Math.Min(n, style.MaxPieces);
            m.S.Speed = speed;
            m.S.Spread = Mathf.DegToRad(spreadDeg);
            m.S.Dir = dir;
            m.S.Gravity = kind switch { VfxBits.Spray => 900f, VfxBits.Fall => 720f, VfxBits.Rise => -170f, _ => 50f };
            return;
        }

        n = Math.Min(n, VfxBudget.ParticlesPerBurst);
        VfxBurst b = RentBurst(x.Priority, n);
        if (b == null) return;
        Texture2D texture = recipe.TextureResource;
        float unit = texture != null ? texture.GetWidth() : 1f;
        float seconds = Mathf.Max(0.08f, life * recipe.Lifetime * style.Tempo * x.LifeMul);
        b.Amount = n;
        b.Lifetime = seconds;
        b.OneShot = true;
        b.Explosiveness = 0.92f;
        b.Randomness = 0f;
        b.Direction = dir.LengthSquared() > 0.01f ? dir : Vector2.Right;
        b.Spread = dir.LengthSquared() > 0.01f ? spreadDeg * 0.5f : 180f;
        b.InitialVelocityMin = speed * recipe.SpeedMin;
        b.InitialVelocityMax = speed * recipe.SpeedMax;
        b.Gravity = recipe.Gravity;
        b.DampingMin = recipe.Damping;
        b.DampingMax = recipe.Damping;
        b.ScaleAmountMin = size * recipe.SizeMin / unit;
        b.ScaleAmountMax = size * recipe.SizeMax / unit;
        b.ScaleAmountCurve = recipe.Scale;
        b.ColorRamp = recipe.Ramp;
        if (recipe.TwoTone)
        {
            b.Tones.SetColor(0, x.P.Main);
            b.Tones.SetColor(1, x.P.Alt);
            b.ColorInitialRamp = b.Tones;
            b.Color = Colors.White;
        }
        else
        {
            b.ColorInitialRamp = null;
            b.Color = recipe.Colour switch { 1 => x.P.Alt, 2 => x.P.Accent, 3 => x.P.Line, -1 => Colors.White, _ => x.P.Main };
        }
        b.Texture = texture;
        b.Material = recipe.Additive && x.Canvas != VfxCanvas.Light ? _additive : null;
        b.TextureFilter = style.Nearest ? TextureFilterEnum.Nearest : TextureFilterEnum.ParentNode;
        b.SetParticleFlag(CpuParticles2D.ParticleFlags.AlignYToVelocity, recipe.AlignToVelocity);
        b.AngleMin = recipe.SpinDeg > 0f ? -180f : 0f;
        b.AngleMax = recipe.SpinDeg > 0f ? 180f : 0f;
        b.AngularVelocityMin = -recipe.SpinDeg;
        b.AngularVelocityMax = recipe.SpinDeg;
        b.FixedFps = (int)recipe.Fps;
        b.EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere;
        b.EmissionSphereRadius = recipe.EmitRadius;
        b.Position = pos;
        b.ZIndex = x.Priority == VfxPriority.Critical ? MustReadZ : WorldZ;
        b.Priority = x.Priority;
        b.LoopId = 0;
        b.Age = 0f;
        b.Life = seconds + 0.06f;
        b.Particles = n;
        b.Live = true;
        b.Visible = true;
        b.Restart();
        b.Emitting = true;
        LiveParticles += n;
        if (LiveParticles > PeakParticles) PeakParticles = LiveParticles;
    }

    /// <summary>An authored sheet in place of the procedural effect: same size rule, same
    /// place, the medium's own frame rate.</summary>
    private void PlaySheet(ref VfxCtx x, Texture2D sheet)
    {
        int height = Math.Max(1, sheet.GetHeight());
        int frames = Math.Max(1, sheet.GetWidth() / height);
        float fps = x.Style.Fps > 0f ? Mathf.Min(x.Style.Fps, 24f) : 24f;
        VfxMark m = Mark(ref x, VfxMarkKind.Sheet, x.Pos, x.Dir.LengthSquared() > 0.01f ? x.Dir.Angle() : 0f, x.Size, frames / fps / x.Style.Tempo);
        if (m == null) return;
        m.S.Sheet = sheet;
        m.S.Frames = frames;
    }

    // ── for tools and checks ─────────────────────────────────────────────────────
    /// <summary>Play one archetype in one medium at a place — the gallery's grid. Bypasses
    /// the table and the budget; never used by the game.</summary>
    public void Preview(VfxArchetype archetype, string styleId, Vector2 pos, Vector2 dir, float size, VfxElement element,
                        VfxCanvas canvas, VfxLook look = null, int stage = 1, VfxFlags flags = VfxFlags.None,
                        float power = 0.5f, Vector2 extents = default, float arc = 0f, float magnitude = 0f, uint seed = 0u,
                        float hold = -1f)
    {
        VfxStyle style = VfxTable.Style(styleId);
        if (style == null) return;
        bool unlimited = Unlimited;
        Unlimited = true;
        var x = new VfxCtx
        {
            Style = style, Pos = pos, Dir = dir, Size = size, Extents = extents, Arc = arc, Magnitude = magnitude,
            Stage = stage, Power = power, CountMul = 1f, ScaleMul = 1f, LifeMul = 1f, SpeedMul = 1f, Flags = flags,
            Priority = VfxPriority.Confirm, Seed = seed != 0u ? seed : NextRandom(), Canvas = canvas, BodyRadius = 22f,
            LoopId = archetype is VfxArchetype.StatusLoop or VfxArchetype.Telegraph or VfxArchetype.FieldLoop ? -1 : 0,
        };
        x.P = look != null && look.HasColours ? style.Look(look, canvas) : style.Element(element, canvas);
        long from = _serial;
        style.Play(archetype, ref x, this);
        Unlimited = unlimited;
        if (hold < 0f) return;
        // Held on one moment of its life — a still for the gallery (use with SetFrozen).
        for (int i = 0; i < _marks.Count; i++)
        {
            VfxMark m = _marks[i];
            if (!m.Live || m.Serial <= from) continue;
            m.Age = hold * m.S.Life;
            m.Step = int.MinValue;
            Clock(m);
        }
        for (int i = 0; i < _bursts.Count; i++)
        {
            VfxBurst b = _bursts[i];
            if (!b.Live || b.Serial <= from) continue;
            b.Preprocess = hold * b.Lifetime;
            b.Restart();
            b.Emitting = true;
            b.Life = float.MaxValue;
            b.SpeedScale = 0.0;
        }
    }

    /// <summary>Start a realm's ambience without an arena — the gallery.</summary>
    public void PreviewArena(string world, float lightness)
    {
        LeaveArena();
        ArenaWorld = world ?? "";
        ArenaStyle = VfxTable.ArenaStyle(ArenaWorld);
        ArenaCanvas = CanvasFor(lightness);
        _arenaScene = GetTree().CurrentScene;
        InArena = true;
        StartAmbience();
    }

    public void SetFrozen(bool frozen)
    {
        Frozen = frozen;
        for (int i = 0; i < _bursts.Count; i++) if (_bursts[i].Live) _bursts[i].SpeedScale = frozen ? 0.0 : 1.0;
        for (int i = 0; i < _ambient.Length; i++) if (_ambient[i].Live) _ambient[i].SpeedScale = frozen ? 0.0 : 1.0;
    }

    public void Seed(uint seed) => _rng = seed | 1u;

    /// <summary>The serial of the last piece rented: a piece with a greater one is newer.</summary>
    public long Serial => _serial;
    public int MarkCount => _marks.Count;
    public int BurstCount => _bursts.Count;
    public VfxMark MarkAt(int i) => _marks[i];
    public VfxBurst BurstAt(int i) => _bursts[i];
    public VfxAmbient AmbientAt(int i) => _ambient[i];
    public int FreeMarks => _freeMarks.Count;
    public int FreeBursts => _freeBursts.Count;

    /// <summary>Shrink pools that <see cref="Unlimited"/> let grow back to the budget.</summary>
    public void TrimPools()
    {
        ClearAll();
        while (_marks.Count > VfxBudget.Marks)
        {
            VfxMark extra = _marks[_marks.Count - 1];
            _marks.RemoveAt(_marks.Count - 1);
            _freeMarks.Remove(extra);
            extra.QueueFree();
        }
        while (_bursts.Count > VfxBudget.Bursts)
        {
            VfxBurst extra = _bursts[_bursts.Count - 1];
            _bursts.RemoveAt(_bursts.Count - 1);
            _freeBursts.Remove(extra);
            extra.QueueFree();
        }
    }

    public void ResetCounters()
    {
        for (int i = 0; i < 3; i++) { Started[i] = 0; Dropped[i] = 0; }
        Stolen = 0;
        PeakMarks = LiveMarks;
        PeakBursts = LiveBursts;
        PeakParticles = LiveParticles;
        AllocatedInUpdate = 0;
    }
}
