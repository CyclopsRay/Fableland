using System.Collections.Generic;
using Godot;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>A small wheel at the top right of the screen: the realm in force under the
/// fangs, the time to the next turn, the realm's name. The colossus behind the arena can be
/// out of view; this never is. NOTE(art): placeholder until the HUD is designed.</summary>
public partial class WheelIndicator : Control
{
    private const float Radius = 38f;
    private readonly WheelOfBecomingField _field;
    private Label _label;

    public WheelIndicator() { }
    public WheelIndicator(WheelOfBecomingField field) => _field = field;

    public override void _Ready()
    {
        AnchorLeft = 1f;
        AnchorRight = 1f;
        OffsetLeft = -230f;
        OffsetRight = -20f;
        OffsetTop = 64f;
        OffsetBottom = 200f;
        MouseFilter = MouseFilterEnum.Ignore;
        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            OffsetTop = 2f * Radius + 14f,
            OffsetBottom = 2f * Radius + 54f,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (_field?.Wheel == null) return;
        WheelOfBecoming wheel = _field.Wheel;
        WheelRealmDef def = _field.LightRealm;
        string state = _field.Liberated ? "the wheel stops"
            : wheel.InLastTurn ? "the Last Turn"
            : wheel.Still ? $"{def.Title} — the Interval, for good"
            : def.IsInterval ? $"{def.Title} — the Interval · {wheel.UntilTurn:0} s"
            : $"{def.Title} — {def.Power} · {wheel.UntilTurn:0} s";
        _label.Text = state;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_field?.Wheel == null) return;
        var centre = new Vector2(Size.X * 0.5f, Radius + 8f);
        float baseAngle = -Mathf.Pi * 0.5f - _field.AngleSegments * Mathf.Tau / WheelTable.Count;
        DrawCircle(centre, Radius + 3f, new Color(0.04f, 0.04f, 0.06f, 0.85f));
        for (int i = 0; i < WheelTable.Count; i++)
        {
            WheelRealmDef def = WheelTable.Realms[i];
            float a0 = baseAngle + (i - 0.5f) * Mathf.Tau / WheelTable.Count;
            float a1 = a0 + Mathf.Tau / WheelTable.Count;
            Color fill = _field.IsSealed(def.Realm) ? new Color(0.16f, 0.16f, 0.18f)
                : def.IsInterval ? new Color(0.98f, 0.92f, 0.78f)
                : new Color(def.SigilR, def.SigilG, def.SigilB);
            var wedge = new List<Vector2> { centre };
            for (int s = 0; s <= 6; s++)
            {
                float a = Mathf.Lerp(a0, a1, s / 6f);
                wedge.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius);
            }
            DrawColoredPolygon(wedge.ToArray(), fill);
            DrawLine(centre, centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * Radius, new Color(0.95f, 0.78f, 0.3f), 1.5f);
        }
        DrawArc(centre, Radius, 0f, Mathf.Tau, 40, new Color(0.95f, 0.78f, 0.3f), 2f);
        // The fangs: a gold notch over the top segment.
        DrawColoredPolygon(new[] { centre + new Vector2(-7f, -Radius - 9f), centre + new Vector2(7f, -Radius - 9f), centre + new Vector2(0f, -Radius + 4f) },
            new Color(0.95f, 0.93f, 0.85f));
        // The time to the next turn, as an arc that empties.
        WheelOfBecoming wheel = _field.Wheel;
        if (!_field.Liberated && !wheel.Still && !wheel.InLastTurn && wheel.RestTotal > 0f)
        {
            float left = Mathf.Clamp(wheel.UntilTurn / wheel.RestTotal, 0f, 1f);
            DrawArc(centre, Radius + 6f, -Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f + Mathf.Tau * left, 32,
                wheel.Telling ? new Color(1f, 0.85f, 0.4f) : new Color(0.85f, 0.85f, 0.85f, 0.8f), 3f);
        }
    }
}
