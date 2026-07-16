using System;

namespace Fableland.Run;

/// <summary>
/// Immutable combat-local copy of a configured team. RunState owns the editable
/// shelter build; AdventureContext carries this frozen order into one arena so a
/// mid-battle switch never rereads or mutates the shelter configuration.
/// </summary>
public sealed class BattleTeamSnapshot
{
    private readonly string[] _memberIds;

    /// <summary>A defensive copy keeps the handoff immutable even if a caller keeps
    /// a reference to its original editable team array.</summary>
    public string[] MemberIds => (string[])_memberIds.Clone();
    public int InitialIndex { get; }
    public int Count => _memberIds.Length;

    public BattleTeamSnapshot(string[] memberIds, int initialIndex)
    {
        _memberIds = memberIds == null ? Array.Empty<string>() : (string[])memberIds.Clone();
        InitialIndex = _memberIds.Length == 0
            ? 0 : Math.Clamp(initialIndex, 0, _memberIds.Length - 1);
    }
}
