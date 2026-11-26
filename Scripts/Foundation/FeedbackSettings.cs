using System;

/// <summary>
/// The player's feedback preferences — how much shake, flash, hit-stop, particle density and
/// volume they want (<c>Docs/Feedback.gdd</c> §7). Plain numbers, 0–1, readable from any
/// layer. Nothing writes or saves them yet: the options screen and their persistence are not
/// built (the defaults below are what every session runs with), and the particle and volume
/// values wait for the VFX and audio directors that will read them. Nothing here is
/// balance: every value only scales how strongly the game *shows* something, except
/// <see cref="HitStop"/>, which scales a freeze — and a freeze is applied to both ends of a
/// hit alike, so it cannot favour either side.
///
/// Comfort first: these exist so a player who is sensitive to motion or flashing light can
/// turn each one down to nothing and still read the fight.
/// </summary>
public static class FeedbackSettings
{
    private static float _screenShake = 1f;
    private static float _cameraPunch = 1f;
    private static float _flash = 1f;
    private static float _hitStop = 1f;
    private static float _particles = 1f;
    private static float _masterVolume = 1f;
    private static float _musicVolume = 0.8f;
    private static float _sfxVolume = 1f;
    private static float _uiVolume = 0.9f;

    /// <summary>Raised after any value changes.</summary>
    public static event Action Changed;

    /// <summary>Camera shake, kick and roll — the tsunami's rumble included — 0 (none) – 1 (as designed).</summary>
    public static float ScreenShake { get => _screenShake; set => Set(ref _screenShake, value); }
    /// <summary>Camera zoom punches (and, when it exists, slow-motion), 0 (none) – 1 (as designed).</summary>
    public static float CameraPunch { get => _cameraPunch; set => Set(ref _cameraPunch, value); }
    /// <summary>Hit flashes, 0 – 1. At 0 a hit still tints its target (a 35 % tint, never a
    /// full flash): the hit must stay readable.</summary>
    public static float Flash { get => _flash; set => Set(ref _flash, value); }
    /// <summary>Hit-stop length, 0 (off) – 1 (as designed). The one setting that touches the
    /// simulation: it scales how long bodies hold still.</summary>
    public static float HitStop { get => _hitStop; set => Set(ref _hitStop, value); }
    /// <summary>Share of decorative particles drawn, 0.25 – 1. Effects that tell the player
    /// something (telegraphs, status loops) ignore it.</summary>
    public static float Particles { get => _particles; set => Set(ref _particles, Math.Max(0.25f, value)); }

    public static float MasterVolume { get => _masterVolume; set => Set(ref _masterVolume, value); }
    public static float MusicVolume { get => _musicVolume; set => Set(ref _musicVolume, value); }
    public static float SfxVolume { get => _sfxVolume; set => Set(ref _sfxVolume, value); }
    public static float UiVolume { get => _uiVolume; set => Set(ref _uiVolume, value); }

    private static void Set(ref float field, float value)
    {
        float clamped = float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);
        if (Math.Abs(clamped - field) < 0.0005f) return;
        field = clamped;
        Changed?.Invoke();
    }

    /// <summary>Back to the designed defaults (also what a fresh install has).</summary>
    public static void ResetToDefaults()
    {
        _screenShake = 1f; _cameraPunch = 1f; _flash = 1f; _hitStop = 1f; _particles = 1f;
        _masterVolume = 1f; _musicVolume = 0.8f; _sfxVolume = 1f; _uiVolume = 0.9f;
        Changed?.Invoke();
    }
}
