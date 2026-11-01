using Godot;
using System.Collections.Generic;
using System.Linq;

// โรงพยาบาลร้าง (zone 4, Lv 35–45): a three-storey hospital swallowed by the jungle, south of ป่าไผ่ทึบ.
// The grounds: a rusted gate between two mossy guardian statues, a cracked forecourt with a wrecked
// ambulance and overturned stretchers. Inside, every floor is a long corridor of rooms (ER, morgue,
// operating theatres, wards, ICU, nursery) full of broken glass, toppled beds and old blood.
// The main staircase in the lobby has collapsed, so the upper floors are reached the long way round:
//   • ground → 1st: the overgrown fire escape outside the west end, or the ventilation duct that
//     climbs the east wall from the plant room;
//   • 1st → 2nd: the inner fire stair at the east end, behind a door locked with a key from the morgue
//     (the morgue itself is locked; its key comes from the nurses' quest or off a ผีพยาบาล).
// The 2nd floor's west half is the maternity ward under a broken roof: the boss's lair.
// Walls, floors and furniture are merged into one mesh per material per storey (see Batch) because the
// building is made of thousands of boxes.
public static partial class WorldBuilder
{
    static readonly Color HospPlaster = new(0.68f, 0.66f, 0.6f);
    static readonly Color HospInner = new(0.7f, 0.72f, 0.68f);
    static readonly Color HospBand = new(0.45f, 0.52f, 0.5f);
    static readonly Color HospBrick = new(0.52f, 0.27f, 0.2f);
    static readonly Color HospFloor = new(0.55f, 0.55f, 0.52f);
    static readonly Color HospMetal = new(0.62f, 0.66f, 0.68f);
    static readonly Color HospRust = new(0.42f, 0.26f, 0.16f);
    static readonly Color HospSheet = new(0.78f, 0.76f, 0.7f);
    static readonly Color HospBlood = new(0.32f, 0.04f, 0.04f);
    static readonly Color HospGlass = new(0.55f, 0.7f, 0.72f, 0.45f);
    static readonly Color HospGrime = new(0.18f, 0.18f, 0.15f, 0.55f);

    // The building in zone coordinates and its size. Everything inside is built relative to BO.
    static readonly Vector3 BO = new(0f, 0f, 20f);
    const float BW = 46f, BD = 12f, Corr = 2.5f, FH = 7f, Slab = 0.4f, WallT = 0.4f;

    // Holes cut in the floors (building coords): the collapsed main stair in the lobby and the fire stair.
    static readonly Rect2 MainStairHole = new(-5f, 3f, 10f, 8.6f);
    static readonly Rect2 FireStairHole = new(30.4f, -11.8f, 13.6f, 3.4f);
    static readonly Rect2 RoofHole = new(-40f, -6f, 22f, 12f);

    // Merges boxes into one mesh per (name, colour), so a storey is a handful of draw calls.
    sealed class Batch
    {
        readonly Dictionary<(string, Color), SurfaceTool> _st = new();

        static readonly (Vector3 n, Vector3 u, Vector3 v)[] Faces =
        {
            (Vector3.Right, Vector3.Up, Vector3.Back), (Vector3.Left, Vector3.Back, Vector3.Up),
            (Vector3.Up, Vector3.Back, Vector3.Right), (Vector3.Down, Vector3.Right, Vector3.Back),
            (Vector3.Back, Vector3.Right, Vector3.Up), (Vector3.Forward, Vector3.Up, Vector3.Right),
        };

        public void Box(string name, Color c, Vector3 center, Vector3 size, Basis? basis = null)
        {
            if (!_st.TryGetValue((name, c), out var st))
            {
                st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                _st[(name, c)] = st;
            }
            var b = basis ?? Basis.Identity;
            var h = size * 0.5f;
            foreach (var (n, u, v) in Faces)
            {
                var nn = n * h.Dot(n.Abs());
                var uu = u * h.Dot(u.Abs());
                var vv = v * h.Dot(v.Abs());
                var p0 = center + b * (nn - uu - vv);
                var p1 = center + b * (nn + uu - vv);
                var p2 = center + b * (nn + uu + vv);
                var p3 = center + b * (nn - uu + vv);
                var normal = (b * n).Normalized();
                // Godot draws clockwise faces: (p0, p2, p1) and (p0, p3, p2).
                foreach (var p in new[] { p0, p2, p1, p0, p3, p2 })
                {
                    st.SetNormal(normal);
                    st.AddVertex(p);
                }
            }
        }

        public void Commit(Node3D parent)
        {
            foreach (var ((name, c), st) in _st)
                Shapes.Child(parent, new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Shapes.Material(c) }, name);
            _st.Clear();
        }
    }

    // Box colliders for one storey, all on one body.
    static StaticBody3D _hospBody;

    static void Col(Vector3 center, Vector3 size, Basis? basis = null) =>
        Shapes.Child(_hospBody, new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = new Transform3D(basis ?? Basis.Identity, center) });

    static void Solid(Batch b, string name, Color c, Vector3 center, Vector3 size)
    {
        b.Box(name, c, center, size);
        Col(center, size);
    }

    // Zone-local point for a building-local one.
    static Vector3 HZ(float x, float y, float z) => BO + new Vector3(x, y, z);

    // Static road layout (zone coords): in from ป่าไผ่ทึบ in the north down to the gate, and a lane
    // from ทุ่งนาหุ่นไล่กา in the west to the broken side fence.
    static readonly Vector3[][] HospRoads =
    {
        new Vector3[] { new(40f, 0f, -Half), new(46f, 0f, -150f), new(30f, 0f, -110f), new(12f, 0f, -70f), new(0f, 0f, -36f) },
        new Vector3[] { new(-Half, 0f, 30f), new(-140f, 0f, 24f), new(-104f, 0f, 32f), new(-78f, 0f, 30f) },
    };

    public static Node3D BuildHospital()
    {
        _rng = new RandomNumberGenerator { Seed = 35451 };
        ResetLayout();
        var world = new Node3D { Name = "Zone_Hospital" };
        HospGround(world);
        HospRoadsBuild(world);
        HospCamp(world);
        HospGrounds(world);
        HospBuilding(world);
        HospBackYard(world);
        HospWilds(world);
        Puddles(world, 30, p => !InHospital(p, 6f));
        PlaceChests(world, "rong_phayaban", 5, p => !InHospital(p, 8f));
        HospMist(world);
        return world;
    }

    static bool InHospital(Vector2 p, float pad) =>
        Mathf.Abs(p.X - BO.X) < BW + 4f + pad && Mathf.Abs(p.Y - BO.Z) < BD + pad;

    // Overgrown earth and old lawn: a vertex-coloured sheet with a flat collider.
    static void HospGround(Node3D world)
    {
        var noise = new FastNoiseLite { Seed = 4545, Frequency = 0.025f };
        const float cell = 6f;
        int n = (int)(Half * 2f / cell);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Color C(float x, float z)
        {
            float v = noise.GetNoise2D(x, z);
            return new Color(0.2f, 0.25f, 0.15f).Lerp(new Color(0.27f, 0.25f, 0.18f), Mathf.Clamp(v + 0.5f, 0f, 1f)).Darkened(Mathf.Clamp(-v, 0f, 0.3f));
        }
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            float x0 = -Half + i * cell, z0 = -Half + j * cell, x1 = x0 + cell, z1 = z0 + cell;
            foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z0), (x1, z1), (x0, z1) })
            {
                st.SetColor(C(x, z));
                st.SetNormal(Vector3.Up);
                st.AddVertex(new Vector3(x, 0f, z));
            }
        }
        var ground = Shapes.Group(world, "Ground");
        Shapes.Child(ground, new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.97f },
        }, "Terrain");
        var body = Shapes.Child(ground, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "TerrainCollider");
        Shapes.Child(body, new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Half * 2f, 1f, Half * 2f) }, Position = new Vector3(0f, -0.5f, 0f) });
        Shapes.MapTag(Shapes.Group(ground, "JungleMap"), "map_grove", new Vector2(Half * 2f, Half * 2f));
    }

    static void HospRoadsBuild(Node3D world)
    {
        var root = Shapes.Group(world, "Roads");
        int k = 0;
        foreach (var r in HospRoads)
            DrawRoad(root, $"OldRoad{k++}", Meander(r, 2.5f, 5f), 4.2f, new Color(0.36f, 0.35f, 0.32f));
        // Old asphalt, cracked: the strips take the road texture.
        foreach (var s in root.FindChildren("Strip*", "MeshInstance3D", true, false))
            s.Name = "Strip";
    }

    // ---------- the old guard post on the road in: the only safe place ----------

    static void HospCamp(Node3D world)
    {
        var c = new Vector3(56f, 0f, -126f);
        var g = Shapes.Group(world, "GuardPost", c);
        Sanctuary(world, c, 18f);
        _landmarks.Add((Flat(c), 14f));
        Occupy(c, 9f);
        // A concrete guard booth with a striped barrier arm, now somebody's home.
        var booth = Shapes.Group(g, "Booth", new Vector3(4f, 0f, 2f), 0.2f);
        Shapes.Box(booth, new Vector3(0f, 1.3f, 0f), new Vector3(3f, 2.6f, 2.6f), HospPlaster.Darkened(0.1f), 0f, "WallPlaster");
        Shapes.Box(booth, new Vector3(0f, 2.7f, 0f), new Vector3(3.6f, 0.2f, 3.2f), HospPlaster.Darkened(0.25f), 0f, "RoofSlab");
        Shapes.Box(booth, new Vector3(0f, 1.6f, -1.31f), new Vector3(2f, 0.9f, 0.04f), new Color(0.1f, 0.12f, 0.12f), 0f, "WindowDark");
        Shapes.Box(booth, new Vector3(-1.51f, 1.1f, 0.3f), new Vector3(0.04f, 2f, 0.9f), new Color(0.05f, 0.05f, 0.05f), 0f, "WindowDoor");
        Shapes.Solid(booth, new Vector3(0f, 1.3f, 0f), new Vector3(3f, 2.6f, 2.6f));
        var arm = Shapes.Box(g, new Vector3(-3f, 1.1f, -3f), new Vector3(5f, 0.12f, 0.12f), new Color(0.85f, 0.2f, 0.15f), 0f, "MetalBarrier");
        arm.Rotation = new Vector3(0f, 0.2f, 0.5f);
        Shapes.Box(g, new Vector3(-5.2f, 0.55f, -3f), new Vector3(0.3f, 1.1f, 0.3f), HospPlaster, 0f, "Plinth");
        // Tarp lean-to, a cot and a fire in an oil drum.
        var tarp = Shapes.Box(g, new Vector3(-2f, 1.6f, 4f), new Vector3(4f, 0.04f, 3f), new Color(0.24f, 0.32f, 0.42f), 0f, "ClothTarp");
        tarp.Rotation = new Vector3(0.35f, 0f, 0f);
        foreach (var x in new[] { -3.8f, -0.2f })
            Shapes.Cylinder(g, new Vector3(x, 1.1f, 2.7f), 0.05f, 0.05f, 2.2f, HospMetal, 0f, "MetalPole");
        Shapes.Box(g, new Vector3(-2f, 0.35f, 4.4f), new Vector3(2f, 0.1f, 0.8f), HospSheet, 0f, "ClothCot");
        Shapes.Cylinder(g, new Vector3(1f, 0.45f, -1.5f), 0.32f, 0.32f, 0.9f, HospRust, 0f, "MetalDrum");
        Shapes.Sphere(g, new Vector3(1f, 0.95f, -1.5f), 0.22f, new Color(1f, 0.5f, 0.2f), 4f, "GlowEmber");
        Shapes.Light(g, new Vector3(1f, 1.6f, -1.5f), new Color(1f, 0.55f, 0.25f), 2.4f, 12f, "FireLight");
        SmallProp(g, "CampSacks", new Vector3(-4.5f, 0f, 1f), 0.4f, Prop.Sacks);
        var shrine = Shapes.Group(world, "Shrine_rong_phayaban", new Vector3(70f, 0f, -134f), -0.5f);
        PhraPhumShrine(shrine, "rong_phayaban", "ศาลพระภูมิหน้าโรงพยาบาล");
        Occupy(new Vector3(70f, 0f, -134f), 3f);
        SignPost(world, new Vector3(40f, 0f, -142f), ("ป่าไผ่ทึบ", new Vector3(0f, 0f, -1f)), ("โรงพยาบาลร้าง", new Vector3(-0.3f, 0f, 1f)));
        SignPost(world, new Vector3(-120f, 0f, 34f), ("ทุ่งนาหุ่นไล่กา", new Vector3(-1f, 0f, 0f)), ("โรงพยาบาลร้าง", new Vector3(1f, 0f, 0f)));
        LanternPost(world, "GuardLantern", new Vector3(46f, 0f, -118f), 2.6f, true, new Vector3(40f, 0f, -118f));
    }

    // ---------- the grounds: gate, statues, forecourt, ambulance, fences ----------

    static void HospGrounds(Node3D world)
    {
        var g = Shapes.Group(world, "Grounds");
        var b = new Batch();
        _hospBody = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "GroundsColliders");
        // Cracked concrete forecourt with weeds in the joints.
        var court = new Vector3(0f, 0f, -14f);
        b.Box("CourtFloor", HospFloor.Darkened(0.12f), court + new Vector3(0f, 0.03f, 0f), new Vector3(120f, 0.06f, 40f));
        Shapes.MapTag(Shapes.Group(g, "CourtMap", court), "map_road", new Vector2(120f, 40f));
        Occupy(court, 4f);
        for (int i = 0; i < 70; i++)
        {
            var p = court + new Vector3(_rng.RandfRange(-58f, 58f), 0.07f, _rng.RandfRange(-19f, 19f));
            b.Box("CrackInk", new Color(0.12f, 0.12f, 0.1f), p, new Vector3(_rng.RandfRange(1f, 4f), 0.01f, 0.08f), new Basis(Vector3.Up, _rng.Randf() * Mathf.Tau));
            if (i % 2 == 0)
                b.Box("WeedLeaf", VineGreen.Lightened(0.08f), p + new Vector3(0.5f, 0.1f, 0f), new Vector3(_rng.RandfRange(0.4f, 1.4f), 0.2f, _rng.RandfRange(0.3f, 1f)));
        }
        // Parking bays.
        for (int i = 0; i < 9; i++)
            b.Box("PaintLine", new Color(0.75f, 0.72f, 0.6f), new Vector3(28f + i * 3f, 0.075f, -26f), new Vector3(0.12f, 0.01f, 5f));

        // Fence of rusted iron railings round the grounds, broken in places; the front gate hangs open.
        void Fence(Vector3 a, Vector3 c)
        {
            var d = c - a;
            float len = d.Length();
            var dir = d / len;
            var basis = new Basis(Vector3.Up, Mathf.Atan2(dir.X, dir.Z));
            var mid = (a + c) * 0.5f;
            b.Box("MetalRail", HospRust, mid + Vector3.Up * 2f, new Vector3(0.08f, 0.08f, len), basis);
            b.Box("MetalRail", HospRust, mid + Vector3.Up * 0.3f, new Vector3(0.08f, 0.08f, len), basis);
            for (float t = 0f; t <= len; t += 0.45f)
                b.Box("MetalBar", HospRust, a + dir * t + Vector3.Up * 1.15f, new Vector3(0.04f, 2.3f, 0.04f));
            for (float t = 0f; t <= len; t += 4f)
                b.Box("WallPlaster", HospPlaster.Darkened(0.2f), a + dir * t + Vector3.Up * 1.3f, new Vector3(0.5f, 2.6f, 0.5f));
            Col(mid + Vector3.Up * 1.3f, new Vector3(0.3f, 2.6f, len), basis);
            Shapes.MapTag(Shapes.Group(g, "FenceMap", mid, basis.GetEuler().Y), "map_wall", new Vector2(0.6f, len));
        }
        const float fx = 76f, fz0 = -34f, fz1 = 72f;
        Fence(new Vector3(-fx, 0f, fz0), new Vector3(-8f, 0f, fz0));
        Fence(new Vector3(8f, 0f, fz0), new Vector3(fx, 0f, fz0));
        Fence(new Vector3(-fx, 0f, fz0), new Vector3(-fx, 0f, 22f));   // west, broken where the lane comes in
        Fence(new Vector3(-fx, 0f, 38f), new Vector3(-fx, 0f, fz1));
        Fence(new Vector3(fx, 0f, fz0), new Vector3(fx, 0f, 10f));     // east, a gap into the jungle
        Fence(new Vector3(fx, 0f, 24f), new Vector3(fx, 0f, fz1));
        Fence(new Vector3(-fx, 0f, fz1), new Vector3(-20f, 0f, fz1));
        Fence(new Vector3(-6f, 0f, fz1), new Vector3(fx, 0f, fz1));
        // Gate pillars and the two leaves, one hanging off its hinge.
        foreach (var s in new[] { -1f, 1f })
        {
            Solid(b, "WallPlaster", HospPlaster.Darkened(0.15f), new Vector3(s * 8f, 1.8f, fz0), new Vector3(1.2f, 3.6f, 1.2f));
            b.Box("WallPlaster", HospPlaster.Darkened(0.25f), new Vector3(s * 8f, 3.7f, fz0), new Vector3(1.5f, 0.25f, 1.5f));
            var leaf = new Basis(Vector3.Up, s * (s < 0f ? 1.2f : 0.5f)) * new Basis(Vector3.Forward, s > 0f ? 0.12f : 0f);
            var hinge = new Vector3(s * 7.3f, 0f, fz0);
            var center = hinge + leaf * new Vector3(-s * 3.4f, 1.2f, 0f);
            b.Box("MetalGate", HospRust.Darkened(0.1f), center, new Vector3(6.8f, 2.4f, 0.1f), leaf);
            Col(center, new Vector3(6.8f, 2.4f, 0.3f), leaf);
            // "Restricted area" boards wired to the gate.
            var board = Shapes.Group(g, $"RestrictedSign{(s < 0f ? "L" : "R")}", center + leaf * new Vector3(0f, 0.3f, -0.12f), leaf.GetEuler().Y);
            Shapes.Box(board, Vector3.Zero, new Vector3(1.6f, 1f, 0.05f), new Color(0.85f, 0.82f, 0.75f), 0f, "SignBoard");
            HospLabel(board, "เขตหวงห้าม\nRESTRICTED AREA", new Vector3(0f, 0f, -0.04f), Mathf.Pi, 30, new Color(0.65f, 0.08f, 0.06f));
            // Guardian statues: mossy stone thep with hands joined, on tall pedestals either side.
            HospStatue(g, new Vector3(s * 12f, 0f, fz0 - 1f));
        }
        Occupy(new Vector3(0f, 0f, fz0), 14f);

        // The wrecked ambulance, two stretchers, a wheelchair, broken glass and a rotten bench.
        HospAmbulance(g, new Vector3(34f, 0f, -6f), 2.6f);
        foreach (var (p, yaw, over) in new[] { (new Vector3(-6f, 0f, -24f), 0.4f, true), (new Vector3(5f, 0f, -14f), 2.2f, false), (new Vector3(-22f, 0f, -8f), 1.1f, true) })
            Stretcher(b, p, yaw, over);
        Wheelchair(b, new Vector3(12f, 0f, -22f), 0.8f, true);
        for (int i = 0; i < 40; i++)
            GlassShards(b, new Vector3(_rng.RandfRange(-40f, 40f), 0.08f, _rng.RandfRange(-30f, 4f)));
        foreach (var x in new[] { -30f, -40f })
        {
            b.Box("WoodBench", DarkWood, new Vector3(x, 0.45f, -4f), new Vector3(2.2f, 0.08f, 0.6f), new Basis(Vector3.Forward, x < -35f ? 0.3f : 0f));
            Col(new Vector3(x, 0.4f, -4f), new Vector3(2.2f, 0.8f, 0.6f));
        }
        // Lamp posts along the drive, all dead but one that flickers.
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector3(i % 2 == 0 ? -14f : 14f, 0f, -30f + i * 8f);
            b.Box("MetalPole", HospMetal.Darkened(0.3f), p + Vector3.Up * 2.5f, new Vector3(0.14f, 5f, 0.14f));
            b.Box("MetalPole", HospMetal.Darkened(0.3f), p + new Vector3(0f, 5f, 0.5f), new Vector3(0.1f, 0.1f, 1f));
            Col(p + Vector3.Up * 2.5f, new Vector3(0.3f, 5f, 0.3f));
            if (i == 2)
                FlickerLight(g, p + new Vector3(0f, 4.8f, 1f), new Color(0.8f, 0.95f, 0.85f), 1.6f, 13f);
        }
        b.Commit(g);
    }

    static void HospStatue(Node3D parent, Vector3 p)
    {
        var s = Shapes.Group(parent, "GuardianStatue", p);
        var stone = new Color(0.36f, 0.4f, 0.34f);
        Shapes.Box(s, new Vector3(0f, 0.9f, 0f), new Vector3(1.6f, 1.8f, 1.6f), stone.Darkened(0.15f), 0f, "StonePlinth");
        Shapes.Cylinder(s, new Vector3(0f, 2.6f, 0f), 0.32f, 0.5f, 1.6f, stone, 0f, "StoneBody");
        Shapes.Box(s, new Vector3(0f, 3.4f, 0f), new Vector3(0.9f, 0.6f, 0.5f), stone, 0f, "StoneShoulders");
        Shapes.Sphere(s, new Vector3(0f, 3.95f, 0f), 0.28f, stone, 0f, "StoneHead");
        var crown = Shapes.Cylinder(s, new Vector3(0f, 4.45f, 0f), 0.02f, 0.2f, 0.75f, stone.Lightened(0.05f), 0f, "StoneCrown");
        crown.Rotation = Vector3.Zero;
        foreach (var x in new[] { -0.12f, 0.12f })
            Shapes.Box(s, new Vector3(x, 3.25f, -0.32f), new Vector3(0.14f, 0.5f, 0.12f), stone, 0f, "StoneHands").Rotation = new Vector3(0f, 0f, x > 0f ? 0.2f : -0.2f);
        for (int i = 0; i < 6; i++)
            Shapes.Box(s, new Vector3(_rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(1.5f, 4f), _rng.RandfRange(-0.45f, 0.45f)), new Vector3(0.35f, 0.25f, 0.35f), VineGreen.Lightened(0.05f), 0f, "Moss");
        Shapes.Solid(s, new Vector3(0f, 2.2f, 0f), new Vector3(1.6f, 4.4f, 1.6f));
        Occupy(p, 1.5f);
    }

    static void HospAmbulance(Node3D parent, Vector3 p, float yaw)
    {
        var a = Shapes.Group(parent, "Ambulance", p, yaw);
        var white = new Color(0.82f, 0.8f, 0.74f);
        Shapes.Box(a, new Vector3(0f, 1.55f, 0.6f), new Vector3(2.3f, 2.2f, 4f), white, 0f, "MetalBody");
        Shapes.Box(a, new Vector3(0f, 1.15f, -2.3f), new Vector3(2.2f, 1.4f, 1.8f), white, 0f, "MetalCab");
        Shapes.Box(a, new Vector3(0f, 1.55f, -3.15f), new Vector3(1.9f, 0.7f, 0.05f), new Color(0.15f, 0.2f, 0.2f, 0.8f), 0f, "WindowGlass");
        Shapes.Box(a, new Vector3(0f, 1.2f, 0.6f), new Vector3(2.34f, 0.3f, 4.02f), new Color(0.75f, 0.12f, 0.1f), 0f, "MetalStripe");
        foreach (var s in new[] { -1f, 1f })
        {
            Shapes.Box(a, new Vector3(s * 1.17f, 2f, 0.6f), new Vector3(0.02f, 0.7f, 0.2f), new Color(0.75f, 0.12f, 0.1f), 0f, "MetalCross");
            Shapes.Box(a, new Vector3(s * 1.17f, 2f, 0.6f), new Vector3(0.02f, 0.2f, 0.7f), new Color(0.75f, 0.12f, 0.1f), 0f, "MetalCross");
            HospLabel(a, "รถพยาบาล", new Vector3(s * 1.19f, 1.65f, 1.9f), s * Mathf.Pi * 0.5f, 26, new Color(0.7f, 0.1f, 0.08f));
            foreach (var z in new[] { -2.2f, 1.6f })
                Shapes.Cylinder(a, new Vector3(s * 1.05f, 0.38f, z), 0.38f, 0.38f, 0.3f, new Color(0.08f, 0.08f, 0.08f), 0f, "MetalTyre").Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
        }
        Shapes.Box(a, new Vector3(0f, 2.75f, -1.3f), new Vector3(1.2f, 0.18f, 0.3f), new Color(0.8f, 0.15f, 0.12f), 0.6f, "GlowSiren");
        for (int i = 0; i < 4; i++)
            Shapes.Box(a, new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(0.4f, 2.4f), _rng.RandfRange(-1f, 2.5f)), new Vector3(0.6f, 0.4f, 0.5f), HospRust, 0f, "MetalRust");
        a.Rotation = new Vector3(0f, yaw, 0.06f); // one wheel sunk in the mud
        Shapes.Solid(a, new Vector3(0f, 1.3f, -0.2f), new Vector3(2.3f, 2.6f, 6f));
        Occupy(p, 4f);
    }

    // ---------- the building ----------

    static void HospBuilding(Node3D world)
    {
        var h = Shapes.Group(world, "Hospital", BO);
        Shapes.MapTag(Shapes.Group(h, "HospitalMap"), "map_house", new Vector2(BW * 2f + 8f, BD * 2f));
        Occupy(BO + new Vector3(-30f, 0f, 0f), 18f);
        Occupy(BO, 18f);
        Occupy(BO + new Vector3(30f, 0f, 0f), 18f);
        _landmarks.Add((Flat(BO), 52f));

        for (int floor = 0; floor < 3; floor++)
        {
            var fg = Shapes.Group(h, $"Floor{floor}");
            _hospBody = Shapes.Child(fg, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
            var b = new Batch();
            float y = floor * FH;
            HospSlab(b, floor, y);
            HospExterior(b, fg, floor, y);
            HospRooms(b, fg, floor, y);
            HospCorridorLights(fg, floor, y);
            b.Commit(fg);
        }
        // Roof: a slab with a parapet, holed over the maternity ward, and the big sign.
        {
            var rg = Shapes.Group(h, "Roof");
            _hospBody = Shapes.Child(rg, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
            var b = new Batch();
            float y = 3f * FH;
            foreach (var r in SlabPieces(new Rect2(-BW, -BD, BW * 2f, BD * 2f), new[] { RoofHole }))
                Solid(b, "RoofSlab", HospPlaster.Darkened(0.3f), new Vector3(r.GetCenter().X, y - Slab * 0.5f, r.GetCenter().Y), new Vector3(r.Size.X, Slab, r.Size.Y));
            foreach (var s in new[] { -1f, 1f })
            {
                b.Box("WallPlaster", HospPlaster.Darkened(0.1f), new Vector3(0f, y + 0.6f, s * (BD - 0.2f)), new Vector3(BW * 2f, 1.2f, 0.4f));
                b.Box("WallPlaster", HospPlaster.Darkened(0.1f), new Vector3(s * (BW - 0.2f), y + 0.6f, 0f), new Vector3(0.4f, 1.2f, BD * 2f));
            }
            // Broken edges of the hole and rubble left on the roof.
            for (int i = 0; i < 18; i++)
            {
                var p = new Vector3(_rng.RandfRange(RoofHole.Position.X, RoofHole.End.X), y + 0.1f, _rng.Randf() < 0.5f ? RoofHole.Position.Y - 0.6f : RoofHole.End.Y + 0.6f);
                b.Box("Rubble", HospPlaster.Darkened(0.35f), p, new Vector3(_rng.RandfRange(0.6f, 2f), 0.4f, _rng.RandfRange(0.6f, 1.4f)), new Basis(Vector3.Up, _rng.Randf() * 3f));
            }
            // Water tanks and the sign frame.
            foreach (var x in new[] { 20f, 26f })
            {
                b.Box("MetalTank", HospMetal.Darkened(0.2f), new Vector3(x, y + 2f, 6f), new Vector3(3f, 2.4f, 3f));
                b.Box("MetalLeg", HospRust, new Vector3(x, y + 0.4f, 6f), new Vector3(2.6f, 0.8f, 2.6f));
            }
            b.Box("MetalFrame", HospRust, new Vector3(0f, y + 2.6f, -BD + 0.6f), new Vector3(16f, 0.15f, 0.15f));
            foreach (var x in new[] { -7f, 0f, 7f })
                b.Box("MetalFrame", HospRust, new Vector3(x, y + 1.6f, -BD + 0.6f), new Vector3(0.15f, 3.2f, 0.15f));
            b.Box("SignBoard", new Color(0.25f, 0.27f, 0.26f), new Vector3(0f, y + 3.6f, -BD + 0.5f), new Vector3(16f, 2.6f, 0.2f));
            HospLabel(rg, "โรงพยาบาลเวียงหมอก", new Vector3(0f, y + 3.95f, -BD + 0.37f), Mathf.Pi, 150, new Color(0.85f, 0.82f, 0.72f));
            HospLabel(rg, "HOSPITAL", new Vector3(0f, y + 2.95f, -BD + 0.37f), Mathf.Pi, 110, new Color(0.8f, 0.78f, 0.7f));
            b.Commit(rg);
        }
        HospFireEscape(h);
        HospDuct(h);
        HospMainStair(h);
        HospFacadeVines(h);
    }

    // A floor slab cut around the holes above it (and a plain floor on the ground).
    static void HospSlab(Batch b, int floor, float y)
    {
        var outer = new Rect2(-BW, -BD, BW * 2f, BD * 2f);
        if (floor == 0)
        {
            b.Box("Floor", HospFloor, new Vector3(0f, 0.03f, 0f), new Vector3(BW * 2f, 0.06f, BD * 2f));
            return;
        }
        var holes = floor == 2 ? new[] { MainStairHole, FireStairHole } : new[] { MainStairHole };
        foreach (var r in SlabPieces(outer, holes))
        {
            var c = r.GetCenter();
            Solid(b, "Floor", HospFloor, new Vector3(c.X, y - Slab * 0.5f, c.Y), new Vector3(r.Size.X, Slab, r.Size.Y));
        }
        // Jagged lip round the stairwell.
        for (int i = 0; i < 10; i++)
        {
            var p = new Vector3(_rng.RandfRange(MainStairHole.Position.X, MainStairHole.End.X), y - 0.1f, _rng.Randf() < 0.5f ? MainStairHole.Position.Y : MainStairHole.End.Y);
            b.Box("Rubble", HospFloor.Darkened(0.2f), p, new Vector3(_rng.RandfRange(0.5f, 1.4f), 0.5f, 0.6f), new Basis(Vector3.Right, _rng.RandfRange(-0.4f, 0.4f)));
        }
    }

    // The rectangle minus the holes, as a set of rectangles (cut into columns at every hole edge).
    static List<Rect2> SlabPieces(Rect2 outer, Rect2[] holes)
    {
        var xs = new List<float> { outer.Position.X, outer.End.X };
        foreach (var hl in holes)
            xs.AddRange(new[] { hl.Position.X, hl.End.X });
        xs = xs.Distinct().OrderBy(x => x).ToList();
        var pieces = new List<Rect2>();
        for (int i = 0; i < xs.Count - 1; i++)
        {
            float x0 = xs[i], x1 = xs[i + 1];
            var cuts = holes.Where(hl => hl.Position.X < x1 - 0.01f && hl.End.X > x0 + 0.01f).OrderBy(hl => hl.Position.Y).ToList();
            float z = outer.Position.Y;
            foreach (var hl in cuts)
            {
                if (hl.Position.Y > z + 0.01f)
                    pieces.Add(new Rect2(x0, z, x1 - x0, hl.Position.Y - z));
                z = Mathf.Max(z, hl.End.Y);
            }
            if (outer.End.Y > z + 0.01f)
                pieces.Add(new Rect2(x0, z, x1 - x0, outer.End.Y - z));
        }
        return pieces;
    }

    // Openings in the outer walls: (face, floor) → list of (from, to, top) along the face.
    static List<(float a, float b, float top)> ExteriorOpenings(char face, int floor) => (face, floor) switch
    {
        ('N', 0) => new() { (-3.2f, 3.2f, 4.2f) },                 // main entrance
        ('W', 1) => new() { (8.4f, 11.4f, 3.2f) },                 // fire escape door
        ('E', 0) => new() { (-11.4f, -8.2f, 3.4f) },               // duct breach (plant room)
        ('E', 1) => new() { (8.4f, 11.4f, 3.4f) },                 // duct breach (store room)
        _ => new(),
    };

    // Outer walls of one storey: piers, sills and lintels round rows of windows (some still glazed,
    // most smashed), dirty plaster east of the lobby and old brick west of it, grime streaks below sills.
    static void HospExterior(Batch b, Node3D fg, int floor, float y)
    {
        foreach (var face in new[] { 'N', 'S', 'W', 'E' })
        {
            bool alongX = face is 'N' or 'S';
            float fixedC = face switch { 'N' => -BD + WallT * 0.5f, 'S' => BD - WallT * 0.5f, 'W' => -BW + WallT * 0.5f, _ => BW - WallT * 0.5f };
            float from = alongX ? -BW : -BD, to = alongX ? BW : BD;
            var doors = ExteriorOpenings(face, floor);
            Vector3 At(float t, float yy) => alongX ? new Vector3(t, yy, fixedC) : new Vector3(fixedC, yy, t);
            Vector3 Size(float len, float hh, float thick = WallT) => alongX ? new Vector3(len, hh, thick) : new Vector3(thick, hh, len);
            Color WallColor(float t) => alongX && t < -10f || face == 'W' ? HospBrick : HospPlaster;
            string WallName(float t) => alongX && t < -10f || face == 'W' ? "WallBrick" : "WallPlaster";

            // Solid runs between the doors.
            var runs = new List<(float a, float b)>();
            float cur = from;
            foreach (var d in doors.OrderBy(d => d.a))
            {
                runs.Add((cur, d.a));
                // Lintel over the door.
                float lh = FH - d.top;
                b.Box(WallName(d.a), WallColor(d.a), At((d.a + d.b) * 0.5f, y + d.top + lh * 0.5f), Size(d.b - d.a, lh));
                Col(At((d.a + d.b) * 0.5f, y + d.top + lh * 0.5f), Size(d.b - d.a, lh, 0.6f));
                cur = d.b;
            }
            runs.Add((cur, to));
            foreach (var (ra, rb) in runs)
            {
                if (rb - ra < 0.05f)
                    continue;
                Col(At((ra + rb) * 0.5f, y + FH * 0.5f), Size(rb - ra, FH, 0.6f));
                // Window modules of 4.5 m; leftovers are plain wall.
                float t = ra;
                while (t < rb - 0.05f)
                {
                    float m = Mathf.Min(4.5f, rb - t);
                    var col = WallColor(t + m * 0.5f);
                    string nm = WallName(t + m * 0.5f);
                    if (m < 3.4f)
                        b.Box(nm, col, At(t + m * 0.5f, y + FH * 0.5f), Size(m, FH));
                    else
                    {
                        float pier = 1.1f, win = m - pier, sill = 1.4f, head = 4.8f;
                        b.Box(nm, col, At(t + pier * 0.5f, y + FH * 0.5f), Size(pier, FH));
                        b.Box(nm, col, At(t + pier + win * 0.5f, y + sill * 0.5f), Size(win, sill));
                        b.Box(nm, col, At(t + pier + win * 0.5f, y + (head + FH) * 0.5f), Size(win, FH - head));
                        // A ledge under the window, grime running down from it.
                        float outward = face is 'N' or 'W' ? -1f : 1f;
                        var ledge = At(t + pier + win * 0.5f, y + sill);
                        b.Box("WallPlaster", HospPlaster.Darkened(0.2f), ledge + (alongX ? new Vector3(0f, 0f, outward * 0.3f) : new Vector3(outward * 0.3f, 0f, 0f)), Size(win + 0.2f, 0.12f, 0.35f));
                        if (_rng.Randf() < 0.5f)
                            b.Box("GrimeInk", HospGrime, ledge + Vector3.Down * 0.7f + (alongX ? new Vector3(0f, 0f, outward * 0.22f) : new Vector3(outward * 0.22f, 0f, 0f)), Size(_rng.RandfRange(0.4f, 1.2f), 1.4f, 0.02f));
                        float roll = _rng.Randf();
                        if (roll < 0.3f)
                            b.Box("WindowGlass", HospGlass, At(t + pier + win * 0.5f, y + (sill + head) * 0.5f), Size(win, head - sill, 0.05f));
                        else if (roll < 0.55f)
                            // Half a pane left in the frame.
                            b.Box("WindowGlass", HospGlass, At(t + pier + win * 0.3f, y + sill + 0.6f), Size(win * 0.5f, 1.2f, 0.05f));
                        // Window frame bars.
                        b.Box("MetalFrame", HospRust, At(t + pier + win * 0.5f, y + (sill + head) * 0.5f), Size(0.08f, head - sill, 0.1f));
                        b.Box("MetalFrame", HospRust, At(t + pier + win * 0.5f, y + (sill + head) * 0.5f), Size(win, 0.08f, 0.1f));
                    }
                    t += m;
                }
            }
            // Floor band between storeys.
            b.Box("WallPlaster", HospPlaster.Darkened(0.28f), At(0f, y + FH - 0.2f) + (alongX ? new Vector3(0f, 0f, face == 'N' ? -0.15f : 0.15f) : new Vector3(face == 'W' ? -0.15f : 0.15f, 0f, 0f)),
                Size(to - from + 0.6f, 0.4f, 0.7f));
        }
        if (floor == 0)
        {
            // Entrance canopy on four columns, with its sign; broken glass doors under it.
            Solid(b, "RoofSlab", HospPlaster.Darkened(0.2f), new Vector3(0f, 4.4f, -BD - 3f), new Vector3(16f, 0.4f, 6f));
            foreach (var x in new[] { -7f, 7f })
                Solid(b, "WallPlaster", HospPlaster, new Vector3(x, 2.2f, -BD - 5.4f), new Vector3(0.5f, 4.4f, 0.5f));
            b.Box("SignBoard", new Color(0.25f, 0.27f, 0.26f), new Vector3(0f, 5.1f, -BD - 5.95f), new Vector3(9f, 1f, 0.12f));
            HospLabel(fg, "ทางเข้าหลัก", new Vector3(0f, 5.1f, -BD - 6.03f), Mathf.Pi, 70, new Color(0.85f, 0.83f, 0.75f));
            foreach (var x in new[] { -2.4f, 2.4f })
                b.Box("MetalFrame", HospMetal.Darkened(0.3f), new Vector3(x, 2.1f, -BD + 0.2f), new Vector3(0.12f, 4.2f, 0.15f));
            b.Box("WindowGlass", HospGlass, new Vector3(-1.4f, 1.3f, -BD - 0.6f), new Vector3(1.6f, 2.4f, 0.05f), new Basis(Vector3.Right, -1.35f));
            for (int i = 0; i < 6; i++)
                GlassShards(b, new Vector3(_rng.RandfRange(-3f, 3f), 0.07f, -BD - _rng.RandfRange(0.5f, 4f)));
            // Steps up to the doors.
            b.Box("Step", HospFloor.Darkened(0.1f), new Vector3(0f, 0.08f, -BD - 1.2f), new Vector3(10f, 0.16f, 2.4f));
        }
    }

    // ---------- rooms ----------

    enum Rm { Triage, Er, Trauma, Admin, Pharmacy, Xray, Or, Plant, Morgue, Records, Nurse, Lab, Recovery, Ward, Icu, FireStair, Landing, Buddha, Store, Delivery, Nursery, Mother, Collapsed, Hall }

    // (x0, x1, north?, kind, label). Rooms on the north side run from the outer wall to the corridor.
    static IEnumerable<(float x0, float x1, bool north, Rm kind, string label)> RoomsOf(int floor)
    {
        switch (floor)
        {
            case 0:
                yield return (-46f, -38f, true, Rm.Triage, "ห้องคัดกรอง");
                yield return (-38f, -28f, true, Rm.Er, "ห้องฉุกเฉิน");
                yield return (-28f, -18f, true, Rm.Trauma, "ห้องอุบัติเหตุ");
                yield return (-18f, -10f, true, Rm.Admin, "ฝ่ายธุรการ");
                yield return (10f, 18f, true, Rm.Pharmacy, "ห้องจ่ายยา");
                yield return (18f, 28f, true, Rm.Xray, "ห้องเอกซเรย์");
                yield return (28f, 38f, true, Rm.Or, "ห้องผ่าตัด 1");
                yield return (38f, 46f, true, Rm.Plant, "ห้องเครื่อง");
                yield return (-46f, -36f, false, Rm.Trauma, "ห้องผู้ป่วยหนักฉุกเฉิน");
                yield return (-36f, -20f, false, Rm.Morgue, "ห้องเก็บศพ");
                yield return (-20f, -10f, false, Rm.Records, "ห้องเวชระเบียน");
                yield return (10f, 18f, false, Rm.Nurse, "เคาน์เตอร์พยาบาล");
                yield return (18f, 28f, false, Rm.Lab, "ห้องแล็บ");
                yield return (28f, 38f, false, Rm.Recovery, "ห้องพักฟื้น");
                yield return (38f, 46f, false, Rm.Or, "ห้องผ่าตัด 2");
                break;
            case 1:
                for (int i = 0; i < 6; i++)
                    yield return (-46f + i * 6f, -40f + i * 6f, true, Rm.Ward, $"ห้องพักผู้ป่วย {101 + i}");
                yield return (-46f, -40f, false, Rm.Landing, "ทางหนีไฟ");
                for (int i = 1; i < 6; i++)
                    yield return (-46f + i * 6f, -40f + i * 6f, false, Rm.Ward, $"ห้องพักผู้ป่วย {106 + i}");
                yield return (10f, 18f, true, Rm.Nurse, "ห้องพักพยาบาล");
                yield return (18f, 30f, true, Rm.Icu, "ห้องไอซียู");
                yield return (30f, 46f, true, Rm.FireStair, "บันไดหนีไฟ");
                yield return (10f, 18f, false, Rm.Buddha, "ห้องพระ");
                yield return (18f, 30f, false, Rm.Icu, "ห้องไอซียู 2");
                yield return (30f, 38f, false, Rm.Xray, "ห้องเอกซเรย์ชั้น 2");
                yield return (38f, 46f, false, Rm.Store, "ห้องเก็บเครื่องมือ");
                break;
            default:
                yield return (-46f, -10f, true, Rm.Hall, "หอผู้ป่วยหลังคลอด");
                yield return (10f, 20f, true, Rm.Delivery, "ห้องคลอด 1");
                yield return (20f, 30f, true, Rm.Delivery, "ห้องคลอด 2");
                yield return (30f, 46f, true, Rm.FireStair, "บันไดหนีไฟ");
                yield return (10f, 24f, false, Rm.Nursery, "ห้องเด็กอ่อน");
                yield return (24f, 36f, false, Rm.Mother, "ห้องพักแม่");
                yield return (36f, 46f, false, Rm.Collapsed, "");
                break;
        }
    }

    static void HospRooms(Batch b, Node3D fg, int floor, float y)
    {
        float wallH = FH - (floor < 2 ? Slab : Slab);
        foreach (var (x0, x1, north, kind, label) in RoomsOf(floor))
        {
            bool hall = kind == Rm.Hall;
            float z0 = hall || north ? -BD : Corr, z1 = hall ? BD : north ? -Corr : BD;
            float zc = north ? -Corr : Corr;
            // Corridor wall with a door (none for the open hall; the collapsed room is sealed).
            if (!hall)
            {
                float da = kind == Rm.FireStair ? x0 + 0.6f : (x0 + x1) * 0.5f - 1f, db = da + 2f;
                if (kind == Rm.Collapsed)
                    da = db = x1;
                HospInnerWall(b, true, zc, x0, x1, y, wallH, kind == Rm.Collapsed ? new List<(float, float, float)>() : new() { (da, db, 3f) });
                if (kind != Rm.Collapsed)
                {
                    HospDoorSign(fg, label, new Vector3((da + db) * 0.5f, y + 3.45f, zc + (north ? 0.24f : -0.24f)), north ? 0f : Mathf.Pi);
                    // Locked doors: the morgue (ground floor) and the fire stair (first floor).
                    if (floor == 0 && kind == Rm.Morgue)
                        LockedDoor(fg, "rp_morgue", "ประตูห้องเก็บศพ", "kunjae_hong_sop", new Vector3((da + db) * 0.5f, y, zc), 2f);
                    else if (floor == 1 && kind == Rm.FireStair)
                        LockedDoor(fg, "rp_fire_stair", "ประตูบันไดหนีไฟ", "kunjae_bandai_nifai", new Vector3((da + db) * 0.5f, y, zc), 2f);
                    else if (_rng.Randf() < 0.35f)
                        // A door torn off and thrown into the corridor.
                        b.Box("WoodDoor", RotWood.Lightened(0.15f), new Vector3((da + db) * 0.5f + _rng.RandfRange(-1f, 1f), y + 0.06f, zc + (north ? 1.4f : -1.4f)), new Vector3(1.9f, 0.08f, 2.8f), new Basis(Vector3.Up, _rng.RandfRange(-0.5f, 0.5f)));
                }
            }
            // Partition walls: each room builds its west wall (unless that is the outer wall); the rooms
            // west of the lobby also build the lobby wall on their east end.
            if (hall)
                HospInnerWall(b, false, -10f, -BD, BD, y, wallH, new() { (-Corr, Corr, 3.6f) });
            else
                foreach (var x in new[] { x0, x1 })
                    if (x == x0 && x > -BW + 0.5f || x == x1 && Mathf.Abs(x + 10f) < 0.1f)
                    {
                        // A breach knocked through every few walls.
                        var gaps = _rng.Randf() < 0.18f ? new List<(float, float, float)> { ((z0 + z1) * 0.5f - 0.8f, (z0 + z1) * 0.5f + 0.8f, 1.8f) } : new();
                        HospInnerWall(b, false, x, z0, z1, y, wallH, gaps);
                    }
            HospFurnish(b, fg, floor, y, kind, x0, x1, z0, z1, north);
        }
        // Lobby: reception counter and waiting rows on the ground floor; a railing round the hole above.
        if (floor == 0)
        {
            Solid(b, "WoodCounter", RotWood.Lightened(0.1f), new Vector3(-5f, y + 0.55f, -6f), new Vector3(6f, 1.1f, 1f));
            b.Box("WoodCounter", RotWood, new Vector3(-5f, y + 1.15f, -6f), new Vector3(6.3f, 0.08f, 1.2f));
            for (int r = 0; r < 3; r++)
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(3f + i * 1.4f, y, -8f + r * 2.2f);
                bool over = _rng.Randf() < 0.3f;
                var basis = over ? new Basis(Vector3.Right, 1.4f) : Basis.Identity;
                b.Box("MetalChair", new Color(0.3f, 0.42f, 0.5f), p + Vector3.Up * (over ? 0.3f : 0.45f), new Vector3(1.2f, 0.08f, 0.5f), basis);
                b.Box("MetalChair", new Color(0.3f, 0.42f, 0.5f), p + new Vector3(0f, over ? 0.6f : 0.8f, over ? 0.3f : 0.25f), new Vector3(1.2f, 0.6f, 0.06f), basis);
            }
            Wheelchair(b, new Vector3(7.5f, y, -1f), 2.4f, true);
            Stretcher(b, new Vector3(-6f, y, 0.5f), 1.4f, true);
            Blood(b, new Vector3(-4f, y, -1f), 2.5f);
        }
        if (floor > 0)
        {
            // Bent railing round the stairwell hole (no collider: you can drop down if you want).
            var hl = MainStairHole;
            b.Box("MetalRail", HospMetal.Darkened(0.2f), new Vector3(hl.GetCenter().X, y + 1f, hl.Position.Y - 0.1f), new Vector3(hl.Size.X, 0.06f, 0.06f), new Basis(Vector3.Forward, 0.08f));
            for (float x = hl.Position.X; x <= hl.End.X; x += 1.2f)
                b.Box("MetalRail", HospMetal.Darkened(0.2f), new Vector3(x, y + 0.5f, hl.Position.Y - 0.1f), new Vector3(0.05f, 1f, 0.05f));
        }
        // Debris all along the corridor.
        for (int i = 0; i < 26; i++)
        {
            var p = new Vector3(_rng.RandfRange(-BW + 2f, BW - 2f), y, _rng.RandfRange(-Corr + 0.4f, Corr - 0.4f));
            if (floor == 2 && p.X < -10f)
                p.Z = _rng.RandfRange(-BD + 2f, BD - 2f);
            float roll = _rng.Randf();
            if (roll < 0.35f)
                GlassShards(b, p + Vector3.Up * 0.07f);
            else if (roll < 0.55f)
                Papers(b, p);
            else if (roll < 0.7f)
                Blood(b, p, _rng.RandfRange(0.6f, 1.8f));
            else if (roll < 0.8f)
                Stretcher(b, p, _rng.Randf() * Mathf.Tau, true);
            else if (roll < 0.88f)
                Wheelchair(b, p, _rng.Randf() * Mathf.Tau, _rng.Randf() < 0.6f);
            else
                b.Box("Rubble", HospInner.Darkened(0.3f), p + Vector3.Up * 0.2f, new Vector3(_rng.RandfRange(0.6f, 1.6f), 0.4f, _rng.RandfRange(0.5f, 1.2f)), new Basis(Vector3.Up, _rng.Randf() * 3f));
        }
    }

    // An inner wall along X (fixed z) or along Z (fixed x), from a to b, with door gaps (from, to, top).
    // Pale plaster above a green-grey band, as hospitals were painted.
    static void HospInnerWall(Batch b, bool alongX, float fixedC, float a, float c, float y, float h, List<(float a, float b, float top)> doors)
    {
        Vector3 At(float t, float yy) => alongX ? new Vector3(t, yy, fixedC) : new Vector3(fixedC, yy, t);
        Vector3 Size(float len, float hh, float thick = 0.3f) => alongX ? new Vector3(len, hh, thick) : new Vector3(thick, hh, len);
        float cur = a;
        void Run(float ra, float rb)
        {
            if (rb - ra < 0.05f)
                return;
            b.Box("WallPlaster", HospInner, At((ra + rb) * 0.5f, y + (h + 1.4f) * 0.5f), Size(rb - ra, h - 1.4f));
            b.Box("PlasterBand", HospBand, At((ra + rb) * 0.5f, y + 0.7f), Size(rb - ra, 1.4f, 0.34f));
            Col(At((ra + rb) * 0.5f, y + h * 0.5f), Size(rb - ra, h));
            if (_rng.Randf() < 0.3f)
                b.Box("GrimeInk", HospGrime, At(_rng.RandfRange(ra, rb), y + _rng.RandfRange(1.5f, 4f)), Size(_rng.RandfRange(0.5f, 1.5f), _rng.RandfRange(0.5f, 2f), 0.36f));
            if (_rng.Randf() < 0.15f)
                b.Box("BloodInk", HospBlood, At(_rng.RandfRange(ra, rb), y + _rng.RandfRange(0.6f, 1.6f)), Size(_rng.RandfRange(0.3f, 0.9f), _rng.RandfRange(0.4f, 1.2f), 0.36f));
        }
        foreach (var d in doors.OrderBy(d => d.a))
        {
            Run(cur, d.a);
            float lh = h - d.top;
            b.Box("WallPlaster", HospInner, At((d.a + d.b) * 0.5f, y + d.top + lh * 0.5f), Size(d.b - d.a, lh));
            Col(At((d.a + d.b) * 0.5f, y + d.top + lh * 0.5f), Size(d.b - d.a, lh));
            cur = d.b;
        }
        Run(cur, c);
    }

    // A locked door (group locked_door): panels and a collider that Main hides once it is unlocked.
    static void LockedDoor(Node3D parent, string id, string name, string key, Vector3 pos, float width)
    {
        var d = Shapes.Group(parent, $"LockedDoor_{id}", pos);
        d.AddToGroup("locked_door", true);
        d.SetMeta("door_id", id);
        d.SetMeta("door_name", name);
        d.SetMeta("key", key);
        var panel = Shapes.Group(d, "DoorPanel");
        Shapes.Box(panel, new Vector3(0f, 1.5f, 0f), new Vector3(width, 3f, 0.12f), new Color(0.38f, 0.4f, 0.36f), 0f, "MetalDoor");
        Shapes.Box(panel, new Vector3(0f, 1.5f, 0f), new Vector3(0.12f, 0.6f, 0.2f), new Color(0.2f, 0.2f, 0.18f), 0f, "MetalChain");
        Shapes.Box(panel, new Vector3(0f, 1.2f, 0f), new Vector3(0.25f, 0.3f, 0.24f), new Color(0.7f, 0.55f, 0.2f), 0.2f, "MetalPadlock");
        Shapes.Box(panel, new Vector3(0f, 2.3f, -0.08f), new Vector3(1.2f, 0.4f, 0.02f), new Color(0.75f, 0.1f, 0.08f), 0f, "SignBoard");
        var body = Shapes.Child(d, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "DoorCollider");
        Shapes.Child(body, new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(width, 3f, 0.4f) }, Position = new Vector3(0f, 1.5f, 0f) });
    }

    // ---------- furniture ----------

    static void HospFurnish(Batch b, Node3D fg, int floor, float y, Rm kind, float x0, float x1, float z0, float z1, bool north)
    {
        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
        float back = north ? z0 + 1.4f : z1 - 1.4f; // along the outer wall
        Vector3 P(float x, float z) => new(x, y, z);
        switch (kind)
        {
            case Rm.Ward:
            case Rm.Recovery:
            case Rm.Mother:
            case Rm.Trauma:
            case Rm.Triage:
                for (float x = x0 + 1.6f; x < x1 - 1f; x += 2.6f)
                    Bed(b, P(x, back + (north ? 0.8f : -0.8f)), north ? 0f : Mathf.Pi, _rng.Randf() < 0.3f);
                if (_rng.Randf() < 0.6f)
                    IvPole(b, P(cx + 0.8f, cz), _rng.Randf() < 0.5f);
                if (_rng.Randf() < 0.5f)
                    Blood(b, P(cx, cz), _rng.RandfRange(0.8f, 1.6f));
                if (kind == Rm.Trauma || kind == Rm.Triage)
                    Curtains(b, x0, x1, back + (north ? 2.6f : -2.6f), y);
                break;
            case Rm.Er:
                Stretcher(b, P(cx - 2f, cz), 0.3f, false);
                Stretcher(b, P(cx + 2f, cz + 1f), 1.9f, true);
                Cabinet(b, P(x0 + 0.8f, back), north);
                Cabinet(b, P(x1 - 0.8f, back), north);
                Blood(b, P(cx, cz - 0.5f), 2.2f);
                Curtains(b, x0, x1, back + (north ? 3f : -3f), y);
                break;
            case Rm.Admin:
            case Rm.Records:
            case Rm.Pharmacy:
                for (float x = x0 + 1.2f; x < x1 - 1f; x += 2.2f)
                    Cabinet(b, P(x, back), north);
                Solid(b, "WoodDesk", RotWood.Lightened(0.15f), P(cx, cz) + Vector3.Up * 0.4f, new Vector3(2.4f, 0.8f, 1.1f));
                for (int i = 0; i < 6; i++)
                    Papers(b, P(_rng.RandfRange(x0 + 1f, x1 - 1f), _rng.RandfRange(z0 + 1f, z1 - 1f)));
                break;
            case Rm.Xray:
                Solid(b, "MetalTable", HospMetal, P(cx, cz) + Vector3.Up * 0.45f, new Vector3(1f, 0.9f, 2.4f));
                b.Box("MetalArm", HospMetal.Darkened(0.2f), P(cx - 1.4f, cz) + Vector3.Up * 1.4f, new Vector3(0.4f, 2.8f, 0.4f));
                b.Box("MetalArm", HospMetal.Darkened(0.2f), P(cx - 0.7f, cz) + Vector3.Up * 2.6f, new Vector3(1.6f, 0.3f, 0.4f));
                b.Box("MetalHead", HospMetal.Lightened(0.1f), P(cx, cz) + Vector3.Up * 2.3f, new Vector3(0.6f, 0.5f, 0.6f));
                b.Box("WallPlaster", HospInner.Darkened(0.1f), P(x1 - 1.2f, back) + Vector3.Up * 1.1f, new Vector3(1.6f, 2.2f, 0.15f));
                Shapes.Box(fg, P(x0 + 0.4f, cz) + Vector3.Up * 1.8f, new Vector3(0.05f, 0.9f, 1.4f), new Color(0.6f, 0.85f, 0.9f), 0.8f, "GlowScreen");
                break;
            case Rm.Or:
                Solid(b, "MetalTable", HospMetal, P(cx, cz) + Vector3.Up * 0.5f, new Vector3(0.9f, 1f, 2.2f));
                b.Box("ClothSheet", HospSheet.Darkened(0.2f), P(cx, cz) + Vector3.Up * 1.05f, new Vector3(1.1f, 0.06f, 2.3f));
                Blood(b, P(cx, cz) + Vector3.Up * 1.08f, 0.6f);
                Blood(b, P(cx + 0.6f, cz + 1f), 1.8f);
                Shapes.Cylinder(fg, P(cx, cz) + Vector3.Up * 3.6f, 0.7f, 0.5f, 0.3f, HospMetal.Lightened(0.15f), 0f, "MetalLamp");
                b.Box("MetalArm", HospMetal, P(cx, cz) + Vector3.Up * 5.2f, new Vector3(0.1f, 3f, 0.1f));
                b.Box("MetalTray", HospMetal, P(cx + 1.4f, cz - 0.8f) + Vector3.Up * 0.9f, new Vector3(0.8f, 0.05f, 0.5f));
                IvPole(b, P(cx - 1.3f, cz + 0.6f), false);
                break;
            case Rm.Plant:
                for (int i = 0; i < 3; i++)
                    Solid(b, "MetalTank", HospMetal.Darkened(0.35f), P(x0 + 1.6f + i * 2.2f, back) + Vector3.Up * 1.1f, new Vector3(1.6f, 2.2f, 1.6f));
                b.Box("MetalPipe", HospRust, P(cx, back + (north ? 1.5f : -1.5f)) + Vector3.Up * 4.5f, new Vector3(x1 - x0, 0.4f, 0.4f));
                // The broken grille where the duct leaves through the east wall.
                b.Box("MetalGrille", HospRust, new Vector3(BW - 0.6f, y + 1.7f, -9.8f), new Vector3(0.1f, 3.2f, 3f), new Basis(Vector3.Up, 0.6f));
                Shapes.Box(fg, new Vector3(BW - 0.3f, y + 3.6f, -9.8f), new Vector3(0.05f, 0.2f, 2.4f), new Color(0.4f, 1f, 0.6f), 0.6f, "GlowArrowInk");
                break;
            case Rm.Morgue:
                // A wall of cold-storage drawers, two steel tables, body bags on the floor.
                for (int col = 0; col < 7; col++)
                for (int row = 0; row < 3; row++)
                {
                    var p = new Vector3(x0 + 2f + col * 1.9f, y + 0.7f + row * 1.05f, z1 - 0.6f);
                    b.Box("MetalDrawer", HospMetal.Darkened(0.1f + (col + row) % 3 * 0.05f), p, new Vector3(1.7f, 0.95f, 0.8f));
                    b.Box("MetalHandle", HospMetal.Darkened(0.5f), p + new Vector3(0f, 0.2f, -0.42f), new Vector3(0.5f, 0.06f, 0.06f));
                }
                Col(new Vector3(cx, y + 1.7f, z1 - 0.6f), new Vector3(x1 - x0, 3.4f, 1f));
                b.Box("MetalDrawer", HospMetal.Darkened(0.2f), new Vector3(x0 + 5.8f, y + 0.7f, z1 - 1.5f), new Vector3(1.7f, 0.2f, 1.8f)); // one drawer pulled out
                foreach (var x in new[] { cx - 3.5f, cx + 2.5f })
                {
                    Solid(b, "MetalTable", HospMetal, new Vector3(x, y + 0.45f, cz - 0.5f), new Vector3(1f, 0.9f, 2.2f));
                    b.Box("ClothSheet", HospSheet.Darkened(0.15f), new Vector3(x, y + 1.05f, cz - 0.5f), new Vector3(0.9f, 0.3f, 1.9f));
                }
                for (int i = 0; i < 4; i++)
                    b.Box("ClothBodyBag", new Color(0.12f, 0.13f, 0.12f), new Vector3(_rng.RandfRange(x0 + 1.5f, x1 - 1.5f), y + 0.2f, _rng.RandfRange(z0 + 1.2f, cz)), new Vector3(0.6f, 0.4f, 1.9f), new Basis(Vector3.Up, _rng.Randf() * 3f));
                Blood(b, new Vector3(cx, y, cz), 2.6f);
                // The chest with the fire-stair key sits in the corner.
                TreasureChest(fg, "key_rp_morgue", new Vector3(x1 - 1.5f, y, z0 + 1.5f), 0.6f);
                fg.GetNode<Node3D>("Chest_key_rp_morgue").SetMeta("loot", "kunjae_bandai_nifai");
                Shapes.Light(fg, new Vector3(cx, y + 5f, cz), new Color(0.55f, 0.75f, 0.95f), 1.2f, 12f, "MorgueLight");
                break;
            case Rm.Nurse:
                Solid(b, "WoodCounter", RotWood.Lightened(0.1f), P(cx, north ? -Corr - 1.2f : Corr + 1.2f) + Vector3.Up * 0.55f, new Vector3(x1 - x0 - 2f, 1.1f, 0.9f));
                Cabinet(b, P(x0 + 1f, back), north);
                Cabinet(b, P(x1 - 1f, back), north);
                for (int i = 0; i < 5; i++)
                    Papers(b, P(_rng.RandfRange(x0 + 1f, x1 - 1f), _rng.RandfRange(z0 + 1f, z1 - 1f)));
                b.Box("MetalChair", new Color(0.3f, 0.42f, 0.5f), P(cx, cz) + new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.08f, 0.6f), new Basis(Vector3.Right, 1.3f));
                break;
            case Rm.Lab:
                foreach (var z in new[] { z0 + 1.4f, z1 - 1.4f })
                {
                    Solid(b, "WoodBench", RotWood.Lightened(0.2f), new Vector3(cx, y + 0.5f, z), new Vector3(x1 - x0 - 2f, 1f, 1f));
                    for (int i = 0; i < 6; i++)
                        b.Box("GlassBottle", HospGlass, new Vector3(_rng.RandfRange(x0 + 1.5f, x1 - 1.5f), y + 1.15f, z), new Vector3(0.12f, 0.3f, 0.12f));
                }
                for (int i = 0; i < 8; i++)
                    GlassShards(b, P(_rng.RandfRange(x0 + 1f, x1 - 1f), _rng.RandfRange(z0 + 2f, z1 - 2f)) + Vector3.Up * 0.07f);
                break;
            case Rm.Icu:
                for (float x = x0 + 2f; x < x1 - 1.5f; x += 3.2f)
                {
                    Bed(b, P(x, back + (north ? 0.8f : -0.8f)), north ? 0f : Mathf.Pi, _rng.Randf() < 0.25f);
                    b.Box("MetalMonitor", new Color(0.2f, 0.22f, 0.22f), P(x + 1.1f, back) + Vector3.Up * 1.5f, new Vector3(0.6f, 0.5f, 0.3f));
                    if (_rng.Randf() < 0.4f)
                        Shapes.Box(fg, P(x + 1.1f, back + (north ? 0.16f : -0.16f)) + Vector3.Up * 1.5f, new Vector3(0.45f, 0.35f, 0.02f), new Color(0.3f, 1f, 0.5f), 1.2f, "GlowScreen");
                }
                Curtains(b, x0, x1, back + (north ? 2.8f : -2.8f), y);
                break;
            case Rm.FireStair:
                if (floor == 1)
                    HospFireStairRamp(b, y);
                break;
            case Rm.Landing:
                b.Box("SignBoard", new Color(0.1f, 0.45f, 0.2f), new Vector3(x0 + 0.25f, y + 3.6f, 9.9f), new Vector3(0.05f, 0.5f, 1.4f));
                HospLabel(fg, "ทางหนีไฟ", new Vector3(x0 + 0.29f, y + 3.6f, 9.9f), -Mathf.Pi * 0.5f, 34, new Color(0.85f, 1f, 0.85f));
                break;
            case Rm.Buddha:
                HospBuddhaRoom(fg, b, x0, x1, z0, z1, y);
                break;
            case Rm.Store:
                for (float x = x0 + 1f; x < x1 - 0.5f; x += 1.6f)
                    Cabinet(b, P(x, back), north);
                b.Box("MetalGrille", HospRust, new Vector3(BW - 0.6f, y + 1.7f, 9.9f), new Vector3(0.1f, 3.2f, 3f), new Basis(Vector3.Up, -0.5f));
                break;
            case Rm.Delivery:
                Solid(b, "MetalTable", HospMetal, P(cx, cz) + Vector3.Up * 0.55f, new Vector3(1.2f, 1.1f, 2f));
                b.Box("MetalStirrup", HospMetal.Darkened(0.2f), P(cx, cz + 1.3f) + Vector3.Up * 1.2f, new Vector3(1.2f, 0.08f, 0.08f));
                b.Box("ClothSheet", HospSheet.Darkened(0.25f), P(cx, cz) + Vector3.Up * 1.13f, new Vector3(1.3f, 0.05f, 2.1f));
                Blood(b, P(cx, cz + 1.5f), 2f);
                Shapes.Cylinder(fg, P(cx, cz) + Vector3.Up * 3.4f, 0.7f, 0.5f, 0.3f, HospMetal.Lightened(0.15f), 0f, "MetalLamp");
                Crib(b, P(x0 + 1.5f, back), 0f, false);
                break;
            case Rm.Nursery:
                for (float x = x0 + 1.4f; x < x1 - 1f; x += 1.8f)
                for (float z = z0 + 1.6f; z < z1 - 1f; z += 2.4f)
                    Crib(b, P(x, z), _rng.RandfRange(-0.2f, 0.2f), _rng.Randf() < 0.2f);
                // Glass viewing wall onto the corridor, smashed.
                b.Box("WindowGlass", HospGlass, new Vector3(cx, y + 2.2f, Corr + 0.25f), new Vector3((x1 - x0) * 0.3f, 1.6f, 0.05f));
                break;
            case Rm.Collapsed:
                // Floor and roof came down here: a solid heap of rubble, nothing to enter.
                for (int i = 0; i < 26; i++)
                    b.Box("Rubble", HospPlaster.Darkened(_rng.RandfRange(0.1f, 0.4f)), new Vector3(_rng.RandfRange(x0 + 1f, x1 - 1f), y + _rng.RandfRange(0.3f, 4.5f), _rng.RandfRange(z0 + 1f, z1 - 1f)),
                        new Vector3(_rng.RandfRange(1.2f, 3.2f), _rng.RandfRange(0.4f, 1f), _rng.RandfRange(1f, 2.6f)), new Basis(new Vector3(_rng.Randf(), 1f, _rng.Randf()).Normalized(), _rng.RandfRange(0.2f, 0.8f)));
                Col(new Vector3(cx, y + 3f, cz), new Vector3(x1 - x0, 6f, z1 - z0));
                break;
            case Rm.Hall:
                HospMaternityHall(b, fg, y);
                break;
        }
    }

    static void Bed(Batch b, Vector3 p, float yaw, bool overturned)
    {
        var basis = new Basis(Vector3.Up, yaw + _rng.RandfRange(-0.15f, 0.15f));
        if (overturned)
            basis *= new Basis(Vector3.Back, 1.5f);
        var up = overturned ? 0.5f : 0f;
        b.Box("MetalBed", HospMetal, p + basis * new Vector3(0f, 0.55f, 0f) + Vector3.Up * up, new Vector3(1f, 0.1f, 2.1f), basis);
        b.Box("ClothMattress", HospSheet.Darkened(_rng.RandfRange(0f, 0.25f)), p + basis * new Vector3(0f, 0.68f, 0f) + Vector3.Up * up, new Vector3(0.92f, 0.16f, 1.95f), basis);
        b.Box("MetalBed", HospMetal.Darkened(0.1f), p + basis * new Vector3(0f, 0.9f, -1.02f) + Vector3.Up * up, new Vector3(1f, 0.8f, 0.06f), basis);
        foreach (var x in new[] { -0.45f, 0.45f })
        foreach (var z in new[] { -0.95f, 0.95f })
            b.Box("MetalBed", HospMetal.Darkened(0.2f), p + basis * new Vector3(x, 0.28f, z) + Vector3.Up * up, new Vector3(0.05f, 0.55f, 0.05f), basis);
        if (!overturned && _rng.Randf() < 0.3f)
            b.Box("BloodInk", HospBlood, p + basis * new Vector3(0.1f, 0.77f, 0.3f), new Vector3(0.5f, 0.01f, 0.7f), basis);
        Col(p + Vector3.Up * 0.5f, new Vector3(1f, 1f, 2.1f), new Basis(Vector3.Up, yaw));
    }

    static void Stretcher(Batch b, Vector3 p, float yaw, bool overturned)
    {
        var basis = new Basis(Vector3.Up, yaw);
        if (overturned)
            basis *= new Basis(Vector3.Back, 1.9f);
        float up = overturned ? 0.35f : 0f;
        b.Box("MetalStretcher", HospMetal, p + Vector3.Up * (0.75f + up * 0.2f), new Vector3(0.65f, 0.06f, 1.9f), basis);
        b.Box("ClothMattress", new Color(0.3f, 0.32f, 0.3f), p + Vector3.Up * (0.82f + up * 0.2f) + basis * new Vector3(0f, 0.04f, 0f), new Vector3(0.58f, 0.08f, 1.8f), basis);
        foreach (var z in new[] { -0.7f, 0.7f })
            b.Box("MetalStretcher", HospMetal.Darkened(0.25f), p + basis * new Vector3(0f, -0.38f, z) + Vector3.Up * 0.75f, new Vector3(0.05f, 0.75f, 0.05f), basis);
    }

    static void Wheelchair(Batch b, Vector3 p, float yaw, bool tipped)
    {
        var basis = new Basis(Vector3.Up, yaw);
        if (tipped)
            basis *= new Basis(Vector3.Back, 1.4f);
        var at = p + Vector3.Up * (tipped ? 0.35f : 0f);
        b.Box("MetalChair", HospMetal.Darkened(0.3f), at + basis * new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0.06f, 0.5f), basis);
        b.Box("MetalChair", HospMetal.Darkened(0.3f), at + basis * new Vector3(0f, 0.8f, 0.25f), new Vector3(0.5f, 0.6f, 0.05f), basis);
        foreach (var x in new[] { -0.3f, 0.3f })
            b.Box("MetalWheel", new Color(0.1f, 0.1f, 0.1f), at + basis * new Vector3(x, 0.32f, 0.05f), new Vector3(0.04f, 0.62f, 0.62f), basis);
    }

    static void IvPole(Batch b, Vector3 p, bool fallen)
    {
        var basis = fallen ? new Basis(Vector3.Forward, 1.45f) : Basis.Identity;
        b.Box("MetalPole", HospMetal, p + basis * new Vector3(0f, 1f, 0f) + (fallen ? Vector3.Down * 0.9f : Vector3.Zero), new Vector3(0.04f, 2f, 0.04f), basis);
        b.Box("GlassBag", new Color(0.8f, 0.85f, 0.7f, 0.6f), p + basis * new Vector3(0.15f, 1.75f, 0f) + (fallen ? Vector3.Down * 0.9f : Vector3.Zero), new Vector3(0.18f, 0.28f, 0.06f), basis);
    }

    static void Cabinet(Batch b, Vector3 p, bool north)
    {
        bool fallen = _rng.Randf() < 0.2f;
        var basis = fallen ? new Basis(Vector3.Right, north ? -1.5f : 1.5f) : Basis.Identity;
        var at = p + (fallen ? new Vector3(0f, 0.3f, north ? 1f : -1f) : new Vector3(0f, 1f, 0f));
        b.Box("MetalCabinet", new Color(0.5f, 0.55f, 0.52f), at, new Vector3(1f, 2f, 0.6f), basis);
        b.Box("MetalHandle", HospMetal.Darkened(0.4f), at + basis * new Vector3(0f, 0f, north ? 0.31f : -0.31f), new Vector3(0.04f, 1.6f, 0.02f), basis);
        Col(at, new Vector3(1f, 2f, 0.6f), basis);
    }

    static void Crib(Batch b, Vector3 p, float yaw, bool tipped)
    {
        var basis = new Basis(Vector3.Up, yaw) * (tipped ? new Basis(Vector3.Back, 1.4f) : Basis.Identity);
        var at = p + Vector3.Up * (tipped ? 0.3f : 0f);
        b.Box("MetalCrib", HospMetal.Lightened(0.05f), at + basis * new Vector3(0f, 0.75f, 0f), new Vector3(0.7f, 0.06f, 1f), basis);
        b.Box("ClothMattress", new Color(0.75f, 0.7f, 0.72f), at + basis * new Vector3(0f, 0.8f, 0f), new Vector3(0.62f, 0.06f, 0.92f), basis);
        foreach (var x in new[] { -0.35f, 0.35f })
        for (float z = -0.45f; z <= 0.46f; z += 0.15f)
            b.Box("MetalCrib", HospMetal, at + basis * new Vector3(x, 0.95f, z), new Vector3(0.03f, 0.35f, 0.03f), basis);
        foreach (var x in new[] { -0.3f, 0.3f })
        foreach (var z in new[] { -0.45f, 0.45f })
            b.Box("MetalCrib", HospMetal.Darkened(0.2f), at + basis * new Vector3(x, 0.37f, z), new Vector3(0.04f, 0.75f, 0.04f), basis);
    }

    // Ragged privacy curtains on a rail across a room.
    static void Curtains(Batch b, float x0, float x1, float z, float y)
    {
        b.Box("MetalRail", HospMetal, new Vector3((x0 + x1) * 0.5f, y + 2.6f, z), new Vector3(x1 - x0 - 0.6f, 0.05f, 0.05f));
        for (float x = x0 + 0.8f; x < x1 - 0.6f; x += 1.1f)
            if (_rng.Randf() < 0.7f)
            {
                float len = _rng.RandfRange(1.2f, 2.3f);
                b.Box("ClothCurtain", new Color(0.55f, 0.66f, 0.65f).Darkened(_rng.RandfRange(0f, 0.3f)), new Vector3(x, y + 2.6f - len * 0.5f, z), new Vector3(1f, len, 0.03f), new Basis(Vector3.Up, _rng.RandfRange(-0.3f, 0.3f)));
            }
    }

    static void GlassShards(Batch b, Vector3 p)
    {
        for (int i = 0; i < 4; i++)
            b.Box("GlassShard", HospGlass, p + new Vector3(_rng.RandfRange(-0.6f, 0.6f), 0f, _rng.RandfRange(-0.6f, 0.6f)), new Vector3(_rng.RandfRange(0.1f, 0.4f), 0.02f, _rng.RandfRange(0.05f, 0.3f)), new Basis(Vector3.Up, _rng.Randf() * 3f));
    }

    static void Papers(Batch b, Vector3 p)
    {
        for (int i = 0; i < 3; i++)
            b.Box("PaperSheet", new Color(0.8f, 0.78f, 0.68f).Darkened(_rng.RandfRange(0f, 0.3f)), p + new Vector3(_rng.RandfRange(-0.5f, 0.5f), 0.07f + i * 0.005f, _rng.RandfRange(-0.5f, 0.5f)), new Vector3(0.25f, 0.005f, 0.33f), new Basis(Vector3.Up, _rng.Randf() * 3f));
    }

    static void Blood(Batch b, Vector3 p, float size)
    {
        b.Box("BloodInk", HospBlood, p + Vector3.Up * 0.075f, new Vector3(size, 0.01f, size * _rng.RandfRange(0.4f, 0.8f)), new Basis(Vector3.Up, _rng.Randf() * 3f));
        for (int i = 0; i < 3; i++)
            b.Box("BloodInk", HospBlood.Darkened(0.2f), p + new Vector3(_rng.RandfRange(-size, size), 0.077f, _rng.RandfRange(-size, size)), new Vector3(size * 0.2f, 0.01f, size * 0.15f));
        // A trail dragged off to one side.
        if (_rng.Randf() < 0.4f)
            b.Box("BloodInk", HospBlood.Darkened(0.1f), p + new Vector3(size, 0.076f, 0f), new Vector3(size * 1.6f, 0.01f, 0.25f), new Basis(Vector3.Up, _rng.RandfRange(-0.4f, 0.4f)));
    }

    // ห้องพระ: a small Buddha room someone still keeps; candles always lit. A place to bind and rest.
    static void HospBuddhaRoom(Node3D fg, Batch b, float x0, float x1, float z0, float z1, float y)
    {
        float cx = (x0 + x1) * 0.5f;
        var altar = Shapes.Group(fg, "Shrine_rong_phayaban_phra", new Vector3(cx, y, z0 + 1.4f), Mathf.Pi);
        Shrine(altar, "rong_phayaban_phra", "ห้องพระชั้นสอง");
        Shapes.Box(altar, new Vector3(0f, 0.5f, 0f), new Vector3(2.4f, 1f, 1f), new Color(0.5f, 0.15f, 0.1f), 0f, "WoodAltar");
        Shapes.Box(altar, new Vector3(0f, 1.2f, 0.2f), new Vector3(1.4f, 0.4f, 0.6f), new Color(0.5f, 0.15f, 0.1f), 0f, "WoodAltar");
        Shapes.Cylinder(altar, new Vector3(0f, 1.65f, 0.25f), 0.12f, 0.3f, 0.5f, Gold, 0.2f, "Spire");
        Shapes.Sphere(altar, new Vector3(0f, 2.05f, 0.25f), 0.14f, Gold, 0.2f, "Spire");
        Shapes.Cylinder(altar, new Vector3(0f, 2.3f, 0.25f), 0.01f, 0.06f, 0.3f, Gold, 0.2f, "Spire");
        foreach (var x in new[] { -0.9f, 0.9f })
        {
            Shapes.Cylinder(altar, new Vector3(x, 1.15f, -0.2f), 0.04f, 0.04f, 0.3f, new Color(0.95f, 0.9f, 0.8f), 0f, "Candle");
            Shapes.Sphere(altar, new Vector3(x, 1.35f, -0.2f), 0.05f, new Color(1f, 0.65f, 0.3f), 4f, "GlowFlame");
            Shapes.Cylinder(altar, new Vector3(x * 0.6f, 1.12f, -0.3f), 0.12f, 0.08f, 0.15f, new Color(0.95f, 0.95f, 0.85f), 0.2f, "Garland");
        }
        Shapes.Light(altar, new Vector3(0f, 2.2f, -1.2f), new Color(1f, 0.72f, 0.4f), 2.2f, 9f, "ShrineLight");
        Shapes.Solid(altar, new Vector3(0f, 0.6f, 0f), new Vector3(2.4f, 1.2f, 1f));
        Sanctuary(fg, new Vector3(cx, y, (z0 + z1) * 0.5f), 5f);
        b.Box("ClothMat", new Color(0.55f, 0.4f, 0.2f), new Vector3(cx, y + 0.08f, z0 + 4f), new Vector3(2.6f, 0.03f, 3.2f));
    }

    // The fire stair from the 1st to the 2nd floor along the north wall of its room, rising east.
    static void HospFireStairRamp(Batch b, float y)
    {
        var a = new Vector3(FireStairHole.Position.X + 0.1f, y, -10.2f);
        var top = new Vector3(FireStairHole.End.X, y + FH, -10.2f);
        Ramp(b, a, top, 3f, new Color(0.45f, 0.45f, 0.42f));
        // Handrail on the open side.
        b.Box("MetalRail", HospMetal.Darkened(0.2f), (a + top) * 0.5f + new Vector3(0f, 1f, 1.6f), new Vector3((top - a).Length(), 0.06f, 0.06f), new Basis(Vector3.Back, Mathf.Atan2(FH, top.X - a.X)));
    }

    // A walkable slope from a to c (both on the walking surface), drawn as steps.
    static void Ramp(Batch b, Vector3 a, Vector3 c, float width, Color color)
    {
        var d = c - a;
        var f = d.Normalized();
        var side = f.Cross(Vector3.Up).Normalized();
        var up = side.Cross(f);
        var basis = new Basis(side, up, f);
        const float t = 0.3f;
        Col((a + c) * 0.5f - up * t * 0.5f, new Vector3(width, t, d.Length()), basis);
        var flat = new Vector3(d.X, 0f, d.Z);
        int n = Mathf.Max(2, Mathf.RoundToInt(d.Y / 0.32f));
        var stepBasis = new Basis(Vector3.Up, Mathf.Atan2(flat.X, flat.Z));
        for (int i = 0; i < n; i++)
        {
            float k = (i + 0.5f) / n;
            var p = a + d * k;
            b.Box("Step", color, p + Vector3.Down * (d.Y / n) * 0.5f, new Vector3(width, d.Y / n, flat.Length() / n + 0.02f), stepBasis);
        }
        // The underside.
        b.Box("Floor", color.Darkened(0.25f), (a + c) * 0.5f - up * (t + 0.3f), new Vector3(width, 0.3f, d.Length()), basis);
    }

    // ---------- the routes round the collapsed stair ----------

    // Overgrown fire escape outside the west end: ground → 1st floor door.
    static void HospFireEscape(Node3D h)
    {
        var g = Shapes.Group(h, "FireEscape");
        _hospBody = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
        var b = new Batch();
        float x = -BW - 1.7f;
        Ramp(b, new Vector3(x, 0f, -10f), new Vector3(x, FH, 8f), 3f, HospRust.Lightened(0.1f));
        Solid(b, "MetalLanding", HospRust.Lightened(0.1f), new Vector3(x, FH - 0.15f, 10f), new Vector3(3.2f, 0.3f, 4.2f));
        // Railing and posts on the outer side; vines over all of it.
        for (float z = -10f; z <= 12f; z += 2f)
        {
            float yy = Mathf.Clamp((z + 10f) / 18f, 0f, 1f) * FH;
            b.Box("MetalPole", HospRust, new Vector3(x - 1.55f, yy * 0.5f + 0.5f, z), new Vector3(0.08f, yy + 1f, 0.08f));
            for (int i = 0; i < 2; i++)
                b.Box("VineLeaf", VineGreen.Lightened(_rng.RandfRange(0f, 0.08f)), new Vector3(x - 1.5f + _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(0.5f, yy + 1f), z + _rng.RandfRange(-1f, 1f)), new Vector3(0.5f, 0.5f, 0.6f));
        }
        Col(new Vector3(x - 1.6f, FH * 0.5f + 0.5f, 1f), new Vector3(0.2f, FH + 1f, 22f));
        b.Box("MetalRail", HospRust, new Vector3(x - 1.55f, FH * 0.5f + 1f, -1f), new Vector3(0.06f, 0.06f, 19.4f), new Basis(Vector3.Right, -Mathf.Atan2(FH, 18f)));
        Shapes.MapTag(Shapes.Group(g, "EscapeMap", new Vector3(x, 0f, 1f)), "map_wall", new Vector2(3.4f, 24f));
        b.Commit(g);
    }

    // The ventilation duct up the east wall: plant room (ground) → store room (1st floor).
    // A big sheet-metal box round a ramp, low and dark inside.
    static void HospDuct(Node3D h)
    {
        var g = Shapes.Group(h, "VentDuct");
        _hospBody = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
        var b = new Batch();
        float x = BW + 1.7f, inner = 3.4f;
        var metal = HospMetal.Darkened(0.35f);
        Solid(b, "MetalDuctFloor", metal, new Vector3(x, 0.1f, -10.5f), new Vector3(3.2f, 0.2f, 3f));
        Ramp(b, new Vector3(x, 0.2f, -9f), new Vector3(x, FH, 8f), 3f, metal.Darkened(0.1f));
        Solid(b, "MetalDuctFloor", metal, new Vector3(x, FH - 0.1f, 10.1f), new Vector3(3.2f, 0.2f, 4.2f));
        // Outer wall, roof following the slope, end caps.
        Solid(b, "MetalDuctWall", metal, new Vector3(x + 1.7f, (FH + inner) * 0.5f, 0f), new Vector3(0.3f, FH + inner, 24.4f));
        var slope = new Basis(Vector3.Right, -Mathf.Atan2(FH - 0.2f, 17f));
        b.Box("MetalDuctWall", metal, new Vector3(x, (FH + 0.2f) * 0.5f + inner, -0.5f), new Vector3(3.6f, 0.3f, 18.6f), slope);
        Col(new Vector3(x, (FH + 0.2f) * 0.5f + inner, -0.5f), new Vector3(3.6f, 0.3f, 18.6f), slope);
        foreach (var z in new[] { -12.1f, 12.1f })
            Solid(b, "MetalDuctWall", metal, new Vector3(x, (FH + inner) * 0.5f, z), new Vector3(3.6f, FH + inner, 0.3f));
        // Rivet seams and a faint emergency strip light inside.
        for (float z = -11f; z < 12f; z += 2.4f)
            b.Box("MetalSeam", metal.Darkened(0.3f), new Vector3(x + 1.86f, (FH + inner) * 0.5f, z), new Vector3(0.05f, FH + inner, 0.12f));
        Shapes.Light(g, new Vector3(x, 3f, -1f), new Color(0.4f, 1f, 0.6f), 0.7f, 8f, "DuctLight");
        Shapes.Light(g, new Vector3(x, FH + 1.5f, 9f), new Color(0.4f, 1f, 0.6f), 0.7f, 6f, "DuctLight");
        Shapes.MapTag(Shapes.Group(g, "DuctMap", new Vector3(x, 0f, 0f)), "map_wall", new Vector2(3.8f, 24.6f));
        b.Commit(g);
    }

    // The main staircase in the lobby: the first flight still stands, the rest lies in a heap that
    // fills the stairwell, with slabs from the floors above hanging into it.
    static void HospMainStair(Node3D h)
    {
        var g = Shapes.Group(h, "CollapsedStair");
        _hospBody = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
        var b = new Batch();
        var hl = MainStairHole;
        // Remaining flight: a few steps that end in mid air.
        for (int i = 0; i < 6; i++)
            b.Box("Step", HospFloor.Darkened(0.05f), new Vector3(-2.5f, 0.16f + i * 0.32f, hl.Position.Y + 0.3f + i * 0.45f), new Vector3(3.6f, 0.32f, 0.46f));
        // The heap: big broken slabs and stair pieces piled up to about 3 m.
        for (int i = 0; i < 30; i++)
        {
            var p = new Vector3(_rng.RandfRange(hl.Position.X + 0.5f, hl.End.X - 0.5f), _rng.RandfRange(0.3f, 2.8f), _rng.RandfRange(hl.Position.Y + 3f, hl.End.Y - 0.3f));
            var sz = new Vector3(_rng.RandfRange(1f, 3.4f), _rng.RandfRange(0.3f, 0.6f), _rng.RandfRange(0.8f, 2.4f));
            var basis = new Basis(new Vector3(_rng.RandfRange(-1f, 1f), 1f, _rng.RandfRange(-1f, 1f)).Normalized(), _rng.RandfRange(0.2f, 0.9f));
            b.Box("Rubble", HospFloor.Darkened(_rng.RandfRange(0.05f, 0.35f)), p, sz, basis);
        }
        // Broken slabs hanging from the upper floors.
        foreach (var yy in new[] { FH, FH * 2f })
        {
            b.Box("Rubble", HospFloor.Darkened(0.2f), new Vector3(1.5f, yy - 2f, hl.End.Y - 1.5f), new Vector3(4f, 0.4f, 3f), new Basis(Vector3.Right, 0.9f));
            b.Box("MetalRebar", HospRust, new Vector3(-2f, yy - 0.8f, hl.Position.Y + 0.4f), new Vector3(0.04f, 1.6f, 0.04f), new Basis(Vector3.Back, 0.4f));
        }
        // Too high to climb, and walled with planks across the front with a warning.
        Col(new Vector3(hl.GetCenter().X, 1.6f, hl.GetCenter().Y + 1.5f), new Vector3(hl.Size.X, 3.2f, hl.Size.Y - 3f));
        Col(new Vector3(-2.5f, 1.4f, hl.Position.Y + 1.6f), new Vector3(3.8f, 2.8f, 2.6f));
        for (int i = 0; i < 3; i++)
            b.Box("WoodPlank", RotWood.Lightened(0.1f), new Vector3(0.5f, 0.6f + i * 0.6f, hl.Position.Y - 0.2f), new Vector3(hl.Size.X + 0.6f, 0.25f, 0.08f), new Basis(Vector3.Back, _rng.RandfRange(-0.12f, 0.12f)));
        Col(new Vector3(0f, 1.1f, hl.Position.Y - 0.2f), new Vector3(hl.Size.X + 0.6f, 2.2f, 0.3f));
        var sign = Shapes.Group(g, "StairSign", new Vector3(0f, 2.2f, hl.Position.Y - 0.3f));
        Shapes.Box(sign, Vector3.Zero, new Vector3(2.6f, 0.9f, 0.05f), new Color(0.85f, 0.75f, 0.25f), 0f, "SignBoard");
        HospLabel(sign, "บันไดถล่ม ห้ามผ่าน\nใช้ทางหนีไฟฝั่งตะวันตก", new Vector3(0f, 0f, -0.04f), Mathf.Pi, 26, new Color(0.15f, 0.1f, 0.05f));
        b.Commit(g);
    }

    // Vines hanging from the roof edge and creeping up from the ground over the facade.
    static void HospFacadeVines(Node3D h)
    {
        var g = Shapes.Group(h, "Vines");
        var b = new Batch();
        for (int i = 0; i < 90; i++)
        {
            int face = _rng.RandiRange(0, 3);
            float t = face < 2 ? _rng.RandfRange(-BW, BW) : _rng.RandfRange(-BD, BD);
            float off = 0.25f;
            var at = face switch { 0 => new Vector3(t, 0f, -BD - off), 1 => new Vector3(t, 0f, BD + off), 2 => new Vector3(-BW - off, 0f, t), _ => new Vector3(BW + off, 0f, t) };
            bool fromTop = _rng.Randf() < 0.55f;
            float len = _rng.RandfRange(3f, fromTop ? 12f : 9f);
            float yy = fromTop ? 3f * FH - len * 0.5f : len * 0.5f;
            var size = face < 2 ? new Vector3(_rng.RandfRange(0.3f, 1.4f), len, 0.12f) : new Vector3(0.12f, len, _rng.RandfRange(0.3f, 1.4f));
            b.Box("VineLeaf", VineGreen.Lightened(_rng.RandfRange(-0.03f, 0.08f)), at + Vector3.Up * yy, size);
            for (int k = 0; k < 3; k++)
                b.Box("VineLeaf", VineGreen.Lightened(0.06f), at + Vector3.Up * (yy + _rng.RandfRange(-len * 0.45f, len * 0.45f)), new Vector3(0.6f, 0.5f, 0.6f));
        }
        // Bushes heaped against the walls.
        for (int i = 0; i < 60; i++)
        {
            int face = _rng.RandiRange(0, 3);
            float t = face < 2 ? _rng.RandfRange(-BW, BW) : _rng.RandfRange(-BD, BD);
            var at = face switch { 0 => new Vector3(t, 0f, -BD - 1.2f), 1 => new Vector3(t, 0f, BD + 1.2f), 2 => new Vector3(-BW - 4f, 0f, t), _ => new Vector3(BW + 4.2f, 0f, t) };
            if (face == 0 && Mathf.Abs(t) < 9f)
                continue;
            float r = _rng.RandfRange(0.7f, 1.6f);
            b.Box("Crown", VineGreen.Lightened(_rng.RandfRange(0f, 0.1f)), at + Vector3.Up * r * 0.5f, new Vector3(r * 1.6f, r, r * 1.4f), new Basis(Vector3.Up, _rng.Randf() * 3f));
        }
        b.Commit(g);
    }

    // Long corridor tubes; most are dead, a few flicker (group flicker, driven by Main).
    static void HospCorridorLights(Node3D fg, int floor, float y)
    {
        var b = new Batch();
        foreach (var x in new[] { -38f, -26f, -14f, 0f, 14f, 26f, 38f })
        {
            b.Box("MetalLamp", HospMetal.Darkened(0.3f), new Vector3(x, y + FH - Slab - 0.05f, 0f), new Vector3(1.4f, 0.08f, 0.3f));
            if (_rng.Randf() < 0.5f)
            {
                Shapes.Box(fg, new Vector3(x, y + FH - Slab - 0.12f, 0f), new Vector3(1.2f, 0.06f, 0.12f), new Color(0.8f, 0.95f, 0.9f), 1.4f, "GlowTube");
                FlickerLight(fg, new Vector3(x, y + FH - Slab - 0.6f, 0f), new Color(0.75f, 0.95f, 0.88f), 1.1f, 10f);
            }
        }
        b.Commit(fg);
    }

    static void FlickerLight(Node3D parent, Vector3 pos, Color color, float energy, float range)
    {
        var l = Shapes.Light(parent, pos, color, energy, range, "FlickerLight");
        l.AddToGroup("flicker", true);
        l.SetMeta("energy", energy);
    }

    static void HospDoorSign(Node3D fg, string text, Vector3 pos, float yaw)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var s = Shapes.Group(fg, "DoorSign", pos, yaw);
        Shapes.Box(s, Vector3.Zero, new Vector3(1.8f, 0.36f, 0.04f), new Color(0.25f, 0.3f, 0.32f), 0f, "SignBoard");
        HospLabel(s, text, new Vector3(0f, 0f, -0.03f), Mathf.Pi, 22, new Color(0.85f, 0.88f, 0.85f));
    }

    static void HospLabel(Node3D parent, string text, Vector3 pos, float yaw, int size, Color color) =>
        Shapes.Child(parent, new Label3D
        {
            Text = text,
            Font = Game.ThaiFont,
            FontSize = size,
            PixelSize = 0.006f,
            Modulate = color,
            OutlineSize = 0,
            DoubleSided = false,
            Position = pos,
            Rotation = new Vector3(0f, yaw, 0f),
        }, "Text");

    // ---------- 2nd floor: the maternity ward under the broken roof (boss lair) ----------

    static void HospMaternityHall(Batch b, Node3D fg, float y)
    {
        // Rows of beds and cribs, half of them thrown about; rubble and moonlight where the roof fell in.
        for (int i = 0; i < 6; i++)
        {
            Bed(b, new Vector3(-43f + i * 5.5f, y, -9.6f), 0f, _rng.Randf() < 0.35f);
            Bed(b, new Vector3(-43f + i * 5.5f, y, 9.6f), Mathf.Pi, _rng.Randf() < 0.35f);
            Crib(b, new Vector3(-41f + i * 5.5f, y, -7.6f), _rng.RandfRange(-0.4f, 0.4f), _rng.Randf() < 0.4f);
        }
        for (int i = 0; i < 24; i++)
        {
            var p = new Vector3(_rng.RandfRange(RoofHole.Position.X, RoofHole.End.X), y + _rng.RandfRange(0.2f, 0.8f), _rng.RandfRange(RoofHole.Position.Y, RoofHole.End.Y));
            if (Mathf.Abs(p.X + 28f) < 4f && Mathf.Abs(p.Z) < 4f)
                continue; // keep the middle clear for the fight
            b.Box("Rubble", HospPlaster.Darkened(_rng.RandfRange(0.1f, 0.4f)), p, new Vector3(_rng.RandfRange(0.8f, 2.4f), _rng.RandfRange(0.3f, 0.8f), _rng.RandfRange(0.8f, 2f)), new Basis(Vector3.Up, _rng.Randf() * 3f));
        }
        // A circle of baby shoes and dolls round where she appears; candles burnt down to nothing.
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.Tau;
            var p = new Vector3(-28f + Mathf.Cos(a) * 3.2f, y + 0.12f, Mathf.Sin(a) * 3.2f);
            b.Box(i % 2 == 0 ? "ClothDoll" : "ClothShoe", i % 2 == 0 ? new Color(0.8f, 0.72f, 0.7f) : new Color(0.75f, 0.6f, 0.65f), p, i % 2 == 0 ? new Vector3(0.2f, 0.35f, 0.15f) : new Vector3(0.12f, 0.1f, 0.2f), new Basis(Vector3.Up, a));
        }
        Shapes.Light(fg, new Vector3(-28f, y + 6f, 0f), new Color(0.75f, 0.82f, 1f), 1.6f, 18f, "MoonShaft");
        Shapes.Light(fg, new Vector3(-28f, y + 1.2f, 0f), new Color(1f, 0.3f, 0.35f), 1f, 8f, "LairLight");
        HospDoorSign(fg, "หอผู้ป่วยหลังคลอด", new Vector3(-10.3f, y + 3.4f, 0f), -Mathf.Pi * 0.5f);
    }

    // ---------- outside the back: laundry yard, incinerator, a heap of old beds ----------

    static void HospBackYard(Node3D world)
    {
        var g = Shapes.Group(world, "BackYard");
        _hospBody = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "Colliders");
        var b = new Batch();
        var c = new Vector3(0f, 0f, 52f);
        _landmarks.Add((Flat(c), 12f));
        // Incinerator chimney.
        Solid(b, "WallBrick", HospBrick.Darkened(0.15f), c + new Vector3(18f, 1.5f, 6f), new Vector3(4f, 3f, 4f));
        Solid(b, "WallBrick", HospBrick.Darkened(0.2f), c + new Vector3(18f, 8f, 6f), new Vector3(1.6f, 13f, 1.6f));
        b.Box("WindowDark", new Color(0.05f, 0.04f, 0.04f), c + new Vector3(18f, 1.2f, 3.98f), new Vector3(1.4f, 1f, 0.05f));
        // Laundry lines with grey sheets.
        for (int r = 0; r < 3; r++)
        {
            float z = c.Z - 6f + r * 3f;
            b.Box("MetalPole", HospRust, new Vector3(-16f, 1.2f, z), new Vector3(0.08f, 2.4f, 0.08f));
            b.Box("MetalPole", HospRust, new Vector3(-4f, 1.2f, z), new Vector3(0.08f, 2.4f, 0.08f));
            b.Box("Rope", new Color(0.5f, 0.48f, 0.42f), new Vector3(-10f, 2.3f, z), new Vector3(12f, 0.03f, 0.03f));
            for (float x = -15f; x < -5f; x += _rng.RandfRange(1.6f, 3f))
                b.Box("ClothSheet", HospSheet.Darkened(_rng.RandfRange(0.1f, 0.4f)), new Vector3(x, 1.5f, z), new Vector3(1.4f, _rng.RandfRange(1.1f, 1.6f), 0.03f), new Basis(Vector3.Up, _rng.RandfRange(-0.2f, 0.2f)));
        }
        // Heap of dumped bed frames.
        for (int i = 0; i < 6; i++)
            Bed(b, c + new Vector3(30f + _rng.RandfRange(-3f, 3f), i * 0.3f, -2f + _rng.RandfRange(-3f, 3f)), _rng.Randf() * Mathf.Tau, true);
        Occupy(c + new Vector3(30f, 0f, -2f), 6f);
        Occupy(c + new Vector3(18f, 0f, 6f), 4f);
        Occupy(c + new Vector3(-10f, 0f, -3f), 8f);
        Shapes.Light(g, c + new Vector3(-10f, 2f, -3f), new Color(0.6f, 0.8f, 0.9f), 0.8f, 10f, "EerieLight");
        b.Commit(g);
    }

    // Dead trees and tangled scrub through the grounds; the jungle itself is scattered at runtime.
    static void HospWilds(Node3D world)
    {
        var g = Shapes.Group(world, "DeadWood");
        int k = 0;
        for (int i = 0; i < 160 && k < 40; i++)
        {
            var p = new Vector3(_rng.RandfRange(-Half + 8f, Half - 8f), 0f, _rng.RandfRange(-Half + 8f, Half - 8f));
            if (Blocked(p, 2f) || DistToRoads(p) < 3f || NearLandmark(p, 4f) || InHospital(Flat(p), 6f))
                continue;
            if (k % 2 == 0)
                DeadTree(g, $"DeadTree{k}", p, _rng.RandfRange(1f, 1.6f));
            else
                ClawBranch(g, p);
            Occupy(p, 1.5f);
            k++;
        }
    }

    // Grey mist pooled low round the grounds.
    static void HospMist(Node3D world)
    {
        var g = Shapes.Group(world, "Mist");
        for (float x = -Half + 40f; x < Half; x += 80f)
        for (float z = -Half + 40f; z < Half; z += 80f)
            Shapes.Child(g, new FogVolume
            {
                Position = new Vector3(x + _rng.RandfRange(-15f, 15f), 0.8f, z + _rng.RandfRange(-15f, 15f)),
                Size = new Vector3(_rng.RandfRange(70f, 95f), 2.2f, _rng.RandfRange(70f, 95f)),
                Material = new FogMaterial { Density = 0.06f, Albedo = new Color(0.6f, 0.62f, 0.62f) },
            }, "Mist");
    }
}
