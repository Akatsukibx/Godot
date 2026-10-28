using Godot;

// Short-lived visual effects that free themselves.
public static class Fx
{
    public static void DamageNumber(Node parent, Vector3 pos, string text, Color color)
    {
        var label = new Label3D
        {
            Text = text,
            FontSize = 84,
            OutlineSize = 16,
            Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Font = Game.ThaiFont,
        };
        parent.AddChild(label);
        label.GlobalPosition = pos + new Vector3((float)GD.RandRange(-0.3, 0.3), 0f, 0f);

        var tween = label.CreateTween().SetParallel();
        tween.TweenProperty(label, "position", label.Position + Vector3.Up * 1.2f, 0.8);
        tween.TweenProperty(label, "modulate:a", 0f, 0.5).SetDelay(0.3);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    public static void Ring(Node parent, Vector3 pos, float radius, Color color, float duration = 0.35f)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(color, 0.45f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var ring = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.04f },
            MaterialOverride = mat,
            Scale = new Vector3(0.2f, 1f, 0.2f),
        };
        parent.AddChild(ring);
        ring.GlobalPosition = new Vector3(pos.X, 0.06f, pos.Z);

        var tween = ring.CreateTween().SetParallel();
        tween.TweenProperty(ring, "scale", new Vector3(radius, 1f, radius), duration);
        tween.TweenProperty(mat, "albedo_color", new Color(color, 0f), duration);
        tween.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }
}
