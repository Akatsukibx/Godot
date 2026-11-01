using Godot;
using System.Collections.Generic;
using System.Linq;
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
    public string Name { get; set; }
    public string SavedAt { get; set; }
    public bool StarterGiven { get; set; }
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
    public int Money { get; set; }
    public string SpawnShrine { get; set; }
    public float Hour { get; set; }
    public QuestSave Quests { get; set; } = new();
    public List<string> SeenCutscenes { get; set; } = new();
    public List<string> BusStops { get; set; } = new();
}

public static class SaveGame
{
    public const int Slots = 5;
    const string LegacyPath = "user://save.json";
    const string Dir = "user://saves";
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    // The slot the running game saves into (0-based).
    public static int Current { get; set; } = 0;

    static string PathOf(int slot) => $"{Dir}/slot{slot + 1}.json";

    // Saves from before character slots: move the single save into slot 1 once (the old file stays as a backup).
    static void Migrate()
    {
        if (!FileAccess.FileExists(LegacyPath) || FileAccess.FileExists(PathOf(0)))
            return;
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        using var from = FileAccess.Open(LegacyPath, FileAccess.ModeFlags.Read);
        using var to = FileAccess.Open(PathOf(0), FileAccess.ModeFlags.Write);
        if (from != null && to != null)
            to.StoreString(from.GetAsText());
    }

    public static bool SlotUsed(int slot)
    {
        Migrate();
        return FileAccess.FileExists(PathOf(slot));
    }

    public static bool Exists => Enumerable.Range(0, Slots).Any(SlotUsed);

    public static int FirstFreeSlot() => Enumerable.Range(0, Slots).Cast<int?>().FirstOrDefault(s => !SlotUsed(s.Value)) ?? -1;

    public static void Save(SaveData data)
    {
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        data.SavedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        using var f = FileAccess.Open(PathOf(Current), FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(data, Options));
    }

    public static SaveData Load(int slot = -1)
    {
        if (slot < 0)
            slot = Current;
        if (!SlotUsed(slot))
            return null;
        try
        {
            return JsonSerializer.Deserialize<SaveData>(FileAccess.GetFileAsString(PathOf(slot)), Options);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"อ่านเซฟไม่ได้: {e.Message}");
            return null;
        }
    }

    public static void Delete(int slot)
    {
        if (SlotUsed(slot))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathOf(slot)));
    }
}
