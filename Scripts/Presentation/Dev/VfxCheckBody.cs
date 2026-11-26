namespace Fableland.Vfx.Dev;

using Godot;

/// <summary>
/// A stand-in body for <see cref="VfxCheck"/>: everything presentation may know of a body
/// (<see cref="IFeedbackBody"/>) and nothing else — no physics, no kit — so the check owns
/// every cue it raises and can say what state the body is in.
/// </summary>
public partial class VfxCheckBody : Node2D, IFeedbackBody
{
    public string Id = "newcomer";
    public string Class = CueOwners.Fighter;
    public CombatSide BodySide = CombatSide.Player;
    public FeedbackState State;
    public float Radius = 22f;
    public int Flashes;

    public string FeedbackId => Id;
    public string FeedbackClass => Class;
    public CombatSide Side => BodySide;
    public FeedbackState FeedbackState => State;
    public Vector2 FeedbackCenter => GlobalPosition;
    public float FeedbackRadius => Radius;
    public bool InHitStop => false;
    public void FlashTint(Color tint, float seconds) => Flashes++;
}
