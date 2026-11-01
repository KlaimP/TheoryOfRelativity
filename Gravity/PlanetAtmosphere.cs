using Godot;

/// <summary>
/// Атмосфера планеты. Добавь дочерним узлом MeshInstance3D к планете (CelestialBody) и повесь этот скрипт.
/// Сфера-оболочка создаётся сама по радиусу планеты, шейдер - Shaders/atmosphere.gdshader.
///
/// Цвет, плотность и яркость настраиваются в материале (AtmosphereMaterial > Shader Parameters).
/// Радиусы и направление на солнце скрипт выставляет сам каждый кадр.
/// </summary>
public partial class PlanetAtmosphere : MeshInstance3D
{
    [Export] public float Height = 0.2f;               // толщина атмосферы в долях радиуса планеты
    [Export] public ShaderMaterial AtmosphereMaterial; // пусто = создать материал с настройками по умолчанию

    private const string ShaderPath = "res://Shaders/atmosphere.gdshader";

    private CelestialBody _planet;
    private bool _built;

    public override void _Ready()
    {
        _planet = GetParentOrNull<CelestialBody>();
        if (_planet == null)
            GD.PushWarning($"{Name}: PlanetAtmosphere должна быть дочерним узлом CelestialBody.");

        CastShadow = ShadowCastingSetting.Off;   // оболочка не должна отбрасывать тень на планету
        ExtraCullMargin = 100f;
    }

    public override void _Process(double delta)
    {
        if (_planet == null) return;

        // Радиус планеты становится известен только после её _Ready (а дочерние узлы готовы раньше родителя),
        // поэтому строим оболочку в первом кадре.
        if (!_built)
        {
            if (!_planet.IsNodeReady() || _planet.Radius <= 0f) return;
            Build();
        }

        CelestialBody sun = GravitySystem.FindSun();
        if (sun != null && sun != _planet && sun.Initialized && _planet.Initialized)
        {
            Vector3 dir = (Vector3)(sun.WorldPosition - _planet.WorldPosition).Normalized();
            AtmosphereMaterial.SetShaderParameter("sun_direction", dir);
        }
    }

    private void Build()
    {
        _built = true;

        // Радиус в единицах меша: мировой радиус планеты делим на её исходный масштаб
        float baseScale = Mathf.Max(_planet.BaseScale.X, Mathf.Max(_planet.BaseScale.Y, _planet.BaseScale.Z));
        float planetRadius = _planet.Radius / baseScale;
        float atmosphereRadius = planetRadius * (1f + Height);

        Mesh = new SphereMesh
        {
            Radius = atmosphereRadius,
            Height = atmosphereRadius * 2f,
            RadialSegments = 64,
            Rings = 32,
        };

        AtmosphereMaterial ??= new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        AtmosphereMaterial.SetShaderParameter("planet_radius", planetRadius);
        AtmosphereMaterial.SetShaderParameter("atmosphere_radius", atmosphereRadius);
        MaterialOverride = AtmosphereMaterial;
    }
}
