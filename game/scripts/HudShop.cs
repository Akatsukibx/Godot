using Godot;
using System.Linq;

// Shop window (ร้านค้า): buy from the shopkeeper's list or sell anything from the bag. Opened from a
// shopkeeper's dialogue (NpcDef.Shop → data/shops.json).
public partial class Hud
{
    ShopDef _shop;
    bool _shopSelling;
    VBoxContainer _shopList;
    Label _shopTitle, _shopMoney;
    Button _shopBuyTab, _shopSellTab;
    int _shopVersion = -1;

    void BuildShop(VBoxContainer body)
    {
        _shopTitle = body.GetParent().GetChild(0).GetChild(0) as Label;
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        _shopBuyTab = UiTheme.Button("ซื้อ", () => { _shopSelling = false; _shopVersion = -1; }, 120f);
        _shopSellTab = UiTheme.Button("ขาย", () => { _shopSelling = true; _shopVersion = -1; }, 120f);
        _shopBuyTab.ToggleMode = _shopSellTab.ToggleMode = true;
        tabs.AddChild(_shopBuyTab);
        tabs.AddChild(_shopSellTab);
        tabs.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _shopMoney = UiTheme.Label("", 18, UiTheme.Gold);
        tabs.AddChild(_shopMoney);
        body.AddChild(tabs);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(620f, 420f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        _shopList = Column(600f, 4);
        scroll.AddChild(_shopList);
    }

    public void OpenShop(string id)
    {
        if (!GameData.Shops.TryGetValue(id, out _shop))
            return;
        _shopSelling = false;
        _shopVersion = -1;
        OpenWindow("shop");
    }

    void RefreshShop()
    {
        var p = _player;
        if (_shop == null || p == null)
            return;
        _shopMoney.Text = $"● {p.Money} เบี้ย";
        if (_shopTitle != null)
            _shopTitle.Text = _shop.Name;
        _shopBuyTab.SetPressedNoSignal(!_shopSelling);
        _shopSellTab.SetPressedNoSignal(_shopSelling);
        if (_shopVersion == p.InventoryVersion)
            return;
        _shopVersion = p.InventoryVersion;
        foreach (var c in _shopList.GetChildren())
            c.QueueFree();
        if (!_shopSelling)
        {
            foreach (var id in _shop.Items)
            {
                if (!GameData.Items.TryGetValue(id, out var d))
                    continue;
                int price = GameData.ItemPrice(d);
                var row = Row(new ItemStack { Id = id }, $"{price} เบี้ย", d.Class != null && d.Class != p.Def.Id ? "ใช้ไม่ได้กับสายนี้" : null);
                var item = d;
                var buy = UiTheme.Button("ซื้อ", () => p.Buy(item), 90f);
                buy.Disabled = p.Money < price;
                row.AddChild(buy);
                if (d.Stackable)
                {
                    var five = UiTheme.Button("ซื้อ 5", () => p.Buy(item, 5), 90f);
                    five.Disabled = p.Money < price * 5;
                    row.AddChild(five);
                }
            }
            return;
        }
        bool any = false;
        for (int i = 0; i < Player.BagSize; i++)
        {
            var s = p.Bag[i];
            if (s == null)
                continue;
            any = true;
            int each = Mathf.RoundToInt(GameData.SellPrice(s.Def) * (1f + s.Plus * 0.25f));
            var row = Row(s, $"{each} เบี้ย{(s.Count > 1 ? " / ชิ้น" : "")}", null);
            int index = i;
            row.AddChild(UiTheme.Button("ขาย", () => p.Sell(index, 1), 90f));
            if (s.Count > 1)
                row.AddChild(UiTheme.Button($"ขายหมด ({s.Count})", () => p.Sell(index, s.Count), 130f));
        }
        if (!any)
            _shopList.AddChild(UiTheme.Label("กระเป๋าว่าง ไม่มีของจะขาย", 15, UiTheme.Muted));
    }

    HBoxContainer Row(ItemStack s, string price, string warning)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.TooltipText = ItemTooltip(s);
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(UiTheme.IconRect(s.Def.Icon, 40f));
        var name = UiTheme.Label(s.Count > 1 ? $"{s.Name}  x{s.Count}" : s.Name, 16, GameData.RarityColor(s.Def.Rarity));
        name.CustomMinimumSize = new Vector2(250f, 0f);
        name.TooltipText = row.TooltipText;
        name.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(name);
        var cost = UiTheme.Label(warning != null ? $"{price}\n{warning}" : price, 14, warning != null ? new Color(1f, 0.5f, 0.45f) : UiTheme.Gold);
        cost.CustomMinimumSize = new Vector2(130f, 0f);
        row.AddChild(cost);
        _shopList.AddChild(row);
        return row;
    }
}
