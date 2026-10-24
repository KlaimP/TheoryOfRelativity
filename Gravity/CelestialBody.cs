using Godot;
using System;

/// <summary>
/// Повесь на сферу (MeshInstance3D / CSGSphere3D / Node3D с дочерним мешем).
/// Сам определяет радиус, родителя-притягивателя, скорость круговой орбиты,
/// вращается вокруг оси и создаёт коллайдер, по которому ходит игрок.
///
/// Гравитация и орбиты завязаны на одну пару чисел: Radius и SurfaceGravity.
///   GM = SurfaceGravity * Radius^2
/// Чем больше GM, тем "тяжелее" тело. Тело притягивается к более тяжёлому телу рядом.
/// </summary>
public partial class CelestialBody : Node3D
{
    [ExportGroup("Size & Gravity")]
    [Export] public float Radius = 0f;                 // 0 = взять из меша/масштаба
    [Export] public float SurfaceGravity = 12f;        // м/с^2 на поверхности
    [Export(PropertyHint.Range, "0.5,4,0.05")]
    public float GravityFalloff = 2f;                  // 2 = как в реальности (1/r^2)

    [ExportGroup("Orbit")]
    [Export] public bool FixedInSpace = false;         // true = стоит на месте (как солнце)
    [Export] public NodePath OrbitParentPath = new NodePath(); // пусто = выбрать автоматически
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
    public Vector3 Position3 { get; private set; }          // мировая позиция центра
    public Vector3 Velocity { get; private set; }           // мировая скорость (ед/сек реального времени)
    public Vector3 AngularVelocity { get; private set; }   // рад/сек, мировая ось
    public Vector3 FrameTranslation { get; private set; }  // сдвиг за последний тик
    public Transform3D FrameDelta { get; private set; } = Transform3D.Identity; // сдвиг+поворот за тик
    public float Soi { get; private set; } = float.PositiveInfinity; // зона влияния

    private Vector3 _relPos, _relVel;
    private StaticBody3D _collider;
    private const float MaxSubstep = 0.02f;

    public override void _EnterTree() => GravitySystem.Register(this);
    public override void _ExitTree() => GravitySystem.Unregister(this);

    public override void _Ready()
    {
        if (Radius <= 0f) Radius = DetectRadius();
        if (Radius <= 0f)
        {
            GD.PushWarning($"{Name}: не удалось определить радиус, ставлю 50. Задай Radius вручную.");
            Radius = 50f;
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
            return radiusVar.AsSingle() * Mathf.Max(scale.X, Mathf.Max(scale.Y, scale.Z));

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
        // TopLevel: коллайдер не наследует масштаб ноды, мы двигаем его сами.
        _collider = new StaticBody3D { Name = "GravityCollider", TopLevel = true };
        _collider.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } });
        AddChild(_collider);
        _collider.GlobalTransform = new Transform3D(Basis.Identity, GlobalPosition);
    }

    /// <summary>Вызывается системой один раз: задаёт родителя и стартовую скорость круговой орбиты.</summary>
    public void InitOrbit(CelestialBody parent)
    {
        Parent = parent;
        Position3 = GlobalPosition;
        Velocity = Vector3.Zero;
        Initialized = true;

        if (parent == null)
        {
            Soi = float.PositiveInfinity;
            return;
        }

        _relPos = GlobalPosition - parent.GlobalPosition;
        double r = _relPos.Length();
        if (r < 0.01)
        {
            Soi = float.PositiveInfinity;
            return;
        }

        Vector3 n = OrbitNormal.LengthSquared() < 1e-6f ? Vector3.Up : OrbitNormal.Normalized();
        Vector3 tangent = n.Cross(_relPos);
        if (tangent.LengthSquared() < 1e-6f) tangent = Vector3.Right.Cross(_relPos);
        tangent = tangent.Normalized() * (Retrograde ? -1f : 1f);

        double speed = Math.Sqrt(parent.GM / r) * OrbitSpeedFactor;
        _relVel = tangent * (float)speed;

        // Радиус сферы влияния (Хилл/Лаплас): внутри него игрок живёт в системе отсчёта этого тела.
        double soi = r * Math.Pow(GM / parent.GM, 0.4) * SoiMultiplier;
        Soi = (float)Math.Max(soi, Radius * 3f);
    }

    /// <summary>Один тик симуляции. realDt = реальное время, simDt = время с учётом ускорения.</summary>
    public void Advance(float realDt, float simDt)
    {
        Transform3D before = Rigid(GlobalTransform);
        Vector3 oldPos = Position3;

        if (Parent != null && !FixedInSpace && simDt > 0f)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(simDt / MaxSubstep), 1, 500);
            float h = simDt / steps;
            double gm = Parent.GM;
            double minR2 = (double)Parent.Radius * Parent.Radius;

            Vector3 a = Accel(_relPos, gm, minR2);
            for (int i = 0; i < steps; i++) // velocity Verlet: стабильные орбиты
            {
                _relPos += _relVel * h + a * (0.5f * h * h);
                Vector3 a2 = Accel(_relPos, gm, minR2);
                _relVel += (a + a2) * (0.5f * h);
                a = a2;
            }
            Position3 = Parent.Position3 + _relPos;
        }

        Transform3D t = GlobalTransform;
        t.Origin = Position3;

        AngularVelocity = Vector3.Zero;
        if (SpinPeriod != 0f && SpinAxis.LengthSquared() > 1e-6f)
        {
            Vector3 axis = SpinAxis.Normalized();
            float angle = Mathf.Tau / SpinPeriod * simDt;
            t.Basis = t.Basis * new Basis(axis, angle);
            if (realDt > 0f)
                AngularVelocity = (t.Basis * axis).Normalized() * (angle / realDt);
        }
        GlobalTransform = t;

        if (_collider != null) _collider.GlobalPosition = Position3;

        FrameDelta = Rigid(t) * before.AffineInverse();
        FrameTranslation = Position3 - oldPos;
        Velocity = realDt > 0f ? FrameTranslation / realDt : Vector3.Zero;
    }

    private static Vector3 Accel(Vector3 p, double gm, double minR2)
    {
        double d2 = Math.Max(p.LengthSquared(), minR2);
        double d = Math.Sqrt(d2);
        return p * (float)(-gm / (d2 * d));
    }

    private static Transform3D Rigid(Transform3D t) => new Transform3D(t.Basis.Orthonormalized(), t.Origin);

    // ---- гравитация для игрока (подогнана под ощущения, не под реальность) ----

    /// <summary>Вектор ускорения свободного падения в точке p (направлен к центру).</summary>
    public Vector3 GravityAt(Vector3 p)
    {
        Vector3 to = Position3 - p;
        float r = to.Length();
        if (r < 1e-3f) return Vector3.Zero;

        float g = r >= Radius
            ? SurfaceGravity * Mathf.Pow(Radius / r, GravityFalloff)
            : SurfaceGravity * (r / Radius);          // внутри тела падает до нуля

        if (Parent != null && !float.IsPositiveInfinity(Soi))
        {
            // плавно гасим гравитацию к краю зоны влияния, чтобы не было скачка
            float t = Mathf.Clamp((r / Soi - 0.5f) / 0.5f, 0f, 1f);
            g *= 1f - t * t * (3f - 2f * t);
        }
        return to / r * g;
    }

    /// <summary>Скорость точки p из-за вращения тела.</summary>
    public Vector3 SpinVelocityAt(Vector3 p) => AngularVelocity.Cross(p - Position3);

    /// <summary>Сила притяжения без учёта SOI (для выбора "главного" тела).</summary>
    public float PullAt(Vector3 p)
    {
        float r = Mathf.Max((Position3 - p).Length(), Radius);
        return SurfaceGravity * Mathf.Pow(Radius / r, GravityFalloff);
    }
}