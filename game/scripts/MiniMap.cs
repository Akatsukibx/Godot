using Godot;
using System.Linq;

// Small north-up map of the area around the player, drawn from the same map_* tagged nodes as the
// full map. Redrawn a few times a second.
public partial class MiniMap : Control
{
    const float Radius = 48f; // metres from the centre to each edge
    static readonly (string group, Color color)[] Layers =
    {
        ("map_water", new Color(0.1f, 0.22f, 0.32f)),
        ("map_land", new Color(0.14f, 0.18f, 0.12f)),
        ("map_grove", new Color(0.08f, 0.13f, 0.08f)),
        ("map_park", new Color(0.2f, 0.32f, 0.18f)),
        ("map_graveyard", new Color(0.22f, 0.2f, 0.24f)),
        ("map_plot", new Color(0.26f, 0.36f, 0.18f)),
        ("map_road", new Color(0.55f, 0.47f, 0.34f)),
        ("map_temple", new Color(0.6f, 0.5f, 0.38f)),
        ("map_deck", new Color(0.55f, 0.4f, 0.25f)),
        ("map_boat", new Color(0.4f, 0.28f, 0.17f)),
        ("map_wall", new Color(0.5f, 0.28f, 0.2f)),
        ("map_house", new Color(0.09f, 0.07f, 0.06f)),
        ("map_alley_dark", new Color(0.45f, 0.3f, 0.65f, 0.8f)),
        ("map_sign", new Color(0.95f, 0.8f, 0.45f)),
    };

    float _timer;

    public MiniMap()
    {
        CustomMinimumSize = new Vector2(176f, 176f);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        _timer -= (float)delta;
        if (_timer > 0f || !IsVisibleInTree())
            return;
        _timer = 0.1f;
        QueueRedraw();
    }

    float MapScale => Size.X / (Radius * 2f);
    Vector2 ToMap(Vector3 world, Vector3 center) => Size * 0.5f + new Vector2(world.X - center.X, world.Z - center.Z) * MapScale;

    public override void _Draw()
    {
        var player = Game.Player;
        if (player == null || Game.Main == null)
            return;
        var c = player.GlobalPosition;
        float s = MapScale;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.14f, 0.18f, 0.12f));
        foreach (var (group, color) in Layers)
            foreach (var node in GetTree().GetNodesInGroup(group).OfType<Node3D>())
            {
                var p = node.GlobalPosition;
                var meta = (Vector2)node.GetMeta("map_size", Vector2.One);
                float reach = Radius * 1.5f + Mathf.Max(meta.X, meta.Y) * 0.5f;
                if (Mathf.Abs(p.X - c.X) > reach || Mathf.Abs(p.Z - c.Z) > reach)
                    continue;
                var size = meta * s;
                DrawSetTransform(ToMap(p, c), -node.GlobalRotation.Y);
                DrawRect(new Rect2(-size * 0.5f, size), color);
            }
        DrawSetTransform(Vector2.Zero);

        foreach (var shrine in GetTree().GetNodesInGroup("map_shrine").OfType<Node3D>())
            DrawCircle(Clamp(ToMap(shrine.GlobalPosition, c)), 4f, new Color(1f, 0.82f, 0.35f));
        foreach (var npc in Game.Main.Npcs)
            DrawCircle(ToMap(npc.GlobalPosition, c), 3f, new Color(0.5f, 1f, 0.6f));
        foreach (var g in Ghost.All.Where(g => !g.Gone))
            DrawCircle(ToMap(g.GlobalPosition, c), 2.5f, g.Weakened ? new Color(0.6f, 0.75f, 1f) : new Color(1f, 0.3f, 0.25f));
        if (Game.Main.Navigation is QuestNav.Target t)
        {
            var p = Clamp(ToMap(t.Position, c));
            DrawColoredPolygon(new[] { p + new Vector2(0f, -6f), p + new Vector2(5f, 0f), p + new Vector2(0f, 6f), p + new Vector2(-5f, 0f) }, new Color(1f, 0.8f, 0.2f));
        }

        var f = new Vector2(player.Facing.X, player.Facing.Z);
        var m = Size * 0.5f;
        var arrow = new[] { m + f * 8f, m + f.Rotated(2.5f) * 6f, m + f.Rotated(-2.5f) * 6f };
        DrawColoredPolygon(arrow, new Color(1f, 0.92f, 0.5f));
        DrawPolyline(arrow.Append(arrow[0]).ToArray(), Colors.Black, 1.2f);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.85f, 0.7f, 0.4f, 0.9f), false, 2f);
        DrawString(Game.ThaiFont, new Vector2(Size.X * 0.5f - 6f, 14f), "N", HorizontalAlignment.Center, 12f, 13, new Color(1f, 0.5f, 0.45f));
    }

    Vector2 Clamp(Vector2 p) => p.Clamp(new Vector2(6f, 6f), Size - new Vector2(6f, 6f));
}
