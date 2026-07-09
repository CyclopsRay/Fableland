namespace Fableland.Run;

/// <summary>
/// The goal a combat node presents. Rolled ONCE at map generation (NODES §4.1) and
/// stored on the node for the whole run; the arena only instantiates it.
/// Boss is <b>structural</b> (LV4/LV6 nodes) — never rolled (NODES decision log v0.3.7).
/// </summary>
public enum MissionType
{
    Collection, // collect wonder cores (60)
    Protect,    // defend the Condensed Wonder Core (15)
    Destroy,    // destroy objectives (10)
    Slaughter,  // clear enemy waves (10)
    Boss,       // kill the boss (LV4/LV6 only, structural)
}
