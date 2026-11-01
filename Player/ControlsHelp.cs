using Godot;

/// <summary>
/// Экранная памятка по управлению (F1) и строка состояния: тело, высота, скорость, ускорение времени.
/// SpacePlayer создаёт её сам, руками добавлять не нужно.
/// </summary>
public partial class ControlsHelp : CanvasLayer
{
    public SpacePlayer Player;

    private Label _status;
    private PanelContainer _panel;
    private Label _helpText;

    public override void _Ready()
    {
        Layer = 10;

        _status = new Label { Position = new Vector2(16, 12) };
        _status.AddThemeFontSizeOverride("font_size", 16);
        _status.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        AddChild(_status);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.04f, 0.08f, 0.85f),
            BorderColor = new Color(0.5f, 0.6f, 0.9f, 0.6f),
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 16, ContentMarginBottom = 16,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(8);

        _panel = new PanelContainer { Position = new Vector2(16, 72), Visible = false };
        _panel.AddThemeStyleboxOverride("panel", style);
        _helpText = new Label();
        _helpText.AddThemeFontSizeOverride("font_size", 17);
        _panel.AddChild(_helpText);
        AddChild(_panel);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Controls.Help) && !e.IsEcho())
        {
            _panel.Visible = !_panel.Visible;
            if (_panel.Visible) _helpText.Text = BuildHelp(); // перечитываем: клавиши могли переназначить
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        string hint = $"{K(Controls.Help)} - управление";
        if (Player == null || !IsInstanceValid(Player) || Player.Frame == null)
        {
            _status.Text = hint;
            return;
        }

        CelestialBody f = Player.Frame;
        Vector3D world = GravitySystem.ToWorld(Player.GlobalPosition);
        double alt = f.SurfaceDistance(world);
        string state = Player.Grounded ? "на земле" : Player.InFreeFlight ? "невесомость" : "в воздухе";
        CelestialBody strongest = StrongestPull(world);
        string pull = strongest != null ? $" (сильнее всех тянет {strongest.Name})" : "";
        string jetpack = Player.JetpackOn ? "ВКЛ" : "выкл";
        _status.Text = $"{f.Name}  ·  высота {FormatDistance(alt)}  ·  скорость {Player.RelativeVelocity.Length():0.0} м/с" +
                       $"  ·  {state}  ·  время x{GravitySystem.TimeScale:0.##}      {hint}\n" +
                       $"гравитация {FormatGravity(Player.Gravity.Length())}{pull}  ·  джетпак ({K(Controls.Jetpack)}): {jetpack}";
    }

    private static CelestialBody StrongestPull(Vector3D p)
    {
        CelestialBody best = null;
        double bestG = 0;
        foreach (var b in GravitySystem.Bodies)
        {
            if (!b.Initialized) continue;
            double g = b.PlayerGravityAt(p).Length();
            if (g > bestG) { bestG = g; best = b; }
        }
        return best;
    }

    private static string FormatGravity(float g) =>
        g >= 0.1f ? $"{g:0.00} м/с²" : $"{g * 1000f:0.0} мм/с²";

    private static string BuildHelp() =>
        "УПРАВЛЕНИЕ\n\n" +
        "На земле\n" +
        $"   {K(Controls.MoveForward)} {K(Controls.MoveLeft)} {K(Controls.MoveBackward)} {K(Controls.MoveRight)}   ходьба\n" +
        $"   {K(Controls.Sprint)}   бег\n" +
        $"   {K(Controls.Jump)}   прыжок\n" +
        $"   {K(Controls.MoveForward)} {K(Controls.MoveLeft)} {K(Controls.MoveBackward)} {K(Controls.MoveRight)} в прыжке   немного подрулить\n\n" +
        "Джетпак\n" +
        $"   {K(Controls.Jetpack)}   включить / выключить\n" +
        $"   {K(Controls.MoveForward)} {K(Controls.MoveLeft)} {K(Controls.MoveBackward)} {K(Controls.MoveRight)}   тяга вперёд / вбок\n" +
        $"   {K(Controls.Jump)} / {K(Controls.Down)}   тяга вверх / вниз\n" +
        $"   {K(Controls.Sprint)}   усиленная тяга\n" +
        $"   {K(Controls.MatchVelocity)} (держать)   уравнять скорость с ближайшим телом\n\n" +
        "Невесомость (гравитация слабее 5 м/с²)\n" +
        "   Мышь   поворот в любую сторону\n" +
        $"   {K(Controls.RollLeft)} / {K(Controls.RollRight)}   крен\n\n" +
        "Мир\n" +
        $"   {K(Controls.TimeFaster)} / {K(Controls.TimeSlower)}   ускорить / замедлить время\n" +
        "   Esc   освободить мышь (клик - вернуть)\n" +
        $"   {K(Controls.Help)}   скрыть памятку";

    private static string K(string action) => Controls.KeyName(action);

    private static string FormatDistance(double m) =>
        m >= 10000 ? $"{m / 1000:0.0} км" : $"{m:0} м";
}
