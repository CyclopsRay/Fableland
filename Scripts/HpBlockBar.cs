using Godot;
using System;

/// <summary>
/// Block-based HP bar that replaces the plain ProgressBar. Divides total HP into
/// 25-HP blocks (like Overwatch) and renders normal HP, temp HP, and shield as
/// differently-colored segments within the same bar.
///
/// Fill order (left to right): normal HP (red) → temp HP (green) → shield (sky blue).
/// Block count adjusts to max(MaxHP, curHP + tempHP + shield) so bonus HP from
/// shield/tempHP expands the bar when it exceeds the base MaxHP.
/// </summary>
public partial class HpBlockBar : Control
{
    private const float HP_PER_BLOCK = 25f;
    private const float BLOCK_GAP = 2f;

    // ── Colors ──
    private static readonly Color NormalColor = new Color(0.85f, 0.20f, 0.15f);   // red
    private static readonly Color TempColor   = new Color(0.20f, 0.85f, 0.20f);   // green
    private static readonly Color ShieldColor = new Color(0.30f, 0.65f, 0.95f);   // sky blue
    private static readonly Color EmptyColor  = new Color(0.10f, 0.10f, 0.10f, 0.85f);
    private static readonly Color BorderColor = new Color(0.22f, 0.22f, 0.22f);

    private float _curHP;
    private float _maxHP = 1f;
    private float _shield;
    private float _tempHP;

    /// <summary>
    /// Push new HP values and schedule a redraw. All values are clamped non-negative.
    /// </summary>
    public void SetValues(float curHP, float maxHP, float shield, float tempHP)
    {
        _curHP   = Mathf.Max(0f, curHP);
        _maxHP   = maxHP > 0f ? maxHP : 1f;
        _shield  = Mathf.Max(0f, shield);
        _tempHP  = Mathf.Max(0f, tempHP);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float barW = Size.X;
        float barH = Size.Y;

        // Effective max — shield/tempHP can push the bar beyond base MaxHP.
        float effectiveMax = Mathf.Max(_maxHP, _curHP + _shield + _tempHP);
        int numBlocks = Mathf.Max(1, Mathf.CeilToInt(effectiveMax / HP_PER_BLOCK));
        float blockW = barW / numBlocks;

        // 1. Draw empty block backgrounds.
        for (int i = 0; i < numBlocks; i++)
        {
            float x = i * blockW;
            DrawRect(new Rect2(x, 0f, blockW - BLOCK_GAP, barH), EmptyColor);
        }

        // 2. Compute segment boundaries in HP space.
        float hpEnd     = Mathf.Min(_curHP, effectiveMax);
        float tempEnd   = Mathf.Min(_curHP + _tempHP, effectiveMax);
        float shieldEnd = Mathf.Min(_curHP + _tempHP + _shield, effectiveMax);

        // 3. Draw filled segments left to right: normal HP → temp HP → shield.
        //    Each segment maps its [startHP, endHP] range in HP space to pixel coords.
        DrawHpSegment(0f,       hpEnd,     effectiveMax, barW, barH, NormalColor);
        DrawHpSegment(hpEnd,    tempEnd,   effectiveMax, barW, barH, TempColor);
        DrawHpSegment(tempEnd,  shieldEnd, effectiveMax, barW, barH, ShieldColor);

        // 4. Draw vertical dividers at block boundaries (on top of fills).
        for (int i = 1; i < numBlocks; i++)
        {
            float x = i * blockW;
            DrawLine(new Vector2(x, 0f), new Vector2(x, barH), BorderColor, 1f);
        }
    }

    /// <summary>
    /// Draw a filled segment of the bar from startHP to endHP (in HP space),
    /// insetting vertically by 1px so the fill sits inside the block edges.
    /// </summary>
    private void DrawHpSegment(float startHP, float endHP, float totalHP,
                               float barW, float barH, Color color)
    {
        if (endHP <= startHP || totalHP <= 0f) return;

        float x1 = startHP / totalHP * barW;
        float x2 = endHP   / totalHP * barW;

        DrawRect(new Rect2(x1, 1f, x2 - x1, barH - 2f), color);
    }
}
