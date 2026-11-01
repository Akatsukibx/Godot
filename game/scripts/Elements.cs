using Godot;
using System.Collections.Generic;
using System.Linq;

// ธาตุ: every hit carries one. Each element leaves its own mark on a ghost that is not resistant to it
// (a ghost that resists, multiplier < 1, shrugs the effect off):
//   ไฟ (fire)       เผาไหม้ — burns for a few seconds after the hit
//   มนตรา (blessed) ผนึก   — sealed: the ghost hits a quarter softer for a while
//   ข้าวสาร (rice)  ตรึง   — scattered grains hold it: slowed briefly
//   จันทรา (moon)   เงาจันทร์ — marks it; the next hit on a marked ghost cuts deeper and clears the mark
// Weapons can carry their own element (ItemDef.Element); otherwise the class's attack element is used.
public static class Elements
{
    public record Info(string Id, string Name, string Effect, string EffectText, Color Color, string Glyph);

    public static readonly Dictionary<string, Info> All = new()
    {
        ["fire"] = new("fire", "ไฟ", "เผาไหม้", "ติดไฟ เสียเลือดต่อเนื่อง 3 วินาที", new Color(1f, 0.55f, 0.18f), "🔥"),
        ["blessed"] = new("blessed", "มนตรา", "ผนึก", "ถูกผนึก โจมตีเบาลง 25% นาน 4 วินาที", new Color(1f, 0.88f, 0.45f), "✦"),
        ["rice"] = new("rice", "ข้าวสาร", "ตรึง", "ถูกข้าวสารตรึง ช้าลง 30% นาน 2 วินาที", new Color(0.96f, 0.95f, 0.85f), "❖"),
        ["moon"] = new("moon", "จันทรา", "เงาจันทร์", "ติดรอยเงาจันทร์ ครั้งต่อไปแรงขึ้น 40%", new Color(0.62f, 0.75f, 1f), "☾"),
    };

    public static Info Of(string id) => id != null && All.TryGetValue(id, out var e) ? e : null;

    public static string Name(string id) => Of(id)?.Name ?? "กายภาพ";

    public static Color ColorOf(string id) => Of(id)?.Color ?? Colors.White;

    // "แพ้ ไฟ ×2.0 · ต้าน ข้าวสาร ×0.5" for the codex and tooltips.
    public static string WeaknessText(Dictionary<string, float> weakness)
    {
        if (weakness == null || weakness.Count == 0)
            return "ไม่มีจุดอ่อนเด่นชัด";
        var weak = weakness.Where(w => w.Value > 1.05f).OrderByDescending(w => w.Value).Select(w => $"{Name(w.Key)} ×{w.Value:0.0}");
        var resist = weakness.Where(w => w.Value < 0.95f).OrderBy(w => w.Value).Select(w => $"{Name(w.Key)} ×{w.Value:0.0}");
        var parts = new List<string>();
        if (weak.Any())
            parts.Add("แพ้ " + string.Join(", ", weak));
        if (resist.Any())
            parts.Add("ต้าน " + string.Join(", ", resist));
        return parts.Count > 0 ? string.Join(" · ", parts) : "ไม่มีจุดอ่อนเด่นชัด";
    }
}
