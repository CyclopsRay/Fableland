using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Runtime;
using Fableland.Realms;

/// <summary>
/// What the local player sees of realms (`Docs/Realms.gdd` §9, T50 §6). Presentation only: it
/// reads where the player is and draws; gameplay never asks it anything.
///
/// - **Inside a sub-realm** the player's camera shows that space alone: its limits become the
///   space's art (<see cref="RealmSpace.View"/>), its smoothing is reset (no glide across
///   40 000 px), the battlefield's far layers are hidden and the space's shown. Nothing marks the
///   border — the space's art goes on past it, and the camera never looks past the art.
/// - **Outside**, every open sub-realm is seen through a <see cref="RealmWindow"/>: its inside drawn
///   into its region at 1 / ratio.
/// </summary>
public partial class RealmView : Node
{
    private ArenaRealmSpaces _spaces;
    private IReadOnlyList<MapParallaxLayer> _mainParallax = new List<MapParallaxLayer>();
    private Rect2I _mainLimits;
    private Node2D _windowParent;
    private RealmInstance _shown;
    private bool _started;
    private readonly Dictionary<RealmInstance, RealmWindow> _windows = new();

    public static RealmView Attach(Node parent, ArenaRealmSpaces spaces, IReadOnlyList<MapParallaxLayer> mainParallax,
                                   Rect2I mainLimits, Node2D windowParent)
    {
        var view = new RealmView
        {
            Name = "RealmView",
            _spaces = spaces,
            _mainParallax = mainParallax ?? new List<MapParallaxLayer>(),
            _mainLimits = mainLimits,
            _windowParent = windowParent,
        };
        parent.AddChild(view);
        return view;
    }

    /// <summary>The realm whose inside the local player's camera shows (null: the battlefield).</summary>
    public RealmInstance Shown => _shown;

    public override void _Process(double delta)
    {
        CharacterController player = CharacterController.LocalPlayer;
        RealmInstance inside = player != null && IsInstanceValid(player) ? Realm.Of(player) : null;
        if (!_started || inside != _shown) Show(inside, player);
        UpdateWindows(inside);
    }

    private void Show(RealmInstance inside, CharacterController player)
    {
        _started = true;
        _shown = inside;
        // Only layers that follow the camera are switched: one node cannot sit in two views at
        // once. Layers fixed in the world (each map's ground art) stay on — far apart, they never
        // show in the wrong view, and the window draws a space's.
        foreach (MapParallaxLayer layer in _mainParallax)
            if (IsInstanceValid(layer) && layer.FollowsCamera) layer.Visible = inside == null;
        if (_spaces != null)
            foreach (ArenaRealmSpaces.Prepared prepared in _spaces.Spaces)
                foreach (MapParallaxLayer layer in prepared.Parallax)
                    if (IsInstanceValid(layer) && layer.FollowsCamera) layer.Visible = inside != null && prepared.Space == inside.Space;

        Camera2D camera = player?.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera == null) return;
        Rect2 art = inside == null ? new Rect2() : inside.Space.View.HasArea() ? inside.Space.View : inside.Space.Bounds;
        Rect2I limits = inside == null
            ? _mainLimits
            : new Rect2I((Vector2I)art.Position.Floor(), (Vector2I)art.Size.Ceil());
        camera.LimitLeft = limits.Position.X;
        camera.LimitTop = limits.Position.Y;
        camera.LimitRight = limits.End.X;
        camera.LimitBottom = limits.End.Y;
        camera.ResetSmoothing();
    }

    private void UpdateWindows(RealmInstance inside)
    {
        foreach (RealmInstance realm in RealmInstance.Active)
        {
            if (!realm.IsOpen || realm == inside || _windows.ContainsKey(realm)) continue;
            _windows[realm] = RealmWindow.Open(_windowParent ?? (Node2D)GetParent(), realm);
        }
        var gone = new List<RealmInstance>();
        foreach ((RealmInstance realm, RealmWindow window) in _windows)
        {
            bool keep = realm.IsOpen && realm != inside && IsInstanceValid(window);
            if (keep) continue;
            if (IsInstanceValid(window)) window.QueueFree();
            gone.Add(realm);
        }
        foreach (RealmInstance realm in gone) _windows.Remove(realm);
    }
}

/// <summary>
/// A window onto a sub-realm, seen from the battlefield (`Realms.gdd` §9): a second camera on the
/// space, at zoom 1 / ratio, drawn into the region and clipped to its circle — prisoners shown
/// small where they are. A lens (a colour rule) can lie about the inside; until each realm has its
/// own, the window shows it as it is.
/// TODO(art): each realm's lens (TBD #90) and the window's edge.
/// </summary>
public partial class RealmWindow : Node2D
{
    /// <summary>Above the battlefield's art (close view 20, effects 30), under the prison's rim.</summary>
    public const int Z = Realm.WindowZ;

    public RealmInstance Instance { get; private set; }
    private SubViewport _viewport;
    private Camera2D _camera;

    private static Shader _circle;

    public static RealmWindow Open(Node2D parent, RealmInstance realm)
    {
        var window = new RealmWindow { Name = "RealmWindow", Instance = realm, ZIndex = Z, ZAsRelative = false };
        parent.AddChild(window);
        window.GlobalPosition = realm.MainAnchor;
        int size = Mathf.Max(8, Mathf.CeilToInt(realm.RadiusPx * 2f));
        window._viewport = new SubViewport
        {
            Name = "Inside",
            Size = new Vector2I(size, size),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            World2D = window.GetViewport().World2D,
        };
        window.AddChild(window._viewport);
        window._camera = new Camera2D { Name = "Camera", Zoom = Vector2.One / Mathf.Max(0.01f, realm.Ratio), Enabled = true };
        window._viewport.AddChild(window._camera);
        window._camera.GlobalPosition = realm.Space.Anchor;
        _circle ??= new Shader
        {
            Code = "shader_type canvas_item;\nuniform vec4 lens : source_color = vec4(1.0);\n"
                 + "void fragment() {\n  if (length(UV - vec2(0.5)) > 0.5) discard;\n  COLOR = texture(TEXTURE, UV) * lens;\n}\n",
        };
        var picture = new Sprite2D { Name = "Picture", Texture = window._viewport.GetTexture(), Material = new ShaderMaterial { Shader = _circle } };
        window.AddChild(picture);
        return window;
    }

    public override void _Process(double delta)
    {
        if (Instance == null || !Instance.IsOpen) { QueueFree(); return; }
        GlobalPosition = Instance.MainAnchor;
        if (_camera != null) _camera.GlobalPosition = Instance.Space.Anchor;
    }
}
