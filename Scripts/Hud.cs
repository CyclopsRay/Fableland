using Godot;

/// <summary>
/// Minimal HUD — HP bar, lives, page count, a centered banner for win/lose, and
/// the bottom-left character status cluster (mugshot, ult charge ring, and
/// Shift/E/item cooldown icons). Built mostly in the scene (Hud.tscn) with
/// dynamic bits (mugshot state, cooldown polling) driven from here.
/// </summary>
public partial class Hud : CanvasLayer
{
    private ProgressBar _hpBar;
    private Label _hpLabel;
    private Label _livesLabel;
    private Label _pagesLabel;
    private Label _banner;

    private TextureRect _mugshot;
    private TextureProgressBar _ultRing;
    private TextureProgressBar _shiftCd;
    private Label _shiftCdLabel;
    private TextureProgressBar _eCd;
    private Label _eCdLabel;

    private CharacterController _player;

    // Indexed by HP-fraction bucket: full, >70%, 50-70%, 20-50%, <20%, dead.
    private Texture2D[] _mugshotStates;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _hpBar = GetNode<ProgressBar>("HpBar");
        _hpLabel = GetNode<Label>("HpBar/HpLabel");
        _livesLabel = GetNode<Label>("LivesLabel");
        _pagesLabel = GetNode<Label>("PagesLabel");
        _banner = GetNode<Label>("Banner");
        GetNode<Label>("VersionLabel").Text = "v" + GameVersion.Current;

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

    public void SetPages(int pages, int target) => _pagesLabel.Text = $"WonderPages: {pages} / {target}";

    public void ShowBanner(string message)
    {
        _banner.Text = message;
        _banner.Visible = true;
    }

    public void HideBanner() => _banner.Visible = false;
}
