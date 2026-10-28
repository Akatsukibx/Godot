using Godot;
using System.Linq;

// Draws the map window: the current zone from the world scene's map_* tagged nodes (so it follows
// edits made in the editor), or the world map of all zones from data/zones.json.
public partial class MapView : Control
{
    public bool WorldMode { get; set; }
    public string CurrentZone { get; set; } = "wiang_mok";

    static readonly Color Grass = new(0.16f, 0.2f, 0.13f);
    static readonly Color Water = new(0.08f, 0.17f, 0.25f);
    static readonly Color Plot = new(0.26f, 0.36f, 0.18f);
    static readonly Color Road = new(0.45f, 0.38f, 0.27f);
    static readonly Color Deck = new(0.5f, 0.36f, 0.22f);
    static readonly Color House = new(0.12f, 0.09f, 0.07f);

    Font Font => Game.ThaiFont;

    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
            QueueRedraw();
    }

    // Admin mode: click the zone map to warp there.
    public override void _GuiInput(InputEvent e)
    {
        if (WorldMode || !Settings.AdminMode || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            return;
        var local = (mb.Position - Origin) / MapScale;
        var world = new Vector3(local.X - Half + Zone.CenterX, 0f, local.Y - Half + Zone.CenterZ);
        Game.Main.Hud.CloseWindows();
        Game.Main.AdminWarp(world);
        AcceptEvent();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.05f, 0.06f));
        if (WorldMode)
            DrawWorld();
        else
            DrawZone();
    }

    // ---------- zone ----------

    ZoneDef Zone => GameData.Zone(CurrentZone) ?? GameData.Zones.First(z => z.Scene != null);
    float Half => Mathf.Max(Zone.SizeX, Zone.SizeZ) * 0.5f + 2f;
    float MapScale => Mathf.Min(Size.X, Size.Y) / (Half * 2f);
    Vector2 Origin => (Size - new Vector2(Half * 2f, Half * 2f) * MapScale) * 0.5f;

    Vector2 ToMap(Vector3 world) => Origin + new Vector2(world.X - Zone.CenterX + Half, world.Z - Zone.CenterZ + Half) * MapScale;
    Vector3 Local(float x, float z) => Zone.Center + new Vector3(x, 0f, z);

    void DrawTagged(string group, Color color, bool outline = false)
    {
        foreach (var node in GetTree().GetNodesInGroup(group).OfType<Node3D>())
        {
            if (!Zone.Contains(node.GlobalPosition))
                continue;
            var size = (Vector2)node.GetMeta("map_size", Vector2.One) * MapScale;
            DrawSetTransform(ToMap(node.GlobalPosition), -node.GlobalRotation.Y);
            DrawRect(new Rect2(-size * 0.5f, size), color);
            if (outline)
                DrawRect(new Rect2(-size * 0.5f, size), new Color(1f, 0.85f, 0.6f, 0.5f), false, 1.5f);
            DrawSetTransform(Vector2.Zero);
        }
    }

    void Text(Vector2 at, string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        float w = 400f;
        var pos = align == HorizontalAlignment.Center ? at - new Vector2(w * 0.5f, 0f) : at;
        DrawStringOutline(Font, pos, text, align, w, size, 4, new Color(0f, 0f, 0f, 0.8f));
        DrawString(Font, pos, text, align, w, size, color);
    }

    void DrawZone()
    {
        var zone = Zone;
        var area = new Rect2(ToMap(Local(-zone.SizeX * 0.5f, -zone.SizeZ * 0.5f)), new Vector2(zone.SizeX, zone.SizeZ) * MapScale);
        DrawRect(area, Grass);
        DrawTagged("map_water", Water);
        DrawTagged("map_grove", new Color(0.08f, 0.12f, 0.08f));
        DrawTagged("map_park", new Color(0.2f, 0.32f, 0.18f));
        DrawTagged("map_graveyard", new Color(0.22f, 0.2f, 0.24f));
        DrawTagged("map_bus_stop", new Color(0.85f, 0.2f, 0.15f));
        DrawTagged("map_plot", Plot);
        DrawTagged("map_road", Road);
        DrawTagged("map_temple", Road.Lightened(0.15f));
        DrawTagged("map_deck", Deck);
        DrawTagged("map_boat", Deck.Darkened(0.3f));
        DrawTagged("map_bus", new Color(0.75f, 0.15f, 0.12f));
        DrawTagged("map_wall", new Color(0.45f, 0.25f, 0.18f));
        DrawTagged("map_house", House, true);
        DrawTagged("map_alley_dark", new Color(0.45f, 0.3f, 0.65f, 0.8f));

        // Shrines (the bound one is brighter) and NPCs with quest markers.
        foreach (var shrine in GetTree().GetNodesInGroup("map_shrine").OfType<Node3D>())
        {
            if (!zone.Contains(shrine.GlobalPosition))
                continue;
            bool bound = Game.Player?.SpawnShrine == (string)shrine.GetMeta("shrine_id", "");
            var sp = ToMap(shrine.GlobalPosition);
            DrawCircle(sp, bound ? 7f : 5f, bound ? new Color(1f, 0.85f, 0.35f) : new Color(0.85f, 0.65f, 0.3f));
            Text(sp + new Vector2(0f, -10f), (string)shrine.GetMeta("shrine_name", "ศาล") + (bound ? " (จุดเกิด)" : ""), 13, UiTheme.Gold);
        }
        foreach (var npc in Game.Main.Npcs)
        {
            if (!zone.Contains(npc.GlobalPosition))
                continue;
            var np = ToMap(npc.GlobalPosition);
            DrawCircle(np, 4.5f, new Color(0.5f, 1f, 0.6f));
            Text(np + new Vector2(0f, 18f), npc.Def.Name, 13, new Color(0.75f, 1f, 0.8f));
        }
        if (zone.Id == "thung_na")
        {
            Text(ToMap(Local(-45f, WorldBuilder.ThungNaShoreZ - 22f)), "หมู่บ้านกลางน้ำ", 15, new Color(0.7f, 0.85f, 1f));
            Text(ToMap(Local(-70f, 100f)), "ทุ่งนาหุ่นไล่กา", 15, new Color(0.85f, 0.95f, 0.7f));
        }
        else if (zone.AlleyOnly)
            Text(ToMap(Local(-40f, 70f)), "ซอยมืด (มีผี)", 15, new Color(0.8f, 0.7f, 1f));

        // Exits to other zones along the edges.
        foreach (var exit in zone.Exits)
        {
            var target = GameData.Zone(exit.To);
            float hx = zone.SizeX * 0.5f - 3f, hz = zone.SizeZ * 0.5f - 3f;
            Vector3 edge = exit.Side switch
            {
                "north" => Local(0f, -hz),
                "south" => Local(0f, hz),
                "east" => Local(hx, 0f),
                _ => Local(-hx, 0f),
            };
            var p = ToMap(edge);
            DrawCircle(p, 7f, target?.Open == true ? new Color(0.5f, 1f, 0.6f) : new Color(0.6f, 0.6f, 0.65f));
            string state = target?.Open == true ? $" (Lv {target.LevelText})" : " (ยังไม่เปิด)";
            var offset = exit.Side switch { "north" => new Vector2(0f, 26f), "south" => new Vector2(0f, -14f), "east" => new Vector2(-110f, 5f), _ => new Vector2(110f, 5f) };
            Text(p + offset, $"→ {target?.Name ?? exit.To}{state}", 14, UiTheme.Muted);
        }

        foreach (var g in Ghost.All.Where(g => !g.Gone))
            DrawCircle(ToMap(g.GlobalPosition), g.Weakened ? 4f : 3.5f, g.Weakened ? new Color(0.6f, 0.75f, 1f) : new Color(1f, 0.3f, 0.25f));
        foreach (var loot in GetTree().GetNodesInGroup("loot").OfType<Node3D>())
            DrawCircle(ToMap(loot.GlobalPosition), 2.5f, new Color(1f, 0.9f, 0.4f));

        DrawSetTransform(Vector2.Zero);
        var player = Game.Player;
        if (player != null)
        {
            var p = ToMap(player.GlobalPosition);
            // View cone of the camera, then the player arrow.
            var look = Game.Main.CameraForward;
            var fwd = new Vector2(look.X, look.Z);
            var cone = new[] { p, p + fwd.Rotated(-0.5f) * 40f, p + fwd.Rotated(0.5f) * 40f };
            DrawColoredPolygon(cone, new Color(1f, 1f, 1f, 0.12f));
            var f = new Vector2(player.Facing.X, player.Facing.Z);
            var arrow = new[] { p + f * 11f, p + f.Rotated(2.5f) * 8f, p + f.Rotated(-2.5f) * 8f };
            DrawColoredPolygon(arrow, new Color(1f, 0.92f, 0.5f));
            DrawPolyline(arrow.Append(arrow[0]).ToArray(), Colors.Black, 1.5f);
        }
        DrawRect(area, new Color(1f, 1f, 1f, 0.25f), false, 1.5f);
    }

    // ---------- world ----------

    void DrawWorld()
    {
        var zones = GameData.Zones;
        int minX = zones.Min(z => z.WorldX), maxX = zones.Max(z => z.WorldX);
        int minY = zones.Min(z => z.WorldY), maxY = zones.Max(z => z.WorldY);
        float cell = Mathf.Min(Size.X / (maxX - minX + 1), Size.Y / (maxY - minY + 1));
        var origin = (Size - new Vector2(maxX - minX + 1, maxY - minY + 1) * cell) * 0.5f;
        Vector2 Center(ZoneDef z) => origin + new Vector2(z.WorldX - minX + 0.5f, z.WorldY - minY + 0.5f) * cell;

        foreach (var z in zones)
            foreach (var exit in z.Exits)
            {
                var other = zones.FirstOrDefault(o => o.Id == exit.To);
                if (other != null)
                    DrawDashedLine(Center(z), Center(other), new Color(1f, 1f, 1f, 0.35f), 2f, 8f);
            }
        foreach (var z in zones)
        {
            var c = Center(z);
            var box = new Rect2(c - new Vector2(cell * 0.36f, cell * 0.26f), new Vector2(cell * 0.72f, cell * 0.52f));
            bool here = z.Id == CurrentZone;
            DrawRect(box, z.Open ? new Color(0.16f, 0.22f, 0.15f, 0.95f) : new Color(0.1f, 0.1f, 0.11f, 0.9f));
            DrawRect(box, here ? UiTheme.Gold : new Color(1f, 1f, 1f, 0.25f), false, here ? 3f : 1.5f);
            Text(c + new Vector2(0f, -4f), z.Name, 18, z.Open ? UiTheme.Cream : UiTheme.Muted);
            Text(c + new Vector2(0f, 20f), (here ? "คุณอยู่ที่นี่" : z.Open ? "เปิดแล้ว" : "ยังไม่เปิด") + $" · Lv {z.LevelText}", 13,
                here ? UiTheme.Gold : UiTheme.Muted);
        }
    }
}
