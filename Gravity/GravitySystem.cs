using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Создаётся автоматически при появлении первого CelestialBody. Руками добавлять не нужно.
/// Обновляет тела в порядке иерархии (солнце -> планеты -> луны) до игрока.
///
/// Плавающее начало координат: настоящие позиции хранятся в double (Vector3D),
/// а сцена всегда построена вокруг Anchor (игрока). Когда игрок отходит от нуля дальше
/// RecenterDistance, весь мир сдвигается так, чтобы игрок снова оказался в нуле.
/// Поэтому float-точности Godot хватает на любых расстояниях.
///
/// Горячие клавиши (Controls.TimeFaster / TimeSlower): ] ускорить время, [ замедлить время.
/// </summary>
public partial class GravitySystem : Node
{
    public static GravitySystem Instance { get; private set; }
    public static bool IsInitialized { get; private set; }
    public static float TimeScale = 1f;
    public static readonly List<CelestialBody> Bodies = new();

    /// <summary>Мировая позиция точки (0,0,0) сцены.</summary>
    public static Vector3D Origin { get; private set; } = Vector3D.Zero;
    /// <summary>Вокруг кого держится начало координат (обычно игрок).</summary>
    public static Node3D Anchor;

    public static float RecenterDistance = 1000f;    // как далеко Anchor может уйти от нуля сцены
    public static float ScaledSpaceStart = 50000f;   // дальше этого тела рисуются уменьшенными
    public static float ColliderRange = 20000f;      // коллайдер тела включён, только если до поверхности ближе

    private static bool _creating;
    private static bool _dirty;
    private static readonly float[] Scales = { 1f, 2f, 5f, 10f, 25f, 50f, 100f, 250f, 1000f, 10000f };
    private readonly List<CelestialBody> _ordered = new();

    public GravitySystem()
    {
        ProcessPhysicsPriority = -100; // раньше игрока
    }

    // ---------- координаты ----------

    public static Vector3D ToWorld(Vector3 scenePos) => Origin + scenePos;
    public static Vector3 ToScene(Vector3D worldPos) => (Vector3)(worldPos - Origin);

    /// <summary>
    /// Переносит начало координат в мировую точку newOrigin. Все ноды верхнего уровня сцены
    /// (кроме тел, их ставит система) сдвигаются так, чтобы их мировая позиция не изменилась.
    /// </summary>
    public static void ShiftOrigin(Vector3D newOrigin)
    {
        Vector3 delta = (Vector3)(Origin - newOrigin);
        Origin = newOrigin;

        var tree = (SceneTree)Engine.GetMainLoop();
        Node scene = tree.CurrentScene;
        if (scene != null)
        {
            foreach (Node child in scene.GetChildren())
                if (child is Node3D n && child is not CelestialBody)
                    n.GlobalPosition += delta;
        }
        if (IsInstanceValid(Anchor) && (scene == null || !scene.IsAncestorOf(Anchor)))
            Anchor.GlobalPosition += delta;

        foreach (var b in Bodies)
            if (b.Initialized) b.Place(Origin);
    }

    // ---------- регистрация ----------

    public static void Register(CelestialBody b)
    {
        if (!Bodies.Contains(b)) Bodies.Add(b);
        _dirty = true;
        if (Instance == null && !_creating)
        {
            _creating = true;
            var sys = new GravitySystem { Name = "GravitySystem" };
            ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, sys);
        }
    }

    public static void Unregister(CelestialBody b)
    {
        Bodies.Remove(b);
        _dirty = true;
        if (Bodies.Count == 0)
        {
            // сцена выгружена: следующая начнёт с чистого листа
            Origin = Vector3D.Zero;
            IsInitialized = false;
            TimeScale = 1f;
        }
    }

    public override void _EnterTree()
    {
        Instance = this;
        _creating = false;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
            IsInitialized = false;
        }
    }

    // ---------- симуляция ----------

    public override void _PhysicsProcess(double delta)
    {
        if (_dirty) Rebuild();

        if (IsInstanceValid(Anchor) && Anchor.IsInsideTree() && Anchor.GlobalPosition.Length() > RecenterDistance)
            ShiftOrigin(ToWorld(Anchor.GlobalPosition));

        double sim = delta * TimeScale;
        foreach (var b in _ordered) b.Advance(delta, sim);
        foreach (var b in _ordered) b.Place(Origin);
    }

    public override void _Ready() => Controls.EnsureDefaults();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsEcho()) return;
        int i = System.Array.IndexOf(Scales, TimeScale);
        if (i < 0) i = 0;
        if (@event.IsActionPressed(Controls.TimeFaster)) i = Mathf.Min(i + 1, Scales.Length - 1);
        else if (@event.IsActionPressed(Controls.TimeSlower)) i = Mathf.Max(i - 1, 0);
        else return;
        TimeScale = Scales[i];
    }

    private void Rebuild()
    {
        _dirty = false;

        // тяжёлые первыми; родитель, заданный вручную, инициализируется раньше ребёнка
        var todo = Bodies
            .Where(b => !b.Initialized || (b.Parent != null && !Bodies.Contains(b.Parent)))
            .OrderByDescending(b => b.GM)
            .ToList();
        var done = new HashSet<CelestialBody>();
        foreach (var b in todo) Init(b, todo, done, new HashSet<CelestialBody>());

        foreach (var b in Bodies)
        {
            int d = 0;
            var p = b.Parent;
            while (p != null && d < 32) { d++; p = p.Parent; }
            b.Depth = d;
        }

        _ordered.Clear();
        _ordered.AddRange(Bodies.Where(b => b.Initialized).OrderBy(b => b.Depth));
        foreach (var b in _ordered) b.Place(Origin);
        IsInitialized = true;
    }

    private static void Init(CelestialBody b, List<CelestialBody> todo, HashSet<CelestialBody> done, HashSet<CelestialBody> stack)
    {
        if (done.Contains(b)) return;
        stack.Add(b);

        CelestialBody parent = ChooseParent(b);
        if (parent != null && stack.Contains(parent))
        {
            GD.PushWarning($"{b.Name}: циклическая цепочка OrbitParentPath, тело будет без родителя.");
            parent = null;
        }
        if (parent != null && todo.Contains(parent)) Init(parent, todo, done, stack);

        b.InitOrbit(parent, StartPosition(b));
        done.Add(b);
        stack.Remove(b);
    }

    private static Vector3D StartPosition(CelestialBody b) =>
        b.Initialized ? b.WorldPosition : ToWorld(b.AuthoredPosition);

    /// <summary>Родитель = более тяжёлое тело с максимальным притяжением в позиции b.</summary>
    private static CelestialBody ChooseParent(CelestialBody b)
    {
        if (b.OrbitParentPath != null && !b.OrbitParentPath.IsEmpty)
        {
            var p = b.GetNodeOrNull<CelestialBody>(b.OrbitParentPath);
            if (p != null && p != b) return p;
            GD.PushWarning($"{b.Name}: OrbitParentPath не указывает на CelestialBody, выбираю родителя автоматически.");
        }

        Vector3D pos = StartPosition(b);
        CelestialBody best = null;
        double bestPull = 0;
        foreach (var o in Bodies)
        {
            if (o == b || o.GM <= b.GM * 1.0001) continue;
            double d2 = (StartPosition(o) - pos).LengthSquared();
            if (d2 < 1e-6) continue;
            double pull = o.GM / d2;
            if (pull > bestPull) { bestPull = pull; best = o; }
        }
        return best;
    }

    // ---------- запросы для игрока (все в мировых координатах) ----------

    /// <summary>
    /// Гравитация в точке p для игрока, который движется вместе с телом frame.
    ///
    /// Тянут все тела, в зоне гравитации которых (PlayerGravityRange) находится игрок.
    /// Тело frame само летит по орбите и тащит игрока за собой, поэтому то, как другие тела тянут
    /// центр frame, вычитаем: остаётся только разница (приливная сила). Пример: солнце тянет и планету,
    /// и стоящего на ней игрока одинаково - значит, относительно планеты игрок этого не замечает.
    /// </summary>
    public static Vector3 GravityAt(Vector3D p, CelestialBody frame)
    {
        Vector3D g = Vector3D.Zero;
        foreach (var b in Bodies)
        {
            if (!b.Initialized) continue;
            g += b.PlayerGravityAt(p);
            if (frame != null && frame.Moves && b != frame)
                g -= b.PlayerGravityAt(frame.WorldPosition);
        }
        return (Vector3)g;
    }

    /// <summary>
    /// К какому телу привязан игрок (относительно него считается скорость, вместе с ним игрок летит по орбите).
    /// Обычно это самое "глубокое" тело, в зоне влияния которого точка (луна > планета > солнце).
    /// Но если солнце сюда своей гравитацией не достаёт (вне его PlayerGravityRange), игрок остаётся
    /// с ближайшей планетой: так солнце не "захватывает" игрока, пока к нему не подлетишь.
    /// </summary>
    public static CelestialBody FindFrame(Vector3D p)
    {
        CelestialBody frame = FindSoiFrame(p);
        if (frame == null || frame.Moves || frame.InPlayerGravityRange(p)) return frame;

        CelestialBody nearest = null;
        double bestD = double.MaxValue;
        foreach (var b in Bodies)
        {
            if (!b.Initialized || !b.Moves) continue;
            double d = b.SurfaceDistance(p);
            if (d < bestD) { bestD = d; nearest = b; }
        }
        return nearest ?? frame;
    }

    /// <summary>Самое "глубокое" тело, в зоне влияния которого находится точка (луна > планета > солнце).</summary>
    private static CelestialBody FindSoiFrame(Vector3D p)
    {
        CelestialBody best = null;
        float bestSoi = float.PositiveInfinity;
        double bestPull = -1;
        CelestialBody strongest = null;
        double strongestPull = -1;

        foreach (var b in Bodies)
        {
            if (!b.Initialized) continue;
            double pull = b.PullAt(p);
            if (pull > strongestPull) { strongestPull = pull; strongest = b; }

            if ((b.WorldPosition - p).Length() > b.Soi) continue;
            bool better = best == null || b.Soi < bestSoi || (b.Soi == bestSoi && pull > bestPull);
            if (better) { best = b; bestSoi = b.Soi; bestPull = pull; }
        }
        return best ?? strongest;
    }

    /// <summary>Солнце: самое тяжёлое тело, которое ни вокруг чего не вращается.</summary>
    public static CelestialBody FindSun()
    {
        CelestialBody best = null;
        foreach (var b in Bodies)
        {
            if (!b.Initialized || b.Parent != null) continue;
            if (best == null || b.GM > best.GM) best = b;
        }
        return best;
    }

    /// <summary>Ближайшее тело по расстоянию до поверхности.</summary>
    public static CelestialBody Nearest(Vector3D p)
    {
        CelestialBody best = null;
        double bestD = double.MaxValue;
        foreach (var b in Bodies)
        {
            if (!b.Initialized) continue;
            double d = b.SurfaceDistance(p);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }
}
