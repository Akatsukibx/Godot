using Godot;
using System.Collections.Generic;
using System.Linq;

// Layout helpers shared by the outer zones: winding roads (every house gets a driveway onto one),
// a record of what is already built so houses never overlap, signposts and thickets of trees.
// Everything works in the zone's local coordinates while a zone is being built.
public static partial class WorldBuilder
{
    static readonly List<(Vector2 a, Vector2 b, float half)> _roads = new();
    static readonly List<(Vector2 c, float r)> _occupied = new();
    static readonly List<(Vector2 c, float r)> _landmarks = new();
    static readonly List<Vector3> _plots = new();
    static int _roadCount;

    // A village or shrine yard that ghosts will not enter (read at runtime by Main).
    static void Sanctuary(Node3D parent, Vector3 pos, float radius)
    {
        var s = Shapes.Group(parent, "Sanctuary", pos);
        s.AddToGroup("sanctuary", true);
        s.SetMeta("radius", radius);
    }

    static void ResetLayout()
    {
        _roads.Clear();
        _occupied.Clear();
        _landmarks.Clear();
        _plots.Clear();
        _roadCount = 0;
    }

    static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    static void AddRoad(Vector3 a, Vector3 b, float half) => _roads.Add((Flat(a), Flat(b), half));

    static void Occupy(Vector3 c, float r) => _occupied.Add((Flat(c), r));

    static bool Blocked(Vector3 c, float r) => _occupied.Any(o => o.c.DistanceTo(Flat(c)) < o.r + r);

    static bool NearLandmark(Vector3 c, float pad = 0f) => _landmarks.Any(l => l.c.DistanceTo(Flat(c)) < l.r + pad);

    static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-6f ? 0f : Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return a + ab * t;
    }

    // Distance from p to the edge of the nearest road (negative when on one).
    static float DistToRoads(Vector3 p)
    {
        var q = Flat(p);
        float best = float.MaxValue;
        foreach (var (a, b, half) in _roads)
            best = Mathf.Min(best, ClosestOnSegment(q, a, b).DistanceTo(q) - half);
        return best;
    }

    static Vector3 NearestRoadPoint(Vector3 p)
    {
        var q = Flat(p);
        var best = q;
        float bestD = float.MaxValue;
        foreach (var (a, b, half) in _roads)
        {
            var c = ClosestOnSegment(q, a, b);
            float d = c.DistanceTo(q) - half;
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }
        return new Vector3(best.X, 0f, best.Y);
    }

    // Points along a route through the waypoints that wanders side to side (never a straight grid),
    // passing exactly through every waypoint.
    static List<Vector3> Meander(Vector3[] waypoints, float amp, float step = 5f)
    {
        var pts = new List<Vector3> { waypoints[0] };
        for (int w = 0; w < waypoints.Length - 1; w++)
        {
            var a = waypoints[w];
            var b = waypoints[w + 1];
            float len = a.DistanceTo(b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            var dir = (b - a) / len;
            var side = new Vector3(-dir.Z, 0f, dir.X);
            float k = _rng.RandfRange(0.8f, 1.6f), ph = _rng.Randf() * Mathf.Tau, ph2 = _rng.Randf() * Mathf.Tau;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                float off = amp * Mathf.Sin(Mathf.Pi * t) * (0.65f * Mathf.Sin(Mathf.Tau * t * k + ph) + 0.35f * Mathf.Sin(Mathf.Tau * t * (k + 1.7f) + ph2));
                pts.Add(a.Lerp(b, t) + side * off);
            }
        }
        return pts;
    }

    // Lays a dirt road along the points: rotated strips plus round patches at every bend.
    static void DrawRoad(Node3D parent, string name, List<Vector3> pts, float width, Color color, float baseY = 0f)
    {
        var g = Shapes.Group(parent, name);
        float y = baseY + 0.02f + (_roadCount++ % 12) * 0.0015f;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            var d = b - a;
            float len = new Vector2(d.X, d.Z).Length();
            if (len < 0.05f)
                continue;
            var mid = (a + b) * 0.5f;
            var strip = Shapes.Box(g, new Vector3(mid.X, y, mid.Z), new Vector3(width, 0.04f, len + 0.1f), color, 0f, "Strip");
            strip.Rotation = new Vector3(0f, Mathf.Atan2(d.X, d.Z), 0f);
            Shapes.MapTag(strip, "map_road", new Vector2(width, len));
            if (i > 0)
                Shapes.Cylinder(g, new Vector3(a.X, y + 0.0005f, a.Z), width * 0.5f, width * 0.5f, 0.04f, color, 0f, "Bend");
            AddRoad(a, b, width * 0.5f);
        }
    }

    // ---------- houses ----------

    enum HouseKind { StiltKalae, Stilt, HutGranary, Twin, Ground, Hut }

    static float HouseRadius(HouseKind k) => k switch
    {
        HouseKind.StiltKalae or HouseKind.Stilt => 7f,
        HouseKind.Twin => 7.5f,
        HouseKind.HutGranary => 6.5f,
        HouseKind.Ground => 5.5f,
        _ => 4.2f,
    };

    // How far in front of its centre a house's door/stairs reach.
    static float HouseFront(HouseKind k) => k switch
    {
        HouseKind.StiltKalae or HouseKind.Stilt => 4.1f + StairRun(2.2f, 0f),
        HouseKind.Twin => 3.7f + StairRun(2.1f, 0f),
        HouseKind.Ground => 4.2f,
        _ => 2.1f + StairRun(1.1f, 0f),
    };

    static HouseKind RandomHouse() => (HouseKind)_rng.RandiRange(0, 5);

    // Builds a house facing the nearest road with a driveway out to it, if the spot is free.
    static bool TryHouse(Node3D parent, string name, Vector3 pos, HouseKind kind, float groundY = 0f, bool driveway = true)
    {
        float r = HouseRadius(kind);
        if (Blocked(pos, r) || DistToRoads(pos) < r + 0.5f)
            return false;
        var q = NearestRoadPoint(pos);
        var to = q - new Vector3(pos.X, 0f, pos.Z);
        var dir = to.LengthSquared() > 0.01f ? to.Normalized() : Vector3.Forward;
        float yaw = Mathf.Atan2(-dir.X, -dir.Z);
        var at = new Vector3(pos.X, groundY, pos.Z);
        switch (kind)
        {
            case HouseKind.StiltKalae: StiltHouse(parent, name, at, yaw, 2.2f, true); break;
            case HouseKind.Stilt: StiltHouse(parent, name, at, yaw, 2.2f, false); break;
            case HouseKind.Twin: TwinHouse(parent, name, at, yaw); break;
            case HouseKind.Ground: GroundHouse(parent, name, at, yaw); break;
            case HouseKind.Hut: Hut(parent, name, at, yaw); break;
            default:
                var right = new Vector3(dir.Z, 0f, -dir.X);
                Hut(parent, name, at - right * 1.8f, yaw);
                Granary(parent, name + "_Granary", at + right * 3.4f + dir * 0.6f, yaw);
                break;
        }
        Occupy(pos, r);
        if (driveway)
        {
            var start = at + dir * (HouseFront(kind) - 0.3f);
            if (Flat(start).DistanceTo(Flat(q)) > 0.8f)
                DrawRoad(parent, name + "_Path", new List<Vector3> { start, new(q.X, groundY, q.Z) }, 1.8f, Dirt.Darkened(0.05f), groundY);
        }
        return true;
    }

    // เรือนไม้ติดดิน: a ground-level wooden house with a tiled roof, a porch and windows on every side.
    static void GroundHouse(Node3D parent, string name, Vector3 pos, float yaw)
    {
        var h = Shapes.Group(parent, name, pos, yaw);
        Shapes.MapTag(h, "map_house", new Vector2(6.6f, 6.4f));
        var wood = Wood.Lightened(_rng.RandfRange(-0.06f, 0.06f));
        const float w = 6.2f, d = 5f, wallH = 2.7f, plinth = 0.3f;
        Shapes.Box(h, new Vector3(0f, plinth * 0.5f, -0.6f), new Vector3(w + 0.4f, plinth, d + 1.6f), Stone.Darkened(0.15f), 0f, "Plinth");
        Shapes.Solid(h, new Vector3(0f, plinth * 0.5f, -0.6f), new Vector3(w + 0.4f, plinth, d + 1.6f), "PlinthCollider");
        Shapes.Box(h, new Vector3(0f, plinth + wallH * 0.5f, 0f), new Vector3(w, wallH, d), wood, 0f, "Walls");
        Shapes.Solid(h, new Vector3(0f, plinth + wallH * 0.5f + 0.4f, 0f), new Vector3(w, wallH + 0.8f, d), "WallCollider");
        foreach (var z in new[] { -d * 0.5f - 0.01f, d * 0.5f + 0.01f })
            for (int i = -5; i <= 5; i++)
                Shapes.Box(h, new Vector3(i * 0.56f, plinth + wallH * 0.5f, z), new Vector3(0.05f, wallH, 0.02f), DarkWood, 0f, "Seam");
        Shapes.Box(h, new Vector3(0f, plinth + 1.05f, -d * 0.5f - 0.03f), new Vector3(1.1f, 2.1f, 0.04f), new Color(0.14f, 0.09f, 0.06f), 0f, "Door");
        bool lit = _rng.Randf() < 0.7f;
        var glass = lit ? WindowGlow : new Color(0.08f, 0.06f, 0.05f);
        foreach (var x in new[] { -2f, 2f })
        {
            Shapes.Box(h, new Vector3(x, plinth + 1.45f, -d * 0.5f - 0.03f), new Vector3(1f, 0.8f, 0.04f), glass, lit ? 1.8f : 0f, lit ? "GlowWindow" : "Window");
            Shapes.Box(h, new Vector3(x, plinth + 1.45f, d * 0.5f + 0.03f), new Vector3(1f, 0.8f, 0.04f), new Color(0.08f, 0.06f, 0.05f), 0f, "WindowBack");
        }
        foreach (var s in new[] { -1f, 1f })
            Shapes.Box(h, new Vector3(s * (w * 0.5f + 0.03f), plinth + 1.45f, 0f), new Vector3(0.04f, 0.8f, 1.1f), glass, lit ? 1.4f : 0f, lit ? "GlowWindowSide" : "WindowSide");
        GableRoof(h, plinth + wallH, w, d + 0.6f, 0f, Tile.Darkened(_rng.RandfRange(0f, 0.2f)), wood, _rng.Randf() < 0.4f);
        // Porch: a lean-to roof on two posts over the front step.
        foreach (var x in new[] { -2.6f, 2.6f })
            Shapes.Box(h, new Vector3(x, plinth + 1.1f, -d * 0.5f - 1.3f), new Vector3(0.14f, 2.2f, 0.14f), DarkWood, 0f, "PorchPost");
        Shapes.Box(h, new Vector3(0f, plinth + 2.3f, -d * 0.5f - 0.75f), new Vector3(w + 0.2f, 0.1f, 1.7f), Thatch, 0f, "PorchRoof").Rotation = new Vector3(0.2f, 0f, 0f);
        Shapes.Box(h, new Vector3(-1.9f, plinth + 0.25f, -d * 0.5f - 0.9f), new Vector3(1.4f, 0.08f, 0.5f), DarkWood, 0f, "Bench");
        if (lit && _rng.Randf() < 0.4f)
            Shapes.Light(h, new Vector3(0f, plinth + 2f, -d * 0.5f - 1f), WindowGlow, 1.1f, 7f, "PorchLight");
    }

    // ---------- signposts ----------

    // A post with one arrow board per destination, pointing along each direction (local to the zone).
    static void SignPost(Node3D parent, Vector3 pos, params (string label, Vector3 dir)[] arms)
    {
        var post = Shapes.Group(parent, "SignPost", pos);
        Shapes.MapTag(post, "map_sign", new Vector2(1f, 1f));
        float height = 2.3f + arms.Length * 0.42f;
        Shapes.Box(post, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.16f, height, 0.16f), DarkWood, 0f, "Post");
        Shapes.Solid(post, new Vector3(0f, 1f, 0f), new Vector3(0.3f, 2f, 0.3f));
        for (int i = 0; i < arms.Length; i++)
        {
            var (label, dir) = arms[i];
            var d = new Vector3(dir.X, 0f, dir.Z).Normalized();
            var arm = Shapes.Group(post, $"Arm{i}", new Vector3(0f, height - 0.3f - i * 0.42f, 0f), Mathf.Atan2(-d.Z, d.X));
            float len = Mathf.Max(1.4f, label.Length * 0.085f + 0.4f);
            var board = new Color(0.55f, 0.42f, 0.26f).Lightened(i * 0.03f);
            Shapes.Box(arm, new Vector3(len * 0.5f + 0.1f, 0f, 0f), new Vector3(len, 0.34f, 0.06f), board, 0f, "Board");
            Shapes.Box(arm, new Vector3(len + 0.1f, 0f, 0f), new Vector3(0.25f, 0.25f, 0.06f), board, 0f, "Tip").Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.25f);
            foreach (var side in new[] { 1f, -1f })
                Shapes.Child(arm, new Label3D
                {
                    Text = label,
                    Font = Game.ThaiFont,
                    FontSize = 34,
                    PixelSize = 0.0042f,
                    Modulate = new Color(1f, 0.95f, 0.82f),
                    OutlineSize = 6,
                    OutlineModulate = new Color(0.15f, 0.1f, 0.06f),
                    DoubleSided = false,
                    Position = new Vector3(len * 0.5f + 0.1f, 0f, side * 0.04f),
                    Rotation = new Vector3(0f, side > 0f ? 0f : Mathf.Pi, 0f),
                }, "Text");
        }
    }

    static void CitySigns(Node3D city)
    {
        SignPost(city, new Vector3(-6.5f, 0f, -7.5f),
            ("ทุ่งนาหุ่นไล่กา (ประตูเหนือ)", new Vector3(0f, 0f, -1f)), ("ประตูทิศใต้ (ปิดอยู่)", new Vector3(0f, 0f, 1f)),
            ("โรงตีเหล็กลุงมั่น", new Vector3(-1f, 0f, 0f)), ("วัดกลางเวียง", new Vector3(1f, 0f, -0.4f)));
        SignPost(city, new Vector3(6f, 0f, -168f), ("ทุ่งนาหุ่นไล่กา", new Vector3(0f, 0f, -1f)), ("ลานศาลหลักเมือง", new Vector3(0f, 0f, 1f)));
    }

    // ---------- ทุ่งนา: roads, hamlets and farmhouses ----------

    static readonly Vector3[][] FieldMainRoads =
    {
        new Vector3[] { new(0f, 0f, 34f), new(-7f, 0f, 95f), new(5f, 0f, 145f), new(0f, 0f, 172f), new(0f, 0f, Half) },
        new Vector3[] { new(-11f, 0f, 0f), new(-55f, 0f, 22f), new(-118f, 0f, 38f), new(-Half + 6f, 0f, 58f) },
        new Vector3[] { new(11f, 0f, 0f), new(60f, 0f, 30f), new(122f, 0f, 44f), new(Half - 6f, 0f, 22f) },
    };

    static readonly Vector3[][] FieldLanes =
    {
        new Vector3[] { new(-55f, 0f, 22f), new(-72f, 0f, -30f), new(-100f, 0f, -62f) },
        new Vector3[] { new(60f, 0f, 30f), new(78f, 0f, -25f), new(68f, 0f, -60f) },
        new Vector3[] { new(-7f, 0f, 95f), new(-55f, 0f, 118f), new(-110f, 0f, 112f) },
        new Vector3[] { new(5f, 0f, 145f), new(70f, 0f, 168f), new(125f, 0f, 168f) },
        new Vector3[] { new(-118f, 0f, 38f), new(-142f, 0f, 90f), new(-112f, 0f, 104f) },
        new Vector3[] { new(122f, 0f, 44f), new(150f, 0f, 96f) },
        new Vector3[] { new(-118f, 0f, 38f), new(-152f, 0f, -20f), new(-150f, 0f, -80f) },
        new Vector3[] { new(122f, 0f, 44f), new(138f, 0f, -18f), new(130f, 0f, -70f) },
    };

    static void BuildFieldRoads(Node3D world)
    {
        var roads = Shapes.Group(world, "Roads");
        Sanctuary(world, Vector3.Zero, 30f);
        Sanctuary(world, new Vector3(0f, 0f, ThungNaShoreZ - 8f), 30f);
        foreach (var h in HamletCenters)
            Sanctuary(world, h, 32f);
        foreach (var c in ForestPatches)
            Occupy(c, 24f);
        _landmarks.Add((Vector2.Zero, 36f));
        _landmarks.Add((new Vector2(0f, ThungNaShoreZ - 5f), 34f));
        _landmarks.Add((new Vector2(0f, Half), 22f));
        foreach (var h in HamletCenters)
            _landmarks.Add((Flat(h), 30f));

        // The first stretch south stays straight past the bus stop.
        DrawRoad(roads, "RoadSouthStart", new List<Vector3> { new(0f, 0f, 11f), new(0f, 0f, 34f) }, 4f, Dirt);
        DrawRoad(roads, "RoadSouth", Meander(FieldMainRoads[0], 6f), 4f, Dirt);
        DrawRoad(roads, "RoadWest", Meander(FieldMainRoads[1], 10f), 3.6f, Dirt);
        DrawRoad(roads, "RoadEast", Meander(FieldMainRoads[2], 10f), 3.6f, Dirt);
        for (int i = 0; i < FieldLanes.Length; i++)
            DrawRoad(roads, $"Lane{i}", Meander(FieldLanes[i], 7f), 2.6f, Dirt.Lerp(GroundGreen, 0.2f));
        Occupy(new Vector3(5.2f, 0f, 26f), 3f);
        // Spots where villagers stand (data/npcs.json) stay clear of houses and trees.
        foreach (var n in GameData.Npcs.Values.Where(n => n.Zone == "thung_na"))
            Occupy(new Vector3(n.X, 0f, n.Z), 2.5f);
    }

    static bool InFieldBounds(Vector3 p, float r) =>
        Mathf.Abs(p.X) < Half - 8f - r && p.Z > ThungNaShoreZ + 8f + r && p.Z < Half - 8f - r;

    // Two hamlets with houses scattered (not in rings) around the main roads, plus lone farmhouses
    // along the lanes. Every house faces a road and has a path out to it.
    static void BuildHamlets(Node3D world)
    {
        var h = Shapes.Group(world, "Hamlets");
        for (int n = 0; n < HamletCenters.Length; n++)
        {
            var c = HamletCenters[n];
            var g = Shapes.Group(h, $"Hamlet{n}");
            if (n == 0)
            {
                var shrinePos = NearestRoadPoint(c + new Vector3(4f, 0f, -12f)) + new Vector3(0f, 0f, -6f);
                var shrine = Shapes.Group(g, "ShrineTaYai", shrinePos);
                PhraPhumShrine(shrine, "thung_na_west", "ศาลตายาย");
                Occupy(shrinePos, 3f);
                SignPost(g, shrinePos + new Vector3(3.2f, 0f, 2.5f), ("หมู่บ้านทุ่งนา", new Vector3(1f, 0f, -0.3f)), ("ป่าตะเคียนเก่า", new Vector3(0f, 0f, 1f)));
            }
            else
                SignPost(g, NearestRoadPoint(c) + new Vector3(0f, 0f, -3.5f), ("หมู่บ้านทุ่งนา", new Vector3(-1f, 0f, -0.3f)), ("ทุ่งตะวันออก", new Vector3(0.3f, 0f, 1f)));
            int built = 0;
            for (int attempt = 0; attempt < 80 && built < 7; attempt++)
            {
                float a = _rng.Randf() * Mathf.Tau, r = _rng.RandfRange(9f, 32f);
                var p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.8f);
                var kind = RandomHouse();
                if (InFieldBounds(p, HouseRadius(kind)) && TryHouse(g, $"House{built}", p, kind))
                    built++;
            }
            for (int i = 0; i < 3; i++)
            {
                var lp = NearestRoadPoint(c + new Vector3(_rng.RandfRange(-25f, 25f), 0f, _rng.RandfRange(-12f, 12f)));
                LanternPost(g, $"HamletLantern{i}", lp + new Vector3(2.2f, 0f, 1.6f), 2.6f, i == 0);
            }
        }

        var farms = Shapes.Group(world, "Farmhouses");
        int k = 0;
        foreach (var lane in FieldLanes.Concat(FieldMainRoads))
        {
            int want = 2;
            for (int attempt = 0; attempt < 24 && want > 0; attempt++)
            {
                int s = _rng.RandiRange(0, lane.Length - 2);
                var basePt = lane[s].Lerp(lane[s + 1], _rng.RandfRange(0.2f, 0.8f));
                var d = (lane[s + 1] - lane[s]).Normalized();
                var side = new Vector3(-d.Z, 0f, d.X) * (_rng.Randf() < 0.5f ? -1f : 1f);
                var p = basePt + side * _rng.RandfRange(13f, 18f);
                var kind = RandomHouse();
                if (!InFieldBounds(p, HouseRadius(kind)) || NearLandmark(p, -10f))
                    continue;
                if (TryHouse(farms, $"Farm{k}", p, kind))
                {
                    k++;
                    want--;
                }
            }
        }
    }

    // Clumps of trees across the open land so the fields feel overgrown, kept off roads, paddies,
    // houses and away from landmarks so those stay in view.
    static void BuildThickets(Node3D world, int clusters)
    {
        var root = Shapes.Group(world, "Thickets");
        int made = 0;
        for (int attempt = 0; attempt < clusters * 12 && made < clusters; attempt++)
        {
            var c = new Vector3(_rng.RandfRange(-Half + 14f, Half - 14f), 0f, _rng.RandfRange(ThungNaShoreZ + 14f, Half - 14f));
            if (DistToRoads(c) < 6f || Blocked(c, 6f) || NearLandmark(c) || _plots.Any(p => p.DistanceTo(c) < 11f))
                continue;
            var g = Shapes.Group(root, $"Thicket{made}", c);
            int trees = _rng.RandiRange(3, 7);
            for (int i = 0; i < trees; i++)
            {
                var p = c + new Vector3(_rng.RandfRange(-7f, 7f), 0f, _rng.RandfRange(-7f, 7f));
                if (DistToRoads(p) < 2.5f || _plots.Any(pl => Mathf.Abs(pl.X - p.X) < 7f && Mathf.Abs(pl.Z - p.Z) < 7f))
                    continue;
                float roll = _rng.Randf();
                var local = p - c;
                if (roll < 0.55f)
                    Tree(g, "Tree", local, _rng.RandfRange(0.9f, 1.4f));
                else if (roll < 0.8f)
                    DeadTree(g, "DeadTree", local, _rng.RandfRange(0.9f, 1.4f));
                else if (roll < 0.9f)
                    Banyan(g, "Banyan", local, _rng.RandfRange(0.6f, 0.85f), false);
                else
                    Stump(g, "Stump", local);
            }
            Occupy(c, 8f);
            made++;
        }
    }
}
