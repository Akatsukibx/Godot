using Godot;
using System.Collections.Generic;
using System.Linq;

// Player settings (audio, brightness, key bindings), saved to user://settings.cfg.
public static class Settings
{
    const string FilePath = "user://settings.cfg";
    const int KeysVersion = 4;

    public static float MasterVolume { get; set; } = 0.8f;
    public static float SfxVolume { get; set; } = 1f;
    public static float MusicVolume { get; set; } = 0.6f;
    public static bool Muted { get; set; }
    public static float Brightness { get; set; } = 1f;
    public static bool Fullscreen { get; set; }
    // 0 = ต่ำ, 1 = ปานกลาง, 2 = สูง
    public static int Graphics { get; set; } = 1;
    public static float MouseSensitivity { get; set; } = 0.5f;
    public static bool InvertY { get; set; }
    // Test-only multiplier on how fast the in-game clock runs.
    public static float TimeScale { get; set; } = 1f;
    // Test-only: 0 = random, 1 = always rain, 2 = never rain.
    public static int RainMode { get; set; }
    public static bool AdminMode { get; set; }
    // Whole-UI scale (content scale factor); smaller keeps the screen uncluttered.
    public static float UiScale { get; set; } = 0.85f;

    public static float MouseRadiansPerPixel => Mathf.Lerp(0.0008f, 0.006f, MouseSensitivity);

    public static readonly (string action, string label)[] Rebindable =
    {
        ("move_up", "เดินขึ้น"),
        ("move_down", "เดินลง"),
        ("move_left", "เดินซ้าย"),
        ("move_right", "เดินขวา"),
        ("sprint", "วิ่ง (กดค้าง)"),
        ("dash", "กลิ้ง"),
        ("jump", "กระโดด"),
        ("interact", "คุย / ไหว้ศาล"),
        ("attack", "โจมตี"),
        ("special", "โจมตีพิเศษ"),
        ("skill1", "สกิล 1"),
        ("skill2", "สกิล 2"),
        ("use_item", "ใช้ยาหอม"),
        ("banish", "ปราบ"),
        ("release", "ปลดปล่อย"),
        ("inventory", "กระเป๋า"),
        ("stats", "สถานะตัวละคร"),
        ("quests", "เควส"),
        ("map", "แผนที่"),
        ("craft", "คราฟต์"),
        ("free_cursor", "แสดงเมาส์ (กดค้าง)"),
        ("navigate", "เส้นนำทางเควส"),
        ("emote_wai", "ท่าไหว้"),
        ("emote_wave", "ท่าโบกมือ"),
        ("emote_sit", "ท่านั่ง"),
        ("respawn", "ฟื้นคืนสติ"),
    };

    static readonly Dictionary<string, InputEvent[]> Defaults = new();
    static bool _inputReady, _loaded;

    public static void InitInput()
    {
        if (_inputReady)
            return;
        _inputReady = true;

        void Bind(string action, params InputEvent[] events)
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            foreach (var e in events)
                InputMap.ActionAddEvent(action, e);
            Defaults[action] = events;
        }
        InputEvent K(Key key) => new InputEventKey { PhysicalKeycode = key };
        InputEvent M(MouseButton button) => new InputEventMouseButton { ButtonIndex = button };

        Bind("move_up", K(Key.W), K(Key.Up));
        Bind("move_down", K(Key.S), K(Key.Down));
        Bind("move_left", K(Key.A), K(Key.Left));
        Bind("move_right", K(Key.D), K(Key.Right));
        Bind("sprint", K(Key.Shift));
        Bind("dash", K(Key.Q));
        Bind("jump", K(Key.Space));
        Bind("interact", K(Key.E));
        Bind("attack", M(MouseButton.Left));
        Bind("special", M(MouseButton.Right));
        Bind("skill1", K(Key.Key1));
        Bind("skill2", K(Key.Key2));
        Bind("use_item", K(Key.H));
        Bind("banish", K(Key.F));
        Bind("release", K(Key.G));
        Bind("inventory", K(Key.Tab));
        Bind("stats", K(Key.I));
        Bind("quests", K(Key.J));
        Bind("map", K(Key.M));
        Bind("craft", K(Key.C));
        Bind("free_cursor", K(Key.Alt));
        Bind("navigate", K(Key.N));
        Bind("emote_wai", K(Key.Z));
        Bind("emote_wave", K(Key.X));
        Bind("emote_sit", K(Key.Ctrl));
        Bind("respawn", K(Key.R));
        Bind("pause", K(Key.Escape));
    }

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) == Error.Ok)
        {
            MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume);
            SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume);
            MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume);
            Muted = (bool)cfg.GetValue("audio", "muted", Muted);
            Brightness = (float)cfg.GetValue("video", "brightness", Brightness);
            Fullscreen = (bool)cfg.GetValue("video", "fullscreen", Fullscreen);
            Graphics = (int)cfg.GetValue("video", "graphics", Graphics);
            UiScale = (float)cfg.GetValue("video", "ui_scale", UiScale);
            MouseSensitivity = (float)cfg.GetValue("controls", "mouse_sensitivity", MouseSensitivity);
            InvertY = (bool)cfg.GetValue("controls", "invert_y", InvertY);
            // Bindings saved before the third-person layout (I/Tab/J/M changed) are ignored.
            bool keysCurrent = (int)cfg.GetValue("keys", "_version", 0) == KeysVersion;
            foreach (var (action, _) in keysCurrent ? Rebindable : System.Array.Empty<(string, string)>())
            {
                var ev = Parse((string)cfg.GetValue("keys", action, ""));
                if (ev != null)
                    SetBinding(action, ev);
            }
        }
        ApplyAudio();
        ApplyUiScale();
        ApplyWindow();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "muted", Muted);
        cfg.SetValue("video", "brightness", Brightness);
        cfg.SetValue("video", "fullscreen", Fullscreen);
        cfg.SetValue("video", "graphics", Graphics);
        cfg.SetValue("video", "ui_scale", UiScale);
        cfg.SetValue("controls", "mouse_sensitivity", MouseSensitivity);
        cfg.SetValue("controls", "invert_y", InvertY);
        cfg.SetValue("keys", "_version", KeysVersion);
        foreach (var (action, _) in Rebindable)
        {
            var events = InputMap.ActionGetEvents(action);
            if (events.Count > 0)
                cfg.SetValue("keys", action, Serialize(events[0]));
        }
        cfg.Save(FilePath);
    }

    public static void ApplyAudio()
    {
        SetBus("Master", Muted ? 0f : MasterVolume);
        SetBus("SFX", SfxVolume);
        SetBus("Music", MusicVolume);
    }

    static void SetBus(string name, float linear)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
        AudioServer.SetBusMute(index, linear <= 0.001f);
    }

    public static void ApplyUiScale() => ((SceneTree)Engine.GetMainLoop()).Root.ContentScaleFactor = UiScale;

    public static void ApplyWindow() =>
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);

    // Gives the action a single binding and removes that input from any other rebindable action.
    public static void Rebind(string action, InputEvent ev)
    {
        foreach (var (other, _) in Rebindable)
        {
            if (other == action)
                continue;
            foreach (var existing in InputMap.ActionGetEvents(other).ToArray())
                if (existing.IsMatch(ev))
                    InputMap.ActionEraseEvent(other, existing);
        }
        SetBinding(action, ev);
    }

    static void SetBinding(string action, InputEvent ev)
    {
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, ev);
    }

    public static void ResetBindings()
    {
        foreach (var (action, events) in Defaults)
        {
            InputMap.ActionEraseEvents(action);
            foreach (var e in events)
                InputMap.ActionAddEvent(action, e);
        }
    }

    public static string BindingText(string action)
    {
        var events = InputMap.ActionGetEvents(action);
        return events.Count == 0 ? "-" : EventText(events[0]);
    }

    public static string EventText(InputEvent e) => e switch
    {
        InputEventKey k => OS.GetKeycodeString(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton mb => mb.ButtonIndex switch
        {
            MouseButton.Left => "คลิกซ้าย",
            MouseButton.Right => "คลิกขวา",
            MouseButton.Middle => "คลิกกลาง",
            MouseButton.Xbutton1 => "เมาส์ 4",
            MouseButton.Xbutton2 => "เมาส์ 5",
            _ => $"เมาส์ {(int)mb.ButtonIndex}",
        },
        _ => "?",
    };

    static string Serialize(InputEvent e) => e switch
    {
        InputEventKey k => $"key:{(long)k.PhysicalKeycode}",
        InputEventMouseButton mb => $"mouse:{(long)mb.ButtonIndex}",
        _ => "",
    };

    static InputEvent Parse(string s)
    {
        var parts = s.Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[1], out long value))
            return null;
        return parts[0] switch
        {
            "key" => new InputEventKey { PhysicalKeycode = (Key)value },
            "mouse" => new InputEventMouseButton { ButtonIndex = (MouseButton)value },
            _ => null,
        };
    }
}
