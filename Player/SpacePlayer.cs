using Godot;

/// <summary>
/// Игрок: ходьба по планетам, прыжки, джетпак, полёт в невесомости.
/// Повесь на CharacterBody3D. Гравитацию от CelestialBody скрипт считает сам.
///
/// Управление (клавиши по умолчанию - в Controls.cs, памятка в игре - F1):
///   На земле:   WASD ходьба, Shift бег, Space прыжок.
///   F           включить / выключить джетпак.
///   В воздухе без джетпака: WASD немного подруливают (как в обычном прыжке).
///   В воздухе с джетпаком:  WASD тяга, Space вверх, Ctrl вниз, Shift усиленная тяга,
///                           X уравнять скорость с ближайшим телом.
///   В невесомости (гравитация слабее FreeFlightGravity): мышь крутит игрока в любую сторону, Q/E крен.
///
/// Как устроен один кадр физики (_PhysicsProcess), по шагам:
///   1. UpdateFrame      - выбираем, к какому телу мы "привязаны" (планета, луна, солнце).
///   2. CarryWithFrame   - переносим игрока вместе с этим телом (оно летит по орбите и вращается).
///   3. UpdateGravity    - считаем гравитацию, решаем: стоим ногами к планете или невесомость.
///   4. ReadInput        - читаем клавиши.
///   5. MoveOnGround / MoveInAir - меняем скорость по вводу.
///   6. MoveAndSlide     - двигаем тело и сталкиваемся с поверхностью.
///   7. UpdateLanding    - проверяем приземление / отрыв от земли.
///
/// Важно про скорость (_vel): она хранится ОТНОСИТЕЛЬНО тела, к которому мы привязаны.
/// Стоишь на планете - скорость ноль, хотя планета несётся вокруг солнца.
///
/// Игрок - центр мира (GravitySystem.Anchor): мир сдвигается вокруг него, чтобы не терялась точность.
/// Поэтому GlobalPosition игрока всегда около нуля, а настоящая позиция = GravitySystem.ToWorld(GlobalPosition).
///
/// Камера:
///   - ExternalCamera не задана: скрипт сам создаёт камеру и сам обрабатывает мышь.
///   - ExternalCamera задана: скрипт только берёт у неё направления движения.
/// </summary>
public partial class SpacePlayer : CharacterBody3D
{
    // =====================================================================
    //  НАСТРОЙКИ (видны в инспекторе Godot, можно менять прямо там)
    // =====================================================================

    [ExportGroup("Walking")]
    [Export] public float WalkSpeed = 4.5f;          // м/с, обычный шаг
    [Export] public float SprintSpeed = 7.5f;        // м/с, бег с Shift
    [Export] public float GroundAcceleration = 40f;  // как быстро набираем скорость шага (м/с²)
    [Export] public float GroundFriction = 60f;      // как быстро останавливаемся, если отпустить клавиши (м/с²)
    [Export] public float JumpSpeed = 4.5f;          // начальная скорость прыжка вверх. Высота = JumpSpeed² / (2g): на планете ~0.85 м
    [Export] public float AirControl = 3f;           // подруливание в прыжке без джетпака (м/с²). Маленькое - как у человека
    [Export] public float LandingFriction = 1f;      // при ударе о землю гасит скольжение: 1 = вся скорость удара идёт в трение

    [ExportGroup("Jetpack")]
    [Export] public bool JetpackStartsOn = false;          // включён ли джетпак при старте
    [Export] public float ThrustAcceleration = 14f;        // тяга (м/с²). Чтобы взлететь с планеты, нужно больше её гравитации (12)
    [Export] public float BoostMultiplier = 2f;            // во сколько раз сильнее тяга с Shift
    [Export] public float MaxThrustSpeed = 100f;           // разогнаться тягой можно только до этой скорости (м/с)
    [Export] public float MatchVelocityAcceleration = 10f; // как быстро X уравнивает скорость (м/с²)
    [Export] public float MatchRange = 1000f;              // X уравнивает скорость с телом, если до его поверхности ближе этого

    [ExportGroup("Orientation")]
    [Export] public float FreeFlightGravity = 3f;          // гравитация слабее этого (м/с²) = невесомость: можно крутиться
    [Export] public float AlignSpeed = 6f;                 // как быстро игрок встаёт ногами к планете
    [Export] public float RollSpeed = 1.6f;                // скорость крена на Q/E (рад/с)

    [ExportGroup("Camera")]
    [Export] public Camera3D ExternalCamera;
    [Export] public float MouseSensitivity = 0.0025f;
    [Export] public float EyeHeight = 0.7f;                // высота глаз над центром капсулы
    [Export] public float CameraFar = 1000000f;            // дальность прорисовки камеры

    [ExportGroup("Start")]
    [Export] public bool SpawnOnNearestBody = true;          // поставить игрока на поверхность при старте
    [Export] public NodePath SpawnBodyPath = new NodePath(); // на какое тело; пусто = ближайшее к игроку в редакторе
    [Export] public bool ShowHud = true;                     // строка состояния и памятка F1
    [Export] public bool LogDiagnostics = true;

    // =====================================================================
    //  ДАННЫЕ ДЛЯ HUD И ДРУГИХ СКРИПТОВ (только чтение)
    // =====================================================================

    public CelestialBody Frame => _frame;       // тело, к которому мы привязаны
    public bool Grounded => _grounded;           // стоим ли на земле
    public bool InFreeFlight => _freeFlight;     // невесомость
    public bool JetpackOn => _jetpackOn;
    public Vector3 RelativeVelocity => _vel;     // скорость относительно Frame
    public Vector3 Gravity => _gravity;          // ощущаемая гравитация

    // =====================================================================
    //  ВНУТРЕННЕЕ СОСТОЯНИЕ
    // =====================================================================

    private Camera3D _camera;
    private bool _ownsCamera;          // true = камеру создали мы, и мышь обрабатываем мы
    private CelestialBody _frame;
    private Vector3 _vel;              // в воздухе: относительно центра тела; на земле: относительно вращающейся поверхности
    private Vector3 _gravity;
    private Vector3 _up = Vector3.Up;  // "верх" в этом кадре (против гравитации)
    private bool _grounded;
    private bool _freeFlight;
    private bool _jetpackOn;
    private bool _started;
    private float _pitch;              // наклон камеры вверх/вниз (когда стоим ногами к планете)
    private float _waitTime;
    private bool _warnedSystem;
    private Vector3 _authoredPos;      // где игрок стоял в редакторе
    private float _spawnHeight = 1f;   // на какой высоте над поверхностью ставить центр игрока при спавне

    // Нажатые клавиши за этот кадр (собираем в одном месте, чтобы не опрашивать Input везде)
    private struct InputState
    {
        public Vector2 Move;      // X: влево(-1)/вправо(+1), Y: назад(-1)/вперёд(+1)
        public float Vertical;    // +1 вверх (Space), -1 вниз (Ctrl)
        public bool Sprint;       // Shift
        public bool JumpPressed;  // Space нажат именно в этом кадре
        public bool Match;        // X
        // крен Q/E читается прямо в FreeFlightRotation: он нужен только в невесомости
    }

    // =====================================================================
    //  ЗАПУСК
    // =====================================================================

    public override void _EnterTree() => GravitySystem.Anchor = this;

    public override void _ExitTree()
    {
        if (GravitySystem.Anchor == this) GravitySystem.Anchor = null;
    }

    public override void _Ready()
    {
        _authoredPos = GlobalPosition;
        _jetpackOn = JetpackStartsOn;
        Controls.EnsureDefaults();
        if (ShowHud) AddChild(new ControlsHelp { Player = this });

        // Коллизия: берём свою CollisionShape3D или создаём капсулу ростом 1.8 м
        CollisionShape3D shape = null;
        foreach (Node c in GetChildren())
        {
            if (c is CollisionShape3D cs && shape == null) shape = cs;
            if (ExternalCamera == null && c is Camera3D cam && _camera == null) _camera = cam;
        }
        if (shape == null)
        {
            shape = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } };
            AddChild(shape);
        }
        // половина роста формы: чтобы при спавне ноги оказались ровно на поверхности
        _spawnHeight = shape.Shape switch
        {
            CapsuleShape3D c => c.Height * 0.5f,
            SphereShape3D s => s.Radius,
            BoxShape3D b => b.Size.Y * 0.5f,
            _ => 1f
        } - shape.Position.Y + 0.05f;

        // Камера
        if (ExternalCamera != null)
        {
            _camera = ExternalCamera;
            _ownsCamera = false;
        }
        else
        {
            if (_camera == null)
            {
                _camera = new Camera3D { Position = new Vector3(0f, EyeHeight, 0f) };
                AddChild(_camera);
            }
            _camera.MakeCurrent();
            _ownsCamera = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        _camera.Far = Mathf.Max(_camera.Far, CameraFar);

        // Настройки CharacterBody3D: что считать полом и как к нему прилипать
        MotionMode = MotionModeEnum.Grounded;
        FloorMaxAngle = Mathf.DegToRad(55f);  // склоны круче 55° - уже стена
        FloorSnapLength = 0.4f;               // прилипаем к полу на спусках, а не подпрыгиваем
        FloorStopOnSlope = true;              // не сползаем со склонов стоя на месте
    }

    /// <summary>Ставит игрока на поверхность тела в первом кадре физики.</summary>
    private void Start()
    {
        _vel = Vector3.Zero;
        _grounded = false;

        CelestialBody body = null;
        if (SpawnOnNearestBody)
        {
            if (SpawnBodyPath != null && !SpawnBodyPath.IsEmpty)
                body = GetNodeOrNull<CelestialBody>(SpawnBodyPath);
            body ??= NearestAuthoredBody();
        }

        if (body == null)
        {
            _frame = GravitySystem.FindFrame(GravitySystem.ToWorld(GlobalPosition));
            return;
        }

        // направление "вверх от центра тела" берём из того, как игрок и тело стояли в редакторе
        Vector3 n = _authoredPos - body.AuthoredPosition;
        n = n.LengthSquared() < 1e-4f ? Vector3.Up : n.Normalized();

        // смотрим туда же, куда смотрел игрок в редакторе, но параллельно земле
        Vector3 fwd = -GlobalTransform.Basis.Z;
        fwd -= n * fwd.Dot(n);
        if (fwd.LengthSquared() < 1e-4f) fwd = n.Cross(Vector3.Right);
        fwd = fwd.Normalized();
        Vector3 right = fwd.Cross(n).Normalized();

        // переносим центр мира прямо в точку спавна: тело может быть очень далеко от нуля
        Vector3D spawn = body.WorldPosition + (Vector3D)n * (body.Radius + _spawnHeight);
        GravitySystem.ShiftOrigin(spawn);
        GlobalTransform = new Transform3D(new Basis(right, n, -fwd), Vector3.Zero);
        ResetPhysicsInterpolation();

        _frame = GravitySystem.FindFrame(spawn);
        // в воздухе скорость не включает вращение тела, поэтому добавляем его, чтобы стоять на месте относительно поверхности
        _vel = _frame != null ? _frame.SpinVelocityAt(GlobalPosition) : Vector3.Zero;
    }

    private CelestialBody NearestAuthoredBody()
    {
        CelestialBody best = null;
        float bestD = float.MaxValue;
        foreach (var b in GravitySystem.Bodies)
        {
            float d = (b.AuthoredPosition - _authoredPos).Length() - b.Radius;
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    // =====================================================================
    //  МЫШЬ
    // =====================================================================

    public override void _Input(InputEvent e)
    {
        if (!_ownsCamera) return;

        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // влево-вправо всегда поворачиваем всё тело игрока
            RotateObjectLocal(Vector3.Up, -mm.Relative.X * MouseSensitivity);
            float dy = -mm.Relative.Y * MouseSensitivity;
            if (_freeFlight)
            {
                // в невесомости вверх-вниз тоже крутим тело: можно сделать сальто
                RotateObjectLocal(Vector3.Right, dy);
            }
            else
            {
                // на планете вверх-вниз наклоняем только камеру (голову), не дальше ~86°
                _pitch = Mathf.Clamp(_pitch + dy, -1.5f, 1.5f);
                _camera.Rotation = new Vector3(_pitch, 0f, 0f);
            }
        }
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            // Esc - отпустить / захватить мышь
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    // =====================================================================
    //  ГЛАВНЫЙ ЦИКЛ ФИЗИКИ
    // =====================================================================

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Тел на сцене может не быть вообще (открытый космос): тогда просто летаем в невесомости.
        // Ждём систему, только если тела есть, но она ещё не успела запуститься.
        if (GravitySystem.Bodies.Count > 0 && !GravitySystem.IsInitialized)
        {
            _waitTime += dt;
            if (LogDiagnostics && !_warnedSystem && _waitTime > 2f)
            {
                _warnedSystem = true;
                GD.PushWarning("SpacePlayer: на сцене есть CelestialBody, но гравитационная система не запустилась. " +
                               "Пересобери проект (Build) и проверь, что GravitySystem.cs лежит в проекте.");
            }
            return;
        }

        if (!_started) { Start(); _started = true; }
        if (_frame != null && !IsInstanceValid(_frame)) _frame = null;

        if (Input.IsActionJustPressed(Controls.Jetpack)) _jetpackOn = !_jetpackOn;

        UpdateFrame();                       // 1
        CarryWithFrame();                    // 2
        UpdateGravity(dt);                   // 3
        InputState input = ReadInput();      // 4

        if (_grounded) MoveOnGround(input, dt); // 5
        else MoveInAir(input, dt);

        _vel += _gravity * dt;               // гравитация действует всегда (на земле её гасит пол)

        Vector3 velBefore = _vel;            // 6
        UpDirection = _up;
        Velocity = _vel;
        MoveAndSlide();
        _vel = Velocity;

        UpdateLanding(velBefore);            // 7
    }

    // ---------------------------------------------------------------------
    //  1. Система отсчёта
    // ---------------------------------------------------------------------

    /// <summary>
    /// Выбираем тело, относительно которого считаем скорость: самое маленькое, в "зоне влияния" которого мы находимся
    /// (луна важнее планеты, планета важнее солнца). При смене тела пересчитываем скорость,
    /// чтобы настоящая скорость в пространстве не изменилась.
    /// </summary>
    private void UpdateFrame()
    {
        CelestialBody newFrame = GravitySystem.FindFrame(GravitySystem.ToWorld(GlobalPosition));
        if (newFrame == _frame) return;

        Vector3 inertial = _vel + FrameVelocity(_frame, _grounded);  // скорость "в пространстве"
        _frame = newFrame;
        _vel = inertial - FrameVelocity(_frame, _grounded);          // та же скорость, но относительно нового тела
    }

    /// <summary>Скорость тела (и, если стоим на нём, скорость его вращающейся поверхности под нами).</summary>
    private Vector3 FrameVelocity(CelestialBody frame, bool withSpin)
    {
        if (frame == null) return Vector3.Zero;
        return frame.Velocity + (withSpin ? frame.SpinVelocityAt(GlobalPosition) : Vector3.Zero);
    }

    // ---------------------------------------------------------------------
    //  2. Перенос вместе с телом
    // ---------------------------------------------------------------------

    /// <summary>
    /// GravitySystem в этом кадре уже сдвинул тело по орбите и повернул вокруг оси.
    /// Стоим на земле - едем вместе с поверхностью (и поворачиваемся вместе с ней).
    /// В воздухе - только летим вместе с центром тела, а вращение под нами не тащит.
    /// </summary>
    private void CarryWithFrame()
    {
        if (_frame == null) return;

        if (_grounded)
        {
            Basis r = _frame.FrameRotation;                                      // поворот тела за кадр
            Vector3 oldCenter = _frame.RenderPosition - _frame.FrameTranslation; // где был центр тела
            Vector3 rel = GlobalPosition - oldCenter;                            // мы относительно центра
            GlobalTransform = new Transform3D((r * GlobalTransform.Basis).Orthonormalized(),
                                              _frame.RenderPosition + r * rel);
            _vel = r * _vel;
        }
        else
        {
            GlobalPosition += _frame.FrameTranslation;
        }
    }

    // ---------------------------------------------------------------------
    //  3. Гравитация и ориентация
    // ---------------------------------------------------------------------

    /// <summary>
    /// Считаем гравитацию от всех тел и решаем, как ориентирован игрок:
    ///   - гравитация сильная или стоим на земле -> плавно встаём ногами к планете, камера наклоняется отдельно;
    ///   - гравитация слабее FreeFlightGravity и мы в воздухе -> невесомость, крутимся мышью и Q/E.
    /// </summary>
    private void UpdateGravity(float dt)
    {
        _gravity = GravitySystem.GravityAt(GravitySystem.ToWorld(GlobalPosition), _frame);
        float g = _gravity.Length();

        // Небольшой запас (10%), чтобы на самой границе режим не мигал туда-сюда каждый кадр.
        float threshold = _freeFlight ? FreeFlightGravity * 1.1f : FreeFlightGravity;
        _freeFlight = !_grounded && g < threshold;

        // "Верх" берём из гравитации, а не из поворота игрока: поворот догоняет её с задержкой,
        // и если опираться на него, при падении под углом поверхность считается стеной и игрок по ней скользит.
        _up = g > 1e-4f ? -_gravity / g : GlobalTransform.Basis.Y.Normalized();

        if (_freeFlight) FreeFlightRotation(dt);
        else AlignToGravity(_up, dt);
    }

    /// <summary>Плавно поворачивает игрока так, чтобы его "верх" смотрел в targetUp.</summary>
    private void AlignToGravity(Vector3 targetUp, float dt)
    {
        Transform3D t = GlobalTransform;
        Vector3 curUp = t.Basis.Y.Normalized();
        var q = new Quaternion(curUp, targetUp);  // поворот "от текущего верха к нужному"
        // на земле выравниваемся вдвое быстрее; формула 1-exp даёт плавность, не зависящую от FPS
        float k = 1f - Mathf.Exp(-(_grounded ? AlignSpeed * 2f : AlignSpeed) * dt);
        q = Quaternion.Identity.Slerp(q, k);
        t.Basis = (new Basis(q) * t.Basis).Orthonormalized();
        GlobalTransform = t;
    }

    /// <summary>Невесомость: наклон камеры переносим на всё тело, Q/E дают крен.</summary>
    private void FreeFlightRotation(float dt)
    {
        if (_ownsCamera && Mathf.Abs(_pitch) > 0.0001f)
        {
            // смотрели вверх/вниз головой - теперь так повёрнуто всё тело, а камера прямо
            RotateObjectLocal(Vector3.Right, _pitch);
            _pitch = 0f;
            _camera.Rotation = Vector3.Zero;
        }
        if (_ownsCamera)
        {
            float roll = Controls.Axis(Controls.RollLeft, Controls.RollRight);
            if (roll != 0f) RotateObjectLocal(Vector3.Back, roll * RollSpeed * dt);
        }
        Transform3D t = GlobalTransform;
        t.Basis = t.Basis.Orthonormalized();
        GlobalTransform = t;
    }

    // ---------------------------------------------------------------------
    //  4. Ввод
    // ---------------------------------------------------------------------

    private InputState ReadInput() => new InputState
    {
        Move = new Vector2(Controls.Axis(Controls.MoveLeft, Controls.MoveRight),
                           Controls.Axis(Controls.MoveBackward, Controls.MoveForward)),
        Vertical = Controls.Axis(Controls.Down, Controls.Jump),
        Sprint = Controls.Pressed(Controls.Sprint),
        JumpPressed = Input.IsActionJustPressed(Controls.Jump),  // прыжок только по нажатию, не по удержанию
        Match = Controls.Pressed(Controls.MatchVelocity),
    };

    /// <summary>
    /// Направления "вперёд" и "вправо" вдоль земли: куда смотрит камера, но без наклона вверх/вниз.
    /// Так W ведёт вперёд по поверхности, даже если смотришь себе под ноги.
    /// </summary>
    private void GroundAxes(out Vector3 forward, out Vector3 right)
    {
        Basis cb = _camera.GlobalTransform.Basis;
        forward = -cb.Z;
        forward -= _up * forward.Dot(_up);         // убираем вертикальную часть
        if (forward.LengthSquared() < 1e-4f)       // смотрим строго вверх или вниз
        {
            forward = cb.Y;
            forward -= _up * forward.Dot(_up);
        }
        forward = forward.Normalized();
        right = forward.Cross(_up).Normalized();
    }

    // ---------------------------------------------------------------------
    //  5. Движение
    // ---------------------------------------------------------------------

    /// <summary>Ходьба: скорость вдоль земли плавно стремится к желаемой, прыжок добавляет скорость вверх.</summary>
    private void MoveOnGround(InputState input, float dt)
    {
        GroundAxes(out Vector3 forward, out Vector3 right);

        Vector3 wish = right * input.Move.X + forward * input.Move.Y;
        if (wish.LengthSquared() > 1f) wish = wish.Normalized();   // по диагонали не быстрее
        bool hasInput = wish.LengthSquared() > 1e-4f;
        wish *= input.Sprint ? SprintSpeed : WalkSpeed;

        // делим скорость на вертикальную часть и горизонтальную (вдоль земли)
        float vUp = _vel.Dot(_up);
        Vector3 horiz = _vel - _up * vUp;

        // без ввода тормозим сильнее: игрок стоит на земле, а не скользит по ней
        horiz = horiz.MoveToward(wish, (hasInput ? GroundAcceleration : GroundFriction) * dt);
        if (input.JumpPressed) vUp = Mathf.Max(vUp, 0f) + JumpSpeed;

        _vel = horiz + _up * vUp;
    }

    /// <summary>
    /// В воздухе. Без джетпака - только лёгкое подруливание, как в обычном прыжке.
    /// С джетпаком - тяга во все стороны и выравнивание скорости на X.
    /// </summary>
    private void MoveInAir(InputState input, float dt)
    {
        if (!_jetpackOn)
        {
            // без джетпака в невесомости двигаться не за что - просто летим по инерции
            if (_freeFlight || input.Move == Vector2.Zero) return;

            GroundAxes(out Vector3 forward, out Vector3 right);
            Vector3 wish = right * input.Move.X + forward * input.Move.Y;
            if (wish.LengthSquared() > 1f) wish = wish.Normalized();
            wish *= input.Sprint ? SprintSpeed : WalkSpeed;

            // меняем только горизонтальную скорость и слабо: в прыжке не разгонишься
            float vUp = _vel.Dot(_up);
            Vector3 horiz = (_vel - _up * vUp).MoveToward(wish, AirControl * dt);
            _vel = horiz + _up * vUp;
            return;
        }

        // --- джетпак ---
        Vector3 thrust;
        if (_freeFlight)
        {
            // невесомость: тянем относительно взгляда (W - куда смотришь, Space - "вверх" для головы)
            Basis cb = _camera.GlobalTransform.Basis;
            thrust = cb.X * input.Move.X + -cb.Z * input.Move.Y + cb.Y * input.Vertical;
        }
        else
        {
            // рядом с планетой: WASD по горизонту (взгляд вниз не толкает в землю), Space/Ctrl - вверх/вниз
            GroundAxes(out Vector3 forward, out Vector3 right);
            thrust = right * input.Move.X + forward * input.Move.Y + _up * input.Vertical;
        }
        if (thrust.LengthSquared() > 1f) thrust = thrust.Normalized();

        if (thrust.LengthSquared() > 1e-4f)
        {
            float accel = ThrustAcceleration * (input.Sprint ? BoostMultiplier : 1f);
            Vector3 nv = _vel + thrust * accel * dt;
            // выше MaxThrustSpeed тягой не разогнаться, но тормозить тягой можно всегда
            if (nv.Length() <= MaxThrustSpeed || nv.Length() < _vel.Length()) _vel = nv;
        }

        // X: рядом с телом уравниваем скорость с ним (зависнуть над луной), вдали - просто останавливаемся
        if (input.Match)
        {
            Vector3D world = GravitySystem.ToWorld(GlobalPosition);
            Vector3 desired = Vector3.Zero;   // ноль = стоять относительно нашего тела
            CelestialBody target = GravitySystem.Nearest(world);
            if (target != null && target.SurfaceDistance(world) <= MatchRange)
                desired = target.Velocity - FrameVelocity(_frame, false);
            _vel = _vel.MoveToward(desired, MatchVelocityAcceleration * dt);
        }
    }

    // ---------------------------------------------------------------------
    //  7. Приземление и отрыв
    // ---------------------------------------------------------------------

    /// <summary>
    /// Стоим ли на земле после движения. При смене состояния переводим скорость:
    /// на земле она считается относительно вращающейся поверхности, в воздухе - нет.
    /// При ударе о землю гасим скольжение пропорционально силе удара (как трение).
    /// </summary>
    private void UpdateLanding(Vector3 velBefore)
    {
        // пол есть, если коснулись поверхности, а гравитация хоть немного прижимает к ней
        // (в невесомости тоже можно приземлиться: дальше игрок сам встанет ногами к поверхности)
        bool nowGrounded = IsOnFloor() && _gravity.LengthSquared() > 1e-6f;

        if (_frame != null && nowGrounded != _grounded)
        {
            Vector3 spin = _frame.SpinVelocityAt(GlobalPosition);
            _vel += nowGrounded ? -spin : spin;
        }

        if (nowGrounded && !_grounded)
        {
            float impact = Mathf.Max(0f, -velBefore.Dot(_up));    // скорость, с которой ударились о землю
            float vUp = _vel.Dot(_up);
            Vector3 horiz = (_vel - _up * vUp).MoveToward(Vector3.Zero, impact * LandingFriction);
            _vel = horiz + _up * Mathf.Min(vUp, 0f);
        }

        _grounded = nowGrounded;
    }
}
