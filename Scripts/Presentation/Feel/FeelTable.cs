using Godot;
using System.Collections.Generic;
using Fableland.Data;

/// <summary>
/// The feel director's own numbers (<c>Docs/Feedback.gdd</c> §5): what a hit of each weight
/// does to the camera and to the body it landed on, and the few cues that get a beat of their
/// own. PRESENTATION data — nothing here can change a fight; hit-stop, the one part of feel
/// that can, lives in <c>FeedbackTable</c>.
///
/// Starting values from <c>Docs/Research/R2-vfx-feel-research.md</c> §4.2, reasoned at
/// 32 px/m and <b>not yet tuned by eye</b>: shake offset = trauma² × 30 px, so the Light cap
/// (0.30) is 2.7 px and the Heavy cap (0.75) 17 px — small hits are carried by the kick and
/// the flash, and the shake is kept for the ones that deserve it.
/// </summary>
public static class FeelTable
{
    /// <summary>What a hit of one weight does. The camera columns are read one tier up when
    /// the victim is the player: being hit must be felt more than hitting.</summary>
    public sealed class WeightFeel
    {
        public HitWeight Weight { get; init; }
        /// <summary>Trauma added, and the ceiling hits of this weight cannot shake above.</summary>
        public float ShakeAdd { get; init; }
        public float ShakeCap { get; init; }
        /// <summary>Camera shove along the blow, px.</summary>
        public float KickPx { get; init; }
        /// <summary>Roll trauma added (0–1; the camera's full roll is under a degree).</summary>
        public float Roll { get; init; }
        /// <summary>How long the victim is shown in the flash colour.</summary>
        public float FlashSeconds { get; init; }
        /// <summary>False = a tint only, never a full flash (a graze).</summary>
        public bool FullFlash { get; init; }
        /// <summary>Zoom punch (0.02 = 1.02×); 0 for none.</summary>
        public float Zoom { get; init; }
    }

    /// <summary>Ordered light to heavy. Index = (int)HitWeight - 1.</summary>
    public static readonly WeightFeel[] ByWeight =
    {
        new() { Weight = HitWeight.Graze,   FlashSeconds = 0.033f },
        new() { Weight = HitWeight.Light,   ShakeAdd = 0.15f, ShakeCap = 0.30f, KickPx = 2f,  FlashSeconds = 0.033f, FullFlash = true },
        new() { Weight = HitWeight.Medium,  ShakeAdd = 0.25f, ShakeCap = 0.50f, KickPx = 4f,  FlashSeconds = 0.050f, FullFlash = true },
        new() { Weight = HitWeight.Heavy,   ShakeAdd = 0.40f, ShakeCap = 0.75f, KickPx = 7f,  Roll = 0.5f, FlashSeconds = 0.067f, FullFlash = true, Zoom = 0.02f },
        new() { Weight = HitWeight.Massive, ShakeAdd = 0.55f, ShakeCap = 0.90f, KickPx = 10f, Roll = 1f,   FlashSeconds = 0.083f, FullFlash = true, Zoom = 0.04f },
    };

    public static WeightFeel For(HitWeight weight) =>
        ByWeight[Mathf.Clamp((int)weight - 1, 0, ByWeight.Length - 1)];

    /// <summary>The tier the camera reads when the player is the one hit.</summary>
    public static HitWeight Heavier(HitWeight weight) =>
        (HitWeight)Mathf.Min((int)weight + 1, (int)HitWeight.Massive);

    // ── A hit's zoom punch: in, hold, out ───────────────────────────────────────────
    public const float ZoomInSeconds = 0.03f;
    public const float ZoomHoldSeconds = 0.08f;
    public const float ZoomOutSeconds = 0.16f;

    // ── The hit that kills adds a little, whatever its weight ───────────────────────
    public const float KillShakeAdd = 0.10f;
    public const float KillZoom = 0.015f;

    // ── Many victims, one moment: one add at the heaviest tier, a sliver per extra ──
    public const float ExtraVictimShake = 0.03f;
    public const int MaxExtraVictims = 5;

    // ── Flash ───────────────────────────────────────────────────────────────────────
    /// <summary>How strongly a tint pulls a body toward the flash colour: what a graze
    /// gets, what "reduce flashing" turns every flash into, and what a flash over the
    /// limits below is demoted to.</summary>
    public const float TintStrength = 0.35f;
    /// <summary>On a dark or mid-tone arena a body flashes light…</summary>
    public static readonly Color LightFlash = new(1f, 1f, 1f);
    /// <summary>…and on a pale one (Wulin's bare paper, Ghost Town's cream) it flashes ink.</summary>
    public static readonly Color DarkFlash = new(0.078f, 0.090f, 0.102f);
    /// <summary>A backdrop at least this light gets the dark flash. Wulin (#E4E9E8) and
    /// Ghost Town (#F1E3C4) are about 0.9; a sky-blue canvas (#87CEEB, 0.76) keeps white.</summary>
    public const float LightArenaLuminance = 0.80f;

    // Photosensitivity (WCAG 2.3.1, Xbox Accessibility Guideline 118 — the stricter of the two):
    /// <summary>Flashes within this long of each other are one flashing moment.</summary>
    public const float FlashMomentSeconds = 0.10f;
    /// <summary>At most this many bodies flash at full strength in one moment; the rest tint.</summary>
    public const int MaxFullFlashesPerMoment = 6;
    /// <summary>A moment whose flashing bodies cover more of the view than this is a LARGE flash…</summary>
    public const float LargeFlashViewShare = 0.025f;
    /// <summary>…and no more than this many large flashes may start in any one second.</summary>
    public const int MaxLargeFlashesPerSecond = 3;
    /// <summary>No flash colour may be a saturated red: R / (R + G + B) must stay under this.</summary>
    public const float SaturatedRedShare = 0.8f;
    /// <summary>No single flash is longer than this.</summary>
    public const float MaxFlashSeconds = 0.10f;

    /// <summary>A beat for one cue — the moments that are not a hit.</summary>
    public sealed class CueFeel
    {
        public float ShakeAdd { get; init; }
        public float ShakeCap { get; init; }
        /// <summary>The shake part fires at most this often (0 = every time).</summary>
        public float ShakeEverySeconds { get; init; }
        public float Roll { get; init; }
        public float Zoom { get; init; }
        public float ZoomIn { get; init; } = ZoomInSeconds;
        public float ZoomHold { get; init; } = ZoomHoldSeconds;
        public float ZoomOut { get; init; } = ZoomOutSeconds;
        /// <summary>Flash the cue's source body for this long (0 = no flash).</summary>
        public float FlashSeconds { get; init; }
        public bool FullFlash { get; init; }
        /// <summary>The flash part fires at most this often (0 = every time).</summary>
        public float FlashEverySeconds { get; init; }
        /// <summary>Only when the cue belongs to the body the player wears.</summary>
        public bool PlayerOnly { get; init; }
        /// <summary>Ignored when the cue's magnitude is below this (a head that only pops).</summary>
        public float MinMagnitude { get; init; }

        /// <summary>Runtime, not data: when this row may shake / flash again (the director's clock).</summary>
        internal double ShakeReadyAt;
        internal double FlashReadyAt;
    }

    /// <summary>Keyed like every director's table: (owner, action), falling back
    /// owner → class → "any".</summary>
    public static readonly CueTable<CueFeel> ByCue = new CueTable<CueFeel>()
        // A boss falls: the biggest beat in the game. One flash on the boss, never the screen.
        .Add(CueOwners.Flow, "boss.defeated", new CueFeel
        {
            ShakeAdd = 1f, ShakeCap = 1f, Roll = 1f,
            Zoom = 0.10f, ZoomIn = 0.10f, ZoomHold = 0.30f, ZoomOut = 0.60f,
            FlashSeconds = 0.10f, FullFlash = true,
        })
        // The player goes down.
        .Add(CueOwners.Flow, "player.down", new CueFeel
        {
            ShakeAdd = 0.60f, ShakeCap = 0.90f,
            Zoom = 0.06f, ZoomIn = 0.06f, ZoomHold = 0.20f, ZoomOut = 0.50f,
        })
        // PumpKing's head goes off: the blast shakes even when it catches nobody.
        .Add("pumpking", "ba.explode", new CueFeel { ShakeAdd = 0.40f, ShakeCap = 0.75f, MinMagnitude = 0.01f })
        // The player stands in something that hurts: a tint, under three a second however
        // many kinds of damage are ticking at once, and a light shake at most once a second.
        .Add(CueOwners.Any, HurtTickMeter.Action, new CueFeel
        {
            PlayerOnly = true, ShakeAdd = 0.15f, ShakeCap = 0.30f, ShakeEverySeconds = 1f,
            FlashSeconds = 0.033f, FlashEverySeconds = 0.34f,
        });

    /// <summary>Boot-time checks (T10 §5): tiers in order and rising, flashes short and
    /// never a saturated red, ceilings above what is added.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        for (int i = 0; i < ByWeight.Length; i++)
        {
            WeightFeel row = ByWeight[i];
            if ((int)row.Weight != i + 1) failures.Add($"row {i} is {row.Weight}, expected weight {i + 1}");
            if (row.ShakeAdd > row.ShakeCap + 0.0001f) failures.Add($"{row.Weight}: adds more trauma than its cap");
            if (row.ShakeCap > 1f) failures.Add($"{row.Weight}: cap over 1");
            if (row.FlashSeconds > MaxFlashSeconds) failures.Add($"{row.Weight}: flash longer than {MaxFlashSeconds} s");
            if (row.Zoom > 0.04f + 0.0001f) failures.Add($"{row.Weight}: a hit's zoom punch over 4 % blurs world-space text");
            if (i == 0) continue;
            WeightFeel lighter = ByWeight[i - 1];
            if (row.ShakeCap < lighter.ShakeCap || row.ShakeAdd < lighter.ShakeAdd) failures.Add($"{row.Weight}: shakes less than {lighter.Weight}");
            if (row.KickPx < lighter.KickPx) failures.Add($"{row.Weight}: kicks less than {lighter.Weight}");
            if (row.FlashSeconds < lighter.FlashSeconds) failures.Add($"{row.Weight}: flashes shorter than {lighter.Weight}");
        }
        if (For(HitWeight.Graze).ShakeAdd != 0f || For(HitWeight.Graze).FullFlash) failures.Add("a graze shakes or flashes");
        if (Heavier(HitWeight.Massive) != HitWeight.Massive || Heavier(HitWeight.Light) != HitWeight.Medium)
            failures.Add("Heavier() does not step one tier, capped at Massive");

        CheckColour(LightFlash, "LightFlash", failures);
        CheckColour(DarkFlash, "DarkFlash", failures);
        if (LightFlash.Luminance < 0.8f) failures.Add("LightFlash is not light");
        if (DarkFlash.Luminance > 0.2f) failures.Add("DarkFlash is not dark");
        float tint = TintStrength;
        if (tint <= 0f || tint >= 1f) failures.Add("TintStrength is not a share");

        foreach ((string owner, string action) in ByCue.Keys)
        {
            CueFeel row = ByCue.Resolve(owner, null, action);
            if (row.ShakeAdd > row.ShakeCap + 0.0001f) failures.Add($"{owner}/{action}: adds more trauma than its cap");
            if (row.FlashSeconds > MaxFlashSeconds + 0.0001f) failures.Add($"{owner}/{action}: flash longer than {MaxFlashSeconds} s");
        }
        return failures;
    }

    private static void CheckColour(Color colour, string name, List<string> failures)
    {
        float sum = colour.R + colour.G + colour.B;
        if (sum > 0.01f && colour.R / sum >= SaturatedRedShare) failures.Add($"{name} is a saturated red");
    }
}

/// <summary>
/// Keeps hit flashes inside the photosensitivity limits whatever a fight does
/// (<c>Docs/Feedback.gdd</c> §5): flashes that start within a tenth of a second of each other
/// are one flashing moment; at most six bodies flash at full strength in a moment; and a
/// moment whose bodies cover more than 2.5 % of the view is a large flash, of which no more
/// than three may start in any second. Whatever is refused is shown as a tint instead.
/// Pure arithmetic on a clock the caller supplies.
/// </summary>
public sealed class FlashLimiter
{
    private readonly double[] _largeAt = new double[FeelTable.MaxLargeFlashesPerSecond];
    private double _momentStart = double.NegativeInfinity;
    private int _momentFull;
    private float _momentShare;
    private bool _momentIsLarge;

    public FlashLimiter()
    {
        for (int i = 0; i < _largeAt.Length; i++) _largeAt[i] = double.NegativeInfinity;
    }

    /// <summary>May a body covering <paramref name="viewShare"/> of the view flash at full
    /// strength at <paramref name="now"/> (seconds)? False = tint it instead.</summary>
    public bool AllowFull(float viewShare, double now)
    {
        if (now - _momentStart > FeelTable.FlashMomentSeconds || now < _momentStart)
        {
            _momentStart = now;
            _momentFull = 0;
            _momentShare = 0f;
            _momentIsLarge = false;
        }
        if (_momentFull >= FeelTable.MaxFullFlashesPerMoment) return false;
        if (!_momentIsLarge && _momentShare + viewShare > FeelTable.LargeFlashViewShare)
        {
            if (!TakeLargeSlot(now)) return false;
            _momentIsLarge = true;
        }
        _momentFull++;
        _momentShare += viewShare;
        return true;
    }

    private bool TakeLargeSlot(double now)
    {
        int oldest = 0;
        int recent = 0;
        for (int i = 0; i < _largeAt.Length; i++)
        {
            if (now - _largeAt[i] < 1.0) recent++;
            if (_largeAt[i] < _largeAt[oldest]) oldest = i;
        }
        if (recent >= _largeAt.Length) return false;
        _largeAt[oldest] = now;
        return true;
    }

    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        const float fighter = 0.0035f;   // a 2 m fighter in a 1152 × 648 view

        var limiter = new FlashLimiter();
        int full = 0;
        for (int i = 0; i < 20; i++) if (limiter.AllowFull(fighter, 10.0)) full++;
        if (full != FeelTable.MaxFullFlashesPerMoment) failures.Add($"{full} bodies flashed in one moment, expected {FeelTable.MaxFullFlashesPerMoment}");

        // Six fighters at once stay under the large-flash area, so they use no slot.
        limiter = new FlashLimiter();
        for (int i = 0; i < 6; i++) limiter.AllowFull(fighter, 1.0);
        if (!limiter.AllowFull(0.05f, 1.2)) failures.Add("six fighter flashes used up a large-flash slot");

        // A body bigger than the threshold: three a second, the fourth is refused, and it is
        // allowed again once the first is a second old.
        limiter = new FlashLimiter();
        int large = 0;
        for (int i = 0; i < 4; i++) if (limiter.AllowFull(0.04f, 5.0 + i * 0.2)) large++;
        if (large != 3) failures.Add($"{large} large flashes within a second, expected 3");
        if (limiter.AllowFull(0.04f, 5.9)) failures.Add("a fourth large flash got through inside the second");
        if (!limiter.AllowFull(0.04f, 6.05)) failures.Add("a large flash is still refused after the first is a second old");
        return failures;
    }
}
