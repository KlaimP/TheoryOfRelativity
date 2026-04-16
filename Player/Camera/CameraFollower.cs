using Godot;
using System;
using System.Reflection.Metadata.Ecma335;

public partial class CameraFollower : Node
{
	[Export] public float CameraDistance = 5.0f;
    [Export] public float CameraHeight = 1.5f;
    [Export] public float MouseSensitivity = 0.005f;
    [Export] public float CameraRotationSpeed = 5.0f;

	private Camera3D _camera;
    private GravityBody _player;
	private Vector3 _cameraOffset = Vector3.Zero;
    private float _horizontalAngle = 0.0f;
    private float _verticalAngle = 0.3f;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_player = GetParent<GravityBody>();
		_camera = GetNode<Camera3D>("Camera3D");

		if(_camera != null)
		{
			_camera = new Camera3D();
			_camera.Name = "Camera";
			AddChild(_camera);
		}

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseMotion)
		{
			_horizontalAngle -= mouseMotion.Relative.X * MouseSensitivity;
			_verticalAngle -= mouseMotion.Relative.Y * MouseSensitivity;
			_verticalAngle = Mathf.Clamp(_verticalAngle, -Mathf.Pi / 2 , Mathf.Pi / 2);
		}

		if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
	}

	public void UpdateRotation()
	{
		if (_camera == null || _player == null)
			return;

		Vector3 playerUp = _player.UpDirection;
		Vector3 playerForward = -playerUp;

		Vector3 playerRight = playerUp.Cross(playerForward).Normalized();

		if (playerRight.Length() < 0.1f)
		{
			playerRight = Vector3.Right;
		}

		Vector3 cameraPos = Mathf.Cos(_verticalAngle) * (
            Mathf.Cos(_horizontalAngle) * playerRight + 
            Mathf.Sin(_horizontalAngle) * playerForward
        ) * CameraDistance;

		cameraPos += Mathf.Sin(_verticalAngle) * playerUp * CameraDistance;
        cameraPos += playerUp * CameraHeight;

        _camera.GlobalPosition = _player.GlobalPosition + cameraPos;
        _camera.LookAt(_player.GlobalPosition + playerUp * CameraHeight, playerUp);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		
	}
}
