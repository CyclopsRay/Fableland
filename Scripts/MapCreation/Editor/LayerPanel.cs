namespace Fableland.MapCreation.Editor;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>
/// GDD §1, §7.1, §7.4, §7.7 — fills <see cref="MapEditor.LayersBox"/> with the
/// layer stack UI + the properties sub-panel for whatever is currently selected
/// (a layer row, or the Canvas row). A plain class (not a Node) so MapEditor stays
/// slim; it owns no scene-tree lifetime of its own beyond the Controls it adds as
/// children of the <see cref="VBoxContainer"/> MapEditor hands it.
///
/// PANEL ORDER MAPPING (brief, spelled out once here): the document's
/// <c>Layers</c> list is back-to-front draw order (farview…, battlefield,
/// closeview…), but the panel wants "front on top, Photoshop-style" — so the
/// panel's row order is simply the REVERSE of <c>doc.Layers</c>, with a Canvas
/// row appended after (Canvas is not a layer, always last/bottom). Reversing the
/// whole list also makes "move up on screen" always mean "swap with the next
/// HIGHER document index" and "move down on screen" always mean "swap with the
/// next LOWER document index", uniformly across every role band — see
/// <see cref="CanReorder"/>.
///
/// REBUILD-VS-REFRESH STRATEGY (brief-mandated): subscribes once to
/// <see cref="EditorState.StateChanged"/> and <see cref="CommandStack.Changed"/>.
/// A small "structural signature" (canvas-selected flag + current layer index +
/// every layer's Role:Name, in order) is recomputed on every notification; if it
/// changed, the rows/add-buttons/properties sub-panel are torn down and rebuilt
/// from scratch (cheap at this scale, and safe: Name only ever commits on blur,
/// so a signature change never happens while a LineEdit is mid-edit). If the
/// signature is unchanged, only <see cref="RefreshPropsInPlace"/> runs, which
/// updates the properties sub-panel's EXISTING controls' displayed values
/// (SetValueNoSignal / conditional Text assignment) — this is what keeps an
/// Undo/Redo of a property edit visible without ever tearing down a control the
/// user might currently have focused.
/// </summary>
public sealed class LayerPanel
{
    private readonly EditorState _state;
    private readonly VBoxContainer _root;

    private readonly VBoxContainer _rowsContainer;
    private readonly HBoxContainer _addButtonsContainer;
    private readonly VBoxContainer _propsContainer;
    private readonly ConfirmationDialog _removeConfirmDialog;

    /// <summary>UI-only "canvas properties mode" — not part of EditorState (canvas is
    /// not a layer, and no other panel needs to know about this selection).</summary>
    private bool _canvasSelected;

    private MapLayerData _pendingRemoveLayer;
    private string _lastSignature;

    // Property-sub-panel control refs, kept across a rebuild's lifetime so
    // RefreshPropsInPlace can update values without recreating controls.
    private LineEdit _nameEdit;
    private SpinBox _parallaxXSpin;
    private SpinBox _parallaxYSpin;
    private CheckBox _loopCheck;
    private CheckBox _collisionCheck;
    private LineEdit _tintEdit;
    private ColorRect _tintSwatch;
    private SpinBox _opacitySpin;
    private SpinBox _swayAmpSpin;
    private SpinBox _swayPeriodSpin;
    private SpinBox _autoXSpin;
    private SpinBox _autoYSpin;
    private SpinBox _gridWSpin;
    private SpinBox _gridHSpin;

    private LineEdit _canvasColorEdit;
    private ColorRect _canvasColorSwatch;

    public LayerPanel(EditorState state, VBoxContainer container)
    {
        _state = state;
        _root = container;

        _rowsContainer = new VBoxContainer();
        _root.AddChild(_rowsContainer);

        _addButtonsContainer = new HBoxContainer();
        _root.AddChild(_addButtonsContainer);

        _root.AddChild(new HSeparator());
        _root.AddChild(new Label { Text = "Properties" });

        _propsContainer = new VBoxContainer();
        _root.AddChild(_propsContainer);

        _removeConfirmDialog = new ConfirmationDialog { Title = "Remove layer?" };
        _root.AddChild(_removeConfirmDialog);
        _removeConfirmDialog.Confirmed += () =>
        {
            if (_pendingRemoveLayer != null) ExecuteRemove(_pendingRemoveLayer);
            _pendingRemoveLayer = null;
        };

        _state.StateChanged += OnAnyChange;
        _state.Commands.Changed += OnAnyChange;

        OnAnyChange(); // initial build
    }

    // ---------------------------------------------------------------- change routing

    private void OnAnyChange()
    {
        string sig = ComputeSignature();
        if (sig != _lastSignature)
        {
            _lastSignature = sig;
            Rebuild();
        }
        else
        {
            RefreshPropsInPlace();
        }
    }

    private string ComputeSignature()
    {
        var doc = _state.Document;
        if (doc?.Layers == null) return "nodoc";

        var sb = new StringBuilder();
        sb.Append(_canvasSelected ? '1' : '0').Append('|');
        sb.Append(_state.CurrentLayerIndex).Append('|');
        foreach (var l in doc.Layers)
            sb.Append(l.Role).Append(':').Append(l.Name).Append(';');
        return sb.ToString();
    }

    // ---------------------------------------------------------------- rebuild

    private void Rebuild()
    {
        FreeChildren(_rowsContainer);
        FreeChildren(_addButtonsContainer);
        FreeChildren(_propsContainer);

        var doc = _state.Document;
        if (doc?.Layers == null) return;

        for (int docIdx = doc.Layers.Count - 1; docIdx >= 0; docIdx--)
            _rowsContainer.AddChild(BuildLayerRow(doc.Layers[docIdx], docIdx));
        _rowsContainer.AddChild(BuildCanvasRow());

        BuildAddButtons(doc);
        BuildPropertiesPanel();
    }

    /// <summary>Detaches every child immediately (so a same-frame AddChild right after
    /// never briefly coexists with the old rows — QueueFree alone only defers the actual
    /// free to end-of-frame, it does not remove the child from the tree synchronously)
    /// and queues each for deferred freeing.</summary>
    private static void FreeChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    // ---------------------------------------------------------------- rows

    private Control BuildLayerRow(MapLayerData layer, int docIdx)
    {
        bool isBattlefield = layer.Role == MapLayerData.RoleBattlefield;
        bool isCurrent = !_canvasSelected && docIdx == _state.CurrentLayerIndex;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", RowStyle(isCurrent ? _state.AccentOf(docIdx) : (Color?)null));

        var hbox = new HBoxContainer();
        panel.AddChild(hbox);

        var swatch = new ColorRect
        {
            CustomMinimumSize = new Vector2(6, 24),
            Color = _state.AccentOf(docIdx),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hbox.AddChild(swatch);

        string roleTag = layer.Role switch
        {
            MapLayerData.RoleFarview => "[far]",
            MapLayerData.RoleBattlefield => "[BATTLE]",
            MapLayerData.RoleCloseview => "[close]",
            _ => "",
        };
        hbox.AddChild(new Label
        {
            Text = $"{layer.Name} {roleTag}",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        if (!isBattlefield)
        {
            var upBtn = new Button { Text = "▲", CustomMinimumSize = new Vector2(24, 24), Disabled = !CanReorder(docIdx, +1) };
            upBtn.Pressed += () => DoReorder(docIdx, docIdx + 1);
            hbox.AddChild(upBtn);

            var downBtn = new Button { Text = "▼", CustomMinimumSize = new Vector2(24, 24), Disabled = !CanReorder(docIdx, -1) };
            downBtn.Pressed += () => DoReorder(docIdx, docIdx - 1);
            hbox.AddChild(downBtn);

            var removeBtn = new Button { Text = "✕", CustomMinimumSize = new Vector2(24, 24) };
            removeBtn.Pressed += () => RequestRemove(layer);
            hbox.AddChild(removeBtn);
        }

        panel.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _canvasSelected = false;
                _state.CurrentLayerIndex = docIdx;
                _state.RaiseStateChanged();
            }
        };

        return panel;
    }

    private Control BuildCanvasRow()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", RowStyle(_canvasSelected ? new Color(1f, 1f, 1f) : (Color?)null));

        var hbox = new HBoxContainer();
        panel.AddChild(hbox);

        hbox.AddChild(new ColorRect
        {
            CustomMinimumSize = new Vector2(6, 24),
            Color = ColorFromHex(_state.Document?.Canvas?.Color, new Color(0.53f, 0.81f, 0.92f)),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        hbox.AddChild(new Label { Text = "Canvas [backdrop]", MouseFilter = Control.MouseFilterEnum.Ignore });

        panel.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _canvasSelected = true;
                _state.RaiseStateChanged();
            }
        };

        return panel;
    }

    private static StyleBoxFlat RowStyle(Color? highlight) => new()
    {
        BgColor = highlight.HasValue ? WithAlpha(highlight.Value, 0.25f) : new Color(0, 0, 0, 0),
    };

    /// <summary>GDD §1 ORDERING — a role-band-only neighbor swap is legal only if the
    /// document neighbor at <c>docIdx + dir</c> exists and shares this row's Role.</summary>
    private bool CanReorder(int docIdx, int dir)
    {
        var layers = _state.Document?.Layers;
        if (layers == null) return false;
        int n = docIdx + dir;
        if (n < 0 || n >= layers.Count) return false;
        return layers[n].Role == layers[docIdx].Role;
    }

    private void DoReorder(int fromIdx, int toIdx)
    {
        _state.Commands.Push(new LayerReorderCommand(_state, _state.Document, fromIdx, toIdx));
        _state.RaiseStateChanged();
    }

    private void RequestRemove(MapLayerData layer)
    {
        int tileCount = layer.Tiles?.Count ?? 0;
        if (tileCount > 0)
        {
            _pendingRemoveLayer = layer;
            _removeConfirmDialog.DialogText = $"Remove layer '{layer.Name}' and its {tileCount} tiles?";
            _removeConfirmDialog.PopupCentered();
        }
        else
        {
            ExecuteRemove(layer);
        }
    }

    private void ExecuteRemove(MapLayerData layer)
    {
        var doc = _state.Document;
        int idx = doc?.Layers?.IndexOf(layer) ?? -1;
        if (idx < 0) return;
        _state.Commands.Push(new LayerRemoveCommand(_state, doc, layer, idx));
        _state.RaiseStateChanged();
    }

    // ---------------------------------------------------------------- add buttons

    private void BuildAddButtons(MapDocument doc)
    {
        int farviewCount = doc.Layers.Count(l => l.Role == MapLayerData.RoleFarview);
        int closeviewCount = doc.Layers.Count(l => l.Role == MapLayerData.RoleCloseview);

        var addFarBtn = new Button { Text = "+ Farview", Disabled = farviewCount >= 8 };
        addFarBtn.Pressed += () => AddLayer(MapLayerData.RoleFarview);
        _addButtonsContainer.AddChild(addFarBtn);

        var addCloseBtn = new Button { Text = "+ Closeview", Disabled = closeviewCount >= 2 };
        addCloseBtn.Pressed += () => AddLayer(MapLayerData.RoleCloseview);
        _addButtonsContainer.AddChild(addCloseBtn);
    }

    private void AddLayer(string role)
    {
        var doc = _state.Document;
        MapLayerData newLayer;
        int insertIndex;

        if (role == MapLayerData.RoleFarview)
        {
            int n = doc.Layers.Count(l => l.Role == MapLayerData.RoleFarview) + 1;
            newLayer = MapLayerData.CreateFarview($"Farview {n}");
            insertIndex = LayerAddCommand.FindBattlefieldIndex(doc); // just below the battlefield
        }
        else
        {
            int n = doc.Layers.Count(l => l.Role == MapLayerData.RoleCloseview) + 1;
            newLayer = MapLayerData.CreateCloseview($"Closeview {n}");
            insertIndex = doc.Layers.Count; // appended at the end
        }

        _canvasSelected = false;
        _state.Commands.Push(new LayerAddCommand(_state, doc, newLayer, insertIndex));
        _state.RaiseStateChanged();
    }

    // ---------------------------------------------------------------- properties panel

    private void BuildPropertiesPanel()
    {
        if (_canvasSelected)
        {
            BuildCanvasProperties();
            return;
        }

        var layer = _state.CurrentLayer;
        if (layer == null) return;
        BuildLayerProperties(layer);
    }

    private void BuildCanvasProperties()
    {
        _canvasColorEdit = null;
        _canvasColorSwatch = null;
        _nameEdit = null; _parallaxXSpin = null; _parallaxYSpin = null; _loopCheck = null;
        _collisionCheck = null; _tintEdit = null; _tintSwatch = null; _opacitySpin = null;
        _swayAmpSpin = null; _swayPeriodSpin = null; _autoXSpin = null; _autoYSpin = null;
        _gridWSpin = null; _gridHSpin = null;

        var canvas = _state.Document.Canvas ??= new CanvasData();

        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Color", CustomMinimumSize = new Vector2(90, 0) });

        _canvasColorSwatch = new ColorRect
        {
            CustomMinimumSize = new Vector2(20, 20),
            Color = ColorFromHex(canvas.Color, new Color(0.53f, 0.81f, 0.92f)),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(_canvasColorSwatch);

        _canvasColorEdit = new LineEdit { Text = canvas.Color ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _canvasColorEdit.TextChanged += t => _canvasColorSwatch.Color = ColorFromHex(t, _canvasColorSwatch.Color);
        _canvasColorEdit.TextSubmitted += _ => _canvasColorEdit.ReleaseFocus();
        _canvasColorEdit.FocusExited += () =>
        {
            string oldV = canvas.Color;
            string newV = _canvasColorEdit.Text.Trim();
            if (oldV == newV) return;
            Commit("Canvas Color", () => canvas.Color = newV, () => canvas.Color = oldV);
        };
        row.AddChild(_canvasColorEdit);

        _propsContainer.AddChild(row);
    }

    /// <summary>GDD §1.2/§3/§4 — every field, with role-appropriate disabling: battlefield
    /// locks Parallax/Loop/Collision/Sway (fixed by design, GDD §1.2 "Battlefield fixes");
    /// closeview locks Collision (never legal, GDD §1 "never" interacts with gameplay) and
    /// Sway (forced 0, GDD §4 "steady"); farview's Collision is gated live by GDD §3.</summary>
    private void BuildLayerProperties(MapLayerData layer)
    {
        bool isBattlefield = layer.Role == MapLayerData.RoleBattlefield;
        bool isCloseview = layer.Role == MapLayerData.RoleCloseview;

        _propsContainer.AddChild(new Label { Text = $"({layer.Role})" });

        // Name
        _nameEdit = AddLineEditRow("Name", layer.Name, commit: v =>
        {
            string old = layer.Name;
            if (old == v) return;
            Commit("Name", () => layer.Name = v, () => layer.Name = old);
        });

        // Parallax never changes Farview's fixed-world collider contract (GDD §3).
        _parallaxXSpin = AddSpinRow("Parallax X", layer.ParallaxX, -4f, 4f, 0.05f, isBattlefield, v =>
        {
            float old = layer.ParallaxX;
            if (Mathf.IsEqualApprox(old, v)) return;
            Commit("Parallax X", () => layer.ParallaxX = v, () => layer.ParallaxX = old);
        });

        _parallaxYSpin = AddSpinRow("Parallax Y", layer.ParallaxY, -4f, 4f, 0.05f, isBattlefield, v =>
        {
            float old = layer.ParallaxY;
            if (Mathf.IsEqualApprox(old, v)) return;
            Commit("Parallax Y", () => layer.ParallaxY = v, () => layer.ParallaxY = old);
        });

        // Loop — looped scenery can never have a fixed Farview collider (GDD §3).
        _loopCheck = AddCheckRow("Loop", layer.Loop, isBattlefield, v =>
        {
            bool old = layer.Loop;
            if (old == v) return;
            bool oldCollision = layer.Collision;
            bool newCollision = oldCollision && CollisionLegal(v);
            Commit("Loop",
                () => { layer.Loop = v; layer.Collision = newCollision; },
                () => { layer.Loop = old; layer.Collision = oldCollision; });
        });

        // Collision — Farview's fixed-world SoftVolume opt-in, GDD §3.
        string collisionLabel = layer.Role == MapLayerData.RoleFarview ? "Collision (SoftVolumes)" : "Collision";
        _collisionCheck = AddCheckRow(collisionLabel, layer.Collision, false, v =>
        {
            bool old = layer.Collision;
            if (old == v) return;
            Commit("Collision", () => layer.Collision = v, () => layer.Collision = old);
        });
        ApplyCollisionGating(layer, _collisionCheck);

        // Tint (hex string; empty = null = no tint) + swatch preview.
        var tintRow = new HBoxContainer();
        tintRow.AddChild(new Label { Text = "Tint", CustomMinimumSize = new Vector2(90, 0) });
        _tintSwatch = new ColorRect
        {
            CustomMinimumSize = new Vector2(20, 20),
            Color = ColorFromHex(layer.Tint, Colors.Transparent),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        tintRow.AddChild(_tintSwatch);
        _tintEdit = new LineEdit { Text = layer.Tint ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _tintEdit.TextChanged += t => _tintSwatch.Color = ColorFromHex(t, Colors.Transparent);
        _tintEdit.TextSubmitted += _ => _tintEdit.ReleaseFocus();
        _tintEdit.FocusExited += () =>
        {
            string old = layer.Tint;
            string raw = _tintEdit.Text.Trim();
            string @new = string.IsNullOrEmpty(raw) ? null : raw;
            if (old == @new) return;
            Commit("Tint", () => layer.Tint = @new, () => layer.Tint = old);
        };
        tintRow.AddChild(_tintEdit);
        _propsContainer.AddChild(tintRow);

        _opacitySpin = AddSpinRow("Opacity", layer.Opacity, 0f, 1f, 0.05f, false, v =>
        {
            float old = layer.Opacity;
            if (Mathf.IsEqualApprox(old, v)) return;
            Commit("Opacity", () => layer.Opacity = v, () => layer.Opacity = old);
        });

        bool lockSway = isBattlefield || isCloseview; // GDD §4: forced 0 on both
        _swayAmpSpin = AddSpinRow("Sway Amp (px)", layer.SwayAmplitudePx, 0f, 64f, 1f, lockSway, v =>
        {
            float old = layer.SwayAmplitudePx;
            if (Mathf.IsEqualApprox(old, v)) return;
            Commit("Sway Amplitude", () => layer.SwayAmplitudePx = v, () => layer.SwayAmplitudePx = old);
        });

        _swayPeriodSpin = AddSpinRow("Sway Period (s)", layer.SwayPeriodSec, 0.1f, 30f, 0.1f, lockSway, v =>
        {
            float old = layer.SwayPeriodSec;
            if (Mathf.IsEqualApprox(old, v)) return;
            Commit("Sway Period", () => layer.SwayPeriodSec = v, () => layer.SwayPeriodSec = old);
        });

        _autoXSpin = AddSpinRow("AutoScroll X", layer.AutoScrollX, -256f, 256f, 1f, false, v =>
        {
            float old = layer.AutoScrollX;
            if (Mathf.IsEqualApprox(old, v)) return;
            Commit("AutoScroll X", () => layer.AutoScrollX = v, () => layer.AutoScrollX = old);
        });

        _autoYSpin = AddSpinRow("AutoScroll Y", layer.AutoScrollY, -256f, 256f, 1f, false, v =>
        {
            float old = layer.AutoScrollY;
            if (Mathf.IsEqualApprox(old, v)) return;
            Commit("AutoScroll Y", () => layer.AutoScrollY = v, () => layer.AutoScrollY = old);
        });

        // GridW/GridH — map bounds (GDD §1.2: default 64x36, max 512x256; every layer's
        // grid is independent, so this applies uniformly regardless of role).
        _gridWSpin = AddSpinRow("Grid W", layer.GridW, 1f, 512f, 1f, false, v =>
        {
            int old = layer.GridW;
            int @new = Mathf.Clamp((int)v, 1, 512);
            if (old == @new) return;
            Commit("Grid W", () => layer.GridW = @new, () => layer.GridW = old);
        });

        _gridHSpin = AddSpinRow("Grid H", layer.GridH, 1f, 256f, 1f, false, v =>
        {
            int old = layer.GridH;
            int @new = Mathf.Clamp((int)v, 1, 256);
            if (old == @new) return;
            Commit("Grid H", () => layer.GridH = @new, () => layer.GridH = old);
        });
    }

    /// <summary>Farview's collider is stationary in world space, independent of its visual
    /// parallax. Only looping is illegal because it would make its visual copies ambiguous.
    /// Battlefield is fixed-on and Closeview fixed-off by role.</summary>
    private static bool CollisionLegal(bool loop) => !loop;

    private void ApplyCollisionGating(MapLayerData layer, CheckBox checkBox)
    {
        if (layer.Role == MapLayerData.RoleBattlefield)
        {
            checkBox.Disabled = true;
            checkBox.TooltipText = "Battlefield collision is fixed by design.";
        }
        else if (layer.Role == MapLayerData.RoleCloseview)
        {
            checkBox.Disabled = true;
            checkBox.TooltipText = "Closeview layers never interact with gameplay (GDD §1).";
        }
        else
        {
            bool legal = CollisionLegal(layer.Loop);
            checkBox.Disabled = !legal;
            checkBox.TooltipText = legal
                ? "Enables fixed-world SoftVolumes on this Farview layer. Other tile categories stay visual-only."
                : layer.Loop
                    ? "Loop layers can never collide."
                    : "";
        }
    }

    // ---------------------------------------------------------------- small control builders

    private LineEdit AddLineEditRow(string label, string value, System.Action<string> commit)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) });
        var edit = new LineEdit { Text = value ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        edit.TextSubmitted += _ => edit.ReleaseFocus();
        edit.FocusExited += () => commit(edit.Text);
        row.AddChild(edit);
        _propsContainer.AddChild(row);
        return edit;
    }

    private SpinBox AddSpinRow(string label, float value, float min, float max, float step, bool disabled, System.Action<float> commit)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) });
        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            Editable = !disabled,
            Modulate = disabled ? new Color(1, 1, 1, 0.5f) : Colors.White,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        spin.ValueChanged += v => commit((float)v);
        row.AddChild(spin);
        _propsContainer.AddChild(row);
        return spin;
    }

    private CheckBox AddCheckRow(string label, bool value, bool disabled, System.Action<bool> commit)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) });
        var check = new CheckBox { ButtonPressed = value, Disabled = disabled };
        check.Toggled += commit.Invoke;
        row.AddChild(check);
        _propsContainer.AddChild(row);
        return check;
    }

    private void Commit(string label, System.Action apply, System.Action revert)
    {
        _state.Commands.Push(new PropertyEditCommand(label, apply, revert));
        _state.RaiseStateChanged();
    }

    // ---------------------------------------------------------------- in-place refresh

    /// <summary>Updates the properties sub-panel's EXISTING controls from current state
    /// without recreating them (structural signature unchanged — e.g. an Undo/Redo of a
    /// property edit, or a tile-count-only change). Never touches a focused control's
    /// text so it can never clobber an in-progress edit.</summary>
    private void RefreshPropsInPlace()
    {
        if (_canvasSelected)
        {
            var canvas = _state.Document?.Canvas;
            if (_canvasColorEdit != null && !_canvasColorEdit.HasFocus())
                _canvasColorEdit.Text = canvas?.Color ?? "";
            if (_canvasColorSwatch != null)
                _canvasColorSwatch.Color = ColorFromHex(canvas?.Color, new Color(0.53f, 0.81f, 0.92f));
            return;
        }

        var layer = _state.CurrentLayer;
        if (layer == null) return;

        if (_nameEdit != null && !_nameEdit.HasFocus()) _nameEdit.Text = layer.Name;
        if (_parallaxXSpin != null && !SpinFocused(_parallaxXSpin)) _parallaxXSpin.SetValueNoSignal(layer.ParallaxX);
        if (_parallaxYSpin != null && !SpinFocused(_parallaxYSpin)) _parallaxYSpin.SetValueNoSignal(layer.ParallaxY);
        if (_loopCheck != null) _loopCheck.SetPressedNoSignal(layer.Loop);
        if (_collisionCheck != null)
        {
            _collisionCheck.SetPressedNoSignal(layer.Collision);
            ApplyCollisionGating(layer, _collisionCheck);
        }
        if (_tintEdit != null && !_tintEdit.HasFocus()) _tintEdit.Text = layer.Tint ?? "";
        if (_tintSwatch != null) _tintSwatch.Color = ColorFromHex(layer.Tint, Colors.Transparent);
        if (_opacitySpin != null && !SpinFocused(_opacitySpin)) _opacitySpin.SetValueNoSignal(layer.Opacity);
        if (_swayAmpSpin != null && !SpinFocused(_swayAmpSpin)) _swayAmpSpin.SetValueNoSignal(layer.SwayAmplitudePx);
        if (_swayPeriodSpin != null && !SpinFocused(_swayPeriodSpin)) _swayPeriodSpin.SetValueNoSignal(layer.SwayPeriodSec);
        if (_autoXSpin != null && !SpinFocused(_autoXSpin)) _autoXSpin.SetValueNoSignal(layer.AutoScrollX);
        if (_autoYSpin != null && !SpinFocused(_autoYSpin)) _autoYSpin.SetValueNoSignal(layer.AutoScrollY);
        if (_gridWSpin != null && !SpinFocused(_gridWSpin)) _gridWSpin.SetValueNoSignal(layer.GridW);
        if (_gridHSpin != null && !SpinFocused(_gridHSpin)) _gridHSpin.SetValueNoSignal(layer.GridH);
    }

    private static bool SpinFocused(SpinBox spin) => spin.GetLineEdit()?.HasFocus() ?? false;

    private static Color WithAlpha(Color c, float a) => new(c.R, c.G, c.B, a);

    private static Color ColorFromHex(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try { return new Color(hex); }
        catch { return fallback; }
    }
}
