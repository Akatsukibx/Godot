using Godot;
using System.Collections.Generic;
using System.Linq;

// วัดร้างบนดอย (zone 5, Lv 45–58): a ruined temple on a mountain top east of the hospital.
// The ground is real terrain (a height map): a rugged peak whose summit is a flat temple terrace, and a
// rock outcrop to the south-east ringed by a deep ravine, reached only by a plank bridge, where the old
// mine is cut into the cliff. A dirt path winds up from the foot of the mountain past a ruined sala
// halfway up; there it forks, one way to the summit, the other down and round to the mine bridge.
// The summit: a mossy Khmer prang, a Thai ordination hall under moss-thick roofs, broken chedis, sema
// stones, a headless Buddha and banyan trees whose roots pour over the brick walls.
public static partial class WorldBuilder
{
    static readonly Color Laterite = new(0.5f, 0.3f, 0.22f);
    static readonly Color OldBrick = new(0.46f, 0.27f, 0.2f);
    static readonly Color Stucco = new(0.62f, 0.6f, 0.54f);
    static readonly Color DoiMoss = new(0.22f, 0.3f, 0.14f);
    static readonly Color DoiRock = new(0.4f, 0.4f, 0.38f);
    static readonly Color DoiPathColor = new(0.33f, 0.27f, 0.2f);

    // Summit terrace and the mine outcrop (local coords).
    static readonly Vector2 DoiSummit = new(20f, -50f);
    const float DoiSummitR = 34f, DoiPeak = 42f, DoiFall = 116f;
    static readonly Vector2 DoiOutcrop = new(130f, 85f);
    const float DoiOutcropR = 20f, DoiOutcropH = 24f, DoiMoatR = 28f;

    static readonly FastNoiseLite DoiNoise = new() { Seed = 5858, Frequency = 0.03f };

    // Ground height at local (x, z). Zero at every zone edge so it meets the neighbours and the
    // outer forest; flat on the summit terrace and the outcrop top; a deep ring of ravine round the outcrop.
    public static float DoiHeight(float x, float z)
    {
        var p = new Vector2(x, z);
        float d1 = p.DistanceTo(DoiSummit);
        float main = DoiPeak * (1f - Smooth01((d1 - DoiSummitR) / DoiFall));
        float d2 = p.DistanceTo(DoiOutcrop);
        float crop = DoiOutcropH * (1f - Smooth01((d2 - DoiOutcropR) / 40f));
        float h = Mathf.Max(main, crop);
        // Rugged slopes, but the two flat tops stay flat.
        float flat = Mathf.Max(Smooth01((DoiSummitR + 6f - d1) / 10f), Smooth01((DoiOutcropR + 4f - d2) / 6f));
        h += DoiNoise.GetNoise2D(x, z) * 4.5f * (1f - flat) * Mathf.Clamp(h / 6f, 0f, 1f);
        h -= 20f * Mathf.Exp(-Mathf.Pow((d2 - DoiMoatR) / 4.5f, 2f));
        float edge = Mathf.Min(Half - Mathf.Abs(x), Half - Mathf.Abs(z));
        return Mathf.Max(0f, h) * Smooth01(edge / 24f);
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    static Vector3 OnDoi(float x, float z, float lift = 0f) => new(x, DoiHeight(x, z) + lift, z);

    // Ground under a world point in any zone with terrain (0 elsewhere).
    public static float ZoneGround(ZoneDef zone, float wx, float wz)
    {
        if (zone == null)
            return 0f;
        float lx = wx - zone.CenterX, lz = wz - zone.CenterZ;
        return zone.Scene switch
        {
            "bueng_bua" => Mathf.Max(0f, BuengHeight(lx, lz)),
            "wat_doi" => DoiHeight(lx, lz),
            _ => 0f,
        };
    }

    // Paths (local): up from the west edge through switchbacks to the sala, on up to the summit gate;
    // and from the sala down and round to the bridgehead before the mine outcrop.
    static readonly Vector3[] DoiPathUp =
    {
        new(-Half, 0f, 20f), new(-150f, 0f, 26f), new(-112f, 0f, 34f), new(-84f, 0f, 14f), new(-60f, 0f, 44f),
        new(-28f, 0f, 48f), new(-4f, 0f, 36f), new(10f, 0f, 30f),
    };
    static readonly Vector3[] DoiPathTop = { new(10f, 0f, 30f), new(32f, 0f, 18f), new(40f, 0f, 2f), new(30f, 0f, -12f), new(24f, 0f, -22f) };
    static readonly Vector3[] DoiPathMine = { new(10f, 0f, 30f), new(36f, 0f, 58f), new(62f, 0f, 84f), new(82f, 0f, 78f), new(97f, 0f, 68f) };
    static Vector3 DoiBridgeA => OnDoi(98.5f, 67.8f);
    static Vector3 DoiBridgeB => OnDoi(115f, 77f);

    public static Node3D BuildWatDoi()
    {
        _rng = new RandomNumberGenerator { Seed = 45581 };
        ResetLayout();
        var world = new Node3D { Name = "Zone_WatDoi" };
        DoiTerrain(world);
        DoiPaths(world);
        DoiFootCamp(world);
        DoiSala(world);
        DoiSummitTemple(world);
        DoiMine(world);
        DoiRocks(world);
        DoiTrees(world);
        PlaceChests(world, "wat_doi", 5, p => DoiHeight(p.X, p.Y) > 3f && SlopeAt(p.X, p.Y) < 0.5f, p => DoiHeight(p.X, p.Y));
        DoiMist(world);
        return world;
    }

    static float SlopeAt(float x, float z)
    {
        float dx = DoiHeight(x + 1f, z) - DoiHeight(x - 1f, z), dz = DoiHeight(x, z + 1f) - DoiHeight(x, z - 1f);
        return new Vector2(dx, dz).Length() * 0.5f;
    }

    // ---------- terrain ----------

    static void DoiTerrain(Node3D world)
    {
        const float cell = 3f;
        int n = (int)(Half * 2f / cell);   // cells per side
        int v = n + 1;                      // vertices per side
        var hs = new float[v * v];
        for (int j = 0; j < v; j++)
        for (int i = 0; i < v; i++)
            hs[j * v + i] = DoiHeight(-Half + i * cell, -Half + j * cell);
        float H(int i, int j) => hs[Mathf.Clamp(j, 0, n) * v + Mathf.Clamp(i, 0, n)];
        Vector3 P(int i, int j) => new(-Half + i * cell, H(i, j), -Half + j * cell);
        Vector3 N(int i, int j) => new Vector3(H(i - 1, j) - H(i + 1, j), 2f * cell, H(i, j - 1) - H(i, j + 1)).Normalized();
        Color C(int i, int j)
        {
            float slope = 1f - N(i, j).Y;
            float n0 = DoiNoise.GetNoise2D(i * 7.1f, j * 7.1f);
            var grass = new Color(0.17f, 0.24f, 0.13f).Lerp(new Color(0.24f, 0.25f, 0.15f), Mathf.Clamp(n0 + 0.5f, 0f, 1f));
            var rock = DoiRock.Lerp(DoiMoss, Mathf.Clamp(n0 + 0.3f, 0f, 0.6f));
            return grass.Lerp(rock, Smooth01((slope - 0.12f) / 0.25f));
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
            foreach (var (a, b) in new[] { (i, j), (i + 1, j), (i + 1, j + 1), (i, j), (i + 1, j + 1), (i, j + 1) })
            {
                st.SetColor(C(a, b));
                st.SetNormal(N(a, b));
                st.AddVertex(P(a, b));
            }
        var ground = Shapes.Group(world, "Ground");
        Shapes.Child(ground, new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.97f },
        }, "Terrain");
        var body = Shapes.Child(ground, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "TerrainCollider");
        Shapes.Child(body, new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = v, MapDepth = v, MapData = hs },
            Scale = new Vector3(cell, 1f, cell),
        });
        Shapes.MapTag(Shapes.Group(ground, "MountainMap"), "map_grove", new Vector2(Half * 2f, Half * 2f));
    }

    // A dirt path that follows the ground: the usual road (for the map, grass and layout) with its
    // flat strip swapped for a ribbon draped over the terrain.
    static void DoiPath(Node3D parent, string name, Vector3[] waypoints, float width)
    {
        var pts = Meander(waypoints, 2.5f, 4f);
        DrawRoad(parent, name, pts, width, DoiPathColor);
        var g = parent.GetNode<Node3D>(name);
        if (g.GetNodeOrNull("Strip") is Node strip)
        {
            g.RemoveChild(strip);
            strip.Free();
        }
        // Resample every 1.5 m so the ribbon hugs the slope.
        var fine = new List<Vector3>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            int k = Mathf.Max(1, Mathf.CeilToInt(pts[i].DistanceTo(pts[i + 1]) / 1.5f));
            for (int s = 0; s < k; s++)
                fine.Add(pts[i].Lerp(pts[i + 1], s / (float)k));
        }
        fine.Add(pts[^1]);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Side(int i)
        {
            var d = (fine[Mathf.Min(i + 1, fine.Count - 1)] - fine[Mathf.Max(i - 1, 0)]) with { Y = 0f };
            return new Vector3(d.Z, 0f, -d.X).Normalized() * width * 0.5f;
        }
        Vector3 Lift(Vector3 p) => p with { Y = DoiHeight(p.X, p.Z) + 0.09f };
        for (int i = 0; i < fine.Count - 1; i++)
        {
            var l0 = Lift(fine[i] - Side(i)); var r0 = Lift(fine[i] + Side(i));
            var l1 = Lift(fine[i + 1] - Side(i + 1)); var r1 = Lift(fine[i + 1] + Side(i + 1));
            foreach (var p in new[] { l0, r0, r1, l0, r1, l1 })
                st.AddVertex(p);
        }
        st.GenerateNormals();
        Shapes.Child(g, new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Shapes.Material(DoiPathColor), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, "Strip");
    }

    static void DoiPaths(Node3D world)
    {
        var root = Shapes.Group(world, "Paths");
        DoiPath(root, "PathUp", DoiPathUp, 3f);
        DoiPath(root, "PathTop", DoiPathTop, 3f);
        DoiPath(root, "PathMine", DoiPathMine, 2.4f);
        // Sema-like boundary stones along the way up, leaning and moss-capped.
        for (int i = 2; i < DoiPathUp.Length; i++)
        {
            var p = DoiPathUp[i] + new Vector3(2.4f, 0f, 1.4f);
            var s = Shapes.Group(root, "PathStone", OnDoi(p.X, p.Z), _rng.Randf() * 3f);
            Shapes.Box(s, new Vector3(0f, 0.5f, 0f), new Vector3(0.4f, 1f, 0.18f), Stucco.Darkened(0.25f), 0f, "StoneMarker").Rotation = new Vector3(_rng.RandfRange(-0.15f, 0.15f), 0f, _rng.RandfRange(-0.15f, 0.15f));
            Shapes.Box(s, new Vector3(0f, 1.02f, 0f), new Vector3(0.42f, 0.08f, 0.2f), DoiMoss, 0f, "Moss");
        }
    }

    // ---------- the foot of the mountain: a wandering monk's camp (the safe place) ----------

    static void DoiFootCamp(Node3D world)
    {
        var c = OnDoi(-158f, 30f);
        var g = Shapes.Group(world, "MonkCamp", c);
        Sanctuary(world, c, 18f);
        _landmarks.Add((Flat(c), 14f));
        Occupy(c, 9f);
        // The klod: a big umbrella with its mosquito net hanging to the ground.
        var klod = Shapes.Group(g, "Klod", new Vector3(3f, 0f, -3f));
        Shapes.Cylinder(klod, new Vector3(0f, 1.2f, 0f), 0.03f, 0.03f, 2.4f, new Color(0.4f, 0.32f, 0.2f), 0f, "BambooPole");
        Shapes.Cylinder(klod, new Vector3(0f, 2.35f, 0f), 0.05f, 1.4f, 0.5f, new Color(0.62f, 0.45f, 0.22f), 0f, "ClothUmbrella");
        Shapes.Cylinder(klod, new Vector3(0f, 1.1f, 0f), 1.3f, 1.3f, 2.2f, new Color(0.85f, 0.85f, 0.8f, 0.35f), 0f, "ClothNet");
        Shapes.Box(klod, new Vector3(0f, 0.03f, 0f), new Vector3(1.8f, 0.04f, 1.8f), new Color(0.55f, 0.5f, 0.35f), 0f, "ClothMat");
        // Small fire, alms bowl, a water kettle on a stone.
        foreach (var a in new[] { 0f, 1.25f, 2.5f, 3.75f, 5f })
            Shapes.Sphere(g, new Vector3(Mathf.Cos(a) * 0.55f, 0.1f, Mathf.Sin(a) * 0.55f), 0.16f, Stone, 0f, "FireStone");
        Shapes.Sphere(g, new Vector3(0f, 0.22f, 0f), 0.16f, new Color(1f, 0.5f, 0.2f), 4f, "GlowEmber");
        Shapes.Light(g, new Vector3(0f, 1f, 0f), new Color(1f, 0.55f, 0.25f), 2.2f, 11f, "CampfireLight");
        Shapes.Sphere(g, new Vector3(1.4f, 0.22f, 1.2f), 0.22f, new Color(0.12f, 0.1f, 0.08f), 0f, "MetalBowl").Scale = new Vector3(1f, 0.8f, 1f);
        Shapes.Box(g, new Vector3(-2f, 0.25f, 1.5f), new Vector3(0.7f, 0.5f, 0.7f), Stone, 0f, "Stone");
        var shrine = Shapes.Group(world, "Shrine_wat_doi", OnDoi(-172f, 36f), 0.4f);
        PhraPhumShrine(shrine, "wat_doi", "ศาลเชิงดอย");
        Occupy(shrine.Position, 3f);
        SignPost(world, OnDoi(-140f, 22f), ("โรงพยาบาลร้าง", new Vector3(-1f, 0f, 0f)), ("วัดร้างบนดอย", new Vector3(1f, 0f, 0.2f)));
        LanternPost(world, "CampLantern", OnDoi(-150f, 22f), 2.4f, true, OnDoi(-150f, 26f));
    }

    // ---------- the sala halfway up, where the path forks ----------

    static void DoiSala(Node3D world)
    {
        var p = OnDoi(14f, 38f);
        var s = Shapes.Group(world, "HalfwaySala", p, 0.3f);
        _landmarks.Add((Flat(p), 8f));
        Occupy(p, 5f);
        Shrine(s, "wat_doi_sala", "ศาลากลางทางขึ้นดอย");
        var wood = RotWood.Darkened(0.1f);
        Shapes.Box(s, new Vector3(0f, 0.3f, 0f), new Vector3(5f, 0.6f, 4f), Laterite.Darkened(0.2f), 0f, "Plinth");
        Shapes.Solid(s, new Vector3(0f, 0.3f, 0f), new Vector3(5f, 0.6f, 4f));
        foreach (var x in new[] { -2.2f, 2.2f })
        foreach (var z in new[] { -1.7f, 1.7f })
        {
            var post = Shapes.Box(s, new Vector3(x, 1.9f, z), new Vector3(0.22f, 2.6f, 0.22f), wood, 0f, "Post");
            post.Rotation = new Vector3(_rng.RandfRange(-0.05f, 0.05f), 0f, _rng.RandfRange(-0.05f, 0.05f));
        }
        // Two-tier roof sagging under moss, one corner fallen.
        var r1 = Shapes.Prism(s, new Vector3(0f, 3.7f, 0f), new Vector3(6f, 1.4f, 5f), DoiMoss.Darkened(0.1f), "MossRoof");
        r1.Rotation = new Vector3(0f, Mathf.Pi * 0.5f, 0.08f);
        Shapes.Prism(s, new Vector3(0f, 4.6f, 0f), new Vector3(3.6f, 1f, 3f), DoiMoss, "MossRoof").Rotation = new Vector3(0f, Mathf.Pi * 0.5f, 0.08f);
        Shapes.Box(s, new Vector3(0f, 0.75f, 1.2f), new Vector3(3.4f, 0.1f, 0.6f), wood, 0f, "Bench");
        // Old bell hanging from the beam, offerings left by someone, a spirit lamp.
        Shapes.Cylinder(s, new Vector3(0f, 2.9f, 0f), 0.18f, 0.32f, 0.5f, new Color(0.35f, 0.3f, 0.18f), 0f, "MetalBell");
        for (int i = 0; i < 4; i++)
            Shapes.Cylinder(s, new Vector3(-1.4f + i * 0.4f, 0.68f, -1.3f), 0.1f, 0.07f, 0.12f, new Color(0.85f, 0.82f, 0.72f), 0f, "OfferingBowl");
        Shapes.Sphere(s, new Vector3(1.6f, 0.75f, -1.3f), 0.07f, new Color(1f, 0.6f, 0.3f), 4f, "GlowCandle");
        Shapes.Light(s, new Vector3(0f, 2.2f, 0f), new Color(1f, 0.65f, 0.35f), 1.4f, 9f, "SalaLight");
        SignPost(world, OnDoi(4f, 28f), ("วัดร้างบนดอย", new Vector3(0.7f, 0f, -0.7f)), ("เหมืองร้าง", new Vector3(0.7f, 0f, 0.7f)), ("เชิงดอย", new Vector3(-1f, 0f, 0.2f)));
    }

    // ---------- the summit ----------

    static void DoiSummitTemple(Node3D world)
    {
        var top = new Vector3(DoiSummit.X, DoiPeak, DoiSummit.Y);
        var g = Shapes.Group(world, "SummitTemple", top);
        _landmarks.Add((Flat(top), DoiSummitR - 2f));
        _hospBody = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
        var b = new Batch();
        Shapes.MapTag(Shapes.Group(g, "TempleMap"), "map_temple", new Vector2(52f, 52f));

        // Broken laterite enclosure wall, a gate gap on the south where the path comes in.
        void WallRun(Vector3 a, Vector3 c)
        {
            var d = c - a;
            float len = d.Length();
            var dir = d / len;
            var basis = new Basis(Vector3.Up, Mathf.Atan2(dir.X, dir.Z));
            for (float t = 0f; t < len; t += 2f)
            {
                if (_rng.Randf() < 0.18f)
                    continue; // fallen section
                float h = _rng.RandfRange(0.6f, 1.6f);
                var at = a + dir * (t + 1f);
                Solid(b, "WallBrick", Laterite.Darkened(_rng.RandfRange(0f, 0.2f)), at + Vector3.Up * h * 0.5f, new Vector3(0.8f, h, 2f));
                if (_rng.Randf() < 0.4f)
                    b.Box("Moss", DoiMoss, at + Vector3.Up * (h + 0.05f), new Vector3(0.9f, 0.12f, 1.6f), basis);
            }
        }
        const float w = 23f;
        WallRun(new Vector3(-w, 0f, -w), new Vector3(w, 0f, -w));
        WallRun(new Vector3(-w, 0f, -w), new Vector3(-w, 0f, w));
        WallRun(new Vector3(w, 0f, -w), new Vector3(w, 0f, w));
        WallRun(new Vector3(-w, 0f, w), new Vector3(-2f, 0f, w));
        WallRun(new Vector3(8f, 0f, w), new Vector3(w, 0f, w));
        // Paving inside, cracked and grassy.
        b.Box("CourtFloor", Stucco.Darkened(0.35f), new Vector3(0f, 0.05f, 0f), new Vector3(w * 2f, 0.1f, w * 2f));
        for (int i = 0; i < 50; i++)
            b.Box("Moss", DoiMoss.Lightened(_rng.RandfRange(0f, 0.08f)), new Vector3(_rng.RandfRange(-w, w), 0.12f, _rng.RandfRange(-w, w)), new Vector3(_rng.RandfRange(0.8f, 3f), 0.05f, _rng.RandfRange(0.8f, 3f)));

        DoiUbosot(b, g, new Vector3(-8f, 0f, -4f));
        DoiPrang(b, g, new Vector3(14f, 0f, -12f));
        foreach (var (p, s) in new[] { (new Vector3(16f, 0f, 12f), 1f), (new Vector3(-18f, 0f, 16f), 0.8f), (new Vector3(22f, 0f, 4f), 0.7f) })
            DoiChedi(b, p, s);
        DoiBuddha(b, g, new Vector3(4f, 0f, 14f));
        // Buried jars of temple treasure, half dug out: the treasure ghosts haunt them.
        for (int i = 0; i < 6; i++)
        {
            var p = new Vector3(_rng.RandfRange(-20f, 20f), 0f, _rng.RandfRange(4f, 22f));
            b.Box("Clay", Clay.Darkened(0.2f), p + Vector3.Up * 0.3f, new Vector3(0.6f, 0.6f, 0.6f), new Basis(Vector3.Forward, _rng.RandfRange(-0.5f, 0.5f)));
            b.Box("Mud", new Color(0.25f, 0.2f, 0.15f), p + Vector3.Up * 0.06f, new Vector3(1.6f, 0.12f, 1.6f));
            if (i % 2 == 0)
                Shapes.Sphere(g, p + new Vector3(0.5f, 0.15f, 0.3f), 0.08f, new Color(1f, 0.8f, 0.3f), 2f, "GlowGold");
        }
        // Naga balustrade either side of the gate.
        foreach (var x in new[] { -2.5f, 8.5f })
        {
            b.Box("Stucco", Stucco.Darkened(0.2f), new Vector3(x, 0.5f, w + 3f), new Vector3(0.6f, 0.6f, 6f));
            b.Box("Stucco", Stucco.Darkened(0.15f), new Vector3(x, 1.3f, w + 6.2f), new Vector3(0.6f, 1.6f, 0.8f), new Basis(Vector3.Right, -0.4f));
        }
        Shapes.Light(g, new Vector3(0f, 6f, 0f), new Color(0.6f, 0.7f, 1f), 1.2f, 30f, "MoonWash");
        b.Commit(g);
    }

    // The ordination hall: a raised base, cracked walls, a porch, and two tiers of moss-thick roof.
    static void DoiUbosot(Batch b, Node3D g, Vector3 c)
    {
        const float L = 20f, W = 9f, baseH = 1.2f, wallH = 5f;
        Solid(b, "Plinth", Laterite.Darkened(0.15f), c + Vector3.Up * baseH * 0.5f, new Vector3(L + 2f, baseH, W + 2f));
        // Steps up at the east end.
        for (int i = 0; i < 4; i++)
            b.Box("Step", Laterite.Darkened(0.1f), c + new Vector3(L * 0.5f + 1.4f + (3 - i) * 0.4f, 0.15f + i * 0.3f, 0f), new Vector3(0.4f, 0.3f, 3f));
        Col(c + new Vector3(L * 0.5f + 2f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 3f), new Basis(Vector3.Forward, 0.6f));
        float y0 = baseH;
        // Walls with window slits, the west end cracked open.
        foreach (var s in new[] { -1f, 1f })
        {
            for (float x = -L * 0.5f; x < L * 0.5f; x += 2.5f)
            {
                bool window = ((int)((x + 10f) / 2.5f)) % 2 == 1;
                b.Box("WallPlaster", Stucco.Darkened(_rng.RandfRange(0f, 0.15f)), c + new Vector3(x + 1.25f, y0 + (window ? 0.8f : wallH * 0.5f), s * W * 0.5f), new Vector3(2.5f, window ? 1.6f : wallH, 0.5f));
                if (window)
                {
                    b.Box("WallPlaster", Stucco.Darkened(0.1f), c + new Vector3(x + 1.25f, y0 + 4.2f, s * W * 0.5f), new Vector3(2.5f, 1.6f, 0.5f));
                    b.Box("WindowDark", new Color(0.04f, 0.04f, 0.04f), c + new Vector3(x + 1.25f, y0 + 2.5f, s * W * 0.5f), new Vector3(1f, 1.8f, 0.52f));
                }
                if (_rng.Randf() < 0.3f)
                    b.Box("GrimeInk", HospGrime, c + new Vector3(x + 1.25f, y0 + 2f, s * (W * 0.5f + 0.27f)), new Vector3(1.2f, 2.5f, 0.02f));
            }
            Col(c + new Vector3(0f, y0 + wallH * 0.5f, s * W * 0.5f), new Vector3(L, wallH, 0.5f));
        }
        b.Box("WallPlaster", Stucco.Darkened(0.12f), c + new Vector3(-L * 0.5f, y0 + wallH * 0.5f, -2.5f), new Vector3(0.5f, wallH, 4f));
        b.Box("WallPlaster", Stucco.Darkened(0.12f), c + new Vector3(-L * 0.5f, y0 + 1.5f, 2.5f), new Vector3(0.5f, 3f, 4f));
        Col(c + new Vector3(-L * 0.5f, y0 + wallH * 0.5f, 0f), new Vector3(0.5f, wallH, W));
        foreach (var z in new[] { -3f, 3f })
            Solid(b, "WallPlaster", Stucco, c + new Vector3(L * 0.5f, y0 + wallH * 0.5f, z), new Vector3(0.5f, wallH, 3f));
        b.Box("WindowDark", new Color(0.03f, 0.03f, 0.03f), c + new Vector3(L * 0.5f + 0.02f, y0 + 1.6f, 0f), new Vector3(0.48f, 3.2f, 3f));
        // Porch columns and roof tiers.
        foreach (var z in new[] { -3.5f, 3.5f })
            b.Box("Stucco", Stucco.Darkened(0.05f), c + new Vector3(L * 0.5f + 2.6f, y0 + 2.4f, z), new Vector3(0.6f, 4.8f, 0.6f));
        foreach (var s in new[] { -1f, 1f })
        {
            var tilt = new Basis(Vector3.Right, s * 0.62f);
            b.Box("MossRoof", DoiMoss.Darkened(0.05f), c + new Vector3(0f, y0 + wallH + 1.6f, s * 2.6f), new Vector3(L + 3f, 0.35f, 6.4f), tilt);
            b.Box("MossRoof", DoiMoss, c + new Vector3(0.5f, y0 + wallH + 3.4f, s * 1.4f), new Vector3(L - 2f, 0.35f, 3.6f), tilt);
            // Thick moss clumps on the slopes.
            for (int i = 0; i < 6; i++)
                b.Box("Moss", DoiMoss.Lightened(0.06f), c + new Vector3(_rng.RandfRange(-L * 0.45f, L * 0.45f), y0 + wallH + 1.9f, s * 2.4f), new Vector3(1.6f, 0.4f, 1.6f), tilt);
        }
        // Gable ends with the stucco pediment, ridge finials.
        foreach (var x in new[] { -L * 0.5f - 1.3f, L * 0.5f + 1.3f })
            b.Box("Stucco", Stucco.Darkened(0.2f), c + new Vector3(x, y0 + wallH + 2.2f, 0f), new Vector3(0.3f, 3.4f, 4f), new Basis(Vector3.Right, 0.785f));
        b.Box("Stucco", Stucco.Darkened(0.25f), c + new Vector3(0f, y0 + wallH + 4.4f, 0f), new Vector3(L, 0.3f, 0.4f));
        // Inside: the main Buddha image, dark and broken, a few candles still burning.
        b.Box("Plinth", Laterite, c + new Vector3(-L * 0.5f + 2.5f, y0 + 0.8f, 0f), new Vector3(2.6f, 1.6f, 4f));
        b.Box("Stucco", Stucco.Darkened(0.3f), c + new Vector3(-L * 0.5f + 2.5f, y0 + 2.6f, 0f), new Vector3(1.6f, 2.2f, 2.4f));
        Shapes.Sphere(g, c + new Vector3(-L * 0.5f + 2.5f, y0 + 4f, 0f), 0.55f, Stucco.Darkened(0.3f), 0f, "StoneHead");
        Shapes.Light(g, c + new Vector3(-L * 0.5f + 5f, y0 + 1.5f, 0f), new Color(1f, 0.65f, 0.35f), 1.4f, 9f, "CandleLight");
        Occupy(g.Position + c, 13f);
    }

    // The Khmer prang: stepped base, a tall body with false doors, and a corncob crown of shrinking tiers.
    static void DoiPrang(Batch b, Node3D g, Vector3 c)
    {
        float y = 0f;
        foreach (var (size, h) in new[] { (11f, 1.2f), (9f, 1.2f), (7f, 1f) })
        {
            Solid(b, "WallBrick", Laterite.Darkened(0.05f), c + Vector3.Up * (y + h * 0.5f), new Vector3(size, h, size));
            y += h;
        }
        // Steps up the south face to the doorway (the boss lair is in front).
        for (int i = 0; i < 6; i++)
            b.Box("Step", Laterite.Darkened(0.12f), c + new Vector3(0f, 0.2f + i * 0.4f, 6.4f - i * 0.35f), new Vector3(2.6f, 0.4f, 0.4f));
        Solid(b, "WallBrick", OldBrick, c + Vector3.Up * (y + 4f), new Vector3(5.4f, 8f, 5.4f));
        foreach (var (d, yaw) in new[] { (Vector3.Back, 0f), (Vector3.Forward, Mathf.Pi), (Vector3.Right, Mathf.Pi * 0.5f), (Vector3.Left, -Mathf.Pi * 0.5f) })
        {
            var face = c + d * 2.75f + Vector3.Up * (y + 2.4f);
            var rot = new Basis(Vector3.Up, yaw);
            b.Box("WallBrick", OldBrick.Lightened(0.05f), face + d * 0.4f, (rot * new Vector3(2.4f, 4.8f, 0.8f)).Abs(), Basis.Identity);
            b.Box(d == Vector3.Back ? "WindowDark" : "WallBrick", d == Vector3.Back ? new Color(0.03f, 0.03f, 0.03f) : OldBrick.Darkened(0.2f), face + d * 0.82f, (rot * new Vector3(1.2f, 3f, 0.05f)).Abs(), Basis.Identity);
            b.Box("Stucco", Stucco.Darkened(0.3f), face + d * 0.85f + Vector3.Up * 2.8f, (rot * new Vector3(2f, 1.2f, 0.1f)).Abs(), Basis.Identity);
        }
        y += 8f;
        // The corncob crown: shrinking tiers, the top broken off.
        float s = 5f;
        for (int i = 0; i < 6; i++)
        {
            b.Box("WallBrick", OldBrick.Darkened(i * 0.03f), c + Vector3.Up * (y + 0.7f), new Vector3(s, 1.4f, s));
            foreach (var d in new[] { Vector3.Back, Vector3.Forward, Vector3.Right, Vector3.Left })
                b.Box("WallBrick", OldBrick.Lightened(0.04f), c + d * (s * 0.5f) + Vector3.Up * (y + 1.5f), new Vector3(0.6f, 0.6f, 0.6f));
            y += 1.4f;
            s *= 0.82f;
        }
        b.Box("Rubble", OldBrick.Darkened(0.2f), c + Vector3.Up * (y + 0.3f) + new Vector3(0.4f, 0f, 0f), new Vector3(1.2f, 0.6f, 1f), new Basis(Vector3.Forward, 0.4f));
        // Moss and roots over everything.
        for (int i = 0; i < 26; i++)
        {
            var p = c + new Vector3(_rng.RandfRange(-3f, 3f), _rng.RandfRange(1f, y), _rng.RandfRange(-3f, 3f));
            var outward = new Vector3(p.X - c.X, 0f, p.Z - c.Z).Normalized();
            float r = Mathf.Lerp(3f, 1.2f, (p.Y - c.Y) / y);
            b.Box("Moss", DoiMoss.Lightened(_rng.RandfRange(-0.05f, 0.08f)), new Vector3(c.X, p.Y, c.Z) + outward * r, new Vector3(_rng.RandfRange(0.6f, 1.6f), _rng.RandfRange(0.3f, 0.9f), _rng.RandfRange(0.6f, 1.6f)));
        }
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.Tau;
            var top = c + new Vector3(Mathf.Cos(a) * 2.6f, 12f, Mathf.Sin(a) * 2.6f);
            RootSegmentThick(g, top, c + new Vector3(Mathf.Cos(a) * 6.5f, 0f, Mathf.Sin(a) * 6.5f), new Color(0.3f, 0.28f, 0.22f), 0.14f);
        }
        Shapes.Light(g, c + new Vector3(0f, 3.4f, 4.5f), new Color(1f, 0.25f, 0.2f), 1.4f, 12f, "LairLight");
        Occupy(g.Position + c, 8f);
    }

    // A small bell-shaped chedi, broken open at the top, brick showing through the stucco.
    static void DoiChedi(Batch b, Vector3 c, float s)
    {
        Solid(b, "WallBrick", OldBrick.Darkened(0.05f), c + Vector3.Up * 0.6f * s, new Vector3(4f, 1.2f, 4f) * s);
        b.Box("Stucco", Stucco.Darkened(0.2f), c + Vector3.Up * 1.6f * s, new Vector3(3.2f, 0.8f, 3.2f) * s);
        b.Box("Stucco", Stucco.Darkened(0.15f), c + Vector3.Up * 3f * s, new Vector3(2.6f, 2f, 2.6f) * s, new Basis(Vector3.Up, 0.785f));
        b.Box("WallBrick", OldBrick, c + Vector3.Up * 4.4f * s, new Vector3(1.4f, 0.8f, 1.4f) * s);
        b.Box("WallBrick", OldBrick.Darkened(0.1f), c + new Vector3(0.2f, 5.1f, 0f) * s, new Vector3(0.8f, 0.8f, 0.8f) * s, new Basis(Vector3.Forward, 0.3f));
        b.Box("Moss", DoiMoss, c + Vector3.Up * 2.05f * s, new Vector3(3.3f, 0.15f, 3.3f) * s);
        Occupy(c, 3f * s);
    }

    // A seated Buddha whose head has fallen and lies beside him.
    static void DoiBuddha(Batch b, Node3D g, Vector3 c)
    {
        Solid(b, "Plinth", Laterite.Darkened(0.1f), c + Vector3.Up * 0.6f, new Vector3(3.4f, 1.2f, 2.6f));
        b.Box("Stucco", Stucco.Darkened(0.25f), c + Vector3.Up * 1.6f, new Vector3(3f, 0.8f, 2.2f));
        b.Box("Stucco", Stucco.Darkened(0.2f), c + Vector3.Up * 2.8f, new Vector3(1.8f, 1.8f, 1.2f));
        b.Box("Stucco", Stucco.Darkened(0.2f), c + new Vector3(0f, 2.2f, -0.6f), new Vector3(1.4f, 0.3f, 0.6f));
        Shapes.Sphere(g, c + new Vector3(2.4f, 0.45f, -0.8f), 0.45f, Stucco.Darkened(0.3f), 0f, "StoneHead");
        b.Box("Moss", DoiMoss, c + Vector3.Up * 3.75f, new Vector3(1.2f, 0.12f, 0.8f));
        b.Box("ClothSash", new Color(0.8f, 0.55f, 0.15f), c + new Vector3(0f, 2.6f, -0.62f), new Vector3(1.85f, 0.25f, 0.05f), new Basis(Vector3.Back, 0.4f));
        Occupy(c, 2.5f);
    }

    // ---------- the mine on the outcrop across the ravine ----------

    static void DoiMine(Node3D world)
    {
        var g = Shapes.Group(world, "AbandonedMine");
        _hospBody = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
        var b = new Batch();
        // Plank bridge over the ravine on two rope rails, a few boards missing.
        var a = DoiBridgeA + Vector3.Up * 0.1f;
        var c = DoiBridgeB + Vector3.Up * 0.1f;
        Ramp(b, a - (c - a).Normalized() * 1f, c + (c - a).Normalized() * 1f, 2.2f, RotWood.Lightened(0.05f));
        var d = c - a;
        var dirFlat = new Vector3(d.X, 0f, d.Z).Normalized();
        var side = new Vector3(dirFlat.Z, 0f, -dirFlat.X);
        foreach (var s in new[] { -1f, 1f })
        {
            var mid = (a + c) * 0.5f + side * s * 1.2f + Vector3.Up * 0.6f;
            var f = d.Normalized();
            var basis = new Basis(f.Cross(Vector3.Up).Normalized(), f.Cross(Vector3.Up).Normalized().Cross(f), f);
            b.Box("Rope", new Color(0.5f, 0.42f, 0.3f), mid, new Vector3(0.05f, 0.05f, d.Length()), basis);
            Col(mid, new Vector3(0.15f, 1.6f, d.Length()), basis);
            foreach (var end in new[] { a, c })
                b.Box("Post", RotDark, end + side * s * 1.3f + Vector3.Up * 0.7f, new Vector3(0.2f, 1.4f, 0.2f));
        }
        Shapes.MapTag(Shapes.Group(g, "BridgeMap", (a + c) * 0.5f, Mathf.Atan2(d.X, d.Z)), "map_road", new Vector2(2.2f, new Vector2(d.X, d.Z).Length()));

        // The cliff on the outcrop: a wall of stacked rock with the adit cut into it, facing the bridge.
        var face = new Vector3(DoiOutcrop.X + 9f, 0f, DoiOutcrop.Y + 6f);
        float baseY = DoiHeight(face.X, face.Z);
        var toward = (new Vector3(DoiBridgeB.X, 0f, DoiBridgeB.Z) - face).Normalized();
        float yaw = Mathf.Atan2(toward.X, toward.Z);
        var rot = new Basis(Vector3.Up, yaw);
        Vector3 F(float x, float y, float z) => new Vector3(face.X, baseY, face.Z) + rot * new Vector3(x, y, z);
        for (int i = 0; i < 22; i++)
        {
            float x = _rng.RandfRange(-11f, 11f);
            if (Mathf.Abs(x) < 2.6f && i % 3 != 0)
                continue;
            float y = _rng.RandfRange(0f, 10f);
            if (Mathf.Abs(x) < 2.6f && y < 4.4f)
                y = 4.4f + _rng.RandfRange(0f, 3f);
            var size = new Vector3(_rng.RandfRange(3f, 6f), _rng.RandfRange(2.5f, 5f), _rng.RandfRange(3f, 5f));
            b.Box("Rock", DoiRock.Darkened(_rng.RandfRange(0f, 0.25f)), F(x, y + size.Y * 0.5f, -size.Z * 0.5f - 0.5f), size, rot * new Basis(Vector3.Up, _rng.RandfRange(-0.3f, 0.3f)));
        }
        foreach (var x in new[] { -7f, 7f })
            Col(F(x, 6f, -3f), new Vector3(9f, 12f, 6f), rot);
        Col(F(0f, 9f, -3f), new Vector3(5f, 6f, 6f), rot);
        Col(F(0f, 2f, -5.6f), new Vector3(5f, 4f, 0.6f), rot); // the back of the adit
        // Timber frame round the adit.
        foreach (var x in new[] { -1.8f, 1.8f })
        {
            b.Box("WoodBeam", RotWood, F(x, 2f, 0f), new Vector3(0.35f, 4f, 0.35f), rot * new Basis(Vector3.Back, x * 0.02f));
            b.Box("WoodBeam", RotWood.Darkened(0.1f), F(x * 1.15f, 1.6f, 0.8f), new Vector3(0.25f, 3.6f, 0.25f), rot * new Basis(Vector3.Right, -0.35f));
        }
        b.Box("WoodBeam", RotWood, F(0f, 4.1f, 0f), new Vector3(4.4f, 0.4f, 0.45f), rot);
        b.Box("WoodBeam", RotWood.Darkened(0.15f), F(0.4f, 3.7f, 0.1f), new Vector3(3.6f, 0.25f, 0.3f), rot * new Basis(Vector3.Back, 0.12f));
        b.Box("WindowDark", new Color(0.02f, 0.02f, 0.02f), F(0f, 2f, -2.5f), new Vector3(3.3f, 3.9f, 5f), rot);
        // Rails out of the mouth, an ore cart tipped on its side, a sign and a lantern.
        foreach (var x in new[] { -0.55f, 0.55f })
            b.Box("MetalRail", HospRust, F(x, 0.08f, 2f), new Vector3(0.08f, 0.08f, 9f), rot);
        for (int i = 0; i < 9; i++)
            b.Box("WoodBeam", RotDark, F(0f, 0.05f, -1.5f + i), new Vector3(1.6f, 0.1f, 0.25f), rot);
        b.Box("MetalCart", HospRust.Darkened(0.1f), F(1.6f, 0.6f, 5.5f), new Vector3(1.2f, 0.9f, 1.6f), rot * new Basis(Vector3.Forward, 1.3f));
        b.Box("Rock", DoiRock.Darkened(0.3f), F(2.6f, 0.25f, 5.5f), new Vector3(1f, 0.5f, 1.2f), rot);
        Col(F(1.6f, 0.6f, 5.5f), new Vector3(1.4f, 1.2f, 1.8f), rot);
        var sign = Shapes.Group(g, "MineSign", F(-2.6f, 2.4f, 0.4f), yaw + Mathf.Pi);
        Shapes.Box(sign, Vector3.Zero, new Vector3(1.8f, 0.8f, 0.06f), new Color(0.55f, 0.42f, 0.26f), 0f, "SignBoard");
        HospLabel(sign, "เหมืองร้าง\nอันตราย ห้ามเข้า", new Vector3(0f, 0f, -0.04f), Mathf.Pi, 26, new Color(0.6f, 0.08f, 0.06f));
        FlickerLight(g, F(1.9f, 3.2f, 0.6f), new Color(1f, 0.6f, 0.3f), 1.4f, 10f);
        Shapes.Sphere(g, F(1.9f, 3.2f, 0.6f), 0.12f, new Color(1f, 0.6f, 0.3f), 3f, "GlowLantern");
        // The way in: a party dungeon of its own (see Main: dungeon gates).
        var gate = Shapes.Group(g, "DungeonGate_mueang_rang", F(0f, 0f, -0.8f), yaw);
        gate.AddToGroup("dungeon_gate", true);
        gate.SetMeta("zone_id", "mueang_rang");
        Shapes.MapTag(Shapes.Group(g, "MineMap", F(0f, 0f, -3f), yaw), "map_wall", new Vector2(22f, 7f));
        Occupy(new Vector3(face.X, 0f, face.Z), 9f);
        _landmarks.Add((Flat(DoiBridgeB), 8f));
        b.Commit(g);
    }

    // Boulders and bare rock faces scattered over the steep ground, thicker on the ravine walls.
    static void DoiRocks(Node3D world)
    {
        var g = Shapes.Group(world, "Rocks");
        var b = new Batch();
        _hospBody = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
        int made = 0;
        for (int i = 0; i < 900 && made < 140; i++)
        {
            float x = _rng.RandfRange(-Half + 20f, Half - 20f), z = _rng.RandfRange(-Half + 20f, Half - 20f);
            var p = new Vector3(x, 0f, z);
            float slope = SlopeAt(x, z);
            if (slope < 0.35f && _rng.Randf() > 0.15f || DistToRoads(p) < 2.5f || NearLandmark(p, 3f) || Blocked(p, 1.5f))
                continue;
            float r = _rng.RandfRange(0.8f, slope > 0.8f ? 3.6f : 2f);
            var at = OnDoi(x, z, r * 0.3f);
            var basis = new Basis(new Vector3(_rng.RandfRange(-1f, 1f), 1f, _rng.RandfRange(-1f, 1f)).Normalized(), _rng.RandfRange(0f, 1f));
            var size = new Vector3(r * 2f, r * _rng.RandfRange(1f, 1.6f), r * 1.6f);
            b.Box("Rock", DoiRock.Darkened(_rng.RandfRange(0f, 0.25f)), at, size, basis);
            if (_rng.Randf() < 0.5f)
                b.Box("Moss", DoiMoss, at + Vector3.Up * size.Y * 0.4f, new Vector3(size.X * 0.7f, 0.2f, size.Z * 0.7f), basis);
            if (r > 1.2f)
                Col(at, size * 0.85f, basis);
            Occupy(p, r);
            made++;
        }
        b.Commit(g);
    }

    // Banyan / strangler figs with buttress roots and hanging aerial roots, the biggest at the summit
    // where they grip the walls; the rest of the forest is scattered at runtime.
    static void DoiTrees(Node3D world)
    {
        var g = Shapes.Group(world, "Banyans");
        var spots = new List<Vector3>
        {
            new(DoiSummit.X - 27f, 0f, DoiSummit.Y - 20f), new(DoiSummit.X + 28f, 0f, DoiSummit.Y + 18f),
            new(DoiSummit.X - 24f, 0f, DoiSummit.Y + 24f), new(DoiSummit.X + 26f, 0f, DoiSummit.Y - 26f),
            new(20f, 0f, 44f), new(-40f, 0f, 30f), new(-96f, 0f, 22f), new(60f, 0f, 70f),
        };
        for (int i = 0; i < 40 && spots.Count < 22; i++)
        {
            var p = new Vector3(_rng.RandfRange(-Half + 20f, Half - 20f), 0f, _rng.RandfRange(-Half + 20f, Half - 20f));
            if (!Blocked(p, 4f) && DistToRoads(p) > 4f && !NearLandmark(p, 4f) && SlopeAt(p.X, p.Z) < 0.6f)
                spots.Add(p);
        }
        int k = 0;
        foreach (var p in spots)
            Banyan(g, $"Banyan{k++}", OnDoi(p.X, p.Z, -0.3f), k <= 4 ? 1.5f : _rng.RandfRange(1f, 1.3f));
    }

    static void Banyan(Node3D parent, string name, Vector3 p, float s)
    {
        var t = Shapes.Group(parent, name, p, _rng.Randf() * Mathf.Tau);
        var bark = new Color(0.32f, 0.3f, 0.24f);
        float h = 8f * s;
        // A trunk of several twisted stems fused together.
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * Mathf.Tau;
            var foot = new Vector3(Mathf.Cos(a) * 0.6f * s, 0f, Mathf.Sin(a) * 0.6f * s);
            RootSegmentThick(t, foot, foot * 0.3f + Vector3.Up * h, bark, 0.42f * s);
        }
        // Buttress roots running out over the ground, aerial roots hanging from the branches.
        for (int i = 0; i < 7; i++)
        {
            float a = _rng.Randf() * Mathf.Tau;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var mid = dir * 2f * s + Vector3.Up * 0.3f;
            RootSegmentThick(t, Vector3.Up * 1.6f * s, mid, bark, 0.26f * s);
            RootSegmentThick(t, mid, dir * _rng.RandfRange(4f, 6f) * s + Vector3.Down * 0.4f, bark.Darkened(0.1f), 0.14f * s);
        }
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * Mathf.Tau + _rng.Randf();
            var tip = new Vector3(Mathf.Cos(a) * 4.5f * s, h + _rng.RandfRange(0.5f, 2f) * s, Mathf.Sin(a) * 4.5f * s);
            RootSegmentThick(t, Vector3.Up * h * 0.8f, tip, bark, 0.2f * s);
            for (int r = 0; r < 3; r++)
            {
                var at = tip * _rng.RandfRange(0.5f, 1f);
                Shapes.Box(t, at with { Y = at.Y * 0.5f }, new Vector3(0.06f, at.Y, 0.06f), bark.Lightened(0.05f), 0f, "VineRoot");
            }
            Shapes.Sphere(t, tip + Vector3.Up * 1.2f * s, 2.8f * s, DoiMoss.Lightened(_rng.RandfRange(0f, 0.08f)), 0f, "Crown").Scale = new Vector3(1f, 0.6f, 1f);
        }
        Shapes.Sphere(t, Vector3.Up * (h + 2f * s), 3.6f * s, DoiMoss.Darkened(0.05f), 0f, "Crown").Scale = new Vector3(1.2f, 0.6f, 1.2f);
        for (int i = 0; i < 5; i++)
            Shapes.Box(t, new Vector3(_rng.RandfRange(-0.6f, 0.6f), _rng.RandfRange(0.5f, h), _rng.RandfRange(-0.6f, 0.6f)) * 1f, new Vector3(0.7f, 0.5f, 0.7f) * s, DoiMoss.Lightened(0.05f), 0f, "Moss");
        Shapes.Solid(t, Vector3.Up * h * 0.5f, new Vector3(1.6f * s, h, 1.6f * s));
        Occupy(p, 3f * s);
    }

    // Mist pooling in the valleys and the ravine; a thinner band clinging to the slopes.
    static void DoiMist(Node3D world)
    {
        var g = Shapes.Group(world, "Mist");
        for (float x = -Half + 40f; x < Half; x += 80f)
        for (float z = -Half + 40f; z < Half; z += 80f)
        {
            float h = DoiHeight(x, z);
            Shapes.Child(g, new FogVolume
            {
                Position = new Vector3(x, h + 2f, z),
                Size = new Vector3(85f, 6f, 85f),
                Material = new FogMaterial { Density = 0.05f, Albedo = new Color(0.62f, 0.64f, 0.68f) },
            }, "Mist");
        }
        Shapes.Child(g, new FogVolume
        {
            Position = new Vector3(DoiOutcrop.X, 6f, DoiOutcrop.Y),
            Size = new Vector3(70f, 14f, 70f),
            Material = new FogMaterial { Density = 0.08f, Albedo = new Color(0.6f, 0.62f, 0.66f) },
        }, "RavineMist");
    }
}
