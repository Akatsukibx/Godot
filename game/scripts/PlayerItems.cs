using Godot;
using System.Collections.Generic;
using System.Linq;

// One bag slot or equipped item. Equipment never stacks and carries its own upgrade level (+0…+10).
public class ItemStack
{
    public string Id { get; set; }
    public int Count { get; set; } = 1;
    public int Plus { get; set; }
    public ItemDef Def => GameData.Items[Id];
    public string Name => Plus > 0 ? $"{Def.Name} +{Plus}" : Def.Name;
    public float Scale => 1f + Plus * GameData.Config.EnhanceStatPerLevel;
}

// Player inventory: a fixed grid of bag slots (drag to rearrange), nine equipment slots,
// set bonuses, upgrading at the smith, crafting with success chances, and the save file.
public partial class Player
{
    public const int BagSize = 48;
    public const int MaxEnhance = 10;

    public static readonly (string slot, string name)[] EquipSlots =
    {
        ("helmet", "หมวก"), ("necklace", "สร้อย"), ("armor", "ชุดเกราะ"), ("weapon", "อาวุธ"), ("offhand", "มือรอง"),
        ("pants", "กางเกง"), ("ring", "แหวน"), ("boots", "รองเท้า"), ("charm", "เครื่องราง"),
    };

    public ItemStack[] Bag { get; } = new ItemStack[BagSize];
    public Dictionary<string, ItemStack> Equipped { get; } = new();
    public int InventoryVersion { get; private set; }

    // Sums an item stat over equipped gear (scaled by upgrade level) plus active set bonuses.
    float GearStat(System.Func<ItemDef, float> item, System.Func<SetBonusDef, float> set)
    {
        float total = Equipped.Values.Sum(s => item(s.Def) * s.Scale);
        foreach (var bonus in ActiveSetBonuses())
            total += set(bonus);
        return total;
    }

    public IEnumerable<SetBonusDef> ActiveSetBonuses()
    {
        foreach (var group in Equipped.Values.Where(s => s.Def.Set != null).GroupBy(s => s.Def.Set))
            if (GameData.Sets.TryGetValue(group.Key, out var set))
                foreach (var b in set.Bonuses.Where(b => group.Count() >= b.Pieces))
                    yield return b;
    }

    public int SetPieces(string setId) => Equipped.Values.Count(s => s.Def.Set == setId);

    // ---------- bag ----------

    public int ItemCount(string id) => Bag.Where(s => s?.Id == id).Sum(s => s.Count);

    void Changed()
    {
        InventoryVersion++;
        RefreshEquipmentVisuals();
    }

    // Throws a whole bag slot away for good.
    public void DiscardSlot(int index)
    {
        if (index < 0 || index >= BagSize || Bag[index] == null)
            return;
        Game.Toast($"ทิ้ง {Bag[index].Name}{(Bag[index].Count > 1 ? $" x{Bag[index].Count}" : "")} แล้ว", Bag[index].Def.Icon);
        Bag[index] = null;
        Changed();
    }

    public bool AddItem(string id, int count = 1, int plus = 0)
    {
        if (!GameData.Items.TryGetValue(id, out var def))
            return false;
        int left = count;
        if (def.Stackable)
        {
            var existing = Bag.FirstOrDefault(s => s?.Id == id);
            if (existing != null)
            {
                existing.Count += count;
                left = 0;
            }
        }
        while (left > 0)
        {
            int free = System.Array.IndexOf(Bag, null);
            if (free < 0)
            {
                Game.Toast($"กระเป๋าเต็ม · {def.Name} หล่นหาย");
                break;
            }
            int n = def.Stackable ? left : 1;
            Bag[free] = new ItemStack { Id = id, Count = n, Plus = plus };
            left -= n;
        }
        Changed();
        Quests.Refresh(this);
        return left == 0;
    }

    // Removes items, taking un-upgraded copies of equipment first.
    public void RemoveItem(string id, int count)
    {
        for (int i = 0; i < BagSize && count > 0; i++)
        {
            int idx = Enumerable.Range(0, BagSize).Where(j => Bag[j]?.Id == id).OrderBy(j => Bag[j].Plus).FirstOrDefault(-1);
            if (idx < 0)
                break;
            int take = Mathf.Min(count, Bag[idx].Count);
            Bag[idx].Count -= take;
            count -= take;
            if (Bag[idx].Count <= 0)
                Bag[idx] = null;
        }
        Changed();
    }

    // Drag and drop between bag slots (swap).
    public void MoveSlot(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= BagSize || to >= BagSize)
            return;
        var a = Bag[from];
        var b = Bag[to];
        if (a != null && b != null && a.Id == b.Id && a.Def.Stackable)
        {
            b.Count += a.Count;
            Bag[from] = null;
        }
        else
        {
            Bag[from] = b;
            Bag[to] = a;
        }
        Changed();
    }

    public bool UseItem(string id)
    {
        int idx = System.Array.FindIndex(Bag, s => s?.Id == id);
        if (idx < 0)
        {
            if (GameData.Items.TryGetValue(id, out var d))
                Game.Toast($"ไม่มี{d.Name}เหลือแล้ว");
            return false;
        }
        return UseSlot(idx);
    }

    public bool UseSlot(int index)
    {
        var stack = Bag[index];
        if (Dead || stack == null)
            return false;
        var item = stack.Def;
        if (item.Equip != null)
            return Equip(index);
        if (item.Use == "box")
            return OpenBox(index);
        if (item.Use != "heal")
            return false;
        if (Hp >= MaxHp)
        {
            Game.Toast("ชีวิตยังเต็มอยู่");
            return false;
        }
        Heal(item.Amount);
        Audio.Play("heal", -4f);
        stack.Count--;
        if (stack.Count <= 0)
            Bag[index] = null;
        Changed();
        return true;
    }

    // A mystery box: one random thing from its list (never a weapon for another class).
    bool OpenBox(int index)
    {
        var stack = Bag[index];
        var pool = (stack.Def.Box ?? new()).Where(id => GameData.Items.TryGetValue(id, out var d) && (d.Class == null || d.Class == Def.Id)).ToList();
        if (pool.Count == 0)
            return false;
        if (stack.Count == 1 ? false : Bag.All(s => s != null))
        {
            Game.Toast("กระเป๋าเต็ม เปิดกล่องไม่ได้");
            return false;
        }
        stack.Count--;
        if (stack.Count <= 0)
            Bag[index] = null;
        var got = GameData.Items[pool[(int)(GD.Randi() % (uint)pool.Count)]];
        AddItem(got.Id);
        Audio.Play("levelup", -6f, 0f);
        Fx.Ring(GetParent(), GlobalPosition, 2.2f, GameData.RarityColor(got.Rarity), 0.6f);
        Game.Toast($"เปิด{stack.Def.Name}ได้ {got.Name} [{GameData.RarityNames[got.Rarity]}]", got.Icon, 5f);
        Changed();
        return true;
    }

    // ---------- equipment ----------

    public bool CanEquip(ItemDef item) =>
        item.Equip != null && (item.Class == null || item.Class == Def.Id) &&
        (item.Equip != "offhand" || item.OffhandType == Def.Offhand);

    public string EquipProblem(ItemDef item)
    {
        if (item.Equip == null)
            return "สวมใส่ไม่ได้";
        if (item.Class != null && item.Class != Def.Id)
            return $"ใช้ได้เฉพาะ{GameData.Classes.FirstOrDefault(c => c.Id == item.Class)?.Name}";
        if (item.Equip == "offhand" && item.OffhandType != Def.Offhand)
            return Def.Offhand == null ? $"{Def.Name}ใช้มือรองไม่ได้" : $"{Def.Name}ใช้ได้เฉพาะ{(Def.Offhand == "shield" ? "โล่" : "มีด")}";
        return null;
    }

    public bool Equip(int bagIndex)
    {
        var stack = Bag[bagIndex];
        if (stack == null)
            return false;
        var item = stack.Def;
        if (EquipProblem(item) is string problem)
        {
            Game.Toast(problem);
            return false;
        }
        float hpRatio = Hp / MaxHp;
        Equipped.TryGetValue(item.Equip, out var old);
        Equipped[item.Equip] = stack;
        Bag[bagIndex] = old; // swap the previous piece into the same bag slot
        Hp = Mathf.Min(MaxHp, MaxHp * hpRatio);
        Stamina = Mathf.Min(Stamina, MaxStamina);
        Audio.Play("pickup", -2f, 0f);
        Game.Toast($"สวมใส่ {stack.Name}", item.Icon);
        Changed();
        return true;
    }

    public void Unequip(string slot, int toBagIndex = -1)
    {
        if (!Equipped.TryGetValue(slot, out var stack))
            return;
        if (toBagIndex < 0 || Bag[toBagIndex] != null)
            toBagIndex = System.Array.IndexOf(Bag, null);
        if (toBagIndex < 0)
        {
            Game.Toast("กระเป๋าเต็ม");
            return;
        }
        Equipped.Remove(slot);
        Bag[toBagIndex] = stack;
        Hp = Mathf.Min(Hp, MaxHp);
        Stamina = Mathf.Min(Stamina, MaxStamina);
        Changed();
    }

    public void RefreshEquipmentVisuals() => _rig?.ApplyEquipment(Def, Equipped);

    // ---------- upgrading (ตีบวก) ----------

    // Materials needed to take an item from +n to +n+1.
    public static Dictionary<string, int> EnhanceCost(int fromPlus) => (fromPlus + 1) switch
    {
        <= 3 and var n => new() { ["lek_sanim"] = 2 * n, ["mai_phu"] = 1 },
        <= 6 and var n => new() { ["lek_nam_phi"] = n - 3, ["saeng_winyan"] = 2 },
        <= 9 and var n => new() { ["namkhang_ratri"] = 1, ["lek_nam_phi"] = n - 5 },
        _ => new() { ["set_tra_phanuek"] = 1, ["namkhang_ratri"] = 2 },
    };

    public static float EnhanceChance(int fromPlus) =>
        GameData.Config.EnhanceSuccess[Mathf.Clamp(fromPlus, 0, GameData.Config.EnhanceSuccess.Length - 1)];

    public bool CanAfford(Dictionary<string, int> cost) => cost.All(kv => ItemCount(kv.Key) >= kv.Value);

    // Returns +1 on success, 0 on a harmless failure, -1 when the item dropped a level.
    public int Enhance(ItemStack stack, bool protect)
    {
        if (stack == null || stack.Def.Equip == null || stack.Plus >= MaxEnhance)
            return 0;
        var cost = EnhanceCost(stack.Plus);
        if (!CanAfford(cost) || (protect && ItemCount("yan_kan_taek") < 1))
            return 0;
        foreach (var (id, n) in cost)
            RemoveItem(id, n);
        if (GD.Randf() < EnhanceChance(stack.Plus))
        {
            stack.Plus++;
            Audio.Play("levelup", -2f, 0f);
            Game.Toast($"ตีบวกสำเร็จ! {stack.Name}", stack.Def.Icon);
            Changed();
            return 1;
        }
        Audio.Play("hurt", -2f, 0f);
        if (stack.Plus >= GameData.Config.EnhanceDropFrom)
        {
            if (protect)
            {
                RemoveItem("yan_kan_taek", 1);
                Game.Toast($"ตีบวกพลาด · ยันต์กันแตกคุ้มครองไว้ {stack.Name} ไม่ลดขั้น", "talisman_gold");
                Changed();
                return 0;
            }
            stack.Plus--;
            Game.Toast($"ตีบวกพลาด · {stack.Def.Name} ลดเหลือ +{stack.Plus}", stack.Def.Icon);
            Changed();
            return -1;
        }
        Game.Toast("ตีบวกพลาด · วัตถุดิบหายไป แต่ของไม่ลดขั้น", stack.Def.Icon);
        Changed();
        return 0;
    }

    // ---------- crafting ----------

    public bool CanCraft(RecipeDef r) => r.Ingredients.All(kv => ItemCount(kv.Key) >= kv.Value);

    // Crafting always works: having the ingredients is enough.
    public bool Craft(RecipeDef r)
    {
        if (Dead || !CanCraft(r))
            return false;
        foreach (var (id, n) in r.Ingredients)
            RemoveItem(id, n);
        var item = GameData.Items[r.Result];
        AddItem(r.Result, r.Count);
        Audio.Play("levelup", -8f, 0f);
        Game.Toast($"คราฟต์สำเร็จ: {item.Name} x{r.Count}", item.Icon);
        Quests.Notify(this, "craft", r.Result);
        return true;
    }

    // ---------- money (เบี้ย) and sorting ----------

    public void AddMoney(int n)
    {
        if (n <= 0)
            return;
        Money += n;
        Game.Toast($"ได้ {n} เบี้ย", null, 2f);
        Changed();
    }

    public bool SpendMoney(int n)
    {
        if (Money < n)
            return false;
        Money -= n;
        Changed();
        return true;
    }

    // ---------- shops ----------

    bool HasRoomFor(ItemDef d) => Bag.Any(s => s == null) || d.Stackable && Bag.Any(s => s?.Id == d.Id);

    public bool Buy(ItemDef d, int count = 1)
    {
        int price = GameData.ItemPrice(d) * count;
        if (Money < price)
        {
            Game.Toast("เบี้ยไม่พอ");
            return false;
        }
        if (!HasRoomFor(d) || !d.Stackable && Bag.Count(s => s == null) < count)
        {
            Game.Toast("กระเป๋าเต็ม");
            return false;
        }
        SpendMoney(price);
        AddItem(d.Id, count);
        Audio.Play("heal", -10f, 0f);
        Game.Toast($"ซื้อ {d.Name}{(count > 1 ? $" x{count}" : "")} · -{price} เบี้ย", d.Icon, 2.5f);
        return true;
    }

    // Sells from one bag slot (upgraded gear fetches more).
    public void Sell(int index, int count)
    {
        var s = Bag[index];
        if (s == null)
            return;
        count = Mathf.Clamp(count, 1, s.Count);
        int gain = Mathf.RoundToInt(GameData.SellPrice(s.Def) * (1f + s.Plus * 0.25f)) * count;
        s.Count -= count;
        if (s.Count <= 0)
            Bag[index] = null;
        Money += gain;
        Audio.Play("heal", -10f, 0f);
        Game.Toast($"ขาย {s.Name}{(count > 1 ? $" x{count}" : "")} · +{gain} เบี้ย", s.Def.Icon, 2.5f);
        Changed();
    }

    public static string CategoryOf(ItemDef d) => d.Equip switch
    {
        "weapon" or "offhand" => "weapon",
        "helmet" or "armor" or "pants" or "boots" => "armor",
        "necklace" or "ring" or "charm" => "jewelry",
        _ => d.Use != null ? "potion" : "material",
    };

    static readonly string[] CategoryOrder = { "weapon", "armor", "jewelry", "potion", "material" };

    // Merges stacks of the same item, then orders the bag by category, rarity (rare first) and name.
    public void SortBag()
    {
        var items = Bag.Where(s => s != null).ToList();
        var merged = new List<ItemStack>();
        foreach (var s in items)
        {
            var into = s.Def.Stackable ? merged.FirstOrDefault(m => m.Id == s.Id && m.Plus == s.Plus) : null;
            if (into != null)
                into.Count += s.Count;
            else
                merged.Add(s);
        }
        var sorted = merged.OrderBy(s => System.Array.IndexOf(CategoryOrder, CategoryOf(s.Def))).ThenByDescending(s => s.Def.Rarity)
            .ThenBy(s => s.Def.Name, System.StringComparer.Ordinal).ThenByDescending(s => s.Plus).ToList();
        System.Array.Clear(Bag);
        for (int i = 0; i < sorted.Count && i < BagSize; i++)
            Bag[i] = sorted[i];
        Changed();
    }

    // ---------- save ----------

    public SaveData ToSave() => new()
    {
        ClassId = Def.Id,
        Level = Level,
        Exp = Exp,
        StatPoints = StatPoints,
        Attr = Attr.ToArray(),
        Bag = Enumerable.Range(0, BagSize).Where(i => Bag[i] != null)
            .Select(i => new StackSave { Slot = i, Id = Bag[i].Id, Count = Bag[i].Count, Plus = Bag[i].Plus }).ToList(),
        Name = CharacterName,
        StarterGiven = true,
        Gear = Equipped.ToDictionary(kv => kv.Key, kv => new StackSave { Id = kv.Value.Id, Count = 1, Plus = kv.Value.Plus }),
        Codex = Codex.ToList(),
        Banished = Banished,
        Released = Released,
        Merit = Merit,
        Money = Money,
        OpenedChests = OpenedChests.ToList(),
        SpawnShrine = SpawnShrine,
        Quests = Quests.Export(),
        BusStops = BusStops.ToList(),
    };

    public void LoadSave(SaveData s)
    {
        Level = Mathf.Max(1, s.Level);
        Exp = s.Exp;
        StatPoints = s.StatPoints;
        if (s.Attr?.Length == 4)
            Attr = s.Attr;
        System.Array.Clear(Bag);
        foreach (var b in s.Bag ?? new())
            if (GameData.Items.ContainsKey(b.Id) && b.Slot is >= 0 and < BagSize)
                Bag[b.Slot] = new ItemStack { Id = b.Id, Count = Mathf.Max(1, b.Count), Plus = b.Plus };
        // Saves from before the bag grid kept plain counts.
        foreach (var (id, n) in s.Inventory ?? new())
            if (n > 0)
                AddItem(id, n);
        Equipped.Clear();
        foreach (var (slot, g) in s.Gear ?? new())
            if (GameData.Items.ContainsKey(g.Id))
                Equipped[slot] = new ItemStack { Id = g.Id, Plus = g.Plus };
        foreach (var (slot, id) in s.Equipped ?? new())
            if (GameData.Items.ContainsKey(id))
                Equipped[slot] = new ItemStack { Id = id };
        // Saves from before starter weapons: hand out the class weapon once.
        if (!s.StarterGiven && !Equipped.ContainsKey("weapon") && Def.StarterWeapon != null && GameData.Items.ContainsKey(Def.StarterWeapon))
            Equipped["weapon"] = new ItemStack { Id = Def.StarterWeapon };
        if (!string.IsNullOrWhiteSpace(s.Name))
            SetName(s.Name);
        Codex.Clear();
        foreach (var c in s.Codex ?? new())
            Codex.Add(c);
        Banished = s.Banished;
        Released = s.Released;
        Merit = s.Merit;
        Money = s.Money;
        OpenedChests.Clear();
        foreach (var c in s.OpenedChests ?? new())
            OpenedChests.Add(c);
        SpawnShrine = s.SpawnShrine ?? SpawnShrine;
        Quests.Import(s.Quests);
        BusStops.Clear();
        foreach (var b in s.BusStops ?? new())
            BusStops.Add(b);
        Changed();
        RestoreAll();
    }
}
