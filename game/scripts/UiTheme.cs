using Godot;

// One shared look for every menu and HUD element: dark glassy panels with a drop shadow and a thin
// highlight edge, light-grey selection, dashed separators and key chips (after the reference settings screen).
public static class UiTheme
{
    public static readonly Color Gold = new(0.95f, 0.82f, 0.5f);
    public static readonly Color Cream = new(0.93f, 0.92f, 0.89f);
    public static readonly Color Muted = new(0.62f, 0.63f, 0.66f);
    public static readonly Color Dark = new(0.08f, 0.08f, 0.09f);
    public static readonly Color PanelBg = new(0.06f, 0.07f, 0.08f, 0.86f);
    public static readonly Color RowBg = new(0.14f, 0.15f, 0.16f, 0.82f);
    public static readonly Color RowHover = new(0.22f, 0.23f, 0.25f, 0.92f);
    public static readonly Color Selected = new(0.82f, 0.83f, 0.84f, 0.96f);
    public static readonly Color Line = new(1f, 1f, 1f, 0.14f);

    static Theme _theme;

    public static Theme Get()
    {
        if (_theme != null)
            return _theme;
        var t = new Theme { DefaultFont = Game.ThaiFont, DefaultFontSize = 18 };

        t.SetStylebox("panel", "PanelContainer", Glass());
        t.SetStylebox("normal", "Button", Box(RowBg, Line, 1, 0, 10));
        t.SetStylebox("hover", "Button", Box(RowHover, new Color(1f, 1f, 1f, 0.35f), 1, 0, 10));
        t.SetStylebox("pressed", "Button", Box(Selected, Selected, 1, 0, 10));
        t.SetStylebox("hover_pressed", "Button", Box(Selected, Colors.White, 1, 0, 10));
        t.SetStylebox("disabled", "Button", Box(new Color(0.1f, 0.1f, 0.11f, 0.7f), new Color(1f, 1f, 1f, 0.05f), 1, 0, 10));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", Cream);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_pressed_color", "Button", Dark);
        t.SetColor("font_hover_pressed_color", "Button", Dark);
        t.SetColor("font_disabled_color", "Button", new Color(Muted, 0.5f));
        t.SetColor("icon_pressed_color", "Button", Colors.White);

        // Switch-style toggles with "ON/OFF" icons, without the Button frame.
        foreach (var type in new[] { "CheckButton", "CheckBox" })
        {
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            {
                var empty = new StyleBoxEmpty();
                empty.SetContentMarginAll(4);
                t.SetStylebox(state, type, empty);
            }
            t.SetColor("font_color", type, Cream);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Cream);
            t.SetColor("font_hover_pressed_color", type, Colors.White);
        }
        t.SetIcon("checked", "CheckButton", Icons.Get("toggle_on"));
        t.SetIcon("unchecked", "CheckButton", Icons.Get("toggle_off"));

        t.SetColor("font_color", "Label", Cream);
        t.SetColor("font_outline_color", "Label", new Color(0f, 0f, 0f, 0.85f));
        t.SetConstant("outline_size", "Label", 3);

        var grabber = Icons.Get("grabber");
        t.SetIcon("grabber", "HSlider", grabber);
        t.SetIcon("grabber_highlight", "HSlider", grabber);
        var track = Box(new Color(0f, 0f, 0f, 0.5f), Line, 1, 0, 0);
        track.ContentMarginTop = track.ContentMarginBottom = 3;
        t.SetStylebox("slider", "HSlider", track);
        var fill = Box(new Color(0.85f, 0.86f, 0.87f), new Color(0f, 0f, 0f, 0f), 0, 0, 0);
        fill.ContentMarginTop = fill.ContentMarginBottom = 3;
        t.SetStylebox("grabber_area", "HSlider", fill);
        t.SetStylebox("grabber_area_highlight", "HSlider", fill);

        t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.04f, 0.04f, 0.05f, 0.96f), new Color(1f, 1f, 1f, 0.25f), 1, 0, 8));
        t.SetFontSize("font_size", "TooltipLabel", 15);
        t.SetStylebox("separator", "HSeparator", Box(Line, Line, 0, 0, 0));

        var scroll = Box(new Color(1f, 1f, 1f, 0.25f), new Color(0f, 0f, 0f, 0f), 0, 0, 0);
        t.SetStylebox("grabber", "VScrollBar", scroll);
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(new Color(1f, 1f, 1f, 0.45f), new Color(0f, 0f, 0f, 0f), 0, 0, 0));
        t.SetStylebox("scroll", "VScrollBar", Box(new Color(0f, 0f, 0f, 0.3f), new Color(0f, 0f, 0f, 0f), 0, 0, 0));
        _theme = t;
        return t;
    }

    // Square corners everywhere (radius is kept for the rare rounded element).
    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 1, int radius = 0, int margin = 12)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(borderWidth);
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(margin);
        return sb;
    }

    // Panel with depth: soft drop shadow, faint light rim and a brighter top edge.
    public static StyleBoxFlat Glass(int margin = 14, float alpha = 0.86f)
    {
        var sb = Box(new Color(PanelBg, alpha), new Color(1f, 1f, 1f, 0.1f), 1, 0, margin);
        sb.BorderWidthTop = 2;
        sb.BorderColor = new Color(1f, 1f, 1f, 0.16f);
        sb.ShadowColor = new Color(0f, 0f, 0f, 0.55f);
        sb.ShadowSize = 18;
        sb.ShadowOffset = new Vector2(0f, 6f);
        return sb;
    }

    public static Label Label(string text = "", int size = 18, Color? color = null)
    {
        var l = new Label { Text = text };
        if (size != 18)
            l.AddThemeFontSizeOverride("font_size", size);
        if (color is Color c)
            l.AddThemeColorOverride("font_color", c);
        return l;
    }

    public static PanelContainer Panel()
    {
        return new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
    }

    public static Button Button(string text, System.Action onPressed, float minWidth = 0f)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 44f) };
        b.Pressed += () =>
        {
            Audio.Play("ui", -6f, 0f);
            onPressed();
        };
        return b;
    }

    // Left-aligned list entry that stays highlighted while selected (category lists, quest list, recipes).
    public static Button ListButton(string text, System.Action onPressed, float minWidth = 220f, Texture2D icon = null)
    {
        var b = Button(text, onPressed, minWidth);
        b.Alignment = HorizontalAlignment.Left;
        b.ToggleMode = true;
        b.Icon = icon;
        b.ExpandIcon = false;
        b.AddThemeConstantOverride("icon_max_width", 28);
        return b;
    }

    // A small light key cap followed by a caption, e.g. [Esc] กลับ.
    public static HBoxContainer KeyChip(string key, string caption)
    {
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 8);
        var cap = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var capStyle = Box(new Color(0.85f, 0.86f, 0.87f), new Color(0f, 0f, 0f, 0f), 0, 0, 0);
        capStyle.ContentMarginLeft = capStyle.ContentMarginRight = 6;
        capStyle.ContentMarginTop = capStyle.ContentMarginBottom = 1;
        cap.AddThemeStyleboxOverride("panel", capStyle);
        var k = Label(key, 13, Dark);
        k.AddThemeConstantOverride("outline_size", 0);
        cap.AddChild(k);
        h.AddChild(cap);
        if (!string.IsNullOrEmpty(caption))
            h.AddChild(Label(caption, 16));
        return h;
    }

    public static Control Dashed(float height = 14f)
    {
        var c = new Control { CustomMinimumSize = new Vector2(0f, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        c.Draw += () => c.DrawDashedLine(new Vector2(0f, height * 0.5f), new Vector2(c.Size.X, height * 0.5f), new Color(1f, 1f, 1f, 0.3f), 1.5f, 6f);
        return c;
    }

    public static Label Heading(string text, int size = 30) => Label(text, size, Colors.White);

    public static Label SectionTitle(string text) => Label(text, 20, new Color(0.85f, 0.86f, 0.88f));

    // A settings-style row: caption on the left, the control on the right, highlighted on hover.
    public static PanelContainer Row(string caption, Control control, System.Action onHover = null)
    {
        var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(0f, 48f) };
        var normal = Box(RowBg, new Color(1f, 1f, 1f, 0.04f), 1, 0, 10);
        normal.ContentMarginLeft = 16;
        var hover = Box(RowHover, new Color(1f, 1f, 1f, 0.75f), 1, 0, 10);
        hover.ContentMarginLeft = 16;
        row.AddThemeStyleboxOverride("panel", normal);
        row.MouseEntered += () =>
        {
            row.AddThemeStyleboxOverride("panel", hover);
            onHover?.Invoke();
        };
        row.MouseExited += () => row.AddThemeStyleboxOverride("panel", normal);
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        h.AddThemeConstantOverride("separation", 12);
        row.AddChild(h);
        var label = Label(caption);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.VerticalAlignment = VerticalAlignment.Center;
        h.AddChild(label);
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(control);
        return row;
    }

    public static TextureRect IconRect(string icon, float size)
    {
        return new TextureRect
        {
            Texture = Icons.Get(icon),
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    public static void Place(Control c, float ax, float ay, float ox, float oy, Control.GrowDirection gh, Control.GrowDirection gv)
    {
        c.AnchorLeft = c.AnchorRight = ax;
        c.AnchorTop = c.AnchorBottom = ay;
        c.OffsetLeft = c.OffsetRight = ox;
        c.OffsetTop = c.OffsetBottom = oy;
        c.GrowHorizontal = gh;
        c.GrowVertical = gv;
    }

    public static Control FullScreenOverlay(Color color, out VBoxContainer content, int separation = 16)
    {
        var overlay = new ColorRect { Color = color };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(center);
        content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", separation);
        center.AddChild(content);
        return overlay;
    }
}
