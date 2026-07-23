using Godot;
using System;

public partial class SpaceCharacterController : GravityBody
{
	[Export] private CameraFollower _cameraFollower;
	private Vector3 _inputDirection = Vector3.Zero;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_cameraFollower = GetNode<CameraFollower>("CameraFollower");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _PhysicsProcess(double delta)
	{
		_inputDirection = Vector3.Zero;

		Basis cameraBasis = _cameraFollower._camera.GetGlobalTransform().Basis;

        Vector3 forward = -cameraBasis.Z; // вперед в Godot это -Z
        Vector3 right = cameraBasis.X;
        Vector3 up = cameraBasis.Y;

		// Обработка ввода для перемещения персонажа
		if (Input.IsActionPressed("move_forward"))
			_inputDirection += forward;
		if (Input.IsActionPressed("move_backward"))
			_inputDirection -= forward;
		if (Input.IsActionPressed("move_left"))
			_inputDirection -= right;
		if (Input.IsActionPressed("move_right"))
			_inputDirection += right;

		_inputDirection = _inputDirection.Normalized();

		Vector3 force = _inputDirection * 10.0f; // Сила перемещения, можно настроить
		
		ApplyForce(force);

		base._PhysicsProcess(delta);
		
	}
}
