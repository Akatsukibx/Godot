using Godot;
using System.Linq;

// Works out where the tracked quest wants the player to go: the NPC to hand it in to, the person to
// talk to, the nearest ghost (or haunt) to deal with, a shrine to bind, or the zone to reach.
public static class QuestNav
{
    public readonly record struct Target(Vector3 Position, string Label, QuestDef Quest);

    public static Target? Find(Player p, Main main)
    {
        var log = p.Quests;
        var quest = log.Tracked(p);
        if (quest == null)
            return null;
        if (log.StateOf(quest, p) == QuestLog.State.Ready)
            return NpcTarget(main, quest.TurnInNpc, quest, $"ส่งเควสที่{GameData.NpcName(quest.TurnInNpc)}");

        for (int i = 0; i < quest.Objectives.Count; i++)
        {
            var o = quest.Objectives[i];
            if (log.Progress(quest, i, p) >= o.Count)
                continue;
            switch (o.Type)
            {
                case "talk":
                    return NpcTarget(main, o.Target, quest, $"คุยกับ{GameData.NpcName(o.Target)}");
                case "bind":
                    var shrine = p.GetTree().GetNodesInGroup("shrine").OfType<Node3D>().OrderBy(s => s.GlobalPosition.DistanceTo(p.GlobalPosition)).FirstOrDefault();
                    return shrine == null ? null : new Target(shrine.GlobalPosition, $"{shrine.GetMeta("shrine_name")}", quest);
                case "reach":
                    var zone = GameData.Zone(o.Target);
                    if (zone == null)
                        return null;
                    var pp = p.GlobalPosition;
                    var edge = new Vector3(
                        Mathf.Clamp(pp.X, zone.CenterX - zone.SizeX * 0.5f + 2f, zone.CenterX + zone.SizeX * 0.5f - 2f), 0f,
                        Mathf.Clamp(pp.Z, zone.CenterZ - zone.SizeZ * 0.5f + 2f, zone.CenterZ + zone.SizeZ * 0.5f - 2f));
                    return new Target(edge, zone.Name, quest);
                case "banish":
                case "release":
                case "resolve":
                    return GhostTarget(p, o.Target, quest);
                default:
                    return null; // craft / collect / level: nothing to walk to
            }
        }
        return null;
    }

    static Target? NpcTarget(Main main, string npcId, QuestDef q, string label)
    {
        var npc = main.Npcs.FirstOrDefault(n => n.Def.Id == npcId);
        return npc == null ? null : new Target(npc.GlobalPosition, label, q);
    }

    // The nearest live ghost of that kind, otherwise where that kind usually appears.
    static Target? GhostTarget(Player p, string ghostId, QuestDef q)
    {
        string name = ghostId == "any" ? "ผี" : GameData.GhostName(ghostId);
        var ghost = Ghost.All.Where(g => !g.Gone && (ghostId == "any" || g.Def.Id == ghostId))
            .OrderBy(g => g.GlobalPosition.DistanceTo(p.GlobalPosition)).FirstOrDefault();
        if (ghost != null)
            return new Target(ghost.GlobalPosition, name, q);
        var zone = GameData.Zones.FirstOrDefault(z => z.Scene != null && (ghostId == "any" ? z.Contains(p.GlobalPosition) : z.Ghosts.Contains(ghostId)));
        if (zone == null)
            return null;
        string group = zone.AlleyOnly ? "alley_spawn" : ghostId == "hun_lai_ka" ? "map_plot" : null;
        if (group != null)
        {
            var spot = p.GetTree().GetNodesInGroup(group).OfType<Node3D>().Where(n => zone.Contains(n.GlobalPosition))
                .OrderBy(n => n.GlobalPosition.DistanceTo(p.GlobalPosition)).FirstOrDefault();
            if (spot != null)
                return new Target(spot.GlobalPosition, $"ที่อยู่ของ{name}", q);
        }
        return new Target(zone.Center + new Vector3(0f, 0f, -zone.SizeZ * 0.15f), $"{zone.Name} ({name})", q);
    }
}
