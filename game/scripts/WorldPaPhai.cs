using Godot;
using System.Collections.Generic;
using System.Linq;

// ป่าไผ่ทึบ (zone 3, Lv 25–35): an abandoned village swallowed by dense bamboo, east of บึงบัวสาย.
// Rotting stilt houses overgrown with vines line a muddy street; standing water holds the pale moon,
// broken things lie everywhere, dead branches twist out of the ground and a low mist never lifts.
// The bamboo itself is scattered at runtime (BuildWilds) because MultiMesh data does not survive baking.
public static partial class WorldBuilder
{
    static readonly Color Mud = new(0.22f, 0.19f, 0.14f);
    static readonly Color RotWood = new(0.3f, 0.25f, 0.19f);
    static readonly Color RotDark = new(0.2f, 0.17f, 0.13f);
    static readonly Color MossRoof = new(0.25f, 0.26f, 0.18f);
    static readonly Color VineGreen = new(0.16f, 0.24f, 0.12f);

    // Muddy tracks (local coords): in from the marsh on the west, through the village to the
    // headman's house, north to the bamboo shrine and south to the old graveyard.
    static readonly Vector3[][] PaPhaiTracks =
    {
        new Vector3[] { new(-Half, 0f, 30f), new(-160f, 0f, 34f), new(-120f, 0f, 22f), new(-80f, 0f, 26f), new(-40f, 0f, 10f), new(0f, 0f, 0f) },
        new Vector3[] { new(0f, 0f, 0f), new(40f, 0f, -18f), new(80f, 0f, -42f), new(118f, 0f, -52f) },
        new Vector3[] { new(0f, 0f, 0f), new(-10f, 0f, -40f), new(-24f, 0f, -80f), new(-30f, 0f, -108f) },
        new Vector3[] { new(0f, 0f, 0f), new(14f, 0f, 40f), new(30f, 0f, 80f), new(44f, 0f, 112f) },
    };

    public static Node3D BuildPaPhai()
    {
        _rng = new RandomNumberGenerator { Seed = 25351 };
        ResetLayout();
        var world = new Node3D { Name = "Zone_PaPhai" };
        PaPhaiGround(world);
        PaPhaiTracksBuild(world);
        PaPhaiCamp(world);
        PaPhaiVillage(world);
        PaPhaiHeadmanHouse(world);
        PaPhaiShrine(world);
        PaPhaiGraveyard(world);
        PaPhaiWilds(world);
        Puddles(world, 45);
        PaPhaiPools(world);
        PlaceChests(world, "pa_phai", 5, p => true);
        PaPhaiMist(world);
        return world;
    }

    // Mud and moss: a vertex-coloured ground sheet with a flat collider.
    static void PaPhaiGround(Node3D world)
    {
        var noise = new FastNoiseLite { Seed = 3131, Frequency = 0.03f };
        const float cell = 6f;
        int n = (int)(Half * 2f / cell);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Color C(float x, float z)
        {
            float v = noise.GetNoise2D(x, z);
            var c = Mud.Lerp(new Color(0.15f, 0.2f, 0.12f), Mathf.Clamp(v + 0.5f, 0f, 1f));
            return c.Darkened(Mathf.Clamp(-v, 0f, 0.4f));
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
        // The whole zone reads as thicket on the map; tracks and houses are drawn over it.
        Shapes.MapTag(Shapes.Group(ground, "ThicketMap"), "map_grove", new Vector2(Half * 2f, Half * 2f));
    }

    static void PaPhaiTracksBuild(Node3D world)
    {
        var root = Shapes.Group(world, "Tracks");
        int k = 0;
        foreach (var t in PaPhaiTracks)
        {
            var pts = Meander(t, 3f, 5f);
            DrawRoad(root, $"MudTrack{k++}", pts, 3.4f, Mud.Lightened(0.06f));
        }
        // Mud, not cobbles: the strips are named so they take the earth texture.
        foreach (var s in root.FindChildren("Strip*", "MeshInstance3D", true, false))
            s.Name = "MudStrip";
    }

    // The hunter's camp at the edge of the bamboo: the only safe place in the zone.
    static void PaPhaiCamp(Node3D world)
    {
        var c = new Vector3(-148f, 0f, 46f);
        var camp = Shapes.Group(world, "HunterCamp", c);
        Sanctuary(world, c, 22f);
        _landmarks.Add((Flat(c), 16f));
        Occupy(c, 10f);
        // A lean-to of bamboo and old cloth.
        var tent = Shapes.Group(camp, "LeanTo", new Vector3(4f, 0f, 3f), 0.4f);
        foreach (var x in new[] { -1.6f, 1.6f })
            Shapes.Cylinder(tent, new Vector3(x, 1.1f, -1f), 0.06f, 0.06f, 2.2f, new Color(0.42f, 0.4f, 0.24f), 0f, "BambooPole");
        var sheet = Shapes.Box(tent, new Vector3(0f, 1.1f, 0f), new Vector3(3.6f, 0.04f, 2.6f), new Color(0.35f, 0.3f, 0.22f), 0f, "ClothSheet");
        sheet.Rotation = new Vector3(0.75f, 0f, 0f);
        Shapes.Box(tent, new Vector3(0f, 0.08f, 0.2f), new Vector3(2.6f, 0.12f, 1.6f), new Color(0.5f, 0.42f, 0.28f), 0f, "ClothMat");
        Shapes.Solid(tent, new Vector3(0f, 1f, 0f), new Vector3(3.6f, 2f, 2.4f));
        // Campfire with a pot.
        foreach (var a in new[] { 0f, 1.2f, 2.4f, 3.6f, 4.8f })
            Shapes.Sphere(camp, new Vector3(Mathf.Cos(a) * 0.6f, 0.12f, Mathf.Sin(a) * 0.6f), 0.18f, Stone, 0f, "FireStone");
        foreach (var a in new[] { 0.3f, 1.9f, 3.5f })
            Shapes.Cylinder(camp, new Vector3(0f, 0.2f, 0f), 0.05f, 0.06f, 0.9f, DarkWood, 0f, "Firewood").Rotation = new Vector3(1.2f, a, 0f);
        Shapes.Sphere(camp, new Vector3(0f, 0.28f, 0f), 0.18f, new Color(1f, 0.5f, 0.2f), 4f, "GlowEmber");
        Shapes.Light(camp, new Vector3(0f, 1f, 0f), new Color(1f, 0.55f, 0.25f), 2.6f, 12f, "CampfireLight");
        // Drying rack with pelts and a crossbow on a stand.
        Shapes.Box(camp, new Vector3(-4f, 1.4f, -1f), new Vector3(2.4f, 0.06f, 0.06f), DarkWood, 0f, "RackBar");
        foreach (var x in new[] { -5.1f, -2.9f })
            Shapes.Cylinder(camp, new Vector3(x, 0.7f, -1f), 0.05f, 0.05f, 1.4f, DarkWood, 0f, "RackPole");
        foreach (var x in new[] { -4.6f, -3.6f })
            Shapes.Box(camp, new Vector3(x, 1f, -1f), new Vector3(0.6f, 0.8f, 0.03f), new Color(0.45f, 0.33f, 0.22f), 0f, "ClothPelt");
        SmallProp(camp, "CampFirewood", new Vector3(2.5f, 0f, -2.5f), 0.6f, Prop.Firewood);
        SmallProp(camp, "CampJars", new Vector3(-2.5f, 0f, 3.2f), 1.2f, Prop.Jars);
        var shrine = Shapes.Group(world, "Shrine_pa_phai", new Vector3(-166f, 0f, 48f));
        PhraPhumShrine(shrine, "pa_phai", "ศาลทางเข้าป่าไผ่");
        Occupy(new Vector3(-166f, 0f, 48f), 3f);
        SignPost(world, new Vector3(-128f, 0f, 30f), ("บึงบัวสาย", new Vector3(-1f, 0f, 0f)), ("หมู่บ้านร้าง", new Vector3(1f, 0f, -0.3f)),
            ("ศาลเจ้าพ่อไผ่", new Vector3(0.6f, 0f, -1f)));
        LanternPost(world, "CampLantern", new Vector3(-140f, 0f, 36f), 2.4f, true, new Vector3(-140f, 0f, 30f));
    }

    // ---------- the abandoned village ----------

    static void PaPhaiVillage(Node3D world)
    {
        var g = Shapes.Group(world, "AbandonedVillage");
        var c = Vector3.Zero;
        _landmarks.Add((Flat(c), 12f));
        // A dry well and a toppled spirit house in the middle of the street.
        var well = Shapes.Group(g, "Well", new Vector3(-6f, 0f, 6f));
        Shapes.Cylinder(well, new Vector3(0f, 0.45f, 0f), 0.9f, 1f, 0.9f, Stone.Darkened(0.2f), 0f, "WellStone");
        Shapes.Cylinder(well, new Vector3(0f, 0.92f, 0f), 0.7f, 0.7f, 0.04f, new Color(0.03f, 0.04f, 0.04f), 0f, "WellHole");
        foreach (var x in new[] { -0.8f, 0.8f })
            Shapes.Box(well, new Vector3(x, 1.4f, 0f), new Vector3(0.12f, 1.9f, 0.12f), RotDark, 0f, "WellPost");
        Shapes.Box(well, new Vector3(0f, 2.3f, 0f), new Vector3(1.8f, 0.1f, 0.12f), RotDark, 0f, "WellBeam");
        Shapes.Cylinder(well, new Vector3(0.2f, 1.6f, 0f), 0.01f, 0.01f, 1.4f, new Color(0.5f, 0.45f, 0.35f), 0f, "Rope");
        Shapes.Solid(well, new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
        Occupy(well.Position, 2f);
        var spirit = Shapes.Group(g, "FallenSpiritHouse", new Vector3(8f, 0f, -6f), 0.7f);
        Shapes.Cylinder(spirit, new Vector3(0f, 0.15f, 0f), 0.12f, 0.15f, 1.5f, new Color(0.6f, 0.58f, 0.52f), 0f, "Pillar").Rotation = new Vector3(0f, 0f, 1.45f);
        var sh = Shapes.Box(spirit, new Vector3(1.1f, 0.35f, 0.2f), new Vector3(0.9f, 0.7f, 0.8f), new Color(0.45f, 0.18f, 0.14f), 0f, "House");
        sh.Rotation = new Vector3(0.3f, 0.5f, 1.2f);
        Shapes.Prism(spirit, new Vector3(1.6f, 0.3f, 0.6f), new Vector3(1.1f, 0.5f, 1f), new Color(0.5f, 0.42f, 0.2f), "Roof").Rotation = new Vector3(0.2f, 0.3f, 2.2f);
        Occupy(spirit.Position, 2f);

        // Ruined stilt houses on both sides of the tracks near the centre.
        int built = 0;
        for (int attempt = 0; attempt < 200 && built < 14; attempt++)
        {
            float a = _rng.Randf() * Mathf.Tau, r = _rng.RandfRange(12f, 46f);
            var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            if (Blocked(p, 5f) || DistToRoads(p) < 5.5f || DistToRoads(p) > 12f)
                continue;
            // Face the nearest track.
            var road = RoadsideSpot(p, 0f);
            float yaw = Mathf.Atan2(road.X - p.X, road.Z - p.Z) + Mathf.Pi;
            RuinedStiltHouse(g, $"RuinedHouse{built}", p, yaw, _rng.RandfRange(0.9f, 1.15f), _rng.Randf() < 0.22f);
            built++;
        }
        // Junk in the street.
        for (int i = 0; i < 40; i++)
        {
            var p = new Vector3(_rng.RandfRange(-50f, 50f), 0f, _rng.RandfRange(-50f, 50f));
            if (Blocked(p, 1f) || DistToRoads(p) > 8f)
                continue;
            Debris(g, p);
        }
        foreach (var (pos, kind) in new[] { (new Vector3(14f, 0f, 12f), Prop.Cart), (new Vector3(-18f, 0f, -12f), Prop.Barrel), (new Vector3(20f, 0f, -16f), Prop.Jars) })
        {
            var prop = Shapes.Group(g, $"Wreck{kind}", pos, _rng.Randf() * Mathf.Tau);
            SmallProp(prop, kind.ToString(), Vector3.Zero, 0f, kind);
            prop.Rotation = new Vector3(_rng.RandfRange(-0.25f, 0.25f), prop.Rotation.Y, _rng.RandfRange(0.2f, 0.5f));
            Occupy(pos, 2f);
        }
    }

    // A rotting stilt house: leaning posts, gappy plank walls, a sagging moss-covered roof, a broken
    // ladder, rags on the rail and vines everywhere. Some have already fallen in.
    static void RuinedStiltHouse(Node3D parent, string name, Vector3 pos, float yaw, float s, bool collapsed)
    {
        var h = Shapes.Group(parent, name, pos, yaw);
        float w = 4.6f * s, d = 5.6f * s, stilt = 2.2f, wallH = 2.3f * s;
        Shapes.MapTag(Shapes.Group(h, "HouseMap"), "map_house", new Vector2(w + 1f, d + 1f));
        Occupy(pos, Mathf.Max(w, d) * 0.6f + 1.5f);
        float tilt = collapsed ? 0.25f : _rng.RandfRange(-0.04f, 0.04f);
        // Stilts (one snapped short on a collapsed house).
        int k = 0;
        foreach (var x in new[] { -w * 0.45f, 0f, w * 0.45f })
        foreach (var z in new[] { -d * 0.45f, d * 0.45f })
        {
            float len = collapsed && k == 0 ? 1f : stilt + (collapsed && z < 0f ? -0.6f : 0f);
            var post = Shapes.Cylinder(h, new Vector3(x, len * 0.5f, z), 0.1f, 0.13f, len, RotDark, 0f, "Stilt");
            post.Rotation = new Vector3(_rng.RandfRange(-0.06f, 0.06f), 0f, _rng.RandfRange(-0.06f, 0.06f));
            Shapes.Solid(h, new Vector3(x, len * 0.5f, z), new Vector3(0.28f, len, 0.28f));
            k++;
        }
        var body = Shapes.Group(h, "Body", new Vector3(0f, collapsed ? 1.4f : stilt, 0f));
        body.Rotation = new Vector3(tilt, 0f, collapsed ? 0.12f : tilt * 0.5f);
        Shapes.Box(body, new Vector3(0f, 0f, 0f), new Vector3(w + 1.2f, 0.14f, d), RotWood, 0f, "PlankFloor");
        // Walls: plank panels with gaps, a dark doorway and an empty window.
        void Panel(Vector3 at, Vector3 size) => Shapes.Box(body, at, size, RotWood.Darkened(_rng.RandfRange(0f, 0.15f)), 0f, "WallPlank");
        float fz = -d * 0.5f + 0.05f;
        Panel(new Vector3(-w * 0.32f, wallH * 0.5f, fz), new Vector3(w * 0.36f, wallH, 0.08f));
        Panel(new Vector3(w * 0.32f, wallH * 0.5f, fz), new Vector3(w * 0.36f, wallH, 0.08f));
        Panel(new Vector3(0f, wallH * 0.85f, fz), new Vector3(w * 0.3f, wallH * 0.3f, 0.08f));
        Shapes.Box(body, new Vector3(0f, wallH * 0.36f, fz + 0.3f), new Vector3(w * 0.28f, wallH * 0.7f, 0.04f), new Color(0.02f, 0.02f, 0.02f), 0f, "WindowDark");
        Panel(new Vector3(0f, wallH * 0.5f, d * 0.5f - 0.05f), new Vector3(w, wallH, 0.08f));
        foreach (var sx in new[] { -1f, 1f })
        {
            // Side walls with a missing board or two.
            Panel(new Vector3(sx * w * 0.5f, wallH * 0.3f, 0f), new Vector3(0.08f, wallH * 0.6f, d));
            if (_rng.Randf() < 0.6f)
                Panel(new Vector3(sx * w * 0.5f, wallH * 0.85f, d * 0.2f), new Vector3(0.08f, wallH * 0.3f, d * 0.55f));
            Shapes.Box(body, new Vector3(sx * w * 0.5f + sx * 0.03f, wallH * 0.62f, -d * 0.2f), new Vector3(0.04f, 0.5f, 0.7f), new Color(0.02f, 0.02f, 0.02f), 0f, "WindowDark");
        }
        // Veranda rail with hanging rags.
        Shapes.Box(body, new Vector3(0f, 0.55f, -d * 0.5f - 0.55f), new Vector3(w + 1.1f, 0.08f, 0.08f), RotDark, 0f, "Rail");
        for (int i = 0; i < 3; i++)
        {
            var rag = Shapes.Box(body, new Vector3(_rng.RandfRange(-w * 0.45f, w * 0.45f), 0.2f, -d * 0.5f - 0.55f), new Vector3(0.5f, 0.75f, 0.02f),
                new Color(0.42f, 0.36f, 0.3f).Lerp(new Color(0.45f, 0.18f, 0.15f), _rng.Randf() * 0.5f), 0f, "ClothRag");
            rag.Rotation = new Vector3(0f, 0f, _rng.RandfRange(-0.2f, 0.2f));
        }
        // Sagging gable roof, one side holed.
        float roofY = wallH + 0.9f * s;
        foreach (var sx in new[] { -1f, 1f })
        {
            float len = sx > 0f && _rng.Randf() < 0.5f ? d * 0.6f : d + 0.8f;
            var roof = Shapes.Box(body, new Vector3(sx * w * 0.28f, roofY - 0.1f, (d + 0.8f - len) * 0.5f), new Vector3(w * 0.66f, 0.14f, len), MossRoof.Darkened(_rng.RandfRange(0f, 0.15f)), 0f, "Roof");
            roof.Rotation = new Vector3(0f, 0f, -sx * (0.55f + _rng.RandfRange(0f, 0.12f)));
            for (int m = 0; m < 3; m++)
                Shapes.Box(body, new Vector3(sx * w * _rng.RandfRange(0.15f, 0.4f), roofY + 0.02f, _rng.RandfRange(-d * 0.4f, d * 0.4f)), new Vector3(0.8f, 0.06f, 0.9f), VineGreen, 0f, "RoofMoss")
                    .Rotation = roof.Rotation;
        }
        // Ladder with a missing rung, leaning off the veranda.
        var ladder = Shapes.Group(h, "Ladder", new Vector3(w * 0.3f, 0f, -d * 0.5f - 1.4f));
        ladder.Rotation = new Vector3(-0.45f, 0f, 0.05f);
        foreach (var x in new[] { -0.3f, 0.3f })
            Shapes.Box(ladder, new Vector3(x, 1.2f, 0f), new Vector3(0.07f, 2.6f, 0.07f), RotDark, 0f, "StairRail");
        for (int i = 0; i < 6; i++)
            if (i != 3)
                Shapes.Box(ladder, new Vector3(0f, 0.3f + i * 0.4f, 0f), new Vector3(0.6f, 0.05f, 0.1f), RotWood, 0f, "StairStep");
        // Vines climbing the stilts and hanging from the eaves.
        for (int i = 0; i < 7; i++)
        {
            var at = new Vector3(_rng.RandfRange(-w * 0.55f, w * 0.55f), 0f, _rng.Randf() < 0.5f ? -d * 0.5f : d * 0.5f);
            float len = _rng.RandfRange(1.2f, 3f);
            var vine = Shapes.Box(h, at + new Vector3(0f, stilt + wallH - len * 0.5f, 0f), new Vector3(0.05f, len, 0.05f), VineGreen, 0f, "Vine");
            vine.Rotation = new Vector3(0f, 0f, _rng.RandfRange(-0.15f, 0.15f));
            for (int l = 0; l < 3; l++)
                Shapes.Sphere(h, vine.Position + new Vector3(_rng.RandfRange(-0.12f, 0.12f), _rng.RandfRange(-len * 0.4f, len * 0.4f), _rng.RandfRange(-0.12f, 0.12f)), _rng.RandfRange(0.12f, 0.22f), VineGreen.Lightened(0.05f), 0f, "VineLeaf");
        }
        // Someone is still at home: a faint pair of eyes in the dark doorway.
        if (_rng.Randf() < 0.3f)
        {
            Shapes.Sphere(body, new Vector3(-0.1f, wallH * 0.55f, fz + 0.25f), 0.03f, new Color(0.7f, 0.9f, 0.85f), 3f, "GlowEyeL");
            Shapes.Sphere(body, new Vector3(0.1f, wallH * 0.55f, fz + 0.25f), 0.03f, new Color(0.7f, 0.9f, 0.85f), 3f, "GlowEyeR");
        }
        // The house above the posts blocks movement; you can still duck between the stilts.
        if (!collapsed)
            Shapes.Solid(h, new Vector3(0f, stilt + wallH * 0.5f + 0.1f, 0f), new Vector3(w + 1.2f, wallH + 0.4f, d));
        else
        {
            Shapes.Solid(h, new Vector3(0f, 1.6f, 0f), new Vector3(w + 1.2f, 2.4f, d));
            for (int i = 0; i < 6; i++)
                Shapes.Box(h, new Vector3(_rng.RandfRange(-w, w), 0.08f, _rng.RandfRange(-d * 0.7f, -d * 0.4f)), new Vector3(_rng.RandfRange(1.2f, 2.6f), 0.08f, 0.25f), RotWood, 0f, "Plank")
                    .Rotation = new Vector3(0f, _rng.Randf() * Mathf.Tau, 0f);
        }
    }

    // Broken planks, cracked jars, a lost basket, a bent branch clawing out of the mud.
    static void Debris(Node3D parent, Vector3 p)
    {
        float roll = _rng.Randf();
        if (roll < 0.4f)
        {
            for (int i = 0; i < 3; i++)
                Shapes.Box(parent, p + new Vector3(_rng.RandfRange(-0.8f, 0.8f), 0.05f + i * 0.06f, _rng.RandfRange(-0.8f, 0.8f)), new Vector3(_rng.RandfRange(0.8f, 2f), 0.06f, 0.22f), RotWood, 0f, "Plank")
                    .Rotation = new Vector3(0f, _rng.Randf() * Mathf.Tau, _rng.RandfRange(-0.1f, 0.1f));
        }
        else if (roll < 0.6f)
        {
            var jar = Shapes.Cylinder(parent, p + new Vector3(0f, 0.25f, 0f), 0.2f, 0.25f, 0.5f, Clay.Darkened(0.25f), 0f, "ClayJar");
            jar.Rotation = new Vector3(_rng.Randf() < 0.5f ? Mathf.Pi * 0.5f : 0f, _rng.Randf() * Mathf.Tau, 0f);
        }
        else if (roll < 0.75f)
            Shapes.Cylinder(parent, p + new Vector3(0f, 0.2f, 0f), 0.35f, 0.25f, 0.4f, Straw.Darkened(0.35f), 0f, "BasketWeave").Rotation = new Vector3(0.6f, 0f, 0.9f);
        else
            ClawBranch(parent, p);
        Occupy(p, 0.8f);
    }

    // A dead branch bending up out of the mud like a hand.
    static void ClawBranch(Node3D parent, Vector3 p)
    {
        var bark = new Color(0.18f, 0.16f, 0.14f);
        var a = p;
        var dir = new Vector3(_rng.RandfRange(-0.4f, 0.4f), 1f, _rng.RandfRange(-0.4f, 0.4f)).Normalized();
        for (int i = 0; i < 4; i++)
        {
            float len = _rng.RandfRange(0.6f, 1.1f);
            var b = a + dir * len;
            RootSegmentThick(parent, a, b, bark, 0.09f - i * 0.018f);
            dir = (dir + new Vector3(_rng.RandfRange(-0.7f, 0.7f), -0.1f, _rng.RandfRange(-0.7f, 0.7f))).Normalized();
            if (i == 2)
                for (int f = 0; f < 3; f++)
                    RootSegmentThick(parent, b, b + new Vector3(_rng.RandfRange(-0.6f, 0.6f), _rng.RandfRange(0.2f, 0.6f), _rng.RandfRange(-0.6f, 0.6f)), bark, 0.025f);
            a = b;
        }
    }

    // ---------- the headman's house (boss lair) ----------

    static void PaPhaiHeadmanHouse(Node3D world)
    {
        var at = new Vector3(122f, 0f, -76f);
        var g = Shapes.Group(world, "HeadmanHouse");
        RuinedStiltHouse(g, "HeadmanHouse", at, Mathf.Pi, 1.65f, false); // facing the track to the south
        _landmarks.Add((Flat(at), 16f));
        // Red cloth strips and a row of skull-white offering bowls in front: the ปอบ's lair.
        for (int i = 0; i < 6; i++)
            Shapes.Box(g, at + new Vector3(-4f + i * 1.6f, 1.6f, 6.5f), new Vector3(0.25f, 1.2f, 0.02f), new Color(0.55f, 0.08f, 0.08f), 0f, "ClothStrip");
        Shapes.Box(g, at + new Vector3(0f, 2.3f, 6.5f), new Vector3(10f, 0.05f, 0.05f), new Color(0.5f, 0.45f, 0.35f), 0f, "Rope");
        for (int i = 0; i < 5; i++)
            Shapes.Cylinder(g, at + new Vector3(-3f + i * 1.5f, 0.1f, 9.5f), 0.22f, 0.14f, 0.18f, new Color(0.82f, 0.8f, 0.72f), 0f, "OfferingBowl");
        Shapes.Light(g, at + new Vector3(0f, 3f, 4f), new Color(1f, 0.2f, 0.15f), 1.8f, 14f, "LairLight");
        for (int i = 0; i < 6; i++)
            DeadTree(g, $"LairTree{i}", at + new Vector3(_rng.RandfRange(-16f, 16f), 0f, _rng.RandfRange(-14f, -9f)), _rng.RandfRange(0.9f, 1.4f));
    }

    // ศาลเจ้าพ่อไผ่: an old spirit shrine in a bamboo clearing to the north (a place to bind).
    static void PaPhaiShrine(Node3D world)
    {
        var p = new Vector3(-32f, 0f, -114f);
        var s = Shapes.Group(world, "Shrine_pa_phai_north", p);
        PhraPhumShrine(s, "pa_phai_north", "ศาลเจ้าพ่อไผ่");
        Sanctuary(world, p, 12f);
        _landmarks.Add((Flat(p), 12f));
        Occupy(p, 4f);
        foreach (var x in new[] { -3f, 3f })
        {
            Shapes.Cylinder(world, p + new Vector3(x, 1.6f, 2f), 0.05f, 0.05f, 3.2f, new Color(0.42f, 0.4f, 0.24f), 0f, "BambooPole");
            Shapes.Box(world, p + new Vector3(x, 2.4f, 2.05f), new Vector3(0.35f, 1.4f, 0.02f), x < 0f ? new Color(0.8f, 0.7f, 0.3f) : new Color(0.7f, 0.18f, 0.15f), 0f, "ClothTung");
        }
    }

    // ป่าช้าเก่า: tilted grave posts and slabs in the south, where the ปอบ come at night.
    static void PaPhaiGraveyard(Node3D world)
    {
        var c = new Vector3(48f, 0f, 124f);
        var g = Shapes.Group(world, "OldGraveyard", c);
        Shapes.MapTag(Shapes.Group(g, "GraveMap"), "map_graveyard", new Vector2(26f, 22f));
        _landmarks.Add((Flat(c), 12f));
        for (int i = 0; i < 18; i++)
        {
            var p = new Vector3(_rng.RandfRange(-11f, 11f), 0f, _rng.RandfRange(-9f, 9f));
            if (_rng.Randf() < 0.5f)
            {
                var post = Shapes.Box(g, p + new Vector3(0f, 0.55f, 0f), new Vector3(0.22f, 1.1f, 0.08f), RotDark, 0f, "GravePost");
                post.Rotation = new Vector3(_rng.RandfRange(-0.3f, 0.3f), _rng.Randf(), _rng.RandfRange(-0.3f, 0.3f));
            }
            else
            {
                var slab = Shapes.Box(g, p + new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.24f, 1.6f), Stone.Darkened(0.3f), 0f, "Slab");
                slab.Rotation = new Vector3(_rng.RandfRange(-0.1f, 0.1f), _rng.Randf(), _rng.RandfRange(-0.15f, 0.15f));
            }
        }
        for (int i = 0; i < 4; i++)
            ClawBranch(g, new Vector3(_rng.RandfRange(-12f, 12f), 0f, _rng.RandfRange(-10f, 10f)));
        Shapes.Light(g, new Vector3(0f, 1.2f, 0f), new Color(0.5f, 0.9f, 0.7f), 1.2f, 12f, "EerieLight");
        Occupy(c, 12f);
    }

    // Twisted dead trees and claw branches through the thicket, wisps of ghost light between them.
    static void PaPhaiWilds(Node3D world)
    {
        var g = Shapes.Group(world, "DeadWood");
        int k = 0;
        for (int i = 0; i < 160 && k < 60; i++)
        {
            var p = new Vector3(_rng.RandfRange(-Half + 8f, Half - 8f), 0f, _rng.RandfRange(-Half + 8f, Half - 8f));
            if (Blocked(p, 2f) || DistToRoads(p) < 3f || NearLandmark(p, 4f))
                continue;
            if (k % 3 == 0)
                DeadTree(g, $"TwistedTree{k}", p, _rng.RandfRange(0.9f, 1.5f));
            else
                ClawBranch(g, p);
            Occupy(p, 1.5f);
            k++;
        }
        for (int i = 0; i < 14; i++)
        {
            var p = new Vector3(_rng.RandfRange(-Half + 20f, Half - 20f), _rng.RandfRange(1.2f, 2.6f), _rng.RandfRange(-Half + 20f, Half - 20f));
            Shapes.Sphere(g, p, 0.12f, new Color(0.6f, 1f, 0.75f), 4f, "GlowWisp");
            Shapes.Light(g, p, new Color(0.5f, 1f, 0.7f), 0.8f, 6f, "WispLight");
        }
    }

    // Wide stagnant pools in the low ground, dark and glassy so the moon shows in them.
    static void PaPhaiPools(Node3D world)
    {
        var g = Shapes.Group(world, "Pools");
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.1f, 0.1f, 0.85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.02f, Metallic = 0.5f };
        int made = 0;
        for (int i = 0; i < 200 && made < 16; i++)
        {
            var p = new Vector3(_rng.RandfRange(-Half + 15f, Half - 15f), 0f, _rng.RandfRange(-Half + 15f, Half - 15f));
            float rx = _rng.RandfRange(2.5f, 6f), rz = _rng.RandfRange(2f, 4.5f);
            if (Blocked(p, rx) || DistToRoads(p) < rx + 1.5f || NearLandmark(p, rx))
                continue;
            Shapes.Child(g, new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.02f, RadialSegments = 24 },
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = p + new Vector3(0f, 0.04f, 0f),
                Scale = new Vector3(rx, 1f, rz),
                Rotation = new Vector3(0f, _rng.Randf() * Mathf.Tau, 0f),
            }, "PoolWater");
            Shapes.MapTag(Shapes.Group(g, "PoolMap", p), "map_water", new Vector2(rx * 2f, rz * 2f));
            Occupy(p, Mathf.Max(rx, rz));
            made++;
        }
    }

    // Thick, low ground mist all over, greener and heavier than the marsh's.
    static void PaPhaiMist(Node3D world)
    {
        var g = Shapes.Group(world, "Mist");
        for (float x = -Half + 40f; x < Half; x += 75f)
        for (float z = -Half + 40f; z < Half; z += 75f)
            Shapes.Child(g, new FogVolume
            {
                Position = new Vector3(x + _rng.RandfRange(-15f, 15f), 1f, z + _rng.RandfRange(-15f, 15f)),
                Size = new Vector3(_rng.RandfRange(70f, 95f), 2.6f, _rng.RandfRange(70f, 95f)),
                Material = new FogMaterial { Density = 0.09f, Albedo = new Color(0.55f, 0.62f, 0.56f) },
            }, "Mist");
    }
}
