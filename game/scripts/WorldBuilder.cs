using Godot;
using System.Collections.Generic;
using System.Linq;

// Builds each zone from code. SceneBaker saves the results to res://models/zone_*.tscn, which the game
// loads; edit those scenes in the Godot editor afterwards. Coordinates are local to the zone's centre.
// Rice, grass and the outer forest are generated at runtime (Build* methods at the end) because
// MultiMesh data does not survive baking; they follow the tagged nodes, so editor changes carry over.
//
// Tags read at runtime (survive baking):
//   map_* groups + "map_size" meta   → drawn on the map; also keep grass off roads, houses and plots
//   map_plot                         → paddies where scarecrows spawn and rice grows
//   shrine (+ shrine_id, shrine_name) → respawn shrines (E to bind)
//   bus_stop (+ stop_id, stop_name)  → fast travel stops for the last bus
//   alley_spawn                      → spots where town ghosts appear
//   water_area (+ map_size)          → wading area; its y is the water surface
//   water_surface                    → meshes using the water shader (rain ripples)
public static partial class WorldBuilder
{
    public const float Half = 186f;
    public const float ThungNaShoreZ = -110f;
    const float DeckTop = 0.5f;

    static readonly Color Wood = new(0.4f, 0.28f, 0.18f);
    static readonly Color DarkWood = new(0.24f, 0.17f, 0.12f);
    static readonly Color Thatch = new(0.36f, 0.28f, 0.17f);
    static readonly Color Tile = new(0.42f, 0.16f, 0.12f);
    static readonly Color Gold = new(0.85f, 0.65f, 0.2f);
    static readonly Color WindowGlow = new(1f, 0.68f, 0.32f);
    static readonly Color Brick = new(0.5f, 0.28f, 0.2f);
    static readonly Color Stone = new(0.42f, 0.4f, 0.37f);
    static readonly Color Dirt = new(0.44f, 0.36f, 0.26f);
    static readonly Color GroundGreen = new(0.24f, 0.29f, 0.18f);

    static RandomNumberGenerator _rng;

    public static Node3D BuildZone(string scene) => scene switch
    {
        "wiang_mok" => BuildWiangMok(),
        "bueng_bua" => BuildBuengBua(),
        _ => BuildThungNa(),
    };

    // ======================================================================
    //  เวียงหมอก (city hub)
    // ======================================================================

    // Street lines (both axes): the main street and avenue at 0, ring roads at ±62, outer roads at ±124.
    static readonly float[] StreetLines = { -124f, -62f, 0f, 62f, 124f };
    const float Wall = 183f;

    static float StreetWidth(float line) => line == 0f ? 8f : Mathf.Abs(line) < 100f ? 6f : 5f;

    public static Node3D BuildWiangMok()
    {
        _rng = new RandomNumberGenerator { Seed = 1100 };
        var city = new Node3D { Name = "Zone_WiangMok" };

        var ground = Shapes.Group(city, "Ground");
        Shapes.Add(ground, new PlaneMesh { Size = new Vector2(Half * 2f, Half * 2f) }, Vector3.Zero, GroundGreen, 0f, "Grass");
        Shapes.Solid(ground, new Vector3(0f, -0.5f, 0f), new Vector3(Half * 2f, 1f, Half * 2f), "GroundCollider");
        foreach (var line in StreetLines)
        {
            float w = StreetWidth(line);
            var color = line == 0f ? Stone : Stone.Lerp(Dirt, 0.5f);
            Shapes.MapTag(Shapes.Box(ground, new Vector3(line, 0.02f, 0f), new Vector3(w, 0.04f, Wall * 2f), color, 0f, $"StreetNS{line:0}"), "map_road", new Vector2(w, Wall * 2f));
            Shapes.MapTag(Shapes.Box(ground, new Vector3(0f, 0.025f, line), new Vector3(Wall * 2f, 0.04f, w), color, 0f, $"StreetEW{line:0}"), "map_road", new Vector2(Wall * 2f, w));
        }
        Shapes.MapTag(Shapes.Cylinder(ground, new Vector3(0f, 0.03f, 0f), 14f, 14f, 0.05f, Stone.Lightened(0.08f), 0f, "Plaza"), "map_road", new Vector2(28f, 28f));

        BuildCityWalls(city);
        BuildCityShrine(city);
        BuildStreetLife(city);
        BuildTemple(city, new Vector3(34f, 0f, -36f));
        BuildBus(city, new Vector3(-1.9f, 0f, 22f));
        BusStop(city, "BusStopCity", new Vector3(-7.4f, 0f, 30f), -Mathf.Pi * 0.5f, "wiang_mok", "ลานศาลหลักเมือง");
        YaiJanHouse(city, new Vector3(25f, 0f, 25f));
        CitySigns(city);
        Smithy(city, new Vector3(-20f, 0f, -12f));
        BuildBlocks(city);
        return city;
    }

    static void BuildCityWalls(Node3D city)
    {
        var walls = Shapes.Group(city, "CityWalls");
        const float h = 4.6f, t = 2f;
        void Segment(string name, Vector3 center, Vector3 size)
        {
            var w = Shapes.Group(walls, name, center);
            Shapes.MapTag(w, "map_wall", new Vector2(size.X, size.Z));
            Shapes.Box(w, new Vector3(0f, h * 0.5f, 0f), size + new Vector3(0f, h, 0f), Brick, 0f, "Bricks");
            Shapes.Box(w, new Vector3(0f, 0.3f, 0f), size + new Vector3(0.3f, 0.6f, 0.3f), Brick.Darkened(0.25f), 0f, "Plinth");
            Shapes.Solid(w, new Vector3(0f, h * 0.5f + 1f, 0f), size + new Vector3(0f, h + 2f, 0f));
            bool alongX = size.X > size.Z;
            float len = alongX ? size.X : size.Z;
            for (float s = -len * 0.5f + 0.6f; s < len * 0.5f - 0.3f; s += 2.2f)
                Shapes.Box(w, alongX ? new Vector3(s, h + 0.35f, 0f) : new Vector3(0f, h + 0.35f, s), new Vector3(1.1f, 0.7f, 1.1f), Brick.Lightened(0.05f), 0f, "Merlon");
        }
        float seg = Wall - 6f;
        foreach (var zSide in new[] { -1f, 1f })
        {
            Segment(zSide < 0f ? "WallNorthW" : "WallSouthW", new Vector3(-(6f + Wall) * 0.5f, 0f, zSide * Wall), new Vector3(seg, 0f, t));
            Segment(zSide < 0f ? "WallNorthE" : "WallSouthE", new Vector3((6f + Wall) * 0.5f, 0f, zSide * Wall), new Vector3(seg, 0f, t));
        }
        Segment("WallEast", new Vector3(Wall, 0f, 0f), new Vector3(t, 0f, Wall * 2f + t));
        Segment("WallWest", new Vector3(-Wall, 0f, 0f), new Vector3(t, 0f, Wall * 2f + t));
        // Corner bastions and towers along the walls.
        foreach (var x in new[] { -Wall, -Wall / 2f, Wall / 2f, Wall })
        foreach (var z in new[] { -Wall, Wall })
            Tower(walls, new Vector3(x, 0f, z));
        foreach (var z in new[] { -Wall / 2f, 0f, Wall / 2f })
        foreach (var x in new[] { -Wall, Wall })
            Tower(walls, new Vector3(x, 0f, z));
        GateHouse(walls, "GateNorth", new Vector3(0f, 0f, -Wall), false);
        GateHouse(walls, "GateSouth", new Vector3(0f, 0f, Wall), true);
    }

    static void Tower(Node3D parent, Vector3 pos)
    {
        var b = Shapes.Group(parent, "Tower", pos);
        Shapes.Box(b, new Vector3(0f, 3f, 0f), new Vector3(5f, 6f, 5f), Brick.Darkened(0.05f), 0f, "Tower");
        PyramidRoof(b, new Vector3(0f, 7f, 0f), 3.2f, 2f, Tile, "Roof");
        Shapes.Solid(b, new Vector3(0f, 3f, 0f), new Vector3(5f, 6f, 5f));
    }

    // Twin towers with a tiled Lanna roof over the opening; the south gate is shut for now.
    static void GateHouse(Node3D parent, string name, Vector3 pos, bool closed)
    {
        var g = Shapes.Group(parent, name, pos);
        foreach (var x in new[] { -7.5f, 7.5f })
        {
            Shapes.Box(g, new Vector3(x, 3.2f, 0f), new Vector3(3f, 6.4f, 3.2f), Brick.Lightened(0.04f), 0f, "Tower");
            Shapes.Solid(g, new Vector3(x, 3.2f, 0f), new Vector3(3f, 6.4f, 3.2f));
        }
        Shapes.Box(g, new Vector3(0f, 6.6f, 0f), new Vector3(18f, 0.6f, 3.6f), Brick.Darkened(0.1f), 0f, "Lintel");
        // Ridge runs along the wall (X): the prism's triangle spans Z after turning it 90°.
        Shapes.Prism(g, new Vector3(0f, 8.1f, 0f), new Vector3(4.6f, 2.4f, 19f), Tile, "Roof").Rotation = new Vector3(0f, Mathf.Pi * 0.5f, 0f);
        Shapes.Box(g, new Vector3(0f, 9.35f, 0f), new Vector3(18.5f, 0.15f, 0.2f), Gold, 0f, "Ridge");
        foreach (var x in new[] { -4f, 4f })
        {
            Shapes.Sphere(g, new Vector3(x, 5.6f, -1.9f), 0.25f, new Color(1f, 0.45f, 0.25f), 3f, "GlowLantern");
            Shapes.Light(g, new Vector3(x, 5.4f, -2.4f), new Color(1f, 0.55f, 0.3f), 1.2f, 9f, "GateLight");
        }
        if (closed)
        {
            Shapes.Box(g, new Vector3(0f, 2.9f, 0f), new Vector3(12f, 5.8f, 0.4f), DarkWood, 0f, "Doors");
            Shapes.Box(g, new Vector3(0f, 2.9f, -0.22f), new Vector3(0.15f, 5.8f, 0.05f), Gold.Darkened(0.4f), 0f, "DoorSeam");
            Shapes.Box(g, new Vector3(0f, 2.6f, -0.3f), new Vector3(7f, 0.3f, 0.2f), Gold.Darkened(0.3f), 0f, "Bar");
            Shapes.Solid(g, new Vector3(0f, 2.9f, 0f), new Vector3(12f, 5.8f, 0.6f));
        }
    }

    // ศาลหลักเมือง: the city pillar shrine in the plaza and the default respawn point.
    // Low steps on every side so it can be walked onto.
    static void BuildCityShrine(Node3D city)
    {
        var s = Shapes.Group(city, "ShrineLakMueang", Vector3.Zero);
        Shrine(s, "lak_mueang", "ศาลหลักเมือง");
        var white = new Color(0.82f, 0.8f, 0.75f);
        Shapes.Box(s, new Vector3(0f, 0.15f, 0f), new Vector3(7.6f, 0.3f, 7.6f), white.Darkened(0.05f), 0f, "Step1");
        Shapes.Box(s, new Vector3(0f, 0.4f, 0f), new Vector3(6.6f, 0.2f, 6.6f), white, 0f, "Step2");
        Shapes.Box(s, new Vector3(0f, 0.6f, 0f), new Vector3(5.4f, 0.2f, 5.4f), new Color(0.7f, 0.68f, 0.62f), 0f, "Platform");
        Shapes.Solid(s, new Vector3(0f, 0.15f, 0f), new Vector3(7.6f, 0.3f, 7.6f), "Step1Collider");
        Shapes.Solid(s, new Vector3(0f, 0.4f, 0f), new Vector3(6.6f, 0.2f, 6.6f), "Step2Collider");
        Shapes.Solid(s, new Vector3(0f, 0.6f, 0f), new Vector3(5.4f, 0.2f, 5.4f), "PlatformCollider");
        foreach (var x in new[] { -2.1f, 2.1f })
        foreach (var z in new[] { -2.1f, 2.1f })
        {
            Shapes.Cylinder(s, new Vector3(x, 2.3f, z), 0.16f, 0.18f, 3.2f, new Color(0.62f, 0.16f, 0.12f), 0f, "Pillar");
            Shapes.Solid(s, new Vector3(x, 2.3f, z), new Vector3(0.36f, 3.2f, 0.36f), "PillarCollider");
        }
        Shapes.Cylinder(s, new Vector3(0f, 2f, 0f), 0.3f, 0.36f, 2.6f, Gold, 0.4f, "CityPillar");
        Shapes.Sphere(s, new Vector3(0f, 3.4f, 0f), 0.34f, Gold, 0.5f, "PillarTop");
        Shapes.Box(s, new Vector3(0f, 3.95f, 0f), new Vector3(5.2f, 0.2f, 5.2f), new Color(0.5f, 0.14f, 0.1f), 0f, "Beam");
        PyramidRoof(s, new Vector3(0f, 5f, 0f), 3.8f, 2f, Tile, "Roof1");
        PyramidRoof(s, new Vector3(0f, 6.3f, 0f), 2.2f, 1.6f, Tile.Lightened(0.05f), "Roof2");
        Shapes.Cylinder(s, new Vector3(0f, 7.7f, 0f), 0.02f, 0.18f, 1.4f, Gold, 0.5f, "Spire");
        Shapes.Cylinder(s, new Vector3(0f, 1.4f, 0f), 0.5f, 0.5f, 0.08f, new Color(0.95f, 0.95f, 0.85f), 0.4f, "Garland");
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.Tau;
            Shapes.Cylinder(s, new Vector3(Mathf.Cos(a) * 1.1f, 0.85f, Mathf.Sin(a) * 1.1f), 0.03f, 0.03f, 0.3f, new Color(0.95f, 0.92f, 0.8f), 0f, "Candle");
            Shapes.Sphere(s, new Vector3(Mathf.Cos(a) * 1.1f, 1.04f, Mathf.Sin(a) * 1.1f), 0.04f, new Color(1f, 0.75f, 0.35f), 4f, "GlowFlame");
        }
        Shapes.Light(s, new Vector3(0f, 2.2f, 0f), new Color(1f, 0.7f, 0.4f), 2.4f, 12f, "ShrineLight");
        Shapes.Solid(s, new Vector3(0f, 2f, 0f), new Vector3(0.8f, 4f, 0.8f), "CityPillarCollider");
    }

    static void Shrine(Node3D node, string id, string name)
    {
        node.AddToGroup("shrine", true);
        node.SetMeta("shrine_id", id);
        node.SetMeta("shrine_name", name);
        Shapes.MapTag(node, "map_shrine", new Vector2(2f, 2f));
    }

    // Four-sided pyramid roof (a 4-segment cone turned 45°).
    static MeshInstance3D PyramidRoof(Node3D parent, Vector3 pos, float halfWidth, float height, Color color, string name)
    {
        var mi = Shapes.Add(parent, new CylinderMesh { TopRadius = 0.02f, BottomRadius = halfWidth * 1.414f, Height = height, RadialSegments = 4, Rings = 1 },
            pos, color, 0f, name);
        mi.Rotation = new Vector3(0f, Mathf.Pi * 0.25f, 0f);
        return mi;
    }

    // Lantern strings across the main street and avenue, shophouses along both.
    static void BuildStreetLife(Node3D city)
    {
        var st = Shapes.Group(city, "Streets");
        int n = 0;
        for (float d = 18f; d <= 172f; d += 12f)
            foreach (var side in new[] { -1f, 1f })
            {
                LanternString(st, $"StringMain{n}", new Vector3(0f, 0f, side * d), 4.7f, n % 3 == 0);
                LanternString(st, $"StringAve{n}", new Vector3(side * d, 0f, 0f), 4.7f, n % 3 == 1, true);
                n++;
            }

        int k = 0;
        // Main street, both sides, out to the outer roads (gaps at the ring roads).
        foreach (var zs in new[] { -1f, 1f })
            for (float z = 18f; z <= 118f; z += 6f)
            {
                if (Mathf.Abs(z - 62f) < 7f)
                    continue;

                    Shophouse(st, $"ShopE{k}", new Vector3(9f, 0f, zs * z), Mathf.Pi * 0.5f, k);
                if (!(zs > 0f && z < 36f)) // bus stop and the bus
                    Shophouse(st, $"ShopW{k}", new Vector3(-9f, 0f, zs * z), -Mathf.Pi * 0.5f, k + 3);
                k++;
            }
        // West avenue both sides (the smithy takes one lot), east avenue south side only
        // (the temple court is to the north; Yai Jan's house sits at the plaza corner).
        for (float x = 18f; x <= 56f; x += 6f)
        {
            if (x > 26f)
                Shophouse(st, $"ShopAvN{k}", new Vector3(-x, 0f, -9f), Mathf.Pi, k);
            Shophouse(st, $"ShopAvS{k}", new Vector3(-x, 0f, 9f), 0f, k + 1);
            if (x > 30f)
                Shophouse(st, $"ShopAvE{k}", new Vector3(x, 0f, 9f), 0f, k + 2);
            k++;
        }
    }

    static void LanternString(Node3D parent, string name, Vector3 center, float halfSpan, bool light, bool acrossZ = false)
    {
        var g = Shapes.Group(parent, name, center, acrossZ ? Mathf.Pi * 0.5f : 0f);
        foreach (var x in new[] { -halfSpan, halfSpan })
            Shapes.Cylinder(g, new Vector3(x, 2.4f, 0f), 0.08f, 0.1f, 4.8f, DarkWood, 0f, "Pole");
        for (int i = 0; i <= 10; i++)
        {
            float t = i / 10f;
            float x = Mathf.Lerp(-halfSpan, halfSpan, t);
            float y = 4.6f - Mathf.Sin(t * Mathf.Pi) * 0.9f;
            if (i > 0 && i < 10)
            {
                var c = i % 3 == 0 ? new Color(1f, 0.82f, 0.35f) : new Color(1f, 0.32f, 0.2f);
                Shapes.Sphere(g, new Vector3(x, y - 0.22f, 0f), 0.16f, c, 2.5f, "GlowLantern");
            }
            if (i < 10)
            {
                float x2 = Mathf.Lerp(-halfSpan, halfSpan, (i + 1) / 10f);
                float y2 = 4.6f - Mathf.Sin((i + 1) / 10f * Mathf.Pi) * 0.9f;
                var seg = Shapes.Box(g, new Vector3((x + x2) * 0.5f, (y + y2) * 0.5f, 0f), new Vector3(Mathf.Sqrt((x2 - x) * (x2 - x) + (y2 - y) * (y2 - y)), 0.025f, 0.025f), DarkWood, 0f, "Rope");
                seg.Rotation = new Vector3(0f, 0f, Mathf.Atan2(y2 - y, x2 - x));
            }
        }
        if (light)
            Shapes.Light(g, new Vector3(0f, 3.4f, 0f), new Color(1f, 0.55f, 0.3f), 1.5f, 11f, "StringLight");
    }

    // Two-storey wooden shophouse (ห้องแถวไม้): folding plank doors, balcony, gable facing the street.
    static void Shophouse(Node3D parent, string name, Vector3 pos, float yaw, int variant)
    {
        var h = Shapes.Group(parent, name, pos, yaw);
        Shapes.MapTag(h, "map_house", new Vector2(5.8f, 7.6f));
        var wood = Wood.Lightened((variant % 4) * 0.04f - 0.04f);
        const float w = 5.8f, d = 7.6f, storey = 3f;
        Shapes.Box(h, new Vector3(0f, storey, 0.2f), new Vector3(w, storey * 2f, d - 0.4f), wood, 0f, "Body");
        Shapes.Box(h, new Vector3(0f, storey, -d * 0.5f + 0.15f), new Vector3(w + 0.1f, 0.2f, 0.5f), DarkWood, 0f, "FloorBeam");
        for (int i = 0; i < 6; i++)
        {
            float x = -w * 0.5f + 0.5f + i * 0.96f;
            bool open = (i == 2 || i == 3) && variant % 3 != 1;
            Shapes.Box(h, new Vector3(x, 1.35f, -d * 0.5f - 0.01f), new Vector3(0.9f, 2.6f, 0.05f), open ? WindowGlow : DarkWood.Lightened(0.08f), open ? 1.4f : 0f, open ? "GlowDoor" : "DoorPanel");
        }
        Shapes.Box(h, new Vector3(0f, storey + 0.1f, -d * 0.5f - 0.5f), new Vector3(w, 0.12f, 1f), wood.Darkened(0.1f), 0f, "Balcony");
        Shapes.Box(h, new Vector3(0f, storey + 1.05f, -d * 0.5f - 0.98f), new Vector3(w, 0.08f, 0.08f), DarkWood, 0f, "BalconyRail");
        for (int i = 0; i <= 8; i++)
            Shapes.Box(h, new Vector3(-w * 0.5f + 0.1f + i * (w - 0.2f) / 8f, storey + 0.6f, -d * 0.5f - 0.98f), new Vector3(0.06f, 0.9f, 0.06f), DarkWood, 0f, "Baluster");
        bool lit = (variant * 7) % 3 != 0;
        foreach (var x in new[] { -1.4f, 1.4f })
            Shapes.Box(h, new Vector3(x, storey + 1.5f, -d * 0.5f + 0.39f), new Vector3(1f, 1.2f, 0.05f), lit ? WindowGlow : new Color(0.1f, 0.08f, 0.07f), lit ? 1.6f : 0f, lit ? "GlowWindow" : "Window");
        var awning = Shapes.Box(h, new Vector3(0f, 2.85f, -d * 0.5f - 0.6f), new Vector3(w + 0.2f, 0.08f, 1.3f), Tile.Darkened(0.1f), 0f, "Awning");
        awning.Rotation = new Vector3(0.25f, 0f, 0f);
        const float slope = 0.62f;
        float rise = w * 0.5f * Mathf.Tan(slope);
        float top = storey * 2f;
        foreach (var side in new[] { -1f, 1f })
        {
            var slab = Shapes.Box(h, new Vector3(side * w * 0.25f, top + rise * 0.5f, 0.2f), new Vector3(w * 0.5f / Mathf.Cos(slope) + 0.3f, 0.14f, d + 0.3f), Tile, 0f, "Roof");
            slab.Rotation = new Vector3(0f, 0f, -side * slope);
        }
        Shapes.Prism(h, new Vector3(0f, top + rise * 0.5f, -d * 0.5f + 0.25f), new Vector3(w, rise, 0.1f), wood, "Gable");
        Shapes.Box(h, new Vector3(0f, top + rise + 0.05f, 0.2f), new Vector3(0.18f, 0.14f, d + 0.4f), DarkWood, 0f, "Ridge");
        Shapes.Sphere(h, new Vector3(-w * 0.5f + 0.5f, 2.4f, -d * 0.5f - 1f), 0.17f, new Color(1f, 0.4f, 0.22f), 2.5f, "GlowLantern");
        Shapes.Solid(h, new Vector3(0f, storey, 0.2f), new Vector3(w, storey * 2f, d - 0.4f));
    }

    // โรงตีเหล็กลุงมั่น: open forge shed with a chimney and smoke, anvil, weapon rack and quench trough.
    static void Smithy(Node3D parent, Vector3 pos)
    {
        var s = Shapes.Group(parent, "SmithyLungMan", pos, Mathf.Pi);
        Shapes.MapTag(s, "map_house", new Vector2(9f, 9f));
        Shapes.Box(s, new Vector3(0f, 0.04f, 0f), new Vector3(9f, 0.08f, 9f), Stone.Darkened(0.2f), 0f, "Floor");
        Shapes.Box(s, new Vector3(0f, 1.8f, 3.6f), new Vector3(8.6f, 3.6f, 1.2f), DarkWood, 0f, "BackWall");
        Shapes.Solid(s, new Vector3(0f, 1.8f, 3.6f), new Vector3(8.6f, 3.6f, 1.2f));
        foreach (var x in new[] { -4.2f, 0f, 4.2f })
            Shapes.Box(s, new Vector3(x, 1.8f, -3.8f), new Vector3(0.25f, 3.6f, 0.25f), DarkWood, 0f, "Post");
        var roof = Shapes.Box(s, new Vector3(0f, 3.9f, 0f), new Vector3(9.4f, 0.14f, 9.4f), Thatch, 0f, "Roof");
        roof.Rotation = new Vector3(-0.12f, 0f, 0f);
        // Brick forge with chimney, glowing coals and rising smoke.
        Shapes.Box(s, new Vector3(2.6f, 0.75f, 2f), new Vector3(2f, 1.5f, 1.6f), Brick.Darkened(0.2f), 0f, "Forge");
        Shapes.Box(s, new Vector3(2.6f, 1.52f, 1.9f), new Vector3(1.3f, 0.06f, 0.9f), new Color(1f, 0.4f, 0.1f), 4f, "GlowCoals");
        Shapes.Box(s, new Vector3(2.6f, 3.6f, 2.4f), new Vector3(0.9f, 4.2f, 0.9f), Brick.Darkened(0.3f), 0f, "Chimney");
        Shapes.Light(s, new Vector3(2.6f, 2f, 1.4f), new Color(1f, 0.45f, 0.15f), 2.4f, 9f, "ForgeLight");
        Shapes.Solid(s, new Vector3(2.6f, 0.75f, 2f), new Vector3(2f, 1.5f, 1.6f));
        Shapes.Child(s, new GpuParticles3D
        {
            Amount = 24,
            Lifetime = 4f,
            Position = new Vector3(2.6f, 5.8f, 2.4f),
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 15f,
                InitialVelocityMin = 0.6f,
                InitialVelocityMax = 1.1f,
                Gravity = new Vector3(0.15f, 0.1f, 0f),
                ScaleMin = 0.6f,
                ScaleMax = 1.8f,
                Color = new Color(0.35f, 0.35f, 0.38f, 0.35f),
            },
            DrawPass1 = new SphereMesh
            {
                Radius = 0.35f, Height = 0.7f, RadialSegments = 6, Rings = 3,
                Material = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            },
        }, "Smoke");
        Shapes.Box(s, new Vector3(-0.5f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.5f), DarkWood, 0f, "AnvilStand");
        Shapes.Box(s, new Vector3(-0.5f, 1f, 0f), new Vector3(1f, 0.28f, 0.42f), new Color(0.22f, 0.22f, 0.24f), 0f, "Anvil");
        Shapes.Solid(s, new Vector3(-0.5f, 0.6f, 0f), new Vector3(1f, 1.2f, 0.6f));
        // Weapon rack
        Shapes.Box(s, new Vector3(-3.4f, 1.2f, 2.9f), new Vector3(1.8f, 0.1f, 0.3f), DarkWood, 0f, "RackBar");
        for (int i = 0; i < 4; i++)
            Shapes.Box(s, new Vector3(-4.1f + i * 0.45f, 1.1f, 2.85f), new Vector3(0.05f, 1.6f, 0.05f), new Color(0.8f, 0.82f, 0.88f), 0.2f, "RackBlade");
        Shapes.Box(s, new Vector3(-2f, 0.3f, -2.2f), new Vector3(1.6f, 0.6f, 0.7f), DarkWood, 0f, "Trough");
        Shapes.Box(s, new Vector3(-2f, 0.56f, -2.2f), new Vector3(1.4f, 0.04f, 0.5f), new Color(0.1f, 0.18f, 0.22f), 0f, "TroughWater");
    }

    // บ้านยายจันทร์: a medium's stilt house hung with talismans, with herb pots and a drying rack.
    static void YaiJanHouse(Node3D parent, Vector3 pos)
    {
        var g = Shapes.Group(parent, "HouseYaiJan", pos, Mathf.Pi * 0.25f);
        StiltHouse(g, "House", Vector3.Zero, 0f, 2f, true);
        var yellow = new Color(0.95f, 0.82f, 0.4f);
        for (int i = 0; i < 7; i++)
        {
            float x = -2.6f + i * 0.85f;
            Shapes.Box(g, new Vector3(x, 4.6f, -2.35f), new Vector3(0.01f, 0.5f, 0.01f), new Color(0.8f, 0.8f, 0.8f), 0f, "String");
            Shapes.Box(g, new Vector3(x, 4.2f, -2.35f), new Vector3(0.18f, 0.3f, 0.01f), yellow, 0.3f, "Talisman");
            Shapes.Box(g, new Vector3(x, 4.2f, -2.36f), new Vector3(0.1f, 0.18f, 0.005f), new Color(0.7f, 0.15f, 0.1f), 0f, "Ink");
        }
        foreach (var (x, r) in new[] { (-2.4f, 0.35f), (-1.6f, 0.28f), (2.2f, 0.4f) })
        {
            Shapes.Cylinder(g, new Vector3(x, r * 0.8f, -5.4f), r * 0.8f, r, r * 1.6f, new Color(0.45f, 0.28f, 0.18f), 0f, "Pot");
            Shapes.Sphere(g, new Vector3(x, r * 1.7f, -5.4f), r * 0.7f, new Color(0.2f, 0.4f, 0.18f), 0f, "Herbs");
        }
        Shapes.Box(g, new Vector3(3.6f, 1f, -4.6f), new Vector3(0.08f, 2f, 0.08f), DarkWood, 0f, "RackPost");
        Shapes.Box(g, new Vector3(5f, 1f, -4.6f), new Vector3(0.08f, 2f, 0.08f), DarkWood, 0f, "RackPost");
        Shapes.Box(g, new Vector3(4.3f, 1.9f, -4.6f), new Vector3(1.6f, 0.05f, 0.05f), DarkWood, 0f, "RackBar");
        for (int i = 0; i < 4; i++)
            Shapes.Box(g, new Vector3(3.75f + i * 0.35f, 1.6f, -4.6f), new Vector3(0.12f, 0.5f, 0.06f), new Color(0.35f, 0.42f, 0.2f), 0f, "DriedHerb");
        Shapes.Light(g, new Vector3(0f, 1.6f, -5f), new Color(1f, 0.62f, 0.3f), 1f, 6f, "YardLight");
    }

    // ป้ายรถเมล์: a little roofed bench with a sign; the last bus stops here for fast travel.
    static void BusStop(Node3D parent, string name, Vector3 pos, float yaw, string id, string label)
    {
        var b = Shapes.Group(parent, name, pos, yaw);
        b.AddToGroup("bus_stop", true);
        b.SetMeta("stop_id", id);
        b.SetMeta("stop_name", label);
        Shapes.MapTag(b, "map_bus_stop", new Vector2(3f, 1.4f));
        foreach (var x in new[] { -1.4f, 1.4f })
            Shapes.Box(b, new Vector3(x, 1.25f, 0.5f), new Vector3(0.1f, 2.5f, 0.1f), new Color(0.55f, 0.12f, 0.1f), 0f, "Post");
        Shapes.Box(b, new Vector3(0f, 2.55f, 0.2f), new Vector3(3.4f, 0.1f, 1.6f), new Color(0.6f, 0.14f, 0.1f), 0f, "Roof");
        Shapes.Box(b, new Vector3(0f, 0.5f, 0.6f), new Vector3(2.6f, 0.08f, 0.5f), DarkWood, 0f, "Bench");
        Shapes.Box(b, new Vector3(-1.9f, 1.6f, -0.2f), new Vector3(0.06f, 3.2f, 0.06f), new Color(0.3f, 0.3f, 0.32f), 0f, "SignPole");
        Shapes.Box(b, new Vector3(-1.9f, 3f, -0.2f), new Vector3(0.7f, 0.5f, 0.04f), new Color(0.8f, 0.15f, 0.12f), 0.6f, "GlowSign");
        Shapes.Child(b, new Label3D
        {
            Text = "รถเมล์สายสุดท้าย",
            Font = Game.ThaiFont,
            FontSize = 40,
            Modulate = new Color(1f, 0.95f, 0.8f),
            OutlineSize = 0,
            Position = new Vector3(-1.9f, 3f, -0.23f),
            Rotation = new Vector3(0f, Mathf.Pi, 0f),
            PixelSize = 0.0035f,
        }, "SignText");
        Shapes.Solid(b, new Vector3(0f, 0.5f, 0.6f), new Vector3(2.6f, 0.4f, 0.5f));
    }

    // วัดกลางเวียง: walled temple court with a tiered-roof hall, a chedi and a bodhi tree.
    static void BuildTemple(Node3D city, Vector3 pos)
    {
        var t = Shapes.Group(city, "Temple", pos);
        var white = new Color(0.9f, 0.88f, 0.84f);
        void CourtWall(string n, Vector3 c, Vector3 s)
        {
            Shapes.Box(t, c + new Vector3(0f, 0.75f, 0f), s + new Vector3(0f, 1.5f, 0f), white, 0f, n);
            Shapes.Solid(t, c + new Vector3(0f, 0.75f, 0f), s + new Vector3(0f, 1.5f, 0f));
        }
        CourtWall("CourtN", new Vector3(0f, 0f, -19f), new Vector3(38f, 0f, 0.5f));
        CourtWall("CourtE", new Vector3(19f, 0f, 0f), new Vector3(0.5f, 0f, 38f));
        CourtWall("CourtW", new Vector3(-19f, 0f, 0f), new Vector3(0.5f, 0f, 38f));
        CourtWall("CourtSW", new Vector3(-11f, 0f, 19f), new Vector3(16f, 0f, 0.5f));
        CourtWall("CourtSE", new Vector3(11f, 0f, 19f), new Vector3(16f, 0f, 0.5f));
        Shapes.MapTag(Shapes.Box(t, new Vector3(0f, 0.02f, 0f), new Vector3(37f, 0.04f, 37f), new Color(0.6f, 0.56f, 0.48f), 0f, "CourtFloor"), "map_temple", new Vector2(38f, 38f));

        var hall = Shapes.Group(t, "Hall", new Vector3(-4f, 0f, 0f));
        Shapes.Box(hall, new Vector3(0f, 0.5f, 0f), new Vector3(11f, 1f, 18f), white, 0f, "Base");
        Shapes.Box(hall, new Vector3(0f, 3f, 0.5f), new Vector3(8.5f, 4f, 15f), new Color(0.62f, 0.2f, 0.14f), 0f, "Walls");
        Shapes.Box(hall, new Vector3(0f, 2.4f, -7.05f), new Vector3(2.2f, 2.8f, 0.1f), Gold.Darkened(0.2f), 0.3f, "Door");
        foreach (var x in new[] { -4.6f, 4.6f })
            for (float z = -7f; z <= 7.5f; z += 2.9f)
                Shapes.Cylinder(hall, new Vector3(x, 3f, z), 0.22f, 0.25f, 4f, white, 0f, "Column");
        float[] widths = { 11f, 8.6f, 6.2f };
        float[] lengths = { 18.5f, 15f, 10f };
        for (int i = 0; i < 3; i++)
            Shapes.Prism(hall, new Vector3(0f, 6.2f + i * 1.25f, 0.3f - i * 0.8f), new Vector3(widths[i], 2.6f, lengths[i]), i == 1 ? Tile.Lightened(0.06f) : Tile, $"RoofTier{i}");
        Shapes.Box(hall, new Vector3(0f, 9.3f, -1.3f), new Vector3(0.16f, 0.16f, 10.2f), Gold, 0.4f, "Ridge");
        foreach (var z in new[] { -6.3f, 3.7f })
            Shapes.Cylinder(hall, new Vector3(0f, 9.8f, z), 0.02f, 0.12f, 1.2f, Gold, 0.5f, "Chofa").Rotation = new Vector3(z < 0f ? 0.5f : -0.5f, 0f, 0f);
        Shapes.Light(hall, new Vector3(0f, 3f, -8.5f), new Color(1f, 0.7f, 0.4f), 1.8f, 10f, "HallLight");
        Shapes.Solid(hall, new Vector3(0f, 2.6f, 0.3f), new Vector3(11f, 5.2f, 18f));

        var chedi = Shapes.Group(t, "Chedi", new Vector3(11f, 0f, -11f));
        Shapes.Box(chedi, new Vector3(0f, 0.6f, 0f), new Vector3(6f, 1.2f, 6f), white, 0f, "Base1");
        Shapes.Box(chedi, new Vector3(0f, 1.6f, 0f), new Vector3(4.6f, 0.8f, 4.6f), white, 0f, "Base2");
        var bell = Shapes.Sphere(chedi, new Vector3(0f, 3.4f, 0f), 2f, white.Darkened(0.05f), 0f, "Bell");
        bell.Scale = new Vector3(1f, 1.1f, 1f);
        for (int i = 0; i < 4; i++)
            Shapes.Cylinder(chedi, new Vector3(0f, 5.5f + i * 0.35f, 0f), 0.65f - i * 0.12f, 0.75f - i * 0.12f, 0.3f, Gold, 0.2f, "Ring");
        Shapes.Cylinder(chedi, new Vector3(0f, 8.6f, 0f), 0.02f, 0.3f, 4.2f, Gold, 0.4f, "Spire");
        Shapes.Solid(chedi, new Vector3(0f, 2.5f, 0f), new Vector3(6f, 5f, 6f));

        Banyan(t, "Bodhi", new Vector3(10f, 0f, 9f), 1.1f, true);
        LanternPost(t, "TempleLantern1", new Vector3(-4f, 0f, 14f), 2.6f, true);
        LanternPost(t, "TempleLantern2", new Vector3(4f, 0f, 14f), 2.6f, false);
    }

    // รถเมล์แดง parked by the plaza.
    static void BuildBus(Node3D city, Vector3 pos)
    {
        var b = Shapes.Group(city, "RedBus", pos);
        Shapes.MapTag(b, "map_bus", new Vector2(2.5f, 9f));
        var red = new Color(0.72f, 0.12f, 0.1f);
        Shapes.Box(b, new Vector3(0f, 1.75f, 0f), new Vector3(2.5f, 2.3f, 9f), red, 0f, "Body");
        Shapes.Box(b, new Vector3(0f, 3.0f, 0f), new Vector3(2.4f, 0.2f, 8.8f), new Color(0.9f, 0.88f, 0.82f), 0f, "Roof");
        Shapes.Box(b, new Vector3(0f, 2.15f, 0.2f), new Vector3(2.52f, 0.8f, 7.6f), new Color(0.95f, 0.75f, 0.4f), 0.7f, "GlowWindows");
        Shapes.Box(b, new Vector3(0f, 2.2f, -4.51f), new Vector3(2.1f, 0.9f, 0.02f), new Color(0.15f, 0.2f, 0.25f), 0f, "Windshield");
        Shapes.Box(b, new Vector3(0f, 1.0f, 0f), new Vector3(2.54f, 0.15f, 9.02f), new Color(0.95f, 0.92f, 0.8f), 0f, "Stripe");
        foreach (var x in new[] { -1.15f, 1.15f })
        foreach (var z in new[] { -3f, 3f })
            Shapes.Cylinder(b, new Vector3(x, 0.5f, z), 0.5f, 0.5f, 0.35f, new Color(0.08f, 0.08f, 0.08f), 0f, "Wheel").Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
        foreach (var x in new[] { -0.8f, 0.8f })
            Shapes.Sphere(b, new Vector3(x, 1.1f, -4.52f), 0.14f, new Color(1f, 0.95f, 0.75f), 3f, "GlowHeadlight");
        Shapes.Child(b, new Label3D
        {
            Text = "สายสุดท้าย",
            Font = Game.ThaiFont,
            FontSize = 48,
            Modulate = new Color(1f, 0.85f, 0.3f),
            OutlineSize = 0,
            Position = new Vector3(0f, 2.8f, -4.53f),
            Rotation = new Vector3(0f, Mathf.Pi, 0f),
            PixelSize = 0.006f,
        }, "Sign");
        Shapes.Solid(b, new Vector3(0f, 1.6f, 0f), new Vector3(2.5f, 3.2f, 9f));
    }

    // Fills every city block between the streets. Special blocks: the temple court, a park with a pond,
    // a graveyard and the dark quarters (fog, no lanterns, ghosts in the alleys).
    static void BuildBlocks(Node3D city)
    {
        var root = Shapes.Group(city, "Blocks");
        var edges = new List<float> { -Wall + 2f };
        edges.AddRange(StreetLines);
        edges.Add(Wall - 2f);
        for (int ix = 0; ix < edges.Count - 1; ix++)
        for (int iz = 0; iz < edges.Count - 1; iz++)
        {
            float x0 = edges[ix] + (ix > 0 ? StreetWidth(edges[ix]) * 0.5f : 0f) + 1.5f;
            float x1 = edges[ix + 1] - (ix + 1 < edges.Count - 1 ? StreetWidth(edges[ix + 1]) * 0.5f : 0f) - 1.5f;
            float z0 = edges[iz] + (iz > 0 ? StreetWidth(edges[iz]) * 0.5f : 0f) + 1.5f;
            float z1 = edges[iz + 1] - (iz + 1 < edges.Count - 1 ? StreetWidth(edges[iz + 1]) * 0.5f : 0f) - 1.5f;
            var c = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
            var g = Shapes.Group(root, $"Block_{ix}_{iz}", c);
            float w = x1 - x0, d = z1 - z0;
            bool core = Mathf.Abs(c.X) < 62f && Mathf.Abs(c.Z) < 62f;
            if (core && c.X > 0f && c.Z < 0f)
                continue; // temple
            if (ix == 1 && iz == 4)
            {
                Park(g, w, d);
                continue;
            }
            if (ix == 4 && iz == 1)
            {
                Graveyard(g, w, d);
                continue;
            }
            bool dark = (c.X < 0f && c.Z > 0f && c.Z < 124f && c.X > -124f);
            // Keep clear of the shophouses that front the main street and avenue.
            float clearX = Mathf.Abs(c.X) < 62f ? 21f : 0f;
            float clearZ = Mathf.Abs(c.Z) < 62f ? 21f : 0f;
            HouseLots(g, c, w, d, clearX, clearZ, dark, core && c.X > 0f && c.Z > 0f);
        }
    }

    // Houses on a grid of lots with dirt alleys between them.
    static void HouseLots(Node3D g, Vector3 center, float w, float d, float clearX, float clearZ, bool dark, bool skipCorner)
    {
        const float pitch = 16f;
        int nx = Mathf.Max(1, (int)(w / pitch)), nz = Mathf.Max(1, (int)(d / pitch));
        float sx = w / nx, sz = d / nz;
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < nz; j++)
        {
            var local = new Vector3(-w * 0.5f + sx * (i + 0.5f), 0f, -d * 0.5f + sz * (j + 0.5f));
            var world = center + local;
            // Leave room for the street-front shophouses, the plaza corner and the smithy.
            if (clearX > 0f && Mathf.Abs(world.X) < clearX)
                continue;
            if (clearZ > 0f && Mathf.Abs(world.Z) < clearZ)
                continue;
            if (skipCorner && world.X < 32f && world.Z < 32f)
                continue;
            if (world.DistanceTo(new Vector3(-20f, 0f, -12f)) < 13f)
                continue;
            float yaw = _rng.RandiRange(0, 3) * Mathf.Pi * 0.5f;
            string n = $"House_{i}_{j}";
            switch (_rng.RandiRange(0, 5))
            {
                case 0: StiltHouse(g, n, local, yaw, 2.3f, true); break;
                case 1: Hut(g, n, local, yaw); break;
                case 2: TwinHouse(g, n, local, yaw); break;
                case 3: Granary(g, n + "_Granary", local + new Vector3(3f, 0f, 2.5f), yaw); Hut(g, n, local - new Vector3(2.5f, 0f, 2.5f), yaw); break;
                case 4: StiltHouse(g, n, local, yaw, 2.0f, false); break;
                default:
                    if (dark)
                        DeadTree(g, "DeadTree", local, _rng.RandfRange(1f, 1.4f));
                    else
                        Tree(g, "YardTree", local, _rng.RandfRange(1.1f, 1.5f));
                    break;
            }
        }
        // Alleys between lot rows; lanterns where they cross unless the quarter is dark.
        for (int i = 1; i < nx; i++)
            Shapes.Box(g, new Vector3(-w * 0.5f + sx * i, 0.015f, 0f), new Vector3(3f, 0.03f, d), Dirt, 0f, "AlleyNS");
        for (int j = 1; j < nz; j++)
            Shapes.Box(g, new Vector3(0f, 0.016f, -d * 0.5f + sz * j), new Vector3(w, 0.03f, 3f), Dirt, 0f, "AlleyEW");
        for (int i = 1; i < nx; i++)
        for (int j = 1; j < nz; j++)
        {
            var p = new Vector3(-w * 0.5f + sx * i, 0f, -d * 0.5f + sz * j);
            if (dark)
            {
                var spawn = Shapes.Group(g, $"AlleySpawn_{i}_{j}", p);
                spawn.AddToGroup("alley_spawn", true);
                Shapes.MapTag(spawn, "map_alley_dark", new Vector2(3f, 3f));
                Shapes.Child(g, new FogVolume
                {
                    Position = p + new Vector3(0f, 1.2f, 0f),
                    Size = new Vector3(7f, 3f, 7f),
                    Material = new FogMaterial { Density = 0.35f, Albedo = new Color(0.6f, 0.68f, 0.85f) },
                }, "AlleyFog");
            }
            else if ((i + j) % 2 == 0)
                LanternPost(g, $"AlleyLantern_{i}_{j}", p + new Vector3(1.3f, 0f, 1.3f), 2.6f, (i * 3 + j) % 3 == 0);
        }
    }

    static void Park(Node3D g, float w, float d)
    {
        Shapes.MapTag(g, "map_park", new Vector2(w, d));
        // Lotus pond with a little sala
        // Raised pond: a dark bed, the water shader a little above it and a stone kerb all round.
        var bed = Shapes.Cylinder(g, new Vector3(0f, 0.02f, 0f), 12f, 12f, 0.04f, new Color(0.05f, 0.07f, 0.06f), 0f, "PondBed");
        bed.Scale = new Vector3(1.4f, 1f, 1f);
        var pond = Shapes.Child(g, new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 12f, BottomRadius = 12f, Height = 0.02f, RadialSegments = 48 },
            MaterialOverride = WaterMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, 0.36f, 0f),
            Scale = new Vector3(1.4f, 1f, 1f),
        }, "PondWater");
        pond.AddToGroup("water_surface", true);
        for (int i = 0; i < 40; i++)
        {
            float a = i / 40f * Mathf.Tau;
            var kerb = Shapes.Box(g, new Vector3(Mathf.Cos(a) * 12.3f * 1.4f, 0.2f, Mathf.Sin(a) * 12.3f), new Vector3(0.6f, 0.4f, 2.2f), Stone.Lightened(_rng.RandfRange(-0.05f, 0.05f)), 0f, "Kerb");
            kerb.Rotation = new Vector3(0f, Mathf.Atan2(-1.4f * Mathf.Sin(a), Mathf.Cos(a)), 0f);
        }
        for (int i = 0; i < 18; i++)
        {
            var p = new Vector3(_rng.RandfRange(-14f, 14f), 0.37f, _rng.RandfRange(-9f, 9f));
            if (new Vector2(p.X / 1.4f, p.Z).Length() > 11f)
                continue;
            Shapes.Cylinder(g, p, 0.5f, 0.5f, 0.02f, new Color(0.2f, 0.42f, 0.2f), 0f, "LotusPad");
            if (i % 3 == 0)
                Shapes.Sphere(g, p + new Vector3(0f, 0.2f, 0f), 0.14f, new Color(1f, 0.6f, 0.75f), 0.6f, "Lotus");
        }
        var sala = Shapes.Group(g, "Sala", new Vector3(0f, 0f, 16f));
        foreach (var x in new[] { -2f, 2f })
        foreach (var z in new[] { -2f, 2f })
            Shapes.Cylinder(sala, new Vector3(x, 1.4f, z), 0.12f, 0.12f, 2.8f, new Color(0.9f, 0.88f, 0.84f), 0f, "Pillar");
        PyramidRoof(sala, new Vector3(0f, 3.6f, 0f), 2.8f, 1.6f, Tile, "Roof");
        for (int i = 0; i < 10; i++)
            Tree(g, "ParkTree", new Vector3(_rng.RandfRange(-w * 0.45f, w * 0.45f), 0f, _rng.RandfRange(-d * 0.45f, d * 0.45f)) * (i % 2 == 0 ? 1f : 0.9f) + new Vector3(0f, 0f, 0f), _rng.RandfRange(1.2f, 1.7f));
        Banyan(g, "OldBanyan", new Vector3(-w * 0.3f, 0f, -d * 0.3f), 1.3f, false);
    }

    // ป่าช้าเก่า: old graveyard with crooked trees and small stupas.
    static void Graveyard(Node3D g, float w, float d)
    {
        Shapes.MapTag(g, "map_graveyard", new Vector2(w, d));
        var white = new Color(0.68f, 0.66f, 0.62f);
        for (int i = 0; i < 40; i++)
        {
            var p = new Vector3(_rng.RandfRange(-w * 0.42f, w * 0.42f), 0f, _rng.RandfRange(-d * 0.42f, d * 0.42f));
            float s = _rng.RandfRange(0.6f, 1.1f);
            var st = Shapes.Group(g, "Stupa", p, _rng.RandfRange(-0.1f, 0.1f));
            st.Rotation = new Vector3(_rng.RandfRange(-0.08f, 0.08f), 0f, _rng.RandfRange(-0.08f, 0.08f));
            Shapes.Box(st, new Vector3(0f, 0.3f * s, 0f), new Vector3(0.7f, 0.6f, 0.7f) * s, white.Darkened(_rng.RandfRange(0f, 0.25f)), 0f, "Base");
            Shapes.Cylinder(st, new Vector3(0f, 0.9f * s, 0f), 0.02f, 0.25f * s, 0.8f * s, white, 0f, "Spire");
        }
        for (int i = 0; i < 9; i++)
            DeadTree(g, "GraveTree", new Vector3(_rng.RandfRange(-w * 0.45f, w * 0.45f), 0f, _rng.RandfRange(-d * 0.45f, d * 0.45f)), _rng.RandfRange(1.1f, 1.6f));
        Takian(g, "GraveTakian", Vector3.Zero, 1.4f);
        var spawn = Shapes.Group(g, "AlleySpawn_Grave", new Vector3(6f, 0f, 6f));
        spawn.AddToGroup("alley_spawn", true);
        Shapes.MapTag(spawn, "map_alley_dark", new Vector2(3f, 3f));
    }

    // ======================================================================
    //  ทุ่งนาหุ่นไล่กา
    // ======================================================================

    static readonly Vector3[] HamletCenters = { new(-120f, 0f, 40f), new(120f, 0f, 40f) };
    static readonly Vector3[] ForestPatches = { new(-150f, 0f, 140f), new(150f, 0f, 150f), new(-80f, 0f, -80f), new(85f, 0f, -78f), new(45f, 0f, 120f) };
    const float FieldRoadZ = 40f;

    public static Node3D BuildThungNa()
    {
        _rng = new RandomNumberGenerator { Seed = 20251028 };
        var world = new Node3D { Name = "Zone_ThungNa" };

        ResetLayout();
        BuildFieldTerrain(world);
        BuildLagoon(world);
        BuildFieldVillage(world);
        BuildFieldRoads(world);
        BuildHamlets(world);
        BuildWaterVillage(world);
        BuildPaddies(world);
        BuildFieldTrees(world);
        BuildThickets(world, 26);
        BuildFireflies(world, Half);
        return world;
    }

    // Lagoon floor depth below the water surface (y = 0) at local (x, z); land is 0.
    // The basin deepens away from the shore and rises again toward the zone's outer edges.
    public static float LagoonDepth(float x, float z)
    {
        float fromShore = ThungNaShoreZ - z;
        if (fromShore <= 0f)
            return 0f;
        float edge = Mathf.Min(Half - Mathf.Abs(x), Half + z);
        return 1.9f * Mathf.SmoothStep(0f, 8f, fromShore) * Mathf.SmoothStep(0f, 9f, edge);
    }

    static void BuildFieldTerrain(Node3D world)
    {
        const float cell = 3f;
        int n = (int)(Half * 2f / cell);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 V(int i, int j)
        {
            float x = -Half + i * cell, z = -Half + j * cell;
            return new Vector3(x, -LagoonDepth(x, z), z);
        }
        Color C(Vector3 v)
        {
            float d = -v.Y;
            if (d > 0.05f)
                return new Color(0.2f, 0.18f, 0.12f).Lerp(new Color(0.1f, 0.1f, 0.08f), Mathf.Clamp(d / 1.9f, 0f, 1f));
            float shore = Mathf.Clamp((v.Z - ThungNaShoreZ) / 4f, 0f, 1f);
            return new Color(0.3f, 0.25f, 0.17f).Lerp(GroundGreen, shore);
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
    }

    public static ShaderMaterial WaterMaterial() => new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, depth_draw_always, specular_schlick_ggx;
uniform vec3 deep_color : source_color = vec3(0.012, 0.04, 0.05);
uniform vec3 shallow_tint : source_color = vec3(0.55, 0.75, 0.72);
uniform vec3 foam_color : source_color = vec3(0.8, 0.86, 0.9);
uniform float rain = 0.0;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
varying vec3 wpos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float waves(vec2 p, float t) {
    return noise(p * 0.5 + vec2(t * 0.07, t * 0.04)) * 0.6
         + noise(p * 1.3 - vec2(t * 0.1, -t * 0.06)) * 0.3
         + noise(p * 3.2 + vec2(t * 0.18, t * 0.12)) * 0.1;
}
// Rain: expanding rings from random points in a grid of cells.
float ripples(vec2 p, float t) {
    vec2 cell = floor(p * 0.9);
    float r = 0.0;
    for (int dx = -1; dx <= 1; dx++)
    for (int dy = -1; dy <= 1; dy++) {
        vec2 c = cell + vec2(float(dx), float(dy));
        float h = hash(c);
        vec2 center = (c + vec2(hash(c + 3.1), hash(c + 7.7))) / 0.9;
        float ph = fract(t * 0.9 + h);
        float d = length(p - center) - ph * 1.1;
        r += sin(d * 30.0) * smoothstep(0.14, 0.0, abs(d)) * (1.0 - ph);
    }
    return r;
}

void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    VERTEX.y += (sin(wpos.x * 0.4 + TIME * 0.9) + sin(wpos.z * 0.33 - TIME * 0.7)) * 0.02;
}

void fragment() {
    vec2 p = wpos.xz;
    float e = 0.08;
    float h0 = waves(p, TIME);
    vec2 grad = vec2(waves(p + vec2(e, 0.0), TIME) - h0, waves(p + vec2(0.0, e), TIME) - h0) / e * 0.3;
    if (rain > 0.01) {
        float r0 = ripples(p, TIME);
        grad += vec2(ripples(p + vec2(e, 0.0), TIME) - r0, ripples(p + vec2(0.0, e), TIME) - r0) / e * 0.025 * rain;
    }
    vec3 n = normalize(vec3(-grad.x, 1.0, -grad.y));
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

    // Thickness of water between this surface and whatever is behind it.
    float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
    vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
    behind.xyz /= behind.w;
    float thick = max(0.0, VERTEX.z - behind.z);

    vec2 offset = n.xz * 0.025 * clamp(thick, 0.0, 1.0);
    vec3 refracted = textureLod(screen_tex, SCREEN_UV + offset, 0.0).rgb;
    float clarity = exp(-thick * 1.3);
    vec3 col = mix(deep_color, refracted * shallow_tint, clarity);
    float foam = (1.0 - smoothstep(0.0, 0.28, thick)) * (0.55 + 0.45 * noise(p * 5.0 + TIME * 0.6));
    col = mix(col, foam_color, foam * 0.75);

    ALBEDO = col;
    ROUGHNESS = mix(0.03, 0.18, rain);
    METALLIC = 0.0;
    SPECULAR = 0.75;
}",
        },
    };

    static void BuildLagoon(Node3D world)
    {
        float depthZ = Half + ThungNaShoreZ + 1.5f;
        var water = Shapes.Group(world, "Lagoon", new Vector3(0f, 0f, -Half + depthZ * 0.5f));
        Shapes.MapTag(water, "map_water", new Vector2(Half * 2f, depthZ));
        water.AddToGroup("water_area", true);
        var surface = Shapes.Child(water, new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(Half * 2f, depthZ), SubdivideWidth = 120, SubdivideDepth = 30 },
            MaterialOverride = WaterMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        }, "Water");
        surface.AddToGroup("water_surface", true);

        // Mangroves (ต้นโกงกาง) along the far edges of the lagoon, standing in the water.
        var mangroves = Shapes.Group(world, "Mangroves");
        int k = 0;
        for (float x = -Half + 5f; x <= Half - 5f; x += _rng.RandfRange(6f, 10f))
            if (Mathf.Abs(x) > 7f) // the boardwalk north to the marsh
                Mangrove(mangroves, $"Mangrove{k++}", new Vector3(x, 0f, _rng.RandfRange(-Half + 3f, -Half + 9f)));
        foreach (var side in new[] { -1f, 1f })
            for (float z = -Half + 12f; z <= ThungNaShoreZ - 6f; z += _rng.RandfRange(6f, 9f))
                Mangrove(mangroves, $"Mangrove{k++}", new Vector3(side * _rng.RandfRange(Half - 9f, Half - 3f), 0f, z));
        for (int i = 0; i < 26; i++)
        {
            var p = new Vector3(_rng.RandfRange(-Half + 20f, Half - 20f), 0f, _rng.RandfRange(-Half + 15f, ThungNaShoreZ - 22f));
            if (Mathf.Abs(p.X) < 40f && p.Z > ThungNaShoreZ - 50f || Mathf.Abs(p.X) < 8f)
                continue; // keep the water village and the boardwalk open
            Mangrove(mangroves, $"Mangrove{k++}", p);
        }
    }

    // Mangrove: short trunk on arching prop roots that reach down into the water.
    static void Mangrove(Node3D parent, string name, Vector3 pos, float? bedY = null)
    {
        var m = Shapes.Group(parent, name, pos, _rng.Randf() * Mathf.Tau);
        float bed = (bedY ?? -LagoonDepth(pos.X, pos.Z)) - 0.1f;
        var bark = new Color(0.32f, 0.26f, 0.2f);
        float s = _rng.RandfRange(0.9f, 1.4f);
        Shapes.Cylinder(m, new Vector3(0f, 2.2f * s, 0f), 0.12f, 0.2f, 2.4f * s, bark, 0f, "Trunk");
        int roots = 7;
        for (int i = 0; i < roots; i++)
        {
            float a = i / (float)roots * Mathf.Tau + _rng.RandfRange(-0.2f, 0.2f);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var top = new Vector3(0f, 1.2f * s, 0f) + dir * 0.1f;
            var knee = dir * 0.9f * s + new Vector3(0f, 1.05f * s, 0f);
            var foot = dir * 1.6f * s + new Vector3(0f, bed, 0f);
            RootSegment(m, top, knee, bark);
            RootSegment(m, knee, foot, bark);
        }
        foreach (var (p, r) in new[] { (new Vector3(0f, 3.8f, 0f), 1.9f), (new Vector3(1.1f, 3.3f, 0.4f), 1.3f), (new Vector3(-1f, 3.4f, -0.5f), 1.3f) })
            Shapes.Sphere(m, p * s, r * s, new Color(0.11f, 0.2f, 0.12f).Lightened(_rng.RandfRange(0f, 0.05f)), 0f, "Crown");
        Shapes.Solid(m, new Vector3(0f, 1f, 0f), new Vector3(1.6f, 3f, 1.6f));
    }

    static void RootSegment(Node3D parent, Vector3 a, Vector3 b, Color color)
    {
        var dir = b - a;
        var seg = Shapes.Cylinder(parent, (a + b) * 0.5f, 0.05f, 0.07f, dir.Length(), color, 0f, "Root");
        seg.Basis = AlignY(dir.Normalized());
    }

    static Basis AlignY(Vector3 up)
    {
        var axis = Vector3.Up.Cross(up);
        if (axis.LengthSquared() < 1e-6f)
            return Basis.Identity;
        return new Basis(axis.Normalized(), Vector3.Up.AngleTo(up));
    }

    static bool NearAny(Vector3 p, IEnumerable<Vector3> centers, float r) => centers.Any(c => c.DistanceTo(p) < r);

    static void BuildPaddies(Node3D world)
    {
        var root = Shapes.Group(world, "Paddies");
        for (int gx = -13; gx <= 13; gx++)
        for (int gz = -7; gz <= 13; gz++)
        {
            var center = new Vector3(gx * 13f, 0f, gz * 13f);
            if (Mathf.Abs(gx) <= 1 && Mathf.Abs(gz) <= 1)
                continue; // village
            if (DistToRoads(center) < 8.6f || Blocked(center, 7.5f))
                continue; // roads, lanes and houses
            if (center.Z < ThungNaShoreZ + 14f || Mathf.Abs(center.X) > Half - 12f || center.Z > Half - 12f)
                continue;
            if (NearAny(center, ForestPatches, 30f))
                continue;
            _plots.Add(center);
            var plot = Shapes.Group(root, $"Plot_{gx}_{gz}", center);
            Shapes.MapTag(plot, "map_plot", new Vector2(11f, 11f));
            float tone = _rng.RandfRange(-0.03f, 0.03f);
            Shapes.Box(plot, new Vector3(0f, 0.03f, 0f), new Vector3(11f, 0.06f, 11f), new Color(0.22f + tone, 0.28f + tone, 0.2f), 0f, "Mud");
            Shapes.Box(plot, new Vector3(0f, 0.12f, -5.7f), new Vector3(11.8f, 0.24f, 0.5f), new Color(0.32f, 0.27f, 0.18f), 0f, "DikeN");
            Shapes.Box(plot, new Vector3(-5.7f, 0.12f, 0f), new Vector3(0.5f, 0.24f, 11.8f), new Color(0.32f, 0.27f, 0.18f), 0f, "DikeW");
            Shapes.Box(plot, new Vector3(0f, 0.12f, 5.7f), new Vector3(11.8f, 0.24f, 0.5f), new Color(0.32f, 0.27f, 0.18f), 0f, "DikeS");
            Shapes.Box(plot, new Vector3(5.7f, 0.12f, 0f), new Vector3(0.5f, 0.24f, 11.8f), new Color(0.32f, 0.27f, 0.18f), 0f, "DikeE");
        }
    }

    static void BuildFieldVillage(Node3D world)
    {
        var v = Shapes.Group(world, "Village");
        Shapes.MapTag(Shapes.Box(v, new Vector3(0f, 0.02f, 0f), new Vector3(22f, 0.04f, 22f), Dirt, 0f, "Plaza"), "map_road", new Vector2(22f, 22f));
        Shapes.MapTag(Shapes.Box(v, new Vector3(0f, 0.025f, (ThungNaShoreZ + 4f - 11f) * 0.5f), new Vector3(2.8f, 0.04f, -(ThungNaShoreZ + 4f) - 11f), Dirt, 0f, "PathNorth"), "map_road", new Vector2(2.8f, -(ThungNaShoreZ + 4f) - 11f));
        AddRoad(new Vector3(0f, 0f, ThungNaShoreZ + 4f), new Vector3(0f, 0f, -11f), 1.4f);
        Occupy(Vector3.Zero, 14f); // the village core keeps its layout
        SignPost(v, new Vector3(13.5f, 0f, 13.5f), ("เวียงหมอก", new Vector3(0f, 0f, 1f)), ("หมู่บ้านกลางน้ำ · บึงบัวสาย", new Vector3(0f, 0f, -1f)),
            ("หมู่บ้านตายาย", new Vector3(-1f, 0f, 0.3f)), ("หมู่บ้านตะวันออก", new Vector3(1f, 0f, 0.3f)));

        var shrine = Shapes.Group(v, "ShrineThungNa", new Vector3(0f, 0f, -3f));
        PhraPhumShrine(shrine, "thung_na", "ศาลพระภูมิทุ่งนา");
        TaSaengFarm(v, new Vector3(-12f, 0f, -7f));
        TwinHouse(v, "HouseTwin", new Vector3(11f, 0f, -7f), -0.3f);
        Hut(v, "HutWest", new Vector3(-8.5f, 0f, 8.5f), Mathf.Pi * 0.5f);
        Granary(v, "Granary", new Vector3(9f, 0f, 8.5f), Mathf.Pi);
        BusStop(v, "BusStopField", new Vector3(5.2f, 0f, 26f), Mathf.Pi * 0.5f, "thung_na", "หมู่บ้านทุ่งนา");

        var ring = Shapes.Group(v, "LanternRing");
        float r = GameData.Config.SafeZoneRadius + 5f;
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * Mathf.Tau + 0.31f;
            LanternPost(ring, $"Lantern{i}", new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), 2.6f, i % 2 == 0);
        }
    }

    static void PhraPhumShrine(Node3D shrine, string id, string name)
    {
        Shrine(shrine, id, name);
        Shapes.Box(shrine, new Vector3(0f, 0.12f, 0f), new Vector3(2.6f, 0.24f, 2.6f), new Color(0.6f, 0.58f, 0.55f), 0f, "Base");
        Shapes.Solid(shrine, new Vector3(0f, 0.12f, 0f), new Vector3(2.6f, 0.24f, 2.6f), "BaseCollider");
        Shapes.Cylinder(shrine, new Vector3(0f, 1.05f, 0f), 0.16f, 0.2f, 1.6f, new Color(0.88f, 0.86f, 0.8f), 0f, "Pillar");
        Shapes.Box(shrine, new Vector3(0f, 1.9f, 0f), new Vector3(1.5f, 0.15f, 1.5f), Gold, 0f, "Platform");
        Shapes.Box(shrine, new Vector3(0f, 2.35f, 0f), new Vector3(1.1f, 0.8f, 0.95f), new Color(0.75f, 0.2f, 0.15f), 0f, "House");
        Shapes.Prism(shrine, new Vector3(0f, 3.05f, 0f), new Vector3(1.5f, 0.75f, 1.35f), Gold, "Roof");
        Shapes.Sphere(shrine, new Vector3(0f, 2.15f, -0.6f), 0.07f, new Color(1f, 0.7f, 0.3f), 4f, "GlowCandle");
        Shapes.Cylinder(shrine, new Vector3(0f, 1.82f, 0f), 0.8f, 0.8f, 0.06f, new Color(0.95f, 0.95f, 0.85f), 0.3f, "Garland");
        Shapes.Light(shrine, new Vector3(0f, 2.4f, -1f), new Color(1f, 0.7f, 0.4f), 2f, 9f, "ShrineLight");
        Shapes.Solid(shrine, new Vector3(0f, 1.2f, 0f), new Vector3(0.6f, 2.4f, 0.6f));
    }

    // บ้านตาแสง: farmhouse with a granary, a buffalo pen and a scarecrow that is (still) just straw.
    static void TaSaengFarm(Node3D parent, Vector3 pos)
    {
        var f = Shapes.Group(parent, "FarmTaSaeng", pos, 0.2f);
        StiltHouse(f, "House", Vector3.Zero, 0f, 2.2f, true);
        Granary(f, "Granary", new Vector3(-6f, 0f, 1f), Mathf.Pi * 0.5f);
        var pen = Shapes.Group(f, "BuffaloPen", new Vector3(5.5f, 0f, 2f));
        for (int i = 0; i <= 4; i++)
        foreach (var z in new[] { -2f, 2f })
            Shapes.Box(pen, new Vector3(-2f + i, 0.6f, z), new Vector3(0.1f, 1.2f, 0.1f), DarkWood, 0f, "FencePost");
        foreach (var z in new[] { -2f, 2f })
        foreach (var y in new[] { 0.45f, 0.95f })
            Shapes.Box(pen, new Vector3(0f, y, z), new Vector3(4.1f, 0.06f, 0.06f), DarkWood, 0f, "FenceRail");
        Shapes.Solid(pen, new Vector3(0f, 0.6f, -2f), new Vector3(4.2f, 1.2f, 0.2f));
        Shapes.Solid(pen, new Vector3(0f, 0.6f, 2f), new Vector3(4.2f, 1.2f, 0.2f));
        // Buffalo
        var buff = Shapes.Group(pen, "Buffalo", new Vector3(0.2f, 0f, 0f), 0.4f);
        var hide = new Color(0.22f, 0.22f, 0.24f);
        Shapes.Box(buff, new Vector3(0f, 1f, 0f), new Vector3(0.9f, 0.8f, 1.8f), hide, 0f, "Body");
        Shapes.Box(buff, new Vector3(0f, 1.05f, -1.05f), new Vector3(0.5f, 0.5f, 0.6f), hide, 0f, "Head");
        foreach (var x in new[] { -0.35f, 0.35f })
        {
            Shapes.Box(buff, new Vector3(x * 1.4f, 1.35f, -1.05f), new Vector3(0.45f, 0.08f, 0.08f), new Color(0.85f, 0.8f, 0.7f), 0f, "Horn").Rotation = new Vector3(0f, 0f, x > 0f ? 0.4f : -0.4f);
            foreach (var z in new[] { -0.6f, 0.6f })
                Shapes.Box(buff, new Vector3(x, 0.35f, z), new Vector3(0.18f, 0.7f, 0.18f), hide, 0f, "Leg");
        }
        // A plain scarecrow
        var sc = Shapes.Group(f, "Scarecrow", new Vector3(0f, 0f, 7f));
        Shapes.Cylinder(sc, new Vector3(0f, 0.9f, 0f), 0.05f, 0.06f, 1.8f, new Color(0.4f, 0.28f, 0.15f), 0f, "Pole");
        Shapes.Box(sc, new Vector3(0f, 1.4f, 0f), new Vector3(1.5f, 0.08f, 0.08f), new Color(0.4f, 0.28f, 0.15f), 0f, "Bar");
        Shapes.Box(sc, new Vector3(0f, 1.2f, 0f), new Vector3(0.55f, 0.7f, 0.28f), new Color(0.5f, 0.42f, 0.3f), 0f, "Shirt");
        Shapes.Sphere(sc, new Vector3(0f, 1.85f, 0f), 0.24f, new Color(0.78f, 0.7f, 0.5f), 0f, "Head");
        Shapes.Cylinder(sc, new Vector3(0f, 2.08f, 0f), 0.02f, 0.5f, 0.22f, new Color(0.6f, 0.5f, 0.25f), 0f, "Hat");
    }

    // ---------- house styles ----------

    // Stairs from a raised floor down to a landing (ground or pier), walkable (ramp collider) and correctly
    // oriented: the top tread meets the floor edge at local z = -edge and the flight runs outward (-z).
    static void Stairs(Node3D parent, float edge, float height, float width = 1.2f, float groundY = 0f)
    {
        float rise = height - groundY;
        float run = rise * 1.1f;
        int steps = Mathf.Max(3, Mathf.RoundToInt(rise / 0.24f));
        var g = Shapes.Group(parent, "Stairs");
        for (int i = 0; i < steps; i++)
        {
            float y = height - (i + 1) * rise / steps;
            float z = -edge - (i + 0.5f) * run / steps;
            Shapes.Box(g, new Vector3(0f, y + 0.03f, z), new Vector3(width, 0.06f, run / steps + 0.06f), Wood, 0f, "Tread");
        }
        float slope = Mathf.Atan2(rise, run);
        float len = Mathf.Sqrt(rise * rise + run * run);
        var mid = new Vector3(0f, groundY + rise * 0.5f, -edge - run * 0.5f);
        foreach (var x in new[] { -width * 0.5f - 0.04f, width * 0.5f + 0.04f })
        {
            Shapes.Box(g, mid + new Vector3(x, 0f, 0f), new Vector3(0.08f, 0.22f, len), DarkWood, 0f, "Stringer").Rotation = new Vector3(-slope, 0f, 0f);
            Shapes.Box(g, mid + new Vector3(x, 0.95f, 0f), new Vector3(0.06f, 0.06f, len), DarkWood, 0f, "Handrail").Rotation = new Vector3(-slope, 0f, 0f);
        }
        var body = Shapes.Child(g, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = mid, Rotation = new Vector3(-slope, 0f, 0f) }, "StairCollider");
        Shapes.Child(body, new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(width, 0.1f, len) } });
    }

    public static float StairRun(float stilt, float landing) => (stilt - landing) * 1.1f;

    // Gable roof (ridge along X) with slabs, ridge beam, gable ends and optional กาแล on the gables.
    static void GableRoof(Node3D h, float wallTop, float width, float depth, float zCenter, Color roof, Color gable, bool kalae)
    {
        const float slope = 0.72f;
        float halfD = depth * 0.5f;
        float rise = halfD * Mathf.Tan(slope);
        foreach (var side in new[] { -1f, 1f })
        {
            var slab = Shapes.Box(h, new Vector3(0f, wallTop + rise * 0.5f, zCenter + side * halfD * 0.5f),
                new Vector3(width + 0.9f, 0.16f, halfD / Mathf.Cos(slope) + 0.45f), roof, 0f, side < 0f ? "RoofFront" : "RoofBack");
            slab.Rotation = new Vector3(side * slope, 0f, 0f);
        }
        Shapes.Box(h, new Vector3(0f, wallTop + rise + 0.03f, zCenter), new Vector3(width + 1f, 0.14f, 0.22f), DarkWood, 0f, "Ridge");
        foreach (var x in new[] { -width * 0.5f, width * 0.5f })
        {
            Shapes.Prism(h, new Vector3(x, wallTop + rise * 0.5f - 0.05f, zCenter), new Vector3(depth - 0.2f, rise, 0.1f), gable, "Gable")
                .Rotation = new Vector3(0f, Mathf.Pi * 0.5f, 0f);
            if (kalae)
                foreach (var s in new[] { -1f, 1f })
                    Shapes.Box(h, new Vector3(x * 1.08f, wallTop + rise + 0.55f, zCenter + s * 0.25f), new Vector3(0.08f, 1.3f, 0.22f), Wood.Lightened(0.1f), 0f, "Kalae")
                        .Rotation = new Vector3(s * 0.55f, 0f, 0f);
        }
    }

    // เรือนไม้ยกพื้น: tall stilts you can walk under, a veranda with a proper staircase, glowing windows.
    // landing = height the stairs come down to (0 = ground, DeckTop = a pier); stilts reach down to stiltBottom.
    static void StiltHouse(Node3D parent, string name, Vector3 pos, float yaw, float stilt, bool kalae, float landing = 0f, float? stiltBottom = null)
    {
        var h = Shapes.Group(parent, name, pos, yaw);
        Shapes.MapTag(h, "map_house", new Vector2(6.4f, 7f));
        var wood = Wood.Lightened(_rng.RandfRange(-0.05f, 0.08f));
        const float w = 6f, wallD = 4.6f, veranda = 1.8f, wallH = 2.8f;
        float bottom = stiltBottom ?? landing;
        float floorZ = -veranda * 0.5f;
        float depth = wallD + veranda;
        float edge = wallD * 0.5f + veranda;
        foreach (var x in new[] { -w * 0.5f + 0.2f, 0f, w * 0.5f - 0.2f })
        foreach (var z in new[] { -edge + 0.2f, -wallD * 0.5f, wallD * 0.5f - 0.2f })
            Shapes.Cylinder(h, new Vector3(x, (stilt + bottom) * 0.5f, z), 0.12f, 0.14f, stilt - bottom, DarkWood, 0f, "Stilt");
        Shapes.Box(h, new Vector3(0f, stilt - 0.1f, floorZ), new Vector3(w + 0.3f, 0.2f, depth + 0.1f), wood.Darkened(0.1f), 0f, "Floor");
        Shapes.Solid(h, new Vector3(0f, stilt - 0.1f, floorZ), new Vector3(w + 0.3f, 0.2f, depth + 0.1f), "FloorCollider");

        Shapes.Box(h, new Vector3(0f, stilt + wallH * 0.5f, 0f), new Vector3(w, wallH, wallD), wood, 0f, "Walls");
        Shapes.Solid(h, new Vector3(0f, stilt + wallH * 0.5f + 0.5f, 0f), new Vector3(w, wallH + 1f, wallD), "WallCollider");
        for (int i = -5; i <= 5; i++)
            Shapes.Box(h, new Vector3(i * 0.55f, stilt + wallH * 0.5f, -wallD * 0.5f - 0.01f), new Vector3(0.05f, wallH, 0.02f), DarkWood, 0f, "Seam");
        Shapes.Box(h, new Vector3(0f, stilt + 1.1f, -wallD * 0.5f - 0.02f), new Vector3(1.1f, 2.2f, 0.04f), new Color(0.14f, 0.09f, 0.06f), 0f, "Door");
        bool lit = _rng.Randf() < 0.75f;
        foreach (var x in new[] { -1.9f, 1.9f })
            Shapes.Box(h, new Vector3(x, stilt + 1.5f, -wallD * 0.5f - 0.02f), new Vector3(1f, 0.85f, 0.04f), lit ? WindowGlow : new Color(0.08f, 0.06f, 0.05f), lit ? 2f : 0f, lit ? "GlowWindow" : "Window");
        Shapes.Box(h, new Vector3(w * 0.5f + 0.01f, stilt + 1.5f, 0f), new Vector3(0.04f, 0.85f, 1f), lit ? WindowGlow : new Color(0.08f, 0.06f, 0.05f), lit ? 1.6f : 0f, lit ? "GlowWindowSide" : "WindowSide");
        Shapes.Box(h, new Vector3(-w * 0.5f - 0.01f, stilt + 1.5f, 0f), new Vector3(0.04f, 0.85f, 1f), new Color(0.08f, 0.06f, 0.05f), 0f, "WindowSideW");
        foreach (var x in new[] { -1.5f, 1.5f })
            Shapes.Box(h, new Vector3(x, stilt + 1.5f, wallD * 0.5f + 0.02f), new Vector3(0.9f, 0.8f, 0.04f), new Color(0.08f, 0.06f, 0.05f), 0f, "WindowBack");
        for (int i = -5; i <= 5; i++)
            Shapes.Box(h, new Vector3(i * 0.55f, stilt + wallH * 0.5f, wallD * 0.5f + 0.01f), new Vector3(0.05f, wallH, 0.02f), DarkWood, 0f, "SeamBack");

        GableRoof(h, stilt + wallH, w, wallD + 0.6f, 0f, Thatch.Lightened(_rng.RandfRange(-0.04f, 0.04f)), wood, kalae);

        for (int i = -6; i <= 6; i++)
        {
            if (Mathf.Abs(i) <= 1)
                continue;
            Shapes.Box(h, new Vector3(i * 0.48f, stilt + 0.5f, -edge + 0.05f), new Vector3(0.06f, 1f, 0.06f), DarkWood, 0f, "Baluster");
        }
        foreach (var s in new[] { -1f, 1f })
        {
            Shapes.Box(h, new Vector3(s * 1.95f, stilt + 1f, -edge + 0.05f), new Vector3(2.1f, 0.07f, 0.09f), DarkWood, 0f, "Rail");
            Shapes.Solid(h, new Vector3(s * 1.95f, stilt + 0.6f, -edge + 0.05f), new Vector3(2.1f, 1.2f, 0.1f), "RailCollider");
            Shapes.Box(h, new Vector3(s * (w * 0.5f + 0.1f), stilt + 1f, floorZ - wallD * 0.5f), new Vector3(0.07f, 0.07f, veranda), DarkWood, 0f, "SideRail");
        }
        Stairs(h, edge, stilt, 1.2f, landing);
        if (lit && _rng.Randf() < 0.35f)
            Shapes.Light(h, new Vector3(0f, stilt + 2.1f, -edge + 0.4f), WindowGlow, 1.2f, 8f, "PorchLight");
    }

    // กระท่อม: small low hut with a pyramid thatch roof.
    static void Hut(Node3D parent, string name, Vector3 pos, float yaw)
    {
        var h = Shapes.Group(parent, name, pos, yaw);
        Shapes.MapTag(h, "map_house", new Vector2(4.2f, 4.2f));
        const float stilt = 1.1f, s = 3.8f;
        foreach (var x in new[] { -1.7f, 1.7f })
        foreach (var z in new[] { -1.7f, 1.7f })
            Shapes.Cylinder(h, new Vector3(x, stilt * 0.5f, z), 0.1f, 0.11f, stilt, DarkWood, 0f, "Stilt");
        Shapes.Box(h, new Vector3(0f, stilt, 0f), new Vector3(s + 0.4f, 0.16f, s + 0.4f), Wood.Darkened(0.1f), 0f, "Floor");
        Shapes.Box(h, new Vector3(0f, stilt + 1.2f, 0f), new Vector3(s, 2.3f, s), new Color(0.55f, 0.46f, 0.3f), 0f, "BambooWalls");
        Shapes.Box(h, new Vector3(0f, stilt + 1.05f, -s * 0.5f - 0.02f), new Vector3(0.9f, 2f, 0.04f), new Color(0.12f, 0.08f, 0.05f), 0f, "Door");
        bool lit = _rng.Randf() < 0.6f;
        Shapes.Box(h, new Vector3(1.2f, stilt + 1.4f, -s * 0.5f - 0.02f), new Vector3(0.7f, 0.6f, 0.04f), lit ? WindowGlow : new Color(0.08f, 0.06f, 0.05f), lit ? 1.8f : 0f, lit ? "GlowWindow" : "Window");
        foreach (var side in new[] { -1f, 1f })
            Shapes.Box(h, new Vector3(side * (s * 0.5f + 0.02f), stilt + 1.4f, 0f), new Vector3(0.04f, 0.6f, 0.7f), new Color(0.08f, 0.06f, 0.05f), 0f, "WindowSide");
        Shapes.Box(h, new Vector3(0f, stilt + 1.4f, s * 0.5f + 0.02f), new Vector3(0.7f, 0.6f, 0.04f), new Color(0.08f, 0.06f, 0.05f), 0f, "WindowBack");
        PyramidRoof(h, new Vector3(0f, stilt + 2.35f + 1.1f, 0f), s * 0.5f + 0.7f, 2.2f, Thatch, "Roof");
        Stairs(h, s * 0.5f + 0.2f, stilt, 0.9f);
        Shapes.Solid(h, new Vector3(0f, (stilt + 2.4f) * 0.5f + 0.1f, 0f), new Vector3(s + 0.4f, stilt + 2.4f, s + 0.4f));
    }

    // เรือนแฝด: two gabled halls side by side sharing one deck.
    static void TwinHouse(Node3D parent, string name, Vector3 pos, float yaw)
    {
        var h = Shapes.Group(parent, name, pos, yaw);
        Shapes.MapTag(h, "map_house", new Vector2(9.6f, 6.6f));
        const float stilt = 2.1f, wallH = 2.7f, wallD = 4.4f, hall = 4f;
        var wood = Wood.Lightened(_rng.RandfRange(-0.04f, 0.06f));
        foreach (var x in new[] { -4.5f, -1.5f, 1.5f, 4.5f })
        foreach (var z in new[] { -3.1f, 0f, 2f })
            Shapes.Cylinder(h, new Vector3(x, stilt * 0.5f, z), 0.12f, 0.14f, stilt, DarkWood, 0f, "Stilt");
        Shapes.Box(h, new Vector3(0f, stilt - 0.1f, -0.6f), new Vector3(9.8f, 0.2f, 6.2f), wood.Darkened(0.1f), 0f, "Deck");
        Shapes.Solid(h, new Vector3(0f, stilt - 0.1f, -0.6f), new Vector3(9.8f, 0.2f, 6.2f), "DeckCollider");
        foreach (var x in new[] { -2.6f, 2.6f })
        {
            var hallNode = Shapes.Group(h, x < 0f ? "HallL" : "HallR", new Vector3(x, 0f, 0f));
            Shapes.Box(hallNode, new Vector3(0f, stilt + wallH * 0.5f, 0f), new Vector3(hall, wallH, wallD), wood, 0f, "Walls");
            Shapes.Solid(hallNode, new Vector3(0f, stilt + wallH * 0.5f + 0.5f, 0f), new Vector3(hall, wallH + 1f, wallD), "WallCollider");
            Shapes.Box(hallNode, new Vector3(0f, stilt + 1.1f, -wallD * 0.5f - 0.02f), new Vector3(1f, 2.2f, 0.04f), new Color(0.14f, 0.09f, 0.06f), 0f, "Door");
            Shapes.Box(hallNode, new Vector3(1.2f, stilt + 1.5f, -wallD * 0.5f - 0.02f), new Vector3(0.8f, 0.8f, 0.04f), WindowGlow, 1.8f, "GlowWindow");
            Shapes.Box(hallNode, new Vector3(0f, stilt + 1.5f, wallD * 0.5f + 0.02f), new Vector3(0.9f, 0.8f, 0.04f), new Color(0.08f, 0.06f, 0.05f), 0f, "WindowBack");
            Shapes.Box(hallNode, new Vector3(x < 0f ? -hall * 0.5f - 0.02f : hall * 0.5f + 0.02f, stilt + 1.5f, 0f), new Vector3(0.04f, 0.8f, 0.9f), WindowGlow, 1.4f, "GlowWindowSide");
            GableRoof(hallNode, stilt + wallH, hall, wallD + 0.6f, 0f, Tile.Darkened(0.15f), wood, true);
        }
        // Deck railing along the front, broken for the stairs.
        foreach (var s in new[] { -1f, 1f })
        {
            Shapes.Box(h, new Vector3(s * 2.8f, stilt + 0.9f, -3.65f), new Vector3(4.2f, 0.07f, 0.08f), DarkWood, 0f, "Rail");
            Shapes.Solid(h, new Vector3(s * 2.8f, stilt + 0.5f, -3.65f), new Vector3(4.2f, 1f, 0.1f), "RailCollider");
            for (int i = 0; i < 6; i++)
                Shapes.Box(h, new Vector3(s * (0.95f + i * 0.75f), stilt + 0.45f, -3.65f), new Vector3(0.06f, 0.9f, 0.06f), DarkWood, 0f, "Baluster");
        }
        Stairs(h, 3.7f, stilt, 1.2f);
    }

    // ยุ้งข้าว: small closed rice granary on tall stilts, walls flaring outward.
    static void Granary(Node3D parent, string name, Vector3 pos, float yaw)
    {
        var h = Shapes.Group(parent, name, pos, yaw);
        Shapes.MapTag(h, "map_house", new Vector2(3.2f, 3.2f));
        const float stilt = 1.7f;
        foreach (var x in new[] { -1.2f, 1.2f })
        foreach (var z in new[] { -1.2f, 1.2f })
            Shapes.Cylinder(h, new Vector3(x, stilt * 0.5f, z), 0.12f, 0.14f, stilt, DarkWood, 0f, "Stilt");
        Shapes.Box(h, new Vector3(0f, stilt, 0f), new Vector3(3f, 0.16f, 3f), Wood.Darkened(0.1f), 0f, "Floor");
        Shapes.Add(h, new CylinderMesh { TopRadius = 1.95f, BottomRadius = 1.6f, Height = 2f, RadialSegments = 4, Rings = 1 },
            new Vector3(0f, stilt + 1.05f, 0f), Wood.Lightened(0.04f), 0f, "FlaredWalls").Rotation = new Vector3(0f, Mathf.Pi * 0.25f, 0f);
        GableRoof(h, stilt + 2.05f, 2.8f, 2.9f, 0f, Thatch, Wood, false);
        Shapes.Box(h, new Vector3(0f, stilt * 0.5f + 0.4f, -1.8f), new Vector3(0.5f, stilt + 0.6f, 0.08f), DarkWood, 0f, "Ladder").Rotation = new Vector3(-0.3f, 0f, 0f);
        Shapes.Solid(h, new Vector3(0f, stilt + 1f, 0f), new Vector3(3f, 2.2f, 3f));
    }

    static void LanternPost(Node3D parent, string name, Vector3 pos, float height, bool light)
    {
        var post = Shapes.Group(parent, name, pos);
        Shapes.Box(post, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.14f, height, 0.14f), DarkWood, 0f, "Post");
        Shapes.Box(post, new Vector3(0.25f, height, 0f), new Vector3(0.6f, 0.07f, 0.07f), DarkWood, 0f, "Arm");
        Shapes.Box(post, new Vector3(0.48f, height - 0.26f, 0f), new Vector3(0.26f, 0.36f, 0.26f), new Color(0.2f, 0.12f, 0.08f), 0f, "Frame");
        Shapes.Sphere(post, new Vector3(0.48f, height - 0.26f, 0f), 0.16f, new Color(1f, 0.6f, 0.28f), 3.5f, "GlowLamp");
        if (light)
            Shapes.Light(post, new Vector3(0.48f, height - 0.34f, 0f), new Color(1f, 0.6f, 0.3f), 1.2f, 8f, "Light");
    }

    // ---------- water village ----------

    static void BuildWaterVillage(Node3D world)
    {
        var wv = Shapes.Group(world, "WaterVillage");
        float zCross = ThungNaShoreZ - 11f;
        const float pierHalf = 1.3f, stilt = 2.4f;
        float bed = -1.95f;

        var ramp = Shapes.Box(wv, new Vector3(0f, DeckTop * 0.5f - 0.05f, ThungNaShoreZ + 2f), new Vector3(2.6f, 0.12f, 4.1f), Wood, 0f, "Ramp");
        ramp.Rotation = new Vector3(Mathf.Atan2(DeckTop, 4f), 0f, 0f);
        var rampBody = Shapes.Child(wv, new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = ramp.Position, Rotation = ramp.Rotation }, "RampCollider");
        Shapes.Child(rampBody, new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.6f, 0.12f, 4.1f) } });

        // House positions along the cross pier: stairs come down onto the pier, and its railing has a gap there.
        float[] north = { -24f, -11f, 11f, 24f };
        float[] south = { -18f, 18f };
        float edge = 4.1f, run = StairRun(stilt, DeckTop);
        float northZ = zCross - pierHalf + 0.35f - edge - run;
        float southZ = zCross + pierHalf - 0.35f + edge + run;

        Deck(wv, "PierMainSouth", new Vector3(0f, 0f, (ThungNaShoreZ + zCross + pierHalf) * 0.5f), new Vector2(2.6f, ThungNaShoreZ - (zCross + pierHalf)), railX: true);
        Deck(wv, "PierCrossing", new Vector3(0f, 0f, zCross), new Vector2(2.6f, 2.6f));
        Deck(wv, "PierMainNorth", new Vector3(0f, 0f, zCross - 5.7f), new Vector2(2.6f, 8.8f), railX: true);
        // Long boardwalk north across the lagoon to the edge of บึงบัวสาย.
        float bwStart = zCross - 10.1f, bwEnd = -Half + 1.5f;
        Deck(wv, "BoardwalkToBueng", new Vector3(0f, 0f, (bwStart + bwEnd) * 0.5f), new Vector2(2.6f, bwStart - bwEnd), railX: true);
        for (float z = bwStart - 12f; z > bwEnd + 4f; z -= 16f)
            LanternPost(wv, $"BoardwalkLantern{z:0}", new Vector3(1.4f, DeckTop, z), 2.4f, ((int)z / 16) % 2 == 0);
        Deck(wv, "PierWest", new Vector3(-16.15f, 0f, zCross), new Vector2(29.7f, 2.6f), railZ: true,
            gapsNeg: north.Where(x => x < 0f).Select(x => x + 16.15f).ToArray(), gapsPos: south.Where(x => x < 0f).Select(x => x + 16.15f).ToArray());
        Deck(wv, "PierEast", new Vector3(16.15f, 0f, zCross), new Vector2(29.7f, 2.6f), railZ: true,
            gapsNeg: north.Where(x => x > 0f).Select(x => x - 16.15f).ToArray(), gapsPos: south.Where(x => x > 0f).Select(x => x - 16.15f).ToArray());

        int n = 1;
        foreach (var x in north)
            StiltHouse(wv, $"WaterHouse{n++}", new Vector3(x, 0f, northZ), Mathf.Pi, stilt, n % 2 == 0, DeckTop, bed);
        foreach (var x in south)
            StiltHouse(wv, $"WaterHouse{n++}", new Vector3(x, 0f, southZ), 0f, stilt, n % 2 == 0, DeckTop, bed);

        Boat(wv, "Boat1", new Vector3(-6f, 0f, zCross + 4f), 0.3f, true);
        Boat(wv, "Boat2", new Vector3(7f, 0f, zCross - 14f), -0.8f, false);
        for (int i = 3; i <= 10; i++)
            Boat(wv, $"Boat{i}", new Vector3(_rng.RandfRange(-150f, 150f), 0f, _rng.RandfRange(-170f, ThungNaShoreZ - 20f)), _rng.Randf() * Mathf.Tau, i % 3 == 0);

        int k = 0;
        foreach (var p in new[]
                 {
                     new Vector3(1.4f, DeckTop, zCross + 6f), new Vector3(-1.4f, DeckTop, zCross + 2f), new Vector3(1.4f, DeckTop, zCross - 8f),
                     new Vector3(-6f, DeckTop, zCross - 1.4f), new Vector3(-29f, DeckTop, zCross + 1.4f),
                     new Vector3(6f, DeckTop, zCross + 1.4f), new Vector3(29f, DeckTop, zCross - 1.4f),
                 })
            LanternPost(wv, $"PierLantern{k++}", p, 2.4f, true);
    }

    // A pier section with its top at DeckTop, pilings down to the lagoon bed and waist-high railings
    // (railX = rails at ±x edges, railZ = rails at ±z edges). gapsNeg/gapsPos leave openings in the
    // -z/+z railing at those local x positions for stairs.
    static void Deck(Node3D parent, string name, Vector3 center, Vector2 size, bool railX = false, bool railZ = false,
        float[] gapsNeg = null, float[] gapsPos = null)
    {
        var d = Shapes.Group(parent, name, center);
        Shapes.MapTag(d, "map_deck", size);
        Shapes.Box(d, new Vector3(0f, DeckTop - 0.08f, 0f), new Vector3(size.X, 0.16f, size.Y), Wood, 0f, "Planks");
        Shapes.Solid(d, new Vector3(0f, DeckTop - 0.1f, 0f), new Vector3(size.X, 0.2f, size.Y), "DeckCollider");
        bool alongZ = size.Y > size.X;
        float len = alongZ ? size.Y : size.X;
        for (float t = -len * 0.5f + 0.5f; t < len * 0.5f; t += 0.5f)
        {
            var seam = alongZ ? new Vector3(0f, DeckTop + 0.001f, t) : new Vector3(t, DeckTop + 0.001f, 0f);
            Shapes.Box(d, seam, alongZ ? new Vector3(size.X, 0.004f, 0.03f) : new Vector3(0.03f, 0.004f, size.Y), DarkWood, 0f, "Seam");
        }
        for (float t = -len * 0.5f; t <= len * 0.5f + 0.01f; t += 2.5f)
        foreach (var side in new[] { -1f, 1f })
        {
            var p = alongZ ? new Vector3(side * (size.X * 0.5f - 0.1f), -0.8f, t) : new Vector3(t, -0.8f, side * (size.Y * 0.5f - 0.1f));
            Shapes.Cylinder(d, p, 0.1f, 0.1f, 2.6f, DarkWood, 0f, "Piling");
        }

        // One railing run from a to b along one axis.
        void RailRun(Vector3 a, Vector3 b)
        {
            var mid = (a + b) * 0.5f;
            var span = b - a;
            float l = span.Length();
            if (l < 0.3f)
                return;
            var railSize = Mathf.Abs(span.X) > Mathf.Abs(span.Z) ? new Vector3(l, 0.08f, 0.08f) : new Vector3(0.08f, 0.08f, l);
            Shapes.Box(d, mid + new Vector3(0f, 1f, 0f), railSize, DarkWood, 0f, "Rail");
            Shapes.Solid(d, mid + new Vector3(0f, 0.6f, 0f), railSize + new Vector3(0f, 1.1f, 0f), "RailCollider");
            for (float t = 0f; t <= l + 0.01f; t += 1.25f)
                Shapes.Box(d, a + span.Normalized() * t + new Vector3(0f, 0.5f, 0f), new Vector3(0.08f, 1f, 0.08f), DarkWood, 0f, "RailPost");
        }
        // A railing along x at a fixed z, broken around each gap.
        void RailWithGaps(float z, float[] gaps)
        {
            float start = -size.X * 0.5f;
            foreach (var gx in (gaps ?? System.Array.Empty<float>()).OrderBy(g => g))
            {
                RailRun(new Vector3(start, DeckTop, z), new Vector3(gx - 0.85f, DeckTop, z));
                start = gx + 0.85f;
            }
            RailRun(new Vector3(start, DeckTop, z), new Vector3(size.X * 0.5f, DeckTop, z));
        }
        if (railX)
        {
            RailRun(new Vector3(-size.X * 0.5f, DeckTop, -size.Y * 0.5f), new Vector3(-size.X * 0.5f, DeckTop, size.Y * 0.5f));
            RailRun(new Vector3(size.X * 0.5f, DeckTop, -size.Y * 0.5f), new Vector3(size.X * 0.5f, DeckTop, size.Y * 0.5f));
        }
        if (railZ)
        {
            RailWithGaps(-size.Y * 0.5f, gapsNeg);
            RailWithGaps(size.Y * 0.5f, gapsPos);
        }
    }

    // เรือมาด: a narrow dugout with upturned ends.
    static void Boat(Node3D parent, string name, Vector3 pos, float yaw, bool lantern)
    {
        var b = Shapes.Group(parent, name, pos, yaw);
        Shapes.MapTag(b, "map_boat", new Vector2(1.2f, 5.6f));
        Shapes.Box(b, new Vector3(0f, 0.1f, 0f), new Vector3(1.1f, 0.4f, 4.4f), DarkWood, 0f, "Hull");
        Shapes.Box(b, new Vector3(0f, 0.22f, 0f), new Vector3(0.85f, 0.25f, 4f), new Color(0.12f, 0.08f, 0.06f), 0f, "Inside");
        Shapes.Prism(b, new Vector3(0f, 0.22f, -2.45f), new Vector3(1.1f, 0.6f, 0.6f), DarkWood, "Bow").Rotation = new Vector3(-Mathf.Pi * 0.5f, 0f, 0f);
        Shapes.Prism(b, new Vector3(0f, 0.22f, 2.45f), new Vector3(1.1f, 0.6f, 0.6f), DarkWood, "Stern").Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        Shapes.Box(b, new Vector3(0.25f, 0.36f, 0.6f), new Vector3(0.07f, 0.07f, 3f), Wood, 0f, "Paddle").Rotation = new Vector3(0f, 0.15f, 0f);
        if (lantern)
        {
            Shapes.Sphere(b, new Vector3(0f, 0.6f, -1.7f), 0.14f, new Color(1f, 0.6f, 0.28f), 3.5f, "GlowLamp");
            Shapes.Light(b, new Vector3(0f, 0.7f, -1.7f), new Color(1f, 0.6f, 0.3f), 0.9f, 6f, "Light");
        }
    }

    // ---------- vegetation ----------

    static void BuildFieldTrees(Node3D world)
    {
        var palms = Shapes.Group(world, "Palms");
        for (float x = -Half + 8f; x <= Half - 8f; x += _rng.RandfRange(7f, 12f))
        {
            if (Mathf.Abs(x) < 5f)
                continue;
            Palm(palms, "ShorePalm", new Vector3(x, 0f, ThungNaShoreZ + 2.6f + _rng.RandfRange(-0.3f, 2f)), _rng.RandfRange(7f, 10f));
        }
        for (int i = 0; i < 50; i++)
        {
            var p = new Vector3(_rng.RandiRange(-13, 12) * 13f + 6.5f, 0f, _rng.RandiRange(-6, 12) * 13f + 6.5f);
            if (p.Length() < 24f || DistToRoads(p) < 3f || Blocked(p, 2f) || p.Z < ThungNaShoreZ + 8f)
                continue;
            Palm(palms, "SugarPalm", p, _rng.RandfRange(8f, 10f));
        }

        // Spooky groves: dead trees, a great ตะเคียน wrapped in cloth, banyans and rotten stumps.
        var groves = Shapes.Group(world, "Groves");
        int g = 0;
        foreach (var c in ForestPatches)
        {
            var grove = Shapes.Group(groves, $"Grove{g}", c);
            Shapes.MapTag(grove, "map_grove", new Vector2(50f, 50f));
            Takian(grove, "GreatTakian", Vector3.Zero, _rng.RandfRange(1.4f, 1.9f));
            for (int i = 0; i < 16; i++)
            {
                float a = _rng.Randf() * Mathf.Tau, r = _rng.RandfRange(6f, 24f);
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                switch (i % 4)
                {
                    case 0: Banyan(grove, "Banyan", p, _rng.RandfRange(0.8f, 1.1f), false); break;
                    case 3: Stump(grove, "Stump", p); break;
                    default: DeadTree(grove, "DeadTree", p, _rng.RandfRange(1f, 1.6f)); break;
                }
            }
            g++;
        }
    }

    public static void Tree(Node3D parent, string name, Vector3 p, float s)
    {
        var t = Shapes.Group(parent, name, p);
        Shapes.Cylinder(t, new Vector3(0f, 1.6f * s, 0f), 0.25f * s, 0.38f * s, 3.2f * s, new Color(0.25f, 0.19f, 0.13f), 0f, "Trunk");
        Shapes.Sphere(t, new Vector3(0f, 4f * s, 0f), 2.1f * s, new Color(0.11f, 0.21f, 0.12f).Lightened(_rng.RandfRange(0f, 0.06f)), 0f, "Crown");
        Shapes.Sphere(t, new Vector3(0.9f * s, 3.4f * s, 0.4f * s), 1.4f * s, new Color(0.13f, 0.24f, 0.13f), 0f, "Crown2");
        Shapes.Solid(t, new Vector3(0f, 1.5f, 0f), new Vector3(0.8f * s, 3f, 0.8f * s));
    }

    // Leafless crooked tree with twisted branches.
    static void DeadTree(Node3D parent, string name, Vector3 p, float s)
    {
        var t = Shapes.Group(parent, name, p, _rng.Randf() * Mathf.Tau);
        var bark = new Color(0.22f, 0.19f, 0.17f);
        float lean = _rng.RandfRange(-0.18f, 0.18f);
        var top = Vector3.Zero;
        for (int i = 0; i < 3; i++)
        {
            var dir = new Vector3(lean * (i + 1), 1f, lean * 0.5f * i).Normalized();
            float len = (1.6f - i * 0.3f) * s;
            var next = top + dir * len;
            RootSegmentThick(t, top, next, bark, (0.24f - i * 0.06f) * s);
            if (i > 0)
                for (int b = 0; b < 2; b++)
                {
                    float a = _rng.Randf() * Mathf.Tau;
                    var bdir = new Vector3(Mathf.Cos(a), _rng.RandfRange(0.2f, 0.8f), Mathf.Sin(a)).Normalized();
                    var tip = next + bdir * _rng.RandfRange(1f, 1.8f) * s;
                    RootSegmentThick(t, next, tip, bark, 0.07f * s);
                    var twig = tip + new Vector3(_rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(0.1f, 0.6f), _rng.RandfRange(-0.5f, 0.5f)) * s;
                    RootSegmentThick(t, tip, twig, bark, 0.035f * s);
                }
            top = next;
        }
        Shapes.Solid(t, new Vector3(0f, 1.5f, 0f), new Vector3(0.5f * s, 3f, 0.5f * s));
    }

    static void RootSegmentThick(Node3D parent, Vector3 a, Vector3 b, Color color, float radius)
    {
        var dir = b - a;
        var seg = Shapes.Cylinder(parent, (a + b) * 0.5f, radius * 0.75f, radius, dir.Length(), color, 0f, "Branch");
        seg.Basis = AlignY(dir.Normalized());
    }

    // ต้นตะเคียน: huge dark tree wrapped in three-coloured cloth with offerings at its foot.
    static void Takian(Node3D parent, string name, Vector3 p, float s)
    {
        var t = Shapes.Group(parent, name, p);
        var bark = new Color(0.2f, 0.16f, 0.13f);
        Shapes.Cylinder(t, new Vector3(0f, 3f * s, 0f), 0.55f * s, 0.9f * s, 6f * s, bark, 0f, "Trunk");
        foreach (var (y, c) in new[] { (1.2f, new Color(0.85f, 0.15f, 0.12f)), (1.45f, new Color(0.95f, 0.8f, 0.2f)), (1.7f, new Color(0.25f, 0.6f, 0.3f)) })
            Shapes.Cylinder(t, new Vector3(0f, y * s, 0f), 0.86f * s, 0.88f * s, 0.2f * s, c, 0f, "Cloth");
        foreach (var (pp, r) in new[] { (new Vector3(0f, 7.5f, 0f), 3.6f), (new Vector3(2.4f, 6.4f, 0.8f), 2.6f), (new Vector3(-2.2f, 6.6f, -1f), 2.8f), (new Vector3(0.5f, 6.2f, 2.3f), 2.4f) })
            Shapes.Sphere(t, pp * s, r * s, new Color(0.07f, 0.13f, 0.08f), 0f, "Crown");
        // Offerings: little dresses on a rack and candles
        Shapes.Box(t, new Vector3(1.4f * s, 0.5f, 0f), new Vector3(0.05f, 1f, 1f), DarkWood, 0f, "Rack");
        for (int i = 0; i < 3; i++)
            Shapes.Box(t, new Vector3(1.4f * s, 0.65f, -0.3f + i * 0.3f), new Vector3(0.02f, 0.4f, 0.22f), new Color(0.9f, 0.5f, 0.7f).Lerp(new Color(0.9f, 0.85f, 0.4f), i * 0.5f), 0f, "Dress");
        Shapes.Sphere(t, new Vector3(1.1f * s, 0.15f, 0.6f), 0.05f, new Color(1f, 0.7f, 0.3f), 4f, "GlowCandle");
        Shapes.Solid(t, new Vector3(0f, 3f, 0f), new Vector3(1.6f * s, 6f, 1.6f * s));
    }

    // ต้นไทร: wide canopy with hanging aerial roots.
    static void Banyan(Node3D parent, string name, Vector3 p, float s, bool sacred)
    {
        var t = Shapes.Group(parent, name, p);
        var bark = new Color(0.3f, 0.26f, 0.21f);
        Shapes.Cylinder(t, new Vector3(0f, 2.4f * s, 0f), 0.5f * s, 0.8f * s, 4.8f * s, bark, 0f, "Trunk");
        if (sacred)
            Shapes.Cylinder(t, new Vector3(0f, 1.2f * s, 0f), 0.82f * s, 0.82f * s, 0.3f, new Color(0.9f, 0.75f, 0.2f), 0f, "Cloth");
        foreach (var (pp, r) in new[] { (new Vector3(0f, 6f, 0f), 3.6f), (new Vector3(2.6f, 5.2f, 1f), 2.6f), (new Vector3(-2.4f, 5.4f, -1f), 2.6f), (new Vector3(0f, 5.2f, -2.6f), 2.4f) })
            Shapes.Sphere(t, pp * s, r * s, new Color(0.12f, 0.22f, 0.12f), 0f, "Crown");
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.Tau, r = _rng.RandfRange(1.6f, 3.2f) * s;
            float top = 4.6f * s;
            Shapes.Cylinder(t, new Vector3(Mathf.Cos(a) * r, top * 0.5f, Mathf.Sin(a) * r), 0.03f, 0.05f, top, bark.Darkened(0.2f), 0f, "AerialRoot");
        }
        Shapes.Solid(t, new Vector3(0f, 2f, 0f), new Vector3(1.4f * s, 4f, 1.4f * s));
    }

    static void Stump(Node3D parent, string name, Vector3 p)
    {
        var t = Shapes.Group(parent, name, p);
        Shapes.Cylinder(t, new Vector3(0f, 0.35f, 0f), 0.42f, 0.55f, 0.7f, new Color(0.26f, 0.2f, 0.15f), 0f, "Stump");
        Shapes.Cylinder(t, new Vector3(0f, 0.71f, 0f), 0.4f, 0.4f, 0.02f, new Color(0.45f, 0.36f, 0.25f), 0f, "Rings");
        Shapes.Sphere(t, new Vector3(0.3f, 0.2f, 0.3f), 0.15f, new Color(0.7f, 0.6f, 0.45f), 0f, "Fungus");
        Shapes.Solid(t, new Vector3(0f, 0.35f, 0f), new Vector3(1f, 0.7f, 1f));
    }

    static void Palm(Node3D parent, string name, Vector3 pos, float height)
    {
        var palm = Shapes.Group(parent, name, pos, _rng.Randf() * Mathf.Tau);
        float lean = _rng.RandfRange(0.05f, 0.18f);
        Vector3 top = Vector3.Zero;
        for (int s = 0; s < 3; s++)
        {
            float segH = height / 3f;
            var dir = new Vector3(0f, 1f, 0f).Rotated(Vector3.Right, lean * (s + 1));
            var mid = top + dir * segH * 0.5f;
            Shapes.Cylinder(palm, mid, 0.14f - s * 0.02f, 0.22f - s * 0.02f, segH + 0.05f, new Color(0.33f, 0.26f, 0.19f), 0f, "Trunk")
                .Rotation = new Vector3(lean * (s + 1), 0f, 0f);
            top += dir * segH;
        }
        for (int k = 0; k < 8; k++)
        {
            float a = k / 8f * Mathf.Tau;
            var frond = Shapes.Box(palm, top + new Vector3(Mathf.Cos(a) * 1.3f, -0.3f, Mathf.Sin(a) * 1.3f),
                new Vector3(2.8f, 0.05f, 0.5f), new Color(0.17f, 0.32f, 0.15f), 0f, "Frond");
            frond.Rotation = new Vector3(0f, -a, -0.42f);
        }
        Shapes.Sphere(palm, top + new Vector3(0f, -0.15f, 0f), 0.28f, new Color(0.3f, 0.25f, 0.12f), 0f, "Coconuts");
        Shapes.Solid(palm, new Vector3(0f, 2f, 0f), new Vector3(0.5f, 4f, 0.5f));
    }

    // Fireflies drifting over the fields; Main only lets them glow at night.
    static void BuildFireflies(Node3D world, float half)
    {
        var colorRamp = new Gradient();
        colorRamp.SetColor(0, new Color(0.8f, 1f, 0.45f, 0f));
        colorRamp.SetColor(1, new Color(0.8f, 1f, 0.45f, 0f));
        colorRamp.AddPoint(0.2f, new Color(0.85f, 1f, 0.5f, 1f));
        colorRamp.AddPoint(0.8f, new Color(0.7f, 1f, 0.4f, 1f));
        var dot = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(2.2f, 2.6f, 1.2f),
        };
        // Several emitters so only nearby ones need drawing.
        int k = 0;
        for (float x = -half + 60f; x < half; x += 120f)
        for (float z = -half + 60f; z < half; z += 120f)
        {
            var ff = Shapes.Child(world, new GpuParticles3D
            {
                Amount = 60,
                Lifetime = 7f,
                Preprocess = 7f,
                Position = new Vector3(x, 1.6f, z),
                VisibilityAabb = new Aabb(new Vector3(-65f, -3f, -65f), new Vector3(130f, 8f, 130f)),
                ProcessMaterial = new ParticleProcessMaterial
                {
                    EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                    EmissionBoxExtents = new Vector3(60f, 1.2f, 60f),
                    Gravity = Vector3.Zero,
                    Direction = new Vector3(0f, 0.3f, 0f),
                    Spread = 180f,
                    InitialVelocityMin = 0.15f,
                    InitialVelocityMax = 0.5f,
                    TurbulenceEnabled = true,
                    TurbulenceNoiseStrength = 1.5f,
                    TurbulenceNoiseScale = 6f,
                    ColorRamp = new GradientTexture1D { Gradient = colorRamp },
                },
                DrawPass1 = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 6, Rings = 3, Material = dot },
            }, $"Fireflies{k++}");
            ff.AddToGroup("fireflies", true);
        }
    }

    // ======================================================================
    //  Outskirts: ground around every zone and the outer boundary walls (the forest is added at runtime)
    // ======================================================================

    public static Rect2 WorldBounds()
    {
        var zones = GameData.Zones.Where(z => z.Scene != null).ToList();
        float minX = zones.Min(z => z.CenterX - z.SizeX * 0.5f), maxX = zones.Max(z => z.CenterX + z.SizeX * 0.5f);
        float minZ = zones.Min(z => z.CenterZ - z.SizeZ * 0.5f), maxZ = zones.Max(z => z.CenterZ + z.SizeZ * 0.5f);
        return new Rect2(minX, minZ, maxX - minX, maxZ - minZ);
    }

    public static Node3D BuildOutskirts()
    {
        var o = new Node3D { Name = "Outskirts" };
        var b = WorldBounds();
        float minX = b.Position.X, minZ = b.Position.Y, maxX = b.End.X, maxZ = b.End.Y;
        const float m = 260f;
        var g = Shapes.Group(o, "Ground");
        // Strips meet the zones exactly at their edges so nothing overlaps.
        void Strip(string n, float x0, float x1, float z0, float z1) =>
            Shapes.Add(g, new PlaneMesh { Size = new Vector2(x1 - x0, z1 - z0) }, new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f), GroundGreen, 0f, n);
        Strip("West", minX - m, minX, minZ - m, maxZ + m);
        Strip("East", maxX, maxX + m, minZ - m, maxZ + m);
        Strip("North", minX, maxX, minZ - m, minZ);
        Strip("South", minX, maxX, maxZ, maxZ + m);

        var walls = Shapes.Group(o, "Bounds");
        float w = maxX - minX, d = maxZ - minZ;
        Shapes.Solid(walls, new Vector3((minX + maxX) * 0.5f, 4f, minZ - 0.5f), new Vector3(w + 2f, 8f, 1f), "WallN");
        Shapes.Solid(walls, new Vector3((minX + maxX) * 0.5f, 4f, maxZ + 0.5f), new Vector3(w + 2f, 8f, 1f), "WallS");
        Shapes.Solid(walls, new Vector3(minX - 0.5f, 4f, (minZ + maxZ) * 0.5f), new Vector3(1f, 8f, d + 2f), "WallW");
        Shapes.Solid(walls, new Vector3(maxX + 0.5f, 4f, (minZ + maxZ) * 0.5f), new Vector3(1f, 8f, d + 2f), "WallE");
        return o;
    }

    // ======================================================================
    //  Runtime scatter: rice, grass, forest, visibility ranges
    // ======================================================================

    static MultiMeshInstance3D Scatter(string name, Mesh mesh, Material material, List<Transform3D> transforms, List<Color> colors = null, float visibleTo = 0f)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = colors != null,
            Mesh = mesh,
        };
        mm.InstanceCount = transforms.Count;
        for (int i = 0; i < transforms.Count; i++)
        {
            mm.SetInstanceTransform(i, transforms[i]);
            if (colors != null)
                mm.SetInstanceColor(i, colors[i]);
        }
        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mm,
            MaterialOverride = material,
            VisibilityRangeEnd = visibleTo,
            CastShadow = visibleTo > 0f && visibleTo < 100f ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
        };
    }

    // Rice stalks for every paddy (map_plot), in chunks so far fields are skipped.
    public static Node3D BuildRice(IEnumerable<Node3D> plots)
    {
        var root = new Node3D { Name = "Rice" };
        var rng = new RandomNumberGenerator { Seed = 99 };
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        var mesh = new BoxMesh { Size = new Vector3(0.18f, 0.6f, 0.18f) };
        foreach (var chunk in plots.GroupBy(p => (Mathf.FloorToInt(p.GlobalPosition.X / 52f), Mathf.FloorToInt(p.GlobalPosition.Z / 52f))))
        {
            var transforms = new List<Transform3D>();
            var colors = new List<Color>();
            foreach (var plot in chunk)
            {
                var center = plot.GlobalPosition;
                bool ripe = rng.Randf() < 0.4f;
                for (int i = 0; i < 140; i++)
                {
                    float s = rng.RandfRange(0.7f, 1.3f);
                    var basis = new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(1f, s, 1f));
                    transforms.Add(new Transform3D(basis, center + new Vector3(rng.RandfRange(-5f, 5f), 0.3f * s, rng.RandfRange(-5f, 5f))));
                    colors.Add(ripe
                        ? new Color(0.62f, 0.54f, 0.26f).Lightened(rng.RandfRange(0f, 0.08f))
                        : new Color(0.27f, 0.45f, 0.18f).Lightened(rng.RandfRange(0f, 0.1f)));
                }
            }
            root.AddChild(Scatter($"Rice_{chunk.Key.Item1}_{chunk.Key.Item2}", mesh, material, transforms, colors, 140f));
        }
        return root;
    }

    // A tuft of grass blades with vertex height in UV2.y for the wind shader.
    static ArrayMesh GrassTuft()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * Mathf.Pi + 0.3f;
            var side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.06f;
            var off = new Vector3(Mathf.Sin(a * 3f), 0f, Mathf.Cos(a * 2f)) * 0.12f;
            float h = 0.35f + (i % 3) * 0.12f;
            var lean = new Vector3(Mathf.Cos(a + 1.3f), 0f, Mathf.Sin(a + 1.3f)) * 0.08f;
            var b0 = off - side; var b1 = off + side; var tip = off + lean + Vector3.Up * h;
            foreach (var (v, y) in new[] { (b0, 0f), (b1, 0f), (tip, 1f) })
            {
                st.SetUV(new Vector2(0f, y));
                st.SetNormal(Vector3.Up);
                st.AddVertex(v);
            }
        }
        return st.Commit();
    }

    public static ShaderMaterial GrassMaterial { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode cull_disabled;
uniform vec3 player_pos;
uniform vec3 base_color : source_color = vec3(0.13, 0.2, 0.08);
uniform vec3 tip_color : source_color = vec3(0.36, 0.46, 0.2);
varying float h;
void vertex() {
    h = UV.y;
    vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float sway = sin(TIME * 1.7 + wp.x * 0.35 + wp.z * 0.27) * 0.1 + sin(TIME * 3.3 + wp.x * 1.3 + wp.z) * 0.035;
    vec3 offset = vec3(sway, 0.0, sway * 0.6) * h;
    // Blades lean away from the player.
    vec2 d = wp.xz - player_pos.xz;
    float dist = length(d);
    float push = (1.0 - smoothstep(0.2, 1.2, dist)) * h * 0.45;
    offset.xz += normalize(d + vec2(0.0001)) * push;
    offset.y -= push * 0.6;
    VERTEX += (inverse(MODEL_MATRIX) * vec4(offset, 0.0)).xyz;
}
void fragment() {
    ALBEDO = mix(base_color, tip_color, h) * COLOR.rgb;
    ROUGHNESS = 0.95;
    BACKLIGHT = vec3(0.15, 0.2, 0.08);
}",
        },
    };

    // Grass over open ground of a zone: off roads, paddies, houses, decks, walls and water.
    // density = tufts per square metre (by graphics quality).
    public static Node3D BuildGrass(ZoneDef zone, SceneTree tree, float density, Rect2 waterRect)
    {
        var root = new Node3D { Name = $"Grass_{zone.Id}" };
        if (density <= 0f)
            return root;
        float half = zone.SizeX * 0.5f;
        int n = (int)(zone.SizeX);
        var blocked = new bool[n, n];
        void Block(Vector3 center, Vector2 size, float yaw, float pad)
        {
            var axisX = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
            var axisZ = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            float r = (size.Length() * 0.5f) + pad;
            int x0 = Mathf.Max(0, (int)(center.X - zone.CenterX + half - r)), x1 = Mathf.Min(n - 1, (int)(center.X - zone.CenterX + half + r));
            int z0 = Mathf.Max(0, (int)(center.Z - zone.CenterZ + half - r)), z1 = Mathf.Min(n - 1, (int)(center.Z - zone.CenterZ + half + r));
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                var p = new Vector2(x + 0.5f - half + zone.CenterX - center.X, z + 0.5f - half + zone.CenterZ - center.Z);
                if (Mathf.Abs(p.Dot(axisX)) <= size.X * 0.5f + pad && Mathf.Abs(p.Dot(axisZ)) <= size.Y * 0.5f + pad)
                    blocked[x, z] = true;
            }
        }
        foreach (var group in new[] { "map_road", "map_plot", "map_house", "map_deck", "map_wall", "map_temple", "map_bus", "map_shrine", "map_bus_stop" })
            foreach (var node in tree.GetNodesInGroup(group).OfType<Node3D>())
                if (zone.Contains(node.GlobalPosition))
                    Block(node.GlobalPosition, (Vector2)node.GetMeta("map_size", Vector2.One), node.GlobalRotation.Y, 0.4f);

        var rng = new RandomNumberGenerator { Seed = (ulong)zone.Id.GetHashCode() };
        var mesh = GrassTuft();
        const int chunk = 32;
        for (int cx = 0; cx < n; cx += chunk)
        for (int cz = 0; cz < n; cz += chunk)
        {
            var transforms = new List<Transform3D>();
            var colors = new List<Color>();
            int count = (int)(chunk * chunk * density);
            for (int i = 0; i < count; i++)
            {
                float x = cx + rng.Randf() * chunk, z = cz + rng.Randf() * chunk;
                if (x >= n || z >= n || blocked[(int)x, (int)z])
                    continue;
                var wp = new Vector3(x - half + zone.CenterX, 0f, z - half + zone.CenterZ);
                if (waterRect.HasPoint(new Vector2(wp.X, wp.Z)))
                    continue;
                if (zone.Scene == "bueng_bua")
                {
                    wp.Y = BuengHeight(wp.X - zone.CenterX, wp.Z - zone.CenterZ);
                    if (wp.Y < 0.08f)
                        continue;
                }
                float s = rng.RandfRange(0.7f, 1.4f);
                transforms.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(Vector3.One * s), wp));
                colors.Add(new Color(1f, 1f, 1f).Lerp(new Color(0.85f, 0.8f, 0.6f), rng.Randf() * 0.5f));
            }
            if (transforms.Count > 0)
                root.AddChild(Scatter($"Grass_{cx}_{cz}", mesh, GrassMaterial, transforms, colors, 70f));
        }
        return root;
    }

    // Forest band around the playable area: healthy trees mixed with dead, crooked ones.
    public static Node3D BuildForest(Rect2 bounds)
    {
        var root = new Node3D { Name = "Forest" };
        var rng = new RandomNumberGenerator { Seed = 77 };
        var trunks = new List<Transform3D>();
        var crowns = new List<Transform3D>();
        var crownColors = new List<Color>();
        var dead = new List<Transform3D>();
        float pad = 3f, depth = 70f;
        int count = (int)((bounds.Size.X + bounds.Size.Y) * 2f * depth / 55f);
        for (int i = 0; i < count; i++)
        {
            float x = rng.RandfRange(bounds.Position.X - depth, bounds.End.X + depth);
            float z = rng.RandfRange(bounds.Position.Y - depth, bounds.End.Y + depth);
            if (x > bounds.Position.X - pad && x < bounds.End.X + pad && z > bounds.Position.Y - pad && z < bounds.End.Y + pad)
                continue;
            float s = rng.RandfRange(1.1f, 2.2f);
            var p = new Vector3(x, 0f, z);
            if (rng.Randf() < 0.3f)
            {
                // Dead tree: a leaning bare trunk plus two forked limbs.
                var lean = new Basis(new Vector3(rng.RandfRange(-1f, 1f), 0f, rng.RandfRange(-1f, 1f)).Normalized(), rng.RandfRange(0.05f, 0.25f));
                dead.Add(new Transform3D(lean.Scaled(new Vector3(s, s * 1.4f, s)), p + Vector3.Up * 2.2f * s));
                for (int k = 0; k < 2; k++)
                {
                    var limb = new Basis(new Vector3(rng.RandfRange(-1f, 1f), 0f, rng.RandfRange(-1f, 1f)).Normalized(), rng.RandfRange(0.6f, 1.1f));
                    dead.Add(new Transform3D(limb.Scaled(new Vector3(s * 0.4f, s * 0.8f, s * 0.4f)), p + Vector3.Up * (2.6f + k * 0.9f) * s));
                }
                continue;
            }
            trunks.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)), p + Vector3.Up * 1.6f * s));
            crowns.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(s, s * rng.RandfRange(0.8f, 1.2f), s)), p + Vector3.Up * 4.1f * s));
            crownColors.Add(new Color(0.1f, 0.19f, 0.11f).Lightened(rng.RandfRange(0f, 0.07f)));
        }
        var bark = new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.19f, 0.14f), Roughness = 0.9f };
        var deadBark = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.18f, 0.16f), Roughness = 0.95f };
        var leaves = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        root.AddChild(Scatter("Trunks", new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.38f, Height = 3.2f, RadialSegments = 7 }, bark, trunks));
        root.AddChild(Scatter("Crowns", new SphereMesh { Radius = 2.2f, Height = 4f, RadialSegments = 10, Rings = 6 }, leaves, crowns, crownColors));
        root.AddChild(Scatter("DeadTrees", new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.3f, Height = 3.2f, RadialSegments = 6 }, deadBark, dead));
        return root;
    }

    // Small props stop drawing at a distance (big buildings always draw) and lamps fade out,
    // which keeps the bigger zones fast. Applied after loading, so baked scenes stay untouched.
    public static void ApplyDistanceCulling(Node root)
    {
        foreach (var node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            if (mi.Mesh == null)
                continue;
            float size = mi.Mesh.GetAabb().Size.Length() * mi.GlobalBasis.Scale.Length() / 1.732f;
            mi.VisibilityRangeEnd = size < 0.5f ? 45f : size < 1.5f ? 85f : size < 5f ? 170f : 0f;
            if (size < 1.5f)
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        foreach (var node in root.FindChildren("*", "OmniLight3D", true, false))
        {
            var l = (OmniLight3D)node;
            l.DistanceFadeEnabled = true;
            l.DistanceFadeBegin = 70f;
            l.DistanceFadeLength = 20f;
        }
    }
}
