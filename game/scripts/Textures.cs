using Godot;
using System.Collections.Generic;

// Procedural surface textures (wood planks, stone/cobble roads, grass, roof tiles and thatch),
// generated once at startup and laid on world meshes with world-space triplanar mapping, so no UVs
// are needed. The texture is a light detail map that multiplies the mesh's own colour.
public static class Textures
{
    enum Kind { Wood, Road, Grass, Tile, Thatch }

    static readonly Dictionary<Kind, ImageTexture> _tex = new();
    static readonly Dictionary<(Material, Kind), Material> _cache = new();

    static ImageTexture Get(Kind k)
    {
        if (_tex.TryGetValue(k, out var t))
            return t;
        const int n = 128;
        var img = Image.CreateEmpty(n, n, true, Image.Format.Rgb8);
        var noise = new FastNoiseLite { Seed = (int)k * 31 + 7, Frequency = 0.08f };
        var rng = new RandomNumberGenerator { Seed = (ulong)k + 11 };
        // Jittered cell centres for cobbles.
        var cells = new Vector2[8, 8];
        for (int i = 0; i < 8; i++)
        for (int j = 0; j < 8; j++)
            cells[i, j] = new Vector2(i + rng.RandfRange(0.2f, 0.8f), j + rng.RandfRange(0.2f, 0.8f)) * (n / 8f);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float nz = noise.GetNoise2D(x, y);
            float v;
            switch (k)
            {
                case Kind.Wood:
                {
                    // Four planks per tile with dark seams, long grain and a few knots.
                    int plank = y / 32;
                    float grain = Mathf.Sin((x * 0.15f + plank * 7f) + nz * 4f + y * 0.6f) * 0.06f;
                    float seam = y % 32 < 2 ? -0.35f : 0f;
                    float end = (x + plank * 41) % 128 < 2 ? -0.25f : 0f;
                    v = 0.86f + grain + nz * 0.08f + seam + end;
                    break;
                }
                case Kind.Road:
                {
                    // Cobbles: distance to the nearest and second-nearest cell centre gives the mortar lines.
                    var p = new Vector2(x, y);
                    float d1 = float.MaxValue, d2 = float.MaxValue;
                    for (int i = 0; i < 8; i++)
                    for (int j = 0; j < 8; j++)
                    {
                        var c = cells[i, j];
                        var dv = p - c;
                        dv.X = Mathf.PosMod(dv.X + n * 0.5f, n) - n * 0.5f;
                        dv.Y = Mathf.PosMod(dv.Y + n * 0.5f, n) - n * 0.5f;
                        float d = dv.Length();
                        if (d < d1) { d2 = d1; d1 = d; }
                        else if (d < d2) d2 = d;
                    }
                    float mortar = Mathf.SmoothStep(0f, 3f, d2 - d1);
                    v = 0.68f + 0.26f * mortar + nz * 0.08f;
                    break;
                }
                case Kind.Grass:
                    v = 0.86f + nz * 0.12f + (rng.Randf() < 0.08f ? 0.1f : 0f) - (rng.Randf() < 0.05f ? 0.12f : 0f);
                    break;
                case Kind.Tile:
                {
                    // Overlapping roof tiles: rows with a shaded lower lip, staggered every other row.
                    int row = y / 16;
                    float lip = (y % 16) / 16f;
                    float joint = (x + row % 2 * 8) % 16 < 1 ? -0.2f : 0f;
                    v = 0.72f + lip * 0.25f + joint + nz * 0.05f;
                    break;
                }
                default: // thatch: dense vertical straw streaks in layered bands
                {
                    float streak = Mathf.Sin(x * 1.3f + nz * 6f) * 0.08f + Mathf.Sin(x * 0.37f) * 0.05f;
                    float band = y % 24 < 2 ? -0.22f : 0f;
                    v = 0.82f + streak + nz * 0.07f + band;
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
        _ => 0.7f,
    };

    static Kind? KindOf(string name)
    {
        if (name.StartsWith("Glow") || name.Contains("Seam") || name.Contains("Window") || name.Contains("Door"))
            return null;
        if (name is "Terrain" or "Grass" || name.StartsWith("GroundStrip"))
            return Kind.Grass;
        if (name.StartsWith("Roof") || name is "Awning" or "PorchRoof" or "FallenRoof")
            return null; // decided by colour below (tile or thatch)
        if (name is "Walls" or "Floor" or "Deck" or "Planks" or "BambooWalls" or "FlaredWalls" or "Body" or "BackWall"
            or "SideWall" or "Tread" or "Balcony" or "Hull" or "Workbench" or "Bench" or "SignBoard" or "Board")
            return Kind.Wood;
        if (name.StartsWith("Street") || name is "Strip" or "Bend" or "Plaza" or "Yard" or "PathNorth" or "Kerb"
            or "Plinth" or "Step1" or "Step2" or "Step3" or "Base" or "CourtN" or "CourtE" or "CourtW" or "CourtSW" or "CourtSE")
            return Kind.Road;
        return null;
    }

    // Lays textures over every matching mesh under root. Safe to call once per loaded zone.
    public static void Apply(Node root)
    {
        foreach (var node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            if (mi.MaterialOverride is not StandardMaterial3D mat || mat.AlbedoTexture != null)
                continue;
            string name = mi.Name.ToString();
            var kind = KindOf(name);
            if (kind == null && (name.StartsWith("Roof") || name is "Awning" or "PorchRoof" or "FallenRoof"))
            {
                var c = mat.AlbedoColor;
                kind = c.R > c.G * 1.5f ? Kind.Tile : Kind.Thatch; // red clay vs straw
            }
            if (kind is not Kind k)
                continue;
            if (!_cache.TryGetValue((mat, k), out var textured))
            {
                var m = (StandardMaterial3D)mat.Duplicate();
                m.AlbedoTexture = Get(k);
                m.Uv1Triplanar = true;
                m.Uv1WorldTriplanar = true;
                m.Uv1Scale = Vector3.One * Scale(k);
                m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                _cache[(mat, k)] = textured = m;
            }
            mi.MaterialOverride = textured;
        }
    }
}
