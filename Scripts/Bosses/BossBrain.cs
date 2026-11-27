namespace Fableland.Bosses;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// One rule of a rule-based boss brain (FOES.gdd §8.4): "when this is true, do that".
///
/// A rule belongs to a <see cref="Channel"/> — what it uses: legs (<see cref="BossBrain.Move"/>)
/// or hands (<see cref="BossBrain.Act"/>). One rule holds each channel at a time, so a boss
/// can walk and throw at once but never two throws. Inside a channel the rules are a
/// priority list: the first one that is off cooldown and whose <see cref="When"/> holds wins.
///
/// Two kinds, told apart by which action they carry:
///   • a ONE-SHOT has only <see cref="OnStart"/> — a tapped button. It fires once each time
///     it wins, then may win again after <see cref="Cooldown"/>.
///   • a CONTINUOUS rule has <see cref="Each"/> — held buttons, re-issued every decision for
///     as long as it keeps winning (its cooldown does not interrupt it).
/// </summary>
public sealed class BossRule
{
    public BossRule(string channel, string name, Func<bool> when)
    {
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        When = when ?? throw new ArgumentNullException(nameof(when));
    }

    public string Channel { get; }
    public string Name { get; }
    public Func<bool> When { get; }

    /// <summary>Runs once, on the decision the rule takes its channel.</summary>
    public Action OnStart { get; init; }

    /// <summary>Runs on every decision while the rule holds its channel, the first included.</summary>
    public Action Each { get; init; }

    /// <summary>Seconds, counted from a start, before the rule may start again.</summary>
    public float Cooldown { get; init; }

    /// <summary>Seconds the rule keeps its channel once started whatever else becomes true —
    /// commitment. Nothing in the channel, however urgent, interrupts a committed rule.</summary>
    public float Commit { get; init; }

    /// <summary>Optional early end of a commitment.</summary>
    public Func<bool> Done { get; init; }

    internal float CooldownLeft;
    internal float CommitLeft;

    /// <summary>How many times this rule has started — for tests and tuning.</summary>
    public int Starts { get; internal set; }
}

/// <summary>
/// The rule scheduler: pure C#, no nodes, no time source of its own — whoever owns it calls
/// <see cref="Decide"/> at the boss's reaction interval. Everything it does is explicit and
/// ordered (T00 rule 3), so a fight is reproducible and a rule set can be tested headless.
/// </summary>
public sealed class BossRuleSet
{
    private readonly List<BossRule> _rules = new();
    private readonly List<string> _channels = new();
    private readonly Dictionary<string, BossRule> _holding = new();

    public IReadOnlyList<BossRule> Rules => _rules;

    /// <summary>Appends a rule. Order is priority within its channel.</summary>
    public BossRuleSet Add(BossRule rule)
    {
        if (rule == null) return this;
        _rules.Add(rule);
        if (!_channels.Contains(rule.Channel)) _channels.Add(rule.Channel);
        return this;
    }

    /// <summary>The rule holding a channel right now, or null.</summary>
    public BossRule Holding(string channel) =>
        _holding.TryGetValue(channel, out BossRule rule) ? rule : null;

    /// <summary>One decision; <paramref name="dt"/> is the time since the last one.</summary>
    public void Decide(float dt)
    {
        foreach (BossRule rule in _rules)
            if (rule.CooldownLeft > 0f) rule.CooldownLeft = Math.Max(0f, rule.CooldownLeft - dt);

        foreach (string channel in _channels)
        {
            BossRule current = Holding(channel);
            if (current != null)
            {
                current.CommitLeft -= dt;
                if (current.CommitLeft > 0f && (current.Done == null || !current.Done()))
                {
                    current.Each?.Invoke();
                    continue;
                }
            }

            BossRule next = null;
            foreach (BossRule rule in _rules)
            {
                if (rule.Channel != channel) continue;
                bool continuing = rule == current && rule.Each != null;
                if (!continuing && rule.CooldownLeft > 0f) continue;
                if (!rule.When()) continue;
                next = rule;
                break;
            }

            _holding[channel] = next;
            if (next == null) continue;

            bool started = !(next == current && next.Each != null);
            if (started)
            {
                next.CooldownLeft = next.Cooldown;
                next.CommitLeft = next.Commit;
                next.Starts++;
                next.OnStart?.Invoke();
            }
            next.Each?.Invoke();
        }
    }

    /// <summary>"move:hold perch · act:bowl" — what the brain is doing, for the debug HUD.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        foreach (string channel in _channels)
            parts.Add(channel + ":" + (Holding(channel)?.Name ?? "—"));
        return string.Join(" · ", parts);
    }

    /// <summary>Structural guard over the scheduling rules.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("BossRuleSet mismatch: " + what); }

        // Every step is 1/8 s: a power of two, so the countdowns below land on exactly zero.
        const float step = 0.125f;
        bool danger = false, armed = true;
        int taps = 0, walks = 0, walkStarts = 0, flees = 0;
        var set = new BossRuleSet()
            .Add(new BossRule("move", "flee", () => danger) { Each = () => flees++, Commit = 0.5f })
            .Add(new BossRule("move", "walk", () => true) { OnStart = () => walkStarts++, Each = () => walks++, Cooldown = 5f })
            .Add(new BossRule("act", "throw", () => armed) { OnStart = () => taps++, Cooldown = 1f });

        set.Decide(step);                                         // decision 1
        Check(set.Holding("move")?.Name == "walk" && set.Holding("act")?.Name == "throw", "first decision: the first true rule of each channel");
        Check(taps == 1 && walks == 1 && walkStarts == 1, "a start runs OnStart then Each");

        set.Decide(step);                                         // 2
        Check(walks == 2 && walkStarts == 1, "a continuous rule keeps its channel despite its own cooldown, without restarting");
        Check(taps == 1 && set.Holding("act") == null, "a one-shot on cooldown releases its channel and does not fire again");

        for (int i = 0; i < 6; i++) set.Decide(step);             // 3..8: 0.875 s since the throw
        Check(taps == 1, "a one-shot waits out its whole cooldown");
        set.Decide(step);                                         // 9: exactly 1 s
        Check(taps == 2, "a one-shot fires again once its cooldown has passed");

        danger = true;
        set.Decide(step);                                         // 10
        Check(set.Holding("move")?.Name == "flee" && flees == 1, "a higher rule takes the channel as soon as it is true");
        danger = false;
        set.Decide(step);                                         // 11
        Check(set.Holding("move")?.Name == "flee" && flees == 2, "a committed rule keeps its channel after its condition ends");
        set.Decide(step);                                         // 12
        set.Decide(step);                                         // 13: 0.375 s into a 0.5 s commitment
        Check(set.Holding("move")?.Name == "flee" && flees == 4, "commitment lasts its full time");
        set.Decide(step);                                         // 14: commitment over, condition false
        // "walk" is true, but it lost its channel: taking it back is a new start, and its
        // 5 s cooldown has been running since decision 1.
        Check(set.Holding("move") == null, "a rule that lost its channel must wait out its cooldown to start again");

        armed = false;
        for (int i = 0; i < 26; i++) set.Decide(step);            // 15..40
        Check(set.Holding("move") == null && walkStarts == 1, "…for the whole cooldown");
        set.Decide(step);                                         // 41: exactly 5 s after its start
        Check(set.Holding("move")?.Name == "walk" && walkStarts == 2, "and starts again once it has");
        Check(taps == 2 && set.Holding("act") == null, "a rule whose condition is false never fires");
        Check(set.Describe() == "move:walk · act:—", "Describe: " + set.Describe());
        return failures;
    }
}

/// <summary>
/// What a boss brain knows about the fight. Refreshed once per decision (never per frame)
/// by <see cref="Sample"/>: a boss reacts at its reaction time, not instantly.
/// </summary>
public sealed class BossSenses
{
    // Self
    public Vector2 Position;
    public Vector2 Velocity;
    public bool OnFloor;
    public bool OnWall;
    public float HpRatio;

    // The player
    public bool HasTarget;
    public Vector2 TargetPosition;
    public Vector2 TargetVelocity;
    public float TargetRadius;
    public float Distance;

    /// <summary>Seconds since the previous decision.</summary>
    public float Dt;

    public Vector2 ToTarget => TargetPosition - Position;

    public void Sample(CharacterController self, float dt)
    {
        Dt = dt;
        Position = self.GlobalPosition;
        Velocity = self.Velocity;
        OnFloor = self.IsOnFloor();
        OnWall = self.IsOnWall();
        HpRatio = self.HpRatio;

        HasTarget = false;
        TargetVelocity = Vector2.Zero;
        Distance = float.MaxValue;
        if (!self.IsInsideTree()) return;
        if (self.GetTree().GetFirstNodeInGroup(CombatTargets.PlayerGroup) is not CharacterController player) return;
        if (player.IsDefeated || !GodotObject.IsInstanceValid(player)) return;

        // A sight cap a skill laid on him (the Black Prison): past it he does not know where
        // the player is, and every rule that needs a target waits.
        float distance = Position.DistanceTo(player.GlobalPosition);
        if (distance > self.SightLimitPx) return;

        HasTarget = true;
        TargetPosition = player.GlobalPosition;
        TargetVelocity = player.Velocity;
        TargetRadius = player.HitRadius;
        Distance = distance;
    }
}

/// <summary>
/// The node that runs a boss's brain: owns the <see cref="VirtualPad"/> the body reads,
/// asks the <see cref="BossRuleSet"/> for a decision every <see cref="DecisionSeconds"/>,
/// and steps the pad once per frame of each loop — always BEFORE the body it drives (its
/// process priorities are lower), so a button the brain taps is seen by the body that same
/// frame, exactly as a key press is.
///
/// Decisions are "immediate mode": every decision starts from released buttons and the
/// winning rules press what they want held. Re-pressing a held button is not an edge.
///
/// This class is the seam for other kinds of AI: anything that can fill a VirtualPad can
/// drive a boss (a behaviour tree, a recorded input track, a trained policy). The rule set
/// is simply the first driver (FOES.gdd §8.4).
/// </summary>
public partial class BossBrain : Node
{
    /// <summary>Channel of rules that steer: left, right, up, down, jump.</summary>
    public const string Move = "move";

    /// <summary>Channel of rules that fight: attack and the skills.</summary>
    public const string Act = "act";

    public VirtualPad Pad { get; } = new();
    public BossRuleSet Rules { get; } = new();

    /// <summary>The boss's reaction time: seconds between decisions.</summary>
    public float DecisionSeconds = 0.15f;

    /// <summary>Refreshes whatever the rules read. Called before each decision with the
    /// seconds since the last one.</summary>
    public Action<float> Sense;

    public bool Awake { get; private set; } = true;

    private CharacterController _body;
    private float _sinceDecision = float.MaxValue;   // decide on the first frame

    /// <summary>Creates a brain, hands its pad to <paramref name="body"/> and parents it there.</summary>
    public static BossBrain Attach(CharacterController body, float decisionSeconds)
    {
        var brain = new BossBrain
        {
            Name = "BossBrain",
            DecisionSeconds = Mathf.Max(0.02f, decisionSeconds),
            _body = body,
            // Lower priority runs first: the brain thinks, then the body acts on it.
            ProcessPriority = -100,
            ProcessPhysicsPriority = -100,
        };
        body.UseControls(brain.Pad);
        body.AddChild(brain);
        return brain;
    }

    public override void _Process(double delta)
    {
        if (Awake && _body != null && !_body.IsDefeated)
        {
            _sinceDecision = Mathf.Min(_sinceDecision, 1f) + (float)delta;
            if (_sinceDecision >= DecisionSeconds)
            {
                float dt = _sinceDecision;
                _sinceDecision = 0f;
                Pad.ReleaseHolds();
                Sense?.Invoke(dt);
                Rules.Decide(dt);
            }
        }
        Pad.StepProcess();
    }

    public override void _PhysicsProcess(double delta) => Pad.StepPhysics();

    /// <summary>The fight is over: let go of everything and stop deciding.</summary>
    public void Sleep()
    {
        Awake = false;
        Pad.ReleaseAll();
    }

    public string Describe() => Awake ? Rules.Describe() : "asleep";
}
