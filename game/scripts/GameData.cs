using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Game data lives in res://data/*.json so new ghosts, classes, items, recipes and quests need no code changes.

public class AttackDef
{
    public string Type { get; set; } = "melee";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public float Damage { get; set; }
    public float Range { get; set; }
    public float Arc { get; set; } = 90f;
    public float Cooldown { get; set; } = 0.5f;
    public string Element { get; set; } = "none";
    public float ProjectileSpeed { get; set; } = 18f;
    public int Count { get; set; } = 1;
    public float Distance { get; set; } = 3f;
    public float StaminaCost { get; set; } = 12f;
}

public class SkillDef
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Type { get; set; } = "aoe";
    public float Damage { get; set; }
    public float Radius { get; set; }
    public float Cooldown { get; set; }
    public string Element { get; set; } = "none";
    public float Slow { get; set; }
    public float SlowDuration { get; set; }
    public float Amount { get; set; }
    public float Duration { get; set; }
    public float DamageReduction { get; set; }
    public float StaminaCost { get; set; } = 25f;
}

public class ClassDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Role { get; set; }
    public string Description { get; set; }
    public string Color { get; set; } = "#ffffff";
    public float MaxHp { get; set; }
    public float MoveSpeed { get; set; }
    public float HpPerLevel { get; set; }
    public float DamagePerLevel { get; set; }
    public AttackDef Attack { get; set; }
    public AttackDef Special { get; set; }
    public string Range { get; set; } = "ใกล้";
    public int Difficulty { get; set; } = 1;
    public string Emblem { get; set; }
    public string Offhand { get; set; }
    public bool Locked { get; set; }
    public Dictionary<string, int> Ratings { get; set; } = new();
    public List<SkillDef> Skills { get; set; } = new();
}

public class DropDef
{
    public string Item { get; set; }
    public float Chance { get; set; }
    public int MinLevel { get; set; }
    public bool NightOnly { get; set; }
}

public class GhostDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Behavior { get; set; } = "brute";
    public string Color { get; set; } = "#ffffff";
    public float MaxHp { get; set; }
    public float Speed { get; set; }
    public float Damage { get; set; }
    public float AttackRange { get; set; }
    public float AttackCooldown { get; set; }
    public float AggroRange { get; set; }
    public int Exp { get; set; }
    public bool ActiveAtDay { get; set; }
    public bool DespawnAtDawn { get; set; }
    public float SpawnWeightDay { get; set; }
    public float SpawnWeightNight { get; set; }
    public Dictionary<string, float> Weakness { get; set; } = new();
    public List<DropDef> Drops { get; set; } = new();
    public string Story { get; set; } = "";
    public string ReleaseText { get; set; } = "";
}

public class ItemDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Icon { get; set; } = "";
    public string Description { get; set; } = "";
    public string Use { get; set; }
    public float Amount { get; set; }
    public string Equip { get; set; }
    public string Class { get; set; }
    public float Attack { get; set; }
    public float Skill { get; set; }
    public float Hp { get; set; }
    public float Stamina { get; set; }
    public int Rarity { get; set; }
    public string OffhandType { get; set; }
    public float Defense { get; set; }
    public string Set { get; set; }
    public string Color { get; set; }
    public bool Stackable => Equip == null;
}

public class RecipeDef
{
    public string Result { get; set; }
    public int Count { get; set; } = 1;
    public float Distance { get; set; } = 3f;
    public string Category { get; set; } = "";
    public Dictionary<string, int> Ingredients { get; set; } = new();
}

public class ObjectiveDef
{
    public string Type { get; set; }
    public string Target { get; set; } = "any";
    public int Count { get; set; } = 1;
    public float Distance { get; set; } = 3f;
    public string Text { get; set; } = "";
}

public class QuestRewardDef
{
    public int Exp { get; set; }
    public Dictionary<string, int> Items { get; set; } = new();
}

public class QuestDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Giver { get; set; } = "";
    public string Description { get; set; } = "";
    public string Requires { get; set; }
    public string TurnIn { get; set; }
    public bool Main { get; set; }
    public int Chapter { get; set; }
    public bool AutoAccept { get; set; }
    public string CutsceneOnComplete { get; set; }
    public QuestDialogueDef Dialogue { get; set; } = new();
    public string TurnInNpc => TurnIn ?? Giver;
    public List<ObjectiveDef> Objectives { get; set; } = new();
    public QuestRewardDef Rewards { get; set; } = new();
}


public class QuestDialogueDef
{
    public List<string> Offer { get; set; } = new();
    public List<string> Progress { get; set; } = new();
    public List<string> Complete { get; set; } = new();
}

public class NpcLookDef
{
    public string Cloth { get; set; } = "#6a5a4a";
    public string Pants { get; set; } = "#2a2420";
    public string Skin { get; set; } = "#c9a487";
    public string Hair { get; set; } = "#1a1410";
    // bun / bald / headband / cap / ngob (งอบ) / hood
    public string Hat { get; set; }
    public bool Lantern { get; set; }
    public bool Robe { get; set; }
    public bool Apron { get; set; }
    public float Height { get; set; } = 1f;
}

public class NpcDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Title { get; set; } = "";
    public string Zone { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public NpcLookDef Look { get; set; } = new();
    public List<string> Lines { get; set; } = new();
}

public class CutsceneLineDef
{
    public string Speaker { get; set; } = "";
    public string Text { get; set; } = "";
}

public class CutsceneShotDef
{
    public float[] From { get; set; }
    public float[] To { get; set; }
    public float[] Look { get; set; }
    public float Duration { get; set; } = 5f;
    public string Prop { get; set; }
    public float[] PropAt { get; set; }
    public List<CutsceneLineDef> Lines { get; set; } = new();
}

public class CutsceneDef
{
    public string Id { get; set; }
    public float? Hour { get; set; }
    public List<CutsceneShotDef> Shots { get; set; } = new();
}
public class ExitDef
{
    public string To { get; set; }
    public string Side { get; set; }
    public string Label { get; set; } = "";
}

public class ZoneDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public bool Open { get; set; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public string Subtitle { get; set; } = "";
    public string Scene { get; set; }
    public float CenterX { get; set; }
    public float CenterZ { get; set; }
    public float SizeX { get; set; }
    public float SizeZ { get; set; }
    public int LevelMin { get; set; } = 1;
    public int LevelMax { get; set; } = 1;
    public string LevelText { get; set; } = "";
    public List<string> Ghosts { get; set; } = new();
    public bool AlleyOnly { get; set; }
    public int MaxGhostsDay { get; set; }
    public int MaxGhostsNight { get; set; }
    public float RainChance { get; set; }
    public Vector3 Center => new(CenterX, 0f, CenterZ);
    public bool Contains(Vector3 p) => Scene != null && Mathf.Abs(p.X - CenterX) <= SizeX * 0.5f && Mathf.Abs(p.Z - CenterZ) <= SizeZ * 0.5f;
    public List<ExitDef> Exits { get; set; } = new();
}

public class GameConfig
{
    public float MinutesPerSecond { get; set; } = 1f;
    public float DayStartHour { get; set; } = 6f;
    public float NightStartHour { get; set; } = 18f;
    public float StartHour { get; set; } = 16.5f;
    public float ExpBase { get; set; } = 60f;
    public float ExpGrowth { get; set; } = 1.4f;
    public int StatPointsPerLevel { get; set; } = 3;
    public float SprintMultiplier { get; set; } = 1.6f;
    public float DashDistance { get; set; } = 6f;
    public float DashDuration { get; set; } = 0.2f;
    public float DashCooldown { get; set; } = 0.5f;
    public float MaxStamina { get; set; } = 100f;
    public float DashStaminaCost { get; set; } = 30f;
    public float StaminaRegen { get; set; } = 22f;
    public float StaminaRegenDelay { get; set; } = 0.8f;
    public float JumpVelocity { get; set; } = 6.5f;
    public float StepHeight { get; set; } = 0.42f;
    public float HpRegenDelay { get; set; } = 10f;
    public float HpRegenPercent { get; set; } = 0.03f;
    public float[] CraftSuccess { get; set; } = { 1f, 0.95f, 0.8f, 0.6f, 0.4f };
    public float[] EnhanceSuccess { get; set; } = { 1f, 1f, 1f, 0.85f, 0.7f, 0.55f, 0.4f, 0.3f, 0.2f, 0.1f };
    public int EnhanceDropFrom { get; set; } = 6;
    public float EnhanceStatPerLevel { get; set; } = 0.1f;
    public float BackpedalMultiplier { get; set; } = 0.6f;
    public float StrafeMultiplier { get; set; } = 0.85f;
    public float ComboWindowSec { get; set; } = 1.2f;
    public float ComboFinisherMult { get; set; } = 1.8f;
    public float MaxWadeDepth { get; set; } = 1.05f;
    public float WadeSlowAtMaxDepth { get; set; } = 0.45f;
    public float GhostHpPerLevel { get; set; } = 0.18f;
    public float GhostDamagePerLevel { get; set; } = 0.12f;
    public float GhostExpPerLevel { get; set; } = 0.25f;
    public float RainRollHours { get; set; } = 3f;
    public bool WeakenedAutoBanish { get; set; } = true;
    public float LootLifetimeSec { get; set; } = 90f;
    public float LootMagnetRange { get; set; } = 3.5f;


    public float SpawnIntervalDay { get; set; } = 7f;
    public float SpawnIntervalNight { get; set; } = 2.5f;
    public float NightDamageMult { get; set; } = 1.4f;
    public float NightDropMult { get; set; } = 1.4f;
    public float NightExpMult { get; set; } = 1.5f;
    public float SafeZoneRadius { get; set; } = 14f;
    public float WorldHalfSize { get; set; } = 60f;
    public float WeakenedDurationSec { get; set; } = 10f;
    public float ReleaseExpRatio { get; set; } = 0.5f;
    public float ReleaseHeal { get; set; } = 15f;
    public float DeathExpLossRatio { get; set; } = 0.2f;
    public Dictionary<string, int> StartingItems { get; set; } = new();
}

public static class GameData
{
    public static GameConfig Config { get; private set; }
    public static List<ClassDef> Classes { get; private set; }
    public static Dictionary<string, GhostDef> Ghosts { get; private set; }
    public static Dictionary<string, ItemDef> Items { get; private set; }
    public static List<RecipeDef> Recipes { get; private set; }
    public static List<QuestDef> Quests { get; private set; }
    public static List<ZoneDef> Zones { get; private set; }
    public static Dictionary<string, NpcDef> Npcs { get; private set; }
    public static Dictionary<string, SetDef> Sets { get; private set; }

    public static readonly string[] RarityNames = { "ปกติ", "ดี", "หายาก", "ล้ำค่า", "ตำนาน" };
    public static readonly Color[] RarityColors =
    {
        new(0.85f, 0.85f, 0.85f), new(0.45f, 0.9f, 0.45f), new(0.4f, 0.65f, 1f), new(0.78f, 0.5f, 1f), new(1f, 0.65f, 0.2f),
    };
    public static Color RarityColor(int r) => RarityColors[Mathf.Clamp(r, 0, 4)];
    public static Dictionary<string, CutsceneDef> Cutscenes { get; private set; }

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static void Load()
    {
        Config = Read<GameConfig>("res://data/game.json");
        Classes = Read<List<ClassDef>>("res://data/classes.json");
        Ghosts = Read<List<GhostDef>>("res://data/ghosts.json").ToDictionary(g => g.Id);
        Items = Read<List<ItemDef>>("res://data/items.json").ToDictionary(i => i.Id);
        Recipes = Read<List<RecipeDef>>("res://data/recipes.json");
        Quests = Read<List<QuestDef>>("res://data/quests.json");
        Zones = Read<List<ZoneDef>>("res://data/zones.json");
        Npcs = Read<List<NpcDef>>("res://data/npcs.json").ToDictionary(n => n.Id);
        Sets = Read<List<SetDef>>("res://data/sets.json").ToDictionary(s => s.Id);
        Cutscenes = Read<List<CutsceneDef>>("res://data/cutscenes.json").ToDictionary(c => c.Id);
    }

    public static string ItemName(string id) => Items.TryGetValue(id, out var item) ? item.Name : id;
    public static string ItemIcon(string id) => Items.TryGetValue(id, out var item) ? item.Icon : null;
    public static string GhostName(string id) => Ghosts.TryGetValue(id, out var g) ? g.Name : id;
    public static string NpcName(string id) => Npcs.TryGetValue(id, out var n) ? n.Name : id;
    public static ZoneDef Zone(string id) => Zones.FirstOrDefault(z => z.Id == id);
    public static ZoneDef ZoneAt(Vector3 p) => Zones.FirstOrDefault(z => z.Contains(p));
    public static Vector3 ToV3(float[] a) => a == null ? Vector3.Zero : new Vector3(a[0], a[1], a[2]);

    public static int ExpToNext(int level) =>
        Mathf.RoundToInt(Config.ExpBase * Mathf.Pow(Config.ExpGrowth, level - 1));

    static T Read<T>(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new System.IO.FileNotFoundException($"อ่านไฟล์ข้อมูลไม่ได้: {path}");
        return JsonSerializer.Deserialize<T>(text, Options);
    }
}

public class SetBonusDef
{
    public int Pieces { get; set; }
    public string Text { get; set; } = "";
    public float Attack { get; set; }
    public float Defense { get; set; }
    public float Hp { get; set; }
    public float Stamina { get; set; }
    public float Skill { get; set; }
}

public class SetDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<SetBonusDef> Bonuses { get; set; } = new();
}
