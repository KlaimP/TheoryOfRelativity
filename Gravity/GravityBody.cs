using Godot;
using System;
using System.ComponentModel.DataAnnotations;

public partial class GravityBody : CharacterBody3D
{
	[Export] public float Mass = 1.0f;
	[Export] public float Drag = 0.0f;

	protected Vector3 _currentVelocity = Vector3.Zero;
	protected Vector3 _currentAcceleration = Vector3.Zero;
    protected Vector3 _appliedGravity = Vector3.Zero;

	protected Vector3 _upDirection = Vector3.Up;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		
	}

	public void ApplyForce(Vector3 force)
	{
		//Считаем ускорение по 2 закону Ньютона: F = m * a => a = F / m
		_currentAcceleration += force / Mass;
	}

	public void ApplyGravity(Vector3 gravityForce)
	{
		// Сохраняем силу гравитации для использования в других местах, например, для камеры
		_appliedGravity = gravityForce;
		ApplyForce(gravityForce);

		// Обновляем направление вверх, если сила гравитации достаточно велика, чтобы избежать шума при очень слабой гравитации
		if (gravityForce.Length() > 0.01)
		{
			_upDirection = -gravityForce.Normalized();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Обновляем скорость с учетом текущего ускорения и применяем сопротивление воздуха
		_currentVelocity += _currentAcceleration * (float)delta;
		_currentVelocity *= (1 - Drag);
		// Применяем скорость
		Velocity = _currentVelocity;
		MoveAndSlide();

		_currentAcceleration = Vector3.Zero; // Сбрасываем ускорение после применения
	}
}
