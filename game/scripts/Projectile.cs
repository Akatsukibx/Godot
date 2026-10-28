using Godot;

// A thrown talisman (ยันต์) that damages the first ghost it touches.
public partial class Projectile : Area3D
{
    Vector3 _dir;
    float _speed, _range, _travelled, _damage;
    string _element;
    bool _hit;

    public void Setup(Vector3 dir, float speed, float range, float damage, string element)
    {
        _dir = dir;
        _speed = speed;
        _range = range;
        _damage = damage;
        _element = element;

        CollisionLayer = 0;
        CollisionMask = Layers.Ghost;
        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.4f } });

        var paper = Shapes.Box(this, Vector3.Zero, new Vector3(0.22f, 0.32f, 0.03f), new Color(1f, 0.8f, 0.35f), 2.5f);
        paper.Rotation = new Vector3(0f, Mathf.Atan2(-dir.X, -dir.Z), 0f);
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.25f), LightEnergy = 1.2f, OmniRange = 3f });

        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 step = _dir * _speed * (float)delta;
        GlobalPosition += step;
        _travelled += step.Length();
        if (_travelled >= _range)
            QueueFree();
    }

    void OnBodyEntered(Node3D body)
    {
        if (_hit || body is not Ghost ghost || ghost.Weakened || ghost.Gone)
            return;
        _hit = true;
        ghost.TakeDamage(_damage, _element);
        QueueFree();
    }
}
