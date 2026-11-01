using Godot;
using System.Collections.Generic;
using System.Linq;

// Spawn groups, boss altars and the admin spawn tools.
//  • Spawn groups (zones.json → spawnGroups): packs that live in one place. While the player is within
//    reach the pack fills up to its count; a member that is banished or released comes back after the
//    group's respawn time; members never chase further than the group's leash.
//  • Boss altars: each zone boss is called out at an altar by its lair. The altar asks for an offering,
//    a level and sometimes the hour (zones.json → boss.summon), then rests for boss.respawn seconds.
public partial class Main
{
    const float GroupActiveRange = 95f;

    class GroupState
    {
        public SpawnGroupDef Def;
        public ZoneDef Zone;
        public int LastAlive;
        public double NextAt;
        public Vector3 Center => Zone.Center + new Vector3(Def.X, Def.Y, Def.Z);
    }

    readonly List<GroupState> _groups = new();
    readonly Dictionary<string, Node3D> _altars = new();
    Node3D _groupMarkers;

    static bool TimeOk(string time, bool night) => time switch { "night" => night, "day" => !night, _ => true };

    void BuildSpawnSites()
    {
        foreach (var zone in GameData.Zones.Where(z => z.Scene != null))
        {
            foreach (var g in zone.SpawnGroups)
                _groups.Add(new GroupState { Def = g, Zone = zone });
            if (zone.Boss != null)
                _altars[zone.Id] = BuildAltar(zone);
        }
    }

    void UpdateSpawnGroups(double now)
    {
        if (Player == null || Player.Dead || Cutscene.Active)
            return;
        bool night = DayNight.IsNight;
        foreach (var s in _groups)
        {
            int alive = Ghost.All.Count(g => !g.Gone && g.Group == s.Def.Id);
            bool near = Player.GlobalPosition.DistanceTo(s.Center) < GroupActiveRange;
            // A member went down while the player was around: its place stays empty for a while.
            if (alive < s.LastAlive && near)
                s.NextAt = Mathf.Max((float)s.NextAt, (float)(now + s.Def.Respawn));
            s.LastAlive = alive;
            if (!near || !TimeOk(s.Def.Time, night) || alive >= s.Def.Count || now < s.NextAt)
                continue;
            var pool = s.Def.Ghosts.Select(id => GameData.Ghosts.GetValueOrDefault(id)).Where(d => d != null).ToList();
            if (pool.Count == 0)
                continue;
            var ghost = SpawnGhost(s.Zone, pool[_rng.RandiRange(0, pool.Count - 1)], null, (s.Center, s.Def.Radius));
            if (ghost != null)
            {
                ghost.Group = s.Def.Id;
                ghost.Leash = s.Def.Leash;
                ghost.Home = s.Center with { Y = ghost.Home.Y };
                s.LastAlive++;
            }
            s.NextAt = now + 1.2; // fill up one at a time
        }
    }

    // ---------- boss altars ----------

    Node3D BuildAltar(ZoneDef zone)
    {
        var lair = zone.Center + new Vector3(zone.Boss.X, zone.Boss.Y, zone.Boss.Z);
        var spot = lair + new Vector3(0f, 0f, 7f);
        var altar = new Node3D { Name = $"BossAltar_{zone.Id}" };
        AddChild(altar);
        altar.GlobalPosition = spot with { Y = GroundAt(spot, 4f) };
        var stone = new Color(0.42f, 0.4f, 0.37f);
        Shapes.Box(altar, new Vector3(0f, 0.3f, 0f), new Vector3(1.8f, 0.6f, 1.1f), stone, 0f, "StoneAltar");
        Shapes.Box(altar, new Vector3(0f, 0.66f, 0f), new Vector3(1.4f, 0.12f, 0.8f), stone.Lightened(0.15f), 0f, "StoneSlab");
        foreach (var x in new[] { -0.55f, 0.55f })
        {
            Shapes.Cylinder(altar, new Vector3(x, 0.84f, 0.2f), 0.04f, 0.04f, 0.24f, new Color(0.95f, 0.9f, 0.8f), 0f, "Candle");
            Shapes.Sphere(altar, new Vector3(x, 1f, 0.2f), 0.05f, new Color(1f, 0.6f, 0.25f), 4f, "GlowFlame");
        }
        Shapes.Cylinder(altar, new Vector3(0f, 0.8f, -0.1f), 0.16f, 0.12f, 0.2f, new Color(0.5f, 0.32f, 0.18f), 0f, "ClayPot");
        foreach (var x in new[] { -1.2f, 1.2f })
        {
            Shapes.Cylinder(altar, new Vector3(x, 1f, -0.3f), 0.05f, 0.05f, 2f, new Color(0.3f, 0.2f, 0.12f), 0f, "Pole");
            Shapes.Box(altar, new Vector3(x, 1.5f, -0.25f), new Vector3(0.3f, 0.9f, 0.02f), new Color(0.75f, 0.15f, 0.12f), 0f, "ClothTung");
        }
        Shapes.Light(altar, new Vector3(0f, 1.4f, 0.4f), new Color(1f, 0.55f, 0.3f), 1.2f, 6f, "CandleLight");
        Shapes.Solid(altar, new Vector3(0f, 0.36f, 0f), new Vector3(1.8f, 0.72f, 1.1f));
        altar.AddToGroup("boss_altar");
        altar.SetMeta("zone_id", zone.Id);
        Textures.Apply(altar);
        return altar;
    }

    // What still stands between the player and the boss, one line each (empty when ready).
    List<string> SummonProblems(ZoneDef zone)
    {
        var s = zone.Boss.Summon ?? new SummonDef();
        var problems = new List<string>();
        double now = Time.GetTicksMsec() / 1000.0;
        if (Ghost.All.Any(g => !g.Gone && g.Def.Id == zone.Boss.Ghost))
            problems.Add("บอสออกมาแล้ว");
        else if (_bossRespawnAt.TryGetValue(zone.Id, out var at) && at > now)
            problems.Add($"แท่นยังพักอยู่ อีก {Mathf.CeilToInt((float)(at - now))} วินาที");
        if (Player.Level < s.MinLevel)
            problems.Add($"ต้องเลเวล {s.MinLevel} ขึ้นไป");
        if (!TimeOk(s.Time, DayNight.IsNight))
            problems.Add(s.Time == "night" ? "อัญเชิญได้เฉพาะกลางคืน" : "อัญเชิญได้เฉพาะกลางวัน");
        foreach (var (id, n) in s.Offering)
            if (Player.ItemCount(id) < n)
                problems.Add($"ของถวาย {GameData.ItemName(id)} {Player.ItemCount(id)}/{n}");
        return problems;
    }

    void ShowAltarMenu(ZoneDef zone)
    {
        var s = zone.Boss.Summon ?? new SummonDef();
        var problems = SummonProblems(zone);
        var need = new List<string>();
        foreach (var (id, n) in s.Offering)
            need.Add($"{(Player.ItemCount(id) >= n ? "✓" : "✗")} {GameData.ItemName(id)} {Player.ItemCount(id)}/{n}");
        need.Add($"{(Player.Level >= s.MinLevel ? "✓" : "✗")} เลเวล {s.MinLevel}+");
        if (s.Time != "any")
            need.Add($"{(TimeOk(s.Time, DayNight.IsNight) ? "✓" : "✗")} {(s.Time == "night" ? "กลางคืน" : "กลางวัน")}");
        string text = $"{s.Text}\n\n{string.Join("\n", need)}" + (problems.Count > 0 ? $"\n\n{problems[0]}" : "");
        var options = new List<(string, System.Action)>();
        if (problems.Count == 0)
            options.Add(($"ถวายของและอัญเชิญ{GameData.GhostName(zone.Boss.Ghost)}", () => SummonBoss(zone, true)));
        options.Add(("ไว้ก่อน", () => { }));
        Hud.ShowMenu($"แท่นอัญเชิญ · {zone.Boss.Lair}", text, options);
    }

    public void SummonBoss(ZoneDef zone, bool payOffering)
    {
        if (!GameData.Ghosts.TryGetValue(zone.Boss.Ghost, out var def))
            return;
        if (payOffering)
            foreach (var (id, n) in (zone.Boss.Summon ?? new SummonDef()).Offering)
                Player.RemoveItem(id, n);
        var lair = zone.Center + new Vector3(zone.Boss.X, zone.Boss.Y, zone.Boss.Z);
        var boss = SpawnGhost(zone, def, lair with { Y = GroundAt(lair, 4f) });
        if (boss == null)
            return;
        _bossRespawnAt[zone.Id] = -1;
        boss.WakeUp();
        Fx.Ring(this, lair, 8f, new Color(1f, 0.3f, 0.25f), 1.2f);
        Audio.Play("ghost", 4f, 0f);
        Hud.Banner($"{def.Name} ตื่นขึ้นแล้ว!", new Color(1f, 0.45f, 0.4f));
    }

    // ---------- admin ----------

    public void AdminSpawn(GhostDef def)
    {
        var zone = CurrentZone ?? GameData.Zone("wiang_mok");
        var at = Player.GlobalPosition + Player.Facing * 6f;
        var g = SpawnGhost(zone, def, at with { Y = GroundAt(at, 2f) });
        g?.WakeUp();
        Game.Toast(g != null ? $"เรียก{def.Name}แล้ว (แอดมิน)" : "เรียกไม่ได้ตรงนี้");
    }

    public void AdminSummonBoss()
    {
        var zone = GameData.Zones.Where(z => z.Boss != null && z.Scene != null)
            .OrderBy(z => (z.Center + new Vector3(z.Boss.X, 0f, z.Boss.Z)).DistanceTo(Player.GlobalPosition)).FirstOrDefault();
        if (zone != null)
            SummonBoss(zone, false);
    }

    public void AdminClearGhosts()
    {
        foreach (var g in Ghost.All.ToArray())
            if (!g.Gone)
                g.Vanish();
        foreach (var s in _groups)
        {
            s.LastAlive = 0;
            s.NextAt = 0;
        }
        _bossRespawnAt.Clear();
        Game.Toast("ล้างผีทั้งหมดและรีเซ็ตเวลาเกิด (แอดมิน)");
    }

    public bool GroupMarkersVisible => _groupMarkers != null;

    // Floating labels over every spawn group and altar, for checking the layout.
    public void AdminToggleGroupMarkers()
    {
        if (_groupMarkers != null)
        {
            _groupMarkers.QueueFree();
            _groupMarkers = null;
            return;
        }
        _groupMarkers = new Node3D { Name = "SpawnGroupMarkers" };
        AddChild(_groupMarkers);
        foreach (var s in _groups)
        {
            var label = new Label3D
            {
                Text = $"◎ {s.Def.Name}\n{string.Join(", ", s.Def.Ghosts.Select(GameData.GhostName))} ×{s.Def.Count} · เกิดใหม่ {s.Def.Respawn:0}s · {s.Def.Time}",
                FontSize = 48, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
                Modulate = new Color(0.6f, 1f, 0.8f),
            };
            _groupMarkers.AddChild(label);
            label.GlobalPosition = s.Center + Vector3.Up * 8f;
            Fx.Ring(_groupMarkers, s.Center + Vector3.Up * 0.3f, s.Def.Radius, new Color(0.6f, 1f, 0.8f), 600f);
        }
    }
}
