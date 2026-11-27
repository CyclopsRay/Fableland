using Godot;
using System;
using Fableland.Items;

/// <summary>
/// Minimal HUD — HP bar, lives (debug only), the mission panel (title/progress/timer/secondary
/// bar), a reward-choice pair of buttons, the Finish-the-Day button, a short-lived reward toast,
/// a centered banner for the debug win/lose flow, and the bottom-left character status cluster
/// (mugshot, ult charge ring, and Shift/E/item cooldown icons). Built mostly in the scene
/// (Hud.tscn) with dynamic bits driven from here.
///
/// GameManager POLLS the active Mission's read-props every frame and pushes them here — missions
/// never touch the HUD directly (see Mission.cs).
/// </summary>
public partial class Hud : CanvasLayer
{
    private HpBlockBar _hpBar;
    private Label _hpLabel;
    private Label _livesLabel;
    private Label _banner;

    private Label _missionTitleLabel;
    private Label _progressLabel;
    private Label _timerLabel;
    private ProgressBar _secondaryBar;
    private ColorRect _secondaryGold, _secondaryNotch;   // Māra's lock, made on first use
    private Label _secondaryBarLabel;

    private Control _rewardChoice;
    private Button _atkChoiceButton;
    private Button _defChoiceButton;

    private Button _finishDayButton;
    private Label _rulesLabel;
    private Label _toastLabel;
    private string _toastToken;

    private TextureRect _mugshot;
    private TextureProgressBar _ultRing;
    private TextureProgressBar _shiftCd;
    private Label _shiftCdLabel;
    private TextureProgressBar _eCd;
    private Label _eCdLabel;
    private Control _itemSlot;
    private TextureProgressBar _itemCd;
    private Label _itemCdLabel;

    // Switch-protagonist slot — same pattern as Shift/E/Item slots: icon + radial CD overlay + label.
    private Control _switchSlot;
    private TextureRect _switchIcon;
    private TextureProgressBar _switchCdOverlay;
    private Label _switchCdLabel;
    private string _switchNextId;           // which protagonist the icon shows; null = hidden

    private CharacterController _player;

    /// <summary>Fired when the player presses Finish the Day.</summary>
    public event Action FinishDayPressed;

    /// <summary>Fired on a reward-choice press: true = ATK, false = DEF.</summary>
    public event Action<bool> RewardChoicePressed;

    // Indexed by HP-fraction bucket: full, >70%, 50-70%, 20-50%, <20%, dead.
    private Texture2D[] _mugshotStates;

    // The portrait of the fighter in play (its own mugshot, tinted as its HP falls), and the
    // magazine shown as pips under it (audit v0.11.1 #6: one face for all five; no magazine).
    private Texture2D _portrait;
    private Label _magazineLabel;

    /// <summary>A protagonist's own mugshot, or null when it has none.</summary>
    private static Texture2D MugshotOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        string path = $"res://Assets/Sprites/Protagonists/{id}/UI/Mugshot.png";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _hpBar = GetNode<HpBlockBar>("HpBar");
        _hpLabel = GetNode<Label>("HpBar/HpLabel");
        _livesLabel = GetNode<Label>("LivesLabel");
        _banner = GetNode<Label>("Banner");
        GetNode<Label>("VersionLabel").Text = "v" + GameVersion.Current;

        _missionTitleLabel = GetNode<Label>("MissionTitleLabel");
        _progressLabel = GetNode<Label>("ProgressLabel");
        _timerLabel = GetNode<Label>("TimerLabel");
        _secondaryBar = GetNode<ProgressBar>("SecondaryBar");
        _secondaryBarLabel = GetNode<Label>("SecondaryBar/SecondaryBarLabel");

        _rewardChoice = GetNode<Control>("RewardChoice");
        _atkChoiceButton = GetNode<Button>("RewardChoice/AtkButton");
        _defChoiceButton = GetNode<Button>("RewardChoice/DefButton");
        _atkChoiceButton.Pressed += () => RewardChoicePressed?.Invoke(true);
        _defChoiceButton.Pressed += () => RewardChoicePressed?.Invoke(false);

        _finishDayButton = GetNode<Button>("FinishDayButton");
        _finishDayButton.Pressed += () => FinishDayPressed?.Invoke();

        _rulesLabel = GetNodeOrNull<Label>("RulesLabel");
        if (_rulesLabel != null) _rulesLabel.Visible = false;
        _toastLabel = GetNode<Label>("ToastLabel");
        _toastLabel.Visible = false;

        _mugshot = GetNode<TextureRect>("StatusCluster/Mugshot");
        _ultRing = GetNode<TextureProgressBar>("StatusCluster/UltRing");
        _shiftCd = GetNode<TextureProgressBar>("StatusCluster/ShiftSlot/CooldownOverlay");
        _shiftCdLabel = GetNode<Label>("StatusCluster/ShiftSlot/CdLabel");
        _eCd = GetNode<TextureProgressBar>("StatusCluster/ESlot/CooldownOverlay");
        _eCdLabel = GetNode<Label>("StatusCluster/ESlot/CdLabel");
        _itemSlot = GetNode<Control>("StatusCluster/ItemSlot");
        _itemCd = GetNode<TextureProgressBar>("StatusCluster/ItemSlot/CooldownOverlay");
        _itemCdLabel = GetNode<Label>("StatusCluster/ItemSlot/CdLabel");
        _switchSlot = GetNode<Control>("StatusCluster/SwitchSlot");
        _switchIcon = GetNode<TextureRect>("StatusCluster/SwitchSlot/Icon");
        _switchCdOverlay = GetNode<TextureProgressBar>("StatusCluster/SwitchSlot/CooldownOverlay");
        _switchCdLabel = GetNode<Label>("StatusCluster/SwitchSlot/CdLabel");

        _mugshotStates = new Texture2D[]
        {
            GD.Load<Texture2D>("res://Assets/Sprites/UI/mugshot_full.svg"),
            GD.Load<Texture2D>("res://Assets/Sprites/UI/mugshot_high.svg"),
            GD.Load<Texture2D>("res://Assets/Sprites/UI/mugshot_mid.svg"),
            GD.Load<Texture2D>("res://Assets/Sprites/UI/mugshot_low.svg"),
            GD.Load<Texture2D>("res://Assets/Sprites/UI/mugshot_critical.svg"),
            GD.Load<Texture2D>("res://Assets/Sprites/UI/mugshot_dead.svg"),
        };

        _magazineLabel = new Label
        {
            Name = "MagazineLabel",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(_mugshot.OffsetLeft - 8f, _mugshot.OffsetBottom + 2f),
            Size = new Vector2(_mugshot.OffsetRight - _mugshot.OffsetLeft + 16f, 20f),
        };
        _magazineLabel.AddThemeFontSizeOverride("font_size", 14);
        GetNode<Control>("StatusCluster").AddChild(_magazineLabel);

        // Sane defaults until GameManager pushes real mission state.
        _secondaryBar.Visible = false;
        _rewardChoice.Visible = false;
        _finishDayButton.Visible = false;
        _timerLabel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_player == null) return;

        UpdateCooldownSlot(_shiftCd, _shiftCdLabel, _player.ShiftCooldown);
        UpdateCooldownSlot(_eCd, _eCdLabel, _player.ESkillCooldown);
        (int shots, int size) = _player.Magazine;
        _magazineLabel.Text = size > 0 ? new string('●', Mathf.Clamp(shots, 0, size)) + new string('○', Mathf.Max(0, size - shots)) : "";
    }

    private static void UpdateCooldownSlot(TextureProgressBar overlay, Label label, (float Remaining, float Max) cd)
    {
        overlay.Value = cd.Max > 0f ? Mathf.Clamp(cd.Remaining / cd.Max, 0f, 1f) : 0f;
        label.Text = cd.Remaining > 0.05f ? Mathf.CeilToInt(cd.Remaining).ToString() : "";
    }

    /// <summary>Hook up the player for the parts of the cluster this HUD polls
    /// itself (ult charge via event, cooldowns via per-frame read). HP still
    /// flows through the existing GameManager -> SetHp wiring. Unsubscribes
    /// any previous player first — C# events don't auto-disconnect freed Godot
    /// objects (same class as the KNOWLEDGE.md v0.5.4 autoload-event leak).</summary>
    public void SetPlayer(CharacterController player)
    {
        if (_player != null)
            _player.UltChargeChanged -= SetUltCharge;
        _player = player;
        if (player != null)
        {
            player.UltChargeChanged += SetUltCharge;
            SetUltCharge(player.UltCharge, player.MaxUltCharge);
        }
    }

    /// <summary>Show the next protagonist's mugshot in the Switch slot (the one Tab will
    /// switch to). Pass null or an unrecognized Id to hide the slot. Placeholder art is
    /// used until real character mugshots land.</summary>
    public void SetNextProtagonist(string id)
    {
        _switchNextId = id;
        Texture2D face = MugshotOf(id);
        if (face != null)
        {
            _switchIcon.Texture = face;
            _switchSlot.Visible = true;
        }
        else
        {
            _switchSlot.Visible = false;
        }
    }

    /// <summary>Update the Switch slot's cooldown overlay and label (called each frame by
    /// GameManager). When the CD is active the icon dims and the label shows remaining
    /// seconds; when ready the icon is full-bright and the label is cleared.</summary>
    public void SetSwitchCooldown(float remaining, float max)
    {
        bool onCd = remaining > 0.05f && max > 0f;
        _switchCdOverlay.Value = max > 0f ? Mathf.Clamp(remaining / max, 0f, 1f) : 0f;
        _switchCdLabel.Text = onCd ? Mathf.CeilToInt(remaining).ToString() : "";
        // Dim the icon while the switch is on cooldown — same visual language as the
        // skill slots' radial fill, but on the mugshot itself.
        _switchIcon.SelfModulate = onCd ? new Color(0.35f, 0.35f, 0.35f, 1f) : Colors.White;
    }

    /// <summary>Drive the existing F/item cooldown radial. Map-only or passive-only held items
    /// remain visible but have no radial fill, making the equipped state legible in combat.</summary>
    public void SetHeldItem(string defId, float remaining, float max)
    {
        bool held = !string.IsNullOrWhiteSpace(defId);
        _itemSlot.Visible = held;
        if (!held) return;
        _itemSlot.TooltipText = $"{ItemCatalog.DisplayName(defId)} — F";
        UpdateCooldownSlot(_itemCd, _itemCdLabel, (remaining, max));
    }

    public void SetUltCharge(float cur, float max)
    {
        _ultRing.MaxValue = max;
        _ultRing.Value = cur;
    }

    /// <summary>Read-only, for checks: whether the Switch slot shows, and the faces drawn.</summary>
    public bool SwitchSlotShown => _switchSlot.Visible;
    public Texture2D SwitchFace => _switchIcon.Texture;
    public Texture2D PortraitFace => _mugshot.Texture;
    public string MagazineText => _magazineLabel?.Text ?? "";

    /// <summary>Whose face the portrait shows: the fighter in play. With no mugshot of its own
    /// the generic mood faces stand in.</summary>
    public void SetPortrait(string protagonistId)
    {
        _portrait = MugshotOf(protagonistId);
        _mugshot.SelfModulate = Colors.White;
    }

    public void SetHp(float cur, float max, float shield = 0f, float tempHP = 0f)
    {
        _hpBar.SetValues(cur, max, shield, tempHP);

        float total = cur + shield + tempHP;
        _hpLabel.Text = $"{Mathf.CeilToInt(total)} / {Mathf.CeilToInt(max)}";

        // Mugshot state is driven by normal HP ratio — shield and tempHP are
        // temporary buffers that don't affect the character portrait.
        float frac = max > 0f ? cur / max : 0f;
        int state = frac >= 1f ? 0 : frac > 0.7f ? 1 : frac > 0.5f ? 2 : frac > 0.2f ? 3 : frac > 0f ? 4 : 5;
        if (_portrait == null)
        {
            _mugshot.Texture = _mugshotStates[state];
            return;
        }
        // The fighter's own face, flushing red as its HP falls and grey when it is down.
        _mugshot.Texture = _portrait;
        _mugshot.SelfModulate = state switch
        {
            5 => new Color(0.35f, 0.35f, 0.38f),
            4 => new Color(1f, 0.45f, 0.45f),
            3 => new Color(1f, 0.72f, 0.72f),
            _ => Colors.White,
        };
    }

    public void SetLives(int lives) => _livesLabel.Text = $"Lives: {lives}";
    public void SetLivesVisible(bool visible) => _livesLabel.Visible = visible;

    // ── Mission panel (G4) ────────────────────────────────────────────────────────────────

    public void SetMissionTitle(string text) => _missionTitleLabel.Text = text;

    /// <summary>The rules of this place, one per line, under the HP bar for the whole fight: a
    /// realm's law, zone 6's state, the seals on you (Docs/Zone6.gdd §6). None hides it.</summary>
    public void SetRulesLines(System.Collections.Generic.IReadOnlyList<string> lines)
    {
        if (_rulesLabel == null) return;
        bool any = lines != null && lines.Count > 0;
        _rulesLabel.Text = any ? string.Join("\n", lines) : "";
        _rulesLabel.Visible = any;
    }

    /// <summary>What the rules label shows right now (checks read it).</summary>
    public string RulesText => _rulesLabel != null && _rulesLabel.Visible ? _rulesLabel.Text : "";
    public void SetProgress(string text) => _progressLabel.Text = text;

    public void SetTimer(bool hasTimer, float remaining)
    {
        _timerLabel.Visible = hasTimer;
        if (!hasTimer) return;
        int secs = Mathf.Max(0, Mathf.CeilToInt(remaining));
        _timerLabel.Text = $"{secs / 60:00}:{secs % 60:00}";
    }

    public void SetSecondaryBar(bool has, float value, float max, string label)
    {
        _secondaryBar.Visible = has;
        if (!has) return;
        _secondaryBar.MaxValue = max;
        _secondaryBar.Value = value;
        _secondaryBarLabel.Text = $"{label} {Mathf.CeilToInt(value)} / {Mathf.CeilToInt(max)}";
    }

    /// <summary>Gold over the first <paramref name="share"/> of the secondary bar, with a notch
    /// where it ends — Māra's lock (Docs/Mara.gdd §7.2). 0 hides it.</summary>
    public void SetSecondaryNotch(float share)
    {
        bool show = share > 0f && share < 1f && _secondaryBar.Visible;
        if (_secondaryGold == null)
        {
            if (!show) return;
            _secondaryGold = new ColorRect { Name = "Lock", Color = new Color(0.96f, 0.78f, 0.28f, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
            _secondaryNotch = new ColorRect { Name = "LockNotch", Color = new Color(1f, 0.86f, 0.36f), MouseFilter = Control.MouseFilterEnum.Ignore };
            _secondaryBar.AddChild(_secondaryGold);
            _secondaryBar.AddChild(_secondaryNotch);
        }
        _secondaryGold.Visible = _secondaryNotch.Visible = show;
        if (!show) return;
        Vector2 size = _secondaryBar.Size;
        _secondaryGold.Position = Vector2.Zero;
        _secondaryGold.Size = new Vector2(size.X * share, size.Y);
        _secondaryNotch.Position = new Vector2(size.X * share - 1.5f, -3f);
        _secondaryNotch.Size = new Vector2(3f, size.Y + 6f);
    }

    public void ShowRewardChoice(bool show) => _rewardChoice.Visible = show;

    public void ConfigureFinishDay(bool visible) => _finishDayButton.Visible = visible;
    public void SetFinishDayEnabled(bool enabled) => _finishDayButton.Disabled = !enabled;

    /// <summary>Show a short-lived toast (reward delivery notice, Q3). Guarded so an older toast's
    /// auto-hide can't clobber a newer one shown in the meantime.</summary>
    public async void ShowToast(string text, float duration = 2.5f)
    {
        _toastToken = text + ":" + Time.GetTicksMsec();
        string token = _toastToken;
        _toastLabel.Text = text;
        _toastLabel.Visible = true;
        await ToSignal(GetTree().CreateTimer(duration), SceneTreeTimer.SignalName.Timeout);
        if (_toastToken == token) _toastLabel.Visible = false;
    }

    // ── Debug win/lose banner ─────────────────────────────────────────────────────────────

    public void ShowBanner(string message)
    {
        _banner.Text = message;
        _banner.Visible = true;
    }

    public void HideBanner() => _banner.Visible = false;
}
