using System.Collections.Generic;
using Godot;

namespace Fableland.Items;

/// <summary>
/// THE VOID's skill (ITEMS.gdd §7.2): for <see cref="Seconds"/> every foe loses its sight. Each
/// hostile target gets a sight cap of 0 under this cast's own source — the reaction every target
/// takes since the Black Prison (<see cref="ICombatTarget.SetSightLimit"/>): a capped foe drops
/// the player at once and a hit does not turn it; a boss brain finds no one. A foe that enters
/// the fight during the darkness is blinded as it arrives. The node lives in the arena, not on
/// the holder's body, so a Tab switch does not lift the darkness early; when it ends — or the
/// arena goes — every cap it laid is cleared.
/// TODO(art): the darkness itself (a veil over every foe's eyes, a hush in the music).
/// </summary>
public partial class VoidBlindness : Node
{
    public const float Seconds = 10f;

    /// <summary>Every darkness in the arena now (checks read it).</summary>
    public static readonly List<VoidBlindness> Active = new();

    public string Source { get; private set; } = "";
    public CombatSide Side { get; private set; } = CombatSide.Player;
    public float SecondsLeft { get; private set; }
    /// <summary>Targets this darkness has blinded so far (checks read it).</summary>
    public int Blinded => _blinded.Count;

    private readonly List<ICombatTarget> _blinded = new();
    private readonly HashSet<ulong> _seen = new();

    /// <summary>Darken the arena for the side's foes, under <paramref name="parent"/>.</summary>
    public static VoidBlindness Cast(Node parent, CombatSide side, string source)
    {
        var darkness = new VoidBlindness
        {
            Name = "VoidBlindness",
            Source = string.IsNullOrWhiteSpace(source) ? "item:the_void:blind" : source,
            Side = side,
            SecondsLeft = Seconds,
        };
        parent.AddChild(darkness);
        return darkness;
    }

    public override void _Ready()
    {
        Active.Add(this);
        BlindNewcomers();
    }

    public override void _PhysicsProcess(double delta)
    {
        SecondsLeft -= (float)delta;
        if (SecondsLeft <= 0f)
        {
            SecondsLeft = 0f;
            Lift();
            QueueFree();
            return;
        }
        BlindNewcomers();
    }

    public override void _ExitTree()
    {
        Lift();
        Active.Remove(this);
    }

    private void BlindNewcomers()
    {
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
        {
            if (!_seen.Add(target.TargetId)) continue;
            target.SetSightLimit(Source, 0f);
            _blinded.Add(target);
        }
    }

    private void Lift()
    {
        foreach (ICombatTarget target in _blinded)
            if (CombatTargets.IsAlive(target)) target.ClearSightLimit(Source);
        _blinded.Clear();
    }
}
