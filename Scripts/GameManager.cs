using Godot;

/// <summary>
/// Prototype orchestrator — a slimmed-down stand-in for the Unity GameManager +
/// FoeManager + DifficultyManager. Owns the match loop: spawns enemies up to a
/// cap, spawns WonderPages, tracks the score, handles player death/respawn/lives,
/// and drives win/lose. Restart reloads the scene.
/// </summary>
public partial class GameManager : Node2D
{
    [Export] public PackedScene EnemyScene;
    [Export] public PackedScene WonderPageScene;
    [Export] public NodePath EntitiesPath = "Entities";
    [Export] public NodePath HudPath = "Hud";

    [Export] public int PagesToWin = 5;
    [Export] public int EnemyCap = 4;
    [Export] public float EnemySpawnInterval = 3f;
    [Export] public float RespawnDelay = 1.2f;

    private Node2D _entities;
    private Hud _hud;
    private CharacterController _player;
    private Godot.Collections.Array<Node> _enemySpawns;
    private Godot.Collections.Array<Node> _pageSpawns;

    private int _pages;
    private float _enemyTimer;
    private bool _ended;

    public override void _Ready()
    {
        // Keep running while the tree is paused so restart works after win/lose.
        ProcessMode = ProcessModeEnum.Always;

        _entities = GetNode<Node2D>(EntitiesPath);
        _hud = GetNode<Hud>(HudPath);
        _player = GetTree().GetFirstNodeInGroup("player") as CharacterController;
        _enemySpawns = GetTree().GetNodesInGroup("enemy_spawn");
        _pageSpawns = GetTree().GetNodesInGroup("page_spawn");

        if (_player != null)
        {
            _player.HpChanged += OnPlayerHpChanged;
            _player.Died += OnPlayerDied;
            _hud.SetHp(_player.CurrentHP, _player.MaxHP);
            _hud.SetLives(_player.LivesRemaining);
            _hud.SetPlayer(_player);
        }
        _hud.SetPages(_pages, PagesToWin);
        _hud.HideBanner();

        for (int i = 0; i < PagesToWin && i < _pageSpawns.Count; i++)
            SpawnPage(i);
    }

    public override void _Process(double delta)
    {
        if (_ended)
        {
            if (Input.IsActionJustPressed("restart"))
            {
                GetTree().Paused = false;
                GetTree().ReloadCurrentScene();
            }
            return;
        }

        _enemyTimer -= (float)delta;
        if (_enemyTimer <= 0f)
        {
            _enemyTimer = EnemySpawnInterval;
            TrySpawnEnemy();
        }
    }

    private void TrySpawnEnemy()
    {
        if (EnemyScene == null || _enemySpawns.Count == 0) return;
        if (GetTree().GetNodesInGroup("enemy").Count >= EnemyCap) return;

        var marker = _enemySpawns[(int)(GD.Randi() % _enemySpawns.Count)] as Node2D;
        var enemy = EnemyScene.Instantiate<Node2D>();
        _entities.AddChild(enemy);
        enemy.GlobalPosition = marker.GlobalPosition;
    }

    private void SpawnPage(int spawnIndex)
    {
        if (WonderPageScene == null || _pageSpawns.Count == 0) return;
        var marker = _pageSpawns[spawnIndex % _pageSpawns.Count] as Node2D;
        var page = WonderPageScene.Instantiate<WonderPage>();
        _entities.AddChild(page);
        page.GlobalPosition = marker.GlobalPosition;
        page.Collected += OnPageCollected;
    }

    private void OnPageCollected()
    {
        if (_ended) return;
        _pages++;
        _hud.SetPages(_pages, PagesToWin);
        if (_pages >= PagesToWin)
        {
            EndGame("YOU WIN!\nPress R to play again");
            return;
        }
        // Drop a fresh page at a random spawn.
        SpawnPage((int)(GD.Randi() % _pageSpawns.Count));
    }

    private void OnPlayerHpChanged(float cur, float max) => _hud.SetHp(cur, max);

    private async void OnPlayerDied()
    {
        _hud.SetLives(_player.LivesRemaining);
        if (_player.LivesRemaining <= 0)
        {
            EndGame("GAME OVER\nPress R to try again");
            return;
        }
        await ToSignal(GetTree().CreateTimer(RespawnDelay), SceneTreeTimer.SignalName.Timeout);
        if (!_ended) _player.Respawn();
    }

    private void EndGame(string message)
    {
        _ended = true;
        _hud.ShowBanner(message);
        GetTree().Paused = true;
    }
}
