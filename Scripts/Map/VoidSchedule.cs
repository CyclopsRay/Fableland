using System;
using System.Collections.Generic;

namespace Fableland.Map;

/// <summary>
/// The city-field VOID clock (MapGDD §8). Early pulses consume the geometrically furthest half
/// of a realm's LV1/LV2 cities; later pulses consume full level bands. A city owns its complete
/// control field, so rendering and run-state devour share this one deterministic schedule.
/// </summary>
public static class VoidSchedule
{
    public const int FirstLevelHalfDay = 10;
    public const int FirstLevelAllDay = 20;
    public const int SecondLevelHalfDay = 30;
    public const int SecondLevelAllDay = 40;
    public const int ThirdLevelAllDay = 43;
    public const int AllOuterCitiesDay = 45;

    public static bool IsFlickering(MapGraph graph, MapNode node, int day) =>
        node != null && !node.Devoured && ShouldDevour(graph, node, day);

    public static bool ShouldDevour(MapGraph graph, MapNode node, int day)
    {
        if (graph == null || node == null || !node.IsCombat || node.WorldIndex < 0) return false;
        return day switch
        {
            FirstLevelHalfDay => node.Level == 1 && IsFurthestHalf(graph, node),
            FirstLevelAllDay => node.Level == 1,
            SecondLevelHalfDay => node.Level == 2 && IsFurthestHalf(graph, node),
            SecondLevelAllDay => node.Level == 2,
            ThirdLevelAllDay => node.Level == 3,
            AllOuterCitiesDay => true,
            _ => false,
        };
    }

    private static bool IsFurthestHalf(MapGraph graph, MapNode node)
    {
        var peers = new List<MapNode>();
        foreach (MapNode candidate in graph.Nodes)
            if (candidate.WorldIndex == node.WorldIndex && candidate.IsCombat && candidate.Level == node.Level)
                peers.Add(candidate);
        peers.Sort((a, b) =>
        {
            int byDistance = b.Pos.DistanceSquaredTo(graph.Center).CompareTo(a.Pos.DistanceSquaredTo(graph.Center));
            return byDistance != 0 ? byDistance : string.CompareOrdinal(a.Id, b.Id);
        });
        int limit = (peers.Count + 1) / 2;
        for (int i = 0; i < limit; i++)
            if (peers[i] == node) return true;
        return false;
    }
}
