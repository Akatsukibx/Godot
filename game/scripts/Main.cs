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
            _streamed.Add((zone, scene));
        }
        WorldBuilder.ApplyDistanceCulling(this);
        AddChild(WorldBuilder.BuildRice(GetTree().GetNodesInGroup("map_plot").OfType<Node3D>()));
        AddChild(WorldBuilder.BuildForest(WorldBuilder.WorldBounds()));
        AddChild(new CloudLayer { Name = "Clouds" });
        AddChild(WorldBuilder.BuildLilies(GetTree().GetNodesInGroup("lily_field").OfType<Node3D>()));
        Textures.Apply(this);
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
        foreach (var dock in GetTree().GetNodesInGroup("boat_dock").OfType<Node3D>().ToList())
            RowBoat.Moor(dock);
        // Altars sit on the ground, so wait until the physics world has the zones in it.
        GetTree().CreateTimer(0.5).Timeout += BuildSpawnSites;
        foreach (var def in GameData.Npcs.Values)
        {
            var zone = GameData.Zone(def.Zone);
            if (zone == null)
                continue;
            var npc = new Npc();
            AddChild(npc);
            npc.Init(def);
            npc.GlobalPosition = zone.Center + new Vector3(def.X, def.Y != 0f ? def.Y : WorldBuilder.ZoneGround(zone, zone.CenterX + def.X, zone.CenterZ + def.Z), def.Z);
            _npcs.Add(npc);
        }
    }

    // Grass density follows the graphics preset; rebuilt when it changes.
    void RebuildGrass()
    {
        foreach (var g in _grass)
            g.QueueFree();
        _grass.Clear();
        _streamed.RemoveAll(s => !IsInstanceValid(s.node) || s.node.IsQueuedForDeletion());
        float density = Settings.Graphics switch { 0 => 0.15f, 1 => 0.45f, _ => 0.9f };
        var water = _water.Count > 0 ? _water[0].rect : new Rect2();
        foreach (var zone in GameData.Zones.Where(z => z.Scene != null))
        {
            var g = WorldBuilder.BuildGrass(zone, GetTree(), density, water);
            AddChild(g);
            _grass.Add(g);
            _streamed.Add((zone, g));
            // Woodland and undergrowth, as dense as the graphics preset allows.
            var wild = WorldBuilder.BuildWilds(zone, GetTree(), Settings.Graphics switch { 0 => 0.35f, 1 => 0.7f, _ => 1f }, water,
                GameData.Npcs.Values.Where(n => n.Zone == zone.Id).Select(n => zone.Center + new Vector3(n.X, 0f, n.Z)));
            AddChild(wild);
            Textures.Apply(wild);
            _grass.Add(wild);
            _streamed.Add((zone, wild));
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

    public void OpenClassSelectInSlot(int slot)
    {
        Hud.ShowClassSelect(GameData.Classes, def => Hud.AskName(def, name =>
        {
            SaveGame.Current = slot;
            StartGame(def, null, name);
        }));
    }

    // A new character goes into the first free slot (when all are full the player is sent to delete one).
    public void OpenClassSelect()
    {
        int free = SaveGame.FirstFreeSlot();
        if (free < 0)
        {
            Menus.ShowCharacterSelect("ช่องเก็บตัวละครเต็ม ลบตัวละครหนึ่งตัวก่อนสร้างตัวใหม่");
            return;
        }
        Hud.ShowClassSelect(GameData.Classes, def => Hud.AskName(def, name =>
        {
            SaveGame.Current = free;
            StartGame(def, null, name);
        }));
    }

    public void ContinueGame(int slot = 0)
    {
        SaveGame.Current = slot;
        var save = SaveGame.Load(slot);
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
    //   --pos=<x>,<z>[,<y>]    place the player (world coordinates; y for upper floors)
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
        if (args.ContainsKey("screenshot") || args.ContainsKey("selftest") || args.ContainsKey("bake"))
            SaveGame.Disabled = true;
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
            Player.GlobalPosition = new Vector3(F(xz[0]), xz.Length > 2 ? F(xz[2]) : 0.3f, F(xz[1]));
            _camPivot.GlobalPosition = Player.GlobalPosition;
        }
        // --spawn=id,id…: call those ghosts in a row in front of the player (model checks).
        if (Player != null && args.TryGetValue("spawn", out var spawnIds))
            GetTree().CreateTimer(1.0).Timeout += () =>
            {
                var ids = spawnIds.Split(',');
                for (int i = 0; i < ids.Length; i++)
                    if (GameData.Ghosts.TryGetValue(ids[i], out var gd))
                    {
                        var at = Player.GlobalPosition + new Vector3((i - (ids.Length - 1) * 0.5f) * 3.2f, 0f, -7f);
                        float y = gd.Aquatic && WaterSurfaceAt(at) is float ws ? ws - 0.85f : GroundAt(at + Vector3.Up * 3f);
                        var g = SpawnGhost(CurrentZone ?? GameData.Zone("wiang_mok"), gd, at with { Y = y });
                        if (g != null)
                            g.SetPhysicsProcess(false); // hold still for the picture
                    }
            };
        // --boat[=seconds]: board the nearest rowing boat and (optionally) row straight ahead for a while.
        if (Player != null && args.TryGetValue("boat", out var rowFor))
            GetTree().CreateTimer(0.8).Timeout += () =>
            {
                var boat = GetTree().GetNodesInGroup("rowboat").OfType<RowBoat>().OrderBy(b => b.GlobalPosition.DistanceTo(Player.GlobalPosition)).FirstOrDefault();
                if (boat == null)
                    return;
                boat.Board(Player);
                boat.DevRow = string.IsNullOrEmpty(rowFor) ? 0f : F(rowFor);
            };
        if (Player != null && args.TryGetValue("level", out var lv))
            Player.DevSetLevel(int.Parse(lv));
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
                    default:
                        if (screen.StartsWith("shop:"))
                            Hud.OpenShop(screen[5..]);
                        else if (screen == "worldmap")
                            Hud.OpenWorldMap();
                        else
                            Hud.OpenWindow(screen);
                        break;
                }
            };
        }
        _fpsLog = args.ContainsKey("fps");
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
        RestoreOpenedChests();
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
        if (shrine == null)
            return new Vector3(0f, 0.3f, 9f);
        // In front of the shrine, on whatever ground is there (a slope, an upper floor).
        var p = shrine.GlobalPosition + new Vector3(0f, 0f, 5f);
        return p with { Y = GroundAt(p, 4f) + 0.3f };
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
            SaveNow();
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
        // Aboard a boat the only thing to do is step ashore.
        if (Player.Boat != null)
        {
            _interactTarget = Player.Boat;
            _prompt.Visible = true;
            _prompt.Text = $"[{Settings.BindingText("interact")}] ขึ้นฝั่ง";
            _prompt.GlobalPosition = Player.Boat.GlobalPosition + new Vector3(0f, 2.4f, 0f);
            if (ControlsEnabled && Input.IsActionJustPressed("interact"))
                Interact(Player.Boat);
            return;
        }
        foreach (var boat in GetTree().GetNodesInGroup("rowboat").OfType<RowBoat>())
        {
            float d = boat.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (boat.Rider == null && d < 3.5f && d < best)
            {
                best = d;
                _interactTarget = boat;
                text = $"[{Settings.BindingText("interact")}] ขึ้นเรือพาย";
            }
        }
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
        foreach (var chest in GetTree().GetNodesInGroup("chest").OfType<Node3D>())
        {
            if (Player.OpenedChests.Contains((string)chest.GetMeta("chest_id")))
                continue;
            float d = chest.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (d < 3f && d < best)
            {
                best = d;
                _interactTarget = chest;
                text = $"[{Settings.BindingText("interact")}] เปิดหีบลึกลับ";
            }
        }
        foreach (var gate in GetTree().GetNodesInGroup("dungeon_gate").OfType<Node3D>())
        {
            float d = gate.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (gate.IsVisibleInTree() && d < 4f && d < best)
            {
                best = d;
                _interactTarget = gate;
                text = $"[{Settings.BindingText("interact")}] เข้า{GameData.Zone((string)gate.GetMeta("zone_id"))?.Name}";
            }
        }
        foreach (var door in GetTree().GetNodesInGroup("locked_door").OfType<Node3D>())
        {
            if (!door.IsVisibleInTree() || Player.OpenedChests.Contains("door:" + (string)door.GetMeta("door_id")))
                continue;
            float d = door.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (d < 3.2f && d < best)
            {
                best = d;
                _interactTarget = door;
                string key = (string)door.GetMeta("key");
                text = Player.ItemCount(key) > 0
                    ? $"[{Settings.BindingText("interact")}] ไข{door.GetMeta("door_name")}ด้วย{GameData.ItemName(key)}"
                    : $"{door.GetMeta("door_name")} (ล็อก · ต้องใช้{GameData.ItemName(key)})";
            }
        }
        foreach (var (zoneId, altar) in _altars)
        {
            float d = altar.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (d < 3.5f && d < best)
            {
                best = d;
                _interactTarget = altar;
                text = $"[{Settings.BindingText("interact")}] แท่นอัญเชิญ{GameData.GhostName(GameData.Zone(zoneId).Boss.Ghost)}";
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
        if (target is RowBoat boat)
        {
            if (boat.Rider == Player)
            {
                if (!boat.TryLeave())
                    Game.Toast("พายเข้าใกล้ฝั่งหรือท่าเรือก่อน");
            }
            else
                boat.Board(Player);
        }
        else if (target.IsInGroup("boss_altar"))
            ShowAltarMenu(GameData.Zone((string)target.GetMeta("zone_id")));
        else if (target.IsInGroup("chest"))
            OpenChest(target);
        else if (target.IsInGroup("locked_door"))
            UnlockDoor(target);
        else if (target.IsInGroup("dungeon_gate"))
            ShowDungeonGate(GameData.Zone((string)target.GetMeta("zone_id")));
        else if (target.IsInGroup("bus_stop"))
            ShowBusMenu((string)target.GetMeta("stop_id"));
        else
            BindShrine(target);
    }

    // Mystery chests: money, a rare material or two and now and then something better. Opened once and saved.
    static readonly Dictionary<string, string[]> ChestLoot = new()
    {
        ["wiang_mok"] = new[] { "saeng_winyan", "lek_sanim", "mai_phu", "sai_sin_khat", "ya_hom_yai" },
        ["thung_na"] = new[] { "khao_san_sek", "namkhang_ratri", "lek_nam_phi", "fang_sap", "ya_hom_yai", "takrut" },
        ["bueng_bua"] = new[] { "phlueak_winyan", "bai_tani", "sai_bua", "set_tra_phanuek", "ya_hom_yai", "waen_phirot" },
        ["pa_phai"] = new[] { "phlueak_winyan", "mak_phlu", "mai_phai_sap", "set_tra_phanuek", "ya_hom_yai", "khrueang_rang_khao" },
        ["rong_phayaban"] = new[] { "phlueak_winyan", "phaphan_sap", "pai_khomue", "set_tra_phanuek", "ya_hom_yai", "takrut_phai_tan" },
        ["wat_doi"] = new[] { "phlueak_winyan", "thong_boran", "khiao_asura", "khao_kon_bat", "set_tra_phanuek", "ya_hom_yai", "soi_mae_sue" },
    };

    void OpenChest(Node3D chest)
    {
        string id = (string)chest.GetMeta("chest_id");
        if (!Player.OpenedChests.Add(id))
            return;
        // A chest set out by hand holds one particular thing (a key, a letter) instead of random loot.
        if (chest.HasMeta("loot"))
        {
            string item = (string)chest.GetMeta("loot");
            Player.AddItem(item, 1);
            ApplyChestOpen(chest);
            Audio.Play("levelup", -4f, 0f);
            Fx.Ring(this, chest.GlobalPosition, 2.5f, new Color(1f, 0.85f, 0.4f), 0.8f);
            Game.Toast($"พบ{GameData.ItemName(item)}", "quest", 4f);
            SaveNow();
            return;
        }
        string zone = id[..id.LastIndexOf('_')];
        var zoneDef = GameData.Zone(zone);
        int mid = zoneDef == null ? 5 : (zoneDef.LevelMin + zoneDef.LevelMax) / 2;
        Player.AddMoney(_rng.RandiRange(25, 60) + mid * 8);
        var pool = ChestLoot.GetValueOrDefault(zone) ?? ChestLoot["wiang_mok"];
        for (int i = 0; i < 2; i++)
            Player.AddItem(pool[_rng.RandiRange(0, pool.Length - 1)], _rng.RandiRange(1, 2));
        if (_rng.Randf() < 0.25f)
            Player.AddItem(pool[^1], 1);
        ApplyChestOpen(chest);
        Audio.Play("levelup", -4f, 0f);
        Fx.Ring(this, chest.GlobalPosition, 2.5f, new Color(1f, 0.85f, 0.4f), 0.8f);
        int found = Player.OpenedChests.Count(c => c.StartsWith(zone + "_"));
        Game.Toast($"เปิดหีบลึกลับ · พบหีบในโซนนี้แล้ว {found}/5", "quest", 4f);
        SaveNow();
    }

    // Lid thrown back, glow gone.
    void ApplyChestOpen(Node3D chest)
    {
        if (chest.FindChild("ChestLid", true, false) is Node3D lid)
        {
            lid.Position += new Vector3(0f, 0.25f, 0.45f);
            lid.Rotation = new Vector3(-1.2f, 0f, Mathf.Pi * 0.5f);
        }
        foreach (var n in new[] { "GlowYan", "ChestGlow" })
            if (chest.FindChild(n, true, false) is Node3D g)
                g.Visible = false;
    }

    void RestoreOpenedChests()
    {
        foreach (var chest in GetTree().GetNodesInGroup("chest").OfType<Node3D>())
            if (Player.OpenedChests.Contains((string)chest.GetMeta("chest_id")))
                ApplyChestOpen(chest);
        foreach (var door in GetTree().GetNodesInGroup("locked_door").OfType<Node3D>())
            SetDoorOpen(door, Player.OpenedChests.Contains("door:" + (string)door.GetMeta("door_id")));
    }

    // Way into a dungeon or instance that lives in a zone of its own (zones.json kind + parent).
    // Joining and the warp come with the party system; until then the gate says so.
    void ShowDungeonGate(ZoneDef zone)
    {
        if (zone == null)
            return;
        string kind = zone.Kind == "dungeon" ? "ดันเจี้ยนปาร์ตี้" : "instance";
        Hud.ShowMenu($"{zone.Name} · {kind}",
            $"ศัตรู: {zone.Enemies}\n\nทางลงมืดสนิท ลมเย็นพัดขึ้นมาพร้อมเสียงขุดดินที่ไม่มีใครขุด\n\nยังเข้าไม่ได้: {kind}นี้จะเปิดพร้อมระบบปาร์ตี้",
            new List<(string, System.Action)> { ("ไว้ก่อน", () => { }) });
    }

    // Locked doors (group locked_door): the key item stays in the bag, the door stays open (saved).
    void UnlockDoor(Node3D door)
    {
        string key = (string)door.GetMeta("key");
        if (Player.ItemCount(key) <= 0)
        {
            Game.Toast($"{door.GetMeta("door_name")}ล็อกอยู่ ต้องใช้{GameData.ItemName(key)}");
            Audio.Play("hit", -8f, 0f);
            return;
        }
        Player.OpenedChests.Add("door:" + (string)door.GetMeta("door_id"));
        SetDoorOpen(door, true);
        Audio.Play("levelup", -6f, 0f);
        Game.Toast($"ไข{door.GetMeta("door_name")}แล้ว");
        SaveNow();
    }

    static void SetDoorOpen(Node3D door, bool open)
    {
        foreach (var n in door.GetChildren())
        {
            if (n is Node3D panel && panel.Name.ToString().StartsWith("DoorPanel"))
                panel.Visible = !open;
            if (n is CollisionObject3D body)
                body.CollisionLayer = open ? 0u : Layers.World;
        }
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
        yield return ("บึงบัวสาย: ท่าเรือลุงพุก", bueng + new Vector3(24f, 0.8f, 158f));
        yield return ("บึงบัวสาย: ท่าเรือหมู่บ้าน", bueng + new Vector3(-114f, 0.8f, 70f));
        var phai = new Vector3(372f, 0f, -744f);
        yield return ("ป่าไผ่ทึบ: ทางเข้า (ค่ายพราน)", phai + new Vector3(-150f, 0.3f, 36f));
        yield return ("ป่าไผ่ทึบ: หมู่บ้านร้าง", phai + new Vector3(0f, 0.3f, 8f));
        yield return ("ป่าไผ่ทึบ: ศาลเจ้าพ่อไผ่", phai + new Vector3(-30f, 0.3f, -104f));
        yield return ("ป่าไผ่ทึบ: ป่าช้าเก่า", phai + new Vector3(40f, 0.3f, 108f));
        yield return ("บึงบัวสาย: สะพานไปป่าไผ่", bueng + new Vector3(150f, 1f, 30f));
        yield return ("ป่าไผ่ทึบ: ทางลงโรงพยาบาล", phai + new Vector3(42f, 0.3f, 160f));
        var hosp = new Vector3(372f, 0f, -372f);
        yield return ("โรงพยาบาลร้าง: ป้อมยาม (ทางเข้า)", hosp + new Vector3(46f, 0.3f, -116f));
        yield return ("โรงพยาบาลร้าง: ลานหน้าตึก", hosp + new Vector3(0f, 0.3f, -20f));
        yield return ("โรงพยาบาลร้าง: บันไดหนีไฟฝั่งตะวันตก", hosp + new Vector3(-48f, 0.3f, 4f));
        yield return ("โรงพยาบาลร้าง: ห้องพระชั้นสอง", hosp + new Vector3(14f, 7.3f, 28f));
        yield return ("โรงพยาบาลร้าง: หอผู้ป่วยหลังคลอด ชั้นสาม", hosp + new Vector3(-24f, 14.3f, 24f));
        yield return ("โรงพยาบาลร้าง: ทางเดินป่าไปดอย", hosp + new Vector3(150f, 0.3f, 18f));
        var doi = new Vector3(744f, 0f, -372f);
        Vector3 Doi(float x, float z) => doi + new Vector3(x, WorldBuilder.DoiHeight(x, z) + 0.6f, z);
        yield return ("วัดร้างบนดอย: เชิงดอย (กลดพระธุดงค์)", Doi(-150f, 34f));
        yield return ("วัดร้างบนดอย: ศาลากลางทาง", Doi(8f, 28f));
        yield return ("วัดร้างบนดอย: ยอดดอย (ประตูวัด)", Doi(24f, -20f));
        yield return ("วัดร้างบนดอย: หน้าปรางค์", Doi(30f, -50f));
        yield return ("วัดร้างบนดอย: สะพานข้ามเหว", Doi(95f, 66f));
        yield return ("วัดร้างบนดอย: ปากเหมืองร้าง", Doi(122f, 80f));
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
        if (_fpsLog)
            LogFps(dt);
        _autosave -= dt;
        if (_autosave <= 0f)
        {
            _autosave = 120f;
            if (Player != null && !Player.Dead && !Cutscene.Active)
            {
                SaveNow();
                Game.Toast("บันทึกอัตโนมัติ", null, 1.2f);
            }
        }
        UpdateCamera(dt);
        UpdateStreaming(dt);
        _fireflies ??= GetTree().GetNodesInGroup("fireflies").OfType<GpuParticles3D>().ToList();
        bool flies = DayNight.NightFactor > 0.5f && Weather.Intensity < 0.5f;
        foreach (var ff in _fireflies)
            if (ff.Emitting != flies)
                ff.Emitting = flies;
        // Failing tube lights (group flicker) stutter on and off, each to its own beat.
        _flicker ??= GetTree().GetNodesInGroup("flicker").OfType<Light3D>().ToList();
        float clock = Time.GetTicksMsec() / 1000f;
        foreach (var l in _flicker)
            if (l.IsVisibleInTree())
            {
                float seed = l.GetInstanceId() % 997;
                float n = Mathf.Sin(clock * 13f + seed) * Mathf.Sin(clock * 7.3f + seed * 1.7f);
                l.LightEnergy = (float)l.GetMeta("energy", 1f) * (n > 0.55f ? 0.12f : 1f);
            }
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

    // Zone streaming: a zone (its buildings, grass and woodland) is only drawn while the camera is
    // within StreamRange of its edge, so far-away zones cost nothing.
    const float StreamRange = 150f;
    readonly List<(ZoneDef zone, Node3D node)> _streamed = new();
    List<GpuParticles3D> _fireflies;
    List<Light3D> _flicker;
    float _streamTick;
    Vector3 _streamAt;

    void UpdateStreaming(float dt)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        var at = cam.GlobalPosition;
        // Re-check at once after a warp so the new place never shows up empty.
        _streamTick -= dt;
        if (_streamTick > 0f && at.DistanceSquaredTo(_streamAt) < 40f * 40f)
            return;
        _streamTick = 0.25f;
        _streamAt = at;
        foreach (var (zone, node) in _streamed)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(at.X - zone.CenterX) - zone.SizeX * 0.5f);
            float dz = Mathf.Max(0f, Mathf.Abs(at.Z - zone.CenterZ) - zone.SizeZ * 0.5f);
            bool show = dx * dx + dz * dz < StreamRange * StreamRange;
            if (node.Visible != show)
                node.Visible = show;
        }
        foreach (var npc in _npcs)
        {
            bool show = npc.GlobalPosition.DistanceSquaredTo(at) < 160f * 160f;
            if (npc.Visible != show)
            {
                npc.Visible = show;
                npc.ProcessMode = show ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
            }
        }
    }

    // --fps: every 2 s print the average frame rate, the worst frame and what the renderer drew.
    bool _fpsLog;
    float _fpsTime, _fpsWorst;
    int _fpsFrames;

    void LogFps(float dt)
    {
        _fpsTime += dt;
        _fpsFrames++;
        _fpsWorst = Mathf.Max(_fpsWorst, dt);
        if (_fpsTime < 2f)
            return;
        var calls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        var prims = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        GD.Print($"[fps] zone={CurrentZone?.Id} avg={_fpsFrames / _fpsTime:0} worst={_fpsWorst * 1000f:0}ms draws={calls} prims={prims / 1000}k");
        _fpsTime = _fpsWorst = 0f;
        _fpsFrames = 0;
    }

    // ---------- ghosts ----------

    float _autosave = 120f;

    void UpdateSpawning(float dt)
    {
        // Ghosts far from the player fade away so each zone only keeps the ones nearby.
        foreach (var g in Ghost.All.ToArray())
            if (!g.Gone && !g.Weakened && g.GlobalPosition.DistanceTo(Player.GlobalPosition) > (g.Def.Boss ? 130f : g.Group != null ? 125f : 85f))
                g.Vanish();

        _spawnTimer -= dt;
        if (_spawnTimer > 0f || Player.Dead || CurrentZone == null || Cutscene.Active)
            return;
        bool night = DayNight.IsNight;
        _spawnTimer = night ? Cfg.SpawnIntervalNight : Cfg.SpawnIntervalDay;
        var zone = CurrentZone;
        int alive = Ghost.All.Count(g => !g.Gone && g.Zone == zone.Id && g.Group == null && !g.Def.Boss);
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

    // Puts a ghost of this kind in the zone: at `at` if given, inside the circle `area` (a spawn group),
    // in an alley (town), in a paddy (scarecrows) or somewhere around the player.
    Ghost SpawnGhost(ZoneDef zone, GhostDef def, Vector3? at = null, (Vector3 c, float r)? area = null)
    {
        Vector3? found = at;
        if (found == null && zone.AlleyOnly && area == null)
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
                if (area is { } circle)
                {
                    float a = _rng.Randf() * Mathf.Tau, rr = circle.r * Mathf.Sqrt(_rng.Randf());
                    pos = circle.c + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                }
                else if (def.Behavior == "statue" && plots.Count > 0)
                    pos = plots[_rng.RandiRange(0, plots.Count - 1)].GlobalPosition + new Vector3(_rng.RandfRange(-4f, 4f), 0f, _rng.RandfRange(-4f, 4f));
                else
                {
                    float angle = _rng.Randf() * Mathf.Tau;
                    pos = Player.GlobalPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _rng.RandfRange(16f, 26f);
                }
                // On the mountain the ground can be far above or below the player: start from the terrain.
                if (zone.Scene == "wat_doi")
                    pos.Y = WorldBuilder.ZoneGround(zone, pos.X, pos.Z) + 0.5f;
                float depth = WaterDepthAt(pos);
                bool terrainOk = def.Aquatic ? depth > 0.9f : def.Flying || depth <= 0f;
                bool ok = zone.Contains(pos) && terrainOk && !InSanctuary(pos)
                    && pos.DistanceTo(zone.Center) > Cfg.SafeZoneRadius + 7f
                    && pos.DistanceTo(Player.GlobalPosition) > (area != null ? 6f : 10f)
                    && Mathf.Abs(pos.X - zone.CenterX) < zone.SizeX * 0.5f - 3f;
                if (ok)
                    found = def.Aquatic && WaterSurfaceAt(pos) is float s ? pos with { Y = s - 0.85f } : pos with { Y = GroundAt(pos) };
            }
        }
        if (found is not Vector3 spot)
            return null;

        var ghost = new Ghost { Name = def.Id };
        AddChild(ghost);
        ghost.Init(def, def.Boss ? zone.LevelMax + 2 : GhostLevel(zone, spot));
        ghost.Zone = zone.Id;
        ghost.GlobalPosition = spot + Vector3.Up * 0.2f;
        ghost.Home = spot;
        return ghost;
    }

    // Height of the ground (or a pier) under a point.
    // Height of the floor under p, looking from `above` metres over it (keep it under the next storey
    // when p is inside a building).
    public float GroundAt(Vector3 p, float above = 6f)
    {
        var query = PhysicsRayQueryParameters3D.Create(new Vector3(p.X, p.Y + above, p.Z), new Vector3(p.X, p.Y - 8f, p.Z), Layers.World);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? ((Vector3)hit["position"]).Y : p.Y;
    }

    // ---------- villages (no ghosts) and zone bosses ----------

    readonly List<(Vector3 c, float r)> _sanctuaries = new();
    readonly Dictionary<string, double> _bossRespawnAt = new();

    public bool InSanctuary(Vector3 p) => _sanctuaries.Any(s => new Vector2(p.X - s.c.X, p.Z - s.c.Z).Length() < s.r);

    // Zone bosses no longer wait at their lair: they are summoned at its altar (see MainSpawns).
    // This only notes when one goes down, so the altar can count down its rest.
    void UpdateBosses()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var zone in GameData.Zones.Where(z => z.Boss != null && z.Scene != null))
        {
            bool alive = Ghost.All.Any(g => !g.Gone && g.Def.Id == zone.Boss.Ghost);
            if (alive)
                _bossRespawnAt[zone.Id] = -1;
            else if (_bossRespawnAt.TryGetValue(zone.Id, out var at) && at < 0)
                _bossRespawnAt[zone.Id] = now + zone.Boss.Respawn; // just went down
        }
        UpdateSpawnGroups(now);
    }

    public Ghost NearestBoss(float range) => Ghost.All
        .Where(g => g.Def.Boss && !g.Gone && g.GlobalPosition.DistanceTo(Player.GlobalPosition) < range)
        .OrderBy(g => g.GlobalPosition.DistanceTo(Player.GlobalPosition)).FirstOrDefault();

    // Ghosts get stronger the further they are from the zone's entrance (its south edge, toward the city).
    int GhostLevel(ZoneDef zone, Vector3 at)
    {
        if (zone.AlleyOnly)
            return _rng.RandiRange(zone.LevelMin, zone.LevelMax);
        var entrance = zone.Center + zone.Entrance switch
        {
            "west" => new Vector3(-zone.SizeX * 0.5f, 0f, 0f),
            "east" => new Vector3(zone.SizeX * 0.5f, 0f, 0f),
            "north" => new Vector3(0f, 0f, -zone.SizeZ * 0.5f),
            _ => new Vector3(0f, 0f, zone.SizeZ * 0.5f),
        };
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
        Game.Toast("รุ่งเช้าแล้ว ผีที่ออกตอนกลางคืนหายไปกับแสงตะวัน หุ่นไล่กากลับไปนิ่ง", "sun");
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
