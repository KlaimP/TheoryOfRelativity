using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Все действия управления в одном месте. Если действия нет в InputMap (Project Settings > Input Map),
/// оно создаётся с клавишей по умолчанию. Переназначь клавишу в InputMap - и памятка F1 покажет новую.
/// </summary>
public static class Controls
{
    public const string MoveForward = "move_forward";
    public const string MoveBackward = "move_backward";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Jump = "jump";              // прыжок; в воздухе держать - тяга вверх
    public const string Down = "thrust_down";       // в воздухе - тяга вниз
    public const string Sprint = "sprint";          // бег на земле, усиленная тяга в воздухе
    public const string Jetpack = "jetpack_toggle"; // включить / выключить джетпак
    public const string MatchVelocity = "match_velocity";
    public const string RollLeft = "camera_roll_left";
    public const string RollRight = "camera_roll_right";
    public const string TimeFaster = "time_faster";
    public const string TimeSlower = "time_slower";
    public const string Help = "show_help";

    private static readonly (string Action, Key Key)[] Defaults =
    {
        (MoveForward, Key.W), (MoveBackward, Key.S), (MoveLeft, Key.A), (MoveRight, Key.D),
        (Jump, Key.Space), (Down, Key.Ctrl), (Sprint, Key.Shift), (Jetpack, Key.F), (MatchVelocity, Key.X),
        (RollLeft, Key.Q), (RollRight, Key.E),
        (TimeFaster, Key.Bracketright), (TimeSlower, Key.Bracketleft), (Help, Key.F1),
    };

    private static bool _ensured;

    public static void EnsureDefaults()
    {
        if (_ensured) return;
        _ensured = true;
        foreach (var (action, key) in Defaults)
        {
            if (!InputMap.HasAction(action)) InputMap.AddAction(action);
            if (InputMap.ActionGetEvents(action).Count == 0)
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }

    public static bool Pressed(string action) => Input.IsActionPressed(action);

    public static float Axis(string negative, string positive) =>
        (Input.IsActionPressed(positive) ? 1f : 0f) - (Input.IsActionPressed(negative) ? 1f : 0f);

    /// <summary>Клавиши, назначенные на действие, для памятки: "W" или "Q / Mouse 4".</summary>
    public static string KeyName(string action)
    {
        if (!InputMap.HasAction(action)) return "?";
        var names = new List<string>();
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            names.Add(e switch
            {
                InputEventKey k when k.PhysicalKeycode != Key.None => OS.GetKeycodeString(k.PhysicalKeycode),
                InputEventKey k => OS.GetKeycodeString(k.Keycode),
                _ => e.AsText()
            });
        }
        return names.Count == 0 ? "-" : string.Join(" / ", names.Distinct());
    }
}
