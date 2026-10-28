using Godot;
using System.Collections.Generic;

// Procedural surface textures generated once at startup and laid on world meshes with world-space
// triplanar mapping (no UVs needed). Each is a light detail map multiplying the mesh's own colour.
// The kind comes from the mesh's name where that is clear (roads, roofs, walls, trunks…), otherwise
// from its colour (brown → wood, brick red → brick, white → plaster, grey → stone, gold, green → leaves).
public static class Textures
{
    public enum Kind { Wood, Road, Grass, Tile, Thatch, Brick, Plaster, Slab, Gold, Bark, Leaves }

    static readonly Dictionary<Kind, ImageTexture> _tex = new();
    static readonly Dictionary<(Material, Kind), Material> _cache = new();

    static ImageTexture Get(Kind k)
    {
        if (_tex.TryGetValue(k, out var t))
            return t;
        const int n = 128;
        var img = Image.CreateEmpty(n, n, true, Image.Format.Rgb8);
        var noise = new FastNoiseLite { Seed = (int)k * 31 + 7, Frequency = 0.08f };
        var fine = new FastNoiseLite { Seed = (int)k * 17 + 3, Frequency = 0.3f };
        var rng = new RandomNumberGenerator { Seed = (ulong)k + 11 };
        var cells = new Vector2[8, 8];
        for (int i = 0; i < 8; i++)
        for (int j = 0; j < 8; j++)
            cells[i, j] = new Vector2(i + rng.RandfRange(0.2f, 0.8f), j + rng.RandfRange(0.2f, 0.8f)) * (n / 8f);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float nz = noise.GetNoise2D(x, y), fz = fine.GetNoise2D(x, y);
            float v;
            switch (k)
            {
                case Kind.Wood:
                {
                    // Four planks per tile with dark seams, long grain and staggered butt joints.
                    int plank = y / 32;
                    float grain = Mathf.Sin(x * 0.15f + plank * 7f + nz * 4f + y * 0.6f) * 0.06f;
                    float seam = y % 32 < 2 ? -0.35f : 0f;
                    float end = (x + plank * 41) % 128 < 2 ? -0.25f : 0f;
                    v = 0.86f + grain + nz * 0.08f + seam + end;
                    break;
                }
                case Kind.Road:
                {
                    // Cobbles: the gap between nearest and second-nearest cell centre is the mortar.
                    float d1 = float.MaxValue, d2 = float.MaxValue;
                    var p = new Vector2(x, y);
                    for (int i = 0; i < 8; i++)
                    for (int j = 0; j < 8; j++)
                    {
                        var dv = p - cells[i, j];
                        dv.X = Mathf.PosMod(dv.X + n * 0.5f, n) - n * 0.5f;
                        dv.Y = Mathf.PosMod(dv.Y + n * 0.5f, n) - n * 0.5f;
                        float d = dv.Length();
                        if (d < d1) { d2 = d1; d1 = d; }
                        else if (d < d2) d2 = d;
                    }
                    v = 0.68f + 0.26f * Mathf.SmoothStep(0f, 3f, d2 - d1) + nz * 0.08f;
                    break;
                }
                case Kind.Grass:
                    v = 0.86f + nz * 0.12f + fz * 0.06f;
                    break;
                case Kind.Tile:
                {
                    int row = y / 16;
                    float lip = (y % 16) / 16f;
                    float joint = (x + row % 2 * 8) % 16 < 1 ? -0.2f : 0f;
                    v = 0.72f + lip * 0.25f + joint + nz * 0.05f;
                    break;
                }
                case Kind.Thatch:
                {
                    float streak = Mathf.Sin(x * 1.3f + nz * 6f) * 0.08f + Mathf.Sin(x * 0.37f) * 0.05f;
                    float band = y % 24 < 2 ? -0.22f : 0f;
                    v = 0.82f + streak + nz * 0.07f + band;
                    break;
                }
                case Kind.Brick:
                {
                    // Running bond: 16 px courses, 32 px bricks offset every other course.
                    int course = y / 16;
                    bool mortar = y % 16 < 2 || (x + course % 2 * 16) % 32 < 2;
                    float tone = Mathf.PosMod((x + course % 2 * 16) / 32 * 7 + course * 3, 5) * 0.03f;
                    v = mortar ? 1.05f : 0.78f + tone + nz * 0.07f + fz * 0.04f;
                    break;
                }
                case Kind.Plaster:
                {
                    // Lime plaster: soft stains, a few hairline cracks.
                    float stain = Mathf.Max(0f, noise.GetNoise2D(x * 0.4f, y * 0.4f)) * 0.18f;
                    float crack = Mathf.Abs(fine.GetNoise2D(x * 0.5f, y * 0.5f)) < 0.015f ? 0.15f : 0f;
                    v = 0.95f - stain - crack + fz * 0.03f;
                    break;
                }
                case Kind.Slab:
                {
                    // Large paving slabs with thin joints.
                    bool joint = x % 64 < 2 || (y + (x / 64) * 32) % 64 < 2;
                    v = joint ? 0.6f : 0.84f + nz * 0.08f + fz * 0.04f;
                    break;
                }
                case Kind.Gold:
                {
                    // Gilded bands with a soft sheen.
                    v = 0.85f + Mathf.Sin(y * 0.8f) * 0.08f + (y % 16 < 2 ? -0.15f : 0f) + nz * 0.05f;
                    break;
                }
                case Kind.Bark:
                {
                    // Vertical furrows broken by rough plates.
                    float furrow = Mathf.Abs(Mathf.Sin(x * 0.45f + nz * 3f));
                    v = 0.62f + furrow * 0.3f + fz * 0.1f;
                    break;
                }
                default: // leaves: overlapping leaf blotches, light and dark
                {
                    float leaf = noise.GetNoise2D(x * 1.6f, y * 1.6f);
                    v = 0.78f + leaf * 0.25f + fz * 0.1f;
                    break;
                }
            }
            v = Mathf.Clamp(v, 0f, 1.2f);
            img.SetPixel(x, y, new Color(v, v, v));
        }
        img.GenerateMipmaps();
        t = ImageTexture.CreateFromImage(img);
        _tex[k] = t;
        return t;
    }

    static float Scale(Kind k) => k switch
    {
        Kind.Wood => 0.55f,
        Kind.Road => 0.4f,
        Kind.Grass => 0.22f,
        Kind.Tile => 0.6f,
        Kind.Thatch => 0.7f,
        Kind.Brick => 0.9f,
        Kind.Plaster => 0.25f,
        Kind.Slab => 0.35f,
        Kind.Gold => 1.2f,
        Kind.Bark => 1.4f,
        _ => 0.6f,
    };

    // Godot suffixes duplicate sibling names ("Strip2", "@MeshInstance3D@41"); match on the base name.
    static string BaseName(string name)
    {
        int end = name.Length;
        while (end > 0 && char.IsDigit(name[end - 1]))
            end--;
        return name[..end];
    }

    static bool Has(string name, params string[] parts)
    {
        foreach (var p in parts)
            if (name.Contains(p))
                return true;
        return false;
    }

    static Kind? KindOf(string name, Color c)
    {
        if (name.StartsWith("Glow") || name.StartsWith("@") || Has(name, "Seam", "Window", "Eye", "Ink", "Yan", "Talisman",
                "Cloth", "Dress", "Lotus", "Water", "Candle", "Garland", "Sash", "Mouth", "Flame", "Rope", "String"))
            return null;
        if (name is "Terrain" or "Grass" || name.StartsWith("GroundStrip"))
            return Kind.Grass;
        if (name.StartsWith("Roof") || name is "Awning" or "PorchRoof" or "FallenRoof")
            return c.R > c.G * 1.5f ? Kind.Tile : Kind.Thatch; // red clay vs straw
        if (name.StartsWith("Street") || name is "Strip" or "Bend" or "Plaza" or "Yard" or "PathNorth")
            return Kind.Road;
        if (name is "CourtFloor" or "Floor" && c.S < 0.2f || name is "Kerb" or "Plinth" or "Step" or "PlinthCollider")
            return Kind.Slab;
        if (Has(name, "Trunk", "Branch", "Root", "Stump", "Pole") && !name.Contains("Pestle"))
            return Kind.Bark;
        if (Has(name, "Crown", "Leaf", "Frond", "Herbs"))
            return Kind.Leaves;
        if (Has(name, "Mud", "Dike"))
            return Kind.Grass;
        if (Has(name, "Brick", "Merlon", "Tower", "Gate", "Forge", "Chimney", "Wall") && (c.H < 0.055f || c.H > 0.95f) && c.S > 0.3f)
            return Kind.Brick;
        if (name is "Spire" or "Chofa" or "Ring" && c.H is > 0.08f and < 0.18f && c.S > 0.45f)
            return Kind.Gold;

        // Everything else by colour.
        if (c.S < 0.16f)
            return c.V > 0.62f ? Kind.Plaster : c.V > 0.22f ? Kind.Slab : null;
        if (c.H is > 0.08f and < 0.18f && c.S > 0.5f && c.V > 0.6f)
            return Kind.Gold;
        if (c.H is > 0.2f and < 0.45f && c.S > 0.2f)
            return Kind.Leaves;
        if ((c.H < 0.05f || c.H > 0.95f) && c.S > 0.35f && c.V > 0.3f)
            return Kind.Brick;
        if (c.H is >= 0.03f and < 0.14f && c.S > 0.2f && c.V < 0.65f)
            return Kind.Wood;
        return null;
    }

    static Material Textured(StandardMaterial3D mat, Kind k)
    {
        if (_cache.TryGetValue((mat, k), out var textured))
            return textured;
        var m = (StandardMaterial3D)mat.Duplicate();
        m.AlbedoTexture = Get(k);
        m.Uv1Triplanar = true;
        m.Uv1WorldTriplanar = true;
        m.Uv1Scale = Vector3.One * Scale(k);
        m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        _cache[(mat, k)] = m;
        return m;
    }

    // Lays textures over every matching mesh under root (meshes and scattered MultiMeshes).
    public static void Apply(Node root)
    {
        foreach (var node in root.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (node is not (MeshInstance3D or MultiMeshInstance3D))
                continue;
            var gi = (GeometryInstance3D)node;
            if (gi.MaterialOverride is not StandardMaterial3D mat || mat.AlbedoTexture != null
                || mat.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded || mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled)
                continue;
            string name = BaseName(gi.Name.ToString());
            Kind? kind = gi is MultiMeshInstance3D
                ? name switch { "Trunks" or "DeadTrees" or "Stems" => Kind.Bark, "Crowns" or "Fronds" => Kind.Leaves, _ => null }
                : KindOf(name, mat.AlbedoColor);
            if (kind is Kind k)
                gi.MaterialOverride = Textured(mat, k);
        }
    }
}
