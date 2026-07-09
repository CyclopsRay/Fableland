using Godot;
using Fableland.Data;
using Fableland.Run;

/// <summary>
/// The generic question-mark ("?") EventRunner (T30 §7): interprets whichever <see cref="EventDef"/>
/// is rolled for this node — ordered pages of authored choices → effects — with no per-event code.
/// Effects are applied through existing RunState verbs (AddWonderCores/HealParty/AddItem); an event
/// author composes <see cref="EventEffect"/>s in <c>Scripts/Data/EventDefs.cs</c>, never touches this
/// file. Per NODES §1.2 the player must resolve the event (reach a page with no further NextPage)
/// before Finish the Day unlocks.
///
/// Null-tolerant: launchable via F5 (no run) — falls back to a debug node id and a fixed event pick
/// (EventDefs.All[0]) so a toolchain-less/no-run launch still renders something.
/// </summary>
public partial class EventController : CanvasLayer
{
    private Label _title, _body, _result;
    private VBoxContainer _choicesBox;
    private Button _finishDay;

    private string _nodeId;
    private EventDef _def;
    private int _pageIndex;
    private bool _resolved;

    public override void _Ready()
    {
        _title = GetNode<Label>("Center/Box/TitleLabel");
        _body = GetNode<Label>("Center/Box/BodyLabel");
        _choicesBox = GetNode<VBoxContainer>("Center/Box/ChoicesBox");
        _result = GetNode<Label>("Center/Box/ResultLabel");
        _finishDay = GetNode<Button>("Center/Box/FinishDayButton");

        _nodeId = RunState.Instance?.CurrentAdventure?.NodeId ?? "(debug)";
        _def = PickEvent(_nodeId);
        _pageIndex = 0;
        _resolved = false;

        _result.Text = "";
        _finishDay.Disabled = true; // must resolve the event first (NODES §1.2)
        _finishDay.Pressed += OnFinishDay;

        RenderPage();
    }

    /// <summary>
    /// Deterministic pick: RunState.Rng.Sub("event:" + nodeId) so the same node always rolls the
    /// same event in a given run, and no other DetRandom stream shifts (KNOWLEDGE determinism
    /// rule). No run in progress (F5 straight into Event.tscn) ⇒ fixed pick, index 0.
    /// </summary>
    private static EventDef PickEvent(string nodeId)
    {
        var rng = RunState.Instance?.Rng?.Sub("event:" + nodeId);
        return rng != null ? rng.Pick(EventDefs.All) : EventDefs.All[0];
    }

    private void RenderPage()
    {
        var page = _def?.Pages != null && _pageIndex >= 0 && _pageIndex < _def.Pages.Length
            ? _def.Pages[_pageIndex]
            : null;

        _title.Text = _def?.Title ?? "Event";
        _body.Text = page?.Text ?? "";

        foreach (Node c in _choicesBox.GetChildren()) c.QueueFree();

        if (page?.Choices == null) return;
        foreach (var choice in page.Choices)
        {
            var btn = new Button { Text = choice?.Label ?? "...", CustomMinimumSize = new Vector2(0, 40) };
            btn.Pressed += () => OnChoice(choice);
            _choicesBox.AddChild(btn);
        }
    }

    private void OnChoice(EventChoice choice)
    {
        if (choice == null || _resolved) return;

        // Hide+disable immediately so a stray double-click can't double-apply effects while the
        // old buttons' QueueFree (deferred to end-of-frame) is still pending.
        foreach (Node c in _choicesBox.GetChildren())
            if (c is Button b) { b.Disabled = true; b.Visible = false; }

        if (choice.Effects != null)
            foreach (var fx in choice.Effects) ApplyEffect(fx);

        _result.Text = choice.ResultText ?? "";

        bool hasNextPage = choice.NextPage >= 0 && _def?.Pages != null && choice.NextPage < _def.Pages.Length;
        if (hasNextPage)
        {
            _pageIndex = choice.NextPage;
            RenderPage();
        }
        else
        {
            Finish();
        }
    }

    /// <summary>Apply one verb via the existing RunState pipeline. Null-tolerant (no run ⇒ no-op).</summary>
    private static void ApplyEffect(EventEffect fx)
    {
        if (fx == null) return;
        switch (fx.Kind)
        {
            case EventEffectKind.GrantCores:
                RunState.Instance?.AddWonderCores(fx.IntArg);
                break;
            case EventEffectKind.HealParty:
                RunState.Instance?.HealParty(fx.FloatArg);
                break;
            case EventEffectKind.GrantItem:
                RunState.Instance?.AddItem(fx.StringArg);
                break;
            case EventEffectKind.Nothing:
            default:
                break; // explicit no-op verb (e.g. "walk away")
        }
    }

    private void Finish()
    {
        if (_resolved) return;
        _resolved = true;
        RunState.Instance?.ResolveEvent(_nodeId);
        _finishDay.Disabled = false; // event resolved → the day can be finished (NODES §1.2)
    }

    private void OnFinishDay()
    {
        if (!_resolved) return;
        var dlg = new ConfirmationDialog { DialogText = "Finish the Day?", Title = "Event" };
        AddChild(dlg);
        dlg.Confirmed += () => { dlg.QueueFree(); RunState.Instance?.EndDay(); };
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered();
    }
}
