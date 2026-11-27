using System;
using Godot;
using Fableland.Run;

namespace Fableland.UI;

/// <summary>
/// The Settings screen (Docs/Feedback.gdd §7; audit v0.11.1 #20 — it was a placeholder
/// sentence): a slider for each of <see cref="FeedbackSettings"/> — comfort first, so every
/// shake, flash and freeze can be turned down to nothing — and the four volumes. Changes apply
/// at once; closing saves them (<see cref="SettingsStore"/>). Opened from the pause menu and
/// the title; works while the tree is paused.
/// </summary>
public partial class SettingsDialog : AcceptDialog
{
    private static readonly (string Label, Func<float> Get, Action<float> Set)[] Rows =
    {
        ("Screen shake", () => FeedbackSettings.ScreenShake, v => FeedbackSettings.ScreenShake = v),
        ("Camera punch", () => FeedbackSettings.CameraPunch, v => FeedbackSettings.CameraPunch = v),
        ("Hit flash", () => FeedbackSettings.Flash, v => FeedbackSettings.Flash = v),
        ("Hit-stop", () => FeedbackSettings.HitStop, v => FeedbackSettings.HitStop = v),
        ("Particles", () => FeedbackSettings.Particles, v => FeedbackSettings.Particles = v),
        ("Master volume", () => FeedbackSettings.MasterVolume, v => FeedbackSettings.MasterVolume = v),
        ("Music", () => FeedbackSettings.MusicVolume, v => FeedbackSettings.MusicVolume = v),
        ("Sound effects", () => FeedbackSettings.SfxVolume, v => FeedbackSettings.SfxVolume = v),
        ("Interface sounds", () => FeedbackSettings.UiVolume, v => FeedbackSettings.UiVolume = v),
    };

    private readonly HSlider[] _sliders = new HSlider[Rows.Length];
    private readonly Label[] _values = new Label[Rows.Length];

    /// <summary>Opens the screen over <paramref name="owner"/>.</summary>
    public static SettingsDialog Open(Node owner)
    {
        var dialog = new SettingsDialog();
        owner.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(460, 0));
        return dialog;
    }

    public override void _Ready()
    {
        Title = "Settings";
        ProcessMode = ProcessModeEnum.Always;
        OkButtonText = "Done";
        SettingsStore.EnsureLoaded();

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        box.AddChild(grid);
        for (int i = 0; i < Rows.Length; i++)
        {
            int row = i;
            grid.AddChild(new Label { Text = Rows[i].Label });
            _sliders[i] = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, CustomMinimumSize = new Vector2(200f, 0f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            _values[i] = new Label { CustomMinimumSize = new Vector2(44f, 0f), HorizontalAlignment = HorizontalAlignment.Right };
            grid.AddChild(_sliders[i]);
            grid.AddChild(_values[i]);
            _sliders[i].ValueChanged += value =>
            {
                Rows[row].Set((float)value);
                _values[row].Text = $"{Mathf.RoundToInt(Rows[row].Get() * 100f)}%";
            };
        }
        var reset = new Button { Text = "Back to the defaults" };
        reset.Pressed += () =>
        {
            FeedbackSettings.ResetToDefaults();
            Refresh();
        };
        box.AddChild(reset);
        box.AddChild(new Label
        {
            Text = "Every shake, flash and freeze can be turned down to nothing; the fight stays readable.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(420f, 0f),
        });
        Refresh();

        Confirmed += Finish;
        Canceled += Finish;
    }

    private void Refresh()
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            _sliders[i].SetValueNoSignal(Rows[i].Get());
            _values[i].Text = $"{Mathf.RoundToInt(Rows[i].Get() * 100f)}%";
        }
    }

    private void Finish()
    {
        SettingsStore.Save();
        QueueFree();
    }
}
