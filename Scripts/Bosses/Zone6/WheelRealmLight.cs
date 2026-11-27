using Godot;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>The light of the realm in force, washed over the whole arena (§9.4). Above the
/// fighters, below the HUD; faint, so it colours the fight without hiding it.</summary>
public partial class WheelRealmLight : Node2D
{
    private readonly WheelOfBecomingField _field;
    public WheelRealmLight() { }
    public WheelRealmLight(WheelOfBecomingField field) => _field = field;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_field == null) return;
        WheelRealmDef def = _field.LightRealm;
        float strength = _field.Liberated ? 0.12f : def.LightA;
        Vector2 origin = _field.GlobalPosition;
        var rect = new Rect2(new Vector2(_field.Left - Units.Px(20f), _field.FloorY - Units.Px(60f)) - origin,
            new Vector2(_field.Right - _field.Left + Units.Px(40f), Units.Px(80f)));
        DrawRect(rect, new Color(def.LightR, def.LightG, def.LightB, strength));
    }
}
