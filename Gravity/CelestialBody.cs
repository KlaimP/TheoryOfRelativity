using Godot;
using System;

/// <summary>
/// Повесь на сферу (MeshInstance3D / CSGSphere3D / Node3D с дочерним мешем).
/// Сам определяет радиус, родителя-притягивателя, скорость круговой орбиты,
/// вращается вокруг оси и создаёт коллайдер, по которому ходит игрок.
///
/// Гравитация и орбиты завязаны на одну пару чисел: Radius и SurfaceGravity.
///   g(r) = SurfaceGravity * (Radius / r)^GravityFalloff   снаружи, линейно до нуля внутри
///   GM   = SurfaceGravity * Radius^2  ("тяжесть" тела: кто вокруг кого крутится)
/// Один и тот же закон g(r) двигает и тела по орбитам, и игрока, поэтому они всегда согласованы.
///
/// Координаты:
///   WorldPosition  - настоящая позиция в double (может быть хоть 1e12).
///   RenderPosition - позиция на сцене относительно плавающего начала координат (float, без искажений).
///   Сама нода может стоять ближе и быть уменьшенной, если тело очень далеко (scaled space),
///   поэтому для физики всегда бери RenderPosition, а не GlobalPosition.
///
/// Тела должны лежать в корне сцены, а не внутри друг друга: позицию и поворот ноды
/// система выставляет сама каждый тик.
/// </summary>
public partial class CelestialBody : Node3D
{
    [ExportGroup("Size & Gravity")]
    [Export] public float Radius = 0f;                 // 0 = взять из меша/масштаба
    [Export] public float SurfaceGravity = 9.8f;       // м/с^2 на поверхности
    [Export(PropertyHint.Range, "0.5,4,0.05")]
    public float GravityFalloff = 2f;                  // 2 = как в реальности (1/r^2)
    // Зона гравитации для игрока (как в Outer Wilds): дальше этого расстояния от центра тело игрока не тянет,
    // последние 40% зоны притяжение плавно гаснет. 0 = тянет везде. На орбиты других тел не влияет.
    [Export] public float PlayerGravityRange = 0f;

    [ExportGroup("Orbit")]
    [Export] public bool FixedInSpace = false;         // true = стоит на месте (как солнце)
    [Export] public NodePath OrbitParentPath = new NodePath(); // пусто = выбрать автоматически
    [Export] public double OrbitDistance = 0;          // >0 = поставить на орбиту на этом расстоянии от родителя (позиция в редакторе игнорируется)
    [Export(PropertyHint.Range, "0,360,0.1")]
    public float OrbitPhase = 0f;                      // градусы: где на орбите стартует тело (для OrbitDistance)
    [Export] public Vector3 OrbitNormal = Vector3.Up;  // нормаль плоскости орбиты
    [Export] public bool Retrograde = false;           // крутиться в обратную сторону
    [Export] public float OrbitSpeedFactor = 1f;       // 1 = круг, <1 эллипс, до ~1.41 = побег
    [Export] public float SoiMultiplier = 1f;          // размер зоны влияния (для игрока)

    [ExportGroup("Rotation")]
    [Export] public float SpinPeriod = 0f;             // секунд на оборот, 0 = не вращается
    [Export] public Vector3 SpinAxis = Vector3.Up;     // локальная ось

    [ExportGroup("Collision")]
    [Export] public bool AutoCollider = true;

    // ---- данные, которые читают другие скрипты ----
    public double GM => (double)SurfaceGravity * Radius * Radius;
    public CelestialBody Parent { get; private set; }
    public int Depth { get; set; }
    public bool Initialized { get; private set; }
    public Vector3 AuthoredPosition { get; private set; }     // где тело стояло в редакторе
    public Vector3D WorldPosition { get; private set; }       // настоящая позиция центра
    public Vector3D WorldVelocity { get; private set; }       // ед/сек реального времени
    public Vector3 Velocity => (Vector3)WorldVelocity;
    public Vector3 RenderPosition { get; private set; }       // центр относительно GravitySystem.Origin
    public Vector3 AngularVelocity { get; private set; }      // рад/сек реального времени, мировая ось
    public bool Moves => Parent != null && !FixedInSpace;     // летит ли тело по орбите
    public Vector3 BaseScale => _baseScale;                   // масштаб узла из редактора (без уменьшения scaled space)
    public Vector3 FrameTranslation { get; private set; }     // сдвиг центра за последний тик
    public Basis FrameRotation { get; private set; } = Basis.Identity; // поворот вокруг центра за последний тик
    public float Soi { get; private set; } = float.PositiveInfinity;   // зона влияния

    private Vector3D _relPos, _relVel;   // относительно родителя, скорость в ед/сек времени симуляции
    private Basis _orientation = Basis.Identity;
    private Vector3 _baseScale = Vector3.One;
    private StaticBody3D _collider;
    private CollisionShape3D _colliderShape;
    private bool _colliderEnabled = true;

    public override void _EnterTree() => GravitySystem.Register(this);
    public override void _ExitTree() => GravitySystem.Unregister(this);

    public override void _Ready()
    {
        AuthoredPosition = GlobalPosition;
        _orientation = GlobalTransform.Basis.Orthonormalized();
        _baseScale = GlobalTransform.Basis.Scale;

        if (Radius <= 0f) Radius = DetectRadius();
        if (Radius <= 0f)
        {
            GD.PushWarning($"{Name}: не удалось определить радиус, ставлю 1000. Задай Radius вручную.");
            Radius = 1000f;
        }
        if (AutoCollider) CreateCollider();
    }

    private float DetectRadius()
    {
        // Скрипт висит прямо на MeshInstance3D / CSGSphere3D: читаем свойства ноды через Get().
        Vector3 scale = GlobalTransform.Basis.Scale;

        Variant meshVar = Get("mesh");
        if (meshVar.VariantType == Variant.Type.Object && meshVar.As<Mesh>() is Mesh mesh)
        {
            Vector3 size = mesh.GetAabb().Size * scale;
            return Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) * 0.5f;
        }

        Variant radiusVar = Get("radius");
        if (radiusVar.VariantType == Variant.Type.Float)
            return radiusVar.AsSingle() * MaxScale(GlobalTransform.Basis);

        // Скрипт на обычном Node3D, а сфера лежит дочерней нодой.
        foreach (Node n in FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)n;
            Vector3 size = mi.GetAabb().Size * mi.GlobalTransform.Basis.Scale;
            return Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) * 0.5f;
        }
        foreach (Node n in FindChildren("*", "CsgSphere3D", true, false))
        {
            var c = (CsgSphere3D)n;
            return c.Radius * MaxScale(c.GlobalTransform.Basis);
        }
        return 0f;
    }

    private static float MaxScale(Basis b)
    {
        Vector3 s = b.Scale;
        return Mathf.Max(s.X, Mathf.Max(s.Y, s.Z));
    }

    private void CreateCollider()
    {
        // TopLevel: коллайдер не наследует масштаб ноды (в т.ч. уменьшение в scaled space), мы двигаем его сами.
        // Сфера не меняется от поворота, поэтому коллайдер только переносим.
        _colliderShape = new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } };
        _collider = new StaticBody3D { Name = "GravityCollider", TopLevel = true };
        _collider.AddChild(_colliderShape);
        AddChild(_collider);
        _collider.GlobalTransform = new Transform3D(Basis.Identity, GlobalPosition);
    }

    /// <summary>Вызывается системой один раз: задаёт родителя, стартовую позицию и скорость круговой орбиты.</summary>
    public void InitOrbit(CelestialBody parent, Vector3D startPosition)
    {
        Parent = parent;
        WorldPosition = startPosition;
        WorldVelocity = Vector3D.Zero;
        _relVel = Vector3D.Zero;
        Initialized = true;
        Soi = float.PositiveInfinity;

        if (parent == null) return;

        Vector3 n = OrbitNormal.LengthSquared() < 1e-6f ? Vector3.Up : OrbitNormal.Normalized();

        if (OrbitDistance > 0)
        {
            // опорное направление в плоскости орбиты, повёрнутое на OrbitPhase
            Vector3 reference = Vector3.Right - n * n.Dot(Vector3.Right);
            if (reference.LengthSquared() < 1e-6f) reference = Vector3.Forward - n * n.Dot(Vector3.Forward);
            reference = reference.Normalized().Rotated(n, Mathf.DegToRad(OrbitPhase));
            _relPos = (Vector3D)reference * OrbitDistance;
        }
        else
        {
            _relPos = startPosition - parent.WorldPosition;
        }
        WorldPosition = parent.WorldPosition + _relPos;

        double r = _relPos.Length();
        if (r < 0.01) return;

        Vector3 dir = (Vector3)_relPos.Normalized();
        Vector3 tangent = n.Cross(dir);
        if (tangent.LengthSquared() < 1e-6f) tangent = Vector3.Right.Cross(dir);
        tangent = tangent.Normalized() * (Retrograde ? -1f : 1f);

        double speed = Math.Sqrt(parent.FieldStrength(r) * r) * OrbitSpeedFactor;
        _relVel = (Vector3D)tangent * speed;

        // Радиус сферы влияния (Лаплас): внутри него игрок живёт в системе отсчёта этого тела.
        double soi = r * Math.Pow(GM / parent.GM, 0.4) * SoiMultiplier;
        Soi = (float)Math.Max(soi, Radius * 3.0);
    }

    /// <summary>Один тик симуляции. realDt = реальное время, simDt = время с учётом ускорения.</summary>
    public void Advance(double realDt, double simDt)
    {
        Vector3D oldPos = WorldPosition;

        if (Moves && simDt > 0)
        {
            // шаг ~1/2000 орбитального периода: точно и не тратит лишнего на далёких орбитах
            double r = Math.Max(_relPos.Length(), Parent.Radius);
            double period = Math.Tau * Math.Sqrt(r / Math.Max(Parent.FieldStrength(r), 1e-12));
            int steps = (int)Math.Clamp(Math.Ceiling(simDt / (period / 2000.0)), 1, 1000);
            double h = simDt / steps;

            Vector3D a = Parent.FieldAt(_relPos);
            for (int i = 0; i < steps; i++) // velocity Verlet: стабильные орбиты
            {
                _relPos += _relVel * h + a * (0.5 * h * h);
                Vector3D a2 = Parent.FieldAt(_relPos);
                _relVel += (a + a2) * (0.5 * h);
                a = a2;
            }
            WorldPosition = Parent.WorldPosition + _relPos;
        }

        AngularVelocity = Vector3.Zero;
        FrameRotation = Basis.Identity;
        if (SpinPeriod != 0f && SpinAxis.LengthSquared() > 1e-6f)
        {
            Vector3 worldAxis = (_orientation * SpinAxis.Normalized()).Normalized();
            float angle = (float)(Math.Tau / SpinPeriod * simDt);
            FrameRotation = new Basis(worldAxis, angle);
            _orientation = (FrameRotation * _orientation).Orthonormalized();
            if (realDt > 0) AngularVelocity = worldAxis * (float)(angle / realDt);
        }

        Vector3D shift = WorldPosition - oldPos;
        FrameTranslation = (Vector3)shift;
        WorldVelocity = realDt > 0 ? shift / realDt : Vector3D.Zero;
    }

    /// <summary>
    /// Ставит ноду на сцену относительно плавающего начала координат.
    /// Далёкие тела рисуются ближе и пропорционально меньше: угловой размер тот же,
    /// а камере не нужен гигантский Far и не ломается точность буфера глубины.
    /// </summary>
    public void Place(Vector3D origin)
    {
        Vector3D rel = WorldPosition - origin;
        double dist = rel.Length();
        RenderPosition = (Vector3)rel;

        double s = 1.0;
        double near = GravitySystem.ScaledSpaceStart;
        if (dist > near)
            s = near * (1.0 + Math.Log(dist / near)) / dist; // логарифм сохраняет порядок "кто дальше"

        var basis = _orientation * Basis.FromScale(_baseScale * (float)s);
        GlobalTransform = new Transform3D(basis, (Vector3)(rel * s));

        if (_collider != null)
        {
            _collider.GlobalTransform = new Transform3D(Basis.Identity, RenderPosition);
            bool enable = dist - Radius < GravitySystem.ColliderRange;
            if (enable != _colliderEnabled)
            {
                _colliderEnabled = enable;
                _colliderShape.SetDeferred(CollisionShape3D.PropertyName.Disabled, !enable);
            }
        }
    }

    // ---- гравитационное поле ----

    /// <summary>Сила тяжести на расстоянии r от центра.</summary>
    public double FieldStrength(double r) => r >= Radius
        ? SurfaceGravity * Math.Pow(Radius / r, GravityFalloff)
        : SurfaceGravity * (r / Radius);              // внутри тела падает до нуля

    /// <summary>Ускорение в точке, смещённой на offset от центра тела (направлено к центру).</summary>
    public Vector3D FieldAt(Vector3D offset)
    {
        double r = offset.Length();
        return r < 1e-6 ? Vector3D.Zero : offset * (-FieldStrength(r) / r);
    }

    /// <summary>Настоящее притяжение тела в мировой точке p (так тело тянет другие тела по орбитам).</summary>
    public Vector3D GravityAt(Vector3D p) => FieldAt(p - WorldPosition);

    /// <summary>Как тело тянет игрока в точке p: то же притяжение, но только внутри PlayerGravityRange.</summary>
    public Vector3D PlayerGravityAt(Vector3D p) => FieldAt(p - WorldPosition) * PlayerGravityFade(p);

    /// <summary>1 = внутри зоны, 0 = за её пределами, между ними плавный переход.</summary>
    public double PlayerGravityFade(Vector3D p)
    {
        if (PlayerGravityRange <= 0f) return 1.0;
        double r = (p - WorldPosition).Length();
        double t = Math.Clamp((r / PlayerGravityRange - 0.6) / 0.4, 0.0, 1.0);
        return 1.0 - t * t * (3.0 - 2.0 * t);
    }

    /// <summary>Находится ли точка в зоне, где тело тянет игрока.</summary>
    public bool InPlayerGravityRange(Vector3D p) =>
        PlayerGravityRange <= 0f || (p - WorldPosition).Length() < PlayerGravityRange;

    /// <summary>Скорость точки p (в координатах сцены) из-за вращения тела.</summary>
    public Vector3 SpinVelocityAt(Vector3 p) => AngularVelocity.Cross(p - RenderPosition);

    /// <summary>Сила притяжения у поверхности или дальше (для выбора "главного" тела).</summary>
    public double PullAt(Vector3D p) => FieldStrength(Math.Max((WorldPosition - p).Length(), Radius));

    /// <summary>Расстояние от мировой точки p до поверхности.</summary>
    public double SurfaceDistance(Vector3D p) => (WorldPosition - p).Length() - Radius;
}
