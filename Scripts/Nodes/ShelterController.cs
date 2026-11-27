using System.Collections.Generic;
using Godot;
using Fableland.Run;
using Fableland.Items;
using Fableland.Debug;
using Fableland.Data;
using Fableland.Map;
using Fableland.Shelter;
using Fableland.UI;

/// <summary>
/// The shelter Adventure scene (NODES §5): a small room the player walks through as the
/// team's lead fighter — the fighter's own walk and jump, no attacks — using a station for
/// each action with the interact key (§5.0): the road out, the team board (Team Build), the
/// lantern (Finish the Day), and the campfire, whetstone and anvil (the Blessing: Rest,
/// Sharpen Weapon, Sharpen Armor). Which stations stand where is data (<see cref="ShelterTable"/>,
/// one room per world, else the default); what each does is a behaviour
/// (<see cref="ShelterStations"/>). Blessings remain day-ending actions while Team Build is a
/// free management action; Team Build keeps the editable next-battle order inside RunState,
/// and an arena receives only its frozen battle snapshot.
/// </summary>
public partial class ShelterController : Node2D
{
    private const string BlessingConfirm =
        "You will use all your stamina and the Blessing of this shelter to make the magic happen.";

    private Node2D _world;
    private CanvasLayer _ui;
    private Label _title;
    private Label _status;
    private PanelContainer _promptBox;
    private Label _prompt;
    private Label _promptReason;
    private Label _keys;

    private string _nodeId;
    private ShelterKind _kind;
    private ShelterLayoutDef _layout;
    private Color _accent;
    private ShelterRoom _room;
    private readonly List<ShelterStationNode> _stations = new();
    private ShelterVisit _visit;

    private CharacterController _walker;
    private string _walkerId;
    private ICharacterControls _walkerSource;    // the player's keys, or a check's pad
    private ShelterWalkControls _controls;
    private ShelterStationNode _current;         // the station the walker stands at
    private readonly HashSet<Node> _modals = new();
    private bool _leaving;

    private bool _isVoidShelter => _kind == ShelterKind.Last;
    private bool _isEidolonShelter => _kind == ShelterKind.Eidolon;

    // ---- Team Build window ----
    private enum TeamBuildMode { Overview, ProtagonistPicker, ItemPicker }

    private Panel _teamBuildPanel;
    private Control _teamBuildContent;
    private Label _teamBuildStatus;
    private Button _teamBuildBack;
    private TeamBuildMode _teamBuildMode;
    private int _selectedTeamSlot = -1;
    private string _teamBuildMessage;

    /// <summary>Ephemeral, debug-only states for implemented bodies not yet granted in the run.
    /// They are never written into Owned or ActiveBuild.</summary>
    private readonly Dictionary<string, ProtagonistState> _debugStates = new();
    private static readonly string[] KnownProtagonistIds =
    {
        "Pomegraknight", "PumpKing", "Cleopastar", "Pixolotl", "Sifu Pangda",
    };

    // ---- read-only, for checks ----
    public ShelterKind Kind => _kind;
    public ShelterLayoutDef Layout => _layout;
    public IReadOnlyList<ShelterStationNode> Stations => _stations;
    public ShelterStationNode CurrentStation => _current;
    public CharacterController Walker => _walker;
    public ShelterVisit Visit => _visit;
    public bool WalkerFrozen => _controls?.Frozen ?? false;
    public string PromptText => _promptBox != null && _promptBox.Visible ? _prompt.Text : "";
    public string StatusText => _status?.Text ?? "";
    public bool TeamBuildOpen => _teamBuildPanel?.Visible ?? false;

    /// <summary>The station built for this id, or null (not offered here, or not built).</summary>
    public ShelterStationNode Station(string id) => _stations.Find(s => s.Def.Id == id && s.Usable);

    public override void _Ready()
    {
        InputDefaults.Ensure();   // the interact key ships with this code (InputDefaults)
        _world = GetNode<Node2D>("World");
        _ui = GetNode<CanvasLayer>("Ui");
        BuildHud();

        var rs = RunState.Instance;
        _nodeId = rs?.CurrentAdventure?.NodeId ?? "(debug)";
        MapNode node = rs?.FindNode(_nodeId);
        _kind = KindOf(node);
        _layout = ShelterTable.ForWorld(node?.Zone);
        // The world's map colour; past the singularity a drained violet; with no run the home's pink.
        _accent = WorldDef.ByAbbr(node?.Zone)?.Color
            ?? (_kind == ShelterKind.Last ? new Color(0.30f, 0.24f, 0.42f) : new Color(0.95f, 0.55f, 0.75f));

        BuildRoom();
        BuildTeamBuildPanel();
        _walkerSource = PlayerControls.Instance;
        _controls = new ShelterWalkControls(_walkerSource);
        EnsureWalker(SpawnPoint);
        Refresh();

        // flow/shelter.enter: the shelter's screen is up. Tag = which kind of shelter.
        UiCues.Attach(this);
        CueBus.EmitAt(CueOwners.Flow, "shelter.enter", Vector2.Zero, variant: rs?.Day ?? 0,
            tag: _isEidolonShelter ? "eidolon" : _isVoidShelter ? "void" : "hub");
    }

    /// <summary>Which of the three kinds a shelter node is (NODES §5): a bridge's midpoint is an
    /// Eidolon Shelter (it is a <see cref="NodeKind.Shelter"/> too, so the bridge is asked
    /// first); any other Shelter node is the zone-6 Last Shelter; everything else a hub.</summary>
    public static ShelterKind KindOf(MapNode node) =>
        !string.IsNullOrWhiteSpace(node?.RealityBridgeId) ? ShelterKind.Eidolon
        : node?.Kind == NodeKind.Shelter ? ShelterKind.Last
        : ShelterKind.Hub;

    // ============================================================= the room

    private void BuildRoom()
    {
        _room = ShelterRoom.Build(_layout, _accent, _kind);
        _world.AddChild(_room);
        foreach (ShelterSpot spot in _layout.Spots)
        {
            ShelterStationDef def = ShelterTable.Station(spot.StationId);
            if (def == null || !def.StandsIn(_kind)) continue;
            // A station with no behaviour is not built: its spot waits (an outline in debug mode).
            var station = ShelterStationNode.Create(def, ShelterStations.Get(def.Id), Units.Px(spot.XM), _accent);
            _world.AddChild(station);
            _stations.Add(station);
        }
    }

    /// <summary>The title, the status line, the prompt for the station in reach and the keys —
    /// built here, over the room. Nothing in it takes the mouse: walking is the keyboard's.</summary>
    private void BuildHud()
    {
        var top = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        top.OffsetTop = 14f;
        top.AddThemeConstantOverride("separation", 4);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 26);
        _title.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        _title.AddThemeConstantOverride("outline_size", 6);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        _status.AddThemeConstantOverride("outline_size", 5);
        top.AddChild(_title);
        top.AddChild(_status);
        _ui.AddChild(top);

        _promptBox = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _promptBox.AddThemeStyleboxOverride("panel", MakePanelStyle(new Color(0.07f, 0.07f, 0.10f, 0.88f), new Color(0.78f, 0.68f, 0.39f), 10, 2));
        // Under the status line, over the wall: never over the floor, where the walker and the
        // stations are (the first capture had it hiding both).
        _promptBox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _promptBox.GrowHorizontal = Control.GrowDirection.Both;
        _promptBox.OffsetTop = 96f;
        _promptBox.OffsetBottom = 160f;
        _promptBox.OffsetLeft = -330f;
        _promptBox.OffsetRight = 330f;
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _prompt = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _prompt.AddThemeFontSizeOverride("font_size", 18);
        _promptReason = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _promptReason.AddThemeColorOverride("font_color", new Color(0.95f, 0.62f, 0.45f));
        box.AddChild(_prompt);
        box.AddChild(_promptReason);
        _promptBox.AddChild(box);
        _ui.AddChild(_promptBox);

        _keys = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _keys.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _keys.GrowVertical = Control.GrowDirection.Begin;
        _keys.OffsetLeft = 16f;
        _keys.OffsetTop = -36f;
        _keys.OffsetBottom = -12f;
        _keys.AddThemeColorOverride("font_color", new Color(0.92f, 0.90f, 0.84f));
        _keys.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        _keys.AddThemeConstantOverride("outline_size", 5);
        _keys.Text = $"{ControlsCard.KeysOf("move_left")} · {ControlsCard.KeysOf("move_right")} walk   " +
                     $"{ControlsCard.KeysOf("jump")} jump   {ControlsCard.KeysOf(InputDefaults.Interact)} use a station";
        _ui.AddChild(_keys);
    }

    /// <summary>The first key bound to an action, as a player reads it ("E").</summary>
    private static string FirstKey(string action) => ControlsCard.FirstKeyOf(action);

    // ============================================================= the walker

    /// <summary>Where the walker arrives: the layout's spawn, a little above the floor.</summary>
    private Vector2 SpawnPoint => new(Units.Px(_layout.SpawnXM), -Units.Px(1.2f));

    /// <summary>Where a protagonist who is not on the roster can be recruited in this run
    /// (NODES §3.1) — the line under a locked face in Team Build.</summary>
    public static string RecruitHint(string id)
    {
        WorldDef home = WorldDef.HomeOf(id);
        if (home == null) return "Not recruitable yet.";
        bool inRun = RunState.Instance?.Graph?.Worlds.Exists(world => world.Abbr == home.Abbr) == true;
        return inRun
            ? $"Not recruited yet — beat the boss of {home.Name} (its 4-1) and {id} joins you."
            : $"Not recruited yet — {home.Name} is not one of this run's worlds.";
    }

    /// <summary>The team's lead walks the room: the fighter who would open the next battle.
    /// With no run (F5 on this scene) the debug page's pick, else the first protagonist.</summary>
    private static string LeadId()
    {
        BattleTeamSnapshot team = RunState.Instance?.Graph != null ? RunState.Instance.CaptureBattleTeam() : null;
        if (team != null && team.Count > 0) return team.MemberIds[team.InitialIndex];
        return DebugManager.Instance?.SelectedProtagonistId ?? ProtagonistRoster.Entries[0].Id;
    }

    /// <summary>Puts the lead in the room — again after Team Build changes who leads, in the
    /// same spot. The body is placed before it enters the tree, so it latches its own spawn
    /// and its camera does not sweep in from the origin.</summary>
    private void EnsureWalker(Vector2 at)
    {
        string lead = LeadId();
        bool alive = _walker != null && IsInstanceValid(_walker);
        if (alive && _walkerId == lead) return;
        if (alive)
        {
            at = _walker.GlobalPosition;
            _world.RemoveChild(_walker);
            _walker.QueueFree();
            _walker = null;
        }
        PackedScene scene = ProtagonistRoster.GetScene(lead) ?? ProtagonistRoster.GetScene(ProtagonistRoster.Entries[0].Id);
        if (scene == null)
        {
            GD.PushError($"[Shelter] no scene for the lead '{lead}'; the room has no walker.");
            return;
        }
        var body = scene.Instantiate<CharacterController>();
        body.Position = at;
        body.UseControls(_controls);
        _world.AddChild(body);
        _walker = body;
        _walkerId = lead;
        FitCamera(body);
    }

    /// <summary>The fighter's own camera, held to the room: it follows along the floor and
    /// never scrolls up or down (the room is shorter than the screen).</summary>
    private void FitCamera(CharacterController body)
    {
        Camera2D camera = body.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera == null) return;
        float viewHeight = GetViewportRect().Size.Y;
        camera.LimitLeft = -(int)Units.Px(1f);
        camera.LimitRight = (int)(_room.WidthPx + Units.Px(1f));
        camera.LimitBottom = (int)Units.Px(2.5f);
        camera.LimitTop = camera.LimitBottom - (int)Mathf.Max(viewHeight, _room.HeightPx + Units.Px(4f));
        camera.MakeCurrent();
        camera.ResetSmoothing();
    }

    /// <summary>Checks drive the walker with their own pad (the player's keys otherwise).</summary>
    public void UseWalkerSource(ICharacterControls source)
    {
        _walkerSource = source ?? PlayerControls.Instance;
        bool frozen = _controls?.Frozen ?? false;
        _controls = new ShelterWalkControls(_walkerSource) { Frozen = frozen };
        if (_walker != null && IsInstanceValid(_walker)) _walker.UseControls(_controls);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_walker == null || !IsInstanceValid(_walker)) return;
        ShelterStationNode reach = StationAt(_walker.GlobalPosition.X);
        if (reach == _current) return;
        _current = reach;
        if (_current != null && _current.Offer.Enabled)
            CueBus.EmitAt(CueOwners.Ui, "hover", _current.GetGlobalTransformWithCanvas().Origin, size: _current.WidthPx);
        ShowStations();
    }

    /// <summary>The usable station whose stretch of floor the walker stands on (the nearest
    /// centre if two could reach — the table forbids it, the rule is still total).</summary>
    public ShelterStationNode StationAt(float x)
    {
        ShelterStationNode best = null;
        foreach (ShelterStationNode station in _stations)
            if (station.Usable && station.InReach(x)
                && (best == null || Mathf.Abs(x - station.Position.X) < Mathf.Abs(x - best.Position.X)))
                best = station;
        return best;
    }

    // ============================================================= using a station

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsEcho()) return;
        if (@event.IsActionPressed("ui_cancel") && TeamBuildOpen)
        {
            CloseTeamBuild();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed(InputDefaults.Interact) && TryUseStation())
            GetViewport().SetInputAsHandled();
    }

    /// <summary>Use the station in reach — what the interact key does. False when nothing is in
    /// reach, it is closed, a dialog or panel is open, or the screen is already leaving.</summary>
    public bool TryUseStation()
    {
        if (_leaving || _modals.Count > 0 || _current == null || !_current.Usable) return false;
        Refresh();
        if (!_current.Offer.Enabled)
        {
            _promptReason.Modulate = new Color(1.4f, 1.4f, 1.4f);
            CreateTween().TweenProperty(_promptReason, "modulate", Colors.White, 0.35f);
            return false;
        }
        // ui/press (ui/back for the way out), as a button raises it: at its place on screen.
        CueBus.EmitAt(CueOwners.Ui, _current.Behaviour.LeavesScreen ? "back" : "press", _current.GetGlobalTransformWithCanvas().Origin, size: _current.WidthPx);
        _current.Behaviour.Use(_visit);
        return true;
    }

    /// <summary>Recompute what this visit offers — the one rule, <see cref="Exits"/>, for the
    /// ways out — and show it on the stations, the status line and the prompt.</summary>
    private void Refresh()
    {
        var rs = RunState.Instance;
        bool blessed = rs?.IsShelterBlessed(_nodeId) ?? true;
        int stamina = rs?.Stamina ?? RunState.MaxStamina;

        // An Eidolon Shelter's only exit is its Blessing, which carries you across. Once the
        // crossing is spent — or can no longer happen (the far shore devoured) — the road opens:
        // the map always has Finish the Day, so this room can never be a dead end (#16).
        string blocked = null;
        bool canCross = _isEidolonShelter && blessed && rs != null && rs.CanCrossEidolonShelter(_nodeId, out blocked);
        _visit = new ShelterVisit
        {
            Shelter = this,
            Run = rs,
            NodeId = _nodeId,
            Kind = _kind,
            Blessed = blessed,
            CanCross = canCross,
            CrossBlocked = blocked,
            Stamina = stamina,
            Exits = Exits(_isEidolonShelter, blessed, canCross, stamina),
        };

        WorldDef world = WorldDef.ByAbbr(rs?.FindNode(_nodeId)?.Zone);
        _title.Text = _isEidolonShelter ? "Eidolon Shelter"
            : _isVoidShelter ? $"Last Shelter {_nodeId}"
            : $"Transportation Hub {_nodeId}{(world != null ? " · " + world.Name : "")}";
        _status.Text = _isEidolonShelter
            ? (canCross ? "Spend this shelter's Blessing at the campfire, whetstone or anvil to cross to the far shore."
                : blessed ? $"{blocked ?? "The bridge cannot be crossed."} Team Build, or take the road back to the map."
                : "The bridge has been crossed: Team Build, or take the road back to the map.")
            : _isVoidShelter
                ? "The singularity has closed behind you. Rest, sharpen, or rebuild before the VOID."
                : (blessed ? "Blessed — one day-ending action available." : "Mundane — the Blessing is spent.");

        foreach (ShelterStationNode station in _stations)
            if (station.Usable) station.Show(station.Behaviour.Offer(_visit), station == _current, FirstKey(InputDefaults.Interact));
        ShowTeamFaces();
        ShowPrompt();
    }

    /// <summary>Re-show the stations without recomputing the visit (the walker moved).</summary>
    private void ShowStations()
    {
        if (_visit == null) return;
        foreach (ShelterStationNode station in _stations)
            if (station.Usable) station.Show(station.Offer, station == _current, FirstKey(InputDefaults.Interact));
        ShowPrompt();
    }

    private void ShowPrompt()
    {
        if (_current == null || !_current.Usable)
        {
            _promptBox.Visible = false;
            return;
        }
        StationOffer offer = _current.Offer;
        _promptBox.Visible = true;
        _prompt.Text = offer.Enabled ? $"[{FirstKey(InputDefaults.Interact)}]  {_current.Def.Name}: {offer.Line}" : $"{_current.Def.Name}: {offer.Line}";
        _prompt.Modulate = offer.Enabled ? Colors.White : new Color(0.7f, 0.7f, 0.72f);
        _promptReason.Text = offer.Enabled ? "" : offer.Reason;
        _promptReason.Visible = !offer.Enabled && !string.IsNullOrEmpty(offer.Reason);
    }

    /// <summary>The team board shows the next battle's team, by face.</summary>
    private void ShowTeamFaces()
    {
        ShelterStationNode board = _stations.Find(s => s.Def.Id == ShelterTable.TeamBoard);
        if (board == null) return;
        var faces = new List<Texture2D>();
        string[] team = RunState.Instance?.Graph != null ? RunState.Instance.CaptureBattleTeam().MemberIds : new[] { LeadId() };
        foreach (string id in team) faces.Add(LoadTexture(MugshotPath(id)));
        board.ShowFaces(faces);
    }

    /// <summary>
    /// Which ways out a shelter offers (NODES §1.2) — the one rule; the stations only show it.
    /// A hub: the Blessing's actions while blessed, Finish the Day, and Back while stamina lasts.
    /// An Eidolon Shelter: its Blessing is its exit — it carries you across — so Back and
    /// Finish are closed while it can; once it is spent, or the crossing can no longer happen,
    /// Back opens, because the map always has Finish the Day (audit v0.11.1 #16). Pure: the
    /// check runs every case.
    /// </summary>
    public static (bool Back, bool Finish, bool Blessings) Exits(bool eidolon, bool blessed, bool canCross, int stamina) =>
        eidolon
            ? (Back: !canCross, Finish: false, Blessings: blessed && canCross)
            : (Back: stamina > 0, Finish: true, Blessings: blessed);

    // ---- what stations ask of the screen (ShelterStations) ----

    /// <summary>A Blessing (Rest, Sharpen Weapon, Sharpen Armor), confirmed: at a hub or the
    /// Last Shelter it uses all stamina and ends the day; at an Eidolon Shelter it carries the
    /// player across the bridge without ending the day.</summary>
    public void RequestBlessing(string effectLine, System.Action apply)
    {
        if (_isEidolonShelter)
        {
            ConfirmWithoutEnding($"{effectLine}\n\nSpend this shelter's Blessing and cross the Bridge of Eidolon?", () =>
            {
                var rs = RunState.Instance;
                string reason = null;
                bool crossed = rs != null && rs.TryCrossEidolonShelter(_nodeId, out reason);
                if (!crossed)
                {
                    _status.Text = reason ?? "The bridge cannot be crossed.";
                    return;
                }
                _leaving = true;
                apply?.Invoke();
            });
            return;
        }

        Confirm($"{effectLine}\n\n{BlessingConfirm}", () =>
        {
            apply?.Invoke();
            RunState.Instance?.ConsumeBlessing(_nodeId);
        });
    }

    /// <summary>The lantern: "Finish the Day?" — the day ends on Yes.</summary>
    public void RequestFinishDay() => Confirm("Finish the Day?", null);

    /// <summary>The road: back to the map, whenever the one rule opens it — at an Eidolon Shelter
    /// with no stamina too (the map has Finish the Day; the old Back button refused there, and
    /// the screen was a dead end).</summary>
    public void RequestLeave()
    {
        var rs = RunState.Instance;
        if (rs == null || _visit == null || !_visit.Exits.Back) return;
        _leaving = true;
        rs.ReturnToMap();
    }

    /// <summary>A line on the status bar (a station's own news).</summary>
    public void Say(string text) => _status.Text = text ?? "";

    /// <summary>Shows a station's own panel over the room — the walker stands still until it is
    /// hidden or freed. For stations that bring a screen of their own (the plantation).</summary>
    public void ShowPanel(Control panel)
    {
        if (panel == null) return;
        if (panel.GetParent() == null) _ui.AddChild(panel);
        panel.Visible = true;
        OpenModal(panel);
        panel.VisibilityChanged += () => { if (!panel.Visible) CloseModal(panel); };
        panel.TreeExiting += () => CloseModal(panel);
    }

    private void OpenModal(Node owner)
    {
        _modals.Add(owner);
        if (_controls != null) _controls.Frozen = true;
    }

    private void CloseModal(Node owner)
    {
        if (!_modals.Remove(owner)) return;
        if (_controls != null) _controls.Frozen = _modals.Count > 0;
        if (!_leaving && IsInsideTree() && !IsQueuedForDeletion()) Refresh();
    }

    private void Confirm(string text, System.Action onYes)
    {
        var dlg = new ConfirmationDialog { DialogText = text, Title = "Shelter" };
        _ui.AddChild(dlg);
        OpenModal(dlg);
        dlg.TreeExiting += () => CloseModal(dlg);
        dlg.Confirmed += () =>
        {
            dlg.QueueFree();
            _leaving = true;
            onYes?.Invoke();
            RunState.Instance?.EndDay();
        };
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered();
    }

    private void ConfirmWithoutEnding(string text, System.Action onYes)
    {
        var dlg = new ConfirmationDialog { DialogText = text, Title = "Eidolon Shelter" };
        _ui.AddChild(dlg);
        OpenModal(dlg);
        dlg.TreeExiting += () => CloseModal(dlg);
        dlg.Confirmed += () => { dlg.QueueFree(); onYes?.Invoke(); };
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered();
    }

    /// <summary>The dialog a station opened, if one is up (read-only, for checks).</summary>
    public ConfirmationDialog OpenDialog()
    {
        foreach (Node child in _ui.GetChildren())
            if (child is ConfirmationDialog dlg && !dlg.IsQueuedForDeletion()) return dlg;
        return null;
    }

    // ============================================================= Team Build

    private void BuildTeamBuildPanel()
    {
        _teamBuildPanel = new Panel { Visible = false };
        _teamBuildPanel.AddThemeStyleboxOverride("panel", MakePanelStyle(new Color(0.055f, 0.06f, 0.11f, 0.98f), new Color(0.78f, 0.68f, 0.39f), 12, 2));

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 22);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        _teamBuildPanel.AddChild(margin);

        var layout = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        layout.AddThemeConstantOverride("separation", 12);
        margin.AddChild(layout);

        var titleRow = new HBoxContainer();
        var title = new Label { Text = "TEAM BUILD", VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        titleRow.AddChild(title);

        var help = new Button
        {
            Text = "?",
            TooltipText = "Team Build\n\nClick a poster to choose an owned protagonist for that slot. Click an item bubble to equip from the backpack. A grey tilted choice is already equipped or fielded; selecting it swaps places. Pick the current choice to cancel.\n\nThe team order is used for the next battle only.",
            CustomMinimumSize = new Vector2(30, 30),
        };
        help.AddThemeStyleboxOverride("normal", MakePanelStyle(new Color(0.18f, 0.18f, 0.26f), new Color(0.75f, 0.69f, 0.42f), 15, 1));
        titleRow.AddChild(help);

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleRow.AddChild(spacer);
        _teamBuildBack = new Button
        {
            Text = "←",
            TooltipText = "Back to Team Build overview",
            CustomMinimumSize = new Vector2(36, 30),
            Visible = false,
        };
        _teamBuildBack.Pressed += BackToTeamOverview;
        titleRow.AddChild(_teamBuildBack);
        var close = new Button { Text = "×", TooltipText = "Close Team Build", CustomMinimumSize = new Vector2(36, 30) };
        close.Pressed += CloseTeamBuild;
        titleRow.AddChild(close);
        layout.AddChild(titleRow);

        _teamBuildContent = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        layout.AddChild(_teamBuildContent);
        _teamBuildStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _teamBuildStatus.AddThemeColorOverride("font_color", new Color(0.74f, 0.77f, 0.88f));
        layout.AddChild(_teamBuildStatus);
        _ui.AddChild(_teamBuildPanel);
    }

    /// <summary>The team board's action. The walker stands still while it is open.</summary>
    public void OpenTeamBuild()
    {
        _teamBuildMode = TeamBuildMode.Overview;
        _selectedTeamSlot = -1;
        _teamBuildMessage = null;
        RepositionTeamBuildPanel();
        RebuildTeamBuildView();
        _teamBuildPanel.Visible = true;
        OpenModal(_teamBuildPanel);
    }

    /// <summary>Closes Team Build; a new lead walks on from where the old one stood.</summary>
    public void CloseTeamBuild()
    {
        _teamBuildPanel.Visible = false;
        _teamBuildMode = TeamBuildMode.Overview;
        _selectedTeamSlot = -1;
        EnsureWalker(SpawnPoint);
        CloseModal(_teamBuildPanel);
    }

    private void RepositionTeamBuildPanel()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float width = Mathf.Min(viewport.X * 0.80f, 1040f);
        float height = Mathf.Min(viewport.Y * 0.80f, 720f);
        _teamBuildPanel.Position = new Vector2((viewport.X - width) / 2f, (viewport.Y - height) / 2f);
        _teamBuildPanel.Size = new Vector2(width, height);
    }

    private void RebuildTeamBuildView()
    {
        foreach (Node child in _teamBuildContent.GetChildren())
        {
            if (child is CanvasItem canvasItem) canvasItem.Visible = false;
            child.QueueFree();
        }
        if (_teamBuildMode == TeamBuildMode.Overview) BuildTeamOverview();
        else BuildSelectionView();
        _teamBuildBack.Visible = _teamBuildMode != TeamBuildMode.Overview;

        _teamBuildContent.Modulate = new Color(1f, 1f, 1f, 0f);
        CreateTween().TweenProperty(_teamBuildContent, "modulate:a", 1f, 0.16f);
    }

    private void BuildTeamOverview()
    {
        var layout = new VBoxContainer();
        layout.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 10);
        _teamBuildContent.AddChild(layout);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var slots = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        slots.AddThemeConstantOverride("separation", 20);
        scroll.AddChild(slots);
        layout.AddChild(scroll);

        var rs = RunState.Instance;
        string[] teamIds = rs?.CaptureBattleTeam()?.MemberIds ?? System.Array.Empty<string>();
        int partyCap = rs?.PartyCap() ?? 3;
        for (int slot = 0; slot < partyCap; slot++)
        {
            string id = slot < teamIds.Length ? teamIds[slot] : null;
            int capturedSlot = slot;
            slots.AddChild(CreateTeamCard(id, capturedSlot, true));
        }

        _teamBuildStatus.Text = _teamBuildMessage ?? "";
    }

    private void BuildSelectionView()
    {
        var root = new HBoxContainer();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 24);
        _teamBuildContent.AddChild(root);

        string selectedId = SelectedTeamMemberId();
        var selectedColumn = new VBoxContainer { CustomMinimumSize = new Vector2(168, 0) };
        selectedColumn.AddChild(new Label { Text = _teamBuildMode == TeamBuildMode.ProtagonistPicker ? "Selected slot" : "Selected holder" });
        selectedColumn.AddChild(CreateTeamCard(selectedId, _selectedTeamSlot, false));
        root.AddChild(selectedColumn);

        var listColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        listColumn.AddChild(new Label { Text = _teamBuildMode == TeamBuildMode.ProtagonistPicker ? "PROTAGONISTS" : "ITEMS" });
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var grid = new GridContainer { Columns = 5, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        scroll.AddChild(grid);
        listColumn.AddChild(scroll);
        root.AddChild(listColumn);

        if (_teamBuildMode == TeamBuildMode.ProtagonistPicker) AddProtagonistChoices(grid);
        else AddItemChoices(grid);

        _teamBuildStatus.Text = _teamBuildMessage ?? "";
    }

    /// <summary>Build one overview/selected card. Its poster has a 2:3 footprint; its item is a
    /// separate bubble so either surface can enter its dedicated picker.</summary>
    private Control CreateTeamCard(string protagonistId, int slot, bool interactive)
    {
        var card = new VBoxContainer { CustomMinimumSize = new Vector2(156, 0) };
        card.AddThemeConstantOverride("separation", 7);
        var slotLabel = new Label { Text = $"SLOT {slot + 1}", HorizontalAlignment = HorizontalAlignment.Center };
        slotLabel.AddThemeColorOverride("font_color", new Color(0.73f, 0.69f, 0.49f));
        card.AddChild(slotLabel);

        var poster = new Button
        {
            CustomMinimumSize = new Vector2(150, 225),
            TooltipText = protagonistId == null ? "Empty slot — choose a protagonist." : ProtagonistTooltip(protagonistId),
            Disabled = !interactive && protagonistId == null,
        };
        poster.AddThemeStyleboxOverride("normal", MakePanelStyle(new Color(0.12f, 0.13f, 0.20f), new Color(0.56f, 0.51f, 0.34f), 8, 2));
        if (protagonistId == null)
        {
            var empty = new Label { Text = "+\nChoose", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            empty.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            empty.AddThemeFontSizeOverride("font_size", 18);
            poster.AddChild(empty);
        }
        else
        {
            AddTexture(poster, LoadTexture(PosterPath(protagonistId)));
        }
        if (interactive) poster.Pressed += () => OpenSelection(slot, TeamBuildMode.ProtagonistPicker);
        card.AddChild(poster);

        var itemButton = new Button
        {
            CustomMinimumSize = new Vector2(58, 58),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            Disabled = protagonistId == null || !interactive,
            TooltipText = ItemTooltipFor(protagonistId),
        };
        itemButton.AddThemeStyleboxOverride("normal", MakePanelStyle(new Color(0.20f, 0.18f, 0.29f), new Color(0.82f, 0.71f, 0.42f), 29, 2));
        var held = FindState(protagonistId);
        if (held != null && ItemCatalog.TryGet(held.HeldItemDefId, out ItemDef item)) AddTexture(itemButton, LoadTexture(item.IconPath));
        else AddCenteredText(itemButton, "+", 24);
        if (interactive && protagonistId != null) itemButton.Pressed += () => OpenSelection(slot, TeamBuildMode.ItemPicker);
        card.AddChild(itemButton);
        return card;
    }

    private void OpenSelection(int slot, TeamBuildMode mode)
    {
        _selectedTeamSlot = slot;
        _teamBuildMode = mode;
        _teamBuildMessage = null;
        RebuildTeamBuildView();
    }

    private void ReturnToOverview(string message = null)
    {
        _teamBuildMessage = message;
        _teamBuildMode = TeamBuildMode.Overview;
        _selectedTeamSlot = -1;
        RebuildTeamBuildView();
    }

    private void BackToTeamOverview()
    {
        if (_teamBuildMode != TeamBuildMode.Overview) ReturnToOverview();
    }

    private void AddProtagonistChoices(GridContainer grid)
    {
        var rs = RunState.Instance;
        var roster = BuildRoster();
        string[] teamIds = rs?.CaptureBattleTeam()?.MemberIds ?? System.Array.Empty<string>();
        foreach (var pair in roster)
        {
            string id = pair.Key;
            bool inTeam = System.Array.IndexOf(teamIds, id) >= 0;
            var choice = CreateSquareChoice(LoadTexture(MugshotPath(id)), ProtagonistTooltip(id), inTeam);
            bool owned = rs?.FindProtagonist(id) != null;
            choice.Disabled = !owned;
            if (!owned) choice.TooltipText += "\n" + RecruitHint(id);
            else choice.Pressed += () => ChooseProtagonist(id);
            grid.AddChild(choice);
        }
    }

    private void ChooseProtagonist(string id)
    {
        string original = SelectedTeamMemberId();
        if (id == original) { ReturnToOverview("No change made."); return; }

        var rs = RunState.Instance;
        string error = null;
        bool changed = rs != null && rs.TrySetActiveBuildSlot(_selectedTeamSlot, id, out error);
        if (!changed)
        {
            _teamBuildMessage = error ?? "Unable to change the team.";
            RebuildTeamBuildView();
            return;
        }
        ReturnToOverview($"{id} is ready for the next battle.");
    }

    private void AddItemChoices(GridContainer grid)
    {
        var rs = RunState.Instance;
        if (rs == null) return;

        var backpack = new List<ItemInstance>(rs.Items);
        backpack.Sort(CompareItems);
        foreach (ItemInstance instance in backpack)
            if (ItemCatalog.TryGet(instance.DefId, out ItemDef item))
                AddItemChoice(grid, item, null, false);

        var holders = new List<ProtagonistState>();
        foreach (ProtagonistState state in rs.Owned)
            if (!string.IsNullOrWhiteSpace(state.HeldItemDefId)) holders.Add(state);
        holders.Sort((left, right) => string.CompareOrdinal(left.HeldItemDefId, right.HeldItemDefId));
        foreach (ProtagonistState holder in holders)
            if (ItemCatalog.TryGet(holder.HeldItemDefId, out ItemDef item))
                AddItemChoice(grid, item, holder, false);

        if (DebugManager.Instance?.Enabled == true)
        {
            foreach (ItemDef item in ItemCatalog.Entries)
            {
                bool represented = false;
                foreach (ItemInstance instance in backpack) if (instance.DefId == item.Id) { represented = true; break; }
                foreach (ProtagonistState holder in holders) if (holder.HeldItemDefId == item.Id) { represented = true; break; }
                if (!represented) AddItemChoice(grid, item, null, true);
            }
        }
    }

    private static int CompareItems(ItemInstance left, ItemInstance right)
    {
        ItemCatalog.TryGet(left?.DefId, out ItemDef leftDef);
        ItemCatalog.TryGet(right?.DefId, out ItemDef rightDef);
        return ItemCatalog.CompareForDisplay(leftDef, rightDef);
    }

    private void AddItemChoice(GridContainer grid, ItemDef item, ProtagonistState holder, bool debugOnly)
    {
        bool equipped = holder != null;
        string suffix = equipped ? $"\nEquipped by {holder.Id}. Click to swap." : debugOnly ? "\n[Debug preview]" : "\nIn backpack.";
        var choice = CreateSquareChoice(LoadTexture(item.IconPath), ItemCatalog.Tooltip(item) + suffix, equipped);
        choice.Pressed += () => ChooseItem(item.Id, holder, debugOnly);
        grid.AddChild(choice);
    }

    private void ChooseItem(string defId, ProtagonistState sourceHolder, bool debugOnly)
    {
        var target = FindState(SelectedTeamMemberId());
        if (target == null) { ReturnToOverview(); return; }
        if (ReferenceEquals(target, sourceHolder)) { ReturnToOverview("No change made."); return; }

        var rs = RunState.Instance;
        if (rs == null) { ReturnToOverview(); return; }
        if (sourceHolder != null)
        {
            if (!rs.TrySwapHeldItems(target, sourceHolder, out string error))
            {
                _teamBuildMessage = error ?? "Unable to swap those items.";
                RebuildTeamBuildView();
                return;
            }
            ReturnToOverview("Items swapped.");
            return;
        }

        rs.HoldItem(target, defId, fromBackpack: !debugOnly);
        ReturnToOverview($"Equipped {ItemCatalog.DisplayName(defId)}.");
    }

    private List<KeyValuePair<string, ProtagonistState>> BuildRoster()
    {
        var roster = new List<KeyValuePair<string, ProtagonistState>>();
        var rs = RunState.Instance;
        if (rs != null)
            foreach (ProtagonistState state in rs.Owned)
                roster.Add(new KeyValuePair<string, ProtagonistState>(state.Id, state));

        if (DebugManager.Instance?.Enabled == true)
        {
            foreach (var entry in ProtagonistRoster.Entries)
            {
                bool exists = false;
                foreach (var pair in roster) if (pair.Key == entry.Id) { exists = true; break; }
                if (exists) continue;
                if (!_debugStates.TryGetValue(entry.Id, out ProtagonistState state))
                {
                    state = new ProtagonistState(entry.Id);
                    _debugStates[entry.Id] = state;
                }
                roster.Add(new KeyValuePair<string, ProtagonistState>(entry.Id, state));
            }
        }

        // Portraits for future protagonists remain visible but unavailable until their playable
        // scene and real unlock contract exist. This lets the Team Build gallery grow without
        // ever placing an unspawnable protagonist into ActiveBuild.
        foreach (string id in KnownProtagonistIds)
        {
            bool exists = false;
            foreach (var pair in roster) if (pair.Key == id) { exists = true; break; }
            if (!exists) roster.Add(new KeyValuePair<string, ProtagonistState>(id, null));
        }
        return roster;
    }

    private string SelectedTeamMemberId()
    {
        string[] members = RunState.Instance?.CaptureBattleTeam()?.MemberIds ?? System.Array.Empty<string>();
        return _selectedTeamSlot >= 0 && _selectedTeamSlot < members.Length ? members[_selectedTeamSlot] : null;
    }

    private ProtagonistState FindState(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        ProtagonistState state = RunState.Instance?.FindProtagonist(id);
        return state ?? (_debugStates.TryGetValue(id, out state) ? state : null);
    }

    private static Button CreateSquareChoice(Texture2D texture, string tooltip, bool selected)
    {
        var choice = new Button { CustomMinimumSize = new Vector2(86, 86), TooltipText = tooltip };
        choice.AddThemeStyleboxOverride("normal", MakePanelStyle(new Color(0.15f, 0.16f, 0.24f), new Color(0.48f, 0.50f, 0.62f), 7, 1));
        if (selected)
        {
            choice.Modulate = new Color(0.57f, 0.57f, 0.60f, 1f);
            choice.Rotation = -0.09f;
        }
        AddTexture(choice, texture);
        return choice;
    }

    private static void AddTexture(Control parent, Texture2D texture)
    {
        if (texture == null) return;
        var image = new TextureRect
        {
            Texture = texture,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(image);
    }

    private static void AddCenteredText(Control parent, string text, int fontSize)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(label);
    }

    private static StyleBoxFlat MakePanelStyle(Color background, Color border, int radius, int borderWidth)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
        };
    }

    private static Texture2D LoadTexture(string path) => string.IsNullOrWhiteSpace(path) ? null : GD.Load<Texture2D>(path);

    private static string PosterPath(string id) => $"res://Assets/Sprites/Protagonists/{id}/UI/Poster.png";
    private static string MugshotPath(string id) => $"res://Assets/Sprites/Protagonists/{id}/UI/Mugshot.png";

    private static string ProtagonistTooltip(string id) => id switch
    {
        "Pomegraknight" => "Pomegraknight\nA fiery pomegranate knight who turns heat into momentum.",
        "PumpKing" => "PumpKing\nA crowned pumpkin monarch with a stubborn soul.",
        "Cleopastar" => "Cleopastar\nA poised starfruit who commands orbiting stars.",
        "Pixolotl" => "Pixolotl\nAn axolotl explorer with a bright, curious heart.",
        "Sifu Pangda" => "Sifu Pangda\nA seasoned hulu-bottle master of calm Chi.",
        _ => id ?? "Empty team slot",
    };

    private static string ItemTooltipFor(string protagonistId)
    {
        if (string.IsNullOrEmpty(protagonistId)) return "Choose a protagonist first.";
        ProtagonistState state = RunState.Instance?.FindProtagonist(protagonistId);
        return state != null && ItemCatalog.TryGet(state.HeldItemDefId, out ItemDef item)
            ? ItemCatalog.Tooltip(item)
            : "Empty held-item slot — choose an item.";
    }
}
