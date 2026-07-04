using Godot;

/// <summary>
/// Minimal HUD — HP bar, lives, page count, and a centered banner for win/lose.
/// Built entirely in code so the scene wiring stays trivial for the prototype.
/// </summary>
public partial class Hud : CanvasLayer
{
    private ProgressBar _hpBar;
    private Label _hpLabel;
    private Label _livesLabel;
    private Label _pagesLabel;
    private Label _banner;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _hpBar = GetNode<ProgressBar>("HpBar");
        _hpLabel = GetNode<Label>("HpBar/HpLabel");
        _livesLabel = GetNode<Label>("LivesLabel");
        _pagesLabel = GetNode<Label>("PagesLabel");
        _banner = GetNode<Label>("Banner");
        GetNode<Label>("VersionLabel").Text = "v" + GameVersion.Current;
    }

    public void SetHp(float cur, float max)
    {
        _hpBar.MaxValue = max;
        _hpBar.Value = cur;
        _hpLabel.Text = $"{Mathf.CeilToInt(cur)} / {Mathf.CeilToInt(max)}";
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
