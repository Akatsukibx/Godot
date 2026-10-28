using Godot;
using System.Collections.Generic;
using System.Linq;

// บึงบัวสาย (zone 2, Lv 15–25): a misty marsh north of the lagoon. Irregular islands rise out of
// shallow, lily-choked water; deep channels between them are crossed on winding boardwalks. Water
// lilies are scattered at runtime (BuildLilies) because MultiMesh data does not survive baking.
public static partial class WorldBuilder
{
    const float BuengDeep = -1.6f;

    // (centre, radius, level): islands are above the water (y = 0), marsh blobs are wadeable shallows.
    static readonly (Vector2 c, float r, float level)[] BuengBlobs =
    {
        (new(-30f, 140f), 26f, 0.35f), (new(-95f, 60f), 34f, 0.38f), (new(70f, 20f), 22f, 0.36f),
        (new(-110f, -110f), 40f, 0.34f), (new(90f, -120f), 30f, 0.36f), (new(25f, 166f), 20f, 0.3f),
        (new(-150f, 10f), 10f, 0.3f), (new(140f, 90f), 12f, 0.3f), (new(-40f, -150f), 9f, 0.3f), (new(150f, -40f), 11f, 0.3f),
        (new(-20f, 62f), 8f, 0.3f), (new(40f, -92f), 9f, 0.3f), (new(120f, 150f), 12f, 0.3f), (new(-150f, 150f), 14f, 0.3f),
        (new(10f, -20f), 65f, -0.55f), (new(-60f, 20f), 30f, -0.7f), (new(40f, 110f), 28f, -0.6f),
        (new(-140f, -40f), 26f, -0.65f), (new(130f, 40f), 30f, -0.6f), (new(-5f, 115f), 22f, -0.75f),
    };

    static readonly FastNoiseLite BuengNoise = new() { Seed = 4242, Frequency = 0.035f };

    // Ground height at local (x, z); the water surface is y = 0.
    public static float BuengHeight(float x, float z)
    {
        float n = BuengNoise.GetNoise2D(x, z);
        float h = BuengDeep + n * 0.2f;
        foreach (var (c, r, level) in BuengBlobs)
        {
            float d = new Vector2(x, z).DistanceTo(c) + n * r * 0.3f;
            h = Mathf.Max(h, Mathf.Lerp(BuengDeep, level, Mathf.SmoothStep(r, r * 0.65f, d)));
        }
        float edge = Mathf.Min(Half - Mathf.Abs(x), Half - Mathf.Abs(z));
        h = Mathf.Max(h, Mathf.Lerp(BuengDeep, 0.12f, Mathf.SmoothStep(16f, 5f, edge)));
        h = Mathf.Max(h, Mathf.Lerp(BuengDeep, 0.12f, Mathf.SmoothStep(150f, 168f, z)));
        return h;
    }

    static Vector3 OnGround(float x, float z) => new(x, Mathf.Max(0f, BuengHeight(x, z)), z);

    public static Node3D BuildBuengBua()
    {
        _rng = new RandomNumberGenerator { Seed = 15251 };
        ResetLayout();
        var world = new Node3D { Name = "Zone_BuengBua" };
        BuildBuengTerrain(world);
        BuildBuengWater(world);
        BuildBoardwalks(world);
        BuildBuengHamlet(world);
        BuildBuengSights(world);
        BuildBuengTrees(world);
        BuildFireflies(world, Half);
        return world;
    }

    static void BuildBuengTerrain(Node3D world)
    {
        const float cell = 3f;
        int n = (int)(Half * 2f / cell);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 V(int i, int j)
        {
            float x = -Half + i * cell, z = -Half + j * cell;
            return new Vector3(x, BuengHeight(x, z), z);
        }
        Color C(Vector3 v)
        {
            if (v.Y < 0f)
                return new Color(0.16f, 0.15f, 0.1f).Lerp(new Color(0.06f, 0.07f, 0.06f), Mathf.Clamp(-v.Y / 1.6f, 0f, 1f));
            return new Color(0.27f, 0.24f, 0.16f).Lerp(new Color(0.19f, 0.25f, 0.15f), Mathf.Clamp(v.Y / 0.3f, 0f, 1f));
        }
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            var a = V(i, j); var b = V(i + 1, j); var c = V(i + 1, j + 1); var d = V(i, j + 1);
            foreach (var v in new[] { a, b, c, a, c, d })
            {
                st.SetColor(C(v));
                st.AddVertex(v);
            }
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        var ground = Shapes.Group(world, "Ground");
        Shapes.Child(ground, new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f },
        }, "Terrain");
        var body = Shapes.Child(ground, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 }, "TerrainCollider");
        Shapes.Child(body, new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });

        // Islands on the map (approximate squares; the water fills the rest).
        foreach (var (c, r, level) in BuengBlobs.Where(b => b.level > 0f))
            Shapes.MapTag(Shapes.Group(ground, "Island", new Vector3(c.X, 0f, c.Y)), "map_land", new Vector2(r * 1.5f, r * 1.5f));
        Shapes.MapTag(Shapes.Group(ground, "SouthShore", new Vector3(0f, 0f, 176f)), "map_land", new Vector2(Half * 2f, 20f));
    }

    static void BuildBuengWater(Node3D world)
    {
        var water = Shapes.Group(world, "Marsh");
        Shapes.MapTag(water, "map_water", new Vector2(Half * 2f, Half * 2f));
        water.AddToGroup("water_area", true);
        var surface = Shapes.Child(water, new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(Half * 2f, Half * 2f), SubdivideWidth = 110, SubdivideDepth = 110 },
            MaterialOverride = WaterMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        }, "Water");
        surface.AddToGroup("water_surface", true);

        // Lily fields, filled in at runtime.
        foreach (var (c, size) in new[]
                 {
                     (new Vector3(10f, 0f, -20f), new Vector2(130f, 130f)), (new Vector3(-60f, 0f, 20f), new Vector2(60f, 60f)),
                     (new Vector3(40f, 0f, 110f), new Vector2(56f, 56f)), (new Vector3(-140f, 0f, -40f), new Vector2(52f, 52f)),
                     (new Vector3(130f, 0f, 40f), new Vector2(60f, 60f)), (new Vector3(-5f, 0f, 115f), new Vector2(46f, 46f)),
                     (new Vector3(-20f, 0f, 165f), new Vector2(70f, 26f)),
                 })
        {
            var field = Shapes.Group(water, "LilyField", c);
            field.AddToGroup("lily_field", true);
            field.SetMeta("map_size", size);
        }

        // Low mist hanging over the water.
        for (int i = 0; i < 10; i++)
        {
            var p = new Vector3(_rng.RandfRange(-150f, 150f), 1.2f, _rng.RandfRange(-150f, 140f));
            Shapes.Child(water, new FogVolume
            {
                Position = p,
                Size = new Vector3(_rng.RandfRange(40f, 70f), 3f, _rng.RandfRange(40f, 70f)),
                Material = new FogMaterial { Density = 0.06f, Albedo = new Color(0.65f, 0.75f, 0.85f) },
            }, "Mist");
        }
    }

    // Winding boardwalks linking the islands (decks at DeckTop over the water).
    static readonly Vector3[][] BuengWalks =
    {
        new Vector3[] { new(0f, 0f, Half), new(-6f, 0f, 160f), new(-30f, 0f, 140f) },
        new Vector3[] { new(-30f, 0f, 140f), new(-62f, 0f, 102f), new(-95f, 0f, 60f) },
        new Vector3[] { new(-30f, 0f, 140f), new(8f, 0f, 92f), new(30f, 0f, 42f), new(70f, 0f, 20f) },
        new Vector3[] { new(-95f, 0f, 60f), new(-116f, 0f, 2f), new(-110f, 0f, -100f) },
        new Vector3[] { new(70f, 0f, 20f), new(98f, 0f, -48f), new(90f, 0f, -110f) },
        new Vector3[] { new(30f, 0f, 42f), new(8f, 0f, -18f), new(-44f, 0f, -62f), new(-100f, 0f, -104f) },
        new Vector3[] { new(-6f, 0f, 160f), new(25f, 0f, 168f) },
    };

    static void BuildBoardwalks(Node3D world)
    {
        var root = Shapes.Group(world, "Boardwalks");
        int k = 0, lamp = 0;
        foreach (var route in BuengWalks)
        {
            var pts = route.Length == 2 ? route.ToList() : Meander(route, 7f, 6f);
            var walk = Shapes.Group(root, $"Walk{k++}");
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var d = b - a;
                float len = d.Length();
                var mid = (a + b) * 0.5f;
                var seg = Shapes.Group(walk, "Segment", new Vector3(mid.X, 0f, mid.Z), Mathf.Atan2(d.X, d.Z));
                Shapes.MapTag(seg, "map_deck", new Vector2(2.2f, len));
                Shapes.Box(seg, new Vector3(0f, DeckTop - 0.08f, 0f), new Vector3(2.2f, 0.16f, len + 0.25f), Wood.Darkened(_rng.RandfRange(0f, 0.12f)), 0f, "Planks");
                Shapes.Solid(seg, new Vector3(0f, DeckTop - 0.1f, 0f), new Vector3(2.2f, 0.2f, len + 0.25f), "DeckCollider");
                for (float t = -len * 0.5f + 0.45f; t < len * 0.5f; t += 0.55f)
                    Shapes.Box(seg, new Vector3(0f, DeckTop + 0.001f, t), new Vector3(2.2f, 0.004f, 0.03f), DarkWood, 0f, "Seam");
                foreach (var side in new[] { -1f, 1f })
                {
                    float bed = BuengHeight(a.X, a.Z);
                    float top = DeckTop - 0.1f;
                    if (bed < top - 0.1f)
                        Shapes.Cylinder(seg, new Vector3(side * 1f, (top + bed - 0.3f) * 0.5f, -len * 0.5f), 0.09f, 0.1f, top - bed + 0.3f, DarkWood, 0f, "Piling");
                }
                AddRoad(a, b, 1.1f);
                if (i > 0)
                {
                    // A round landing at every bend so the walk has no notches to catch on.
                    Shapes.Cylinder(walk, new Vector3(a.X, DeckTop - 0.08f, a.Z), 1.15f, 1.15f, 0.16f, Wood.Darkened(0.06f), 0f, "Planks");
                    var joint = Shapes.Child(walk, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = new Vector3(a.X, DeckTop - 0.1f, a.Z) }, "JointCollider");
                    Shapes.Child(joint, new CollisionShape3D { Shape = new CylinderShape3D { Radius = 1.15f, Height = 0.2f } });
                }
                if (i % 5 == 2)
                {
                    float side = lamp++ % 2 == 0 ? 1.25f : -1.25f;
                    var right = new Vector3(d.Z, 0f, -d.X) / len;
                    LanternPost(walk, $"WalkLantern{lamp}", new Vector3(mid.X, DeckTop, mid.Z) + right * side, 2.4f, lamp % 3 == 0);
                }
            }
        }
    }

    // A small fishing hamlet on the western island: stilt houses facing a yard, with paths to the boardwalk.
    static void BuildBuengHamlet(Node3D world)
    {
        var g = Shapes.Group(world, "BuengHamlet");
        var c = new Vector3(-95f, 0f, 60f);
        Sanctuary(g, c, 32f);
        foreach (var n in GameData.Npcs.Values.Where(n => n.Zone == "bueng_bua"))
            Occupy(new Vector3(n.X, 0f, n.Z), n.Activity == "wander" ? n.WanderRadius + 1f : 2.5f);
        _landmarks.Add((Flat(c), 20f));
        int built = 0;
        for (int attempt = 0; attempt < 120 && built < 7; attempt++)
        {
            float a = _rng.Randf() * Mathf.Tau, r = _rng.RandfRange(9f, 24f);
            var p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            var kind = _rng.Randf() < 0.6f ? (_rng.Randf() < 0.5f ? HouseKind.StiltKalae : HouseKind.Stilt) : (_rng.Randf() < 0.5f ? HouseKind.Hut : HouseKind.Twin);
            float rad = HouseRadius(kind);
            bool onLand = Enumerable.Range(0, 8).All(i =>
            {
                float aa = i / 8f * Mathf.Tau;
                return BuengHeight(p.X + Mathf.Cos(aa) * rad, p.Z + Mathf.Sin(aa) * rad) > 0.15f;
            });
            if (onLand && TryHouse(g, $"MarshHouse{built}", p, kind, OnGround(p.X, p.Z).Y))
                built++;
        }
        for (int i = 0; i < 3; i++)
            Boat(g, $"HamletBoat{i}", new Vector3(c.X + 34f + i * 3f, 0f, c.Z - 8f + i * 5f), 0.4f + i * 0.3f, i == 0);
    }

    static void BuildBuengSights(Node3D world)
    {
        var s = Shapes.Group(world, "Sights");

        // Shrine and signpost where the boardwalk from the lagoon arrives.
        var shrine = Shapes.Group(s, "ShrineBueng", OnGround(25f, 172f));
        PhraPhumShrine(shrine, "bueng_bua", "ศาลริมบึง");
        Occupy(new Vector3(25f, 0f, 172f), 3f);
        Sanctuary(s, new Vector3(25f, 0f, 172f), 16f);
        _landmarks.Add((new Vector2(25f, 172f), 16f));
        SignPost(s, OnGround(-9f, 170f), ("ทุ่งนาหุ่นไล่กา", new Vector3(0.2f, 0f, 1f)), ("หมู่บ้านชาวบึง", new Vector3(-1f, 0f, -0.7f)),
            ("ศาลาร้างกลางบึง", new Vector3(1f, 0f, -1.2f)));

        // ศาลาร้าง: a ruined sala and a broken chedi on the eastern island.
        var ruin = Shapes.Group(s, "RuinedSala", OnGround(70f, 20f));
        _landmarks.Add((new Vector2(70f, 20f), 18f));
        var white = new Color(0.72f, 0.7f, 0.66f);
        foreach (var x in new[] { -3f, 3f })
        foreach (var z in new[] { -4f, 4f })
        {
            float hgt = _rng.RandfRange(1.2f, 3.4f);
            Shapes.Cylinder(ruin, new Vector3(x, hgt * 0.5f, z), 0.22f, 0.26f, hgt, white.Darkened(_rng.RandfRange(0.1f, 0.35f)), 0f, "Pillar");
            Shapes.Solid(ruin, new Vector3(x, hgt * 0.5f, z), new Vector3(0.5f, hgt, 0.5f));
        }
        Shapes.Box(ruin, new Vector3(0f, 0.15f, 0f), new Vector3(8f, 0.3f, 10f), white.Darkened(0.4f), 0f, "Floor");
        Shapes.Solid(ruin, new Vector3(0f, 0.15f, 0f), new Vector3(8f, 0.3f, 10f));
        var roof = Shapes.Box(ruin, new Vector3(1.5f, 3.2f, 2f), new Vector3(5f, 0.15f, 6f), Tile.Darkened(0.3f), 0f, "FallenRoof");
        roof.Rotation = new Vector3(0.3f, 0.2f, -0.25f);
        var chedi = Shapes.Group(ruin, "BrokenChedi", new Vector3(-9f, 0f, -6f));
        Shapes.Box(chedi, new Vector3(0f, 0.6f, 0f), new Vector3(3.6f, 1.2f, 3.6f), white.Darkened(0.3f), 0f, "Base");
        Shapes.Cylinder(chedi, new Vector3(0f, 2.1f, 0f), 1.1f, 1.5f, 1.8f, white.Darkened(0.2f), 0f, "Bell");
        Shapes.Cylinder(chedi, new Vector3(0.2f, 3.4f, 0f), 0.35f, 0.7f, 0.9f, white.Darkened(0.25f), 0f, "BrokenSpire").Rotation = new Vector3(0f, 0f, 0.35f);
        Shapes.Solid(chedi, new Vector3(0f, 1.5f, 0f), new Vector3(3.6f, 3f, 3.6f));
        Banyan(ruin, "SacredBanyan", new Vector3(9f, 0f, 5f), 1.2f, true);
        for (int i = 0; i < 5; i++)
            Shapes.Sphere(ruin, new Vector3(_rng.RandfRange(-2f, 2f), 0.36f, _rng.RandfRange(-3f, 3f)), 0.05f, new Color(1f, 0.7f, 0.3f), 4f, "GlowCandle");
        Shapes.Light(ruin, new Vector3(0f, 1.2f, 0f), new Color(1f, 0.65f, 0.35f), 1.4f, 10f, "CandleLight");

        // The north-eastern islet: a great ตะเคียน among dead trees, lit an eerie green.
        var lair = Shapes.Group(s, "TakianIslet", OnGround(90f, -120f));
        _landmarks.Add((new Vector2(90f, -120f), 14f));
        Takian(lair, "GreatTakian", Vector3.Zero, 2f);
        Shapes.Light(lair, new Vector3(0f, 3f, 0f), new Color(0.45f, 1f, 0.7f), 2f, 18f, "EerieLight");
        Occupy(new Vector3(90f, 0f, -120f), 6f);

        // Boats drifting in the deep channels.
        for (int i = 0; i < 12; i++)
        {
            var p = new Vector3(_rng.RandfRange(-160f, 160f), 0f, _rng.RandfRange(-160f, 150f));
            if (BuengHeight(p.X, p.Z) > -1f || DistToRoads(p) < 3f)
                continue;
            Boat(s, $"Boat{i}", p, _rng.Randf() * Mathf.Tau, i % 4 == 0);
        }
    }

    static void BuildBuengTrees(Node3D world)
    {
        var root = Shapes.Group(world, "MarshTrees");
        int k = 0;
        // Mangroves standing in the shallows near the island shores.
        for (int i = 0; i < 260 && k < 70; i++)
        {
            float x = _rng.RandfRange(-Half + 8f, Half - 8f), z = _rng.RandfRange(-Half + 8f, 150f);
            float h = BuengHeight(x, z);
            if (h > -0.1f || h < -1f || DistToRoads(new Vector3(x, 0f, z)) < 3f)
                continue;
            Mangrove(root, $"Mangrove{k++}", new Vector3(x, 0f, z), h);
        }
        // Dead trees, banyans and stumps on the islands, thickest on the north-western dead wood.
        k = 0;
        for (int i = 0; i < 900 && k < 150; i++)
        {
            float x = _rng.RandfRange(-Half + 6f, Half - 6f), z = _rng.RandfRange(-Half + 6f, Half - 6f);
            var p = new Vector3(x, 0f, z);
            float h = BuengHeight(x, z);
            if (h < 0.2f || DistToRoads(p) < 2.5f || Blocked(p, 2f) || NearLandmark(p))
                continue;
            var at = new Vector3(x, h, z);
            float roll = _rng.Randf();
            bool deadWood = new Vector2(x, z).DistanceTo(new Vector2(-110f, -110f)) < 40f;
            if (deadWood || roll < 0.45f)
                DeadTree(root, "DeadTree", at, _rng.RandfRange(1f, 1.6f));
            else if (roll < 0.7f)
                Tree(root, "Tree", at, _rng.RandfRange(0.9f, 1.3f));
            else if (roll < 0.82f)
                Banyan(root, "Banyan", at, _rng.RandfRange(0.7f, 1f), false);
            else
                Stump(root, "Stump", at);
            Occupy(p, 1.6f);
            k++;
        }
    }

    // Runtime: lily pads with pink and white flowers across the tagged fields, only where there is water.
    public static Node3D BuildLilies(IEnumerable<Node3D> fields)
    {
        var root = new Node3D { Name = "Lilies" };
        var zone = GameData.Zones.FirstOrDefault(z => z.Scene == "bueng_bua");
        if (zone == null)
            return root;
        var rng = new RandomNumberGenerator { Seed = 909 };
        var pads = new List<Transform3D>();
        var padColors = new List<Color>();
        var flowers = new List<Transform3D>();
        var flowerColors = new List<Color>();
        foreach (var f in fields)
        {
            var size = (Vector2)f.GetMeta("map_size", Vector2.One);
            var c = f.GlobalPosition;
            int count = (int)(size.X * size.Y * 0.05f);
            for (int i = 0; i < count; i++)
            {
                var p = c + new Vector3(rng.RandfRange(-0.5f, 0.5f) * size.X, 0f, rng.RandfRange(-0.5f, 0.5f) * size.Y);
                float h = BuengHeight(p.X - zone.CenterX, p.Z - zone.CenterZ);
                if (h > -0.18f)
                    continue;
                float s = rng.RandfRange(0.6f, 1.4f);
                pads.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(s, 1f, s)), p + new Vector3(0f, 0.025f, 0f)));
                padColors.Add(new Color(0.16f, 0.34f, 0.17f).Lightened(rng.RandfRange(0f, 0.12f)));
                if (rng.Randf() < 0.3f)
                {
                    var fp = p + new Vector3(rng.RandfRange(-0.2f, 0.2f), 0.1f, rng.RandfRange(-0.2f, 0.2f));
                    flowers.Add(new Transform3D(Basis.Identity.Scaled(Vector3.One * rng.RandfRange(0.8f, 1.2f)), fp));
                    flowerColors.Add(rng.Randf() < 0.6f ? new Color(1f, 0.5f, 0.72f) : new Color(0.96f, 0.95f, 0.9f));
                }
            }
        }
        var padMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.6f };
        var flowerMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        if (pads.Count > 0)
            root.AddChild(Scatter("LilyPads", new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.02f, RadialSegments = 10 }, padMat, pads, padColors, 90f));
        if (flowers.Count > 0)
            root.AddChild(Scatter("LilyFlowers", new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.05f, Height = 0.16f, RadialSegments = 8 }, flowerMat, flowers, flowerColors, 90f));
        return root;
    }
}
