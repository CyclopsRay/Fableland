using Godot;

/// <summary>One pooled visual marker for Pixolotl's temporal route. It is deliberately
/// collision-free; the corresponding frame in Pixolotl owns gameplay state.</summary>
public partial class PapelPicadoGhost : Node2D
{
    private float _age;
    private float _fadeDuration;
    private float _forceFadeRemaining;
    private float _alpha;
    private bool _active;

    public bool Active => _active;

    public void Activate(Vector2 position, float fadeDuration)
    {
        GlobalPosition = position;
        _age = 0f;
        _fadeDuration = Mathf.Max(0.01f, fadeDuration);
        _forceFadeRemaining = 0f;
        _alpha = 0.7f;
        _active = true;
        Visible = true;
        QueueRedraw();
    }

    public void Advance(float actDelta)
    {
        if (!_active || actDelta <= 0f) return;
        if (_forceFadeRemaining > 0f)
        {
            _forceFadeRemaining -= actDelta;
            _alpha = Mathf.Max(0f, _alpha * Mathf.Max(0f, _forceFadeRemaining) / Mathf.Max(0.0001f, _forceFadeRemaining + actDelta));
            if (_forceFadeRemaining <= 0f) Deactivate();
        }
        else
        {
            _age += actDelta;
            float t = Mathf.Clamp(_age / _fadeDuration, 0f, 1f);
            _alpha = Mathf.Lerp(0.7f, 0.3f, t);
        }
        QueueRedraw();
    }

    public void ForceFade(float duration)
    {
        if (!_active) return;
        _forceFadeRemaining = Mathf.Max(0.01f, duration);
    }

    public void Deactivate()
    {
        _active = false;
        Visible = false;
        _forceFadeRemaining = 0f;
    }

    public override void _Draw()
    {
        if (!_active) return;
        Color fill = new Color(0.95f, 0.42f, 0.10f, _alpha);
        Color edge = new Color(1f, 0.80f, 0.28f, _alpha);
        DrawCircle(Vector2.Zero, Units.Px(0.45f), fill);
        DrawArc(Vector2.Zero, Units.Px(0.45f), 0f, Mathf.Tau, 20, edge, 2f);
        DrawCircle(Vector2.Zero, Units.Px(0.13f), new Color(0.25f, 0.08f, 0.18f, _alpha));
    }
}
