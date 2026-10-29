using Godot;
using System.Collections.Generic;
using System.Linq;

// Third-person player: faces the camera's heading and moves relative to it — W forward, S walks backward,
// A/D strafe; sprinting only works forward. Space jumps, Q rolls, low steps are climbed automatically.
// Wades (and slows) in shallow water and regains health after a while out of combat.
// Items, equipment, upgrading, crafting and saving live in PlayerItems.cs.
public partial class Player : CharacterBody3D
{
    public const int Vit = 0, Str = 1, Spi = 2, Sta = 3;

    public static readonly (string name, string desc)[] StatInfo =
    {
        ("พลังชีวิต", "ชีวิตสูงสุด +12 ต่อแต้ม"),
        ("พลังโจมตี", "ความแรงการโจมตีปกติและคลิกขวา +6% ต่อแต้ม"),
        ("พลังจิต", "ความเสียหายและการฟื้นฟูของสกิล +8% ต่อแต้ม"),
        ("ความอึด", "ความอึดสูงสุด +10 และฟื้นความอึดเร็วขึ้น 5% ต่อแต้ม"),
    };

    public const float ResolveRange = 3.5f;
    const float TurnRate = 16f;

    public ClassDef Def { get; private set; }
    public int Level { get; private set; } = 1;
    public int Exp { get; private set; }
    public float Hp { get; private set; }
    public float Stamina { get; private set; }
    public int[] Attr { get; private set; } = new int[4];
    public int StatPoints { get; private set; }
    public int Banished { get; set; }
    public int Released { get; set; }
    public int Merit { get; set; }
    public bool Dead { get; private set; }
    public bool Sprinting { get; private set; }
    public float WaterDepth { get; private set; }
    public bool Swimming { get; private set; }
    public bool Crouching { get; private set; }
    public bool Flying { get; private set; }
    public bool Immortal { get; private set; }
    public string SpawnShrine { get; set; } = "lak_mueang";
    public Vector3 Facing { get; private set; } = Vector3.Forward;
    public HashSet<string> Codex { get; } = new();
    public QuestLog Quests { get; } = new();
    public HashSet<string> BusStops { get; } = new();
    public string CharacterName { get; private set; } = "";
    public bool HasWeapon => Equipped.ContainsKey("weapon");
    public event System.Action Died;

    float LevelMult => 1f + Def.DamagePerLevel * (Level - 1);
    float StrMult => LevelMult * (1f + Attr[Str] * 0.06f) * (Frenzied ? 1.2f : 1f);

    public float MaxHp => Def.MaxHp + Def.HpPerLevel * (Level - 1) + Attr[Vit] * 12f + GearStat(i => i.Hp, b => b.Hp);
    public float MaxStamina => Cfg.MaxStamina + Attr[Sta] * 10f + GearStat(i => i.Stamina, b => b.Stamina);
    public float WeaponBonus => GearStat(i => i.Attack, b => b.Attack);
    public float Defense => GearStat(i => i.Defense, b => b.Defense);
    public float AttackDamage => (HasWeapon ? Def.Attack.Damage + WeaponBonus : Cfg.UnarmedDamage) * StrMult;
    public float SpecialDamage => (Def.Special.Damage + WeaponBonus) * StrMult;
    public float SkillMult => LevelMult * (1f + Attr[Spi] * 0.08f + GearStat(i => i.Skill, b => b.Skill));
    public float StaminaRegen => Cfg.StaminaRegen * (1f + Attr[Sta] * 0.05f);
    public float MoveSpeed => Def.MoveSpeed;
    public float AttackCooldown => Def.Attack.Cooldown * (Frenzied ? 0.5f : 1f);
    public bool Frenzied => _frenzyTimer > 0f;
    // What the camera is looking at (flattened); scarecrows freeze while they are in front of it.
    public Vector3 LookDir => Game.Main.CameraForward;

    public float AttackCooldownLeft => _attackCd;
    public float SpecialCooldownLeft => _specialCd;
    public float SkillCooldownLeft(int i) => _skillCd[i];
    public float DashCooldownLeft => _dashCd;
    public bool Guarding => _guardTimer > 0f;
    public bool InCombat => _combatTimer > 0f;

    static GameConfig Cfg => GameData.Config;

    float _attackCd, _specialCd, _dashCd, _dashTimer, _dashSpeed, _guardTimer, _guardReduction, _invuln, _flash;
    float _aimTimer, _staminaDelay, _noStaminaToast, _yaw, _comboTimer, _rippleTimer, _combatTimer, _frenzyTimer;
    int _combo;
    bool _leftHand;
    PlayerRig.Act _lastSwing;
    float[] _skillCd;
    Vector3 _dashDir;
    Vector2 _localVel;
    PlayerRig _rig;

    public void Init(ClassDef def)
    {
        Def = def;
        _skillCd = new float[def.Skills.Count];
        CollisionLayer = Layers.Player;
        CollisionMask = Layers.World | Layers.Npc;
        FloorMaxAngle = Mathf.DegToRad(52f);
        FloorSnapLength = Cfg.StepHeight + 0.1f;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        });
        _rig = new PlayerRig();
        AddChild(_rig);
        _rig.Build(def);

        Hp = MaxHp;
        Stamina = MaxStamina;
        foreach (var (id, count) in Cfg.StartingItems)
            AddItem(id, count);
        if (def.StarterWeapon != null && GameData.Items.ContainsKey(def.StarterWeapon))
            Equipped["weapon"] = new ItemStack { Id = def.StarterWeapon };
        _nameplate = new Label3D
        {
            Position = new Vector3(0f, 2.25f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Font = Game.ThaiFont,
            FontSize = 40,
            OutlineSize = 10,
            PixelSize = 0.0045f,
            Modulate = new Color(1f, 0.95f, 0.8f),
        };
        AddChild(_nameplate);
        SetName(def.Name);
        RefreshEquipmentVisuals();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Dead)
            return;
        float dt = (float)delta;
        _attackCd = Mathf.Max(0f, _attackCd - dt);
        _specialCd = Mathf.Max(0f, _specialCd - dt);
        _dashCd = Mathf.Max(0f, _dashCd - dt);
        for (int i = 0; i < _skillCd.Length; i++)
            _skillCd[i] = Mathf.Max(0f, _skillCd[i] - dt);
        _guardTimer = Mathf.Max(0f, _guardTimer - dt);
        _frenzyTimer = Mathf.Max(0f, _frenzyTimer - dt);
        _invuln = Mathf.Max(0f, _invuln - dt);
        _aimTimer = Mathf.Max(0f, _aimTimer - dt);
        _noStaminaToast = Mathf.Max(0f, _noStaminaToast - dt);
        _staminaDelay = Mathf.Max(0f, _staminaDelay - dt);
        _comboTimer = Mathf.Max(0f, _comboTimer - dt);
        _combatTimer = Mathf.Max(0f, _combatTimer - dt);
        if (_comboTimer <= 0f)
            _combo = 0;
        if (_staminaDelay <= 0f)
            Stamina = Mathf.Min(MaxStamina, Stamina + StaminaRegen * dt);
        // Out of combat long enough: health slowly comes back.
        if (!InCombat && Hp < MaxHp)
            Hp = Mathf.Min(MaxHp, Hp + MaxHp * Cfg.HpRegenPercent * dt);

        bool controls = Game.Main.ControlsEnabled;
        Vector2 input = controls ? Input.GetVector("move_left", "move_right", "move_up", "move_down") : Vector2.Zero;
        Basis cam = Game.Main.CameraYawBasis;
        Vector3 move = cam * new Vector3(input.X, 0f, input.Y);
        bool moving = move.LengthSquared() > 0.01f;
        float forwardInput = -input.Y;
        bool onFloor = IsOnFloor();
        WaterDepth = Game.Main.WaterDepthAt(GlobalPosition);

        if (controls && Input.IsActionJustPressed("dash") && _dashCd <= 0f && _dashTimer <= 0f && !Swimming && !Flying)
            TryRoll(moving ? move.Normalized() : Facing);

        Vector3 v;
        if (_dashTimer > 0f)
        {
            _dashTimer -= dt;
            v = _dashDir * _dashSpeed * WadeFactor();
            Sprinting = false;
        }
        else
        {
            // Backpedal and strafe are slower than running forward; only forward running can sprint.
            float mult = forwardInput < -0.1f ? Cfg.BackpedalMultiplier
                : Mathf.Abs(input.X) > Mathf.Abs(forwardInput) ? Cfg.StrafeMultiplier : 1f;
            Sprinting = moving && forwardInput > 0.3f && Input.IsActionPressed("sprint") && !Crouching && !Swimming;
            if (Crouching)
                mult *= Cfg.CrouchMultiplier;
            float water = Swimming ? (Stamina > 0f ? Cfg.SwimMultiplier : Cfg.SwimMultiplier * 0.5f) : WadeFactor();
            v = move.LimitLength(1f) * MoveSpeed * mult * (Sprinting ? Cfg.SprintMultiplier : 1f) * water;
        }

        // Deep water: float with the head above the surface and swim (slowly, using stamina).
        var surface = Game.Main.WaterSurfaceAt(GlobalPosition);
        Swimming = !Flying && surface is float && WaterDepth > Cfg.SwimDepth;
        if (Swimming)
        {
            Crouching = false;
            float target = (float)surface - Cfg.SwimFloat;
            v.Y = Mathf.Clamp((target - GlobalPosition.Y) * 5f, -4f, 3f);
            if (moving)
            {
                Stamina = Immortal ? Stamina : Mathf.Max(0f, Stamina - Cfg.SwimStaminaDrain * dt);
                _staminaDelay = 0.6f;
            }
        }
        else if (Flying)
        {
            float up = (Input.IsActionPressed("jump") ? 1f : 0f) - (Input.IsKeyPressed(Key.Ctrl) ? 1f : 0f);
            float fast = Input.IsActionPressed("sprint") ? 3.5f : 1f;
            v = move.LimitLength(1f) * Cfg.FlySpeed * fast;
            v.Y = up * Cfg.FlySpeed * fast;
        }
        else if (controls && onFloor && Input.IsActionJustPressed("jump") && _dashTimer <= 0f && WaterDepth < 0.8f)
        {
            v.Y = Cfg.JumpVelocity;
            Crouching = false;
        }
        else
            v.Y = onFloor ? 0f : Velocity.Y - 20f * dt;
        if (onFloor && v.Y <= 0f && !Flying)
            TryStepUp(new Vector3(v.X, 0f, v.Z) * dt, Cfg.StepHeight);
        // Swimming into a pier or a bank: haul out if the top is within reach.
        if (Swimming && moving)
            TryStepUp(new Vector3(v.X, 0f, v.Z).Normalized() * 0.3f, Cfg.ClimbOutHeight);
        Velocity = v;
        MoveAndSlide();

        UpdateFacing(dt, moving);
        var flat = new Vector3(Velocity.X, 0f, Velocity.Z);
        var right = new Vector3(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        _localVel = new Vector2(flat.Dot(right), flat.Dot(Facing));
        if (moving)
            _rig.Emote = null;

        // Ripples while wading
        _rippleTimer -= dt;
        if (WaterDepth > 0.1f && flat.LengthSquared() > 1f && _rippleTimer <= 0f)
        {
            _rippleTimer = 0.35f;
            Fx.Ring(GetParent(), GlobalPosition, 1.2f, new Color(0.8f, 0.9f, 1f), 0.6f);
        }

        if (controls)
        {
            if (Input.IsActionPressed("attack") && _attackCd <= 0f && _dashTimer <= 0f)
                Attack();
            if (Input.IsActionJustPressed("special") && _specialCd <= 0f && _dashTimer <= 0f)
                Special();
            if (_skillCd.Length > 0 && Input.IsActionJustPressed("skill1"))
                UseSkill(0);
            if (_skillCd.Length > 1 && Input.IsActionJustPressed("skill2"))
                UseSkill(1);
            if (Input.IsActionJustPressed("use_item"))
                UseItem(ItemCount("ya_hom") > 0 || ItemCount("ya_hom_yai") == 0 ? "ya_hom" : "ya_hom_yai");
            if (Input.IsActionJustPressed("banish"))
                Game.Main.NearestWeakened(GlobalPosition, ResolveRange)?.Resolve(true, this);
            if (Input.IsActionJustPressed("release"))
                Game.Main.NearestWeakened(GlobalPosition, ResolveRange)?.Resolve(false, this);
            if (Input.IsActionJustPressed("emote_wai"))
                ToggleEmote("wai");
            if (Input.IsActionJustPressed("emote_wave"))
                ToggleEmote("wave");
            if (Input.IsActionJustPressed("emote_sit") && !Flying && !Swimming)
                Crouching = !Crouching;
            if (Input.IsActionJustPressed("fly") && Settings.AdminMode)
                SetFlying(!Flying);
            if (Input.IsActionJustPressed("immortal") && Settings.AdminMode)
                SetImmortal(!Immortal);
        }
    }

    // Climbs kerbs, steps and plinths up to Config.StepHeight without jumping: if the way ahead is
    // blocked but clear one step higher, lift the body; floor snapping settles it on the step.
    void TryStepUp(Vector3 motion, float height)
    {
        if (motion.LengthSquared() < 1e-6f)
            return;
        var probe = motion.Normalized() * Mathf.Max(motion.Length(), 0.12f);
        var from = GlobalTransform;
        if (!TestMove(from, probe))
            return;
        var lift = Vector3.Up * height;
        if (TestMove(from, lift))
            return;
        var raised = from.Translated(lift);
        if (TestMove(raised, probe))
            return;
        GlobalPosition += lift;
    }

    float WadeFactor() => Mathf.Lerp(1f, Cfg.WadeSlowAtMaxDepth, Mathf.Clamp(WaterDepth / Cfg.MaxWadeDepth, 0f, 1f));

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _flash = Mathf.Max(0f, _flash - dt);
        if (_flash > 0f)
            _rig.SetTint(new Color(1f, 0.2f, 0.2f), 0.6f);
        else if (Guarding)
            _rig.SetTint(new Color(1f, 0.9f, 0.4f), 0.35f);
        else if (Frenzied)
            _rig.SetTint(new Color(0.5f, 0.55f, 1f), 0.35f);
        else
            _rig.SetTint(Colors.White, 0f);
        if (_debugAnim != null)
        {
            DebugAnimate(dt);
            return;
        }
        _rig.Crouch = Crouching;
        _rig.Swim = Swimming || Flying;
        bool airborne = !IsOnFloor() && Velocity.Y > -4f && !_rig.Rolling && !Dead;
        airborne &= !Swimming && !Flying;
        _rig.Animate(dt, Dead ? Vector2.Zero : _localVel, Sprinting, Dead, airborne && Mathf.Abs(Velocity.Y) > 0.5f);
    }

    Label3D _nameplate;

    public void SetName(string name)
    {
        CharacterName = string.IsNullOrWhiteSpace(name) ? Def.Name : name.Trim();
        _nameplate.Text = CharacterName;
    }

    public void SetImmortal(bool on)
    {
        Immortal = on;
        if (on)
        {
            Hp = MaxHp;
            Stamina = MaxStamina;
        }
        Game.Toast(on ? "โหมดอมตะ: ไม่เสียเลือด ไม่ตาย ความอึดไม่ลด" : "ปิดโหมดอมตะ");
    }

    public void SetFlying(bool on)
    {
        Flying = on;
        CollisionMask = on ? 0u : Layers.World | Layers.Npc;
        Velocity = Vector3.Zero;
        Game.Toast(on ? "โหมดบิน: Space ขึ้น · Ctrl ลง · Shift เร็ว" : "ปิดโหมดบิน");
    }

    void ToggleEmote(string emote) => _rig.Emote = _rig.Emote == emote ? null : emote;

    // Dev only (--anim=…): loops one animation in place for screenshots.
    string _debugAnim;
    float _debugTimer;
    public void DebugAnim(string anim) => _debugAnim = anim;

    void DebugAnimate(float dt)
    {
        _debugTimer -= dt;
        bool replay = _debugTimer <= 0f;
        if (replay)
            _debugTimer = 0.9f;
        var right = new Vector3(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        switch (_debugAnim)
        {
            case "run": _rig.Animate(dt, new Vector2(0f, 9f), true, false); return;
            case "back": _rig.Animate(dt, new Vector2(0f, -3.5f), false, false); return;
            case "strafe": _rig.Animate(dt, new Vector2(5f, 0f), false, false); return;
            case "jump": _rig.Animate(dt, Vector2.Zero, false, false, true); return;
            case "roll": if (replay) _rig.PlayRoll(right, 0.42f); break;
            case "swing1": if (replay) _rig.Play(PlayerRig.Act.Swing1, 0.38f); break;
            case "swing2": if (replay) _rig.Play(PlayerRig.Act.Swing2, 0.38f); break;
            case "swing3": if (replay) _rig.Play(PlayerRig.Act.Swing3, 0.38f); break;
            case "stab": if (replay) _rig.Play(PlayerRig.Act.StabL, 0.3f); break;
            case "finisher": if (replay) _rig.Play(PlayerRig.Act.Finisher, 0.6f); break;
        }
        _rig.Animate(dt, Vector2.Zero, false, false);
    }

    static float YawOf(Vector3 dir) => Mathf.Atan2(-dir.X, -dir.Z);

    // The body faces the camera's heading whenever it moves or attacks; standing still it keeps its pose.
    void UpdateFacing(float dt, bool moving)
    {
        if (moving || _aimTimer > 0f || _dashTimer > 0f)
            _yaw = Mathf.LerpAngle(_yaw, YawOf(LookDir), 1f - Mathf.Exp(-TurnRate * dt));
        Facing = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
        _rig.Yaw = _yaw;
    }

    // Snaps the body to the aim direction for an attack.
    Vector3 Aim()
    {
        _aimTimer = 0.45f;
        _combatTimer = Cfg.HpRegenDelay;
        _yaw = YawOf(LookDir);
        Facing = LookDir;
        _rig.Yaw = _yaw;
        return LookDir;
    }

    public void FaceToward(Vector3 point)
    {
        var d = point - GlobalPosition;
        d.Y = 0f;
        if (d.LengthSquared() > 0.01f)
            _yaw = YawOf(d.Normalized());
    }

    bool SpendStamina(float cost)
    {
        if (Immortal)
            return true;
        if (Stamina < cost)
        {
            if (_noStaminaToast <= 0f)
            {
                Game.Toast("ความอึดไม่พอ");
                _noStaminaToast = 2f;
            }
            return false;
        }
        Stamina -= cost;
        _staminaDelay = Cfg.StaminaRegenDelay;
        return true;
    }

    void TryRoll(Vector3 dir)
    {
        if (!SpendStamina(Cfg.DashStaminaCost))
            return;
        StartDash(dir, Cfg.DashDistance, Cfg.DashDuration, true);
        _rig.PlayRoll(dir, Cfg.DashDuration);
        Audio.Play("dash", -2f);
    }

    void StartDash(Vector3 dir, float distance, float duration, bool invulnerable)
    {
        _dashDir = dir;
        _dashTimer = duration;
        _dashSpeed = distance / duration;
        _dashCd = Cfg.DashCooldown;
        if (invulnerable)
            _invuln = Mathf.Max(_invuln, duration + 0.1f);
    }

    void Attack()
    {
        var a = Def.Attack;
        _attackCd = AttackCooldown;
        var dir = Aim();
        if (!HasWeapon)
        {
            // Bare hands: quick alternating punches, short reach.
            _attackCd = Cfg.UnarmedCooldown;
            _leftHand = !_leftHand;
            _rig.Play(_leftHand ? PlayerRig.Act.StabL : PlayerRig.Act.StabR, 0.25f);
            Audio.Play("swing", -6f);
            HitArc(dir, 1.5f, 80f, AttackDamage, "none");
            return;
        }
        if (a.Type == "projectile")
        {
            _rig.Play(PlayerRig.Act.Throw, Mathf.Min(0.3f, _attackCd));
            Audio.Play("throw", -4f);
            Shoot(AimedDir(a.Range), a, AttackDamage);
            return;
        }

        // Melee combo: three swings (random sword cuts, or alternating dagger stabs), then a spinning
        // finisher on the fourth consecutive hit.
        _combo = _comboTimer > 0f ? _combo + 1 : 1;
        _comboTimer = Cfg.ComboWindowSec;
        if (_combo >= 4)
        {
            _combo = 0;
            _comboTimer = 0f;
            _attackCd = AttackCooldown * 1.6f;
            _rig.Play(PlayerRig.Act.Finisher, 0.6f);
            Audio.Play("swing", 1f, 0f);
            Audio.Play("skill", -6f, 0f);
            HitArc(dir, a.Range + 0.8f, 360f, AttackDamage * Cfg.ComboFinisherMult, a.Element);
            Fx.Ring(GetParent(), GlobalPosition, a.Range + 1.2f, new Color(1f, 0.9f, 0.55f), 0.35f);
            return;
        }
        if (Def.Offhand == "dagger")
        {
            _leftHand = !_leftHand;
            _rig.Play(_leftHand ? PlayerRig.Act.StabL : PlayerRig.Act.StabR, Mathf.Min(0.3f, _attackCd));
        }
        else
        {
            var swings = new[] { PlayerRig.Act.Swing1, PlayerRig.Act.Swing2, PlayerRig.Act.Swing3 }.Where(s => s != _lastSwing).ToArray();
            _lastSwing = swings[GD.RandRange(0, swings.Length - 1)];
            _rig.Play(_lastSwing, Mathf.Min(0.38f, _attackCd));
        }
        Audio.Play("swing", -2f);
        HitArc(dir, a.Range, a.Arc, AttackDamage, a.Element);
        Fx.Ring(GetParent(), GlobalPosition + dir * (a.Range * 0.5f), a.Range * 0.6f, new Color(0.85f, 0.9f, 1f), 0.15f);
    }

    // Right click: a fan of talismans (projectile classes) or a lunge (melee classes; the assassin's goes further).
    void Special()
    {
        var s = Def.Special;
        if (s == null)
            return;
        if (!SpendStamina(s.StaminaCost))
            return;
        _specialCd = s.Cooldown;
        var dir = Aim();
        if (s.Type == "fan")
        {
            _rig.Play(PlayerRig.Act.Throw, 0.35f);
            Audio.Play("throw", -1f, 0f);
            float arc = Mathf.DegToRad(s.Arc);
            var aimed = AimedDir(s.Range);
            for (int i = 0; i < s.Count; i++)
            {
                float t = s.Count == 1 ? 0f : i / (float)(s.Count - 1) - 0.5f;
                Shoot(aimed.Rotated(Vector3.Up, t * arc), s, SpecialDamage);
            }
            return;
        }
        _rig.Play(PlayerRig.Act.Thrust, 0.4f);
        Audio.Play("swing", 0f, 0f);
        StartDash(dir, s.Distance, 0.15f + s.Distance * 0.02f, s.Distance > 4f);
        _dashCd = 0f;
        HitArc(dir, s.Range, s.Arc, SpecialDamage, s.Element, 1.2f);
        Fx.Ring(GetParent(), GlobalPosition + dir * s.Distance, 1.6f, new Color(1f, 0.9f, 0.6f), 0.25f);
    }

    Vector3 Muzzle => GlobalPosition + new Vector3(0f, 1.2f, 0f) + Facing * 0.6f;

    // Projectiles fly from the hand to whatever the crosshair points at.
    Vector3 AimedDir(float range)
    {
        var d = Game.Main.AimTarget(Muzzle, range) - Muzzle;
        return d.LengthSquared() > 0.01f ? d.Normalized() : Facing;
    }

    void Shoot(Vector3 dir, AttackDef a, float damage)
    {
        var p = new Projectile();
        GetParent().AddChild(p);
        p.GlobalPosition = Muzzle;
        p.Setup(dir, a.ProjectileSpeed, a.Range, damage, a.Element);
    }

    void HitArc(Vector3 dir, float range, float arcDeg, float damage, string element, float reach = 0f)
    {
        float half = Mathf.DegToRad(arcDeg * 0.5f);
        foreach (var g in Ghost.All.ToArray())
        {
            Vector3 to = g.GlobalPosition - GlobalPosition;
            to.Y = 0f;
            float dist = to.Length();
            if (dist > range + 0.5f + reach)
                continue;
            if (dist > 0.6f && dir.AngleTo(to / dist) > half)
                continue;
            g.TakeDamage(damage, element);
        }
    }

    void UseSkill(int i)
    {
        var s = Def.Skills[i];
        if (_skillCd[i] > 0f)
            return;
        if (!SpendStamina(s.StaminaCost))
            return;
        _skillCd[i] = s.Cooldown;
        _combatTimer = Cfg.HpRegenDelay;
        _rig.Play(PlayerRig.Act.Cast, 0.45f);

        switch (s.Type)
        {
            case "aoe":
                Audio.Play("skill");
                foreach (var g in Ghost.All.ToArray())
                {
                    if (g.GlobalPosition.DistanceTo(GlobalPosition) > s.Radius)
                        continue;
                    g.TakeDamage(s.Damage * SkillMult, s.Element);
                    if (s.Slow > 0f)
                        g.ApplySlow(s.Slow, s.SlowDuration);
                }
                Fx.Ring(GetParent(), GlobalPosition, s.Radius,
                    s.Element == "rice" ? new Color(1f, 1f, 0.92f) : new Color(1f, 0.85f, 0.4f));
                break;
            case "heal":
                Audio.Play("heal");
                Heal(s.Amount * SkillMult);
                Fx.Ring(GetParent(), GlobalPosition, 2.5f, new Color(0.5f, 1f, 0.7f));
                break;
            case "guard":
                Audio.Play("skill", 0f, 0f);
                _guardTimer = s.Duration;
                _guardReduction = s.DamageReduction;
                Fx.Ring(GetParent(), GlobalPosition, 2f, new Color(1f, 0.9f, 0.4f));
                break;
            case "frenzy":
                Audio.Play("skill", 2f, 0f);
                _frenzyTimer = s.Duration;
                Fx.Ring(GetParent(), GlobalPosition, 2.2f, new Color(0.55f, 0.6f, 1f), 0.5f);
                break;
        }
    }

    public void Heal(float amount)
    {
        float before = Hp;
        Hp = Mathf.Min(MaxHp, Hp + amount);
        if (Hp - before >= 1f)
            Fx.DamageNumber(GetParent(), GlobalPosition + Vector3.Up * 2.3f, $"+{Mathf.RoundToInt(Hp - before)}", new Color(0.5f, 1f, 0.6f));
    }

    public void TakeDamage(float amount, string source)
    {
        if (Dead || _invuln > 0f || Immortal)
            return;
        if (Guarding)
            amount *= 1f - _guardReduction;
        // Armour: each point of defense shaves a little off every hit.
        amount *= 100f / (100f + Defense);
        Hp -= amount;
        _invuln = 0.2f;
        _flash = 0.15f;
        _combatTimer = Cfg.HpRegenDelay;
        _rig.Play(PlayerRig.Act.Hurt, 0.25f);
        Audio.Play("hurt", -3f);
        Fx.DamageNumber(GetParent(), GlobalPosition + Vector3.Up * 2.3f, Mathf.RoundToInt(amount).ToString(), new Color(1f, 0.35f, 0.3f));
        if (Hp <= 0f)
            Die(source);
    }

    void Die(string source)
    {
        Hp = 0f;
        Dead = true;
        Velocity = Vector3.Zero;
        _localVel = Vector2.Zero;
        int lost = Mathf.RoundToInt(Exp * Cfg.DeathExpLossRatio);
        Exp -= lost;
        Audio.Play("banish", 0f, 0f);
        Game.Toast($"คุณถูก{source}เล่นงานจนสิ้นสติ (เสีย {lost} EXP)");
        Died?.Invoke();
    }

    public void Respawn(Vector3 position)
    {
        Dead = false;
        Hp = MaxHp;
        Stamina = MaxStamina;
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _invuln = 2f;
    }

    public void Teleport(Vector3 position)
    {
        GlobalPosition = position;
        Velocity = Vector3.Zero;
    }

    public void RestoreAll()
    {
        Hp = MaxHp;
        Stamina = MaxStamina;
    }

    public void GainExp(int amount)
    {
        Exp += amount;
        while (Exp >= GameData.ExpToNext(Level))
        {
            Exp -= GameData.ExpToNext(Level);
            Level++;
            StatPoints += Cfg.StatPointsPerLevel;
            Hp = MaxHp;
            Stamina = MaxStamina;
            Audio.Play("levelup", 0f, 0f);
            Game.Toast($"เลเวลอัป! ได้แต้มสถานะ +{Cfg.StatPointsPerLevel} (กด {Settings.BindingText("stats")})");
            Game.Main.Hud.Banner($"เลเวล {Level}", UiTheme.Gold);
            Fx.Ring(GetParent(), GlobalPosition, 4f, new Color(1f, 0.85f, 0.4f), 0.6f);
        }
        Quests.Refresh(this);
    }

    public void AddStat(int stat)
    {
        if (StatPoints <= 0)
            return;
        StatPoints--;
        Attr[stat]++;
        if (stat == Vit)
            Hp += 12f;
        if (stat == Sta)
            Stamina += 10f;
    }
}
