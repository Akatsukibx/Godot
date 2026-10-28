using Godot;

// Top-left player frame in the style of Ragnarok-like MMOs: a round live portrait ringed by the EXP gauge
// (yellow), a level badge, and HP with a slightly thinner blue stamina bar beside it.
public partial class PlayerFrame : Control
{
    const float Dia = 112f, Ring = 7f;

    Player _player;
    SubViewport _portraitVp;
    PlayerRig _portraitRig;
    TextureRect _portrait;
    Label _level, _name, _hpText, _stText, _points;
    ProgressBar _hp, _st;
    float _expFrac;

    public void Build(Player p)
    {
        _player = p;
        MouseFilter = MouseFilterEnum.Ignore;
        foreach (var c in GetChildren())
            c.QueueFree();

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);

        // Portrait rendered live from the player's model.
        _portraitVp = new SubViewport { OwnWorld3D = true, Size = new Vector2I(160, 160), Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(_portraitVp);
        _portraitVp.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.16f, 0.2f, 0.3f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        var key = new DirectionalLight3D { LightEnergy = 1.1f, LightColor = new Color(1f, 0.92f, 0.82f) };
        _portraitVp.AddChild(key);
        key.RotationDegrees = new Vector3(-25f, 25f, 0f);
        _portraitRig = new PlayerRig { Yaw = Mathf.Pi + 0.35f };
        _portraitVp.AddChild(_portraitRig);
        _portraitRig.Build(p.Def);
        var cam = new Camera3D { Fov = 30f };
        _portraitVp.AddChild(cam);
        cam.Transform = new Transform3D(Basis.Identity, new Vector3(0.1f, 1.72f, 1.25f)).LookingAt(new Vector3(0f, 1.66f, 0f), Vector3.Up);

        var stack = new Control { CustomMinimumSize = new Vector2(Dia, Dia), MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(stack);
        _portrait = new TextureRect
        {
            Texture = _portraitVp.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = new Vector2(Ring + 2f, Ring + 2f),
            Size = new Vector2(Dia - Ring * 2f - 4f, Dia - Ring * 2f - 4f),
            MouseFilter = MouseFilterEnum.Ignore,
            Material = new ShaderMaterial
            {
                Shader = new Shader
                {
                    Code = @"shader_type canvas_item;
void fragment() {
    vec4 c = texture(TEXTURE, UV);
    c.a *= smoothstep(0.5, 0.47, length(UV - 0.5));
    COLOR = c;
}",
                },
            },
        };
        stack.AddChild(_portrait);
        var ring = new Control { CustomMinimumSize = new Vector2(Dia, Dia), Size = new Vector2(Dia, Dia), MouseFilter = MouseFilterEnum.Ignore };
        ring.Draw += () =>
        {
            var c = new Vector2(Dia, Dia) * 0.5f;
            float r = Dia * 0.5f - Ring * 0.5f;
            ring.DrawCircle(c, Dia * 0.5f, new Color(0f, 0f, 0f, 0.45f), false, 2f, true);
            ring.DrawArc(c, r, 0f, Mathf.Tau, 72, new Color(0.08f, 0.08f, 0.1f, 0.9f), Ring, true);
            if (_expFrac > 0.001f)
                ring.DrawArc(c, r, -Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f + _expFrac * Mathf.Tau, 72, new Color(1f, 0.82f, 0.25f), Ring, true);
            ring.DrawArc(c, Dia * 0.5f - Ring - 1f, 0f, Mathf.Tau, 72, new Color(1f, 1f, 1f, 0.35f), 1.5f, true);
        };
        stack.AddChild(ring);
        // Level badge on the lower left of the ring.
        var badge = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(-4f, Dia - 30f) };
        var badgeStyle = UiTheme.Box(new Color(0.12f, 0.1f, 0.06f, 0.95f), UiTheme.Gold, 2, 0, 3);
        badgeStyle.ContentMarginLeft = badgeStyle.ContentMarginRight = 6;
        badge.AddThemeStyleboxOverride("panel", badgeStyle);
        _level = UiTheme.Label("", 15, UiTheme.Gold);
        badge.AddChild(_level);
        stack.AddChild(badge);

        var info = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 4);
        row.AddChild(info);
        _name = UiTheme.Label("", 19, Colors.White);
        info.AddChild(_name);
        (_hp, _hpText) = Bar(new Color(0.32f, 0.78f, 0.3f), 22f, 260f);
        (_st, _stText) = Bar(new Color(0.3f, 0.62f, 0.95f), 16f, 240f);
        info.AddChild(_hp);
        info.AddChild(_st);
        _points = UiTheme.Label("", 14, UiTheme.Gold);
        info.AddChild(_points);
    }

    static (ProgressBar, Label) Bar(Color fill, float height, float width)
    {
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(width, height), MouseFilter = MouseFilterEnum.Ignore };
        var f = new StyleBoxFlat { BgColor = fill, BorderColor = fill.Lightened(0.4f), BorderWidthTop = 2 };
        var bg = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.65f), BorderColor = new Color(1f, 1f, 1f, 0.18f) };
        bg.SetBorderWidthAll(1);
        bar.AddThemeStyleboxOverride("fill", f);
        bar.AddThemeStyleboxOverride("background", bg);
        var text = UiTheme.Label("", 13);
        text.SetAnchorsPreset(LayoutPreset.FullRect);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        bar.AddChild(text);
        return (bar, text);
    }

    public override void _Process(double delta)
    {
        var p = _player;
        if (p == null || _hp == null)
            return;
        _level.Text = $"Lv {p.Level}";
        _name.Text = p.Def.Name;
        _hp.MaxValue = p.MaxHp;
        _hp.Value = p.Hp;
        _hpText.Text = $"{Mathf.CeilToInt(p.Hp)} / {p.MaxHp:0}" + (p.Guarding ? "  เกราะเพชร" : "");
        _st.MaxValue = p.MaxStamina;
        _st.Value = p.Stamina;
        _stText.Text = $"{Mathf.FloorToInt(p.Stamina)} / {p.MaxStamina:0}";
        _points.Visible = p.StatPoints > 0;
        _points.Text = $"มีแต้มสถานะ {p.StatPoints} แต้ม · กด {Settings.BindingText("stats")}";
        float frac = Mathf.Clamp(p.Exp / (float)GameData.ExpToNext(p.Level), 0f, 1f);
        if (!Mathf.IsEqualApprox(frac, _expFrac))
        {
            _expFrac = frac;
            foreach (var c in GetChildren())
                if (c is HBoxContainer row)
                    row.GetChild<Control>(0).GetChild<Control>(1).QueueRedraw();
        }
        _portraitRig.Animate((float)delta, Vector2.Zero, false, p.Dead);
    }
}
