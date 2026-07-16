using Godot;
using Fableland.Map;
using Fableland.Run;

/// <summary>
/// Title menu. Play opens the three persistent run slots: an empty slot starts a fresh seeded
/// run, while an occupied one resumes its checkpoint. File details remain behind RunState.
/// </summary>
public partial class MenuController : Control
{
    private Control _mainChoices;
    private Control _slotPanel;
    private Label _slotStatus;
    private readonly Button[] _slotButtons = new Button[SaveGameService.SlotCount];

    public override void _Ready()
    {
        GetNode<Button>("Center/VBox/MapCreationButton").Pressed += OnMapCreation;
        GetNode<Button>("Center/VBox/StartButton").Pressed += ShowSaveSlots;
        _mainChoices = GetNode<Control>("Center/VBox");
        _slotPanel = GetNode<Control>("Center/SlotPanel");
        _slotStatus = GetNode<Label>("Center/SlotPanel/Status");
        _slotPanel.Visible = false;
        for (int i = 0; i < _slotButtons.Length; i++)
        {
            int slot = i;
            _slotButtons[i] = GetNode<Button>($"Center/SlotPanel/Slot{slot + 1}");
            _slotButtons[i].Pressed += () => SelectSlot(slot);
        }
        GetNode<Button>("Center/SlotPanel/BackButton").Pressed += HideSaveSlots;
    }

    private void OnMapCreation()
    {
        GetTree().ChangeSceneToFile("res://Scenes/MapCreation/MapBrowser.tscn");
    }

    private void ShowSaveSlots()
    {
        _mainChoices.Visible = false;
        _slotPanel.Visible = true;
        _slotStatus.Visible = false;
        RefreshSlots();
    }

    private void HideSaveSlots()
    {
        _slotPanel.Visible = false;
        _mainChoices.Visible = true;
    }

    private void RefreshSlots()
    {
        SaveSlotInfo[] slots = RunState.Instance?.GetSaveSlots();
        for (int i = 0; i < _slotButtons.Length; i++)
        {
            SaveSlotInfo info = slots != null && i < slots.Length
                ? slots[i] : new SaveSlotInfo(i, false, "Empty");
            _slotButtons[i].Text = info.Occupied
                ? $"Slot {i + 1}\n{info.Summary}"
                : $"Slot {i + 1}\nNew Game";
        }
    }

    private void SelectSlot(int slot)
    {
        RunState run = RunState.Instance;
        if (run == null) { ShowSlotError("Run system is unavailable."); return; }

        SaveSlotInfo info = SaveGameService.GetSlotInfo(slot);
        // A corrupt or newer-version file is neither an empty slot nor a safe candidate for
        // overwrite. Keep it intact and surface the loader's diagnostic instead.
        if (!string.IsNullOrEmpty(info.Error))
        {
            ShowSlotError(info.Error);
            return;
        }
        string loadError;
        bool success;
        if (info.Occupied)
            success = run.TryLoadRunFromSlot(slot, out loadError);
        else
            success = run.StartNewRunInSlot(slot, DetRandom.NewSeed(), out loadError);
        if (!success)
        {
            ShowSlotError(loadError);
            RefreshSlots();
            return;
        }
        run.ResumeLoadedRun();
    }

    private void ShowSlotError(string text)
    {
        _slotStatus.Text = string.IsNullOrWhiteSpace(text) ? "Unable to use that slot." : text;
        _slotStatus.Visible = true;
    }
}
