using System;
using Godot;

namespace Fableland.Map.Pages;

/// <summary>
/// A canvas layer whose drawing is supplied by its owner. A node's own <c>_Draw</c> renders
/// behind its children (KNOWLEDGE v0.6.7), so a view that stacks layers gives each one its
/// own child; this is that child. <c>_Draw</c> runs only when the owner calls
/// <c>QueueRedraw()</c> — moving or scaling the layer never re-runs it.
/// </summary>
public partial class DrawLayer : Node2D
{
    public Action<DrawLayer> Painter;

    public override void _Draw() => Painter?.Invoke(this);
}
