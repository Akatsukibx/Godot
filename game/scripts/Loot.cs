using Godot;

// An item dropped by a ghost: pops out, settles on the ground, then flies to a nearby player.
public partial class Loot : Node3D
{
    string _item;
    int _count;
    float _age, _time;
    bool _landed, _collected;
    Vector3 _vel;
    Sprite3D _sprite;

    static GameConfig Cfg => GameData.Config;

    public static void Drop(Node parent, Vector3 at, string item, int count = 1)
    {
        var loot = new Loot();
        parent.AddChild(loot);
        loot.Setup(at, item, count);
    }

    void Setup(Vector3 at, string item, int count)
    {
        AddToGroup("loot");
        _item = item;
        _count = count;
        GlobalPosition = at + Vector3.Up * 1.0f;
        _vel = new Vector3((float)GD.RandRange(-2.5, 2.5), 5f, (float)GD.RandRange(-2.5, 2.5));
        _time = (float)GD.RandRange(0.0, 6.0);
        _sprite = new Sprite3D
        {
            Texture = Icons.Get(GameData.Items.TryGetValue(item, out var def) ? def.Icon : null),
            PixelSize = 0.011f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
        };
        AddChild(_sprite);
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.92f, 0.7f), LightEnergy = 0.7f, OmniRange = 2.2f });
    }

    public override void _Process(double delta)
    {
        if (_collected)
            return;
        float dt = (float)delta;
        _age += dt;
        _time += dt;

        if (!_landed)
        {
            _vel.Y -= 16f * dt;
            GlobalPosition += _vel * dt;
            if (GlobalPosition.Y <= 0.5f)
            {
                GlobalPosition = new Vector3(GlobalPosition.X, 0.5f, GlobalPosition.Z);
                _landed = true;
            }
            return;
        }

        _sprite.Position = new Vector3(0f, Mathf.Sin(_time * 3f) * 0.12f, 0f);

        var player = Game.Player;
        if (player != null && !player.Dead)
        {
            Vector3 target = player.GlobalPosition + Vector3.Up * 0.9f;
            float d = GlobalPosition.DistanceTo(target);
            if (d < 0.7f)
            {
                Collect(player);
                return;
            }
            if (d < Cfg.LootMagnetRange)
                GlobalPosition = GlobalPosition.MoveToward(target, (6f + (Cfg.LootMagnetRange - d) * 6f) * dt);
        }

        if (_age > Cfg.LootLifetimeSec)
            QueueFree();
    }

    void Collect(Player player)
    {
        _collected = true;
        player.AddItem(_item, _count);
        string icon = GameData.Items.TryGetValue(_item, out var def) ? def.Icon : null;
        Game.Toast($"ได้รับ {GameData.ItemName(_item)} x{_count}", icon, 2.5f);
        Audio.Play("pickup", -4f);
        QueueFree();
    }
}
