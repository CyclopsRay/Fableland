using System.Collections.Generic;

namespace Fableland.Map;

/// <summary>
/// The VOID devour clock (MapGDD §8). Each outer sublevel is eaten at the END of its devour
/// day; on that day its nodes flicker as a warning. Lives on the map layer as a shared static
/// so both the map VIEW (flicker/reachability) and <c>RunState</c>'s day-end pipeline read the
/// same schedule — previously this table was private to MapController.
/// </summary>
public static class VoidSchedule
{
    public static readonly Dictionary<string, int> DevourDay = new()
    {
        ["1-A"] = 10, ["1-B"] = 20, ["2-A"] = 30, ["2-B"] = 35, ["3"] = 40, ["4"] = 45,
    };
}
