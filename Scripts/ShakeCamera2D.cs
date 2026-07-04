using Godot;

/// <summary>
/// A Camera2D with trauma-based screen shake. Call <see cref="AddTrauma"/> on
/// impacts; the offset scales with trauma² and decays back to zero.
/// </summary>
public partial class ShakeCamera2D : Camera2D
{
    [Export] public float MaxOffset = 14f;
    [Export] public float Decay = 4.5f;

    private float _trauma;

    public void AddTrauma(float amount) => _trauma = Mathf.Clamp(_trauma + amount, 0f, 1f);

    public override void _Process(double delta)
    {
        if (_trauma > 0f)
        {
            _trauma = Mathf.Max(0f, _trauma - Decay * (float)delta);
            float amt = _trauma * _trauma;
            Offset = new Vector2(GD.Randf() * 2f - 1f, GD.Randf() * 2f - 1f) * MaxOffset * amt;
        }
        else if (Offset != Vector2.Zero)
        {
            Offset = Vector2.Zero;
        }
    }
}
