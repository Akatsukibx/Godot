using Godot;
using System.Collections.Generic;

// Plays a cutscene from data/cutscenes.json: letterbox bars, a camera gliding between points, and
// subtitled lines. Click / E / Space shows the next line (lines also advance by themselves);
// hold Esc to skip the whole scene.
public partial class Cutscene : Node
{
    const float SkipHold = 1f;

    public bool Active { get; private set; }

    CanvasLayer _layer;
    ColorRect _top, _bottom;
    Label _speaker, _text, _hint;
    ProgressBar _skipBar;
    Camera3D _cam, _previous;
    Node3D _prop;
    CutsceneDef _def;
    System.Action _onDone;
    int _shot, _line;
    float _shotTime, _lineTime, _skipTime;

    public override void _Ready()
    {
        _layer = new CanvasLayer { Layer = 8, Visible = false };
        AddChild(_layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Get() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(root);
        _top = Bar(root, 0f);
        _bottom = Bar(root, 1f);

        _speaker = UiTheme.Label("", 22, UiTheme.Gold);
        _speaker.HorizontalAlignment = HorizontalAlignment.Center;
        UiTheme.Place(_speaker, 0.5f, 1f, 0f, -118f, Control.GrowDirection.Both, Control.GrowDirection.Begin);
        root.AddChild(_speaker);
        _text = UiTheme.Label("", 24, Colors.White);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(1100f, 0f);
        UiTheme.Place(_text, 0.5f, 1f, 0f, -84f, Control.GrowDirection.Both, Control.GrowDirection.End);
        root.AddChild(_text);
        _hint = UiTheme.Label("คลิกเพื่อไปต่อ · กด Esc ค้างเพื่อข้าม", 14, UiTheme.Muted);
        UiTheme.Place(_hint, 1f, 1f, -24f, -14f, Control.GrowDirection.Begin, Control.GrowDirection.Begin);
        root.AddChild(_hint);
        _skipBar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(160f, 4f), MaxValue = SkipHold, Visible = false };
        UiTheme.Place(_skipBar, 1f, 1f, -24f, -40f, Control.GrowDirection.Begin, Control.GrowDirection.Begin);
        root.AddChild(_skipBar);

        _cam = new Camera3D { Fov = 55f, Far = 800f };
        AddChild(_cam);
    }

    static ColorRect Bar(Control root, float anchor)
    {
        var bar = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AnchorLeft = 0f;
        bar.AnchorRight = 1f;
        bar.AnchorTop = bar.AnchorBottom = anchor;
        bar.OffsetTop = anchor == 0f ? 0f : 0f;
        bar.OffsetBottom = 0f;
        root.AddChild(bar);
        return bar;
    }

    public void Play(string id, System.Action onDone)
    {
        if (!GameData.Cutscenes.TryGetValue(id, out var def) || def.Shots.Count == 0)
        {
            onDone?.Invoke();
            return;
        }
        _def = def;
        _onDone = onDone;
        Active = true;
        if (def.Hour is float hour)
            Game.DayNight.SetHour(hour);
        _previous = GetViewport().GetCamera3D();
        _cam.Current = true;
        _layer.Visible = true;
        Game.Main.Hud.SetCinematic(true);
        // Bars slide in to a 2.35:1 frame.
        float barH = GetViewport().GetVisibleRect().Size.Y * 0.12f;
        _top.OffsetBottom = 0f;
        _bottom.OffsetTop = 0f;
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(_top, "offset_bottom", barH, 0.6);
        tw.TweenProperty(_bottom, "offset_top", -barH, 0.6);
        StartShot(0);
    }

    void StartShot(int index)
    {
        _shot = index;
        _shotTime = 0f;
        _prop?.QueueFree();
        _prop = null;
        var s = _def.Shots[index];
        if (s.Prop == "krasue")
        {
            _prop = Models.BuildKrasue();
            AddChild(_prop);
            _prop.GlobalPosition = GameData.ToV3(s.PropAt);
        }
        ShowLine(0);
    }

    void ShowLine(int i)
    {
        _line = i;
        _lineTime = 0f;
        var lines = _def.Shots[_shot].Lines;
        if (i < lines.Count)
        {
            _speaker.Text = lines[i].Speaker;
            _text.Text = lines[i].Text;
            _text.VisibleRatio = 0f;
        }
        else
        {
            _speaker.Text = "";
            _text.Text = "";
        }
    }

    void Advance()
    {
        var lines = _def.Shots[_shot].Lines;
        if (_text.VisibleRatio < 1f && _line < lines.Count)
        {
            _text.VisibleRatio = 1f;
            return;
        }
        if (_line + 1 < lines.Count)
            ShowLine(_line + 1);
        else if (_shot + 1 < _def.Shots.Count)
            StartShot(_shot + 1);
        else
            Finish();
    }

    void Finish()
    {
        Active = false;
        _prop?.QueueFree();
        _prop = null;
        _layer.Visible = false;
        _skipBar.Visible = false;
        if (_previous != null && IsInstanceValid(_previous))
            _previous.Current = true;
        Game.Main.Hud.SetCinematic(false);
        var done = _onDone;
        _onDone = null;
        done?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!Active)
            return;
        float dt = (float)delta;
        _shotTime += dt;
        _lineTime += dt;
        var s = _def.Shots[_shot];

        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(_shotTime / Mathf.Max(0.1f, s.Duration), 0f, 1f));
        var pos = GameData.ToV3(s.From).Lerp(GameData.ToV3(s.To), t);
        _cam.GlobalTransform = new Transform3D(Basis.Identity, pos).LookingAt(GameData.ToV3(s.Look), Vector3.Up);
        if (_prop != null)
            _prop.Position = GameData.ToV3(s.PropAt) + Vector3.Up * Mathf.Sin(_shotTime * 1.6f) * 0.25f;

        // Typewriter text, then advance by itself after a reading pause.
        if (_line < s.Lines.Count)
        {
            int len = s.Lines[_line].Text.Length;
            _text.VisibleRatio = Mathf.Min(1f, _lineTime * 30f / Mathf.Max(1, len));
            if (_lineTime > 1.6f + len * 0.07f)
                Advance();
        }
        else if (_shotTime >= s.Duration)
            Advance();

        if (Input.IsActionJustPressed("interact") || Input.IsActionJustPressed("jump") || Input.IsActionJustPressed("attack"))
            Advance();

        if (Input.IsActionPressed("pause"))
        {
            _skipTime += dt;
            _skipBar.Visible = true;
            _skipBar.Value = _skipTime;
            if (_skipTime >= SkipHold)
            {
                _skipTime = 0f;
                Finish();
            }
        }
        else
        {
            _skipTime = 0f;
            _skipBar.Visible = false;
        }
    }
}
