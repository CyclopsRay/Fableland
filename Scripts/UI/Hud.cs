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
    private Label _secondaryBarLabel;

    private Control _rewardChoice;
    private Button _atkChoiceButton;
    private Button _defChoiceButton;

    private Button _finishDayButton;
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

    // Lazy-loaded next-protagonist mugshot textures (placeholder art until real mugshots land).
    // Keyed by protagonist Id; only the two implemented characters have entries.
    private static readonly System.Collections.Generic.Dictionary<string, string> _nextMugshotPaths = new()
    {
        {"Pomegraknight", "res://Assets/Sprites/UI/mugshot_next_pomegraknight.svg"},
        {"PumpKing", "res://Assets/Sprites/UI/mugshot_next_pumpking.svg"},
    };

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
        if (id != null && _nextMugshotPaths.TryGetValue(id, out string path))
        {
            _switchIcon.Texture = GD.Load<Texture2D>(path);
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

    public void SetHp(float cur, float max, float shield = 0f, float tempHP = 0f)
    {
        _hpBar.SetValues(cur, max, shield, tempHP);

        float total = cur + shield + tempHP;
        _hpLabel.Text = $"{Mathf.CeilToInt(total)} / {Mathf.CeilToInt(max)}";

        // Mugshot state is driven by normal HP ratio — shield and tempHP are
        // temporary buffers that don't affect the character portrait.
        float frac = max > 0f ? cur / max : 0f;
        int state = frac >= 1f ? 0 : frac > 0.7f ? 1 : frac > 0.5f ? 2 : frac > 0.2f ? 3 : frac > 0f ? 4 : 5;
        _mugshot.Texture = _mugshotStates[state];
    }

    public void SetLives(int lives) => _livesLabel.Text = $"Lives: {lives}";
    public void SetLivesVisible(bool visible) => _livesLabel.Visible = visible;

    // ── Mission panel (G4) ────────────────────────────────────────────────────────────────

    public void SetMissionTitle(string text) => _missionTitleLabel.Text = text;
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
