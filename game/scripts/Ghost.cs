using Godot;
using System.Collections.Generic;

// One ghost. Behaviour comes from GhostDef.Behavior:
//   statue  – only moves while the player is looking away (หุ่นไล่กาสาป)
//   chaser  – fast, weaving, floating (ผีโพง)
//   brute   – slow, tanky, long wind-up (ผีตายโหง)
// Stats in GhostDef are for level 1; each level adds Config.GhostHpPerLevel / DamagePerLevel / ExpPerLevel.
public partial class Ghost : CharacterBody3D
{
    public static readonly List<Ghost> All = new();

    public GhostDef Def { get; private set; }
    public float Hp { get; private set; }
    public float MaxHp { get; private set; }
    public int Level { get; private set; } = 1;
    public string Zone { get; set; }
    public Vector3 Home { get; set; }
    public bool Weakened { get; private set; }
    public bool Gone { get; private set; }

    float _attackCd, _windup, _slowTimer, _slowFactor = 1f, _weakTimer, _flash, _time, _awake, _damage;
    Node3D _visual;
    Label3D _label;
    MeshInstance3D _hpBar;
    readonly List<(StandardMaterial3D mat, Color color)> _mats = new();

    static GameConfig Cfg => GameData.Config;

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    public void Init(GhostDef def, int level = 1)
    {
        Def = def;
        Level = Mathf.Max(1, level);
        MaxHp = def.MaxHp * (1f + Cfg.GhostHpPerLevel * (Level - 1));
        Hp = MaxHp;
        _damage = def.Damage * (1f + Cfg.GhostDamagePerLevel * (Level - 1));
        CollisionLayer = Layers.Ghost;
        CollisionMask = Layers.World | Layers.Npc;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.45f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        });
        _visual = new Node3D();
        AddChild(_visual);
        var model = Models.Ghost(def);
        _visual.AddChild(model);
        PlayerRig.TrackMaterials(model, _mats);

        _label = new Label3D
        {
            Position = new Vector3(0f, 2.95f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 52,
            OutlineSize = 12,
            Font = Game.ThaiFont,
            Modulate = new Color(1f, 0.9f, 0.9f),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        AddChild(_label);
        _hpBar = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.3f, 0.13f) },
            MaterialOverride = HpBarMaterial(),
            Position = new Vector3(0f, 2.78f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_hpBar);
        _time = (float)GD.RandRange(0.0, 10.0);
        _visual.Rotation = new Vector3(0f, (float)GD.RandRange(0.0, Mathf.Tau), 0f);
        UpdateLabel();
    }

    // One shared billboard material for every ghost's health bar; each bar sets its own "fill".
    static ShaderMaterial _hpBarMaterial;

    static ShaderMaterial HpBarMaterial()
    {
        if (_hpBarMaterial != null)
            return _hpBarMaterial;
        var shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_test_disabled, shadows_disabled, fog_disabled;
instance uniform float fill = 1.0;
void vertex() {
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}
void fragment() {
    vec2 px = UV * vec2(130.0, 13.0);
    bool edge = px.x < 1.5 || px.x > 128.5 || px.y < 1.5 || px.y > 11.5;
    vec3 hp = UV.y < 0.5 ? vec3(0.62, 0.03, 0.02) : vec3(0.38, 0.01, 0.01);
    vec3 col = UV.x < fill ? hp : vec3(0.012, 0.01, 0.014);
    ALBEDO = edge ? vec3(0.0) : col;
    ALPHA = 0.92;
}",
        };
        _hpBarMaterial = new ShaderMaterial { Shader = shader, RenderPriority = 10 };
        return _hpBarMaterial;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Gone)
            return;
        float dt = (float)delta;
        _time += dt;
        _flash = Mathf.Max(0f, _flash - dt);
        _attackCd = Mathf.Max(0f, _attackCd - dt);
        _awake = Mathf.Max(0f, _awake - dt);
        if (_slowTimer > 0f)
        {
            _slowTimer -= dt;
            if (_slowTimer <= 0f)
                _slowFactor = 1f;
        }
        UpdateTint();

        if (Weakened)
        {
            _weakTimer -= dt;
            _visual.Position = new Vector3(0f, 0.12f * Mathf.Sin(_time * 2f), 0f);
            if (_weakTimer <= 0f)
            {
                // Nobody chose in time: the talisman banishes it, with the usual EXP and drops.
                if (Cfg.WeakenedAutoBanish && Game.Player is { Dead: false } p)
                    Resolve(true, p, auto: true);
                else
                {
                    Game.Log($"วิญญาณ{Def.Name}สลายไปเองก่อนได้ตัดสินใจ");
                    Vanish();
                }
            }
            return;
        }

        if (Def.Behavior == "chaser")
            _visual.Position = new Vector3(0f, 0.25f * Mathf.Sin(_time * 3f), 0f);

        var player = Game.Player;
        bool night = Game.IsNight;
        bool dormant = !night && !Def.ActiveAtDay && _awake <= 0f;
        _label.Visible = !dormant;
        _hpBar.Visible = !dormant && !Weakened;
        if (dormant || player == null || player.Dead)
        {
            _windup = 0f;
            Velocity = Vector3.Zero;
            return;
        }

        Vector3 to = player.GlobalPosition - GlobalPosition;
        to.Y = 0f;
        float dist = to.Length();
        Vector3 dir = dist > 0.01f ? to / dist : Vector3.Forward;

        // The field village circle is a sanctuary by day; town ghosts never stray far from their alley.
        var zone = GameData.Zone(Zone);
        bool playerSafe = !night && zone != null && !zone.AlleyOnly && player.GlobalPosition.DistanceTo(zone.Center) < Cfg.SafeZoneRadius + 4f;
        float leash = zone?.AlleyOnly == true ? 12f : 45f;
        bool tooFar = player.GlobalPosition.DistanceTo(Home) > leash;
        bool chasing = !playerSafe && !tooFar && (dist < Def.AggroRange * (night ? 1.3f : 1f) || _awake > 0f);
        bool frozen = Def.Behavior == "statue" && dist < 30f && player.LookDir.Dot(-dir) > 0.35f;

        Vector3 move = Vector3.Zero;
        if (_windup > 0f)
        {
            _windup -= dt;
            if (_windup <= 0f)
            {
                if (dist <= Def.AttackRange + 0.5f)
                    player.TakeDamage(_damage * (night ? Cfg.NightDamageMult : 1f), Def.Name);
                _attackCd = Def.AttackCooldown;
            }
        }
        else if (!chasing && GlobalPosition.DistanceTo(Home) > 2f && !frozen && Def.Behavior != "statue")
        {
            var back = (Home - GlobalPosition) with { Y = 0f };
            move = back.Normalized() * Def.Speed * 0.6f;
            _visual.Rotation = new Vector3(0f, Mathf.Atan2(-back.X, -back.Z), 0f);
        }
        else if (chasing && !frozen)
        {
            if (dist <= Def.AttackRange)
            {
                if (_attackCd <= 0f)
                    _windup = Def.Behavior == "brute" ? 0.65f : 0.4f;
            }
            else
            {
                move = dir * Def.Speed * _slowFactor;
                if (Def.Behavior == "chaser")
                    move += dir.Cross(Vector3.Up) * Mathf.Sin(_time * 2.5f) * Def.Speed * 0.5f;
            }
            _visual.Rotation = new Vector3(0f, Mathf.Atan2(-dir.X, -dir.Z), 0f);
        }

        // Keep ghosts from stacking on one spot.
        foreach (var other in All)
        {
            if (other == this || other.Gone)
                continue;
            Vector3 away = GlobalPosition - other.GlobalPosition;
            away.Y = 0f;
            float l = away.Length();
            if (l < 1.1f && l > 0.001f)
                move += away / l * (1.1f - l) * 4f;
        }

        Velocity = new Vector3(move.X, IsOnFloor() ? 0f : Velocity.Y - 20f * dt, move.Z);
        MoveAndSlide();
    }

    void UpdateTint()
    {
        Color tint = Colors.White;
        float w = 0f;
        if (_flash > 0f) { tint = Colors.White; w = 0.7f; }
        else if (_windup > 0f) { tint = new Color(1f, 0.1f, 0.1f); w = 0.55f; }
        else if (Weakened) { tint = new Color(0.6f, 0.7f, 1f); w = 0.45f; }
        foreach (var (mat, color) in _mats)
            mat.AlbedoColor = w > 0f ? color.Lerp(new Color(tint, color.A), w) : color;
    }

    void UpdateLabel()
    {
        _hpBar.SetInstanceShaderParameter("fill", Mathf.Clamp(Hp / MaxHp, 0f, 1f));
        _label.Text = Weakened
            ? $"{Def.Name} · อ่อนแรง\n[{Settings.BindingText("banish")}] ปราบ\n[{Settings.BindingText("release")}] ปลดปล่อย"
            : $"{Def.Name} Lv.{Level}";
        _label.Modulate = LevelColor();
    }

// Name colour by level relative to the player: grey (easy) → white → yellow → red (dangerous).    Color LevelColor()    {        int diff = Level - (Game.Player?.Level ?? Level);        return diff <= -5 ? new Color(0.7f, 0.7f, 0.72f) : diff <= 1 ? new Color(1f, 0.92f, 0.9f) : diff <= 4 ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.4f, 0.35f);    }
    // Name colour by level relative to the player: grey (easy) → white → yellow → red (dangerous).
    Color LevelColor()
    {
        int diff = Level - (Game.Player?.Level ?? Level);
        return diff <= -5 ? new Color(0.7f, 0.7f, 0.72f) : diff <= 1 ? new Color(1f, 0.92f, 0.9f) : diff <= 4 ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.4f, 0.35f);
    }

    public void TakeDamage(float amount, string element)
    {
        if (Weakened || Gone)
            return;
        float mult = Def.Weakness != null && Def.Weakness.TryGetValue(element, out float m) ? m : 1f;
        float dmg = amount * mult;
        Hp -= dmg;
        _flash = 0.12f;

        if (!Game.IsNight && !Def.ActiveAtDay && _awake <= 0f)
            Game.Log($"{Def.Name}ตื่นขึ้นกลางวันแสกๆ!");
        _awake = Mathf.Max(_awake, 15f);

        Color color = mult >= 1.5f ? new Color(1f, 0.6f, 0.15f) : mult < 1f ? new Color(0.65f, 0.65f, 0.7f) : Colors.White;
        string suffix = mult >= 1.5f ? " แพ้ทาง!" : mult < 1f ? " ต้านทาน" : "";
        Fx.DamageNumber(GetParent(), GlobalPosition + Vector3.Up * 2.3f, Mathf.RoundToInt(dmg) + suffix, color);
        Audio.Play("hit", -6f);

        if (Hp <= 0f)
        {
            Hp = 0f;
            EnterWeakened();
        }
        UpdateLabel();
    }

    public void ApplySlow(float slow, float duration)
    {
        _slowFactor = 1f - slow;
        _slowTimer = duration;
    }

    void EnterWeakened()
    {
        Weakened = true;
        _windup = 0f;
        _weakTimer = Cfg.WeakenedDurationSec;
        _label.Visible = true;
        _hpBar.Visible = false;
        _visual.RotationDegrees = new Vector3(20f, _visual.RotationDegrees.Y, 0f);
        Audio.Play("ghost", -4f);
    }

    // Banish = full EXP + item drops on the ground. Release = half EXP, merit, a little healing and a spirit light.
    public void Resolve(bool banish, Player p, bool auto = false)
    {
        if (!Weakened || Gone)
            return;
        bool night = Game.IsNight;
        // Higher-level ghosts give more; far lower-level ones barely anything.
        float expMult = (night ? Cfg.NightExpMult : 1f) * (1f + Cfg.GhostExpPerLevel * (Level - 1))
            * Mathf.Clamp(1f + 0.1f * (Level - p.Level), 0.1f, 1.5f);

        if (p.Codex.Add(Def.Id))
            Game.Toast($"สมุดภูตผี: บันทึก{Def.Name}ใหม่ — {Def.Story}", null, 7f);

        if (banish)
        {
            int exp = Mathf.RoundToInt(Def.Exp * expMult);
            p.Banished++;
            Game.Toast(auto ? $"ยันต์ปราบ{Def.Name}ให้เอง +{exp} EXP" : $"ปราบ{Def.Name} +{exp} EXP");
            foreach (var drop in Def.Drops)
            {
                float chance = Mathf.Min(1f, drop.Chance * (night ? Cfg.NightDropMult : 1f));
                if (GD.Randf() < chance)
                    Loot.Drop(GetParent(), GlobalPosition, drop.Item);
            }
            Audio.Play("banish");
            Fx.Ring(GetParent(), GlobalPosition, 3f, new Color(1f, 0.4f, 0.2f), 0.5f);
            p.Quests.Notify(p, banish ? "banish" : "release", Def.Id);
            p.GainExp(exp);
        }
        else
        {
            int exp = Mathf.RoundToInt(Def.Exp * Cfg.ReleaseExpRatio * expMult);
            p.Released++;
            p.Merit++;
            p.Heal(Cfg.ReleaseHeal);
            Loot.Drop(GetParent(), GlobalPosition, "saeng_winyan");
            Game.Toast($"ปลดปล่อย{Def.Name} +{exp} EXP · บุญ +1 — “{Def.ReleaseText}”", null, 5f);
            Audio.Play("release");
            Fx.Ring(GetParent(), GlobalPosition, 3f, new Color(0.7f, 0.85f, 1f), 0.8f);
            p.Quests.Notify(p, banish ? "banish" : "release", Def.Id);
            p.GainExp(exp);
        }
        Vanish();
    }

    public void Vanish()
    {
        if (Gone)
            return;
        Gone = true;
        Weakened = false;
        CollisionLayer = 0;
        _label.Visible = false;
        _hpBar.Visible = false;
        Velocity = Vector3.Zero;
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(_visual, "position", _visual.Position + Vector3.Up * 2.5f, 1.0);
        tween.TweenProperty(_visual, "scale", new Vector3(0.05f, 0.05f, 0.05f), 1.0);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }
}
