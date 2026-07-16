using System;
using Godot;

namespace Fableland.UI;

/// <summary>
/// Reusable pause overlay for map and arena. It processes while the tree is paused so Escape,
/// Continue, Settings, and Save &amp; Quit remain responsive. State changes stay in the caller.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private Func<bool> _saveAndQuit;
    private Label _status;

    public static void Open(Node owner, Func<bool> saveAndQuit)
    {
        if (owner == null || owner.GetTree().Paused) return;
        var menu = new PauseMenu { _saveAndQuit = saveAndQuit };
        owner.AddChild(menu);
        owner.GetTree().Paused = true;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;

        var dim = new ColorRect
        {
            Color = new Color(0.02f, 0.03f, 0.07f, 0.78f),
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(dim);

        var center = new CenterContainer
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(320f, 0f) };
        center.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);

        var title = new Label
        {
            Text = "PAUSED",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);
        box.AddChild(MakeButton("Continue", Close));
        box.AddChild(MakeButton("Settings", ShowSettings));
        box.AddChild(MakeButton("Save & Quit", SaveAndQuit));
        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
        };
        box.AddChild(_status);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("pause") && !@event.IsEcho())
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private static Button MakeButton(string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0f, 50f) };
        button.Pressed += pressed;
        return button;
    }

    private void ShowSettings()
    {
        var dialog = new AcceptDialog
        {
            Title = "Settings",
            DialogText = "Settings are reserved for the upcoming meta-save. This pause menu is ready to host them.",
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(dialog);
        dialog.Confirmed += dialog.QueueFree;
        dialog.PopupCentered();
    }

    private void SaveAndQuit()
    {
        if (_saveAndQuit?.Invoke() == true)
        {
            Close(unpause: false);
            return;
        }
        _status.Text = "Could not save. Your run is still paused.";
        _status.Visible = true;
    }

    private void Close() => Close(unpause: true);

    private void Close(bool unpause)
    {
        if (unpause && GetTree() != null) GetTree().Paused = false;
        QueueFree();
    }
}
