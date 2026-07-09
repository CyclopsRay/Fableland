using Godot;
using System;

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
    private ProgressBar _hpBar;
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

    private CharacterController _player;

    /// <summary>Fired when the player presses Finish the Day.</summary>
    public event Action FinishDayPressed;

    /// <summary>Fired on a reward-choice press: true = ATK, false = DEF.</summary>
    public event Action<bool> RewardChoicePressed;

    // Indexed by HP-fraction bucket: full, >70%, 50-70%, 20-50%, <20%, dead.
    private Texture2D[] _mugshotStates;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _hpBar = GetNode<ProgressBar>("HpBar");
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

        _mugshotStates = new Texture2D[]
        {
            GD.Load<Texture2D>("res://Sprites/UI/mugshot_full.svg"),
            GD.Load<Texture2D>("res://Sprites/UI/mugshot_high.svg"),
            GD.Load<Texture2D>("res://Sprites/UI/mugshot_mid.svg"),
            GD.Load<Texture2D>("res://Sprites/UI/mugshot_low.svg"),
            GD.Load<Texture2D>("res://Sprites/UI/mugshot_critical.svg"),
            GD.Load<Texture2D>("res://Sprites/UI/mugshot_dead.svg"),
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
    /// flows through the existing GameManager -> SetHp wiring.</summary>
    public void SetPlayer(CharacterController player)
    {
        _player = player;
        player.UltChargeChanged += SetUltCharge;
        SetUltCharge(player.UltCharge, player.MaxUltCharge);
    }

    public void SetUltCharge(float cur, float max)
    {
        _ultRing.MaxValue = max;
        _ultRing.Value = cur;
    }

    public void SetHp(float cur, float max)
    {
        _hpBar.MaxValue = max;
        _hpBar.Value = cur;
        _hpLabel.Text = $"{Mathf.CeilToInt(cur)} / {Mathf.CeilToInt(max)}";

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
