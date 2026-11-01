using Godot;

/// <summary>
/// Повесь на OmniLight3D. Точечный свет стоит в центре солнца и светит во все стороны,
/// поэтому все планеты освещены с правильной стороны, а луна может отбрасывать тень на планету (затмение).
///
/// Затухания нет (как в космосе): яркость одинаковая на любом расстоянии, дальность света
/// сама подстраивается под самое далёкое тело. Солнце выбирается автоматически
/// (самое тяжёлое тело без родителя) или задаётся в SunPath.
///
/// Тени точечного света на больших расстояниях мыльные: они хороши для затмений,
/// но не для мелких предметов. Если дальность больше MaxShadowRange, тени выключаются.
/// </summary>
public partial class SunLight : OmniLight3D
{
    [Export] public NodePath SunPath = new NodePath();
    [Export] public float RangeMargin = 1.25f;        // запас дальности сверх самого далёкого тела
    [Export] public float MaxShadowRange = 60000f;

    private CelestialBody _sun;
    private bool _shadowsWanted;

    public override void _Ready()
    {
        _shadowsWanted = ShadowEnabled;
        OmniAttenuation = 0f;                          // без затухания с расстоянием
        OmniShadowMode = ShadowMode.Cube;              // куб точнее параболоида на больших дистанциях
    }

    public override void _Process(double delta)
    {
        if (_sun == null || !IsInstanceValid(_sun)) _sun = FindSun();
        if (_sun == null || !_sun.Initialized) return;

        // RenderPosition - настоящая позиция относительно начала координат (без уменьшения scaled space)
        GlobalPosition = _sun.RenderPosition;

        double far = _sun.Radius * 2.0;
        foreach (var b in GravitySystem.Bodies)
        {
            if (b == _sun || !b.Initialized) continue;
            double d = (b.WorldPosition - _sun.WorldPosition).Length() + b.Radius;
            if (d > far) far = d;
        }
        Camera3D cam = GetViewport().GetCamera3D();
        if (cam != null)
        {
            double d = (GravitySystem.ToWorld(cam.GlobalPosition) - _sun.WorldPosition).Length();
            if (d > far) far = d;
        }

        OmniRange = (float)(far * RangeMargin);
        ShadowEnabled = _shadowsWanted && OmniRange <= MaxShadowRange;
    }

    private CelestialBody FindSun()
    {
        if (SunPath != null && !SunPath.IsEmpty)
        {
            var explicitSun = GetNodeOrNull<CelestialBody>(SunPath);
            if (explicitSun != null) return explicitSun;
        }
        return GravitySystem.FindSun();
    }
}
