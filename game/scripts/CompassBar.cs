using Godot;

// Heading strip at the top centre: N/E/S/W and the intercardinals slide past as the camera turns,
// with a gold marker for the tracked quest's target. North is -Z, east is +X.
public partial class CompassBar : Control
{
    const float HalfWidth = 230f;
    const float PxPerRad = HalfWidth / (Mathf.Pi * 0.5f);
    static readonly string[] Names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
            QueueRedraw();
    }

    static float Bearing(Vector3 v) => Mathf.Atan2(v.X, -v.Z);

    public override void _Draw()
    {
        if (Game.Main == null || Game.Player == null)
            return;
        var font = Game.ThaiFont;
        float cx = Size.X * 0.5f;
        DrawRect(new Rect2(cx - HalfWidth, 6f, HalfWidth * 2f, 26f), new Color(0f, 0f, 0f, 0.38f));
        float heading = Bearing(Game.Main.CameraForward);
        for (int i = 0; i < 16; i++)
        {
            float rel = Mathf.Wrap(i * Mathf.Pi / 8f - heading, -Mathf.Pi, Mathf.Pi);
            if (Mathf.Abs(rel) > Mathf.Pi * 0.5f)
                continue;
            float x = cx + rel * PxPerRad;
            float fade = 1f - Mathf.Abs(rel) / (Mathf.Pi * 0.5f) * 0.7f;
            if (i % 2 == 0)
            {
                string name = Names[i / 2];
                bool cardinal = name.Length == 1;
                var color = name == "N" ? new Color(1f, 0.45f, 0.4f, fade) : new Color(0.95f, 0.92f, 0.8f, fade);
                int size = cardinal ? 18 : 13;
                DrawStringOutline(font, new Vector2(x - 30f, 25f), name, HorizontalAlignment.Center, 60f, size, 4, new Color(0f, 0f, 0f, 0.8f * fade));
                DrawString(font, new Vector2(x - 30f, 25f), name, HorizontalAlignment.Center, 60f, size, color);
            }
            else
                DrawLine(new Vector2(x, 14f), new Vector2(x, 24f), new Color(1f, 1f, 1f, 0.35f * fade), 1.5f);
        }
        DrawColoredPolygon(new[] { new Vector2(cx, 36f), new Vector2(cx - 5f, 43f), new Vector2(cx + 5f, 43f) }, new Color(1f, 0.85f, 0.4f));

        // Tracked quest target
        if (Game.Main.Navigation is QuestNav.Target t && !Game.Main.Cutscene.Active)
        {
            float rel = Mathf.Wrap(Bearing(t.Position - Game.Player.GlobalPosition) - heading, -Mathf.Pi, Mathf.Pi);
            float x = cx + Mathf.Clamp(rel * PxPerRad, -HalfWidth + 8f, HalfWidth - 8f);
            DrawColoredPolygon(new[] { new Vector2(x, 4f), new Vector2(x - 7f, 19f), new Vector2(x, 14f), new Vector2(x + 7f, 19f) }, new Color(1f, 0.8f, 0.2f));
        }
    }
}
