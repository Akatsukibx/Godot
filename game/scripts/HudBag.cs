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
    Label _bagMoney;
    string _bagFilter = "";
    Button _bagUse, _bagUnequip;
    (string kind, int index, string slot) _bagSel = ("bag", 0, null);
    int _bagVersion = -1;

    const float SlotSize = 62f;

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

    // The character sheet on the left of the bag: level, EXP, health, stamina, the four stats with their
    // + buttons, derived numbers and a button to the spirit codex.
    Label _bsLevel, _bsPoints, _bsDerived;
    Button _bsUpgrade;
    ProgressBar _bsExp, _bsHp, _bsSt;
    Label _bsHpText, _bsStText;
    readonly List<(Label value, Button plus)> _bsRows = new();

    void BuildBagStats(Control parent)
    {
        var col = Column(250f, 5);
        col.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        parent.AddChild(col);
        _bsLevel = UiTheme.Label("", 22, UiTheme.Gold);
        col.AddChild(_bsLevel);
        (_bsExp, _) = TextBar(new Color(0.95f, 0.78f, 0.2f), 8f, 250f);
        col.AddChild(_bsExp);
        (_bsHp, _bsHpText) = TextBar(new Color(0.3f, 0.75f, 0.3f), 18f, 250f);
        col.AddChild(_bsHp);
        (_bsSt, _bsStText) = TextBar(new Color(0.25f, 0.6f, 0.95f), 18f, 250f);
        col.AddChild(_bsSt);
        col.AddChild(UiTheme.Dashed(6f));
        _bsPoints = UiTheme.Label("", 15, UiTheme.Gold);
        col.AddChild(_bsPoints);
        for (int i = 0; i < Player.StatInfo.Length; i++)
        {
            var (name, desc) = Player.StatInfo[i];
            int stat = i;
            var value = UiTheme.Label("0", 18, Colors.White);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            var row = UiTheme.Row(name, value);
            row.TooltipText = desc;
            col.AddChild(row);
            _bsRows.Add((value, null));
        }
        col.AddChild(UiTheme.Dashed(6f));
        _bsDerived = UiTheme.Label("", 13, UiTheme.Cream);
        _bsDerived.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bsDerived.CustomMinimumSize = new Vector2(250f, 0f);
        col.AddChild(_bsDerived);
        col.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _bsUpgrade = UiTheme.Button("อัปสถานะ / สมุดภูตผี", () => OpenWindow("stats"), 250f);
        col.AddChild(_bsUpgrade);
    }

    void RefreshBagStats()
    {
        var p = _player;
        int need = GameData.ExpToNext(p.Level);
        _bsLevel.Text = $"เลเวล {p.Level}";
        _bsExp.MaxValue = need;
        _bsExp.Value = p.Exp;
        _bsExp.TooltipText = $"EXP {p.Exp}/{need}";
        _bsHp.MaxValue = p.MaxHp;
        _bsHp.Value = p.Hp;
        _bsHpText.Text = $"{p.Hp:0} / {p.MaxHp:0}";
        _bsSt.MaxValue = p.MaxStamina;
        _bsSt.Value = p.Stamina;
        _bsStText.Text = $"{p.Stamina:0} / {p.MaxStamina:0}";
        _bsPoints.Text = p.StatPoints > 0 ? $"มีแต้มสถานะ {p.StatPoints} แต้ม — กด \"อัปสถานะ\"" : "ค่าพลังปัจจุบัน";
        _bsUpgrade.Text = p.StatPoints > 0 ? $"อัปสถานะ ({p.StatPoints})" : "อัปสถานะ / สมุดภูตผี";
        for (int i = 0; i < _bsRows.Count; i++)
        {
            _bsRows[i].value.Text = i switch
            {
                Player.Vit => $"{p.MaxHp:0} HP",
                Player.Str => $"{p.AttackDamage:0.#}",
                Player.Spi => $"{p.SkillMult * 100f:0}%",
                _ => $"{p.MaxStamina:0}",
            };
        }
        _bsDerived.Text = string.Join("\n",
            $"โจมตี {p.AttackDamage:0.#} · คลิกขวา {p.SpecialDamage:0.#}",
            $"ป้องกัน {p.Defense:0} · พลังจิต {p.SkillMult * 100f:0}%",
            $"ฟื้นความอึด {p.StaminaRegen:0.#}/วินาที",
            $"EXP {p.Exp}/{need}");
    }

    void BuildBag(VBoxContainer body)
    {
        // Category tabs (they dim the other kinds of item), the sort button, money and the craft button.
        var tools = new HBoxContainer();
        tools.AddThemeConstantOverride("separation", 6);
        foreach (var (key, label) in new[] { ("", "ทั้งหมด"), ("weapon", "อาวุธ"), ("armor", "ชุด"), ("jewelry", "เครื่องประดับ"), ("potion", "ยา"), ("material", "วัตถุดิบ") })
        {
            string k = key;
            var tab = UiTheme.Button(label, () => { _bagFilter = k; _bagVersion = -1; }, 0f);
            tab.CustomMinimumSize = new Vector2(0f, 34f);
            tools.AddChild(tab);
        }
        tools.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _bagMoney = UiTheme.Label("", 18, UiTheme.Gold);
        tools.AddChild(_bagMoney);
        tools.AddChild(UiTheme.Button("จัดเรียง", () => { _player.SortBag(); }, 110f));
        tools.AddChild(UiTheme.Button("⚒  คราฟต์", () => OpenWindow("craft"), 130f));
        body.AddChild(tools);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 16);
        body.AddChild(h);
        BuildBagStats(h);

        // Character with equipment slots on both sides.
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 26);
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 26);
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
        left.Alignment = BoxContainer.AlignmentMode.Center;
        right.Alignment = BoxContainer.AlignmentMode.Center;
        h.AddChild(left);
        var center = new VBoxContainer();
        center.AddThemeConstantOverride("separation", 4);
        h.AddChild(center);
        _bagPreview = ModelPreview.CreateEmpty(new Vector2(250f, 520f));
        center.AddChild(_bagPreview);
        _bagStats = UiTheme.Label("", 12, UiTheme.Muted);
        h.AddChild(right);

        _bagGrid = new GridContainer { Columns = 6 };
        _bagGrid.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
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
        detail.Alignment = BoxContainer.AlignmentMode.Center;
        detail.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
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
        s.TooltipText = stack != null ? ItemTooltip(stack) : "";
        s.AddThemeStyleboxOverride("hover_pressed", UiTheme.Box(new Color(0.34f, 0.35f, 0.37f), Colors.White, 2, 0, 4));
    }

    // Hover text for any item: name and grade, what it is, its stats (with the change against what is
    // worn in that slot), who can use it and where it comes from.
    public static string ItemTooltip(ItemStack s)
    {
        var d = s.Def;
        var p = Game.Player;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{d.Name}{(s.Plus > 0 ? $" +{s.Plus}" : "")}{(s.Count > 1 ? $"  x{s.Count}" : "")}");
        string kind = d.Equip switch
        {
            null => d.Use != null ? "ของใช้" : "วัตถุดิบ", "weapon" => "อาวุธ", "offhand" => "มือรอง", "helmet" => "หมวก", "armor" => "ชุดเกราะ",
            "pants" => "กางเกง", "boots" => "รองเท้า", "necklace" => "สร้อย", "ring" => "แหวน", _ => "เครื่องราง",
        };
        sb.AppendLine($"[{GameData.RarityNames[d.Rarity]}] {kind}");
        if (!string.IsNullOrEmpty(d.Description))
            sb.AppendLine(d.Description);
        if (d.Equip != null)
        {
            var worn = p != null && p.Equipped.TryGetValue(d.Equip, out var w) && w != s ? w : null;
            void Line(string label, float mine, float theirs, bool pct = false)
            {
                if (mine == 0f && theirs == 0f)
                    return;
                string v = pct ? $"+{mine * 100f:0}%" : $"+{mine:0}";
                float diff = mine - theirs;
                string cmp = worn == null || Mathf.Abs(diff) < 0.001f ? "" : diff > 0f ? $"  (▲ {(pct ? diff * 100f : diff):0})" : $"  (▼ {(pct ? -diff * 100f : -diff):0})";
                sb.AppendLine($"• {label} {v}{cmp}");
            }
            sb.AppendLine();
            Line("พลังโจมตี", d.Attack * s.Scale, (worn?.Def.Attack ?? 0f) * (worn?.Scale ?? 1f));
            Line("ป้องกัน", d.Defense * s.Scale, (worn?.Def.Defense ?? 0f) * (worn?.Scale ?? 1f));
            Line("ชีวิต", d.Hp * s.Scale, (worn?.Def.Hp ?? 0f) * (worn?.Scale ?? 1f));
            Line("ความอึด", d.Stamina * s.Scale, (worn?.Def.Stamina ?? 0f) * (worn?.Scale ?? 1f));
            Line("พลังจิต", d.Skill * s.Scale, (worn?.Def.Skill ?? 0f) * (worn?.Scale ?? 1f), true);
            if (Elements.Of(d.Element) is { } el)
                sb.AppendLine($"• ธาตุ{el.Name} {el.Glyph}: ศัตรูที่โดน{el.EffectText}");
            if (d.Set != null && GameData.Sets.TryGetValue(d.Set, out var set))
                sb.AppendLine($"• เซ็ต {set.Name}");
            if (d.Class != null)
                sb.AppendLine($"ใช้ได้เฉพาะ{GameData.Classes.FirstOrDefault(c => c.Id == d.Class)?.Name}");
            if (worn != null)
                sb.AppendLine($"(เทียบกับ {worn.Def.Name}{(worn.Plus > 0 ? $" +{worn.Plus}" : "")} ที่ใส่อยู่)");
        }
        else if (d.Use != null && d.Amount > 0f)
            sb.AppendLine($"• ฟื้นฟู {d.Amount:0}");
        return sb.ToString().TrimEnd();
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
        RefreshBagStats();
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
        _bagMoney.Text = $"● {p.Money} เบี้ย";
        for (int i = 0; i < Player.BagSize; i++)
            _bagSlots[i].Modulate = _bagFilter == "" || p.Bag[i] == null || Player.CategoryOf(p.Bag[i].Def) == _bagFilter ? Colors.White : new Color(1f, 1f, 1f, 0.28f);
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
