using System.Collections.Generic;
using Godot;
using Fableland.Run;
using Fableland.Items;
using Fableland.Debug;

/// <summary>
/// Placeholder shelter Adventure scene (v0.5.0). A menu of the shelter's actions; Phase 4 will
/// flesh out plantations, traders, jousting, build/item management. For now it covers the
/// day-ending Blessing actions (Rest / Sharpen Weapon / Sharpen Armor, NODES §5.5), leaving via
/// Finish the Day, free "Back to Map" (no day end) while stamina remains, and a free "Team
/// Build" menu (v0.6.0 M1 stub — assign wonder items to a protagonist's single held slot; see
/// <see cref="OpenTeamBuild"/>). Only id + display-name items exist yet — no passive/skill/
/// cooldown behavior, T30 §4 future work.
///
/// Null-tolerant: launchable via F5 (no run) — falls back to a debug node id, Blessed.
/// </summary>
public partial class ShelterController : CanvasLayer
{
    private const string BlessingConfirm =
        "You will use all your stamina and the Blessing of this shelter to make the magic happen.";

    private Label _title;
    private Label _status;
    private Button _rest, _sharpenWeapon, _sharpenArmor, _teamBuild, _finishDay, _back;

    private string _nodeId;

    // ---- Team Build overlay (v0.6.0 M1) ----
    private Panel _teamBuildPanel;
    private VBoxContainer _rosterList;
    private VBoxContainer _backpackList;
    private Label _teamBuildStatus;
    private string _selectedProtagId;
    /// <summary>Ephemeral, debug-only ProtagonistStates for roster entries not in RunState.Owned
    /// (e.g. PumpKing before it's granted for real). NEVER added to Owned/ActiveBuild — pure
    /// display/assignment scratch state, cached so selections persist across refreshes.</summary>
    private readonly Dictionary<string, ProtagonistState> _debugStates = new();

    public override void _Ready()
    {
        _title = GetNode<Label>("Center/Box/TitleLabel");
        _status = GetNode<Label>("Center/Box/StatusLabel");
        _rest = GetNode<Button>("Center/Box/RestButton");
        _sharpenWeapon = GetNode<Button>("Center/Box/SharpenWeaponButton");
        _sharpenArmor = GetNode<Button>("Center/Box/SharpenArmorButton");
        _teamBuild = GetNode<Button>("Center/Box/TeamBuildButton");
        _finishDay = GetNode<Button>("Center/Box/FinishDayButton");
        _back = GetNode<Button>("Center/Box/BackButton");

        var rs = RunState.Instance;
        _nodeId = rs?.CurrentAdventure?.NodeId ?? "(debug)";

        _rest.Pressed += () => Blessing("Rest — restore 30% max HP (excess → permanent max HP).",
            () => rs?.RestBlessing());
        _sharpenWeapon.Pressed += () => Blessing("Sharpen Weapon — permanently +10 ATK.",
            () => rs?.AddAtk(10));
        _sharpenArmor.Pressed += () => Blessing("Sharpen Armor — permanently +10 DEF.",
            () => rs?.AddDef(10));
        // Team Build is a free management action — no stamina/Blessing cost, no day end, no
        // scene change (NODES §5.1 build/item swap is available at Blessed AND Mundane shelters).
        _teamBuild.Pressed += OpenTeamBuild;
        _finishDay.Pressed += () => Confirm("Finish the Day?", null);
        _back.Pressed += OnBack;

        BuildTeamBuildPanel();

        Refresh();
    }

    private void Refresh()
    {
        var rs = RunState.Instance;
        bool blessed = rs?.IsShelterBlessed(_nodeId) ?? true;
        int stamina = rs?.Stamina ?? RunState.MaxStamina;

        _title.Text = $"Shelter {_nodeId}  (placeholder)";
        _status.Text = blessed ? "Blessed — one day-ending action available." : "Mundane — the Blessing is spent.";

        // Blessed shelter's day-ending actions grey out once Mundane (NODES §5).
        _rest.Disabled = !blessed;
        _sharpenWeapon.Disabled = !blessed;
        _sharpenArmor.Disabled = !blessed;
        // Leaving is free but needs stamina to actually move on the map (NODES §5).
        _back.Disabled = stamina <= 0;
    }

    /// <summary>A day-ending Blessing action: GDD confirm text → apply effect, consume Blessing, end day.</summary>
    private void Blessing(string effectLine, System.Action apply)
    {
        Confirm($"{effectLine}\n\n{BlessingConfirm}", () =>
        {
            apply?.Invoke();
            RunState.Instance?.ConsumeBlessing(_nodeId);
        });
    }

    /// <summary>
    /// Shows a Yes/Cancel popup. On Yes: run <paramref name="onYes"/> (if any), then end the day
    /// via RunState (which swaps back to the map, or to RunOver on VOID death).
    /// </summary>
    private void Confirm(string text, System.Action onYes)
    {
        var dlg = new ConfirmationDialog { DialogText = text, Title = "Shelter" };
        AddChild(dlg);
        dlg.Confirmed += () =>
        {
            dlg.QueueFree();
            onYes?.Invoke();
            RunState.Instance?.EndDay();
        };
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered();
    }

    private void OnBack()
    {
        var rs = RunState.Instance;
        if (rs == null || rs.Stamina <= 0) return; // can't move with no stamina
        rs.ReturnToMap(); // free — leaving a shelter does not end the day (NODES §5)
    }

    // ============================================================= Team Build overlay (v0.6.0 M1)

    /// <summary>
    /// Build the Team Build overlay: a panel filling ~70% of the viewport with a title/close row,
    /// a scrollable roster list, a scrollable backpack list, and a status label. Mirrors
    /// <c>DebugManager.BuildLogPanel()</c>/<c>BuildProtagonistPanel()</c>'s construction style
    /// (hand-built Panel + StyleBoxFlat + VBoxContainer) for visual consistency. Built once;
    /// content is rebuilt by <see cref="RefreshTeamBuild"/> on every open + after every action.
    /// </summary>
    private void BuildTeamBuildPanel()
    {
        _teamBuildPanel = new Panel { Visible = false };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.08f, 0.92f),
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            BorderColor = new Color(0.3f, 0.3f, 0.5f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        };
        _teamBuildPanel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _teamBuildPanel.AddChild(vbox);

        // Title bar row
        var titleRow = new HBoxContainer();
        var titleLabel = new Label { Text = "Team Build", HorizontalAlignment = HorizontalAlignment.Left };
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        titleRow.AddChild(titleLabel);

        var closeBtn = new Button { Text = "X", CustomMinimumSize = new Vector2(32, 28) };
        closeBtn.Pressed += () => _teamBuildPanel.Visible = false; // close = hide, not free
        titleRow.AddChild(closeBtn);
        vbox.AddChild(titleRow);

        var helpLabel = new Label { Text = "Select a protagonist, then click an item to assign it." };
        vbox.AddChild(helpLabel);

        vbox.AddChild(new Label { Text = "Roster" });
        var rosterScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _rosterList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rosterScroll.AddChild(_rosterList);
        vbox.AddChild(rosterScroll);

        vbox.AddChild(new Label { Text = "Backpack (click an item to assign it to the selected protagonist)" });
        var backpackScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _backpackList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        backpackScroll.AddChild(_backpackList);
        vbox.AddChild(backpackScroll);

        _teamBuildStatus = new Label { Text = "" };
        vbox.AddChild(_teamBuildStatus);

        AddChild(_teamBuildPanel);
    }

    /// <summary>Open the Team Build menu — a free management action (no day end, no scene change,
    /// available regardless of Blessed state or stamina; see the wiring comment in _Ready).</summary>
    private void OpenTeamBuild()
    {
        RepositionTeamBuildPanel();
        RefreshTeamBuild();
        _teamBuildPanel.Visible = true;
    }

    private void RepositionTeamBuildPanel()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float w = vp.X * 0.70f;
        float h = vp.Y * 0.70f;
        float x = (vp.X - w) / 2f;
        float y = (vp.Y - h) / 2f;
        _teamBuildPanel.Position = new Vector2(x, y);
        _teamBuildPanel.Size = new Vector2(w, h);
    }

    /// <summary>
    /// Rebuild the roster + backpack rows from current state. Roster = RunState.Owned (real)
    /// unioned with ProtagonistRoster entries not already Owned (debug-only, ephemeral state) when
    /// debug mode is on — NEVER written into Owned/ActiveBuild. Backpack = RunState.Items (real)
    /// plus, in debug mode, any catalog entry not already in Items or held by the roster union (a
    /// display-only bypass, never bulk-written into Items). Null-tolerant throughout (T30 §4 stub
    /// — no passive/skill/cooldown behavior attaches to any of this).
    /// </summary>
    private void RefreshTeamBuild()
    {
        var rs = RunState.Instance;
        bool debugOn = DebugManager.Instance?.Enabled == true;

        // ---- roster union ----
        var roster = new List<(string Id, ProtagonistState State)>();
        if (rs != null)
            foreach (var p in rs.Owned) roster.Add((p.Id, p));

        if (debugOn)
        {
            foreach (var entry in ProtagonistRoster.Entries)
            {
                bool alreadyOwned = false;
                foreach (var r in roster) if (r.Id == entry.Id) { alreadyOwned = true; break; }
                if (alreadyOwned) continue;

                if (!_debugStates.TryGetValue(entry.Id, out var state))
                {
                    state = new ProtagonistState(entry.Id);
                    _debugStates[entry.Id] = state;
                }
                roster.Add((entry.Id, state));
            }
        }

        if (_selectedProtagId != null)
        {
            bool stillPresent = false;
            foreach (var r in roster) if (r.Id == _selectedProtagId) { stillPresent = true; break; }
            if (!stillPresent) _selectedProtagId = null; // e.g. debug toggled off mid-panel
        }

        // ---- roster rows ----
        foreach (Node c in _rosterList.GetChildren()) c.QueueFree();
        foreach (var (id, state) in roster)
        {
            var row = new HBoxContainer();

            string marker = id == _selectedProtagId ? $"> {id} <" : id;
            var selectBtn = new Button { Text = marker, CustomMinimumSize = new Vector2(160, 32) };
            selectBtn.Pressed += () => { _selectedProtagId = id; RefreshTeamBuild(); };
            row.AddChild(selectBtn);

            string heldText = string.IsNullOrEmpty(state.HeldItemDefId) ? "(empty)" : ItemCatalog.DisplayName(state.HeldItemDefId);
            row.AddChild(new Label { Text = heldText, CustomMinimumSize = new Vector2(200, 0) });

            var returnBtn = new Button
            {
                Text = "Return to backpack",
                Disabled = string.IsNullOrEmpty(state.HeldItemDefId),
            };
            returnBtn.Pressed += () =>
            {
                RunState.Instance?.UnholdItem(state);
                RefreshTeamBuild();
            };
            row.AddChild(returnBtn);

            _rosterList.AddChild(row);
        }

        // ---- backpack rows ----
        foreach (Node c in _backpackList.GetChildren()) c.QueueFree();

        if (rs != null)
        {
            foreach (var it in rs.Items)
            {
                string defId = it.DefId;
                var btn = new Button { Text = ItemCatalog.DisplayName(defId), CustomMinimumSize = new Vector2(0, 32) };
                btn.Pressed += () => AssignItem(defId, fromBackpack: true); // real backpack item
                _backpackList.AddChild(btn);
            }
        }

        if (debugOn)
        {
            var heldIds = new HashSet<string>();
            foreach (var (_, state) in roster)
                if (!string.IsNullOrEmpty(state.HeldItemDefId)) heldIds.Add(state.HeldItemDefId);
            var backpackIds = new HashSet<string>();
            if (rs != null) foreach (var it in rs.Items) backpackIds.Add(it.DefId);

            foreach (var entry in ItemCatalog.Entries)
            {
                if (backpackIds.Contains(entry.Id) || heldIds.Contains(entry.Id)) continue;
                string defId = entry.Id;
                var btn = new Button { Text = "[DBG] " + entry.DisplayName, CustomMinimumSize = new Vector2(0, 32) };
                btn.Pressed += () => AssignItem(defId, fromBackpack: false); // debug display bypass — never in Items
                _backpackList.AddChild(btn);
            }
        }

        _teamBuildStatus.Text = _selectedProtagId == null
            ? "Select a protagonist, then click an item to assign it."
            : $"Selected: {_selectedProtagId}";
    }

    /// <summary>Assign a wonder item (by DefId) to the currently-selected roster protagonist.
    /// <paramref name="fromBackpack"/> true = a real RunState.Items backpack item (consumed and
    /// restored on unhold); false = a debug-catalog display-bypass item that must never enter the
    /// real economy (see RunState.HoldItem).</summary>
    private void AssignItem(string defId, bool fromBackpack)
    {
        if (_selectedProtagId == null)
        {
            _teamBuildStatus.Text = "Select a protagonist first.";
            return;
        }
        var state = FindProtagState(_selectedProtagId);
        if (state == null) return;

        RunState.Instance?.HoldItem(state, defId, fromBackpack);
        RefreshTeamBuild();
    }

    /// <summary>Find the ProtagonistState behind a roster id — a real RunState.Owned entry, or
    /// (debug-only) the cached ephemeral state. Never null-derefs when RunState.Instance is null.</summary>
    private ProtagonistState FindProtagState(string id)
    {
        if (id == null) return null;
        var rs = RunState.Instance;
        if (rs != null)
            foreach (var p in rs.Owned)
                if (p.Id == id) return p;
        return _debugStates.TryGetValue(id, out var s) ? s : null;
    }
}
