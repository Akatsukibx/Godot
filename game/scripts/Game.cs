using Godot;

public static class Layers
{
    public const uint World = 1;
    public const uint Player = 2;
    public const uint Ghost = 4;
    // NPC bodies block players and ghosts but not the camera.
    public const uint Npc = 8;
}

// Shared access point for the running scene, so gameplay scripts don't need node paths.
public static class Game
{
    public static Main Main { get; set; }
    public static Player Player => Main?.Player;
    public static DayNight DayNight => Main?.DayNight;
    public static bool IsNight => DayNight?.IsNight ?? false;
    public static Font ThaiFont { get; set; }

    public static void Log(string message) => Toast(message);

    public static void Toast(string message, string icon = null, float duration = 3.5f)
    {
        GD.Print(message);
        Main?.Hud?.Toast(message, icon, duration);
    }

    public static Font MakeThaiFont() => new SystemFont
    {
        FontNames = new[] { "Leelawadee UI", "Tahoma", "Noto Sans Thai", "Sarabun", "Segoe UI" },
    };
}
