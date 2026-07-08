using Godot;
using Fableland.Run;

/// <summary>
/// Placeholder shelter Adventure scene (v0.5.0). A menu of the shelter's actions; Phase 4 will
/// flesh out plantations, traders, jousting, build/item management. For now it covers the
/// day-ending Blessing actions (Rest / Sharpen Weapon / Sharpen Armor, NODES §5.5), leaving via
/// Finish the Day, and free "Back to Map" (no day end) while stamina remains.
///
/// Null-tolerant: launchable via F5 (no run) — falls back to a debug node id, Blessed.
/// </summary>
public partial class ShelterController : CanvasLayer
{
    private const string BlessingConfirm =
        "You will use all your stamina and the Blessing of this shelter to make the magic happen.";

    private Label _title;
    private Label _status;
    private Button _rest, _sharpenWeapon, _sharpenArmor, _finishDay, _back;

    private string _nodeId;

    public override void _Ready()
    {
        _title = GetNode<Label>("Center/Box/TitleLabel");
        _status = GetNode<Label>("Center/Box/StatusLabel");
        _rest = GetNode<Button>("Center/Box/RestButton");
        _sharpenWeapon = GetNode<Button>("Center/Box/SharpenWeaponButton");
        _sharpenArmor = GetNode<Button>("Center/Box/SharpenArmorButton");
        _finishDay = GetNode<Button>("Center/Box/FinishDayButton");
        _back = GetNode<Button>("Center/Box/BackButton");

        var rs = RunState.Instance;
        _nodeId = rs?.CurrentAdventure?.NodeId ?? "(debug)";

        _rest.Pressed += () => Blessing("Rest — restore 30% max HP (excess → permanent max HP).",
            () => rs?.RestBlessing());
        _sharpenWeapon.Pressed += () => Blessing("Sharpen Weapon — permanently +10 ATK.",
            () => rs?.AddAtk(10));
        _sharpenArmor.Pressed += () => Blessing("Sharpen Armor — permanently +10 DEF.",
            () => rs?.AddDef(10));
        _finishDay.Pressed += () => Confirm("Finish the Day?", null);
        _back.Pressed += OnBack;

        Refresh();
    }

    private void Refresh()
    {
        var rs = RunState.Instance;
        bool blessed = rs?.IsShelterBlessed(_nodeId) ?? true;
        int stamina = rs?.Stamina ?? RunState.MaxStamina;

        _title.Text = $"Shelter {_nodeId}  (placeholder)";
        _status.Text = blessed ? "Blessed — one day-ending action available." : "Mundane — the Blessing is spent.";

        // Blessed shelter's day-ending actions grey out once Mundane (NODES §5).
        _rest.Disabled = !blessed;
        _sharpenWeapon.Disabled = !blessed;
        _sharpenArmor.Disabled = !blessed;
        // Leaving is free but needs stamina to actually move on the map (NODES §5).
        _back.Disabled = stamina <= 0;
    }

    /// <summary>A day-ending Blessing action: GDD confirm text → apply effect, consume Blessing, end day.</summary>
    private void Blessing(string effectLine, System.Action apply)
    {
        Confirm($"{effectLine}\n\n{BlessingConfirm}", () =>
        {
            apply?.Invoke();
            RunState.Instance?.ConsumeBlessing(_nodeId);
        });
    }

    /// <summary>
    /// Shows a Yes/Cancel popup. On Yes: run <paramref name="onYes"/> (if any), then end the day
    /// via RunState (which swaps back to the map, or to RunOver on VOID death).
    /// </summary>
    private void Confirm(string text, System.Action onYes)
    {
        var dlg = new ConfirmationDialog { DialogText = text, Title = "Shelter" };
        AddChild(dlg);
        dlg.Confirmed += () =>
        {
            dlg.QueueFree();
            onYes?.Invoke();
            RunState.Instance?.EndDay();
        };
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered();
    }

    private void OnBack()
    {
        var rs = RunState.Instance;
        if (rs == null || rs.Stamina <= 0) return; // can't move with no stamina
        rs.ReturnToMap(); // free — leaving a shelter does not end the day (NODES §5)
    }
}
