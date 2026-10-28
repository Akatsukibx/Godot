using Godot;
using System.Linq;

// A townsperson from data/npcs.json. Shows "!" when it has a quest to give, "?" when one is ready to hand in
// (grey "?" while the player is still working on it), turns to face the player nearby, and idles with PlayerRig.
public partial class Npc : Node3D
{
    public NpcDef Def { get; private set; }

    PlayerRig _rig;
    Label3D _marker;
    float _baseYaw, _time, _refresh;
    Vector3 _home, _wanderTarget;
    float _wanderWait;

    public void Init(NpcDef def)
    {
        Def = def;
        Name = $"Npc_{def.Id}";
        AddToGroup("npc");
        _baseYaw = Mathf.DegToRad(def.Yaw);
        _rig = new PlayerRig { Yaw = _baseYaw };
        AddChild(_rig);
        _rig.Attach(Models.Npc(def));
        AddProp(def.Activity);

        float top = 2.05f * def.Look.Height;
        AddChild(new Label3D
        {
            Text = $"{def.Name}\n{def.Title}",
            Font = Game.ThaiFont,
            FontSize = 40,
            OutlineSize = 10,
            Modulate = new Color(0.75f, 0.95f, 0.75f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0f, top + 0.35f, 0f),
            PixelSize = 0.005f,
        });
        _marker = new Label3D
        {
            Font = Game.ThaiFont,
            FontSize = 120,
            OutlineSize = 18,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0f, top + 0.95f, 0f),
            PixelSize = 0.005f,
        };
        AddChild(_marker);
        if (def.Activity == "wander")
            return; // wanderers do not block the way
        var body = new StaticBody3D { CollisionLayer = Layers.Npc, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f }, Position = new Vector3(0f, 0.9f, 0f) });
        AddChild(body);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        var p = Game.Player;
        // Turn toward the player when they come close, back to the usual pose otherwise.
        float target = _baseYaw;
        if (p != null && p.GlobalPosition.DistanceTo(GlobalPosition) < 5f)
        {
            var d = p.GlobalPosition - GlobalPosition;
            target = Mathf.Atan2(-d.X, -d.Z);
        }
        bool near = p != null && p.GlobalPosition.DistanceTo(GlobalPosition) < 5f;
        var vel = Vector2.Zero;
        if (Def.Activity == "wander" && !near)
        {
            // Stroll between random spots around home, pausing at each.
            if (_home == Vector3.Zero)
                _home = _wanderTarget = GlobalPosition;
            var to = (_wanderTarget - GlobalPosition) with { Y = 0f };
            if (to.Length() < 0.3f)
            {
                _wanderWait -= dt;
                if (_wanderWait <= 0f)
                {
                    float a = GD.Randf() * Mathf.Tau, r = GD.Randf() * Def.WanderRadius;
                    _wanderTarget = _home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    _wanderWait = 1.5f + GD.Randf() * 3f;
                }
            }
            else
            {
                var step = to.Normalized() * 1.3f;
                GlobalPosition += step * dt;
                target = Mathf.Atan2(-step.X, -step.Z);
                vel = new Vector2(0f, 1.3f);
            }
        }
        _rig.Emote = near && Def.Activity is "sweep" or "farm" or "chat" ? null : Def.Activity is "idle" or "wander" ? null : Def.Activity;
        _rig.Yaw = Mathf.LerpAngle(_rig.Yaw, target, 1f - Mathf.Exp(-5f * dt));
        _rig.Animate(dt, vel, false, false);

        _refresh -= dt;
        if (_refresh <= 0f && p != null)
        {
            _refresh = 0.4f;
            UpdateMarker(p);
        }
        _marker.Position = _marker.Position with { Y = 2.05f * Def.Look.Height + 0.95f + Mathf.Sin(_time * 3f) * 0.08f };
    }

    void UpdateMarker(Player p)
    {
        var log = p.Quests;
        if (log.ReadyFor(Def.Id, p).Any())
            Mark("?", UiTheme.Gold);
        else if (log.OffersFrom(Def.Id, p).Any())
            Mark("!", log.OffersFrom(Def.Id, p).Any(q => q.Main) ? new Color(1f, 0.6f, 0.2f) : UiTheme.Gold);
        else if (log.ActiveFrom(Def.Id, p).Any() || log.Active.Any(q => q.TurnInNpc == Def.Id))
            Mark("?", new Color(0.65f, 0.65f, 0.7f));
        else
            _marker.Visible = false;
    }

    void Mark(string text, Color color)
    {
        _marker.Visible = true;
        _marker.Text = text;
        _marker.Modulate = color;
    }

    // A broom, a fishing rod or a basket of seedlings in the hand, to match what they are doing.
    void AddProp(string activity)
    {
        var hand = _rig.FindChild("Weapon", true, false) as Node3D;
        if (hand == null)
            return;
        var wood = new Color(0.45f, 0.33f, 0.2f);
        switch (activity)
        {
            case "sweep":
                Shapes.Box(hand, new Vector3(0f, -0.2f, 0f), new Vector3(0.04f, 1.3f, 0.04f), wood, 0f, "BroomHandle");
                Shapes.Cylinder(hand, new Vector3(0f, -0.9f, 0f), 0.02f, 0.18f, 0.35f, new Color(0.75f, 0.65f, 0.4f), 0f, "BroomHead");
                break;
            case "fish":
                Shapes.Box(hand, new Vector3(0f, 0f, -1.2f), new Vector3(0.03f, 0.03f, 2.4f), wood, 0f, "Rod").Rotation = new Vector3(0.35f, 0f, 0f);
                break;
            case "farm":
                Shapes.Box(hand, new Vector3(0f, -0.08f, -0.05f), new Vector3(0.06f, 0.18f, 0.06f), new Color(0.3f, 0.55f, 0.25f), 0f, "Seedlings");
                break;
            case "sell":
                Shapes.Box(hand, new Vector3(0f, -0.05f, -0.1f), new Vector3(0.3f, 0.06f, 0.2f), new Color(0.6f, 0.45f, 0.25f), 0f, "Tray");
                break;
        }
    }

    public string Greeting() => Def.Lines.Count > 0 ? Def.Lines[(int)(GD.Randi() % Def.Lines.Count)] : "...";
}
