using System.Collections.Generic;
using Godot;
using Fableland.Run;
using Fableland.Items;
using Fableland.Debug;

/// <summary>
/// Shelter Adventure scene. Blessings remain day-ending actions while Team Build is a free
/// management action. Team Build keeps the editable next-battle order inside RunState; an arena
/// receives only its frozen battle snapshot.
/// </summary>
public partial class ShelterController : CanvasLayer
{
    private const string BlessingConfirm =
        "You will use all your stamina and the Blessing of this shelter to make the magic happen.";

    private Label _title;
    private Label _status;
    private Button _rest, _sharpenWeapon, _sharpenArmor, _teamBuild, _finishDay, _back;

    private string _nodeId;
    private bool _isVoidShelter;
    private bool _isEidolonShelter;

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
        _isVoidShelter = rs?.FindNode(_nodeId)?.Kind == Fableland.Map.NodeKind.Shelter;
        _isEidolonShelter = !string.IsNullOrWhiteSpace(rs?.FindNode(_nodeId)?.RealityBridgeId);

        _rest.Pressed += () => Blessing("Rest — restore 30% max HP (excess → permanent max HP).",
            () => rs?.RestBlessing());
        _sharpenWeapon.Pressed += () => Blessing("Sharpen Weapon — permanently +10 ATK.",
            () => rs?.AddAtk(10));
        _sharpenArmor.Pressed += () => Blessing("Sharpen Armor — permanently +10 DEF.",
            () => rs?.AddDef(10));
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

        _title.Text = _isEidolonShelter ? "Eidolon Shelter" : _isVoidShelter ? $"Last Shelter {_nodeId}" : $"Transportation Hub {_nodeId}";
        _status.Text = _isEidolonShelter
            ? (blessed ? "Spend this shelter's Blessing on Rest or Sharpen, then cross to the far shore." : "The bridge has been crossed; only Team Build remains.")
            : _isVoidShelter
                ? "The singularity has closed behind you. Rest, sharpen, or rebuild before the VOID."
                : (blessed ? "Blessed — one day-ending action available." : "Mundane — the Blessing is spent.");

        _rest.Disabled = !blessed;
        _sharpenWeapon.Disabled = !blessed;
        _sharpenArmor.Disabled = !blessed;
        _finishDay.Disabled = _isEidolonShelter;
        _back.Disabled = _isEidolonShelter || stamina <= 0;
        _back.Text = _isVoidShelter ? "Continue into the VOID" : "Back to Map";
    }

    private void Blessing(string effectLine, System.Action apply)
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

    private void ConfirmWithoutEnding(string text, System.Action onYes)
    {
        var dlg = new ConfirmationDialog { DialogText = text, Title = "Eidolon Shelter" };
        AddChild(dlg);
        dlg.Confirmed += () => { dlg.QueueFree(); onYes?.Invoke(); };
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered();
    }

    private void OnBack()
    {
        var rs = RunState.Instance;
        if (rs == null || rs.Stamina <= 0) return;
        rs.ReturnToMap();
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
        AddChild(_teamBuildPanel);
    }

    private void OpenTeamBuild()
    {
        _teamBuildMode = TeamBuildMode.Overview;
        _selectedTeamSlot = -1;
        _teamBuildMessage = null;
        RepositionTeamBuildPanel();
        RebuildTeamBuildView();
        _teamBuildPanel.Visible = true;
    }

    private void CloseTeamBuild()
    {
        _teamBuildPanel.Visible = false;
        _teamBuildMode = TeamBuildMode.Overview;
        _selectedTeamSlot = -1;
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
            if (!owned) choice.TooltipText += "\nUnavailable outside debug preview.";
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
