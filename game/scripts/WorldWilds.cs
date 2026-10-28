using Godot;
using System.Collections.Generic;
using System.Linq;

// Runtime undergrowth and woodland for every zone: tall ยาง, round fruit trees, spreading rain trees,
// sugar palms, banana clumps, bamboo, bushes and ferns, scattered in clusters so the land feels
// overgrown. Kept off roads, houses, temples, paddies, piers, water and where villagers stand.
// Drawn with MultiMeshes (one per part) and given trunk colliders; density follows the graphics preset.
public static partial class WorldBuilder
{
    enum Wild { Tall, Round, Rain, Palm, Banana, Bamboo, Bush, Fern }

    static float WildClearance(Wild w) => w switch
    {
        Wild.Tall => 3.6f,
        Wild.Round => 3f,
        Wild.Rain => 4.6f,
        Wild.Palm => 2.4f,
        Wild.Banana => 2f,
        Wild.Bamboo => 2.2f,
        Wild.Bush => 1.3f,
        _ => 0.9f,
    };

    public static Node3D BuildWilds(ZoneDef zone, SceneTree tree, float density, Rect2 waterRect, IEnumerable<Vector3> keepClear)
    {
        var root = new Node3D { Name = $"Wilds_{zone.Id}" };
        if (density <= 0f)
            return root;
        float half = zone.SizeX * 0.5f;
        int n = (int)zone.SizeX;
        var blocked = new bool[n, n];
        void Block(Vector3 center, Vector2 size, float yaw, float pad)
        {
            var axisX = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
            var axisZ = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            float r = size.Length() * 0.5f + pad;
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
        var pads = new Dictionary<string, float>
        {
            ["map_road"] = 1.2f, ["map_house"] = 1.5f, ["map_temple"] = 7f, ["map_plot"] = 0.6f, ["map_deck"] = 1.5f,
            ["map_wall"] = 3f, ["map_shrine"] = 3f, ["map_bus"] = 2f, ["map_bus_stop"] = 2f, ["map_sign"] = 1.5f,
            ["map_boat"] = 1f, ["map_graveyard"] = -6f,
        };
        foreach (var (group, pad) in pads)
            foreach (var node in tree.GetNodesInGroup(group).OfType<Node3D>())
                if (zone.Contains(node.GlobalPosition))
                    Block(node.GlobalPosition, (Vector2)node.GetMeta("map_size", Vector2.One), node.GlobalRotation.Y, pad);
        foreach (var p in keepClear)
            if (zone.Contains(p))
                Block(p, new Vector2(2f, 2f), 0f, 1.5f);

        bool Free(Vector3 wp, float r)
        {
            for (int i = 0; i < 9; i++)
            {
                var q = i == 0 ? wp : wp + new Vector3(Mathf.Cos(i * Mathf.Tau / 8f), 0f, Mathf.Sin(i * Mathf.Tau / 8f)) * r;
                int x = (int)(q.X - zone.CenterX + half), z = (int)(q.Z - zone.CenterZ + half);
                if (x < 2 || z < 2 || x >= n - 2 || z >= n - 2 || blocked[x, z] || waterRect.HasPoint(new Vector2(q.X, q.Z)))
                    return false;
                if (zone.Scene == "bueng_bua" && BuengHeight(q.X - zone.CenterX, q.Z - zone.CenterZ) < 0.12f)
                    return false;
            }
            return true;
        }

        var rng = new RandomNumberGenerator { Seed = (ulong)zone.Id.GetHashCode() + 5 };
        var clusters = new FastNoiseLite { Seed = zone.Id.GetHashCode(), Frequency = 0.02f };
        // City streets stay tidier than the countryside.
        float zoneMult = zone.AlleyOnly ? 0.45f : 1f;
        var trunks = new List<Transform3D>(); var trunkColors = new List<Color>();
        var crowns = new List<Transform3D>(); var crownColors = new List<Color>();
        var fronds = new List<Transform3D>(); var frondColors = new List<Color>();
        var stems = new List<Transform3D>(); var stemColors = new List<Color>();
        var colliders = new List<(Vector3 p, float r)>();
        Color Leaf(float dark) => new Color(0.12f, 0.24f, 0.12f).Lightened(rng.RandfRange(0f, 0.1f)).Darkened(dark);
        Color Bark() => new Color(0.27f, 0.2f, 0.14f).Lightened(rng.RandfRange(-0.05f, 0.08f));
        void Trunk(Vector3 p, float r, float h, float lean = 0f)
        {
            var basis = new Basis(new Vector3(rng.RandfRange(-1f, 1f), 0f, rng.RandfRange(-1f, 1f)).Normalized(), lean).Scaled(new Vector3(r, h, r));
            trunks.Add(new Transform3D(basis, p + Vector3.Up * h * 0.5f));
            trunkColors.Add(Bark());
        }
        void Crown(Vector3 c, Vector3 s, float dark = 0f)
        {
            crowns.Add(new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(s), c));
            crownColors.Add(Leaf(dark));
        }
        void Blade(Vector3 origin, float yaw, float pitch, Vector3 size, Color color)
        {
            var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch);
            var center = origin + basis * new Vector3(0f, 0f, -size.Z * 0.5f);
            fronds.Add(new Transform3D(basis.Scaled(size), center));
            frondColors.Add(color);
        }

        const float cell = 4.2f;
        for (float x = -half + 3f; x < half - 3f; x += cell)
        for (float z = -half + 3f; z < half - 3f; z += cell)
        {
            var wp = zone.Center + new Vector3(x + rng.RandfRange(-1.8f, 1.8f), 0f, z + rng.RandfRange(-1.8f, 1.8f));
            float cluster = 0.5f + 0.5f * clusters.GetNoise2D(wp.X, wp.Z);
            if (rng.Randf() > density * zoneMult * (0.15f + 0.85f * cluster * cluster))
                continue;
            float roll = rng.Randf();
            var kind = roll < 0.11f ? Wild.Tall : roll < 0.27f ? Wild.Round : roll < 0.33f ? Wild.Rain : roll < 0.41f ? Wild.Palm
                : roll < 0.5f ? Wild.Banana : roll < 0.58f ? Wild.Bamboo : roll < 0.82f ? Wild.Bush : Wild.Fern;
            if (zone.Scene == "bueng_bua" && kind is Wild.Palm or Wild.Rain)
                kind = Wild.Bush;
            if (!Free(wp, WildClearance(kind)))
            {
                kind = Wild.Fern;
                if (!Free(wp, WildClearance(kind)))
                    continue;
            }
            float ground = zone.Scene == "bueng_bua" ? Mathf.Max(0f, BuengHeight(wp.X - zone.CenterX, wp.Z - zone.CenterZ)) : 0f;
            var p = wp with { Y = ground };
            float s = rng.RandfRange(0.8f, 1.25f);
            switch (kind)
            {
                case Wild.Tall: // ยาง: a tall bare trunk with a high, flattened crown
                    Trunk(p, 0.32f * s, 9f * s);
                    Crown(p + new Vector3(0f, 9.4f * s, 0f), new Vector3(2.8f, 1.6f, 2.8f) * s, 0.05f);
                    Crown(p + new Vector3(1.4f, 8.6f * s, 0.6f), new Vector3(1.7f, 1.1f, 1.7f) * s);
                    colliders.Add((p, 0.4f * s));
                    break;
                case Wild.Round: // มะม่วง: short trunk, big round crown
                    Trunk(p, 0.26f * s, 3.2f * s);
                    Crown(p + new Vector3(0f, 4.3f * s, 0f), new Vector3(2.6f, 2.1f, 2.6f) * s);
                    Crown(p + new Vector3(-1.2f, 3.6f * s, 0.8f), new Vector3(1.5f, 1.3f, 1.5f) * s, 0.04f);
                    colliders.Add((p, 0.35f * s));
                    break;
                case Wild.Rain: // จามจุรี: wide umbrella canopy
                    Trunk(p, 0.4f * s, 4.4f * s, 0.08f);
                    Crown(p + new Vector3(0f, 5.4f * s, 0f), new Vector3(4.6f, 1.5f, 4.6f) * s, 0.03f);
                    colliders.Add((p, 0.5f * s));
                    break;
                case Wild.Palm: // ตาล: slim trunk and a burst of fronds
                {
                    float h = rng.RandfRange(6.5f, 9.5f);
                    Trunk(p, 0.2f, h, rng.RandfRange(0f, 0.08f));
                    var top = p + Vector3.Up * h;
                    Crown(top, new Vector3(0.55f, 0.55f, 0.55f), 0.1f);
                    for (int i = 0; i < 9; i++)
                        Blade(top, i / 9f * Mathf.Tau + rng.Randf() * 0.3f, rng.RandfRange(-0.5f, 0.2f), new Vector3(0.7f, 0.06f, 2.6f), Leaf(0f).Lightened(0.05f));
                    colliders.Add((p, 0.3f));
                    break;
                }
                case Wild.Banana: // กล้วย: a few stems with long drooping leaves
                    for (int k = 0; k < 3; k++)
                    {
                        var bp = p + new Vector3(rng.RandfRange(-0.7f, 0.7f), 0f, rng.RandfRange(-0.7f, 0.7f));
                        float h = rng.RandfRange(1.8f, 2.8f);
                        stems.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(0.14f, h, 0.14f)), bp + Vector3.Up * h * 0.5f));
                        stemColors.Add(new Color(0.42f, 0.5f, 0.28f));
                        for (int i = 0; i < 5; i++)
                            Blade(bp + Vector3.Up * h, rng.Randf() * Mathf.Tau, rng.RandfRange(-0.9f, -0.2f), new Vector3(0.55f, 0.04f, 1.9f), new Color(0.24f, 0.45f, 0.18f).Lightened(rng.RandfRange(-0.05f, 0.1f)));
                    }
                    break;
                case Wild.Bamboo: // ไผ่: a clump of leaning canes, leafy at the top
                    for (int k = 0; k < 8; k++)
                    {
                        var bp = p + new Vector3(rng.RandfRange(-0.8f, 0.8f), 0f, rng.RandfRange(-0.8f, 0.8f));
                        float h = rng.RandfRange(5f, 9f);
                        var lean = new Basis(new Vector3(rng.RandfRange(-1f, 1f), 0f, rng.RandfRange(-1f, 1f)).Normalized(), rng.RandfRange(0.05f, 0.25f));
                        stems.Add(new Transform3D(lean.Scaled(new Vector3(0.07f, h, 0.07f)), bp + lean * (Vector3.Up * h * 0.5f)));
                        stemColors.Add(new Color(0.45f, 0.55f, 0.25f).Lightened(rng.RandfRange(-0.05f, 0.05f)));
                        Crown(bp + lean * (Vector3.Up * h), new Vector3(0.9f, 1.3f, 0.9f), -0.05f);
                    }
                    colliders.Add((p, 0.9f));
                    break;
                case Wild.Bush:
                    for (int k = 0; k < 3; k++)
                    {
                        float r = rng.RandfRange(0.5f, 1.1f) * s;
                        Crown(p + new Vector3(rng.RandfRange(-0.6f, 0.6f), r * 0.6f, rng.RandfRange(-0.6f, 0.6f)), new Vector3(r, r * 0.8f, r), 0.08f);
                    }
                    break;
                default: // fern / low shrub
                    Crown(p + Vector3.Up * 0.2f, new Vector3(0.8f, 0.35f, 0.8f) * s, -0.04f);
                    break;
            }
        }

        var bark = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        var leaves = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.85f };
        var blades = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        if (trunks.Count > 0)
            root.AddChild(Scatter("Trunks", new CylinderMesh { TopRadius = 0.7f, BottomRadius = 1f, Height = 1f, RadialSegments = 7 }, bark, trunks, trunkColors, 260f));
        if (crowns.Count > 0)
            root.AddChild(Scatter("Crowns", new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 9, Rings = 5 }, leaves, crowns, crownColors, 260f));
        if (fronds.Count > 0)
            root.AddChild(Scatter("Fronds", new BoxMesh { Size = Vector3.One }, blades, fronds, frondColors, 200f));
        if (stems.Count > 0)
            root.AddChild(Scatter("Stems", new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 6 }, bark, stems, stemColors, 200f));
        if (colliders.Count > 0)
        {
            var body = new StaticBody3D { Name = "WildColliders", CollisionLayer = Layers.World, CollisionMask = 0 };
            foreach (var (p, r) in colliders)
                body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = r, Height = 3f }, Position = p + Vector3.Up * 1.5f });
            root.AddChild(body);
        }
        return root;
    }
}
