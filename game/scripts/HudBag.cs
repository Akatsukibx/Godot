using Godot;
using System.Collections.Generic;
using System.Linq;

// A bag or equipment slot that can be dragged onto another slot.
public partial class ItemSlot : Button
{
    public string Kind;   // "bag" or "equip"
    public int Index;     // bag index
    public string Slot;   // equipment slot
    public System.Action<ItemSlot, ItemSlot> OnDropped;
    public System.Func<ItemStack> GetStack;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        var stack = GetStack?.Invoke();
        if (stack == null)
            return default;
        SetDragPreview(new TextureRect
        {
            Texture = Icons.Get(stack.Def.Icon),
            Size = new Vector2(48f, 48f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Modulate = new Color(1f, 1f, 1f, 0.85f),
        });
        return this;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => data.As<GodotObject>() is ItemSlot from && from != this;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.As<GodotObject>() is ItemSlot from)
            OnDropped?.Invoke(from, this);
    }
}

// Bag window (character with equipment slots, the bag grid, item details) and the upgrade window.
public partial class Hud
{
    readonly List<ItemSlot> _bagSlots = new();
    readonly Dictionary<string, ItemSlot> _equipSlots = new();
    GridContainer _bagGrid;
    ModelPreview _bagPreview;
    TextureRect _bagIcon;
    Label _bagName, _bagInfo, _bagDesc, _bagFooter, _bagStats;
    Button _bagUse, _bagUnequip;
    (string kind, int index, string slot) _bagSel = ("bag", 0, null);
    int _bagVersion = -1;

    const float SlotSize = 54f;

    ItemSlot MakeItemSlot(string kind, int index, string slot)
    {
        var s = new ItemSlot
        {
            Kind = kind, Index = index, Slot = slot,
            CustomMinimumSize = new Vector2(SlotSize, SlotSize),
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        s.GetStack = () => StackOf(s.Kind, s.Index, s.Slot);
        s.OnDropped = HandleDrop;
        s.Pressed += () =>
        {
            _bagSel = (s.Kind, s.Index, s.Slot);
            _bagVersion = -1;
        };
        // Double-click (or right-click) uses / equips / unequips.
        s.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true } mb && (mb.DoubleClick || mb.ButtonIndex == MouseButton.Right))
            {
                if (s.Kind == "bag")
                    _player.UseSlot(s.Index);
                else
                    _player.Unequip(s.Slot);
                _bagVersion = -1;
            }
        };
        var count = UiTheme.Label("", 12);
        count.Name = "Count";
        Place(count, 1f, 1f, -3f, 0f, Control.GrowDirection.Begin, Control.GrowDirection.Begin);
        s.AddChild(count);
        var plus = UiTheme.Label("", 12, UiTheme.Gold);
        plus.Name = "Plus";
        Place(plus, 0f, 0f, 3f, 0f, Control.GrowDirection.End, Control.GrowDirection.End);
        s.AddChild(plus);
        return s;
    }

    ItemStack StackOf(string kind, int index, string slot) =>
        kind == "bag" ? _player?.Bag[index] : slot != null && _player != null && _player.Equipped.TryGetValue(slot, out var e) ? e : null;

    void HandleDrop(ItemSlot from, ItemSlot to)
    {
        var p = _player;
        if (from.Kind == "bag" && to.Kind == "bag")
            p.MoveSlot(from.Index, to.Index);
        else if (from.Kind == "bag" && to.Kind == "equip")
        {
            var item = p.Bag[from.Index]?.Def;
            if (item?.Equip == to.Slot)
                p.Equip(from.Index);
            else if (item != null)
                Game.Toast(item.Equip == null ? $"{item.Name}สวมใส่ไม่ได้" : $"{item.Name}ต้องใส่ช่อง{SlotName(item.Equip)}");
        }
        else if (from.Kind == "equip" && to.Kind == "bag")
            p.Unequip(from.Slot, to.Index);
        _bagSel = (to.Kind, to.Index, to.Slot);
        _bagVersion = -1;
        Audio.Play("ui", -6f, 0f);
    }

    static string SlotName(string slot) => Player.EquipSlots.FirstOrDefault(s => s.slot == slot).name ?? slot;

    void BuildBag(VBoxContainer body)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 16);
        body.AddChild(h);

        // Character with equipment slots on both sides.
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 6);
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 6);
        foreach (var (slot, name) in Player.EquipSlots)
        {
            var col = slot is "helmet" or "armor" or "pants" or "boots" ? left : right;
            var s = MakeItemSlot("equip", 0, slot);
            s.TooltipText = name;
            _equipSlots[slot] = s;
            var wrap = new VBoxContainer();
            wrap.AddThemeConstantOverride("separation", 0);
            wrap.AddChild(s);
            wrap.AddChild(UiTheme.Label(name, 11, UiTheme.Muted));
            col.AddChild(wrap);
        }
        h.AddChild(left);
        var center = new VBoxContainer();
        center.AddThemeConstantOverride("separation", 4);
        h.AddChild(center);
        _bagPreview = ModelPreview.CreateEmpty(new Vector2(220f, 330f));
        center.AddChild(_bagPreview);
        _bagStats = UiTheme.Label("", 12, UiTheme.Muted);
        center.AddChild(_bagStats);
        h.AddChild(right);

        _bagGrid = new GridContainer { Columns = 6 };
        _bagGrid.AddThemeConstantOverride("h_separation", 4);
        _bagGrid.AddThemeConstantOverride("v_separation", 4);
        for (int i = 0; i < Player.BagSize; i++)
        {
            var s = MakeItemSlot("bag", i, null);
            _bagSlots.Add(s);
            _bagGrid.AddChild(s);
        }
        h.AddChild(_bagGrid);

        var detail = Column(220f, 6);
        h.AddChild(detail);
        _bagIcon = UiTheme.IconRect(null, 64f);
        _bagName = Wrapped("", 17, 220f, Colors.White);
        _bagInfo = UiTheme.Label("", 12, UiTheme.Muted);
        _bagDesc = Wrapped("", 13, 220f, UiTheme.Cream);
        _bagUse = UiTheme.Button("ใช้", () =>
        {
            if (_bagSel.kind == "bag")
                _player.UseSlot(_bagSel.index);
            _bagVersion = -1;
        }, 140f);
        _bagUnequip = UiTheme.Button("ถอด", () =>
        {
            if (_bagSel.kind == "equip")
                _player.Unequip(_bagSel.slot);
            _bagVersion = -1;
        }, 140f);
        foreach (var b in new[] { _bagUse, _bagUnequip })
            b.CustomMinimumSize = new Vector2(140f, 34f);
        detail.AddChild(_bagIcon);
        detail.AddChild(_bagName);
        detail.AddChild(_bagInfo);
        detail.AddChild(UiTheme.Dashed(6f));
        detail.AddChild(_bagDesc);
        detail.AddChild(_bagUse);
        detail.AddChild(_bagUnequip);

        _bagFooter = UiTheme.Label("", 13, UiTheme.Muted);
        body.AddChild(UiTheme.Dashed(6f));
        body.AddChild(_bagFooter);
    }

    static void ShowStack(ItemSlot s, ItemStack stack)
    {
        s.Icon = stack != null ? Icons.Get(stack.Def.Icon) : null;
        s.GetNode<Label>("Count").Text = stack != null && stack.Count > 1 ? stack.Count.ToString() : "";
        s.GetNode<Label>("Plus").Text = stack != null && stack.Plus > 0 ? $"+{stack.Plus}" : "";
        var border = stack != null ? GameData.RarityColor(stack.Def.Rarity) : new Color(1f, 1f, 1f, 0.08f);
        s.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0f, 0f, 0f, 0.4f), new Color(border, stack != null && stack.Def.Rarity > 0 ? 0.8f : 0.25f), 1, 0, 4));
        s.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.3f, 0.31f, 0.33f), Colors.White, 2, 0, 4));
        s.AddThemeStyleboxOverride("hover_pressed", UiTheme.Box(new Color(0.34f, 0.35f, 0.37f), Colors.White, 2, 0, 4));
    }

    public static string StackStats(ItemStack s)
    {
        var d = s.Def;
        var parts = new List<string>();
        void Add(float v, string label, bool pct = false)
        {
            if (v != 0f)
                parts.Add(pct ? $"{label} +{v * s.Scale * 100f:0}%" : $"{label} +{v * s.Scale:0}");
        }
        Add(d.Attack, "พลังโจมตี");
        Add(d.Defense, "ป้องกัน");
        Add(d.Hp, "ชีวิต");
        Add(d.Stamina, "ความอึด");
        Add(d.Skill, "พลังจิต", true);
        return string.Join(" · ", parts);
    }

    void RefreshBag()
    {
        var p = _player;
        if (_bagVersion != p.InventoryVersion)
        {
            _bagVersion = p.InventoryVersion;
            for (int i = 0; i < Player.BagSize; i++)
            {
                ShowStack(_bagSlots[i], p.Bag[i]);
                _bagSlots[i].SetPressedNoSignal(_bagSel.kind == "bag" && _bagSel.index == i);
            }
            foreach (var (slot, s) in _equipSlots)
            {
                ShowStack(s, p.Equipped.GetValueOrDefault(slot));
                s.SetPressedNoSignal(_bagSel.kind == "equip" && _bagSel.slot == slot);
            }
            _bagPreview.ShowEquipment(p.Def, p.Equipped);
            _bagStats.Text = $"โจมตี {p.AttackDamage:0} · ป้องกัน {p.Defense:0}\nชีวิต {p.MaxHp:0} · ความอึด {p.MaxStamina:0}";
        }

        var sel = StackOf(_bagSel.kind, _bagSel.index, _bagSel.slot);
        if (sel == null)
        {
            _bagIcon.Texture = null;
            _bagName.Text = _bagSel.kind == "equip" ? $"ช่อง{SlotName(_bagSel.slot)}ว่าง" : "ช่องว่าง";
            _bagInfo.Text = "ลากของมาวางเพื่อสวมใส่หรือจัดเรียง";
            _bagDesc.Text = "ดับเบิลคลิกหรือคลิกขวาที่ของเพื่อใช้ / สวมใส่ / ถอด";
            _bagUse.Visible = _bagUnequip.Visible = false;
        }
        else
        {
            var d = sel.Def;
            _bagIcon.Texture = Icons.Get(d.Icon);
            _bagName.Text = sel.Count > 1 ? $"{sel.Name}  x{sel.Count}" : sel.Name;
            _bagName.AddThemeColorOverride("font_color", GameData.RarityColor(d.Rarity));
            _bagInfo.Text = GameData.RarityNames[d.Rarity] + (d.Equip != null ? $" · {SlotName(d.Equip)}" : "");
            var lines = new List<string>();
            if (StackStats(sel) is { Length: > 0 } stats)
                lines.Add(stats);
            if (d.Set != null && GameData.Sets.TryGetValue(d.Set, out var set))
            {
                int pieces = p.SetPieces(d.Set);
                lines.Add($"{set.Name} ({pieces}/4)");
                lines.AddRange(set.Bonuses.Select(b => $"{(pieces >= b.Pieces ? "✓" : "·")} {b.Pieces} ชิ้น: {b.Text}"));
            }
            if (d.Equip != null && p.EquipProblem(d) is string problem)
                lines.Add($"✗ {problem}");
            lines.Add(d.Description);
            _bagDesc.Text = string.Join("\n", lines);
            _bagUse.Visible = _bagSel.kind == "bag" && (d.Use != null || d.Equip != null);
            _bagUse.Text = d.Equip != null ? "สวมใส่" : "ใช้";
            _bagUse.Disabled = d.Equip != null && !p.CanEquip(d);
            _bagUnequip.Visible = _bagSel.kind == "equip";
        }
        _bagFooter.Text = $"บุญ {p.Merit} · ปราบ {p.Banished} · ปลดปล่อย {p.Released} · ช่องว่าง {p.Bag.Count(s => s == null)}/{Player.BagSize}";
    }

    // ---------- upgrading (at ลุงมั่น) ----------

    VBoxContainer _enhList;
    TextureRect _enhIcon;
    Label _enhName, _enhStats, _enhCost, _enhChance;
    CheckButton _enhProtect;
    Button _enhButton;
    ItemStack _enhSel;
    int _enhVersion = -1;

    void BuildEnhance(VBoxContainer body)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 18);
        body.AddChild(h);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(280f, 380f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        h.AddChild(scroll);
        _enhList = Column(260f, 4);
        scroll.AddChild(_enhList);
        var detail = Column(340f, 8);
        h.AddChild(detail);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        _enhIcon = UiTheme.IconRect(null, 64f);
        _enhName = Wrapped("", 19, 260f, Colors.White);
        top.AddChild(_enhIcon);
        top.AddChild(_enhName);
        detail.AddChild(top);
        _enhStats = Wrapped("", 14, 340f, UiTheme.Cream);
        detail.AddChild(_enhStats);
        detail.AddChild(UiTheme.Dashed(6f));
        detail.AddChild(UiTheme.SectionTitle("วัตถุดิบ"));
        _enhCost = Wrapped("", 14, 340f);
        detail.AddChild(_enhCost);
        _enhChance = UiTheme.Label("", 16, UiTheme.Gold);
        detail.AddChild(_enhChance);
        _enhProtect = new CheckButton { Text = "ใช้ยันต์กันแตก" };
        _enhProtect.AddThemeFontSizeOverride("font_size", 14);
        detail.AddChild(_enhProtect);
        _enhButton = UiTheme.Button("ตีบวก", () =>
        {
            _player.Enhance(_enhSel, _enhProtect.ButtonPressed && _enhProtect.Visible);
            _enhVersion = -1;
        }, 200f);
        detail.AddChild(_enhButton);
    }

    public void OpenEnhance() => OpenWindow("enhance");

    void RefreshEnhance()
    {
        var p = _player;
        var gear = p.Equipped.Values.Concat(p.Bag.Where(s => s?.Def.Equip != null)).ToList();
        if (_enhSel != null && !gear.Contains(_enhSel))
            _enhSel = null;
        _enhSel ??= gear.FirstOrDefault();
        if (_enhVersion != p.InventoryVersion)
        {
            _enhVersion = p.InventoryVersion;
            foreach (var c in _enhList.GetChildren())
                c.QueueFree();
            if (gear.Count == 0)
                _enhList.AddChild(Wrapped("ยังไม่มีอุปกรณ์ให้ตีบวก คราฟต์อาวุธหรือชุดเกราะก่อน", 14, 250f, UiTheme.Muted));
            foreach (var s in gear)
            {
                var stack = s;
                bool worn = p.Equipped.ContainsValue(s);
                var b = UiTheme.ListButton((worn ? "◆ " : "") + s.Name, () =>
                {
                    _enhSel = stack;
                    _enhVersion = -1;
                }, 260f, Icons.Get(s.Def.Icon));
                b.CustomMinimumSize = new Vector2(260f, 40f);
                b.AddThemeColorOverride("font_color", GameData.RarityColor(s.Def.Rarity));
                b.SetPressedNoSignal(s == _enhSel);
                _enhList.AddChild(b);
            }
        }
        if (_enhSel == null)
        {
            _enhIcon.Texture = null;
            _enhName.Text = "เลือกอุปกรณ์";
            _enhStats.Text = _enhCost.Text = _enhChance.Text = "";
            _enhButton.Disabled = true;
            _enhProtect.Visible = false;
            return;
        }
        var sel = _enhSel;
        _enhIcon.Texture = Icons.Get(sel.Def.Icon);
        _enhName.Text = sel.Name;
        _enhName.AddThemeColorOverride("font_color", GameData.RarityColor(sel.Def.Rarity));
        if (sel.Plus >= Player.MaxEnhance)
        {
            _enhStats.Text = $"{StackStats(sel)}\nตีบวกถึงขั้นสูงสุดแล้ว";
            _enhCost.Text = _enhChance.Text = "";
            _enhButton.Disabled = true;
            _enhProtect.Visible = false;
            return;
        }
        var next = new ItemStack { Id = sel.Id, Plus = sel.Plus + 1 };
        _enhStats.Text = $"ตอนนี้: {StackStats(sel)}\n+{next.Plus}: {StackStats(next)}";
        var cost = Player.EnhanceCost(sel.Plus);
        _enhCost.Text = string.Join("\n", cost.Select(kv => $"{(p.ItemCount(kv.Key) >= kv.Value ? "✓" : "✗")} {GameData.ItemName(kv.Key)}  {p.ItemCount(kv.Key)}/{kv.Value}"));
        float chance = Player.EnhanceChance(sel.Plus);
        bool risky = sel.Plus >= GameData.Config.EnhanceDropFrom;
        _enhChance.Text = $"โอกาสสำเร็จ {chance * 100f:0}%" + (risky ? " · พลาดแล้วลดขั้น" : "");
        int charms = p.ItemCount("yan_kan_taek");
        _enhProtect.Visible = risky;
        _enhProtect.Text = $"ใช้ยันต์กันแตก (มี {charms})";
        _enhProtect.Disabled = charms == 0;
        _enhButton.Disabled = !p.CanAfford(cost);
        _enhButton.Text = $"ตีบวกเป็น +{next.Plus}";
    }
}
