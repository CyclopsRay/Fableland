using Godot;
using System;

public partial class Player : CharacterBody2D
{
    [Export] public float Speed = 300f;
    [Export] public float JumpVelocity = -450f;
    [Export] public float Gravity = 980f;
    [Export] public float AttackDuration = 0.2f;

    private Sprite2D _sprite;
    private Color _originalColor = Colors.White;
    private bool _isAttacking;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _originalColor = _sprite.Modulate;
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("attack") && !_isAttacking)
        {
            DoAttack();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Horizontal movement
        float inputDir = 0f;
        if (Input.IsActionPressed("move_left"))
            inputDir -= 1f;
        if (Input.IsActionPressed("move_right"))
            inputDir += 1f;

        // Apply gravity
        if (!IsOnFloor())
        {
            Velocity = new Vector2(Velocity.X, Velocity.Y + Gravity * dt);
        }
        else
        {
            // Snappy stop on ground with no input
            if (Mathf.Abs(inputDir) < 0.01f)
            {
                Velocity = new Vector2(0f, Velocity.Y);
            }
        }

        // Horizontal velocity
        Velocity = new Vector2(inputDir * Speed, Velocity.Y);

        // Jump
        if (Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            Velocity = new Vector2(Velocity.X, JumpVelocity);
        }

        MoveAndSlide();

        // Flip sprite based on facing direction
        if (inputDir > 0.01f)
            _sprite.FlipH = false;
        else if (inputDir < -0.01f)
            _sprite.FlipH = true;
    }

    private async void DoAttack()
    {
        _isAttacking = true;
        _sprite.Modulate = Colors.Red;
        await ToSignal(GetTree().CreateTimer(AttackDuration), "timeout");
        _sprite.Modulate = _originalColor;
        _isAttacking = false;
    }
}
