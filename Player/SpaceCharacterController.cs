using Godot;
using System;

public partial class SpaceCharacterController : GravityBody
{
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _PhysicsProcess(double delta)
	{
		// Получаем гравитацию от менеджера гравитации
		//Vector3 gravityForce = GravityManager.Instance.CalculateGravity(this);
		//ApplyGravity(gravityForce);

		// Обработка ввода для перемещения персонажа
		Vector3 inputDirection = Vector3.Zero;
		if (Input.IsActionPressed("move_forward"))
			inputDirection -= Transform.Basis.Z;
		if (Input.IsActionPressed("move_backward"))
			inputDirection += Transform.Basis.Z;
		if (Input.IsActionPressed("move_left"))
			inputDirection -= Transform.Basis.X;
		if (Input.IsActionPressed("move_right"))
			inputDirection += Transform.Basis.X;

		inputDirection = inputDirection.Normalized();
		
		Vector3 localInputForce = inputDirection * 10.0f; // Сила перемещения, можно настроить
		Vector3 worldInputForce =  this.GlobalBasis * localInputForce; // Преобразуем в мировые координаты

		ApplyForce(worldInputForce);

		base._PhysicsProcess(delta);
		
	}
}
