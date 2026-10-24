using Godot;

public partial class CameraFollower : Node3D
{
    [Export] public float MouseSensitivity = 0.001f;

    public Camera3D _camera;
    private SpacePlayer _player;
    
    private float _yaw = 0.0f;      // Горизонтальный поворот (влево/вправо)
    private float _pitch = 0.3f;    // Вертикальный поворот (вверх/вниз)
    private float _roll = 0.0f;     // Крен (вращение вокруг оси Z)

    public override void _Ready()
    {
        _player = GetParent<SpacePlayer>();
        
        _camera = GetNodeOrNull<Camera3D>("Camera3D");
        if (_camera == null)
        {
            _camera = new Camera3D();
            _camera.Name = "Camera";
            AddChild(_camera);
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent @event)
    {
        // Движение мыши - управление камерой
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= (float)mouseMotion.Relative.X * MouseSensitivity;
            _pitch -= (float)mouseMotion.Relative.Y * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, -Mathf.Pi / 2, Mathf.Pi / 2);
        }

		float rollInput = 0.0f;
        // Q - крен влево
        if (@event.IsActionPressed("camera_roll_left"))
            rollInput = -1.0f;

        // E - крен вправо
        if (@event.IsActionPressed("camera_roll_right"))
            rollInput = 1.0f;

        // R - сброс крена
        if (@event.IsActionPressed("camera_reset_roll"))
            _roll = Mathf.Lerp(_roll, 0.0f, 0.15f);

        // ESC - переключить захват мыши
        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_camera == null) return;

        // Камера находится в центре персонажа
        _camera.GlobalPosition = GlobalPosition;
        
        // Камера смотрит в направлении yaw/pitch/roll
        _camera.Rotation = new Vector3(_pitch, _yaw, _roll);
    }
}