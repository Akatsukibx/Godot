using Godot;
using System.Collections.Generic;
using System.Text.Json;

// One save slot in user://save.json. Saved automatically when binding at a shrine, turning in a quest,
// and when leaving the game; the main menu offers "เล่นต่อ" when a save exists.
public class QuestSave
{
    public Dictionary<string, int[]> Progress { get; set; } = new();
    public List<string> Done { get; set; } = new();
    public List<string> Pinned { get; set; } = new();
}

public class StackSave
{
    public int Slot { get; set; }
    public string Id { get; set; }
    public int Count { get; set; } = 1;
    public int Plus { get; set; }
}

public class SaveData
{
    public string ClassId { get; set; }
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int StatPoints { get; set; }
    public int[] Attr { get; set; }
    public List<StackSave> Bag { get; set; } = new();
    public Dictionary<string, StackSave> Gear { get; set; } = new();
    // Older saves only:
    public Dictionary<string, int> Inventory { get; set; }
    public Dictionary<string, string> Equipped { get; set; }
    public List<string> Codex { get; set; } = new();
    public int Banished { get; set; }
    public int Released { get; set; }
    public int Merit { get; set; }
    public string SpawnShrine { get; set; }
    public float Hour { get; set; }
    public QuestSave Quests { get; set; } = new();
    public List<string> SeenCutscenes { get; set; } = new();
    public List<string> BusStops { get; set; } = new();
}

public static class SaveGame
{
    const string Path = "user://save.json";
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static bool Exists => FileAccess.FileExists(Path);

    public static void Save(SaveData data)
    {
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(data, Options));
    }

    public static SaveData Load()
    {
        if (!Exists)
            return null;
        try
        {
            return JsonSerializer.Deserialize<SaveData>(FileAccess.GetFileAsString(Path), Options);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"อ่านเซฟไม่ได้: {e.Message}");
            return null;
        }
    }
}
