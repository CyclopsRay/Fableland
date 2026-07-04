using Godot;

/// <summary>
/// A stackable, self-decaying "points" debuff shared by hazard-driven statuses
/// (Fire's "OnFire", Freeze's "Frozen"): every <see cref="TickInterval"/> seconds,
/// 10% of the current stack (rounded up, kept integer) burns off as damage. A new
/// hit adds flat points on top of whatever is left, so repeated exposure stacks.
/// </summary>
public class DecayingDebuff
{
    private const float TickInterval = 0.2f;
    private const float DecayFraction = 0.1f;

    public float Stack { get; private set; }
    public bool Active => Stack > 0f;

    private float _timer;

    public void AddStack(float amount) => Stack = Mathf.Round(Stack + amount);

    /// <summary>Advance the tick clock; returns the damage dealt this call (0 if none).
    /// Loops in case <paramref name="dt"/> spans more than one tick.</summary>
    public float Tick(float dt)
    {
        if (Stack <= 0f) { _timer = 0f; return 0f; }

        _timer += dt;
        float damage = 0f;
        while (_timer >= TickInterval && Stack > 0f)
        {
            _timer -= TickInterval;
            float reduction = Mathf.Min(Stack, Mathf.Ceil(Stack * DecayFraction));
            Stack -= reduction;
            damage += reduction;
        }
        return damage;
    }

    public void Clear()
    {
        Stack = 0f;
        _timer = 0f;
    }
}
