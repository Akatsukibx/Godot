using Godot;
using System.Collections.Generic;
using System.Linq;

// In-game HUD: player frame (HP / stamina / EXP), clock, quest tracker, toasts, crosshair, skill bar,
// menu icons, prompts, and the windows: status (I), bag (Tab), quests (J), map (M), crafting (K).
public partial class Hud : CanvasLayer
{
	const int MaxToasts = 5;
	const int BagSlots = 30;

	class Slot
	{
		public PanelContainer Root;
		public TextureRect Icon;
		public ColorRect Shade;
		public Label Cd, Count;
		public HBoxContainer Key;
		public Label KeyText;
	}

	class Window
	{
		public Control Root;
		public VBoxContainer Body;
		public System.Action Refresh;
	}

	Control _root;
	Player _player;
	Label _clockLabel, _bannerLabel, _zoneTitle, _zoneSub;
	PlayerFrame _frame;
	TextureRect _rainIcon;
	Tween _zoneTween;
	TextureRect _phaseIcon;
	VBoxContainer _toasts, _tracker;
	Control _crosshair;
	Slot _attackSlot, _specialSlot, _dashSlot, _potionSlot;
	readonly List<Slot> _skillSlots = new();
	HBoxContainer _skillBar;
	readonly List<(string action, Label key)> _menuKeys = new();
	Control _classSelect, _deathScreen;
	readonly Dictionary<string, Window> _windows = new();
	readonly List<Control> _gameplay = new();
	int _trackerVersion = -1;


	// status
	Label _statPoints, _statDerived, _codexLabel;
	readonly List<(Label value, Button plus)> _statRows = new();

	// quests
	VBoxContainer _questList;
	Label _questTitle, _questGiver, _questDesc, _questObjectives, _questRewards;
	Label _questWhere;
	Button _questPin;
	string _questSelected;
	int _questVersion = -1;

	// crafting
	VBoxContainer _recipeList, _recipeIngredients;
	TextureRect _recipeIcon;
	Label _recipeName, _recipeDesc;
	Button _craftButton;
	int _recipeSelected;
	int _craftVersion = -1;

	// map
	MapView _map;
	Button _mapZoneTab, _mapWorldTab;

	System.Action<ClassDef> _onClassChosen;
	System.Action _onRespawn;
	Tween _bannerTween;

	public bool WindowOpen => _windows.Values.Any(w => w.Root.Visible);
	public bool Playing => _player != null && _classSelect == null;

	static void Place(Control c, float ax, float ay, float ox, float oy, Control.GrowDirection gh, Control.GrowDirection gv) =>
		UiTheme.Place(c, ax, ay, ox, oy, gh, gv);

	public override void _Ready()
	{
		_root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Get() };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		BuildFrame();
		BuildClock();
		BuildTracker();
		BuildBossBar();
		BuildToasts();
		BuildCrosshair();
		BuildZoneTitle();
		BuildMenuIcons();
		BuildBanner();
		BuildBag(AddWindow("bag", "กระเป๋า", "inventory", RefreshBag));
		BuildStats(AddWindow("stats", "สถานะตัวละคร", "stats", RefreshStats));
		BuildQuests(AddWindow("quests", "เควส", "quests", RefreshQuests));
		BuildMap(AddWindow("map", "แผนที่", "map", null));
		BuildCraft(AddWindow("craft", "คราฟต์", "craft", RefreshCraft));
		BuildEnhance(AddWindow("enhance", "ตีบวกอุปกรณ์ · ลุงมั่น", "interact", RefreshEnhance));
		BuildDialogue();
		SetGameplayVisible(false);
	}

	// ---------- HUD pieces ----------

	void AddGameplay(Control c)
	{
		_root.AddChild(c);
		_gameplay.Add(c);
	}

	void SetGameplayVisible(bool visible)
	{
		foreach (var c in _gameplay)
			c.Visible = visible;
	}

	static (ProgressBar bar, Label text) TextBar(Color fill, float height, float width = 270f)
	{
		var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
		var f = new StyleBoxFlat { BgColor = fill, BorderColor = fill.Lightened(0.35f), BorderWidthTop = height > 8f ? 2 : 0 };
		
		var bg = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.6f), BorderColor = new Color(1f, 1f, 1f, 0.12f) };
		bg.SetBorderWidthAll(1);
		
		bar.AddThemeStyleboxOverride("fill", f);
		bar.AddThemeStyleboxOverride("background", bg);
		var text = UiTheme.Label("", 13);
		text.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		text.HorizontalAlignment = HorizontalAlignment.Center;
		text.VerticalAlignment = VerticalAlignment.Center;
		bar.AddChild(text);
		return (bar, text);
	}

	void BuildFrame()
	{
		_frame = new PlayerFrame();
		Place(_frame, 0f, 0f, 18f, 16f, Control.GrowDirection.End, Control.GrowDirection.End);
		AddGameplay(_frame);
	}

	// Big zone name in the middle of the screen when entering an area; fades out by itself.
	void BuildZoneTitle()
	{
		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1f, 1f, 1f, 0f) };
		box.AddThemeConstantOverride("separation", 2);
		Place(box, 0.5f, 0.32f, 0f, 0f, Control.GrowDirection.Both, Control.GrowDirection.Both);
		_zoneTitle = UiTheme.Label("", 72, new Color(1f, 0.93f, 0.78f));
		_zoneTitle.AddThemeConstantOverride("outline_size", 14);
		_zoneTitle.HorizontalAlignment = HorizontalAlignment.Center;
		_zoneSub = UiTheme.Label("", 20, new Color(0.85f, 0.85f, 0.9f));
		_zoneSub.AddThemeConstantOverride("outline_size", 8);
		_zoneSub.HorizontalAlignment = HorizontalAlignment.Center;
		box.AddChild(_zoneTitle);
		var line = UiTheme.Dashed(10f);
		line.CustomMinimumSize = new Vector2(420f, 10f);
		box.AddChild(line);
		box.AddChild(_zoneSub);
		_root.AddChild(box);
	}

	public void ZoneTitle(ZoneDef zone)
	{
		if (zone == null)
			return;
		_zoneTitle.Text = zone.Name;
		_zoneSub.Text = $"{zone.Subtitle} · เลเวลแนะนำ {zone.LevelText}";
		var box = (Control)_zoneTitle.GetParent();
		_zoneTween?.Kill();
		_zoneTween = box.CreateTween();
		_zoneTween.TweenProperty(box, "modulate:a", 1f, 0.8);
		_zoneTween.TweenInterval(2.6);
		_zoneTween.TweenProperty(box, "modulate:a", 0f, 1.8);
	}

	// Hides the HUD during cutscenes.
	public void SetCinematic(bool on)
	{
		CloseWindows();
		SetGameplayVisible(!on && _player != null);
	}

	void BuildClock()
	{
		var mini = new MiniMap();
		Place(mini, 1f, 0f, -18f, 18f, Control.GrowDirection.Begin, Control.GrowDirection.End);
		AddGameplay(mini);
		// Clock under the minimap: no frame, centred on its width.
		var h = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(176f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		h.AddThemeConstantOverride("separation", 6);
		Place(h, 1f, 0f, -18f, 200f, Control.GrowDirection.Begin, Control.GrowDirection.End);
		_phaseIcon = UiTheme.IconRect("sun", 22f);
		h.AddChild(_phaseIcon);
		_clockLabel = UiTheme.Label("", 19);
		_clockLabel.VerticalAlignment = VerticalAlignment.Center;
		_clockLabel.AddThemeConstantOverride("outline_size", 5);
		_clockLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
		h.AddChild(_clockLabel);
		_rainIcon = UiTheme.IconRect("rain", 20f);
		_rainIcon.TooltipText = "ฝนตก";
		h.AddChild(_rainIcon);
		AddGameplay(h);
	}

	// Quest tracker on the left, middle of the screen: pinned quests (or the active ones), collapsible.
	Button _trackerHeader;
	bool _trackerCollapsed;
	Label _compass;

	void BuildTracker()
	{
		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(250f, 0f) };
		box.AddThemeConstantOverride("separation", 2);
		var backing = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		backing.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.02f, 0.02f, 0.03f, 0.55f), new Color(1f, 1f, 1f, 0.08f), 1, 6, 8));
		backing.AddChild(box);
		Place(backing, 0f, 0.3f, 18f, 0f, Control.GrowDirection.End, Control.GrowDirection.End);
		_trackerHeader = new Button { Alignment = HorizontalAlignment.Left, Flat = true, MouseFilter = Control.MouseFilterEnum.Stop };
		_trackerHeader.AddThemeFontSizeOverride("font_size", 15);
		_trackerHeader.AddThemeColorOverride("font_color", UiTheme.Gold);
		_trackerHeader.Pressed += () =>
		{
			_trackerCollapsed = !_trackerCollapsed;
			_trackerVersion = -1;
		};
		box.AddChild(_trackerHeader);
		_tracker = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_tracker.AddThemeConstantOverride("separation", 1);
		box.AddChild(_tracker);
		AddGameplay(backing);

		// Compass to the tracked quest's target, top centre.
        _compass = UiTheme.Label("", 15, UiTheme.Gold);
        _compass.HorizontalAlignment = HorizontalAlignment.Center;
        _compass.Visible = false; // the gold marker on the compass bar shows the way instead
        Place(_compass, 0.5f, 0f, 0f, 48f, Control.GrowDirection.Both, Control.GrowDirection.End);
        AddGameplay(_compass);
        var bar = new CompassBar { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(520f, 46f) };
        Place(bar, 0.5f, 0f, 0f, 6f, Control.GrowDirection.Both, Control.GrowDirection.End);
        AddGameplay(bar);
    }

    // Boss health across the top while a zone boss is nearby.
    VBoxContainer _bossBox;
    Label _bossName;
    ProgressBar _bossBar;

    void BuildBossBar()
    {
        _bossBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _bossBox.AddThemeConstantOverride("separation", 2);
        Place(_bossBox, 0.5f, 0f, 0f, 78f, Control.GrowDirection.Both, Control.GrowDirection.End);
        _bossName = UiTheme.Label("", 18, new Color(1f, 0.55f, 0.45f));
        _bossName.HorizontalAlignment = HorizontalAlignment.Center;
        _bossBox.AddChild(_bossName);
        (_bossBar, _) = TextBar(new Color(0.75f, 0.1f, 0.08f), 14f, 520f);
        _bossBox.AddChild(_bossBar);
        AddGameplay(_bossBox);
        _immortalLabel = UiTheme.Label("อมตะ", 15, new Color(1f, 0.85f, 0.3f));
        _immortalLabel.AddThemeConstantOverride("outline_size", 5);
        _immortalLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        Place(_immortalLabel, 0f, 0f, 150f, 128f, Control.GrowDirection.End, Control.GrowDirection.End);
        AddGameplay(_immortalLabel);
    }

    Label _immortalLabel;

    void RefreshBossBar()
    {
        _immortalLabel.Visible = _player.Immortal && !Game.Main.Cutscene.Active;
        var boss = Game.Main.NearestBoss(45f);
        _bossBox.Visible = boss != null && !Game.Main.Cutscene.Active;
        if (boss == null)
            return;
        _bossName.Text = $"{boss.Def.Name}  Lv.{boss.Level}" + (boss.Weakened ? $"  · อ่อนแรง กด {Settings.BindingText("banish")} ปราบ / {Settings.BindingText("release")} ปลดปล่อย" : "");
        _bossBar.MaxValue = boss.MaxHp;
        _bossBar.Value = boss.Hp;
    }

    void RefreshTracker()
    {
        var log = _player.Quests;
        int version = log.Version + _player.InventoryVersion * 1000 + _player.Level * 100000 + (_trackerCollapsed ? 7 : 0);
        if (_trackerVersion == version)
            return;
        _trackerVersion = version;
        foreach (var c in _tracker.GetChildren())
            c.QueueFree();
        var shown = log.PinnedActive.OrderBy(q => q.Main ? 0 : q.Repeatable ? 2 : 1).ToList();
        if (shown.Count == 0)
            shown = log.Active.OrderBy(q => q.Main ? 0 : 1).Take(2).ToList();
        _trackerHeader.Text = $"{(_trackerCollapsed ? "▸" : "▾")}  เควส ({log.Active.Count()})";
        _tracker.Visible = !_trackerCollapsed;
        foreach (var q in shown.Take(4))
        {
            bool ready = log.IsComplete(q, _player);
            var title = UiTheme.Label((ready ? "✓ " : q.Main ? "★ " : "◆ ") + q.Name, 14, ready ? new Color(0.6f, 1f, 0.6f) : q.Main ? new Color(1f, 0.7f, 0.35f) : UiTheme.Cream);
            _tracker.AddChild(title);
            if (ready)
            {
                _tracker.AddChild(UiTheme.Label($"   ส่งที่ {GameData.NpcName(q.TurnInNpc)}", 12, UiTheme.Muted));
                continue;
            }
            for (int i = 0; i < q.Objectives.Count; i++)
            {
                var o = q.Objectives[i];
                _tracker.AddChild(UiTheme.Label($"   {o.Text} {log.Progress(q, i, _player)}/{o.Count}", 12, UiTheme.Muted));
            }
        }
    }

    void RefreshCompass()
    {
        if (Game.Main.Navigation is not QuestNav.Target t || Game.Main.Cutscene.Active)
        {
            _compass.Text = "";
            return;
        }
        var to = t.Position - _player.GlobalPosition;
        float dist = new Vector2(to.X, to.Z).Length();
        // Angle of the target relative to where the camera looks: ↑ ahead, ↓ behind.
        var fwd = Game.Main.CameraForward;
        float angle = Mathf.Wrap(Mathf.Atan2(to.X, to.Z) - Mathf.Atan2(fwd.X, fwd.Z), -Mathf.Pi, Mathf.Pi);
        string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
        int idx = (int)Mathf.PosMod(Mathf.Round(-angle / (Mathf.Pi / 4f)), 8f);
        _compass.Text = "";
    }

    void BuildToasts()
    {
        _toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _toasts.AddThemeConstantOverride("separation", 6);
        _toasts.Alignment = BoxContainer.AlignmentMode.End;
        Place(_toasts, 1f, 0.34f, -18f, 0f, Control.GrowDirection.Begin, Control.GrowDirection.End);
        AddGameplay(_toasts);
    }

    void BuildCrosshair()
    {
        _crosshair = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(_crosshair, 0.5f, 0.5f, 0f, 0f, Control.GrowDirection.Both, Control.GrowDirection.Both);
        _crosshair.Draw += () =>
        {
            _crosshair.DrawCircle(Vector2.Zero, 2.5f, new Color(1f, 1f, 1f, 0.9f));
            _crosshair.DrawArc(Vector2.Zero, 11f, 0f, Mathf.Tau, 32, new Color(1f, 1f, 1f, 0.35f), 1.5f);
        };
        AddGameplay(_crosshair);
    }

    void BuildSkillBar()
    {
        _skillBar?.QueueFree();
        _gameplay.Remove(_skillBar);
        _skillSlots.Clear();
        _skillBar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _skillBar.AddThemeConstantOverride("separation", 8);
        Place(_skillBar, 1f, 1f, -18f, -18f, Control.GrowDirection.Begin, Control.GrowDirection.Begin);

        var d = _player.Def;
        _attackSlot = MakeSlot(d.Attack.Icon, $"{d.Attack.Name}\nโจมตีปกติ");
        _specialSlot = MakeSlot(d.Special.Icon, $"{d.Special.Name}\nโจมตีพิเศษ · คูลดาวน์ {d.Special.Cooldown:0.#} วินาที");
        foreach (var s in d.Skills)
            _skillSlots.Add(MakeSlot(s.Icon, $"{s.Name}\nคูลดาวน์ {s.Cooldown:0.#} วินาที · แรงขึ้นตามพลังจิต"));
        _dashSlot = MakeSlot("dash", $"พุ่ง\nใช้ความอึด {GameData.Config.DashStaminaCost:0} · หลบการโจมตีได้ระหว่างพุ่ง\nพุ่งด้านข้างตัวจะตะแคง");
        _potionSlot = MakeSlot("ya_hom", "ยาหอม\nฟื้นชีวิต");
        foreach (var slot in new[] { _attackSlot, _specialSlot }.Concat(_skillSlots).Append(_dashSlot).Append(_potionSlot))
            _skillBar.AddChild(slot.Root);
        AddGameplay(_skillBar);
    }

    static Slot MakeSlot(string icon, string tooltip)
    {
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 4);
        var root = new PanelContainer
        {
            CustomMinimumSize = new Vector2(66f, 66f),
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = tooltip,
        };
        var style = UiTheme.Glass(5, 0.8f);
        style.ShadowSize = 8;
        root.AddThemeStyleboxOverride("panel", style);
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(holder);

        var iconRect = UiTheme.IconRect(icon, 0f);
        iconRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(iconRect);

        var shade = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.AnchorLeft = 0f;
        shade.AnchorRight = 1f;
        shade.AnchorTop = 1f;
        shade.AnchorBottom = 1f;
        holder.AddChild(shade);

        var cd = UiTheme.Label("", 19);
        cd.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        cd.HorizontalAlignment = HorizontalAlignment.Center;
        cd.VerticalAlignment = VerticalAlignment.Center;
        holder.AddChild(cd);

        var count = UiTheme.Label("", 13);
        Place(count, 1f, 0f, -1f, -3f, Control.GrowDirection.Begin, Control.GrowDirection.End);
        holder.AddChild(count);

        var key = UiTheme.KeyChip("", "");
        key.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(root);
        col.AddChild(key);
        var wrapper = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        wrapper.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        wrapper.AddChild(col);
        return new Slot
        {
            Root = wrapper, Icon = iconRect, Shade = shade, Cd = cd, Count = count, Key = key,
            KeyText = key.GetChild<PanelContainer>(0).GetChild<Label>(0),
        };
    }

    static void UpdateSlot(Slot slot, float left, float total, string action, bool usable, string count = "")
    {
        float frac = total > 0f ? Mathf.Clamp(left / total, 0f, 1f) : 0f;
        slot.Shade.AnchorTop = 1f - frac;
        slot.Cd.Text = left > 0.05f ? left.ToString("0.0") : "";
        slot.KeyText.Text = ShortKey(action);
        slot.Icon.Modulate = usable ? Colors.White : new Color(0.45f, 0.45f, 0.5f);
        slot.Count.Text = count;
    }

    static string ShortKey(string action) => Settings.BindingText(action) switch
    {
        "คลิกซ้าย" => "LMB",
        "คลิกขวา" => "RMB",
        "คลิกกลาง" => "MMB",
        var s => s,
    };

    // Bottom-left: status, bag, quests, map, crafting — clickable while the cursor is free.
    void BuildMenuIcons()
    {
        var bar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 8);
        Place(bar, 0f, 1f, 18f, -18f, Control.GrowDirection.End, Control.GrowDirection.Begin);
        foreach (var (window, icon, label, action) in new[]
                 {
                     ("stats", "status", "สถานะ", "stats"), ("bag", "bag", "กระเป๋า", "inventory"),
                     ("quests", "quest", "เควส", "quests"), ("map", "map", "แผนที่", "map"), ("craft", "craft", "คราฟต์", "craft"),
                 })
        {
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 3);
            var button = new Button
            {
                Icon = Icons.Get(icon),
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(58f, 58f),
                TooltipText = $"{label} ({Settings.BindingText(action)})",
            };
            string w = window;
            button.Pressed += () => Toggle(_windows[w]);
            col.AddChild(button);
            var chip = UiTheme.KeyChip(Settings.BindingText(action), "");
            chip.Alignment = BoxContainer.AlignmentMode.Center;
            col.AddChild(chip);
            var caption = UiTheme.Label(label, 13, UiTheme.Muted);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(caption);
            _menuKeys.Add((action, chip.GetChild<PanelContainer>(0).GetChild<Label>(0)));
            bar.AddChild(col);
        }
        AddGameplay(bar);
    }

    void BuildBanner()
    {
        _bannerLabel = UiTheme.Label("", 48);
        _bannerLabel.AddThemeConstantOverride("outline_size", 10);
        _bannerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bannerLabel.Modulate = new Color(1f, 1f, 1f, 0f);
        Place(_bannerLabel, 0.5f, 0.2f, 0f, 0f, Control.GrowDirection.Both, Control.GrowDirection.Both);
        _root.AddChild(_bannerLabel);
    }

    // ---------- windows ----------

    VBoxContainer AddWindow(string id, string title, string action, System.Action refresh)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Glass(22, 0.9f));
        Place(panel, 0.5f, 0.5f, 0f, 0f, Control.GrowDirection.Both, Control.GrowDirection.Both);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        panel.AddChild(v);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 14);
        header.AddChild(UiTheme.Heading(title, 30));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        header.AddChild(CloseButton());
        v.AddChild(header);
        v.AddChild(UiTheme.Dashed());
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        v.AddChild(body);
        _root.AddChild(panel);
        _windows[id] = new Window { Root = panel, Body = body, Refresh = refresh };
        return body;
    }

    // The ✕ in the corner of every window and dialogue.
    Button CloseButton()
    {
        var b = new Button { Text = "✕", Flat = true, CustomMinimumSize = new Vector2(40f, 40f), FocusMode = Control.FocusModeEnum.None, TooltipText = "ปิด (Esc)" };
        b.AddThemeFontSizeOverride("font_size", 24);
        b.AddThemeColorOverride("font_color", UiTheme.Cream);
        b.AddThemeColorOverride("font_hover_color", new Color(1f, 0.45f, 0.4f));
        b.Pressed += () => CloseWindows();
        return b;
    }

    static VBoxContainer Column(float width, int separation = 8)
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0f) };
        v.AddThemeConstantOverride("separation", separation);
        return v;
    }

    static Label Wrapped(string text, int size, float width, Color? color = null)
    {
        var l = UiTheme.Label(text, size, color);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(width, 0f);
        return l;
    }

    // --- status ---

    void BuildStats(VBoxContainer body)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 24);
        body.AddChild(h);
        var left = Column(560f, 6);
        h.AddChild(left);
        _statPoints = UiTheme.Label("", 18, UiTheme.Gold);
        left.AddChild(_statPoints);
        for (int i = 0; i < Player.StatInfo.Length; i++)
        {
            var (name, desc) = Player.StatInfo[i];
            int stat = i;
            var controls = new HBoxContainer();
            controls.AddThemeConstantOverride("separation", 10);
            var value = UiTheme.Label("0", 20, Colors.White);
            value.CustomMinimumSize = new Vector2(40f, 0f);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            var plus = UiTheme.Button("+", () => _player.AddStat(stat), 40f);
            plus.CustomMinimumSize = new Vector2(40f, 34f);
            controls.AddChild(value);
            controls.AddChild(plus);
            var row = UiTheme.Row(name, controls);
            row.TooltipText = desc;
            left.AddChild(row);
            left.AddChild(UiTheme.Label("   " + desc, 13, UiTheme.Muted));
            _statRows.Add((value, plus));
        }
        left.AddChild(UiTheme.Dashed(8f));
        _statDerived = UiTheme.Label("", 15);
        left.AddChild(_statDerived);

        var right = Column(360f, 6);
        h.AddChild(right);
        right.AddChild(UiTheme.SectionTitle("สมุดภูตผี"));
        right.AddChild(UiTheme.Dashed(8f));
        _codexLabel = Wrapped("", 14, 360f);
        right.AddChild(_codexLabel);
    }

    void RefreshStats()
    {
        var p = _player;
        _statPoints.Text = $"แต้มสถานะคงเหลือ {p.StatPoints}";
        for (int i = 0; i < _statRows.Count; i++)
        {
            _statRows[i].value.Text = p.Attr[i].ToString();
            _statRows[i].plus.Disabled = p.StatPoints <= 0;
        }
        _statDerived.Text = string.Join("\n",
            $"เลเวล {p.Level} · EXP {p.Exp}/{GameData.ExpToNext(p.Level)}",
            $"ชีวิตสูงสุด {p.MaxHp:0} · ความอึดสูงสุด {p.MaxStamina:0} (ฟื้น {p.StaminaRegen:0.#}/วินาที)",
            $"{p.Def.Attack.Name} {p.AttackDamage:0.0} · {p.Def.Special.Name} {p.SpecialDamage:0.0}",
            $"ความเสียหายพลังจิต (สกิล) {p.SkillMult * 100f:0}%" + (p.WeaponBonus > 0f ? $" · อาวุธ +{p.WeaponBonus:0}" : ""));
        _codexLabel.Text = string.Join("\n\n", GameData.Ghosts.Values.Select(g =>
            p.Codex.Contains(g.Id) ? $"• {g.Name}\n{g.Story}" : "• ??? (ยังไม่พบ)"));
    }

    // --- quests ---

    void BuildQuests(VBoxContainer body)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 22);
        body.AddChild(h);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(320f, 440f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        h.AddChild(scroll);
        _questList = Column(300f, 6);
        scroll.AddChild(_questList);

        var detail = Column(440f);
        h.AddChild(detail);
        _questTitle = UiTheme.Label("", 24, Colors.White);
        _questGiver = UiTheme.Label("", 15, UiTheme.Gold);
        _questDesc = Wrapped("", 16, 440f);
        _questObjectives = UiTheme.Label("", 16);
        _questRewards = UiTheme.Label("", 15, UiTheme.Muted);
        _questWhere = UiTheme.Label("", 17, UiTheme.Gold);
        detail.AddChild(_questTitle);
        detail.AddChild(_questGiver);
        detail.AddChild(UiTheme.Dashed(8f));
        detail.AddChild(_questDesc);
        detail.AddChild(UiTheme.SectionTitle("เป้าหมาย"));
        detail.AddChild(_questObjectives);
        detail.AddChild(UiTheme.SectionTitle("รางวัล"));
        detail.AddChild(_questRewards);
        detail.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 8f) });
        detail.AddChild(_questWhere);
        _questPin = UiTheme.Button("", () =>
        {
            var log = _player.Quests;
            if (_questSelected != null)
                log.Pin(_questSelected, !log.IsPinned(_questSelected));
            _questVersion = -1;
        }, 200f);
        detail.AddChild(_questPin);
    }

    static string StateText(QuestLog.State s) => s switch
    {
        QuestLog.State.Available => "รับได้",
        QuestLog.State.Active => "กำลังทำ",
        QuestLog.State.Ready => "ส่งได้!",
        QuestLog.State.Done => "เสร็จแล้ว",
        _ => "",
    };


    void RefreshQuests()
    {
        var log = _player.Quests;
        // Only quests the player has taken (in progress, ready or finished); offers stay with the NPCs.
        var visible = GameData.Quests.Where(q => log.StateOf(q, _player) is QuestLog.State.Active or QuestLog.State.Ready or QuestLog.State.Done || log.IsDone(q.Id))
            .OrderBy(q => log.StateOf(q, _player) == QuestLog.State.Done ? 1 : 0).ThenBy(q => q.Main ? 0 : q.Repeatable ? 2 : 1).ThenBy(q => log.StateOf(q, _player) switch
            {
                QuestLog.State.Ready => 0, QuestLog.State.Active => 1, QuestLog.State.Available => 2, _ => 3,
            }).ToList();
        _questSelected ??= visible.FirstOrDefault()?.Id;
        int version = log.Version + _player.InventoryVersion * 1000 + _player.Level * 100000;
        if (_questVersion != version)
        {
            _questVersion = version;
            foreach (var c in _questList.GetChildren())
                c.QueueFree();
            bool? doneSection = null;
            foreach (var q in visible)
            {
                bool isDone = log.StateOf(q, _player) == QuestLog.State.Done;
                if (doneSection != isDone)
                {
                    doneSection = isDone;
                    _questList.AddChild(UiTheme.Label(isDone ? "เสร็จแล้ว" : "ยังไม่เสร็จ", 15, isDone ? UiTheme.Muted : UiTheme.Gold));
                }
                var state = log.StateOf(q, _player);
                string id = q.Id;
                var b = UiTheme.ListButton($"{q.Name}\n{StateText(state)}", () =>
                {
                    _questSelected = id;
                    _questVersion = -1;
                }, 300f, Icons.Get("quest"));
                b.CustomMinimumSize = new Vector2(300f, 60f);
                b.SetPressedNoSignal(id == _questSelected);
                if (state == QuestLog.State.Done)
                    b.Modulate = new Color(1f, 1f, 1f, 0.55f);
                _questList.AddChild(b);
            }
        }

        var sel = GameData.Quests.FirstOrDefault(q => q.Id == _questSelected);
        if (sel == null)
        {
            _questTitle.Text = "ยังไม่มีเควส";
            _questWhere.Text = "";
            return;
        }
        var st = log.StateOf(sel, _player);
        _questTitle.Text = sel.Name;
        _questGiver.Text = (sel.Main ? $"เนื้อเรื่องหลัก · บทที่ {sel.Chapter} · " : "") + $"ผู้มอบ: {GameData.NpcName(sel.Giver)} · {StateText(st)}";
        _questDesc.Text = sel.Description;
        _questObjectives.Text = string.Join("\n", sel.Objectives.Select((o, i) =>
        {
            int have = st == QuestLog.State.Done ? o.Count : st is QuestLog.State.Active or QuestLog.State.Ready ? log.Progress(sel, i, _player) : 0;
            return $"{(have >= o.Count ? "✓" : "○")} {o.Text}  {have}/{o.Count}";
        }));
        _questRewards.Text = string.Join(" · ", new[] { $"{sel.Rewards.Exp} EXP" }
            .Concat(sel.Rewards.Items.Select(kv => $"{GameData.ItemName(kv.Key)} x{kv.Value}")));
        // Quests are taken from and handed in to NPCs in the world.
        _questPin.Visible = st is QuestLog.State.Active or QuestLog.State.Ready;
        _questPin.Text = log.IsPinned(sel.Id) ? "เลิกปักหมุด" : "ปักหมุด (ติดตาม/นำทาง)";
        _questWhere.Text = st switch
        {
            QuestLog.State.Available => $"รับเควสได้ที่ {GameData.NpcName(sel.Giver)}",
            QuestLog.State.Ready => $"กลับไปส่งเควสที่ {GameData.NpcName(sel.TurnInNpc)}",
            QuestLog.State.Active => $"ส่งเควสที่ {GameData.NpcName(sel.TurnInNpc)}",
            _ => "",
        };
    }

    // --- crafting ---

    void BuildCraft(VBoxContainer body)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 22);
        body.AddChild(h);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(320f, 440f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        h.AddChild(scroll);
        _recipeList = Column(300f, 6);
        scroll.AddChild(_recipeList);

        var detail = Column(400f);
        h.AddChild(detail);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        _recipeIcon = UiTheme.IconRect(null, 80f);
        var names = new VBoxContainer();
        _recipeName = UiTheme.Label("", 22, Colors.White);
        _recipeDesc = Wrapped("", 15, 300f, UiTheme.Muted);
        names.AddChild(_recipeName);
        names.AddChild(_recipeDesc);
        top.AddChild(_recipeIcon);
        top.AddChild(names);
        detail.AddChild(top);
        detail.AddChild(UiTheme.Dashed(8f));
        detail.AddChild(UiTheme.SectionTitle("วัตถุดิบ"));
        _recipeIngredients = Column(400f, 6);
        detail.AddChild(_recipeIngredients);
        detail.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 8f) });
        _craftButton = UiTheme.Button("คราฟต์", () =>
        {
            _player.Craft(GameData.Recipes[_recipeSelected]);
            _craftVersion = -1;
        }, 200f);
        detail.AddChild(_craftButton);
    }

    void RefreshCraft()
    {
        var p = _player;
        if (_craftVersion != p.InventoryVersion)
        {
            _craftVersion = p.InventoryVersion;
            foreach (var c in _recipeList.GetChildren())
                c.QueueFree();
            string category = null;
            for (int i = 0; i < GameData.Recipes.Count; i++)
            {
                var r = GameData.Recipes[i];
                if (r.Category != category)
                {
                    category = r.Category;
                    _recipeList.AddChild(UiTheme.Label(category, 15, UiTheme.Muted));
                }
                var item = GameData.Items[r.Result];
                int index = i;
                var b = UiTheme.ListButton(item.Name, () =>
                {
                    _recipeSelected = index;
                    _craftVersion = -1;
                }, 300f, Icons.Get(item.Icon));
                b.CustomMinimumSize = new Vector2(300f, 46f);
                b.SetPressedNoSignal(i == _recipeSelected);
                if (!p.CanCraft(r))
                    b.Modulate = new Color(1f, 1f, 1f, 0.55f);
                _recipeList.AddChild(b);
            }

            var sel = GameData.Recipes[_recipeSelected];
            var result = GameData.Items[sel.Result];
            _recipeIcon.Texture = Icons.Get(result.Icon);
            _recipeName.Text = sel.Count > 1 ? $"{result.Name} x{sel.Count}" : result.Name;
            string cls = result.Class != null ? $"\n(ใช้ได้เฉพาะ{GameData.Classes.FirstOrDefault(c => c.Id == result.Class)?.Name})" : "";
            _recipeDesc.Text = $"[{GameData.RarityNames[result.Rarity]}] {result.Description}{cls}\nโอกาสสำเร็จ {Player.CraftChance(sel) * 100f:0}%" + (Player.CraftChance(sel) < 1f ? " · พลาดแล้ววัตถุดิบหาย" : "");
            _recipeName.AddThemeColorOverride("font_color", GameData.RarityColor(result.Rarity));
            foreach (var c in _recipeIngredients.GetChildren())
                c.QueueFree();
            foreach (var (id, need) in sel.Ingredients)
            {
                int have = p.ItemCount(id);
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 10);
                row.AddChild(UiTheme.IconRect(GameData.ItemIcon(id), 36f));
                var name = UiTheme.Label(GameData.ItemName(id), 17);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(name);
                row.AddChild(UiTheme.Label($"{have}/{need}", 17, have >= need ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.45f)));
                row.MouseFilter = Control.MouseFilterEnum.Stop;
                row.TooltipText = GameData.ItemSourceText(id, have);
                _recipeIngredients.AddChild(row);
            }
            _craftButton.Disabled = !p.CanCraft(sel);
        }
    }

    // --- map ---

    void BuildMap(VBoxContainer body)
    {
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        body.AddChild(tabs);
        _mapZoneTab = UiTheme.ListButton("พื้นที่นี้", () => SetMapMode(false), 160f);
        _mapWorldTab = UiTheme.ListButton("แผนที่โลก", () => SetMapMode(true), 160f);
        tabs.AddChild(_mapZoneTab);
        tabs.AddChild(_mapWorldTab);
        tabs.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        tabs.AddChild(UiTheme.Label("● ผี   ● ของดรอป   ▲ คุณ", 14, UiTheme.Muted));
        _map = new MapView { CustomMinimumSize = new Vector2(720f, 620f), ClipContents = true };
        body.AddChild(_map);
        SetMapMode(false);
    }

    void SetMapMode(bool world)
    {
        _map.WorldMode = world;
        _mapZoneTab.SetPressedNoSignal(!world);
        _mapWorldTab.SetPressedNoSignal(world);
    }

    void Toggle(Window window)
    {
        bool open = !window.Root.Visible;
        CloseWindows();
        window.Root.Visible = open;
        _bagVersion = _questVersion = _craftVersion = _enhVersion = -1;
        Audio.Play("ui", -6f, 0f);
    }

    public void OpenWindow(string name)
    {
        if (name == "inventory")
            name = "bag";
        if (_windows.TryGetValue(name, out var window) && !window.Root.Visible)
            Toggle(window);
    }

    public bool CloseWindows()
    {
        bool any = WindowOpen;
        foreach (var w in _windows.Values)
            w.Root.Visible = false;
        return any;
    }

    // ---------- dialogue ----------

    Label _dlgName, _dlgText;
    VBoxContainer _dlgOptions;
    Npc _dlgNpc;

    // What a left click anywhere does while talking: the next line, or closing a plain greeting.
    // Null while a question is waiting for an answer.
    System.Action _dlgAdvance;
    Label _dlgHint;

    void BuildDialogue()
    {
        // A full-screen catcher so a click anywhere moves the conversation on; the box sits at the bottom.
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _dlgAdvance != null)
            {
                var a = _dlgAdvance;
                _dlgAdvance = null;
                a();
                root.AcceptEvent();
            }
        };
        var bottom = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bottom.AddThemeConstantOverride("margin_bottom", 120);
        root.AddChild(bottom);
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Pass,
            CustomMinimumSize = new Vector2(820f, 0f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Glass(16, 0.93f));
        bottom.AddChild(panel);
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);
        var header = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _dlgName = UiTheme.Label("", 23, UiTheme.Gold);
        header.AddChild(_dlgName);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass });
        header.AddChild(CloseButton());
        v.AddChild(header);
        v.AddChild(UiTheme.Dashed(8f));
        _dlgText = UiTheme.Label("", 21, Colors.White);
        _dlgText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dlgText.CustomMinimumSize = new Vector2(780f, 56f);
        _dlgText.AddThemeConstantOverride("outline_size", 4);
        _dlgText.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _dlgText.AddThemeConstantOverride("line_spacing", 4);
        v.AddChild(_dlgText);
        _dlgOptions = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _dlgOptions.AddThemeConstantOverride("separation", 7);
        v.AddChild(_dlgOptions);
        _dlgHint = UiTheme.Label("", 13, UiTheme.Muted);
        _dlgHint.HorizontalAlignment = HorizontalAlignment.Right;
        v.AddChild(_dlgHint);
        _root.AddChild(root);
        _windows["dialogue"] = new Window { Root = root, Body = v };
    }

    void SetAdvance(System.Action advance, string hint)
    {
        _dlgAdvance = advance;
        _dlgHint.Text = advance != null ? hint : "เลือกคำตอบด้านบน";
    }

    public void ShowDialogue(Npc npc)
    {
        CloseWindows();
        _dlgNpc = npc;
        _windows["dialogue"].Root.Visible = true;
        _dlgName.Text = $"{npc.Def.Name} · {npc.Def.Title}";
        _player.Quests.Notify(_player, "talk", npc.Def.Id);
        DialogueMenu(npc.Greeting());
    }

    void Option(string text, System.Action action, bool primary = false)
    {
        var b = UiTheme.Button(text, action, 0f);
        b.Alignment = HorizontalAlignment.Left;
        b.CustomMinimumSize = new Vector2(0f, 42f);
        b.AddThemeFontSizeOverride("font_size", 19);
        if (primary)
            b.AddThemeColorOverride("font_color", UiTheme.Gold);
        _dlgOptions.AddChild(b);
    }

    void ClearOptions()
    {
        foreach (var c in _dlgOptions.GetChildren())
        {
            _dlgOptions.RemoveChild(c);
            c.QueueFree();
        }
    }

    void DialogueMenu(string text)
    {
        var p = _player;
        var log = p.Quests;
        string id = _dlgNpc.Def.Id;
        var progress = log.ActiveFrom(id, p).Concat(log.Active.Where(q => q.TurnInNpc == id && log.StateOf(q, p) == QuestLog.State.Active)).Distinct().FirstOrDefault();
        _dlgText.Text = progress?.Dialogue.Progress.FirstOrDefault() is string hint ? $"{text}\n\n({progress.Name}) {hint}" : text;
        ClearOptions();
        foreach (var q in log.ReadyFor(id, p).ToList())
        {
            var quest = q;
            Option($"✓  ส่งเควส: {q.Name}", () => Say(quest.Dialogue.Complete.Count > 0 ? quest.Dialogue.Complete : new() { "ขอบใจมาก" }, () =>
            {
                log.TurnIn(quest, p);
                if (_windows["dialogue"].Root.Visible)
                    DialogueMenu(_dlgNpc.Greeting());
            }), true);
        }
        foreach (var q in log.OffersFrom(id, p).ToList())
        {
            var quest = q;
            Option($"{(q.Main ? "★" : "!")}  {(q.Main ? "เนื้อเรื่องหลัก" : "เควส")}: {q.Name}", () =>
                Say(quest.Dialogue.Offer.Count > 0 ? quest.Dialogue.Offer : new() { quest.Description }, () =>
                {
                    _dlgText.Text = $"{quest.Description}\n\nรางวัล: {quest.Rewards.Exp} EXP" +
                        string.Concat(quest.Rewards.Items.Select(kv => $" · {GameData.ItemName(kv.Key)} x{kv.Value}"));
                    ClearOptions();
                    Option("รับเควส", () =>
                    {
                        log.Accept(quest, p);
                        DialogueMenu("ฝากด้วยนะ");
                    }, true);
                    Option("ไว้ก่อน", () => DialogueMenu(_dlgNpc.Greeting()));
                    SetAdvance(null, "");
                }), true);
        }
        if (id == "lung_man")
            Option("▸  ตีบวกอุปกรณ์", () => { CloseWindows(); OpenEnhance(); });
        if (id == "khon_khap")
            Option("▸  นั่งรถเมล์สายสุดท้าย", () => { CloseWindows(); Game.Main.ShowBusMenu("wiang_mok"); });
        Option("ลาก่อน", () => CloseWindows());
        // Nothing to choose but goodbye: a click closes the conversation.
        SetAdvance(_dlgOptions.GetChildCount() == 1 ? () => CloseWindows() : null, "คลิกเพื่อปิด");
    }


    // A simple choice box in the dialogue panel (fast travel, admin tools…).
    public void ShowMenu(string title, string text, List<(string label, System.Action action)> options)
    {
        CloseWindows();
        _dlgNpc = null;
        _windows["dialogue"].Root.Visible = true;
        _dlgName.Text = title;
        _dlgText.Text = text;
        ClearOptions();
        foreach (var (label, action) in options)
        {
            var a = action;
            Option(label, () => { CloseWindows(); a(); }, true);
        }
        Option("ปิด", () => CloseWindows());
        SetAdvance(null, "");
    }

    // Fades to black with a line of text, runs `during` while hidden, then fades back.
    public void FadeTravel(string text, System.Action during)
    {
        var cover = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Stop };
        cover.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var label = UiTheme.Label(text, 22, new Color(0.9f, 0.85f, 0.7f));
        label.SetAnchorsPreset(Control.LayoutPreset.Center);
        label.GrowHorizontal = label.GrowVertical = Control.GrowDirection.Both;
        label.Modulate = new Color(1f, 1f, 1f, 0f);
        cover.AddChild(label);
        _root.AddChild(cover);
        Audio.Play("dash", -6f, 0f);
        var tw = cover.CreateTween();
        tw.TweenProperty(cover, "color:a", 1f, 0.6);
        tw.TweenProperty(label, "modulate:a", 1f, 0.3);
        tw.TweenCallback(Callable.From(during));
        tw.TweenInterval(1.4);
        tw.TweenProperty(label, "modulate:a", 0f, 0.3);
        tw.TweenProperty(cover, "color:a", 0f, 0.7);
        tw.TweenCallback(Callable.From(cover.QueueFree));
    }
    // Shows lines one at a time, then runs `then`.
    void Say(List<string> lines, System.Action then, int index = 0)
    {
        if (index >= lines.Count)
        {
            then();
            return;
        }
        _dlgText.Text = lines[index];
        ClearOptions();
        Option(index + 1 < lines.Count ? "ต่อไป ▸" : "▸", () => Say(lines, then, index + 1), true);
        SetAdvance(() => Say(lines, then, index + 1), "คลิกเพื่อไปต่อ");
    }

    // ---------- overlays ----------

    public void ShowClassSelect(List<ClassDef> classes, System.Action<ClassDef> onChosen)
    {
        var screen = new ClassSelect();
        _classSelect = screen;
        _root.AddChild(screen);
        screen.Build(classes, def => ChooseClass(def), () =>
        {
            _classSelect.QueueFree();
            _classSelect = null;
            Game.Main.Menus.ShowMainMenu();
        });
        _onClassChosen = onChosen;
    }

    public void ChooseClass(ClassDef def)
    {
        if (_classSelect == null || def.Locked)
            return;
        _classSelect.QueueFree();
        _classSelect = null;
        _onClassChosen?.Invoke(def);
    }

    // Name entry after picking a class; the name is shown above the character and in the frame.
    Control _nameDialog;
    LineEdit _nameEdit;
    System.Action<string> _onName;

    public void AskName(ClassDef def, System.Action<string> onDone)
    {
        _onName = onDone;
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.75f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);
        _nameDialog = dim;
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.AddChild(center);
        var panel = UiTheme.Panel();
        panel.CustomMinimumSize = new Vector2(440f, 0f);
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        panel.AddChild(col);
        col.AddChild(UiTheme.Heading("ตั้งชื่อตัวละคร", 24));
        col.AddChild(UiTheme.Label($"อาชีพ: {def.Name}", 15, UiTheme.Muted));
        _nameEdit = new LineEdit { PlaceholderText = def.Name, MaxLength = 16, CustomMinimumSize = new Vector2(400f, 42f) };
        _nameEdit.AddThemeFontOverride("font", Game.ThaiFont);
        _nameEdit.AddThemeFontSizeOverride("font_size", 20);
        _nameEdit.TextSubmitted += t => ConfirmName(t);
        col.AddChild(_nameEdit);
        col.AddChild(UiTheme.Label("ยาวไม่เกิน 16 ตัวอักษร · เว้นว่างไว้จะใช้ชื่ออาชีพ", 13, UiTheme.Muted));
        var start = new Button { Text = "เริ่มเล่น", CustomMinimumSize = new Vector2(400f, 46f) };
        start.AddThemeFontSizeOverride("font_size", 20);
        start.Pressed += () => ConfirmName(_nameEdit.Text);
        col.AddChild(start);
        _nameEdit.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void ConfirmName(string name)
    {
        if (_nameDialog == null)
            return;
        _nameDialog.QueueFree();
        _nameDialog = null;
        var cb = _onName;
        _onName = null;
        cb?.Invoke(name?.Trim() ?? "");
    }

    public void ShowDeath(System.Action onRespawn)
    {
        _onRespawn = onRespawn;
        CloseWindows();
        _deathScreen = UiTheme.FullScreenOverlay(new Color(0.15f, 0f, 0f, 0.6f), out var v);
        _root.AddChild(_deathScreen);
        var title = UiTheme.Label("คุณสิ้นสติกลางทุ่ง", 46, new Color(1f, 0.6f, 0.55f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var sub = UiTheme.Label("ยายจันทร์ลากคุณกลับมาที่ศาลพระภูมิ", 20);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(title);
        v.AddChild(sub);
        v.AddChild(UiTheme.Button($"ฟื้นคืนสติ  [{Settings.BindingText("respawn")}]", Respawn, 260f));
    }

    void Respawn()
    {
        if (_deathScreen == null)
            return;
        _deathScreen.QueueFree();
        _deathScreen = null;
        _onRespawn?.Invoke();
    }

    // ---------- runtime ----------

    public void BindPlayer(Player p)
    {
        _player = p;
        _frame.Build(p);
        BuildSkillBar();
        SetGameplayVisible(true);
    }

    public void Toast(string message, string icon = null, float duration = 3.5f)
    {
        if (_toasts == null)
            return;
        while (_toasts.GetChildCount() >= MaxToasts)
        {
            var old = _toasts.GetChild(0);
            _toasts.RemoveChild(old);
            old.QueueFree();
        }
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1f, 1f, 1f, 0f) };
        var style = UiTheme.Glass(8, 0.78f);
        style.ShadowSize = 8;
        style.BorderWidthLeft = 3;
        style.BorderColor = new Color(1f, 1f, 1f, 0.3f);
        panel.AddThemeStyleboxOverride("panel", style);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 8);
        panel.AddChild(h);
        if (icon != null)
            h.AddChild(UiTheme.IconRect(icon, 22f));
        var label = UiTheme.Label(message, 14);
        if (message.Length > 40)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(320f, 0f);
        }
        h.AddChild(label);
        _toasts.AddChild(panel);

        var tween = panel.CreateTween();
        tween.TweenProperty(panel, "modulate:a", 1f, 0.2);
        tween.TweenInterval(duration);
        tween.TweenProperty(panel, "modulate:a", 0f, 0.5);
        tween.TweenCallback(Callable.From(panel.QueueFree));
    }

    public void Banner(string text, Color color)
    {
        _bannerTween?.Kill();
        _bannerLabel.Text = text;
        _bannerLabel.Modulate = color;
        _bannerTween = CreateTween();
        _bannerTween.TweenProperty(_bannerLabel, "modulate:a", 0f, 1.2).SetDelay(2.2);
    }

    public override void _Process(double delta)
    {
        if (_classSelect != null)
            return;
        if (_player == null)
            return;
        if (_deathScreen != null && Input.IsActionJustPressed("respawn"))
            Respawn();
        foreach (var (action, window) in new[] { ("inventory", "bag"), ("stats", "stats"), ("quests", "quests"), ("map", "map"), ("craft", "craft") })
            if (Input.IsActionJustPressed(action))
                Toggle(_windows[window]);
        Refresh(_player);
    }

    void Refresh(Player p)
    {
        var dn = Game.DayNight;
        _phaseIcon.Texture = Icons.Get(dn.IsNight ? "moon" : "sun");
        _clockLabel.Text = dn.ClockText();
        _clockLabel.AddThemeColorOverride("font_color", dn.IsNight ? new Color(0.75f, 0.82f, 1f) : new Color(1f, 0.9f, 0.6f));
        _rainIcon.Visible = Game.Main.Weather.Intensity > 0.3f;
        _map.CurrentZone = Game.Main.CurrentZone?.Id ?? "wiang_mok";
        _crosshair.Visible = Game.Main.MouseCaptured && !Game.Main.Cutscene.Active;
        _crosshair.QueueRedraw();
        RefreshTracker();
        RefreshCompass();
        RefreshBossBar();
        if (Input.IsActionJustPressed("navigate"))
        {
            Game.Main.GuideLine = !Game.Main.GuideLine;
            Game.Toast(Game.Main.GuideLine ? "เปิดเส้นนำทางเควส" : "ปิดเส้นนำทางเควส", "map", 1.5f);
        }
        foreach (var (action, key) in _menuKeys)
            key.Text = Settings.BindingText(action);

        UpdateSlot(_attackSlot, p.AttackCooldownLeft, p.AttackCooldown, "attack", true);
        UpdateSlot(_specialSlot, p.SpecialCooldownLeft, p.Def.Special.Cooldown, "special", true);
        for (int i = 0; i < _skillSlots.Count; i++)
            UpdateSlot(_skillSlots[i], p.SkillCooldownLeft(i), p.Def.Skills[i].Cooldown, i == 0 ? "skill1" : "skill2", true);
        UpdateSlot(_dashSlot, p.DashCooldownLeft, GameData.Config.DashCooldown, "dash", p.Stamina >= GameData.Config.DashStaminaCost);
        int potions = p.ItemCount("ya_hom");
        UpdateSlot(_potionSlot, 0f, 0f, "use_item", potions > 0, potions.ToString());

        foreach (var w in _windows.Values)
            if (w.Root.Visible)
                w.Refresh?.Invoke();
    }
}
