namespace Fableland.MapCreation.Runtime;

using Godot;
using Fableland.MapCreation.Data;

/// <summary>
/// One map layer's artwork at runtime (MapCreation.gdd §1): the layer's tile visuals under a
/// node that applies camera parallax, auto-scroll, and horizontal looping. Presentation
/// only — physics is built separately in fixed world space and never inherits this
/// transform (§3: a collider must not move with the camera).
///
/// The layer follows the viewport's CURRENT camera every frame instead of holding one, so a
/// protagonist switch (which retires one body's Camera2D and enables another's) needs no
/// rebinding. Until <see cref="SetAnchor"/> is called it draws at its authored position.
/// Shared by the editor Playtest and the combat arena; before v0.10.6 only the Playtest had
/// parallax, so far layers sat rigidly behind the battlefield in a real run.
/// </summary>
public partial class MapParallaxLayer : Node2D
{
    private Vector2 _parallax;
    private Vector2 _autoScroll;
    private bool _loop;
    private float _loopWidth;
    private Node2D _singleVisuals;
    private Node2D[] _loopVisuals;
    private double _time;

    private Camera2D _explicitCamera;
    private Vector2 _anchorWorld;
    private bool _anchored;

    /// <summary>Required by Godot for script instantiation; use the data constructor.</summary>
    public MapParallaxLayer() : this(null) { }

    public MapParallaxLayer(MapLayerData data)
    {
        _singleVisuals = new Node2D { Name = "SingleVisuals" };
        AddChild(_singleVisuals);
        if (data == null)
        {
            _parallax = Vector2.One;
            return;
        }

        Name = string.IsNullOrWhiteSpace(data.Name) ? "Layer" : data.Name;
        _parallax = new Vector2(data.ParallaxX, data.ParallaxY);
        _autoScroll = new Vector2(data.AutoScrollX, data.AutoScrollY);
        _loop = data.Loop && data.GridW > 0;
        _loopWidth = data.GridW * MapGrid.PixelsPerCell;

        if (_loop)
        {
            _loopVisuals = new Node2D[3];
            for (int i = 0; i < _loopVisuals.Length; i++)
            {
                _loopVisuals[i] = new Node2D { Name = "LoopCopy" + i };
                AddChild(_loopVisuals[i]);
            }
        }

        // A layer that moves with the battlefield and neither scrolls nor loops never changes.
        SetProcess(_loop || _parallax != Vector2.One || _autoScroll != Vector2.Zero);
    }

    /// <summary>Adds one tile visual. A looping layer repeats ALL of its artwork; the centre
    /// copy keeps the original node and the two neighbours get duplicates.</summary>
    public void AddVisual(Node2D visual)
    {
        if (visual == null) return;
        if (!_loop)
        {
            _singleVisuals.AddChild(visual);
            return;
        }

        for (int i = 0; i < _loopVisuals.Length; i++)
        {
            Node2D copy = i == 1 ? visual : visual.Duplicate() as Node2D;
            if (copy != null) _loopVisuals[i].AddChild(copy);
        }
    }

    /// <summary>The world point at which authored tile positions are visually exact — the
    /// character spawn. With the camera centred there, every layer lines up as painted.</summary>
    public void SetAnchor(Vector2 anchorWorld)
    {
        _anchorWorld = anchorWorld;
        _anchored = true;
    }

    /// <summary>Optional: pin to one camera instead of the viewport's current one.</summary>
    public void SetCamera(Camera2D camera) => _explicitCamera = camera;

    public override void _Process(double delta)
    {
        _time += delta;
        Camera2D camera = _explicitCamera != null && IsInstanceValid(_explicitCamera)
            ? _explicitCamera
            : GetViewport()?.GetCamera2D();
        if (camera == null || !_anchored)
        {
            Position = _autoScroll * (float)_time;
            return;
        }

        // The rendered view centre, including smoothing and shake — never a body position.
        Vector2 cameraWorld = camera.GetScreenCenterPosition();
        Position = (cameraWorld - _anchorWorld) * (Vector2.One - _parallax) + _autoScroll * (float)_time;
        if (!_loop || _loopWidth <= 0f) return;

        // Screen space is tile + copyOffset - (camera*p + anchor*(1-p)). Centre the three
        // visual copies around that same anchored parallax-space coordinate.
        float loopCenter = cameraWorld.X * _parallax.X + _anchorWorld.X * (1f - _parallax.X) -
            _autoScroll.X * (float)_time;
        int centerRepeat = Mathf.FloorToInt(loopCenter / _loopWidth);
        for (int i = 0; i < _loopVisuals.Length; i++)
            _loopVisuals[i].Position = new Vector2((centerRepeat + i - 1) * _loopWidth, 0f);
    }
}
