using System;
using System.Collections.Generic;

/// <summary>
/// Pure combat-time math. A future bullet-time effect supplies <paramref name="worldRate"/>,
/// while a character or projectile supplies its relative local rate. Neither UI nor input
/// consults this class.
/// </summary>
public static class LocalTime
{
    public const float DefaultRate = 1f;
    public const float MinimumRate = 0.01f;
    public const float MaximumRate = 20f;

    public static float ClampRate(float rate)
    {
        if (float.IsNaN(rate) || float.IsInfinity(rate)) return DefaultRate;
        return Math.Clamp(rate, MinimumRate, MaximumRate);
    }

    /// <summary>Return an entity's simulation delta from the arena-world delta.</summary>
    public static float ActDelta(float worldDelta, float relativeRate = DefaultRate, float worldRate = DefaultRate)
    {
        if (worldDelta <= 0f || float.IsNaN(worldDelta) || float.IsInfinity(worldDelta)) return 0f;
        return worldDelta * ClampRate(relativeRate) * ClampRate(worldRate);
    }

    /// <summary>How many world seconds are required to advance a local duration.</summary>
    public static float WorldSecondsFor(float localSeconds, float relativeRate = DefaultRate, float worldRate = DefaultRate)
    {
        if (localSeconds <= 0f) return 0f;
        return localSeconds / (ClampRate(relativeRate) * ClampRate(worldRate));
    }

    /// <summary>Pure checks run at debug boot and are deliberately Godot-free.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        AssertNear(ActDelta(1f, 1f), 1f, "default 1× delta", failures);
        AssertNear(ActDelta(1f, 1.5f), 1.5f, "1.5× local delta", failures);
        AssertNear(ActDelta(1f, 5f, 0.1f), 0.5f, "world bullet-time composition", failures);
        AssertNear(WorldSecondsFor(0.9f, 1.5f), 0.6f, "0.9 local-second reload at 1.5×", failures);
        AssertNear(WorldSecondsFor(3f, 1.5f), 2f, "ten 0.3-second frames at 1.5×", failures);
        AssertNear(WorldSecondsFor(10f, 1.5f), 10f / 1.5f, "10-second bubble lifetime at 1.5×", failures);
        AssertNear(ClampRate(-2f), MinimumRate, "negative rate clamp", failures);
        return failures;
    }

    private static void AssertNear(float actual, float expected, string label, List<string> failures)
    {
        if (MathF.Abs(actual - expected) > 0.0001f)
            failures.Add($"{label}: expected {expected:F4}, got {actual:F4}");
    }
}
