using Godot;
using Fableland.Map;
using Fableland.Map.Pages;

/// <summary>
/// The third map view: the island as torn storybook pages (`Docs/Art/OverworldStyle.md`,
/// `MapGDD.md` §11). Off by default; the View button cycles atlas → schematic → pages.
///
/// The pages are drawn by a child, <see cref="OverworldPageView"/>, not by this node's
/// <c>_Draw</c> (which renders behind its children). This partial only feeds it: the same
/// projection every other view uses, expressed as one transform, and the same run state
/// (mist, visited, reachable, devoured). It keeps no state of its own and writes none —
/// input, hit-testing and movement are exactly the other views'.
/// </summary>
public partial class MapController : Node2D
{
    private bool _pages;                       // true while the pages view is the one shown
    private OverworldPageView _pagesView;
    private System.Collections.Generic.Dictionary<MapNode, int> _pagesSteps;
    private int _pagesStamp = -1;              // signature of the run state the view last showed
    private Viewport.Msaa _msaaBeforePages;

    private string RenderButtonText() => _pages ? "View: pages" : _rendered ? "View: atlas" : "View: schematic";

    /// <summary>Switch the pages view on or off: build it the first time, and give torn
    /// edges the anti-aliasing they need only while they are on screen.</summary>
    private void SetPagesView(bool on)
    {
        if (on == _pages) return;
        _pages = on;
        if (on)
        {
            _msaaBeforePages = GetViewport().Msaa2D;
            GetViewport().Msaa2D = Viewport.Msaa.Msaa4X;
            if (_pagesView == null)
            {
                _pagesView = new OverworldPageView { Name = "PagesView" };
                AddChild(_pagesView);
                MoveChild(_pagesView, 0);
                _pagesView.LabelFont = _font;
                _pagesView.RegionRevealed = region => !_mist || _revealed.Contains(region);
                _pagesView.NodeVisible = NodeVisible;
                _pagesView.NodeVisited = node => _visited.Contains(node);
                _pagesView.NodeReachable = node => _pagesSteps != null && (CanExplore(node, _pagesSteps) || CanRevisit(node, _pagesSteps));
                _pagesView.NodeFrontier = node => _pagesSteps != null && CanExplore(node, _pagesSteps);
            }
            _pagesStamp = -1;
        }
        else
        {
            GetViewport().Msaa2D = _msaaBeforePages;
        }
        if (_pagesView != null) _pagesView.Visible = on;
    }

    public override void _ExitTree()
    {
        if (_pages) GetViewport().Msaa2D = _msaaBeforePages;
    }

    /// <summary>Called from <c>_Draw</c> in place of the atlas: hand the child this frame's
    /// projection and, when the run state changed, let it rebuild what depends on it.</summary>
    private void DrawPages(System.Collections.Generic.Dictionary<MapNode, int> steps)
    {
        if (_pagesView == null || _graph == null) return;
        _pagesSteps = steps;
        if (_pagesView.Model?.Graph != _graph)
        {
            _pagesView.Build(_graph);
            _pagesStamp = -1;
        }

        // The projection is affine (rotate, foreshorten, zoom, pin), so it is one transform.
        Vector2 origin = Project(Vector2.Zero);
        var view = new Transform2D(Project(Vector2.Right) - origin, Project(Vector2.Down) - origin, origin);
        _pagesView.SetView(view, _zoom);
        _pagesView.Token = _tokenPos;

        // Day ends, realms entered, bridges built, the VOID crossed: all rare. Detect them by a
        // cheap signature rather than hooking every place the run can change.
        int devoured = 0;
        foreach (MapNode node in _graph.Nodes)
            if (node.Devoured) devoured++;
        int stamp = System.HashCode.Combine(_mist, _day, devoured, _visited.Count, _revealed.Count, _graph.Edges.Count, _graph.Nodes.Count, _current);
        if (stamp != _pagesStamp)
        {
            _pagesStamp = stamp;
            _pagesView.Mist = _mist;
            _pagesView.Day = _day;
            _pagesView.RefreshState();
        }
        _pagesView.Animate(_time);
    }
}
