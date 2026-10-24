using Godot;
 
/// <summary>
/// Повесь на пустой CharacterBody3D. Капсулу коллизии и камеру скрипт создаст сам, если их нет.
///
/// Управление (физические клавиши, раскладка не важна):
///   WASD        ходьба / тяга в воздухе (по направлению взгляда)
///   Space       прыжок / тяга вверх
///   Ctrl / C    тяга вниз
///   Shift       бег
///   Q / E       крен (только в невесомости)
///   X (держать) уравнять скорость с ближайшим телом
///   Esc         освободить / захватить мышь
///   [ и ]       замедлить / ускорить время орбит
///
/// Идея: игрок живёт в системе отсчёта ближайшего тела (его сферы влияния).
/// Тело двигается и вращается, а игрока переносим вместе с ним. Скорость _vel хранится
/// относительно этой системы отсчёта. Поэтому на планете можно спокойно ходить,
/// а при взлёте игрок сохраняет скорость планеты, как в Outer Wilds.
/// </summary>
public partial class SpacePlayer : CharacterBody3D
{
    [ExportGroup("Walking")]
    [Export] public float WalkSpeed = 5f;
    [Export] public float SprintSpeed = 9f;
    [Export] public float GroundAcceleration = 45f;
    [Export] public float JumpSpeed = 6.5f;
 
    [ExportGroup("Jetpack")]
    [Export] public float ThrustAcceleration = 14f;
    [Export] public float MaxThrustSpeed = 150f;        // относительно системы отсчёта
    [Export] public float MatchVelocityAcceleration = 25f;
 
    [ExportGroup("Orientation")]
    [Export] public float AlignSpeed = 6f;              // как быстро "вставать" ногами к планете
    [Export] public float MinGravityForAlignment = 1f;  // слабее этого = невесомость, свободный полёт
    [Export] public float RollSpeed = 1.6f;
 
    [ExportGroup("Camera")]
    [Export] public float MouseSensitivity = 0.0025f;
    [Export] public float EyeHeight = 0.7f;
 
    [ExportGroup("Start")]
    [Export] public bool SpawnOnNearestBody = true;     // поставить игрока на поверхность ближайшей планеты
 
    private Camera3D _camera;
    private CelestialBody _frame;
    private Vector3 _vel;          // скорость относительно системы отсчёта _frame
    private Vector3 _gravity;
    private bool _grounded;
    private bool _started;
    private bool _freeFlight;
    private float _pitch;
 
    public override void _Ready()
    {
        bool hasShape = false;
        foreach (Node c in GetChildren())
        {
            if (c is CollisionShape3D) hasShape = true;
            if (c is Camera3D cam && _camera == null) _camera = cam;
        }
        if (!hasShape)
        {
            AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } });
        }
        if (_camera == null)
        {
            _camera = new Camera3D { Position = new Vector3(0f, EyeHeight, 0f), Far = 100000f };
            AddChild(_camera);
        }
        _camera.MakeCurrent();
 
        MotionMode = MotionModeEnum.Grounded;
        FloorMaxAngle = Mathf.DegToRad(55f);
        FloorSnapLength = 0.4f;
        FloorStopOnSlope = true;
 
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }
 
    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateObjectLocal(Vector3.Up, -mm.Relative.X * MouseSensitivity);
            float dy = -mm.Relative.Y * MouseSensitivity;
            if (_freeFlight)
            {
                RotateObjectLocal(Vector3.Right, dy);   // в невесомости можно крутиться на 360°
            }
            else
            {
                _pitch = Mathf.Clamp(_pitch + dy, -1.5f, 1.5f);
                _camera.Rotation = new Vector3(_pitch, 0f, 0f);
            }
        }
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }
 
    public override void _PhysicsProcess(double delta)
    {
        if (!GravitySystem.IsInitialized || GravitySystem.Instance == null) return;
        float dt = (float)delta;
 
        if (!_started) { Start(); _started = true; }
        if (_frame != null && !IsInstanceValid(_frame)) _frame = null;
 
        // 1. система отсчёта: ближайшая зона влияния
        CelestialBody newFrame = GravitySystem.FindFrame(GlobalPosition);
        if (newFrame != _frame)
        {
            Vector3 inertial = _vel + FrameVelocity(_frame, _grounded);
            _frame = newFrame;
            _vel = inertial - FrameVelocity(_frame, _grounded);
        }
 
        // 2. переносим игрока вместе с телом
        if (_frame != null)
        {
            if (_grounded)
            {
                Transform3D d = _frame.FrameDelta;       // орбита + вращение планеты
                GlobalTransform = d * GlobalTransform;
                _vel = d.Basis * _vel;
            }
            else
            {
                GlobalPosition += _frame.FrameTranslation; // в воздухе только орбитальное движение
            }
        }
 
        // 3. гравитация и ориентация
        _gravity = _frame != null ? _frame.GravityAt(GlobalPosition) : Vector3.Zero;
        float gMag = _gravity.Length();
        bool hasGravity = gMag >= MinGravityForAlignment;
        _freeFlight = !hasGravity;
 
        if (hasGravity) AlignToGravity(-_gravity / gMag, dt);
        else FreeFlightRotation(dt);
 
        // 4. ввод
        float ix = Held(Godot.Key.D) - Held(Godot.Key.A);
        float iz = Held(Godot.Key.W) - Held(Godot.Key.S);
        float iy = Held(Godot.Key.Space) - Mathf.Max(Held(Godot.Key.Ctrl), Held(Godot.Key.C));
        bool sprint = Input.IsPhysicalKeyPressed(Godot.Key.Shift);
        bool jump = Input.IsPhysicalKeyPressed(Godot.Key.Space);
 
        Basis body = GlobalTransform.Basis;
        Vector3 up = body.Y.Normalized();
 
        if (_grounded)
        {
            Vector3 fwd = -body.Z;
            Vector3 wish = body.X * ix + fwd * iz;
            if (wish.LengthSquared() > 1f) wish = wish.Normalized();
            wish *= sprint ? SprintSpeed : WalkSpeed;
 
            float vUp = _vel.Dot(up);
            Vector3 horiz = _vel - up * vUp;
            horiz = horiz.MoveToward(wish, GroundAcceleration * dt);
            if (jump) vUp = Mathf.Max(vUp, 0f) + JumpSpeed;
            _vel = horiz + up * vUp;
        }
        else
        {
            Basis cb = _camera.GlobalTransform.Basis;
            Vector3 thrust = cb.X * ix + (-cb.Z) * iz + cb.Y * iy;
            if (thrust.LengthSquared() > 1f) thrust = thrust.Normalized();
            Vector3 nv = _vel + thrust * ThrustAcceleration * dt;
            if (nv.Length() <= MaxThrustSpeed || nv.Length() < _vel.Length()) _vel = nv;
 
            if (Input.IsPhysicalKeyPressed(Godot.Key.X))
            {
                CelestialBody target = GravitySystem.Nearest(GlobalPosition);
                if (target != null)
                {
                    Vector3 desired = target.Velocity - FrameVelocity(_frame, false);
                    _vel = _vel.MoveToward(desired, MatchVelocityAcceleration * dt);
                }
            }
        }
 
        _vel += _gravity * dt;
 
        // 5. движение
        UpDirection = up;
        Velocity = _vel;
        MoveAndSlide();
        _vel = Velocity;
 
        // 6. приземление / отрыв: добавляем или убираем скорость вращения поверхности
        bool nowGrounded = IsOnFloor();
        if (_frame != null && nowGrounded != _grounded)
        {
            Vector3 spin = _frame.SpinVelocityAt(GlobalPosition);
            _vel += nowGrounded ? -spin : spin;
        }
        _grounded = nowGrounded;
    }
 
    private Vector3 FrameVelocity(CelestialBody frame, bool withSpin)
    {
        if (frame == null) return Vector3.Zero;
        return frame.Velocity + (withSpin ? frame.SpinVelocityAt(GlobalPosition) : Vector3.Zero);
    }
 
    private void AlignToGravity(Vector3 targetUp, float dt)
    {
        Transform3D t = GlobalTransform;
        Vector3 curUp = t.Basis.Y.Normalized();
        var q = new Quaternion(curUp, targetUp);
        float k = 1f - Mathf.Exp(-(_grounded ? AlignSpeed * 2f : AlignSpeed) * dt);
        q = Quaternion.Identity.Slerp(q, k);
        t.Basis = (new Basis(q) * t.Basis).Orthonormalized();
        GlobalTransform = t;
    }
 
    private void FreeFlightRotation(float dt)
    {
        // при входе в невесомость переносим наклон камеры в тело
        if (Mathf.Abs(_pitch) > 0.0001f)
        {
            RotateObjectLocal(Vector3.Right, _pitch);
            _pitch = 0f;
            _camera.Rotation = Vector3.Zero;
        }
        float roll = Held(Godot.Key.E) - Held(Godot.Key.Q);
        if (roll != 0f) RotateObjectLocal(Vector3.Back, roll * RollSpeed * dt);
 
        Transform3D t = GlobalTransform;
        t.Basis = t.Basis.Orthonormalized();
        GlobalTransform = t;
    }
 
    private void Start()
    {
        _vel = Vector3.Zero;
        _grounded = false;
 
        if (SpawnOnNearestBody)
        {
            CelestialBody body = GravitySystem.Nearest(GlobalPosition);
            if (body != null)
            {
                Vector3 n = GlobalPosition - body.Position3;
                n = n.LengthSquared() < 1e-4f ? Vector3.Up : n.Normalized();
 
                Vector3 fwd = -GlobalTransform.Basis.Z;
                fwd -= n * fwd.Dot(n);
                if (fwd.LengthSquared() < 1e-4f) fwd = n.Cross(Vector3.Right);
                fwd = fwd.Normalized();
                Vector3 right = fwd.Cross(n).Normalized();
 
                Vector3 pos = body.Position3 + n * (body.Radius + 0.95f);
                GlobalTransform = new Transform3D(new Basis(right, n, -fwd), pos);
                ResetPhysicsInterpolation();
 
                _frame = GravitySystem.FindFrame(pos);
                _vel = _frame != null ? _frame.SpinVelocityAt(pos) : Vector3.Zero;
                return;
            }
        }
        _frame = GravitySystem.FindFrame(GlobalPosition);
    }
 
    private static float Held(Key k) => Input.IsPhysicalKeyPressed(k) ? 1f : 0f;
}