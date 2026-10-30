using Godot;
using System.Collections.Generic;
using System.Linq;

// Full-screen class selection: class list on the left, the character showcased in the middle,
// emblem, difficulty, description and ability bars on the right, and a red select button.
public partial class ClassSelect : Control
{
	static readonly (string key, string label)[] RatingNames =
	{
		("attack", "พลังโจมตี"), ("defense", "พลังป้องกัน"), ("support", "ช่วยเหลือทีม"), ("control", "ควบคุมศัตรู"),
	};

	List<ClassDef> _classes;
	System.Action<ClassDef> _onChoose;
	ModelPreview _preview;
	readonly List<(Button button, Label name, ColorRect glow)> _entries = new();
	Control _emblem;
	TextureRect _emblemIcon;
	Label _name, _range, _difficulty, _desc, _skills;
	readonly List<List<ColorRect>> _bars = new();
	Button _select;
	int _index;

	public void Build(List<ClassDef> classes, System.Action<ClassDef> onChoose, System.Action onBack)
	{
		_classes = classes;
		_onChoose = onChoose;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		var bg = new ColorRect { Color = new Color(0.03f, 0.03f, 0.035f, 0.97f), MouseFilter = MouseFilterEnum.Ignore };
		bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		// Showcase fills the screen behind the panels.
		_preview = ModelPreview.CreateEmpty(new Vector2(64f, 64f), true);
		AddChild(_preview);
		_preview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_preview.Turn = false;
		_preview.MouseFilter = MouseFilterEnum.Ignore;

		// Back button
		var back = new Button { Text = "←  เลือกคลาส", Flat = true, Position = new Vector2(28f, 20f) };
		back.AddThemeFontSizeOverride("font_size", 22);
		back.AddThemeColorOverride("font_color", UiTheme.Cream);
		back.Pressed += () => onBack();
		AddChild(back);

		// Class list
		var list = new VBoxContainer();
		list.AddThemeConstantOverride("separation", 10);
		UiTheme.Place(list, 0f, 0f, 36f, 90f, GrowDirection.End, GrowDirection.End);
		AddChild(list);
		for (int i = 0; i < classes.Count; i++)
		{
			int index = i;
			var def = classes[i];
			var row = new Control { CustomMinimumSize = new Vector2(300f, 52f) };
			var glow = new ColorRect { Color = new Color(1f, 0.75f, 0.3f, 0f), Position = new Vector2(62f, 42f), Size = new Vector2(220f, 2f), MouseFilter = MouseFilterEnum.Ignore };
			row.AddChild(glow);
			var circle = new PanelContainer { Position = Vector2.Zero, CustomMinimumSize = new Vector2(48f, 48f), MouseFilter = MouseFilterEnum.Ignore };
			var cs = UiTheme.Box(new Color(0.08f, 0.08f, 0.09f), new Color(0.6f, 0.5f, 0.35f), 2, 24, 6);
			circle.AddThemeStyleboxOverride("panel", cs);
			circle.AddChild(UiTheme.IconRect(def.Emblem ?? def.Attack.Icon, 34f));
			row.AddChild(circle);
			var name = UiTheme.Label(def.Name + (def.Locked ? "  (เร็วๆ นี้)" : ""), 18, def.Locked ? UiTheme.Muted : UiTheme.Cream);
			name.Position = new Vector2(62f, 12f);
			row.AddChild(name);
			var hit = new Button { Flat = true, Size = new Vector2(300f, 52f), MouseFilter = MouseFilterEnum.Stop };
			hit.Pressed += () => Select(index);
			row.AddChild(hit);
			list.AddChild(row);
			_entries.Add((hit, name, glow));
		}

		// Right column
		var right = new VBoxContainer { CustomMinimumSize = new Vector2(340f, 0f) };
		right.AddThemeConstantOverride("separation", 8);
		UiTheme.Place(right, 1f, 0f, -36f, 30f, GrowDirection.Begin, GrowDirection.End);
		AddChild(right);
		_emblem = new Control { CustomMinimumSize = new Vector2(340f, 200f) };
		_emblem.Draw += () =>
		{
			var c = new Vector2(170f, 100f);
			_emblem.DrawArc(c, 92f, 0f, Mathf.Tau, 96, new Color(0.75f, 0.62f, 0.4f, 0.5f), 2f, true);
			_emblem.DrawArc(c, 80f, 0f, Mathf.Tau, 96, new Color(0.75f, 0.62f, 0.4f, 0.25f), 1f, true);
			for (int i = 0; i < 24; i++)
			{
				float a = i / 24f * Mathf.Tau;
				var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
				_emblem.DrawLine(c + dir * 84f, c + dir * 92f, new Color(0.75f, 0.62f, 0.4f, 0.4f), 1.5f);
			}
		};
		_emblemIcon = UiTheme.IconRect(null, 110f);
		_emblemIcon.Position = new Vector2(115f, 45f);
		_emblemIcon.Size = new Vector2(110f, 110f);
		_emblem.AddChild(_emblemIcon);
		right.AddChild(_emblem);
		_name = UiTheme.Label("", 34, Colors.White);
		_name.HorizontalAlignment = HorizontalAlignment.Center;
		right.AddChild(_name);
		var tagRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		var tag = new PanelContainer();
		tag.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.3f, 0.3f, 0.32f), new Color(0f, 0f, 0f, 0f), 0, 0, 3));
		_range = UiTheme.Label("", 13);
		tag.AddChild(_range);
		tagRow.AddChild(tag);
		right.AddChild(tagRow);
		right.AddChild(UiTheme.Dashed(8f));
		_difficulty = UiTheme.Label("", 16, UiTheme.Cream);
		_difficulty.HorizontalAlignment = HorizontalAlignment.Center;
		right.AddChild(_difficulty);
		_desc = UiTheme.Label("", 14, UiTheme.Muted);
		_desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_desc.CustomMinimumSize = new Vector2(340f, 0f);
		_desc.HorizontalAlignment = HorizontalAlignment.Center;
		right.AddChild(_desc);
		right.AddChild(UiTheme.Dashed(8f));
		foreach (var (_, label) in RatingNames)
		{
			right.AddChild(UiTheme.Label(label, 14, UiTheme.Cream));
			var bars = new HBoxContainer();
			bars.AddThemeConstantOverride("separation", 4);
			var segs = new List<ColorRect>();
			for (int s = 0; s < 5; s++)
			{
				var seg = new ColorRect { CustomMinimumSize = new Vector2(64f, 5f) };
				bars.AddChild(seg);
				segs.Add(seg);
			}
			right.AddChild(bars);
			_bars.Add(segs);
		}
		right.AddChild(UiTheme.Dashed(8f));
		_skills = UiTheme.Label("", 13, UiTheme.Muted);
		_skills.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_skills.CustomMinimumSize = new Vector2(340f, 0f);
		right.AddChild(_skills);

		// Red select button, bottom right
		_select = new Button { Text = "เลือก", CustomMinimumSize = new Vector2(220f, 56f) };
		_select.AddThemeFontSizeOverride("font_size", 22);
		var red = UiTheme.Box(new Color(0.75f, 0.12f, 0.1f), new Color(1f, 0.55f, 0.45f), 2, 0, 8);
		_select.AddThemeStyleboxOverride("normal", red);
		_select.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.88f, 0.18f, 0.14f), Colors.White, 2, 0, 8));
		_select.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.6f, 0.08f, 0.06f), Colors.White, 2, 0, 8));
		_select.AddThemeStyleboxOverride("disabled", UiTheme.Box(new Color(0.25f, 0.2f, 0.2f), new Color(1f, 1f, 1f, 0.1f), 1, 0, 8));
		_select.AddThemeColorOverride("font_color", Colors.White);
		_select.AddThemeColorOverride("font_hover_color", Colors.White);
		_select.Pressed += () =>
		{
			Audio.Play("ui", -4f, 0f);
			_onChoose(_classes[_index]);
		};
		UiTheme.Place(_select, 1f, 1f, -36f, -32f, GrowDirection.Begin, GrowDirection.Begin);
		AddChild(_select);

		Select(classes.FindIndex(c => !c.Locked));
	}

	void Select(int index)
	{
		_index = Mathf.Clamp(index, 0, _classes.Count - 1);
		var def = _classes[_index];
		Audio.Play("ui", -8f, 0f);
		for (int i = 0; i < _entries.Count; i++)
		{
			bool on = i == _index;
			_entries[i].glow.Color = new Color(1f, 0.72f, 0.3f, on ? 0.55f : 0f);
			_entries[i].name.AddThemeColorOverride("font_color", on ? UiTheme.Gold : _classes[i].Locked ? UiTheme.Muted : UiTheme.Cream);
		}
		_preview.SetClass(def);
		_preview.PlayAction(def.Offhand == "dagger" ? PlayerRig.Act.StabR : def.Attack.Type == "projectile" ? PlayerRig.Act.Cast : PlayerRig.Act.Swing1, 0.6f);
		_emblemIcon.Texture = Icons.Get(def.Emblem ?? def.Attack.Icon);
		_name.Text = def.Name;
		_range.Text = $"ระยะ{def.Range}";
		_difficulty.Text = $"ระดับความยาก  |  {new string('★', def.Difficulty)}{new string('☆', 3 - def.Difficulty)}";
		_desc.Text = $"{def.Role}\n{def.Description}";
		for (int r = 0; r < RatingNames.Length; r++)
		{
			int value = def.Ratings.GetValueOrDefault(RatingNames[r].key);
			for (int s = 0; s < 5; s++)
				_bars[r][s].Color = s < value ? new Color(0.95f, 0.78f, 0.5f) : new Color(1f, 1f, 1f, 0.12f);
		}
		_skills.Text = def.Locked ? "" : string.Join(" · ", new[] { def.Attack.Name, def.Special.Name }.Concat(def.Skills.Select(s => s.Name)));
		_select.Disabled = def.Locked;
		_select.Text = def.Locked ? "เร็วๆ นี้" : "เลือก";
	}

	public override void _Process(double delta)
	{
		if (Input.IsActionJustPressed("move_down") || Input.IsActionJustPressed("ui_down"))
			Select(_index + 1);
		else if (Input.IsActionJustPressed("move_up") || Input.IsActionJustPressed("ui_up"))
			Select(_index - 1);
	}
}
