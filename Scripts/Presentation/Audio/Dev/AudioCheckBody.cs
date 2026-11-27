using Godot;

/// <summary>A body as presentation sees one — an id, a class, a side, a state — for the
/// audio check to raise cues and hits with (<c>AudioCheck</c>). It does nothing else.</summary>
public partial class AudioCheckBody : Node2D, IFeedbackBody
{
    public string Id = "crab";
    public string Class = CueOwners.Foe;
    public CombatSide BodySide = CombatSide.Foe;
    public FeedbackState State = FeedbackState.None;

    public string FeedbackId => Id;
    public string FeedbackClass => Class;
    public CombatSide Side => BodySide;
    public FeedbackState FeedbackState => State;
    public Vector2 FeedbackCenter => Position;
    public float FeedbackRadius => 32f;
    public bool InHitStop => false;
    public void FlashTint(Color tint, float seconds) { }
}
