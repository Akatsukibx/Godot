using Godot;
using System.Collections.Generic;
using System.Linq;

// Root of the prototype: places every open zone (เวียงหมอก, ทุ่งนาหุ่นไล่กา…) side by side in one world so
// they can be walked between, runs the third-person camera, tracks the current zone, spawns ghosts
// per zone, handles NPCs, shrines, cutscenes, weather, saving and graphics settings.
public partial class Main : Node3D
{
    public Player Player { get; private set; }
    public DayNight DayNight { get; private set; }
    public Hud Hud { get; private set; }
    public Menus Menus { get; private set; }
    public Cutscene Cutscene { get; private set; }
    public Weather Weather { get; private set; }
    public ZoneDef CurrentZone { get; private set; }
    public IReadOnlyList<Npc> Npcs => _npcs;

    const float MinPitch = -1.15f, MaxPitch = 0.3f;
    const float NpcTalkRange = 3f, ShrineRange = 5.5f;
    static bool _devArgsUsed;

    Node3D _camPivot;
    SpringArm3D _arm;
    Camera3D _cam;
    DirectionalLight3D _sun;
    Godot.Environment _env;
    Label3D _prompt;
    float _camYaw, _camPitch = -0.32f, _armLength = 5.5f, _spawnTimer = 3f, _menuOrbit;
    readonly RandomNumberGenerator _rng = new();
    readonly List<Npc> _npcs = new();
    readonly Dictionary<string, Node3D> _shrines = new();
    readonly Dictionary<string, Node3D> _busStops = new();
    readonly List<Node3D> _grass = new();
    float _slowTick;
    readonly List<(Rect2 rect, float surface)> _water = new();
    readonly HashSet<string> _seenCutscenes = new();
    Node3D _interactTarget;

    static GameConfig Cfg => GameData.Config;

    // Camera heading only (no pitch): movement and aiming are relative to it.
    public Basis CameraYawBasis => new(Vector3.Up, _camYaw);
    public Vector3 CameraForward => new(-Mathf.Sin(_camYaw), 0f, -Mathf.Cos(_camYaw));
    public bool MouseCaptured => Input.MouseMode == Input.MouseModeEnum.Captured;
    // Gameplay input is live only while the cursor is captured (no window, menu, cutscene or held Alt).
    public bool ControlsEnabled => Hud.Playing && !Hud.WindowOpen && MouseCaptured && !Cutscene.Active;

    public override void _Ready()
    {
        Game.Main = this;
        GameData.Load();
        Game.ThaiFont ??= Game.MakeThaiFont();
        Settings.InitInput();
        Settings.Load();
        _rng.Randomize();

        if (OS.GetCmdlineUserArgs().Contains("--bake"))
        {
            SceneBaker.BakeAll();
            SetProcess(false);
            SetProcessUnhandledInput(false);
            GetTree().Quit();
            return;
        }

        AddChild(new Audio());
        BuildEnvironment();
        BuildWorld();
        BuildCamera();
        ApplyBrightness();
        ApplyGraphics();

        Hud = new Hud();
        AddChild(Hud);
        Menus = new Menus();
        AddChild(Menus);
        Cutscene = new Cutscene();
        AddChild(Cutscene);
        Weather = new Weather();
        AddChild(Weather);
        _prompt = new Label3D
        {
            Font = Game.ThaiFont,
            FontSize = 44,
            OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.005f,
            Modulate = new Color(1f, 0.95f, 0.75f),
            Visible = false,
        };
        AddChild(_prompt);
        Audio.PlayMusic(DayNight.IsNight ? "amb_night" : "amb_day");

        DayNight.SetHour(21.5f); // the menus show the city at night
        if (!HandleDevArgs())
            Menus.ShowMainMenu();
    }

    void BuildWorld()
    {
        AddChild(Models.Outskirts());
        foreach (var zone in GameData.Zones.Where(z => z.Scene != null))
        {
            var scene = Models.Zone(zone.Scene);
            scene.Position = zone.Center;
            AddChild(scene);
        }
        Textures.Apply(this);
        WorldBuilder.ApplyDistanceCulling(this);
        AddChild(WorldBuilder.BuildRice(GetTree().GetNodesInGroup("map_plot").OfType<Node3D>()));
        AddChild(WorldBuilder.BuildForest(WorldBuilder.WorldBounds()));
        AddChild(WorldBuilder.BuildLilies(GetTree().GetNodesInGroup("lily_field").OfType<Node3D>()));
        foreach (var shrine in GetTree().GetNodesInGroup("shrine").OfType<Node3D>())
            _shrines[(string)shrine.GetMeta("shrine_id")] = shrine;
        foreach (var stop in GetTree().GetNodesInGroup("bus_stop").OfType<Node3D>())
            _busStops[(string)stop.GetMeta("stop_id")] = stop;
        foreach (var s in GetTree().GetNodesInGroup("sanctuary").OfType<Node3D>())
            _sanctuaries.Add((s.GlobalPosition, (float)s.GetMeta("radius", 25f)));
        foreach (var w in GetTree().GetNodesInGroup("water_area").OfType<Node3D>())
        {
            var size = (Vector2)w.GetMeta("map_size", Vector2.One);
            var p = w.GlobalPosition;
            _water.Add((new Rect2(p.X - size.X * 0.5f, p.Z - size.Y * 0.5f, size.X, size.Y), p.Y));
        }
        foreach (var def in GameData.Npcs.Values)
        {
            var zone = GameData.Zone(def.Zone);
            if (zone == null)
                continue;
            var npc = new Npc();
            AddChild(npc);
            npc.Init(def);
            npc.GlobalPosition = zone.Center + new Vector3(def.X, zone.Scene == "bueng_bua" ? Mathf.Max(0f, WorldBuilder.BuengHeight(def.X, def.Z)) : 0f, def.Z);
            _npcs.Add(npc);
        }
    }

    // Grass density follows the graphics preset; rebuilt when it changes.
    void RebuildGrass()
    {
        foreach (var g in _grass)
            g.QueueFree();
        _grass.Clear();
        float density = Settings.Graphics switch { 0 => 0.15f, 1 => 0.45f, _ => 0.9f };
        var water = _water.Count > 0 ? _water[0].rect : new Rect2();
        foreach (var zone in GameData.Zones.Where(z => z.Scene != null))
        {
            var g = WorldBuilder.BuildGrass(zone, GetTree(), density, water);
            AddChild(g);
            _grass.Add(g);
        }
    }

    public void ApplyBrightness()
    {
        _env.AdjustmentEnabled = true;
        _env.AdjustmentBrightness = Settings.Brightness;
    }

    // Low / medium / high presets.
    public void ApplyGraphics()
    {
        int q = Mathf.Clamp(Settings.Graphics, 0, 2);
        _sun.ShadowEnabled = q >= 1;
        _sun.DirectionalShadowMode = q == 2 ? DirectionalLight3D.ShadowMode.Parallel4Splits : DirectionalLight3D.ShadowMode.Parallel2Splits;
        _sun.DirectionalShadowMaxDistance = q == 2 ? 90f : 45f;
        _env.GlowEnabled = q >= 1;
        _env.SsaoEnabled = q == 2;
        _env.SsrEnabled = q == 2;
        _env.VolumetricFogEnabled = q == 2;
        var vp = GetViewport();
        vp.Msaa3D = q switch { 0 => Viewport.Msaa.Disabled, 1 => Viewport.Msaa.Msaa2X, _ => Viewport.Msaa.Msaa4X };
        vp.Scaling3DScale = q == 0 ? 0.75f : 1f;
        RenderingServer.DirectionalShadowAtlasSetSize(q == 2 ? 4096 : 2048, true);
        if (_grass.Count > 0 || _water.Count > 0)
            RebuildGrass();
        foreach (var ff in GetTree().GetNodesInGroup("fireflies").OfType<GpuParticles3D>())
            ff.Amount = q == 0 ? 30 : 70;
    }

    public void OpenClassSelect() => Hud.ShowClassSelect(GameData.Classes, def => Hud.AskName(def, name => StartGame(def, null, name)));

    public void ContinueGame()
    {
        var save = SaveGame.Load();
        var def = GameData.Classes.FirstOrDefault(c => c.Id == save?.ClassId);
        if (def == null)
        {
            OpenClassSelect();
            return;
        }
        StartGame(def, save);
    }

    // Dev-only command-line options (after `--`); returns true when they replace the main menu.
    //   --bake                 write models/zones/NPCs to res://models/*.tscn and quit
    //   --class=<index>        skip menus and class select (new game, opening cutscene skipped)
    //   --continue             load the save
    //   --cutscene=<id>        play a cutscene right away
    //   --hour=<h>             set the clock (e.g. 21.5)
    //   --graphics=<0|1|2>     graphics preset for this run
    //   --rain=<0|1|2>         rain mode (random / on / off)
    //   --pos=<x>,<z>          place the player (world coordinates)
    //   --yaw=<deg> --pitch=<deg>   aim the camera
    //   --level=<n>            set the player's level
    //   --give=<id>:<n>,...    add items
    //   --open=<bag|stats|quests|map|craft|settings|graphics|keys|test|pause|class|talk>   open a screen
    //   --selftest             run banish/release/craft/quests without input
    //   --screenshot=<path>    save a screenshot after --shot-delay seconds (default 4) and quit
    bool HandleDevArgs()
    {
        if (_devArgsUsed)
            return false;
        _devArgsUsed = true;
        var args = OS.GetCmdlineUserArgs()
            .Select(a => a.Split('=', 2))
            .ToDictionary(p => p[0].TrimStart('-'), p => p.Length > 1 ? p[1] : "");
        if (args.Count == 0)
            return false;
        float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        if (args.TryGetValue("graphics", out var gq))
        {
            Settings.Graphics = int.Parse(gq);
            ApplyGraphics();
        }
        if (args.TryGetValue("rain", out var rain))
            Settings.RainMode = int.Parse(rain);
        if (args.TryGetValue("class", out var cls))
        {
            _seenCutscenes.Add("opening");
            OpenClassSelect();
            Hud.ChooseClass(GameData.Classes[int.Parse(cls)]);
            Hud.ConfirmName(args.TryGetValue("name", out var nm) ? nm : "");
        }
        if (args.ContainsKey("continue"))
            ContinueGame();
        if (args.TryGetValue("hour", out var hour))
            DayNight.SetHour(F(hour));
        if (args.TryGetValue("yaw", out var yaw))
            _camYaw = Mathf.DegToRad(F(yaw));
        if (args.TryGetValue("pitch", out var pitch))
            _camPitch = Mathf.DegToRad(F(pitch));
        if (Player != null && args.TryGetValue("pos", out var pos))
        {
            var xz = pos.Split(',');
            Player.GlobalPosition = new Vector3(F(xz[0]), 0.3f, F(xz[1]));
            _camPivot.GlobalPosition = Player.GlobalPosition;
        }
        if (Player != null && args.TryGetValue("level", out var lv))
            for (int i = 1; i < int.Parse(lv); i++)
                Player.GainExp(GameData.ExpToNext(Player.Level));
        if (Player != null && args.TryGetValue("give", out var give))
            foreach (var pair in give.Split(','))
            {
                var kv = pair.Split(':');
                Player.AddItem(kv[0], kv.Length > 1 ? int.Parse(kv[1]) : 1);
            }
        if (Player != null && args.ContainsKey("equip"))
            for (int i = 0; i < Player.BagSize; i++)
                if (Player.Bag[i]?.Def.Equip != null && Player.CanEquip(Player.Bag[i].Def))
                    Player.Equip(i);
        if (args.TryGetValue("cutscene", out var cut))
            GetTree().CreateTimer(0.5).Timeout += () => Cutscene.Play(cut, null);
        if (Player != null && args.TryGetValue("anim", out var anim))
            Player.DebugAnim(anim);
        if (Player != null && args.ContainsKey("selftest"))
            GetTree().CreateTimer(1.5).Timeout += RunSelfTest;
        if (args.TryGetValue("open", out var screen))
        {
            GetTree().CreateTimer(1.0).Timeout += () =>
            {
                switch (screen)
                {
                    case "settings": Menus.ShowSettings(() => { }); break;
                    case "graphics": Menus.ShowSettings(() => { }, 1); break;
                    case "keys": Menus.ShowSettings(() => { }, 3); break;
                    case "test": Menus.ShowSettings(() => { }, 4); break;
                    case "pause": Input.ParseInputEvent(new InputEventAction { Action = "pause", Pressed = true }); break;
                    case "class": OpenClassSelect(); break;
                    case "talk": Hud.ShowDialogue(_npcs.OrderBy(n => n.GlobalPosition.DistanceTo(Player.GlobalPosition)).First()); break;
                    default: Hud.OpenWindow(screen); break;
                }
            };
        }
        if (args.TryGetValue("screenshot", out var path))
        {
            float wait = args.TryGetValue("shot-delay", out var d) ? F(d) : 4f;
            GetTree().CreateTimer(wait, true).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng(path);
                if (Player != null)
                    GD.Print($"[shot] zone={CurrentZone?.Id} pos={Player.GlobalPosition.X:0},{Player.GlobalPosition.Z:0} hp={Player.Hp:0}/{Player.MaxHp} " +
                        $"st={Player.Stamina:0} water={Player.WaterDepth:0.00} ghosts={Ghost.All.Count(g => !g.Gone)} rain={Weather.Intensity:0.0} " +
                        $"items={string.Join(",", Player.Bag.Where(s => s != null).Select(s => $"{s.Id}={s.Count}"))}");
                GetTree().Quit();
            };
        }
        return Player != null || args.ContainsKey("open") && args["open"] == "class" || args.ContainsKey("cutscene");
    }

    // Exercises the main systems without input; results go to stdout.
    void RunSelfTest()
    {
        var log = Player.Quests;
        var yai = _npcs.First(n => n.Def.Id == "yai_jan");
        log.Notify(Player, "talk", "yai_jan");
        var m1 = GameData.Quests.First(q => q.Id == "m1_curtain");
        GD.Print($"[selftest] m1={log.StateOf(m1, Player)}");
        log.TurnIn(m1, Player);
        var m2 = GameData.Quests.First(q => q.Id == "m2_first_talisman");
        log.Accept(m2, Player);

        var def = GameData.Ghosts["winyan_long_soi"];
        for (int i = 0; i < 2; i++)
        {
            var g = new Ghost { Name = "test" };
            AddChild(g);
            g.Init(def, 2);
            g.Zone = "wiang_mok";
            g.GlobalPosition = Player.GlobalPosition + new Vector3(2f + i, 0f, 0f);
            g.Home = g.GlobalPosition;
            g.TakeDamage(9999f, "fire");
            g.Resolve(i == 0, Player);
        }
        Player.TakeDamage(10f, "selftest");
        Player.AddItem("fang_sap", 2);
        bool crafted = Player.Craft(GameData.Recipes.First(r => r.Result == "ya_hom"));
        BindShrine(_shrines["lak_mueang"]);
        GD.Print($"[selftest] lv={Player.Level} exp={Player.Exp} hp={Player.Hp:0} banished={Player.Banished} released={Player.Released} " +
            $"crafted={crafted} m2={log.StateOf(m2, Player)} {log.Progress(m2, 0, Player)}/3 spawn={Player.SpawnShrine} save={SaveGame.Exists} " +
            $"water(lagoon)={WaterDepthAt(new Vector3(3f, 0f, -372f - 112f)):0.00} water(deep)={WaterDepthAt(new Vector3(-60f, 0f, -372f - 150f)):0.00} " +
            $"yai={yai.GlobalPosition}");
    }

    void StartGame(ClassDef def, SaveData save, string name = null)
    {
        // Set the clock before the player exists so leaving the night-time menu does not announce dawn.
        DayNight.SetHour(save?.Hour ?? Cfg.StartHour);
        Player = new Player { Name = "Player" };
        AddChild(Player);
        Player.Init(def);
        if (name != null)
            Player.SetName(name);
        if (save != null)
        {
            Player.LoadSave(save);
            foreach (var c in save.SeenCutscenes ?? new())
                _seenCutscenes.Add(c);
        }
        Player.GlobalPosition = SpawnPosition();
        Player.Died += () => Hud.ShowDeath(RespawnPlayer);
        Player.Quests.Completed += OnQuestCompleted;
        Player.Quests.Accepted += _ => NotifyZone();
        Hud.BindPlayer(Player);
        _camYaw = 0f;
        _camPitch = -0.32f;
        _armLength = 5.5f;
        _camPivot.GlobalPosition = Player.GlobalPosition + new Vector3(0f, 1.55f, 0f);
        Player.Quests.Refresh(Player);
        UpdateZone(true);

        if (save == null)
            PlayCutscene("opening", () =>
            {
                Hud.ZoneTitle(CurrentZone);
                Game.Toast($"เดินไปคุยกับยายจันทร์ที่ศาลหลักเมือง (กด {Settings.BindingText("interact")} เมื่ออยู่ใกล้)", "quest", 7f);
            });
        else
            Game.Toast("โหลดเกมจากศาลที่ไหว้ไว้ล่าสุด", "quest");
    }

    public void PlayCutscene(string id, System.Action then = null)
    {
        if (_seenCutscenes.Contains(id) && id == "opening")
        {
            then?.Invoke();
            return;
        }
        _seenCutscenes.Add(id);
        Cutscene.Play(id, then);
    }

    void OnQuestCompleted(QuestDef q)
    {
        SaveNow();
        if (q.CutsceneOnComplete != null)
            GetTree().CreateTimer(0.4).Timeout += () => { Hud.CloseWindows(); PlayCutscene(q.CutsceneOnComplete); };
    }

    public void SaveNow()
    {
        if (Player == null)
            return;
        var data = Player.ToSave();
        data.Hour = DayNight.Hour;
        data.SeenCutscenes = _seenCutscenes.ToList();
        SaveGame.Save(data);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            SaveNow();
    }

    Vector3 SpawnPosition()
    {
        var shrine = _shrines.GetValueOrDefault(Player?.SpawnShrine ?? "lak_mueang") ?? _shrines.GetValueOrDefault("lak_mueang");
        return shrine != null ? shrine.GlobalPosition + new Vector3(0f, 0.3f, 5f) : new Vector3(0f, 0.3f, 9f);
    }

    void RespawnPlayer()
    {
        var at = SpawnPosition();
        foreach (var g in Ghost.All.ToArray())
            if (!g.Gone && g.GlobalPosition.DistanceTo(at) < 25f)
                g.Vanish();
        Player.Respawn(at);
        Hud.Banner($"ฟื้นคืนสติที่{_shrines.GetValueOrDefault(Player.SpawnShrine)?.GetMeta("shrine_name") ?? "ศาล"}", new Color(0.9f, 0.9f, 1f));
    }

    // ---------- zones ----------

    void UpdateZone(bool silent = false)
    {
        var zone = GameData.ZoneAt(Player.GlobalPosition);
        if (zone == null || zone == CurrentZone)
            return;
        CurrentZone = zone;
        NotifyZone();
        if (!silent)
        {
            Hud.ZoneTitle(zone);
            Weather.Roll();
        }
    }

    void NotifyZone()
    {
        if (CurrentZone != null && Player != null)
            Player.Quests.Notify(Player, "reach", CurrentZone.Id);
    }

    // Depth of water at a point (water surface minus the ground under it), 0 on land or on a pier.
    public float WaterDepthAt(Vector3 p)
    {
        foreach (var (rect, surface) in _water)
        {
            if (!rect.HasPoint(new Vector2(p.X, p.Z)))
                continue;
            var query = PhysicsRayQueryParameters3D.Create(new Vector3(p.X, surface + 2.5f, p.Z), new Vector3(p.X, surface - 5f, p.Z), Layers.World);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0)
                return 0f;
            return Mathf.Max(0f, surface - ((Vector3)hit["position"]).Y);
        }
        return 0f;
    }

    // Water surface height at a point, or null on dry land.
    public float? WaterSurfaceAt(Vector3 p)
    {
        foreach (var (rect, surface) in _water)
            if (rect.HasPoint(new Vector2(p.X, p.Z)))
                return surface;
        return null;
    }

    // Where a shot from `muzzle` should go: a ghost close to the crosshair (light aim assist), else
    // whatever the crosshair ray hits, else far along it.
    public Vector3 AimTarget(Vector3 muzzle, float range)
    {
        var origin = _cam.GlobalPosition;
        var ray = -_cam.GlobalBasis.Z;
        Ghost best = null;
        float bestAngle = Mathf.DegToRad(7f);
        foreach (var g in Ghost.All)
        {
            if (g.Gone || g.Weakened)
                continue;
            var chest = g.GlobalPosition + Vector3.Up * 1.1f;
            if (chest.DistanceTo(muzzle) > range + 2f)
                continue;
            float angle = ray.AngleTo(chest - origin);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = g;
            }
        }
        if (best != null)
            return best.GlobalPosition + Vector3.Up * 1.1f;
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + ray * (range + 20f), Layers.World | Layers.Ghost);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        var point = hit.Count > 0 ? (Vector3)hit["position"] : origin + ray * (range + 20f);
        // Never shoot backwards at something between the camera and the player.
        var flat = new Vector3(ray.X, 0f, ray.Z).Normalized();
        if ((point - muzzle).Dot(flat) < 1f)
            point = muzzle + flat * range;
        return point;
    }

    // ---------- interaction ----------

    void UpdateInteraction()
    {
        _interactTarget = null;
        if (Player == null || Player.Dead || Cutscene.Active || Hud.WindowOpen)
        {
            _prompt.Visible = false;
            return;
        }
        float best = float.MaxValue;
        string text = null;
        foreach (var npc in _npcs)
        {
            float d = npc.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (d < NpcTalkRange && d < best)
            {
                best = d;
                _interactTarget = npc;
                text = $"[{Settings.BindingText("interact")}] คุยกับ{npc.Def.Name}";
            }
        }
        foreach (var shrine in _shrines.Values)
        {
            float d = shrine.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (d < ShrineRange && d < best)
            {
                best = d;
                _interactTarget = shrine;
                bool bound = Player.SpawnShrine == (string)shrine.GetMeta("shrine_id");
                text = $"[{Settings.BindingText("interact")}] ไหว้{shrine.GetMeta("shrine_name")}" + (bound ? " (จุดเกิดปัจจุบัน)" : " · ตั้งจุดเกิด");
            }
        }
        foreach (var stop in _busStops.Values)
        {
            float d = stop.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (d < 3.5f && d < best)
            {
                best = d;
                _interactTarget = stop;
                text = $"[{Settings.BindingText("interact")}] ขึ้นรถเมล์สายสุดท้าย";
            }
        }
        _prompt.Visible = _interactTarget != null;
        if (_interactTarget != null)
        {
            _prompt.Text = text;
            _prompt.GlobalPosition = _interactTarget.GlobalPosition + new Vector3(0f, _interactTarget is Npc ? 1.2f : 2.6f, 0f);
            if (ControlsEnabled && Input.IsActionJustPressed("interact"))
                Interact(_interactTarget);
        }
    }

    void Interact(Node3D target)
    {
        if (target is Npc npc)
        {
            Player.FaceToward(npc.GlobalPosition);
            Hud.ShowDialogue(npc);
            return;
        }
        if (target.IsInGroup("bus_stop"))
            ShowBusMenu((string)target.GetMeta("stop_id"));
        else
            BindShrine(target);
    }

    void BindShrine(Node3D shrine)
    {
        Player.SpawnShrine = (string)shrine.GetMeta("shrine_id");
        Player.RestoreAll();
        Player.Quests.Notify(Player, "bind", Player.SpawnShrine);
        Audio.Play("release", -2f, 0f);
        Fx.Ring(this, shrine.GlobalPosition, 4f, new Color(1f, 0.85f, 0.5f), 0.9f);
        Game.Toast($"ตั้งจุดเกิดที่{shrine.GetMeta("shrine_name")} · ฟื้นฟูชีวิตเต็ม · บันทึกเกมแล้ว", "sun", 4f);
        SaveNow();
    }


    // ---------- fast travel (รถเมล์สายสุดท้าย) and admin warp ----------

    void DiscoverBusStops()
    {
        foreach (var (id, stop) in _busStops)
            if (!Player.BusStops.Contains(id) && stop.GlobalPosition.DistanceTo(Player.GlobalPosition) < 18f)
            {
                Player.BusStops.Add(id);
                Game.Toast($"พบป้ายรถเมล์: {stop.GetMeta("stop_name")} · ใช้เดินทางเร็วได้แล้ว", "map");
            }
    }

    public void ShowBusMenu(string fromId)
    {
        var options = _busStops
            .Where(kv => kv.Key != fromId && Player.BusStops.Contains(kv.Key))
            .Select(kv => ($"ไป{kv.Value.GetMeta("stop_name")}", (System.Action)(() => TravelTo(kv.Value))))
            .ToList();
        string text = options.Count > 0
            ? "รถเมล์สายสุดท้ายจอดรออยู่ในหมอก... จะไปป้ายไหน"
            : "ยังไม่รู้จักป้ายอื่นเลย ต้องเดินไปเจอป้ายรถเมล์ก่อนถึงจะนั่งไปได้";
        Hud.ShowMenu("รถเมล์สายสุดท้าย", text, options);
    }

    void TravelTo(Node3D stop)
    {
        var target = stop.GlobalPosition + stop.GlobalBasis * new Vector3(0f, 0.3f, -2.5f);
        Hud.FadeTravel("รถเมล์สายสุดท้ายแล่นผ่านหมอก...", () =>
        {
            Player.Teleport(target);
            _camPivot.GlobalPosition = target + new Vector3(0f, 1.55f, 0f);
            UpdateZone();
        });
    }

    public IEnumerable<(string name, Vector3 pos)> WarpPoints()
    {
        foreach (var (id, s) in _shrines)
            yield return ((string)s.GetMeta("shrine_name"), s.GlobalPosition + new Vector3(0f, 0.3f, 5f));
        foreach (var npc in _npcs)
            yield return (npc.Def.Name, npc.GlobalPosition + new Vector3(0f, 0.3f, 2f));
        yield return ("ประตูเมืองทิศเหนือ", new Vector3(0f, 0.3f, -170f));
        yield return ("หมู่บ้านกลางน้ำ", new Vector3(0f, 0.8f, -372f + WorldBuilder.ThungNaShoreZ - 11f));
        yield return ("ซอยมืด", new Vector3(-31f, 0.3f, 31f));
        yield return ("ป่าช้าเก่า", new Vector3(93f, 0.3f, -153f));
        foreach (var zone in GameData.Zones.Where(z => z.Boss != null && z.Scene != null))
            yield return ($"บอส {GameData.GhostName(zone.Boss.Ghost)}", zone.Center + new Vector3(zone.Boss.X + 12f, 0.5f, zone.Boss.Z + 12f));
        var bueng = new Vector3(0f, 0f, -744f);
        yield return ("บึงบัวสาย: ทางเข้า", bueng + new Vector3(-6f, 0.6f, 174f));
        yield return ("บึงบัวสาย: หมู่บ้านชาวบึง", bueng + new Vector3(-95f, 0.8f, 60f));
        yield return ("บึงบัวสาย: ศาลาร้าง", bueng + new Vector3(70f, 0.8f, 30f));
        yield return ("บึงบัวสาย: ป่าไม้ตาย", bueng + new Vector3(-110f, 0.8f, -100f));
    }

    // Admin warp for testing (Settings > ทดสอบ, or clicking the map while admin mode is on).
    public void AdminWarp(Vector3 pos)
    {
        if (Player == null)
            return;
        var from = new Vector3(pos.X, 60f, pos.Z);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 120f, Layers.World));
        if (hit.Count > 0)
            pos.Y = ((Vector3)hit["position"]).Y + 0.3f;
        Player.Teleport(pos);
        _camPivot.GlobalPosition = pos + new Vector3(0f, 1.55f, 0f);
        UpdateZone();
        Game.Toast("วาปแล้ว (แอดมิน)", "map", 2f);
    }

    // ---------- quest navigation ----------

    public QuestNav.Target? Navigation { get; private set; }
    Label3D _navMarker;
    MultiMeshInstance3D _guide;
    public bool GuideLine { get; set; }

    void UpdateNavigation()
    {
        Navigation = QuestNav.Find(Player, this);
        if (_navMarker == null)
        {
            _navMarker = new Label3D
            {
                Text = "▼",
                FontSize = 160,
                OutlineSize = 20,
                Modulate = UiTheme.Gold,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FixedSize = true,
                PixelSize = 0.0004f,
            };
            AddChild(_navMarker);
        }
        _navMarker.Visible = Navigation != null && !Cutscene.Active;
        if (Navigation is QuestNav.Target t)
            _navMarker.GlobalPosition = t.Position + Vector3.Up * 3f;
        UpdateGuide();
    }

    // A dotted line of light from the player toward the target (toggle with the navigate key).
    void UpdateGuide()
    {
        if (_guide == null)
        {
            _guide = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 6, Rings = 3 } },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.85f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.8f, 0.3f) },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_guide);
        }
        var mm = _guide.Multimesh;
        if (!GuideLine || Navigation is not QuestNav.Target t)
        {
            mm.InstanceCount = 0;
            return;
        }
        var from = Player.GlobalPosition;
        var to = t.Position;
        var dir = (to - from) with { Y = 0f };
        float len = Mathf.Min(dir.Length(), 45f);
        int count = Mathf.Max(0, (int)(len / 1.5f));
        mm.InstanceCount = count;
        dir = dir.Normalized();
        for (int i = 0; i < count; i++)
        {
            var p = from + dir * (1.5f + i * 1.5f);
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, p with { Y = from.Y + 0.25f }));
        }
    }
    // ---------- camera ----------

    void BuildCamera()
    {
        _camPivot = new Node3D { Name = "CameraPivot" };
        AddChild(_camPivot);
        _arm = new SpringArm3D
        {
            SpringLength = _armLength,
            CollisionMask = Layers.World,
            Shape = new SphereShape3D { Radius = 0.3f },
            Position = new Vector3(0.55f, 0f, 0f),
        };
        _camPivot.AddChild(_arm);
        _cam = new Camera3D { Fov = 62f, Far = 700f };
        _arm.AddChild(_cam);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Player == null || !MouseCaptured || Cutscene.Active)
            return;
        if (e is InputEventMouseMotion motion)
        {
            float k = Settings.MouseRadiansPerPixel;
            _camYaw -= motion.Relative.X * k;
            _camPitch = Mathf.Clamp(_camPitch - motion.Relative.Y * k * (Settings.InvertY ? -1f : 1f), MinPitch, MaxPitch);
        }
        else if (e is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp)
                _armLength = Mathf.Max(2.5f, _armLength - 0.5f);
            else if (wheel.ButtonIndex == MouseButton.WheelDown)
                _armLength = Mathf.Min(10f, _armLength + 0.5f);
        }
    }

    void UpdateCamera(float dt)
    {
        if (Player == null)
        {
            // Slow drift down the lantern street behind the menus.
            _menuOrbit += dt * 0.03f;
            _camPivot.GlobalPosition = new Vector3(0f, 3f, 34f);
            _camPivot.Rotation = new Vector3(-0.06f, Mathf.Sin(_menuOrbit) * 0.35f, 0f);
            _arm.SpringLength = 6f;
            _arm.Position = Vector3.Zero;
            return;
        }
        var target = Player.GlobalPosition + new Vector3(0f, 1.55f, 0f);
        _camPivot.GlobalPosition = _camPivot.GlobalPosition.DistanceTo(target) > 8f
            ? target
            : _camPivot.GlobalPosition.Lerp(target, 1f - Mathf.Exp(-14f * dt));
        _camPivot.Rotation = new Vector3(_camPitch, _camYaw, 0f);
        _arm.Position = new Vector3(0.55f, 0f, 0f);
        _arm.SpringLength = Mathf.Lerp(_arm.SpringLength, _armLength, 1f - Mathf.Exp(-10f * dt));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateCamera(dt);
        foreach (var ff in GetTree().GetNodesInGroup("fireflies").OfType<GpuParticles3D>())
            ff.Emitting = DayNight.NightFactor > 0.5f && Weather.Intensity < 0.5f;
        if (Player == null)
            return;

        WorldBuilder.GrassMaterial.SetShaderParameter("player_pos", Player.GlobalPosition);
        _slowTick -= dt;
        if (_slowTick <= 0f)
        {
            _slowTick = 0.3f;
            DiscoverBusStops();
            UpdateBosses();
            UpdateNavigation();
        }
        UpdateZone();
        UpdateInteraction();
        UpdateSpawning(dt);
    }

    // ---------- ghosts ----------

    void UpdateSpawning(float dt)
    {
        // Ghosts far from the player fade away so each zone only keeps the ones nearby.
        foreach (var g in Ghost.All.ToArray())
            if (!g.Gone && !g.Weakened && g.GlobalPosition.DistanceTo(Player.GlobalPosition) > (g.Def.Boss ? 130f : 85f))
                g.Vanish();

        _spawnTimer -= dt;
        if (_spawnTimer > 0f || Player.Dead || CurrentZone == null || Cutscene.Active)
            return;
        bool night = DayNight.IsNight;
        _spawnTimer = night ? Cfg.SpawnIntervalNight : Cfg.SpawnIntervalDay;
        var zone = CurrentZone;
        int alive = Ghost.All.Count(g => !g.Gone && g.Zone == zone.Id);
        if (zone.Ghosts.Count == 0 || alive >= (night ? zone.MaxGhostsNight : zone.MaxGhostsDay))
            return;
        var def = PickGhost(zone, night);
        if (def != null)
            SpawnGhost(zone, def);
    }

    GhostDef PickGhost(ZoneDef zone, bool night)
    {
        var pool = zone.Ghosts.Select(id => GameData.Ghosts.GetValueOrDefault(id))
            .Where(g => g != null)
            .Select(g => (def: g, w: night ? g.SpawnWeightNight : g.SpawnWeightDay))
            .Where(x => x.w > 0f)
            .ToList();
        if (pool.Count == 0)
            return null;
        float roll = _rng.Randf() * pool.Sum(x => x.w);
        foreach (var (def, w) in pool)
        {
            roll -= w;
            if (roll <= 0f)
                return def;
        }
        return pool.Last().def;
    }

    void SpawnGhost(ZoneDef zone, GhostDef def, Vector3? at = null)
    {
        Vector3? found = at;
        if (found == null && zone.AlleyOnly)
        {
            // Town ghosts appear in dark alleys spread across the city, away from the central plaza
            // and never right next to the player.
            var spots = GetTree().GetNodesInGroup("alley_spawn").OfType<Node3D>()
                .Where(s => zone.Contains(s.GlobalPosition))
                .Select(s => s.GlobalPosition)
                .Where(p => p.DistanceTo(Player.GlobalPosition) is > 12f and < 80f)
                .ToList();
            var far = spots.Where(p => p.DistanceTo(zone.Center) > 45f).ToList();
            if (far.Count > 0)
                spots = far;
            if (spots.Count > 0)
                found = spots[_rng.RandiRange(0, spots.Count - 1)];
        }
        else if (found == null)
        {
            var plots = GetTree().GetNodesInGroup("map_plot").OfType<Node3D>().Where(n => zone.Contains(n.GlobalPosition)).ToList();
            for (int attempt = 0; attempt < 20 && found == null; attempt++)
            {
                Vector3 pos;
                // Scarecrows stand in paddies; everything else appears around the player.
                if (def.Behavior == "statue" && plots.Count > 0)
                    pos = plots[_rng.RandiRange(0, plots.Count - 1)].GlobalPosition + new Vector3(_rng.RandfRange(-4f, 4f), 0f, _rng.RandfRange(-4f, 4f));
                else
                {
                    float angle = _rng.Randf() * Mathf.Tau;
                    pos = Player.GlobalPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _rng.RandfRange(16f, 26f);
                }
                float depth = WaterDepthAt(pos);
                bool terrainOk = def.Aquatic ? depth > 0.9f : def.Flying || depth <= 0f;
                bool ok = zone.Contains(pos) && terrainOk && !InSanctuary(pos)
                    && pos.DistanceTo(zone.Center) > Cfg.SafeZoneRadius + 7f
                    && pos.DistanceTo(Player.GlobalPosition) > 10f
                    && Mathf.Abs(pos.X - zone.CenterX) < zone.SizeX * 0.5f - 3f;
                if (ok)
                    found = def.Aquatic && WaterSurfaceAt(pos) is float s ? pos with { Y = s - 0.85f } : pos with { Y = GroundAt(pos) };
            }
        }
        if (found is not Vector3 spot)
            return;

        var ghost = new Ghost { Name = def.Id };
        AddChild(ghost);
        ghost.Init(def, def.Boss ? zone.LevelMax + 2 : GhostLevel(zone, spot));
        ghost.Zone = zone.Id;
        ghost.GlobalPosition = spot + Vector3.Up * 0.2f;
        ghost.Home = spot;
    }

    // Height of the ground (or a pier) under a point.
    public float GroundAt(Vector3 p)
    {
        var query = PhysicsRayQueryParameters3D.Create(new Vector3(p.X, p.Y + 6f, p.Z), new Vector3(p.X, p.Y - 8f, p.Z), Layers.World);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? ((Vector3)hit["position"]).Y : p.Y;
    }

    // ---------- villages (no ghosts) and zone bosses ----------

    readonly List<(Vector3 c, float r)> _sanctuaries = new();
    readonly Dictionary<string, double> _bossRespawnAt = new();

    public bool InSanctuary(Vector3 p) => _sanctuaries.Any(s => new Vector2(p.X - s.c.X, p.Z - s.c.Z).Length() < s.r);

    // Each zone's boss waits at its lair; it appears when the player comes near and returns some
    // minutes after being banished or released.
    void UpdateBosses()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var zone in GameData.Zones.Where(z => z.Boss != null && z.Scene != null))
        {
            var lair = zone.Center + new Vector3(zone.Boss.X, 0f, zone.Boss.Z);
            bool alive = Ghost.All.Any(g => !g.Gone && g.Def.Id == zone.Boss.Ghost);
            if (alive)
            {
                _bossRespawnAt[zone.Id] = -1;
                continue;
            }
            if (_bossRespawnAt.TryGetValue(zone.Id, out var at) && at < 0)
                _bossRespawnAt[zone.Id] = at = now + zone.Boss.Respawn; // just went down
            if (now < at || Player.GlobalPosition.DistanceTo(lair) > 70f || !GameData.Ghosts.TryGetValue(zone.Boss.Ghost, out var def))
                continue;
            SpawnGhost(zone, def, lair with { Y = GroundAt(lair) });
            _bossRespawnAt[zone.Id] = -1;
        }
    }

    public Ghost NearestBoss(float range) => Ghost.All
        .Where(g => g.Def.Boss && !g.Gone && g.GlobalPosition.DistanceTo(Player.GlobalPosition) < range)
        .OrderBy(g => g.GlobalPosition.DistanceTo(Player.GlobalPosition)).FirstOrDefault();

    // Ghosts get stronger the further they are from the zone's entrance (its south edge, toward the city).
    int GhostLevel(ZoneDef zone, Vector3 at)
    {
        if (zone.AlleyOnly)
            return _rng.RandiRange(zone.LevelMin, zone.LevelMax);
        var entrance = zone.Center + new Vector3(0f, 0f, zone.SizeZ * 0.5f);
        float t = Mathf.Clamp(at.DistanceTo(entrance) / (zone.SizeZ * 0.95f), 0f, 1f);
        int lv = Mathf.RoundToInt(Mathf.Lerp(zone.LevelMin, zone.LevelMax, t)) + _rng.RandiRange(-1, 1);
        return Mathf.Clamp(lv, zone.LevelMin, zone.LevelMax);
    }

    public Ghost NearestWeakened(Vector3 from, float maxDist)
    {
        Ghost best = null;
        float bestDist = maxDist;
        foreach (var g in Ghost.All)
        {
            if (!g.Weakened || g.Gone)
                continue;
            float d = g.GlobalPosition.DistanceTo(from);
            if (d <= bestDist)
            {
                best = g;
                bestDist = d;
            }
        }
        return best;
    }

    void OnPhaseChanged(bool night)
    {
        if (night)
        {
            Audio.PlayMusic("amb_night");
            if (Player == null || Cutscene.Active)
                return;
            Hud.Banner("ราตรีมาเยือน", new Color(0.65f, 0.75f, 1f));
            Game.Toast("ตะวันลับฟ้าแล้ว ผีเริ่มออกล่า ระวังตัวด้วย", "moon");
            return;
        }
        Audio.PlayMusic("amb_day");
        if (Player == null || Cutscene.Active)
            return;
        Hud.Banner("รุ่งอรุณ", new Color(1f, 0.85f, 0.55f));
        Game.Toast("รุ่งเช้าแล้ว ผีโพงหายไปกับแสงตะวัน หุ่นไล่กากลับไปนิ่ง", "sun");
        foreach (var g in Ghost.All.ToArray())
            if (!g.Gone && !g.Weakened && g.Def.DespawnAtDawn)
                g.Vanish();
    }

    void BuildEnvironment()
    {
        var sky = DayNight.CreateSkyMaterial();
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogSkyAffect = 0.12f,
            GlowEnabled = true,
            GlowBloom = 0.08f,
            VolumetricFogDensity = 0.012f,
            VolumetricFogAlbedo = new Color(0.6f, 0.65f, 0.8f),
            SsrMaxSteps = 48,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        _sun = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 60f };
        AddChild(_sun);

        DayNight = new DayNight();
        AddChild(DayNight);
        DayNight.Setup(_sun, _env, sky);
        DayNight.PhaseChanged += OnPhaseChanged;
    }
}
