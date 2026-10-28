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

    public void Init(NpcDef def)
    {
        Def = def;
        Name = $"Npc_{def.Id}";
        AddToGroup("npc");
        _baseYaw = Mathf.DegToRad(def.Yaw);
        _rig = new PlayerRig { Yaw = _baseYaw };
        AddChild(_rig);
        _rig.Attach(Models.Npc(def));

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
        _rig.Yaw = Mathf.LerpAngle(_rig.Yaw, target, 1f - Mathf.Exp(-5f * dt));
        _rig.Animate(dt, Vector2.Zero, false, false);

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

    public string Greeting() => Def.Lines.Count > 0 ? Def.Lines[(int)(GD.Randi() % Def.Lines.Count)] : "...";
}
