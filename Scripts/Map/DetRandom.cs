using System.Collections.Generic;

namespace Fableland.Map;

/// <summary>
/// Deterministic PRNG. ALL randomness in the game must flow through a DetRandom
/// created from the 8-character run seed, so a given seed always yields the exact
/// same world. Seed string is folded to a 64-bit state (FNV-1a) and advanced with
/// xorshift64*. Do not use Godot's global RNG or System.Random for gameplay — those
/// aren't reproducible from the seed.
/// </summary>
public sealed class DetRandom
{
    private ulong _state;
    private readonly string _seed;

    public DetRandom(string seed)
    {
        _seed = seed ?? "";
        ulong h = 1469598103934665603UL; // FNV offset basis
        foreach (char c in _seed)
        {
            h ^= (byte)c;
            h *= 1099511628211UL; // FNV prime
        }
        _state = h == 0 ? 0x9E3779B97F4A7C15UL : h;
    }

    /// <summary>
    /// A derived, independent stream for a subsystem — deterministic from this stream's seed
    /// plus a tag (e.g. <c>Rng.Sub("items")</c>). Deriving by seed string (not by advancing the
    /// parent) means one subsystem consuming numbers never shifts another's — the whole reason
    /// map layout stays identical when a new subsystem is added (KNOWLEDGE: determinism rule).
    /// </summary>
    public DetRandom Sub(string tag) => new(_seed + ":" + tag);

    private ulong NextU()
    {
        // xorshift64* — state is guaranteed non-zero by the ctor.
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextU() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Next raw 64-bit draw — used to seed per-foe <c>RandomNumberGenerator</c>s
    /// deterministically from this stream (Phase 3 arena plumbing).</summary>
    public ulong NextULong() => NextU();

    /// <summary>True with probability p (p in [0,1]).</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Integer in [minInclusive, maxInclusive].</summary>
    public int Range(int minInclusive, int maxInclusive)
    {
        int span = maxInclusive - minInclusive + 1;
        return minInclusive + (int)(NextU() % (ulong)span);
    }

    public T Pick<T>(IList<T> list) => list[(int)(NextU() % (ulong)list.Count)];

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(NextU() % (ulong)(i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Generate a fresh random run seed: 8 chars from [0-9A-Z]. Not seed-derived (used by the dice button).</summary>
    public static string NewSeed()
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var rng = new System.Random();
        var chars = new char[8];
        for (int i = 0; i < 8; i++) chars[i] = alphabet[rng.Next(alphabet.Length)];
        return new string(chars);
    }
}
