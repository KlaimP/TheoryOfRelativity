using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Создаётся автоматически при появлении первого CelestialBody. Руками добавлять не нужно.
/// Обновляет тела в порядке иерархии (солнце -> планеты -> луны) до игрока.
/// Горячие клавиши: ] ускорить время, [ замедлить время.
/// </summary>
public partial class GravitySystem : Node
{
    public static GravitySystem Instance { get; private set; }
    public static bool IsInitialized { get; private set; }
    public static float TimeScale = 1f;
    public static readonly List<CelestialBody> Bodies = new();

    private static bool _creating;
    private static bool _dirty;
    private static readonly float[] Scales = { 1f, 2f, 5f, 10f, 25f, 50f, 100f };
    private readonly List<CelestialBody> _ordered = new();

    public GravitySystem()
    {
        ProcessPhysicsPriority = -100; // раньше игрока
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
        float real = (float)delta;
        float sim = real * TimeScale;
        foreach (var b in _ordered) b.Advance(real, sim);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } k)
        {
            int i = System.Array.IndexOf(Scales, TimeScale);
            if (i < 0) i = 0;
            if (k.PhysicalKeycode == Key.Bracketright) i = Mathf.Min(i + 1, Scales.Length - 1);
            else if (k.PhysicalKeycode == Key.Bracketleft) i = Mathf.Max(i - 1, 0);
            else return;
            TimeScale = Scales[i];
            GD.Print($"Time scale x{TimeScale}");
        }
    }

    private void Rebuild()
    {
        _dirty = false;

        // тяжёлые первыми, чтобы родитель уже был инициализирован
        var todo = Bodies
            .Where(b => !b.Initialized || (b.Parent != null && !Bodies.Contains(b.Parent)))
            .OrderByDescending(b => b.GM)
            .ToList();
        foreach (var b in todo) b.InitOrbit(ChooseParent(b));

        foreach (var b in Bodies)
        {
            int d = 0;
            var p = b.Parent;
            while (p != null && d < 32) { d++; p = p.Parent; }
            b.Depth = d;
        }

        _ordered.Clear();
        _ordered.AddRange(Bodies.Where(b => b.Initialized).OrderBy(b => b.Depth));
        IsInitialized = true;
    }

    /// <summary>Родитель = более тяжёлое тело с максимальным притяжением в позиции b.</summary>
    private static CelestialBody ChooseParent(CelestialBody b)
    {
        if (b.OrbitParentPath != null && !b.OrbitParentPath.IsEmpty)
        {
            var p = b.GetNodeOrNull<CelestialBody>(b.OrbitParentPath);
            if (p != null && p != b) return p;
        }

        CelestialBody best = null;
        double bestPull = 0;
        foreach (var o in Bodies)
        {
            if (o == b || o.GM <= b.GM * 1.0001) continue;
            double d2 = (o.GlobalPosition - b.GlobalPosition).LengthSquared();
            if (d2 < 1e-6) continue;
            double pull = o.GM / d2;
            if (pull > bestPull) { bestPull = pull; best = o; }
        }
        return best;
    }

    // ---------- запросы для игрока ----------

    /// <summary>Самое "глубокое" тело, в зоне влияния которого находится точка (луна > планета > солнце).</summary>
    public static CelestialBody FindFrame(Vector3 p)
    {
        CelestialBody best = null;
        float bestSoi = float.PositiveInfinity, bestPull = -1f;
        CelestialBody strongest = null;
        float strongestPull = -1f;

        foreach (var b in Bodies)
        {
            if (!b.Initialized) continue;
            float pull = b.PullAt(p);
            if (pull > strongestPull) { strongestPull = pull; strongest = b; }

            if ((b.Position3 - p).Length() > b.Soi) continue;
            bool better = best == null || b.Soi < bestSoi || (b.Soi == bestSoi && pull > bestPull);
            if (better) { best = b; bestSoi = b.Soi; bestPull = pull; }
        }
        return best ?? strongest;
    }

    /// <summary>Ближайшее тело по расстоянию до поверхности.</summary>
    public static CelestialBody Nearest(Vector3 p)
    {
        CelestialBody best = null;
        float bestD = float.MaxValue;
        foreach (var b in Bodies)
        {
            if (!b.Initialized) continue;
            float d = (b.Position3 - p).Length() - b.Radius;
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }
}