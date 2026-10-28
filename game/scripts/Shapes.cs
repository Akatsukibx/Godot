using Godot;
using System.Collections.Generic;

// Primitive-shape builders used by Models/WorldBuilder. Their output is baked into editable .tscn scenes.
public static class Shapes
{
    // While building scenes for baking, identical colours share one material to keep the files small.
    public static bool ShareMaterials { get; set; }
    static readonly Dictionary<(Color, float), StandardMaterial3D> Shared = new();

    public static StandardMaterial3D Material(Color color, float emission = 0f)
    {
        if (ShareMaterials && Shared.TryGetValue((color, emission), out var cached))
            return cached;
        var m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f };
        if (emission > 0f)
        {
            m.EmissionEnabled = true;
            m.Emission = color;
            m.EmissionEnergyMultiplier = emission;
        }
        if (color.A < 1f)
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        if (ShareMaterials)
            Shared[(color, emission)] = m;
        return m;
    }

    public static T Child<T>(Node parent, T node, string name = null) where T : Node
    {
        if (name != null)
            node.Name = name;
        parent.AddChild(node, true);
        return node;
    }

    public static Node3D Group(Node parent, string name, Vector3 pos = default, float yaw = 0f) =>
        Child(parent, new Node3D { Position = pos, Rotation = new Vector3(0f, yaw, 0f) }, name);

    public static MeshInstance3D Add(Node3D parent, Mesh mesh, Vector3 pos, Color color, float emission = 0f, string name = null) =>
        Child(parent, new MeshInstance3D { Mesh = mesh, Position = pos, MaterialOverride = Material(color, emission) }, name);

    public static MeshInstance3D Box(Node3D parent, Vector3 pos, Vector3 size, Color color, float emission = 0f, string name = null) =>
        Add(parent, new BoxMesh { Size = size }, pos, color, emission, name);

    public static MeshInstance3D Sphere(Node3D parent, Vector3 pos, float radius, Color color, float emission = 0f, string name = null) =>
        Add(parent, new SphereMesh { Radius = radius, Height = radius * 2f }, pos, color, emission, name);

    public static MeshInstance3D Cylinder(Node3D parent, Vector3 pos, float top, float bottom, float height, Color color, float emission = 0f, string name = null) =>
        Add(parent, new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = height }, pos, color, emission, name);

    public static MeshInstance3D Capsule(Node3D parent, Vector3 pos, float radius, float height, Color color, float emission = 0f, string name = null) =>
        Add(parent, new CapsuleMesh { Radius = radius, Height = height }, pos, color, emission, name);

    public static MeshInstance3D Prism(Node3D parent, Vector3 pos, Vector3 size, Color color, string name = null) =>
        Add(parent, new PrismMesh { Size = size }, pos, color, 0f, name);

    public static StaticBody3D Solid(Node3D parent, Vector3 pos, Vector3 size, string name = null)
    {
        var body = Child(parent, new StaticBody3D { Position = pos, CollisionLayer = Layers.World, CollisionMask = 0 }, name ?? "Collider");
        Child(body, new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return body;
    }

    public static OmniLight3D Light(Node3D parent, Vector3 pos, Color color, float energy, float range, string name = null) =>
        Child(parent, new OmniLight3D { Position = pos, LightColor = color, LightEnergy = energy, OmniRange = range }, name);

    // Marks a node for the map screen; the tags survive baking so the map follows edits made in the editor.
    public static T MapTag<T>(T node, string group, Vector2 size) where T : Node
    {
        node.AddToGroup(group, true);
        node.SetMeta("map_size", size);
        return node;
    }
}
