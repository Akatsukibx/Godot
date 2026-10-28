using Godot;
using System.Collections.Generic;
using System.Linq;

// Quest progress for one player. Quests come from data/quests.json and are taken from / handed in to NPCs.
// A quest unlocks once its "requires" quest has been turned in; autoAccept quests start by themselves.
// Kill/craft/talk/bind/reach objectives count events; collect and level objectives read the player's state.
public class QuestLog
{
    public enum State { Locked, Available, Active, Ready, Done }

    readonly Dictionary<string, int[]> _progress = new();
    readonly HashSet<string> _done = new();
    readonly HashSet<string> _announcedReady = new();
    readonly List<string> _pinned = new();
    public bool IsPinned(string id) => _pinned.Contains(id);
    public int Version { get; private set; }
    public event System.Action<QuestDef> Accepted;
    public event System.Action<QuestDef> Completed;

    public IEnumerable<QuestDef> Active => GameData.Quests.Where(q => _progress.ContainsKey(q.Id));
    public bool IsDone(string id) => _done.Contains(id);

    public State StateOf(QuestDef q, Player p)
    {
        if (_done.Contains(q.Id) && !q.Repeatable)
            return State.Done;
        if (_progress.ContainsKey(q.Id))
            return IsComplete(q, p) ? State.Ready : State.Active;
        if (q.Requires != null && !_done.Contains(q.Requires) || p.Level < q.MinLevel)
            return State.Locked;
        return State.Available;
    }

    public int Progress(QuestDef q, int i, Player p)
    {
        var o = q.Objectives[i];
        int value = o.Type switch
        {
            "collect" => p.ItemCount(o.Target),
            "level" => p.Level,
            _ => _progress.TryGetValue(q.Id, out var counts) ? counts[i] : 0,
        };
        return Mathf.Min(value, o.Count);
    }

    public bool IsComplete(QuestDef q, Player p) =>
        Enumerable.Range(0, q.Objectives.Count).All(i => Progress(q, i, p) >= q.Objectives[i].Count);

    // Quests an NPC can hand out right now, and quests ready to hand in to them.
    public IEnumerable<QuestDef> OffersFrom(string npc, Player p) =>
        GameData.Quests.Where(q => q.Giver == npc && !q.AutoAccept && StateOf(q, p) == State.Available);

    public IEnumerable<QuestDef> ReadyFor(string npc, Player p) =>
        GameData.Quests.Where(q => q.TurnInNpc == npc && StateOf(q, p) == State.Ready);

    public IEnumerable<QuestDef> ActiveFrom(string npc, Player p) =>
        GameData.Quests.Where(q => q.Giver == npc && StateOf(q, p) == State.Active);

    public void Accept(QuestDef q, Player p)
    {
        if (StateOf(q, p) != State.Available)
            return;
        _progress[q.Id] = new int[q.Objectives.Count];
        if (q.Main || _pinned.Count < 3)
            Pin(q.Id, true);
        Version++;
        Game.Toast(q.Main ? $"เนื้อเรื่องหลัก: {q.Name}" : $"รับเควส: {q.Name}", "quest");
        Accepted?.Invoke(q);
        Refresh(p);
    }

    public void TurnIn(QuestDef q, Player p)
    {
        if (StateOf(q, p) != State.Ready)
            return;
        foreach (var o in q.Objectives.Where(o => o.Type == "collect"))
            p.RemoveItem(o.Target, o.Count);
        _progress.Remove(q.Id);
        _done.Add(q.Id);
        Version++;
        Audio.Play("levelup", -4f, 0f);
        Game.Toast($"ส่งเควส {q.Name} สำเร็จ · +{q.Rewards.Exp} EXP", "quest");
        foreach (var (id, n) in q.Rewards.Items)
            p.AddItem(id, n);
        p.GainExp(q.Rewards.Exp);
        Completed?.Invoke(q);
        Refresh(p);
    }

    // Counts an event toward every active objective it matches:
    //   banish/release <ghostId> (objective "resolve" accepts either) · craft <itemId> · talk <npcId>
    //   bind <shrineId> · reach <zoneId>
    public void Notify(Player p, string type, string target)
    {
        foreach (var q in Active.ToList())
        {
            var counts = _progress[q.Id];
            for (int i = 0; i < q.Objectives.Count; i++)
            {
                var o = q.Objectives[i];
                bool typeOk = o.Type == type || (o.Type == "resolve" && type is "banish" or "release");
                if (!typeOk || counts[i] >= o.Count || !TargetMatches(o.Target, target))
                    continue;
                // Objectives are done in order when a quest has several (e.g. reach, then talk).
                if (Enumerable.Range(0, i).Any(j => Progress(q, j, p) < q.Objectives[j].Count))
                    continue;
                counts[i]++;
                Version++;
            }
        }
        Refresh(p);
    }

    static bool TargetMatches(string wanted, string actual) =>
        wanted == "any" || wanted == actual ||
        (wanted == "weapon" && GameData.Items.TryGetValue(actual, out var item) && item.Equip == "weapon");

    // Starts auto-accept quests and announces quests that just became ready to hand in.
    public void Refresh(Player p)
    {
        foreach (var q in GameData.Quests.Where(q => q.AutoAccept && StateOf(q, p) == State.Available).ToList())
            Accept(q, p);
        foreach (var q in Active)
        {
            bool ready = IsComplete(q, p);
            if (ready && _announcedReady.Add(q.Id))
            {
                Version++;
                // Talk-to-hand-in quests finish in the same conversation, so no extra nudge for those.
                if (!q.Objectives.Any(o => o.Type == "talk" && o.Target == q.TurnInNpc))
                    Game.Toast($"เควส “{q.Name}” ครบแล้ว · กลับไปหา{GameData.NpcName(q.TurnInNpc)}", "quest", 5f);
            }
            else if (!ready)
                _announcedReady.Remove(q.Id);
        }
    }


    // Pinned quests show in the tracker; the first pinned active one is navigated to.
    public void Pin(string id, bool on)
    {
        _pinned.Remove(id);
        if (on)
            _pinned.Insert(0, id);
        Version++;
    }

    public IEnumerable<QuestDef> PinnedActive => _pinned.Select(id => GameData.Quests.FirstOrDefault(q => q.Id == id))
        .Where(q => q != null && _progress.ContainsKey(q.Id));

    public QuestDef Tracked(Player p) => PinnedActive.FirstOrDefault() ?? Active.OrderBy(q => q.Main ? 0 : 1).FirstOrDefault();
    public QuestSave Export() => new()
    {
        Progress = _progress.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
        Done = _done.ToList(),
        Pinned = _pinned.ToList(),
    };

    public void Import(QuestSave s)
    {
        _progress.Clear();
        _done.Clear();
        _announcedReady.Clear();
        if (s == null)
            return;
        _pinned.Clear();
        _pinned.AddRange(s.Pinned ?? new());
        foreach (var id in s.Done)
            _done.Add(id);
        foreach (var (id, counts) in s.Progress)
        {
            var q = GameData.Quests.FirstOrDefault(x => x.Id == id);
            if (q != null)
                _progress[id] = counts.Length == q.Objectives.Count ? counts : new int[q.Objectives.Count];
        }
        Version++;
    }
}
