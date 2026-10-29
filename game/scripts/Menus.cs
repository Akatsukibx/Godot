using Godot;
using System.Collections.Generic;

// Main menu, pause menu and the settings screen. Runs while the tree is paused, and decides
// when the mouse is captured for the third-person camera.
public partial class Menus : CanvasLayer
{
    static readonly string[] Categories = { "เสียง", "กราฟิก", "การควบคุม", "ปุ่มบังคับ", "ทดสอบ" };

    Control _root, _mainMenu, _pauseMenu, _settings;
    VBoxContainer _rows;
    Label _sectionTitle, _descTitle, _descBody;
    readonly List<Button> _categoryButtons = new();
    System.Action _settingsBack;
    string _waitingAction;
    readonly Dictionary<string, Button> _bindButtons = new();

    public bool MainMenuOpen => _mainMenu != null;
    public bool AnyOpen => _mainMenu != null || _pauseMenu != null || _settings != null;

    public override void _Ready()
    {
        Layer = 10;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Get() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
    }

    public override void _Process(double delta)
    {
        var hud = Game.Main.Hud;
        bool capture = hud.Playing && !hud.WindowOpen && !AnyOpen && !(Game.Player?.Dead ?? true) && !Game.Main.Cutscene.Active
            && !Input.IsActionPressed("free_cursor") && GetWindow().HasFocus();
        var mode = capture ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != mode)
            Input.MouseMode = mode;
    }

    static Label Centered(string text, int size, Color color)
    {
        var l = UiTheme.Label(text, size, color);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        return l;
    }

    static Control MenuPanel(Control overlayContent, out VBoxContainer v)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiTheme.Glass(26));
        v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);
        overlayContent.AddChild(panel);
        return panel;
    }

    // ---------- main menu ----------

    public void ShowMainMenu()
    {
        _mainMenu = UiTheme.FullScreenOverlay(new Color(0.01f, 0.01f, 0.04f, 0.3f), out var outer, 14);
        _root.AddChild(_mainMenu);
        outer.AddChild(Centered("ราตรีเวียงหมอก", 76, UiTheme.Gold));
        outer.AddChild(Centered("MMORPG ผจญภัยล่าผีไทย · KRITZ", 18, UiTheme.Muted));
        outer.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 18f) });
        MenuPanel(outer, out var v);
        if (SaveGame.Exists)
            v.AddChild(MenuButton("เล่นต่อ", () =>
            {
                CloseMainMenu();
                Game.Main.ContinueGame();
            }));
        v.AddChild(MenuButton(SaveGame.Exists ? "เริ่มเกมใหม่" : "เริ่มเกม", () =>
        {
            CloseMainMenu();
            Game.Main.OpenClassSelect();
        }));
        v.AddChild(MenuButton("ตั้งค่า", () =>
        {
            _mainMenu.Visible = false;
            ShowSettings(() => _mainMenu.Visible = true);
        }));
        v.AddChild(MenuButton("ออกเกม", () =>
        {
            Game.Main.SaveNow();
            GetTree().Quit();
        }));
        outer.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });
        outer.AddChild(Centered("ต้นแบบรอบที่ 4 · เล่นคนเดียว", 14, UiTheme.Muted));
    }

    static Button MenuButton(string text, System.Action action)
    {
        var b = UiTheme.Button(text, action, 380f);
        b.CustomMinimumSize = new Vector2(380f, 52f);
        b.Alignment = HorizontalAlignment.Left;
        b.AddThemeFontSizeOverride("font_size", 22);
        return b;
    }

    void CloseMainMenu()
    {
        _mainMenu?.QueueFree();
        _mainMenu = null;
    }

    // ---------- pause ----------

    void ShowPause()
    {
        GetTree().Paused = true;
        _pauseMenu = UiTheme.FullScreenOverlay(new Color(0f, 0f, 0.03f, 0.55f), out var outer, 12);
        _root.AddChild(_pauseMenu);
        outer.AddChild(Centered("พักเกม", 46, Colors.White));
        MenuPanel(outer, out var v);
        v.AddChild(MenuButton("เล่นต่อ", ClosePause));
        v.AddChild(MenuButton("ตั้งค่า", () =>
        {
            _pauseMenu.Visible = false;
            ShowSettings(() => _pauseMenu.Visible = true);
        }));
        v.AddChild(MenuButton("กลับเมนูหลัก", () =>
        {
            Game.Main.SaveNow();
            GetTree().Paused = false;
            GetTree().ReloadCurrentScene();
        }));
        v.AddChild(MenuButton("ออกเกม", () =>
        {
            Game.Main.SaveNow();
            GetTree().Quit();
        }));
        outer.AddChild(CloseX(() => ClosePause()));
    }

    void ClosePause()
    {
        _pauseMenu?.QueueFree();
        _pauseMenu = null;
        GetTree().Paused = false;
    }

    // ---------- settings ----------

    public void ShowSettings(System.Action onBack, int category = 0)
    {
        _settingsBack = onBack;
        _settings = new ColorRect { Color = new Color(0.01f, 0.015f, 0.03f, 0.9f) };
        _settings.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_settings);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" })
            margin.AddThemeConstantOverride($"margin_{side}", 70);
        margin.AddThemeConstantOverride("margin_top", 50);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        _settings.AddChild(margin);
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 14);
        margin.AddChild(page);

        var header = new HBoxContainer();
        header.AddChild(UiTheme.Heading("ตั้งค่า", 40));
        page.AddChild(header);

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 28);
        page.AddChild(columns);

        // Left: categories
        var cats = new VBoxContainer { CustomMinimumSize = new Vector2(230f, 0f) };
        cats.AddThemeConstantOverride("separation", 8);
        columns.AddChild(cats);
        _categoryButtons.Clear();
        for (int i = 0; i < Categories.Length; i++)
        {
            int index = i;
            var b = UiTheme.ListButton(Categories[i], () => SelectCategory(index), 230f);
            b.CustomMinimumSize = new Vector2(230f, 50f);
            cats.AddChild(b);
            _categoryButtons.Add(b);
        }
        cats.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        cats.AddChild(MenuButtonSmall("บันทึกและกลับ", CloseSettings));

        // Centre: rows of the current category
        var center = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        center.AddThemeConstantOverride("separation", 4);
        columns.AddChild(center);
        _sectionTitle = UiTheme.SectionTitle("");
        center.AddChild(_sectionTitle);
        center.AddChild(UiTheme.Dashed());
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        center.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);

        // Right: description of the hovered row
        var right = new PanelContainer { CustomMinimumSize = new Vector2(340f, 0f), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        right.AddThemeStyleboxOverride("panel", UiTheme.Glass(18, 0.7f));
        columns.AddChild(right);
        var rv = new VBoxContainer();
        rv.AddThemeConstantOverride("separation", 8);
        right.AddChild(rv);
        _descTitle = UiTheme.Label("", 20, Colors.White);
        _descBody = UiTheme.Label("", 15, UiTheme.Muted);
        _descBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _descBody.CustomMinimumSize = new Vector2(300f, 0f);
        rv.AddChild(_descTitle);
        rv.AddChild(_descBody);
        rv.AddChild(UiTheme.Dashed());
        rv.AddChild(CloseX(() => CloseSettings()));

        SelectCategory(category);
    }

    static Control CloseX(System.Action close)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var b = new Button { Text = "✕", Flat = true, CustomMinimumSize = new Vector2(40f, 40f), FocusMode = Control.FocusModeEnum.None, TooltipText = "ปิด (Esc)" };
        b.AddThemeFontSizeOverride("font_size", 24);
        b.AddThemeColorOverride("font_hover_color", new Color(1f, 0.45f, 0.4f));
        b.Pressed += close;
        row.AddChild(b);
        return row;
    }

    static Button MenuButtonSmall(string text, System.Action action)
    {
        var b = UiTheme.Button(text, action, 230f);
        b.CustomMinimumSize = new Vector2(230f, 46f);
        return b;
    }

    void SelectCategory(int index)
    {
        for (int i = 0; i < _categoryButtons.Count; i++)
            _categoryButtons[i].SetPressedNoSignal(i == index);
        foreach (var child in _rows.GetChildren())
            child.QueueFree();
        _bindButtons.Clear();
        _waitingAction = null;
        _sectionTitle.Text = Categories[index];
        Describe(Categories[index], "ชี้ที่รายการเพื่อดูคำอธิบาย");
        switch (index)
        {
            case 0: BuildAudio(); break;
            case 1: BuildGraphics(); break;
            case 2: BuildControls(); break;
            case 3: BuildKeys(); break;
            case 4: BuildTest(); break;
        }
    }

    void Describe(string title, string body)
    {
        _descTitle.Text = title;
        _descBody.Text = body;
    }

    void AddRow(string caption, string description, Control control) =>
        _rows.AddChild(UiTheme.Row(caption, control, () => Describe(caption, description)));

    void SliderRow(string caption, string description, float value, System.Action<float> onChanged,
        float min = 0f, float max = 100f, float step = 1f, System.Func<double, string> format = null)
    {
        format ??= v => $"{v:0}%";
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(280f, 24f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var readout = UiTheme.Label(format(slider.Value), 16);
        readout.CustomMinimumSize = new Vector2(70f, 0f);
        readout.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += v =>
        {
            readout.Text = format(v);
            onChanged((float)v);
        };
        h.AddChild(readout);
        h.AddChild(slider);
        AddRow(caption, description, h);
    }

    void ToggleRow(string caption, string description, bool value, System.Action<bool> onChanged)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 10);
        var state = UiTheme.Label(value ? "เปิด" : "ปิด", 16);
        var toggle = new CheckButton { ButtonPressed = value };
        toggle.Toggled += on =>
        {
            state.Text = on ? "เปิด" : "ปิด";
            Audio.Play("ui", -6f, 0f);
            onChanged(on);
        };
        h.AddChild(state);
        h.AddChild(toggle);
        AddRow(caption, description, h);
    }

    // A row of mutually exclusive choices (e.g. ต่ำ / ปานกลาง / สูง).
    void ChoiceRow(string caption, string description, string[] options, int value, System.Action<int> onChanged)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 4);
        var group = new ButtonGroup();
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var b = new Button
            {
                Text = options[i],
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = i == value,
                CustomMinimumSize = new Vector2(104f, 34f),
            };
            b.Pressed += () =>
            {
                Audio.Play("ui", -6f, 0f);
                onChanged(index);
            };
            h.AddChild(b);
        }
        AddRow(caption, description, h);
    }

    void BuildAudio()
    {
        SliderRow("เสียงรวม", "ระดับเสียงทั้งหมดของเกม", Settings.MasterVolume * 100f,
            x => { Settings.MasterVolume = x / 100f; Settings.ApplyAudio(); });
        ToggleRow("ปิดเสียงทั้งหมด", "ปิดเสียงทุกอย่างโดยไม่เปลี่ยนระดับที่ตั้งไว้", Settings.Muted,
            on => { Settings.Muted = on; Settings.ApplyAudio(); });
        SliderRow("เสียงเอฟเฟกต์", "เสียงโจมตี สกิล เก็บของ และเสียงผี", Settings.SfxVolume * 100f, x =>
        {
            Settings.SfxVolume = x / 100f;
            Settings.ApplyAudio();
            Audio.Play("pickup", 0f, 0f);
        });
        SliderRow("เพลงและบรรยากาศ", "เสียงบรรยากาศกลางวันและกลางคืน", Settings.MusicVolume * 100f,
            x => { Settings.MusicVolume = x / 100f; Settings.ApplyAudio(); });
    }

    void BuildGraphics()
    {
        ChoiceRow("คุณภาพกราฟิก",
            "ต่ำ: ไม่มีเงา ความละเอียด 75% เหมาะกับเครื่องสเปกต่ำ\nปานกลาง: เงา แสงฟุ้ง ขอบเรียบ 2x\nสูง: เงาไกล เงาบดบังแสง (SSAO) เงาสะท้อนบนน้ำ หมอกแสงโคม ขอบเรียบ 4x",
            new[] { "ต่ำ", "ปานกลาง", "สูง" }, Settings.Graphics, q =>
            {
                Settings.Graphics = q;
                Game.Main.ApplyGraphics();
            });
        // Brightness maps 0–100% on the slider to 0.5–1.5 in the environment.
        SliderRow("ความสว่าง (แกมม่า)", "50% คือค่าปกติ ปรับขึ้นถ้ากลางคืนมืดเกินไป", (Settings.Brightness - 0.5f) * 100f, x =>
        {
            Settings.Brightness = 0.5f + x / 100f;
            Game.Main?.ApplyBrightness();
        });
        SliderRow("ขนาด UI", "ย่อหรือขยายหน้าจอ UI ทั้งหมด (ค่าเริ่มต้น 85%)", Settings.UiScale * 100f, x =>
        {
            Settings.UiScale = x / 100f;
            Settings.ApplyUiScale();
        }, 65f, 120f, 5f);
        ToggleRow("เต็มจอ", "สลับระหว่างหน้าต่างกับเต็มจอ", Settings.Fullscreen,
            on => { Settings.Fullscreen = on; Settings.ApplyWindow(); });
    }

    void BuildControls()
    {
        SliderRow("ความไวเมาส์", "ความเร็วในการหมุนกล้องเมื่อขยับเมาส์", Settings.MouseSensitivity * 100f,
            x => Settings.MouseSensitivity = x / 100f);
        ToggleRow("กลับแกนเมาส์ขึ้นลง", "เลื่อนเมาส์ขึ้นแล้วกล้องก้มลง", Settings.InvertY, on => Settings.InvertY = on);
        AddRow("ซูมกล้อง", "หมุนลูกกลิ้งเมาส์เพื่อซูมเข้าออก", UiTheme.KeyChip("ลูกกลิ้ง", ""));
        AddRow("แสดงเมาส์ชั่วคราว", "กดค้างเพื่อให้เมาส์โผล่มาคลิกไอคอนบนหน้าจอได้", UiTheme.KeyChip(Settings.BindingText("free_cursor"), ""));
    }

    void BuildKeys()
    {
        foreach (var (action, label) in Settings.Rebindable)
        {
            var button = new Button { CustomMinimumSize = new Vector2(200f, 34f) };
            string captured = action;
            button.Pressed += () => BeginRebind(captured);
            _bindButtons[action] = button;
            AddRow(label, "คลิกที่ปุ่มแล้วกดปุ่มใหม่บนคีย์บอร์ดหรือเมาส์ (Esc ยกเลิก)\nถ้าซ้ำกับคำสั่งอื่น คำสั่งเดิมจะถูกล้างออก", button);
        }
        var reset = UiTheme.Button("คืนค่าเริ่มต้น", () =>
        {
            Settings.ResetBindings();
            RefreshBindButtons();
        }, 200f);
        reset.CustomMinimumSize = new Vector2(200f, 34f);
        AddRow("คืนค่าปุ่มทั้งหมด", "กลับไปใช้ปุ่มบังคับเริ่มต้นของเกม", reset);
        RefreshBindButtons();
    }

    // Test-only clock controls (to be removed later).
    void BuildTest()
    {
        var dn = Game.DayNight;
        SliderRow("เวลาในเกม", "ตั้งเวลาในเกมทันที (สำหรับทดสอบ จะเอาออกภายหลัง)", dn.Hour, h => dn.SetHour(h),
            0f, 23.75f, 0.25f, v => $"{(int)v:00}:{(int)((v - (int)v) * 60):00}");
        SliderRow("ความเร็วเวลา", "ปกติ 1x = 1 วินาทีจริงเท่ากับ 1 นาทีในเกม (สำหรับทดสอบ)", Settings.TimeScale,
            x => Settings.TimeScale = x, 0f, 60f, 1f, v => $"{v:0}x");
        var quick = new HBoxContainer();
        quick.AddThemeConstantOverride("separation", 4);
        foreach (var (text, h) in new[] { ("เช้า", 7f), ("เที่ยง", 12f), ("เย็น", 17.5f), ("ดึก", 22f) })
        {
            var b = UiTheme.Button(text, () => dn.SetHour(h), 80f);
            b.CustomMinimumSize = new Vector2(80f, 34f);
            quick.AddChild(b);
        }
        AddRow("ข้ามไปช่วงเวลา", "กดเพื่อข้ามไปช่วงเวลานั้นทันที", quick);
        ChoiceRow("ฝน", "สุ่ม: ฝนตกตามโอกาสของแต่ละพื้นที่ (zones.json)\nบังคับตก / ไม่ตก: สำหรับทดสอบ", new[] { "สุ่ม", "ตก", "ไม่ตก" }, Settings.RainMode, m => Settings.RainMode = m);
        ToggleRow("โหมดแอดมิน", "เปิดให้วาปได้: กดปุ่มด้านล่าง หรือคลิกตำแหน่งบนแผนที่ (M) เพื่อวาปไปที่นั่น (ชั่วคราว ช่วงทดสอบ)", Settings.AdminMode, on =>
        {
            Settings.AdminMode = on;
            SelectCategory(4);
        });
        if (Settings.AdminMode && Game.Player != null)
            ToggleRow("โหมดอมตะ", $"ไม่เสียเลือด ไม่ตาย ความอึดไม่ลด · เปิด/ปิดด้วยปุ่ม {Settings.BindingText("immortal")}", Game.Player.Immortal, on => Game.Player.SetImmortal(on));
        if (Settings.AdminMode && Game.Player != null)
            ToggleRow("โหมดบิน", $"บินทะลุสิ่งกีดขวาง · Space ขึ้น · Ctrl ลง · Shift บินเร็ว · เปิด/ปิดด้วยปุ่ม {Settings.BindingText("fly")}", Game.Player.Flying, on => Game.Player.SetFlying(on));
        if (Settings.AdminMode && Game.Player != null)
            foreach (var (name, pos) in Game.Main.WarpPoints())
            {
                var p = pos;
                var b = UiTheme.Button("วาป", () =>
                {
                    Game.Main.AdminWarp(p);
                    CloseSettings();
                    if (_pauseMenu != null)
                        ClosePause();
                }, 90f);
                b.CustomMinimumSize = new Vector2(90f, 32f);
                AddRow($"วาปไป{name}", $"ตำแหน่ง {p.X:0}, {p.Z:0}", b);
            }
    }

    void CloseSettings()
    {
        _waitingAction = null;
        Settings.Save();
        _settings?.QueueFree();
        _settings = null;
        _bindButtons.Clear();
        _settingsBack?.Invoke();
    }

    void RefreshBindButtons()
    {
        foreach (var (action, button) in _bindButtons)
            button.Text = Settings.BindingText(action);
    }

    void BeginRebind(string action)
    {
        RefreshBindButtons();
        _waitingAction = action;
        _bindButtons[action].Text = "กดปุ่มใหม่...";
    }

    void FinishRebind(InputEvent ev)
    {
        if (ev != null)
            Settings.Rebind(_waitingAction, ev);
        _waitingAction = null;
        RefreshBindButtons();
    }

    // ---------- input ----------

    public override void _Input(InputEvent e)
    {
        if (_waitingAction != null)
        {
            if (e is InputEventKey k && k.Pressed && !k.Echo)
            {
                FinishRebind(k.PhysicalKeycode == Key.Escape ? null : new InputEventKey { PhysicalKeycode = k.PhysicalKeycode });
                GetViewport().SetInputAsHandled();
            }
            else if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex is MouseButton.Left or MouseButton.Right
                         or MouseButton.Middle or MouseButton.Xbutton1 or MouseButton.Xbutton2)
            {
                FinishRebind(new InputEventMouseButton { ButtonIndex = mb.ButtonIndex });
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (!e.IsActionPressed("pause") || Game.Main.Cutscene.Active)
            return;
        GetViewport().SetInputAsHandled();
        if (_settings != null)
            CloseSettings();
        else if (_pauseMenu != null)
            ClosePause();
        else if (Game.Main.Hud.CloseWindows())
            return;
        else if (Game.Main.Hud.Playing && _mainMenu == null)
            ShowPause();
    }
}
