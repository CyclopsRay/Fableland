using System.Collections.Generic;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>
/// A kit that fights on the Wheel of Becoming (Docs/Mara.gdd §5–§6): Māra's own. The wheel is
/// the arena's (<see cref="WheelOfBecomingField"/>); it tells the body which realm is in force
/// and asks one thing back. A kit without it — the stand-in — still turns the wheel: the realms'
/// modifiers, the lock and Devour's heal are on the body whatever kit wears it.
/// </summary>
public interface IWheelFighter
{
    /// <summary>The fight begins: which poisons are sealed in him (the crown shows them).</summary>
    void WheelBegins(IReadOnlyCollection<string> sealedPoisons);

    /// <summary>A realm landed. <paramref name="arrival"/>: its arrival strike fires (false for the
    /// realm the fight opens on). Its power holds until the next realm lands.</summary>
    void RealmLanded(WheelRealm realm, bool arrival);

    /// <summary>The Last Turn passes a realm: its arrival strike only, nothing that holds.</summary>
    void RealmPassed(WheelRealm realm);

    /// <summary>The Last Turn began: no realm holds until the Interval lands.</summary>
    void LastTurnBegan();

    /// <summary>True while the colossus has a claw off the rim (Hand of the Wheel): a turn waits.</summary>
    bool HandOffWheel { get; }

    /// <summary>He fell in the Interval: the claws open.</summary>
    void Liberated();
}
