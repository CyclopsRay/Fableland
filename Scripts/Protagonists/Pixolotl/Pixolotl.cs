using Godot;
using System.Collections.Generic;
using Fableland.Data;
using Fableland.Run;

/// <summary>
/// Pixolotl — temporal lower-ground controller. Her two held skills are mutually
/// exclusive: E accelerates only her local simulation; Shift consumes the visible
/// frame route while every owned bubble rewinds by the same elapsed local time.
/// </summary>
public partial class Pixolotl : CharacterController
{
    private enum TemporalState { Normal, EHeld, ShiftRewind, ShiftRecovery }

    private sealed class PapelPicadoFrame
    {
        public Vector2 Position;
        public float Hp;
        public Vector2 IntentVelocity;
        public Vector2 ExternalVelocity;
        public PapelPicadoGhost Ghost;
    }

    [Export] public PackedScene BubbleScene;

    private readonly List<PapelPicadoFrame> _frames = new(); // oldest → newest
    private readonly List<PapelPicadoGhost> _ghostPool = new();
    private readonly List<PixolotlBubble> _bubblePool = new();
    private TemporalState _state;
    private float _frameTimer;
    private float _shiftCd;
    private float _eCd;
    private float _shiftStepTimer;
    private int _shiftJumps;
    private PapelPicadoFrame _arrivalFrame;
    private float _recoveryTimer;
    private float _eRealTimer;
    private int _eFramesGenerated;

    public override float LocalTimeRate => _state == TemporalState.EHeld
        ? CharacterTable.Pixolotl.ELocalTimeRate
        : LocalTime.DefaultRate;

    public override (float Remaining, float Max) ShiftCooldown => (_shiftCd, CharacterTable.Pixolotl.ShiftCooldown);
    public override (float Remaining, float Max) ESkillCooldown => (_eCd, CharacterTable.Pixolotl.ESkillCooldown);

    protected override void InitCharacter()
    {
        PixolotlDef def = CharacterTable.Pixolotl;
        SetBaseMaxHP(def.BaseHp);
        MoveSpeed = Units.Px(def.MoveSpeedMps);
        MaxJumps = def.MaxJumps;
        ConfigureAmmo("Pixolotl");
        RegisterExistingBubbles();
        WarmBubblePool();
    }

    public override void SaveCooldownsToState(ProtagonistState p)
    {
        if (p == null) return;
        p.ShiftCdRemaining = _shiftCd;
        p.ESkillCdRemaining = _eCd;
    }

    public override void LoadCooldownsFromState(ProtagonistState p)
    {
        if (p == null) return;
        _shiftCd = p.ShiftCdRemaining;
        _eCd = p.ESkillCdRemaining;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        float realDt = (float)delta;
        float actDt = GetActDelta(realDt);

        if (_shiftCd > 0f) _shiftCd = Mathf.Max(0f, _shiftCd - actDt);
        if (_eCd > 0f) _eCd = Mathf.Max(0f, _eCd - actDt);
        AdvanceGhosts(actDt);

        switch (_state)
        {
            case TemporalState.Normal:
                AdvanceFrames(actDt);
                break;
            case TemporalState.EHeld:
                _eRealTimer += realDt;
                AdvanceFrames(actDt);
                if (Input.IsActionJustReleased("skill2") || _eFramesGenerated >= CharacterTable.Pixolotl.EFrameLimit
                    || _eRealTimer >= CharacterTable.Pixolotl.EMaxRealSeconds)
                    EndE();
                break;
            case TemporalState.ShiftRewind:
                _shiftStepTimer -= actDt;
                if (_shiftStepTimer <= 0f) RewindOneFrame();
                if (Input.IsActionJustReleased("skill1") && _shiftJumps > 0) ResolveShift();
                else if (Input.IsActionJustReleased("skill1")) CancelShift();
                break;
            case TemporalState.ShiftRecovery:
                _recoveryTimer -= actDt;
                if (_recoveryTimer <= 0f)
                {
                    ControlsLocked = false;
                    _state = TemporalState.Normal;
                }
                break;
        }
    }

    protected override void HandleBA()
    {
        if (_state is TemporalState.ShiftRewind or TemporalState.ShiftRecovery) return;
        if (!TryConsumeAmmo()) return;

        Vector2 origin = FirePoint != null ? FirePoint.GlobalPosition : GlobalPosition;
        Vector2 inheritedVelocity = LocalMotionVelocity;
        float speed = Units.Px(CharacterTable.Pixolotl.BubbleSpeedMps);
        foreach (float degrees in CharacterTable.Pixolotl.BubbleAnglesDeg)
        {
            float radians = Mathf.DegToRad(degrees);
            Vector2 direction = new(Mathf.Cos(radians), -Mathf.Sin(radians));
            PixolotlBubble bubble = RentBubble();
            if (bubble == null) continue;
            bubble.Activate(this, origin, direction * speed + inheritedVelocity, DamageDealtMultiplier);
        }

        StartAmmoAttackInterval();
        if (Ammo != null && Ammo.Current == 0) RequestAmmoReload();
    }

    protected override void HandleSkill1()
    {
        if (_state != TemporalState.Normal || _shiftCd > 0f || _frames.Count == 0) return;
        _state = TemporalState.ShiftRewind;
        ControlsLocked = true;
        Invincible = true;
        _shiftStepTimer = CharacterTable.Pixolotl.ShiftFirstIntervals;
        _shiftJumps = 0;
        _arrivalFrame = null;
    }

    protected override void HandleSkill2()
    {
        if (_state != TemporalState.Normal || _eCd > 0f) return;
        _state = TemporalState.EHeld;
        _eCd = CharacterTable.Pixolotl.ESkillCooldown;
        _eRealTimer = 0f;
        _eFramesGenerated = 0;
    }

    protected override void HandleSkillUlt()
    {
        // Deliberately deferred by Pixolotl.gdd v1.1.
    }

    private void AdvanceFrames(float actDt)
    {
        _frameTimer += actDt;
        while (_frameTimer >= CharacterTable.Pixolotl.FrameInterval)
        {
            _frameTimer -= CharacterTable.Pixolotl.FrameInterval;
            StoreFrame();
            if (_state == TemporalState.EHeld)
            {
                _eFramesGenerated++;
                if (_eFramesGenerated >= CharacterTable.Pixolotl.EFrameLimit) break;
            }
        }
    }

    private void StoreFrame()
    {
        if (_frames.Count >= CharacterTable.Pixolotl.FrameCapacity)
        {
            PapelPicadoFrame oldest = _frames[0];
            oldest.Ghost?.ForceFade(CharacterTable.Pixolotl.GhostForceFadeSeconds);
            _frames.RemoveAt(0);
        }

        (Vector2 intent, Vector2 external) = CaptureMovementVelocityState();
        var frame = new PapelPicadoFrame
        {
            Position = GlobalPosition,
            Hp = CurrentHP,
            IntentVelocity = intent,
            ExternalVelocity = external,
            Ghost = RentGhost(),
        };
        frame.Ghost.Activate(frame.Position, CharacterTable.Pixolotl.GhostFadeSeconds);
        _frames.Add(frame);
    }

    private void RewindOneFrame()
    {
        if (_frames.Count == 0)
        {
            ResolveShift();
            return;
        }

        PapelPicadoFrame frame = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);
        frame.Ghost?.ForceFade(CharacterTable.Pixolotl.GhostForceFadeSeconds);
        GlobalPosition = ResolveSafeFramePosition(frame.Position);
        RestoreMovementVelocityState(frame.IntentVelocity, frame.ExternalVelocity);
        _arrivalFrame = frame;
        _shiftJumps++;

        float rewindSeconds = _shiftJumps == 1 ? _frameTimer : CharacterTable.Pixolotl.FrameInterval;
        RewindOwnedBubbles(rewindSeconds);

        if (_shiftCd <= 0f) _shiftCd = CharacterTable.Pixolotl.ShiftCooldown;
        if (_frames.Count == 0)
        {
            ResolveShift();
            return;
        }

        _shiftStepTimer += _shiftJumps < CharacterTable.Pixolotl.ShiftSlowJumps
            ? CharacterTable.Pixolotl.ShiftFirstIntervals
            : CharacterTable.Pixolotl.ShiftLaterIntervals;
    }

    private void ResolveShift()
    {
        if (_state != TemporalState.ShiftRewind || _arrivalFrame == null) { CancelShift(); return; }
        Invincible = false;
        Heal(0.5f * Mathf.Max(0f, _arrivalFrame.Hp - CurrentHP));
        ApplyArrivalEffect();
        _frameTimer = 0f;
        _recoveryTimer = CharacterTable.Pixolotl.ShiftRecovery;
        _state = TemporalState.ShiftRecovery;
    }

    private void CancelShift()
    {
        Invincible = false;
        ControlsLocked = false;
        _state = TemporalState.Normal;
    }

    private void ApplyArrivalEffect()
    {
        float half = Units.Px(1.5f);
        foreach (Node node in GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not BaseFoe foe) continue;
            Vector2 delta = foe.GlobalPosition - GlobalPosition;
            if (Mathf.Abs(delta.X) > half + foe.HitRadius || Mathf.Abs(delta.Y) > half + foe.HitRadius) continue;
            float dealt = foe.TakeHit(new HitInfo(CharacterTable.Pixolotl.ShiftDamage, Vector2.Zero, 0f), GlobalPosition);
            foe.ApplyTrapped(CharacterTable.Pixolotl.ShiftTrapSeconds);
            ReportDamageDealt(dealt);
        }
        ShakeCamera(0.38f);
    }

    private void EndE()
    {
        if (_state == TemporalState.EHeld) _state = TemporalState.Normal;
    }

    private void RewindOwnedBubbles(float localSeconds)
    {
        foreach (Node node in GetTree().GetNodesInGroup("pixolotl_bubble"))
            if (node is PixolotlBubble bubble && bubble.TemporalOwner == this)
                bubble.Rewind(localSeconds);
    }

    /// <summary>Historical coordinates can become occupied after a moving platform or arena
    /// change. Shift uses the nearest free point rather than embedding the body in terrain.</summary>
    private Vector2 ResolveSafeFramePosition(Vector2 desired)
    {
        var collision = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        if (collision?.Shape == null || GetWorld2D() == null) return desired;

        Vector2[] directions =
        {
            Vector2.Zero, Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down,
            new Vector2(-1f, -1f).Normalized(), new Vector2(1f, -1f).Normalized(),
            new Vector2(-1f, 1f).Normalized(), new Vector2(1f, 1f).Normalized(),
        };
        for (int ring = 0; ring <= 8; ring++)
        {
            float distance = Units.Px(0.25f) * ring;
            foreach (Vector2 direction in directions)
            {
                Vector2 candidate = desired + direction * distance;
                var query = new PhysicsShapeQueryParameters2D
                {
                    Shape = collision.Shape,
                    Transform = new Transform2D(0f, candidate),
                    CollisionMask = (1u << (Units.LayerGround - 1)) | (1u << (Units.LayerPlatform - 1)),
                    CollideWithBodies = true,
                    CollideWithAreas = false,
                };
                if (GetWorld2D().DirectSpaceState.IntersectShape(query, 1).Count == 0) return candidate;
            }
        }
        return desired;
    }

    private void RegisterExistingBubbles()
    {
        foreach (Node node in GetTree().GetNodesInGroup("pixolotl_bubble"))
        {
            if (node is not PixolotlBubble bubble || _bubblePool.Contains(bubble)) continue;
            bubble.BindOwner(this);
            _bubblePool.Add(bubble);
        }
    }

    private void WarmBubblePool()
    {
        if (BubbleScene == null || GetParent() == null) return;
        while (_bubblePool.Count < CharacterTable.Pixolotl.BubbleAnglesDeg.Length * 3)
        {
            PixolotlBubble bubble = BubbleScene.Instantiate<PixolotlBubble>();
            GetParent().AddChild(bubble);
            bubble.AddToGroup("pixolotl_bubble");
            _bubblePool.Add(bubble);
        }
    }

    private PixolotlBubble RentBubble()
    {
        foreach (PixolotlBubble bubble in _bubblePool)
            if (IsInstanceValid(bubble) && !bubble.Active)
                return bubble;
        return null; // the authored 18-live cap is also the pool cap
    }

    private PapelPicadoGhost RentGhost()
    {
        foreach (PapelPicadoGhost ghost in _ghostPool)
            if (IsInstanceValid(ghost) && !ghost.Active)
                return ghost;
        var created = new PapelPicadoGhost();
        GetParent().AddChild(created);
        _ghostPool.Add(created);
        return created;
    }

    private void AdvanceGhosts(float actDt)
    {
        foreach (PapelPicadoGhost ghost in _ghostPool)
            if (IsInstanceValid(ghost)) ghost.Advance(actDt);
    }

    public override void _ExitTree()
    {
        foreach (PixolotlBubble bubble in _bubblePool)
            if (IsInstanceValid(bubble) && bubble.TemporalOwner == this)
                bubble.BindOwner(null);
        foreach (PapelPicadoGhost ghost in _ghostPool)
            if (IsInstanceValid(ghost)) ghost.QueueFree();
        base._ExitTree();
    }

    protected override void DrawDebug()
    {
        foreach (PapelPicadoFrame frame in _frames)
            DrawCircle(ToLocal(frame.Position), 4f, new Color(1f, 0.55f, 0.15f, 0.35f));
        DrawRect(new Rect2(new Vector2(-Units.Px(1.5f), -Units.Px(1.5f)), new Vector2(Units.Px(3f), Units.Px(3f))),
            new Color(1f, 0.55f, 0.15f, 0.18f), false, 1f);
    }
}
