using System;
using System.Collections.Generic;
using Godot;
using Fableland.Data;
using Fableland.Run;

namespace Fableland.Debug;

/// <summary>
/// Debug-mode autoload — persists across all scenes. Provides a toggle button (always visible,
/// top-right), a Skip button (visible when debug is on), a live-updating log streamed to disk
/// (<c>user://debug_log.txt</c>), and an in-game log viewer toggled by key <c>5</c>.
///
/// Logging hooks in game code are one-liners:
/// <c>DebugManager.Instance?.Log("DMG_RCVD", "Took 18.5 from CrabFoe");</c>
/// When debug is off the Log method returns immediately after one bool check.
/// </summary>
public partial class DebugManager : CanvasLayer
{
    public static DebugManager Instance { get; private set; }

    // ---- state ----
    public bool Enabled { get; private set; }

    /// <summary>
    /// Debug-layer protagonist override (null = scene default). Persists across scenes since
    /// this is an autoload; not saved. Never touches <c>RunState</c> — see
    /// <see cref="ProtagonistRoster"/> for why this is a separate registry.
    /// </summary>
    public string SelectedProtagonistId { get; private set; }

    /// <summary>Fired when the Skip button is pressed (GameManager handles it).</summary>
    public event Action SkipRequested;

    private readonly List<DebugLogEntry> _entries = new();
    private const int MaxBufferLines = 2000;

    // ---- file log ----
    private string _logPath;
    private FileAccess _logFile;

    // ---- UI ----
    private Button _toggleBtn;
    private Button _skipBtn;
    private Panel _logPanel;
    private RichTextLabel _logContent;
    private Label _logTitle;
    private bool _logVisible;

    // ---- protagonist page UI ----
    private Panel _protagonistPanel;
    private Label _protagonistTitle;
    private Label _protagonistStatus;
    private readonly List<Button> _protagonistButtons = new();
    private bool _protagonistPageVisible;

    // ---- layout constants ----
    private const float BtnW = 56f;
    private const float BtnH = 44f;
    private const float BtnMargin = 10f;

    public override void _EnterTree()
    {
        Instance = this;
        _logPath = ProjectSettings.GlobalizePath("user://debug_log.txt");
        OpenLogFile();
    }

    public override void _ExitTree()
    {
        CloseLogFile();
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        // ---- toggle button (always visible) ----
        _toggleBtn = new Button
        {
            Text = "DBG",
            CustomMinimumSize = new Vector2(BtnW, BtnH),
            OffsetRight = BtnW,
            OffsetBottom = BtnH,
        };
        _toggleBtn.Pressed += OnToggle;
        // Debug tools are for whoever builds the game: an exported release hides the toggle
        // (audit v0.11.1 #4). The editor's F5 is a debug build, so it stays there.
        _toggleBtn.Visible = OS.IsDebugBuild();
        AddChild(_toggleBtn);

        // ---- skip button (visible only when debug is on) ----
        _skipBtn = new Button
        {
            Text = "SKIP",
            CustomMinimumSize = new Vector2(BtnW, BtnH),
            Visible = false,
        };
        _skipBtn.Pressed += () => SkipRequested?.Invoke();
        AddChild(_skipBtn);

        // ---- log viewer panel (hidden until key 5) ----
        BuildLogPanel();

        // ---- protagonist page (hidden until key 4, only while debug is on) ----
        BuildProtagonistPanel();

        RepositionButtons();
    }

    /// <summary>
    /// Build the log-viewer overlay: a panel filling ~80 % of the viewport, containing a
    /// title bar, a scrollable RichTextLabel, and a close button.
    /// </summary>
    private void BuildLogPanel()
    {
        _logPanel = new Panel
        {
            Visible = false,
            // Will be sized + positioned in RepositionLogPanel each time it opens.
        };
        // Dark semi-transparent background — provide a StyleBoxFlat.
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
        _logPanel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _logPanel.AddChild(vbox);

        // Title bar row
        var titleRow = new HBoxContainer();
        _logTitle = new Label
        {
            Text = "Debug Log  (5 to toggle, Esc to close)",
            HorizontalAlignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _logTitle.AddThemeFontSizeOverride("font_size", 16);
        titleRow.AddChild(_logTitle);

        var closeBtn = new Button
        {
            Text = "X",
            CustomMinimumSize = new Vector2(32, 28),
        };
        closeBtn.Pressed += ToggleLogViewer;
        titleRow.AddChild(closeBtn);
        vbox.AddChild(titleRow);

        // RichTextLabel owns its own scrolling. Nesting it inside ScrollContainer
        // gives the label no reliable viewport/minimum size, which can leave every
        // populated line laid out outside the visible region.
        _logContent = new RichTextLabel
        {
            // Entries contain literal [time] and [category] fields. BBCode would
            // interpret those brackets as tags and strip the log contents.
            BbcodeEnabled = false,
            FitContent = false,
            ScrollFollowing = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _logContent.AddThemeFontSizeOverride("normal_font_size", 14);
        vbox.AddChild(_logContent);

        AddChild(_logPanel);
    }

    /// <summary>
    /// Build the protagonist-selection overlay: one button per <see cref="ProtagonistRoster"/>
    /// entry, a "Clear override" button, and a status label. Mirrors <see cref="BuildLogPanel"/>'s
    /// construction style (hand-built Panel + StyleBoxFlat + VBoxContainer + title/close row).
    /// </summary>
    private void BuildProtagonistPanel()
    {
        _protagonistPanel = new Panel
        {
            Visible = false,
            // Sized + positioned in RepositionProtagonistPanel each time it opens.
        };
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
        _protagonistPanel.AddThemeStyleboxOverride("panel", style);

        // The page outgrew a short window (the roster section, v0.12.8): it scrolls.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _protagonistPanel.AddChild(scroll);
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        // Title bar row
        var titleRow = new HBoxContainer();
        _protagonistTitle = new Label
        {
            Text = "Protagonists (4 to toggle, Esc to close)",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _protagonistTitle.AddThemeFontSizeOverride("font_size", 16);
        titleRow.AddChild(_protagonistTitle);

        var closeBtn = new Button
        {
            Text = "X",
            CustomMinimumSize = new Vector2(32, 28),
        };
        closeBtn.Pressed += ToggleProtagonistPage;
        titleRow.AddChild(closeBtn);
        vbox.AddChild(titleRow);

        // One button per roster entry.
        _protagonistButtons.Clear();
        foreach (var entry in ProtagonistRoster.Entries)
        {
            string id = entry.Id;   // captured per-iteration for the closure
            var btn = new Button { Text = id };
            btn.Pressed += () => SelectProtagonist(id);
            vbox.AddChild(btn);
            _protagonistButtons.Add(btn);
        }

        // Clear-override button.
        var clearBtn = new Button { Text = "Clear override (scene default)" };
        clearBtn.Pressed += ClearProtagonistOverride;
        vbox.AddChild(clearBtn);

        BuildRosterSection(vbox);

        // Boss fights (FOES.gdd §8): stage any boss without the overworld. One button per
        // realm boss row, so a new boss appears here by existing; zone 6's twelve share a picker.
        var bossTitle = new Label { Text = "Boss fights (from the title screen — no run in progress)" };
        bossTitle.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(bossTitle);
        foreach (BossDef boss in BossTable.RealmBosses())
        {
            BossDef def = boss;   // captured per-iteration for the closure
            var fight = new Button { Text = "Fight " + def.DisplayName };
            fight.Pressed += () => StageBossFight(def);
            vbox.AddChild(fight);
        }
        BuildZone6Section(vbox);
        var clearBoss = new Button { Text = "Clear staged boss" };
        clearBoss.Pressed += ClearStagedBoss;
        vbox.AddChild(clearBoss);

        // Status feedback label.
        _protagonistStatus = new Label { Text = "" };
        vbox.AddChild(_protagonistStatus);

        AddChild(_protagonistPanel);
    }

    public override void _Process(double delta)
    {
        // Reposition on every frame so buttons keep hugging the right edge after resize.
        RepositionButtons();

        // Key 5: toggle log viewer all the time (even when debug is off, so the user can
        // still see a log if they enabled debug earlier and it wrote something).
        if (Input.IsActionJustPressed("debug_log_viewer"))
            ToggleLogViewer();

        // Key 4: toggle the protagonist page, but ONLY while debug mode is on — unlike the log
        // viewer, this must do nothing at all when debug is off.
        if (Enabled && Input.IsActionJustPressed("debug_protagonist_page"))
            ToggleProtagonistPage();

        // Escape: close whichever debug overlay is open (one panel per press).
        if (Input.IsActionJustPressed("ui_cancel"))
        {
            if (_protagonistPageVisible) ToggleProtagonistPage();
            else if (_logVisible) ToggleLogViewer();
        }

        // Keep log viewer up to date while visible.
        if (_logVisible)
            RefreshLogContent();
    }

    // ---- public API ---------------------------------------------------------------

    /// <summary>
    /// Log one entry. Categories: DMG_DEALT, DMG_RCVD, HAZARD, DOT, HEAL, BUFF,
    /// CORES, ITEM, STATUS, MISSION, SYSTEM.
    /// Returns immediately when debug mode is off.
    /// </summary>
    public void Log(string category, string message)
    {
        if (!Enabled) return;

        string time = DateTime.Now.ToString("HH:mm:ss");
        var entry = new DebugLogEntry(time, category, message);
        _entries.Add(entry);
        while (_entries.Count > MaxBufferLines) _entries.RemoveAt(0);

        WriteToFile(entry.ToString());
    }

    // Convenience helpers so call sites read clearly.
    public void LogPlayerDmgDealt(float amount, string target)
        => Log("DMG_DEALT", $"Dealt {amount:F1} dmg to {target}");

    public void LogPlayerDmgReceived(float raw, float actual, string source)
        => Log("DMG_RCVD", $"Took {actual:F1} from {source} (raw: {raw:F1})");

    public void LogHazardDmg(float amount, string target)
        => Log("HAZARD", $"Hazard dealt {amount:F1} to {target}");

    public void LogDotDmg(float amount, string kind)
        => Log("DOT", $"DoT: {amount:F1} ({kind})");

    public void LogHeal(float amount, string context)
        => Log("HEAL", $"+{amount:F1} HP ({context})");

    public void LogBuff(string name, int amount, int total)
        => Log("BUFF", $"+{amount} {name} (total: {total})");

    public void LogCores(int amount, int total)
        => Log("CORES", $"+{amount} wonder core{(amount == 1 ? "" : "s")} (total: {total})");

    public void LogItem(string defId, int total)
        => Log("ITEM", $"Got item '{defId}' (total items: {total})");

    public void LogStatus(string status, float amount)
        => Log("STATUS", $"{status} +{amount}");

    public void LogMission(string text)
        => Log("MISSION", text);

    public void LogSystem(string text)
        => Log("SYSTEM", text);

    // ---- internals -----------------------------------------------------------------

    private void OnToggle()
    {
        Enabled = !Enabled;
        _toggleBtn.Text = Enabled ? "DBG*" : "DBG";
        _skipBtn.Visible = Enabled;
        if (Enabled) LogSystem("Debug mode ON");
        else if (_protagonistPageVisible) ToggleProtagonistPage();   // force-close on debug OFF
    }

    private void ToggleLogViewer()
    {
        _logVisible = !_logVisible;
        if (_logVisible)
        {
            RepositionLogPanel();
            RefreshLogContent();
        }
        _logPanel.Visible = _logVisible;
    }

    private void RepositionLogPanel()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float w = vp.X * 0.78f;
        float h = vp.Y * 0.70f;
        float x = (vp.X - w) / 2f;
        float y = (vp.Y - h) / 2f;
        _logPanel.Position = new Vector2(x, y);
        _logPanel.Size = new Vector2(w, h);
    }

    private void ToggleProtagonistPage()
    {
        _protagonistPageVisible = !_protagonistPageVisible;
        if (_protagonistPageVisible)
        {
            RepositionProtagonistPanel();
            RefreshProtagonistButtons();
            RefreshZone6Status();
        }
        _protagonistPanel.Visible = _protagonistPageVisible;
    }

    private void RepositionProtagonistPanel()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float w = vp.X * 0.40f;
        float h = vp.Y * 0.92f;      // roster, the boss-fight section and zone 6's knobs
        float x = (vp.X - w) / 2f;
        float y = (vp.Y - h) / 2f;
        _protagonistPanel.Position = new Vector2(x, y);
        _protagonistPanel.Size = new Vector2(w, h);
    }

    /// <summary>Re-label each roster button so the currently selected entry (if any) is visibly marked.</summary>
    private void RefreshProtagonistButtons()
    {
        for (int i = 0; i < ProtagonistRoster.Entries.Length; i++)
        {
            string id = ProtagonistRoster.Entries[i].Id;
            _protagonistButtons[i].Text = id == SelectedProtagonistId ? $"> {id} <" : id;
        }
    }

    private void SelectProtagonist(string id)
    {
        SelectedProtagonistId = id;
        RefreshProtagonistButtons();

        if (GetTree().CurrentScene is GameManager gm && gm.DebugSwapProtagonist(id))
        {
            _protagonistStatus.Text = $"Swapped to {id}.";
        }
        else
        {
            _protagonistStatus.Text = $"Selected {id} — applies on next Arena entry (while debug is on).";
        }
        // Return focus to the Arena even if the selected body was already active. Otherwise
        // the roster overlay can keep swallowing the mouse click expected to be that body's BA.
        if (GetTree().CurrentScene is GameManager && _protagonistPageVisible)
        {
            foreach (Button button in _protagonistButtons) button.ReleaseFocus();
            ToggleProtagonistPage();
        }
        LogSystem($"Protagonist selection: {id}");
    }

    /// <summary>
    /// Stage a boss fight in a debug arena: the Arena reads
    /// <see cref="GameManager.StagedDebugBossId"/> only when no run's adventure exists, and
    /// asks the run's own map selection for that boss's arena. Refused while a run's
    /// adventure is set: the Arena would stage that node's fight, not this one.
    /// </summary>
    private void StageBossFight(BossDef def)
    {
        if (RunState.Instance?.CurrentAdventure != null)
        {
            _protagonistStatus.Text = "A run is in progress — stage boss fights from the title screen.";
            return;
        }
        GameManager.StagedDebugBossId = def.Id;
        LogSystem($"Boss fight staged: {def.Id}");
        if (_protagonistPageVisible) ToggleProtagonistPage();
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://Scenes/Arena.tscn");
    }

    // ---- the run's roster (NODES §3.1) -----------------------------------------------

    /// <summary>A run starts with one protagonist and recruits the rest (v0.12.8); a tester
    /// still wants all five. Two ways: everyone joins the run in progress now, or new runs
    /// start with all five and the prototype's team (also <c>FABLELAND_ROSTER=all</c>).</summary>
    private void BuildRosterSection(VBoxContainer vbox)
    {
        var title = new Label { Text = "Roster — a run starts with its home protagonist alone" };
        title.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(title);

        var recruitAll = new Button { Text = "Recruit all five into this run" };
        recruitAll.Pressed += () =>
        {
            RunState run = RunState.Instance;
            if (run?.Graph == null)
            {
                _protagonistStatus.Text = "No run in progress — tick the box below for new runs.";
                return;
            }
            int joined = run.DebugRecruitAll();
            _protagonistStatus.Text = joined == 0
                ? "Everyone is already on this run's roster."
                : $"{joined} joined. Team: {string.Join(", ", run.ActiveBuild)} — the next fight uses it.";
        };
        vbox.AddChild(recruitAll);

        var allOnNewRun = new CheckBox
        {
            Text = "New runs start with all five",
            ButtonPressed = RunState.DebugAllProtagonistsOnNewRun,
        };
        allOnNewRun.Toggled += on =>
        {
            RunState.DebugAllProtagonistsOnNewRun = on;
            LogSystem("New runs start with " + (on ? "all five protagonists" : "their home protagonist alone"));
        };
        vbox.AddChild(allOnNewRun);
    }

    // ---- zone 6 (Docs/Zone6.gdd §8) -------------------------------------------------

    private Label _zone6Status;

    /// <summary>Zone 6's knobs: which variant new runs get, a fight with any of its twelve
    /// bosses, the Void Frontier's gravity and Avīci's seals — on the run in progress, or on
    /// the state a debug fight is dealt when there is none.</summary>
    private void BuildZone6Section(VBoxContainer vbox)
    {
        var title = new Label { Text = "Zone 6" };
        title.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(title);

        var variantRow = new HBoxContainer();
        variantRow.AddChild(new Label { Text = "New runs:" });
        var variant = new OptionButton();
        variant.AddItem("Seeded");
        foreach (Zone6VariantDef v in Zone6Table.Variants) variant.AddItem(v.Name + " (" + v.NameZh + ")");
        int forced = Array.FindIndex(Zone6Table.Variants, v => v.Id == Zone6State.ForcedVariantId);
        variant.Selected = forced + 1;
        variant.ItemSelected += index =>
        {
            Zone6State.ForcedVariantId = index <= 0 ? null : Zone6Table.Variants[index - 1].Id;
            LogSystem("Zone 6 variant for new runs: " + (Zone6State.ForcedVariantId ?? "seeded"));
            RefreshZone6Status();
        };
        variantRow.AddChild(variant);
        vbox.AddChild(variantRow);

        var fightRow = new HBoxContainer();
        var bosses = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        List<BossDef> zone6 = BossTable.Zone6Bosses();
        foreach (BossDef boss in zone6) bosses.AddItem((boss.Role == BossRole.Zone6Final ? "★ " : "") + boss.DisplayName);
        fightRow.AddChild(bosses);
        var fight = new Button { Text = "Fight" };
        fight.Pressed += () => { if (bosses.Selected >= 0) StageBossFight(zone6[bosses.Selected]); };
        fightRow.AddChild(fight);
        vbox.AddChild(fightRow);

        var gravityRow = new HBoxContainer();
        gravityRow.AddChild(new Label { Text = "Gravity:" });
        foreach ((string text, float factor) in new[] { ("×0.8 (win)", Zone6Table.GravityWinFactor), ("×1.1 (loss)", Zone6Table.GravityLossFactor), ("Reset", 0f) })
        {
            float f = factor;
            var button = new Button { Text = text };
            button.Pressed += () =>
            {
                Zone6State state = EditableZone6();
                state.DebugSetGravity(f > 0f ? state.Gravity * f : Zone6Table.GravityOnArrival);
                LogSystem($"Zone 6 gravity ×{state.Gravity:0.00}");
                RefreshZone6Status();
            };
            gravityRow.AddChild(button);
        }
        vbox.AddChild(gravityRow);

        var sealRow = new HBoxContainer();
        sealRow.AddChild(new Label { Text = "Seal:" });
        var poison = new OptionButton();
        foreach (Zone6PoisonDef p in Zone6Table.Poisons) poison.AddItem(p.Glyph + " " + p.SealName);
        poison.Selected = 0;
        sealRow.AddChild(poison);
        foreach ((string text, int delta) in new[] { ("+", 1), ("−", -1) })
        {
            int d = delta;
            var button = new Button { Text = text, CustomMinimumSize = new Vector2(32, 0) };
            button.Pressed += () =>
            {
                Zone6State state = EditableZone6();
                state.DebugAddSeal(Zone6Table.Poisons[Math.Max(0, poison.Selected)].Id, d);
                LogSystem("Zone 6 seals: " + state.StatusLine());
                RefreshZone6Status();
            };
            sealRow.AddChild(button);
        }
        vbox.AddChild(sealRow);

        _zone6Status = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        vbox.AddChild(_zone6Status);
        RefreshZone6Status();
    }

    /// <summary>The zone 6 the knobs edit: the run's when one exists, else the debug state.
    /// A change lands in the next arena entered — a fight already running keeps its rules.</summary>
    private static Zone6State EditableZone6() =>
        RunState.Instance?.Graph != null ? RunState.Instance.Zone6 : Zone6State.Debug;

    private void RefreshZone6Status()
    {
        if (_zone6Status == null) return;
        Zone6State state = EditableZone6();
        string whose = RunState.Instance?.Graph != null ? "this run" : "debug fights";
        _zone6Status.Text = $"{whose}: {state.StatusLine()}";
    }

    private void ClearStagedBoss()
    {
        GameManager.StagedDebugBossId = null;
        _protagonistStatus.Text = "Staged boss cleared — the next debug Arena is the scene's own.";
        LogSystem("Staged boss cleared");
    }

    private void ClearProtagonistOverride()
    {
        SelectedProtagonistId = null;
        RefreshProtagonistButtons();
        _protagonistStatus.Text = "Override cleared — scene default.";
        LogSystem("Protagonist override cleared");
    }

    private void RefreshLogContent()
    {
        // AddText writes literal text, so the bracketed timestamps/categories cannot
        // be mistaken for BBCode tags. Colors are applied through the RichText API.
        _logContent.Clear();
        if (_entries.Count == 0)
        {
            // Log() no-ops while debug mode is off, so an empty buffer is ambiguous —
            // say which case it is instead of leaving the pane blank.
            AppendLogText(new Color(0.67f, 0.67f, 0.67f), Enabled
                ? "Debug mode is ON — no events logged yet."
                : "Debug mode is OFF, so nothing has been recorded. Click the DBG button (top-right) to start logging, then reopen this panel.");
            _logTitle.Text = "Debug Log  (0 lines, 5/Esc to close)";
            return;
        }

        foreach (var e in _entries)
        {
            Color color = e.Category switch
            {
                "DMG_DEALT" => new Color(1f, 0.53f, 0.53f),
                "DMG_RCVD" => new Color(1f, 0.27f, 0.27f),
                "HAZARD" => new Color(1f, 0.53f, 0.27f),
                "DOT" => new Color(0.8f, 0.4f, 0.27f),
                "HEAL" => new Color(0.27f, 1f, 0.27f),
                "BUFF" => new Color(0.27f, 0.67f, 1f),
                "CORES" => new Color(1f, 0.8f, 0.27f),
                "ITEM" => new Color(0.8f, 0.53f, 1f),
                "STATUS" => new Color(0.53f, 0.8f, 1f),
                "MISSION" => Colors.White,
                "SYSTEM" => new Color(0.67f, 0.67f, 0.67f),
                _ => new Color(0.8f, 0.8f, 0.8f),
            };
            AppendLogText(color, $"[{e.Time}] [{e.Category}] {e.Message}\n");
        }
        _logTitle.Text = $"Debug Log  ({_entries.Count} lines, 5/Esc to close)";
    }

    private void AppendLogText(Color color, string text)
    {
        _logContent.PushColor(color);
        _logContent.AddText(text);
        _logContent.Pop();
    }

    private void RepositionButtons()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float right = vp.X - BtnW - BtnMargin;
        // Centered vertically on the right edge
        float midY = (vp.Y - BtnH) / 2f;
        _toggleBtn.Position = new Vector2(right, midY);
        _skipBtn.Position = new Vector2(right, midY + BtnH + 4f);
    }

    private void OpenLogFile()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir) && !DirAccess.DirExistsAbsolute(dir))
                DirAccess.MakeDirRecursiveAbsolute(dir);
            _logFile = FileAccess.Open(_logPath, FileAccess.ModeFlags.Write);
            _logFile?.StoreLine("[-- Debug log started --]");
            _logFile?.Flush();
        }
        catch
        {
            _logFile = null;
        }
    }

    private void CloseLogFile()
    {
        try { _logFile?.Flush(); _logFile?.Close(); _logFile?.Dispose(); } catch { }
        _logFile = null;
    }

    private void WriteToFile(string line)
    {
        if (_logFile == null) return;
        try
        {
            _logFile.SeekEnd();
            _logFile.StoreLine(line);
            _logFile.Flush();
        }
        catch
        {
            // Best-effort only — never crash the game for a log write.
        }
    }
}
