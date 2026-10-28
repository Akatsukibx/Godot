using Godot;

// Writes the code-built models and world to res://models/*.tscn so they can be edited in the Godot editor.
// Run with bake.bat (or `-- --bake`). Baking overwrites those files, so only re-bake when you want
// to throw away editor changes and start again from the code.
public static class SceneBaker
{
    public static void BakeAll()
    {
        DirAccess.MakeDirRecursiveAbsolute("res://models");
        Shapes.ShareMaterials = true;
        foreach (var c in GameData.Classes)
            Save(Models.BuildPlayer(c), Models.PlayerPath(c.Id));
        foreach (var g in GameData.Ghosts.Values)
            Save(Models.BuildGhost(g), Models.GhostPath(g.Id));
        foreach (var n in GameData.Npcs.Values)
            Save(Models.BuildNpc(n), Models.NpcPath(n.Id));
        foreach (var z in GameData.Zones)
            if (z.Scene != null)
                Save(WorldBuilder.BuildZone(z.Scene), Models.ZonePath(z.Scene));
        Save(WorldBuilder.BuildOutskirts(), Models.OutskirtsPath);
        Shapes.ShareMaterials = false;
    }

    static void Save(Node3D root, string path)
    {
        SetOwner(root, root);
        var scene = new PackedScene();
        var err = scene.Pack(root);
        // Write to a temporary file and swap it in, so an interrupted bake never leaves a broken scene.
        string tmp = path.Replace(".tscn", ".tmp.tscn");
        if (err == Error.Ok)
            err = ResourceSaver.Save(scene, tmp);
        if (err == Error.Ok)
            err = DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(tmp), ProjectSettings.GlobalizePath(path));
        GD.Print($"[bake] {path}: {err}");
        root.Free();
    }

    static void SetOwner(Node node, Node owner)
    {
        foreach (var child in node.GetChildren())
        {
            child.Owner = owner;
            SetOwner(child, owner);
        }
    }
}
