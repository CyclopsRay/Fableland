using System;
using System.Collections.Generic;
using Godot;

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

        // Scrollable content
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _logContent = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollFollowing = true,
        };
        // Ensure text is visible on the dark panel background
        _logContent.AddThemeColorOverride("default_color", new Color(0.9f, 0.9f, 0.9f));
        _logContent.AddThemeFontSizeOverride("normal_font_size", 14);
        scroll.AddChild(_logContent);
        vbox.AddChild(scroll);

        // Bottom: line count
        var footer = new Label();
        vbox.AddChild(footer);

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

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _protagonistPanel.AddChild(vbox);

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
        }
        _protagonistPanel.Visible = _protagonistPageVisible;
    }

    private void RepositionProtagonistPanel()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float w = vp.X * 0.40f;
        float h = vp.Y * 0.50f;
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
        LogSystem($"Protagonist selection: {id}");
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
        // Rebuild the full BBcode text each frame the viewer is open. For a few hundred
        // lines this is cheap; at 2000 lines it's still fine for a debug tool.
        var sb = new System.Text.StringBuilder();
        foreach (var e in _entries)
        {
            string color = e.Category switch
            {
                "DMG_DEALT" => "#ff8888",
                "DMG_RCVD" => "#ff4444",
                "HAZARD" => "#ff8844",
                "DOT" => "#cc6644",
                "HEAL" => "#44ff44",
                "BUFF" => "#44aaff",
                "CORES" => "#ffcc44",
                "ITEM" => "#cc88ff",
                "STATUS" => "#88ccff",
                "MISSION" => "#ffffff",
                "SYSTEM" => "#aaaaaa",
                _ => "#cccccc",
            };
            sb.Append($"[color={color}][{e.Time}] [{e.Category}] {e.Message}[/color]\n");
        }
        _logContent.Text = sb.ToString();
        _logTitle.Text = $"Debug Log  ({_entries.Count} lines, 5/Esc to close)";
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
