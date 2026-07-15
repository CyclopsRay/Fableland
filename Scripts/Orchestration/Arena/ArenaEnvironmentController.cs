using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Fableland.Data;
using Fableland.Map;
using Fableland.Run;

/// <summary>
/// Arena-scoped coordinator for temporary environmental events (Gameplay.gdd §A.6).
/// It owns an event's ordered lifecycle — warning, payload sweep, restoration, cooldown
/// — while individual hazards remain responsible for their own collision and damage.
///
/// This lives in ORCHESTRATION: combat actors keep their generic AddImpulse API and know
/// nothing about this controller; the controller applies the current wind field to them.
/// Presentation is limited to the authored canvas and map-visual roots, so the HUD and
/// combat actors are not accidentally tinted with the environment.
/// </summary>
public partial class ArenaEnvironmentController : Node
{
    [ExportGroup("Scene wiring")]
    [Export] public NodePath CanvasPath = "../Background";
    [Export] public NodePath WorldVisualPath = "../World";
    [Export] public NodePath HazardsPath = "../Hazards";
    [Export] public NodePath TsunamiTriggerPath = "../Hazards/TsunamiButton";

    /// <summary>Current visual wind strength in [0,1]. Visual responders read this;
    /// ambient gusts never apply a physics impulse in this first slice.</summary>
    public float VisualWindStrength { get; private set; }

    /// <summary>Horizontal wind direction: -1 is leftward, from the sea/right edge.</summary>
    public float WindDirection { get; private set; } = -1f;

    private enum Phase { Calm, Warning, Sweep, Recovery, Cooldown }

    private Phase _phase = Phase.Calm;
    private EnvironmentEventDef _tsunami;
    private DetRandom _rng;

    private ColorRect _canvas;
    private CanvasItem _worldVisual;
    private Node _hazards;
    private TsunamiTrigger _tsunamiTrigger;
    private readonly List<TsunamiTrigger> _tsunamiTriggers = new();
    private TsunamiHazard _wave;

    private Color _normalCanvasColor = Colors.White;
    private Color _normalWorldModulate = Colors.White;
    private float _phaseTime;
    private float _cooldownRemaining;
    private float _windPulseTimer;
    private float _ambientNextGust;
    private float _ambientGustRemaining;
    private readonly List<Node2D> _windTargets = new();

    public override void _EnterTree() => AddToGroup("arena_environment");

    public override void _Ready()
    {
        _canvas = GetNodeOrNull<ColorRect>(CanvasPath);
        _worldVisual = GetNodeOrNull<CanvasItem>(WorldVisualPath);
        _hazards = GetNodeOrNull<Node>(HazardsPath);
        _tsunamiTrigger = string.IsNullOrEmpty(TsunamiTriggerPath.ToString())
            ? null
            : GetNodeOrNull<TsunamiTrigger>(TsunamiTriggerPath);

        if (_canvas == null) GD.PushError("ArenaEnvironmentController: CanvasPath does not resolve to a ColorRect.");
        else _normalCanvasColor = _canvas.Color;
        if (_worldVisual == null) GD.PushError("ArenaEnvironmentController: WorldVisualPath does not resolve to a CanvasItem.");
        else _normalWorldModulate = _worldVisual.Modulate;
        if (_hazards == null) GD.PushError("ArenaEnvironmentController: HazardsPath does not resolve to a Node.");

        if (!EnvironmentEventDefs.TryGet("tsunami", out _tsunami))
            GD.PushError("ArenaEnvironmentController: missing required environment definition 'tsunami'.");

        if (_tsunamiTrigger == null)
        {
            // Map playtests register their authored triggers after this controller enters the
            // tree. An empty path is therefore valid; a non-empty bad path is still a scene
            // wiring error worth surfacing.
            if (!string.IsNullOrEmpty(TsunamiTriggerPath.ToString()))
                GD.PushError("ArenaEnvironmentController: TsunamiTriggerPath does not resolve to a TsunamiTrigger.");
        }
        else
        {
            RegisterTsunamiTrigger(_tsunamiTrigger);
        }

        var rs = RunState.Instance;
        string nodeId = rs?.CurrentAdventure?.NodeId ?? "debug";
        _rng = rs?.CurrentAdventure != null
            ? rs.Rng.Sub("arena_environment:" + nodeId)
            : new DetRandom("debug-arena-environment");
        ScheduleNextAmbientGust();
    }

    public override void _ExitTree()
    {
        for (int i = 0; i < _tsunamiTriggers.Count; i++)
            if (IsInstanceValid(_tsunamiTriggers[i])) _tsunamiTriggers[i].Activated -= OnTsunamiTriggered;
        _tsunamiTriggers.Clear();
        if (_wave != null && IsInstanceValid(_wave)) _wave.Finished -= OnWaveFinished;

        ShakeCamera2D.Instance?.SetSustainedTrauma(0f);
        RestoreAuthoredPresentation();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_tsunami == null) return;
        float dt = (float)delta;

        UpdateAmbientGust(dt);

        switch (_phase)
        {
            case Phase.Warning:
                TickWarning(dt);
                break;
            case Phase.Sweep:
                TickSweep(dt);
                break;
            case Phase.Recovery:
                TickRecovery(dt);
                break;
            case Phase.Cooldown:
                _cooldownRemaining -= dt;
                if (_cooldownRemaining <= 0f)
                {
                    _phase = Phase.Calm;
                    ArmAllTsunamiTriggers();
                }
                break;
        }
    }

    private void OnTsunamiTriggered(TsunamiTrigger trigger)
    {
        if (_phase != Phase.Calm || trigger == null) return;

        _tsunamiTrigger = trigger;
        _tsunamiTrigger.SetArmed(false);
        _phase = Phase.Warning;
        _phaseTime = 0f;
    }

    /// <summary>
    /// Binds an authored tsunami trigger to this arena. The fixed Arena scene uses the
    /// exported path; map playtests call this after instantiating each hazard tile. Keeping
    /// the registration here lets both routes share the exact same event lifecycle.
    /// </summary>
    public void RegisterTsunamiTrigger(TsunamiTrigger trigger)
    {
        if (trigger == null || _tsunamiTriggers.Contains(trigger)) return;
        _tsunamiTriggers.Add(trigger);
        trigger.Activated += OnTsunamiTriggered;
        trigger.SetArmed(_phase == Phase.Calm);
    }

    private void ArmAllTsunamiTriggers()
    {
        for (int i = _tsunamiTriggers.Count - 1; i >= 0; i--)
        {
            TsunamiTrigger trigger = _tsunamiTriggers[i];
            if (!IsInstanceValid(trigger))
            {
                _tsunamiTriggers.RemoveAt(i);
                continue;
            }
            trigger.SetArmed(true);
        }
    }

    private void TickWarning(float dt)
    {
        _phaseTime += dt;
        float stormBlend = Progress(_phaseTime, _tsunami.WarningDurationSec);
        ApplyStormPresentation(stormBlend);
        VisualWindStrength = Mathf.Clamp(GetAmbientVisualStrength() + _tsunami.StormWindVisualStrength * stormBlend, 0f, 1f);
        float shakeBlend = Progress(_phaseTime, _tsunami.ShakeRampDurationSec);
        ShakeCamera2D.Instance?.SetSustainedTrauma(_tsunami.SustainedShakeTrauma * shakeBlend);

        if (stormBlend >= 1f) StartTsunamiSweep();
    }

    private void StartTsunamiSweep()
    {
        _phase = Phase.Sweep;
        _phaseTime = 0f;
        _windPulseTimer = 0f; // first pulse is delivered on the first sweep physics tick
        RefreshWindTargets();

        if (_hazards == null || _tsunamiTrigger?.TsunamiScene == null)
        {
            GD.PushError("ArenaEnvironmentController: tsunami cannot sweep without Hazards and TsunamiScene wiring.");
            BeginRecovery();
            return;
        }

        _wave = _tsunamiTrigger.TsunamiScene.Instantiate<TsunamiHazard>();
        _hazards.AddChild(_wave);
        _wave.GlobalPosition = _tsunamiTrigger.SpawnPosition;
        _wave.Finished += OnWaveFinished;
    }

    private void TickSweep(float dt)
    {
        VisualWindStrength = 1f;
        ShakeCamera2D.Instance?.SetSustainedTrauma(_tsunami.SustainedShakeTrauma);
        _windPulseTimer -= dt;
        if (_windPulseTimer <= 0f)
        {
            _windPulseTimer += _tsunami.StormWindPulseIntervalSec;
            DeliverStormWindPulse();
        }
    }

    private void OnWaveFinished()
    {
        if (_wave != null && IsInstanceValid(_wave)) _wave.Finished -= OnWaveFinished;
        _wave = null;
        BeginRecovery();
    }

    private void BeginRecovery()
    {
        if (_phase == Phase.Recovery || _phase == Phase.Cooldown || _phase == Phase.Calm) return;
        _phase = Phase.Recovery;
        _phaseTime = 0f;
        _windTargets.Clear();
        ShakeCamera2D.Instance?.SetSustainedTrauma(0f);
    }

    private void TickRecovery(float dt)
    {
        _phaseTime += dt;
        float restoration = Progress(_phaseTime, _tsunami.RestoreDurationSec);
        ApplyStormPresentation(1f - restoration);
        VisualWindStrength = Mathf.Max(GetAmbientVisualStrength(), 1f - restoration);

        if (restoration < 1f) return;

        RestoreAuthoredPresentation();
        _phase = Phase.Cooldown;
        _cooldownRemaining = _tsunami.CooldownSec;
    }

    private void UpdateAmbientGust(float dt)
    {
        _ambientNextGust -= dt;
        if (_ambientGustRemaining > 0f)
        {
            _ambientGustRemaining -= dt;
            return;
        }
        if (_ambientNextGust > 0f) return;

        _ambientGustRemaining = _tsunami.AmbientGustDurationSec;
        ScheduleNextAmbientGust();
    }

    private void ScheduleNextAmbientGust()
    {
        float t = (float)_rng.NextDouble();
        _ambientNextGust = Mathf.Lerp(_tsunami.AmbientGustMinIntervalSec, _tsunami.AmbientGustMaxIntervalSec, t);
    }

    private float GetAmbientVisualStrength()
    {
        if (_ambientGustRemaining <= 0f || _tsunami.AmbientGustDurationSec <= 0f) return 0f;
        float elapsed = _tsunami.AmbientGustDurationSec - _ambientGustRemaining;
        float arc = Mathf.Sin(Mathf.Pi * Progress(elapsed, _tsunami.AmbientGustDurationSec));
        return _tsunami.AmbientGustVisualStrength * arc;
    }

    private void DeliverStormWindPulse()
    {
        // A switch can replace the sole player during the sweep. It is cheap and allocation-free
        // to ensure that direct reference every pulse; foe targets were captured on sweep entry.
        var currentPlayer = CharacterController.LocalPlayer;
        if (currentPlayer != null && !_windTargets.Contains(currentPlayer)) _windTargets.Add(currentPlayer);

        Vector2 impulse = Vector2.Right * WindDirection * Units.Px(_tsunami.StormWindPulseMps);
        for (int i = _windTargets.Count - 1; i >= 0; i--)
        {
            Node2D target = _windTargets[i];
            if (!IsInstanceValid(target))
            {
                _windTargets.RemoveAt(i);
                continue;
            }

            if (target is CharacterController character) character.AddImpulse(impulse);
            else if (target is BaseFoe foe) foe.AddImpulse(impulse);
        }
    }

    private void RefreshWindTargets()
    {
        _windTargets.Clear();
        AddGroupTargets("player");
        AddGroupTargets("foe");
    }

    private void AddGroupTargets(StringName group)
    {
        var nodes = GetTree().GetNodesInGroup(group);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is Node2D target && !_windTargets.Contains(target)) _windTargets.Add(target);
        }
    }

    private void ApplyStormPresentation(float blend)
    {
        if (_canvas != null && IsInstanceValid(_canvas))
            _canvas.Color = _normalCanvasColor.Lerp(ColorFromHex(_tsunami.StormCanvasHex, _normalCanvasColor), blend);
        if (_worldVisual != null && IsInstanceValid(_worldVisual))
            _worldVisual.Modulate = _normalWorldModulate.Lerp(ColorFromHex(_tsunami.StormWorldTintHex, _normalWorldModulate), blend);
    }

    private void RestoreAuthoredPresentation()
    {
        if (_canvas != null && IsInstanceValid(_canvas)) _canvas.Color = _normalCanvasColor;
        if (_worldVisual != null && IsInstanceValid(_worldVisual)) _worldVisual.Modulate = _normalWorldModulate;
    }

    private static float Progress(float value, float duration) =>
        duration <= 0f ? 1f : Mathf.Clamp(value / duration, 0f, 1f);

    private static Color ColorFromHex(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        string value = hex.Trim().TrimStart('#');
        if (value.Length != 6 && value.Length != 8) return fallback;
        if (!int.TryParse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r)
            || !int.TryParse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g)
            || !int.TryParse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b)) return fallback;
        int a = 255;
        if (value.Length == 8 && !int.TryParse(value.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)) return fallback;
        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }
}
