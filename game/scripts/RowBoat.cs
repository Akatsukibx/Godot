using Godot;

// A rowing boat (เรือพาย) moored at a jetty. Board it with the interact key, row with W/S and steer
// with A/D, and step off onto any bank or pier within reach. It only floats where the water is deep
// enough, slips under the arched boardwalks and stops at the flat ones. Left alone away from its
// jetty, it drifts back to its mooring.
public partial class RowBoat : Node3D
{
    const float MaxSpeed = 5.5f, Accel = 4f, TurnRate = 1.6f, MinDepth = 0.55f;

    public Player Rider { get; private set; }
    public float DevRow; // dev (--boat=seconds): row ahead on its own
    Transform3D _home;
    float _speed, _yaw, _bob, _idle, _stroke;
    Node3D _hull, _oar;

    public static RowBoat Moor(Node3D dock)
    {
        var boat = new RowBoat { Name = $"RowBoat_{dock.GetMeta("dock_id")}" };
        Game.Main.AddChild(boat);
        boat._home = dock.GlobalTransform;
        boat.Reset();
        boat._hull = Shapes.Group(boat, "Hull");
        WorldBuilder.Boat(boat._hull, "RowingBoat", Vector3.Zero, 0f, true);
        boat._oar = boat._hull.FindChild("Paddle", true, false) as Node3D;
        boat.AddToGroup("rowboat");
        Textures.Apply(boat);
        return boat;
    }

    void Reset()
    {
        GlobalTransform = _home;
        _yaw = _home.Basis.GetEuler().Y;
        _speed = 0f;
    }

    public Vector3 Seat => GlobalPosition + GlobalBasis * new Vector3(0f, 0.12f, 0.7f);

    public float Yaw => _yaw;

    public void Board(Player p)
    {
        Rider = p;
        _idle = 0f;
        p.EnterBoat(this);
        Game.Toast($"W/S พาย · A/D เลี้ยว · [{Settings.BindingText("interact")}] ขึ้นฝั่ง", "quest", 5f);
    }

    // Steps the rider off onto the nearest bank or deck beside the boat; false when there is none in reach.
    public bool TryLeave()
    {
        var space = GetWorld3D().DirectSpaceState;
        Vector3? best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.Tau;
            for (float r = 1.6f; r <= 3.6f; r += 1f)
            {
                var p = GlobalPosition + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 4f, p + Vector3.Down * 4f, Layers.World));
                if (hit.Count == 0)
                    continue;
                var at = (Vector3)hit["position"];
                float surface = Game.Main.WaterSurfaceAt(at) ?? at.Y - 1f;
                if (at.Y < surface - 0.25f || at.Y > GlobalPosition.Y + 1.4f)
                    continue;
                float score = r - at.Y;
                if (score < bestScore)
                    (bestScore, best) = (score, at);
            }
        }
        if (best is not Vector3 land)
            return false;
        var rider = Rider;
        Rider = null;
        _speed = 0f;
        rider.LeaveBoat(land + Vector3.Up * 0.3f);
        return true;
    }

    // The rider was moved away by something else (death, warp): just let go of them.
    public void Eject()
    {
        Rider = null;
        _speed = 0f;
        _idle = 0f;
    }

    public override void _ExitTree()
    {
        // Zone unloaded or teleported away while aboard: put the rider back on their feet.
        if (Rider != null && IsInstanceValid(Rider) && Rider.IsInsideTree())
            Rider.LeaveBoat(Rider.GlobalPosition + Vector3.Up * 0.5f);
        Rider = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _bob += dt;
        float surface = Game.Main.WaterSurfaceAt(GlobalPosition) ?? _home.Origin.Y;

        if (Rider == null)
        {
            _speed = Mathf.MoveToward(_speed, 0f, Accel * dt);
            // Abandoned away from the jetty with nobody near: back to the mooring.
            _idle += dt;
            var player = Game.Main.Player;
            if (_idle > 20f && GlobalPosition.DistanceTo(_home.Origin) > 3f && (player == null || player.GlobalPosition.DistanceTo(GlobalPosition) > 40f))
                Reset();
        }
        else
        {
            bool controls = Game.Main.ControlsEnabled;
            float fwd = controls ? Input.GetAxis("move_down", "move_up") : 0f;
            if (DevRow > 0f)
            {
                DevRow -= dt;
                fwd = 1f;
            }
            float turn = controls ? Input.GetAxis("move_right", "move_left") : 0f;
            _speed = Mathf.MoveToward(_speed, fwd * MaxSpeed * (fwd < 0f ? 0.5f : 1f), Accel * dt);
            _yaw += turn * TurnRate * dt * (0.4f + 0.6f * Mathf.Min(1f, Mathf.Abs(_speed) / 2f));
            if (Mathf.Abs(fwd) > 0.1f || Mathf.Abs(turn) > 0.1f)
                _stroke += dt * 4f;
        }

        var forward = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
        var step = forward * _speed * dt;
        if (step.LengthSquared() > 1e-6f)
        {
            var next = GlobalPosition + step;
            if (CanFloat(next, surface, Mathf.Sign(_speed)) && !Hits(forward * Mathf.Sign(_speed), surface))
                GlobalPosition = next;
            else
                _speed = 0f;
        }
        GlobalPosition = GlobalPosition with { Y = surface - 0.08f + Mathf.Sin(_bob * 1.7f) * 0.03f };
        GlobalBasis = Basis.FromEuler(new Vector3(Mathf.Sin(_bob * 1.3f) * 0.02f, _yaw, Mathf.Sin(_bob * 1.1f) * 0.025f));
        if (_oar != null)
            _oar.Rotation = new Vector3(Mathf.Sin(_stroke) * 0.5f, 0.15f + Mathf.Cos(_stroke) * 0.4f, 0f);
        if (Rider != null && Mathf.Abs(_speed) > 1f && ((int)(_bob * 3f) % 2 == 0) && ((int)((_bob - dt) * 3f) % 2 != 0))
            Fx.Ring(GetParent(), GlobalPosition - forward * 2f, 1.4f, new Color(0.8f, 0.9f, 1f), 0.7f);
    }

    // Enough water under the middle and the leading end (so a boat aground at one end can still back off), measured below any deck overhead.
    bool CanFloat(Vector3 at, float surface, float heading)
    {
        if (Game.Main.WaterSurfaceAt(at) == null)
            return false;
        var space = GetWorld3D().DirectSpaceState;
        var forward = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
        foreach (var o in new[] { 0f, 2f * heading })
        {
            var p = at + forward * o;
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(p.X, surface + 0.25f, p.Z), new Vector3(p.X, surface - 6f, p.Z), Layers.World));
            if (hit.Count > 0 && surface - ((Vector3)hit["position"]).Y < MinDepth)
                return false;
        }
        return true;
    }

    // A flat pier or a wall in the way at gunwale height (arched spans pass overhead).
    bool Hits(Vector3 dir, float surface)
    {
        var space = GetWorld3D().DirectSpaceState;
        var right = new Vector3(-dir.Z, 0f, dir.X);
        foreach (var s in new[] { -0.5f, 0f, 0.5f })
        foreach (var h in new[] { 0.42f, 1.3f })
        {
            var from = GlobalPosition with { Y = surface + h } + right * s;
            var q = PhysicsRayQueryParameters3D.Create(from, from + dir * 2.6f, Layers.World);
            q.HitBackFaces = true;
            if (space.IntersectRay(q).Count > 0)
                return true;
        }
        return false;
    }
}
