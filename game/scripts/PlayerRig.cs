using Godot;
using System.Collections.Generic;
using System.Linq;

// Animates a humanoid model (Models.BuildHumanoid) procedurally by its named joints: locomotion in any
// direction (walk/run/backpedal/strafe with knees and elbows), jump, roll, a four-swing sword combo,
// throw/thrust/cast/hurt, emotes and death. Used for the player, NPCs and the class-select preview.
public partial class PlayerRig : Node3D
{
    public enum Act { None, Swing1, Swing2, Swing3, Finisher, StabL, StabR, Throw, Thrust, Cast, Hurt, Roll }

    public float Yaw { get; set; }
    public string Emote { get; set; }
    public bool Crouch { get; set; }
    public bool Swim { get; set; }

    struct Pose
    {
        public float HipsY, TorsoX, TorsoY, TorsoZ, HeadX, HeadY;
        public float ArmLx, ArmLz, ArmRx, ArmRz, ElbowL, ElbowR;
        public float LegL, LegR, KneeL, KneeR, LegLz, LegRz;
    }

    Node3D _hips, _torso, _head, _armL, _armR, _legL, _legR, _kneeL, _kneeR, _elbowL, _elbowR, _lantern, _offhand, _weapon;
    float _lanternSwing, _lanternVel;
    readonly List<Node3D> _gear = new();
    Act _act;
    float _actTime, _actDur, _time, _phase, _deathTilt, _tintWeight, _spin;
    Vector3 _rollDir;
    Color _tint;
    readonly List<(StandardMaterial3D mat, Color color)> _mats = new();

    public bool Rolling => _act == Act.Roll;

    public void Build(ClassDef def) => Attach(Models.Player(def));

    public void Attach(Node3D model)
    {
        if (model.HasMeta("skinned"))
        {
            AttachSkinned(model);
            return;
        }
        AddChild(model);
        _hips = model.GetNode<Node3D>("Hips");
        _legL = _hips.GetNode<Node3D>("LegL");
        _legR = _hips.GetNode<Node3D>("LegR");
        _kneeL = _legL.GetNodeOrNull<Node3D>("Knee");
        _kneeR = _legR.GetNodeOrNull<Node3D>("Knee");
        _torso = _hips.GetNode<Node3D>("Torso");
        _head = _torso.GetNode<Node3D>("Head");
        _armL = _torso.GetNode<Node3D>("ArmL");
        _armR = _torso.GetNode<Node3D>("ArmR");
        _elbowL = _armL.GetNodeOrNull<Node3D>("Elbow");
        _elbowR = _armR.GetNodeOrNull<Node3D>("Elbow");
        _lantern = _hips.GetNodeOrNull<Node3D>("Lantern");
        _offhand = _elbowL?.GetNodeOrNull<Node3D>("Offhand");
        _weapon = _elbowR?.GetNodeOrNull<Node3D>("Weapon");
        TrackMaterials(model, _mats);
    }

    // Gives every tintable mesh its own material copy so tinting one model never affects another.
    public static void TrackMaterials(Node root, List<(StandardMaterial3D, Color)> into)
    {
        foreach (var node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            if (mi.Name.ToString().StartsWith("Glow") || mi.MaterialOverride is not StandardMaterial3D shared)
                continue;
            var own = (StandardMaterial3D)shared.Duplicate();
            mi.MaterialOverride = own;
            into.Add((own, own.AlbedoColor));
        }
    }

    // Actions override locomotion; a hurt flinch never interrupts another action.
    public void Play(Act act, float duration)
    {
        if (act == Act.Hurt && _act != Act.None && _act != Act.Hurt)
            return;
        _act = act;
        _actTime = 0f;
        _actDur = Mathf.Max(duration, 0.05f);
        _actSerial++;
        if (act != Act.Hurt)
            Emote = null;
    }

    // Tumble toward a world direction.
    public void PlayRoll(Vector3 worldDir, float duration)
    {
        _rollDir = worldDir.Normalized();
        Play(Act.Roll, duration);
    }

    public void SetTint(Color color, float weight)
    {
        _tint = color;
        _tintWeight = weight;
    }

    // localVel: x = rightward speed, y = forward speed (m/s) relative to the body's facing.
    public void Animate(float dt, Vector2 localVel, bool running, bool dead, bool airborne = false)
    {
        if (_skinned)
        {
            AnimateSkinned(dt, localVel, running, dead, airborne);
            return;
        }
        _time += dt;
        if (_act != Act.None)
        {
            _actTime += dt;
            if (_actTime >= _actDur)
                _act = Act.None;
        }
        float t = _act == Act.None ? 0f : _actTime / _actDur;

        _deathTilt = Mathf.Lerp(_deathTilt, dead ? -1.45f : 0f, 1f - Mathf.Exp(-6f * dt));
        _spin = _act == Act.Finisher ? Mathf.SmoothStep(0.15f, 0.75f, t) * Mathf.Tau : 0f;

        if (_act == Act.Roll)
        {
            // Rotate the whole body about its middle around the axis perpendicular to the roll.
            var center = new Vector3(0f, 0.55f, 0f);
            var axis = Vector3.Up.Cross(_rollDir);
            var roll = axis.LengthSquared() > 0.01f ? new Basis(axis.Normalized(), Mathf.SmoothStep(0f, 1f, t) * Mathf.Tau) : Basis.Identity;
            var basis = roll * new Basis(Vector3.Up, Yaw);
            Transform = new Transform3D(basis, center - roll * center);
        }
        else
            Transform = new Transform3D(Basis.FromEuler(new Vector3(_deathTilt, Yaw + _spin, 0f)), Vector3.Zero);

        var p = new Pose { HipsY = 0.95f };
        float speed = localVel.Length();
        if (airborne && !dead)
        {
            p.LegL = 0.7f; p.KneeL = -1.2f;
            p.LegR = -0.15f; p.KneeR = -0.5f;
            p.ArmLx = 0.5f; p.ArmRx = 0.5f; p.ArmLz = -0.5f; p.ArmRz = 0.5f;
            p.ElbowL = p.ElbowR = 0.6f;
        }
        else if (!dead && speed > 0.2f)
            Locomotion(ref p, dt, localVel, running);
        else if (!dead)
        {
            p.HipsY = 0.95f + Mathf.Sin(_time * 2f) * 0.012f;
            p.ArmLz = -0.06f;
            p.ArmRz = 0.06f;
            p.ElbowL = p.ElbowR = 0.15f;
            p.KneeL = p.KneeR = -0.05f;
            ApplyEmote(ref p);
        }
        if (Swim && !dead)
            SwimPose(ref p, speed, dt);
        else if (Crouch && !dead)
        {
            p.HipsY -= 0.36f;
            p.LegL += 0.75f; p.LegR += 0.75f;
            p.KneeL -= 1.1f; p.KneeR -= 1.1f;
            p.TorsoX -= 0.3f;
            p.HeadX += 0.2f;
        }
        ApplyAction(ref p, t);

        float k = 1f - Mathf.Exp(-(_act != Act.None ? 30f : 16f) * dt);
        _hips.Position = new Vector3(0f, Mathf.Lerp(_hips.Position.Y, p.HipsY, k), 0f);
        _torso.Rotation = _torso.Rotation.Lerp(new Vector3(p.TorsoX, p.TorsoY, p.TorsoZ), k);
        _head.Rotation = _head.Rotation.Lerp(new Vector3(p.HeadX, p.HeadY, 0f), k);
        _armL.Rotation = _armL.Rotation.Lerp(new Vector3(p.ArmLx, 0f, p.ArmLz), k);
        _armR.Rotation = _armR.Rotation.Lerp(new Vector3(p.ArmRx, 0f, p.ArmRz), k);
        _legL.Rotation = _legL.Rotation.Lerp(new Vector3(p.LegL, 0f, p.LegLz), k);
        _legR.Rotation = _legR.Rotation.Lerp(new Vector3(p.LegR, 0f, p.LegRz), k);
        if (_kneeL != null)
        {
            _kneeL.Rotation = _kneeL.Rotation.Lerp(new Vector3(p.KneeL, 0f, 0f), k);
            _kneeR.Rotation = _kneeR.Rotation.Lerp(new Vector3(p.KneeR, 0f, 0f), k);
        }
        if (_elbowL != null)
        {
            _elbowL.Rotation = _elbowL.Rotation.Lerp(new Vector3(p.ElbowL, 0f, 0f), k);
            _elbowR.Rotation = _elbowR.Rotation.Lerp(new Vector3(p.ElbowR, 0f, 0f), k);
        }

        // Belt lantern: a damped pendulum pushed by walking.
        if (_lantern != null)
        {
            float push = -localVel.Y * 0.04f + Mathf.Sin(_phase * 2f) * Mathf.Min(speed, 8f) * 0.012f;
            _lanternVel += (push - _lanternSwing * 14f - _lanternVel * 3f) * dt;
            _lanternSwing += _lanternVel * dt * 6f;
            _lantern.Rotation = new Vector3(Mathf.Clamp(_lanternSwing, -0.7f, 0.7f), 0f, Mathf.Sin(_time * 1.3f) * 0.05f);
        }

        foreach (var (mat, color) in _mats)
            mat.AlbedoColor = _tintWeight > 0f ? color.Lerp(new Color(_tint, color.A), _tintWeight) : color;
    }

    // Rotation conventions (character faces -Z): +X on a hanging limb swings it forward,
    // -X on a knee folds the shin back, +X on an elbow folds the forearm forward,
    // +Z moves a hanging arm toward +X (outward for the right arm, inward for the left).
    void Locomotion(ref Pose p, float dt, Vector2 v, bool running)
    {
        float speed = v.Length();
        float forward = v.Y / speed, side = v.X / speed;
        float dir = forward < -0.3f ? -1f : 1f; // backpedal plays the cycle in reverse
        _phase += dt * dir * (running ? 11.5f : 8.5f) * Mathf.Clamp(speed / 5.5f, 0.6f, 1.5f);
        float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);
        float stride = Mathf.Abs(forward) > 0.3f ? 1f : 0.45f;
        float amp = (running ? 0.9f : 0.5f) * stride;

        p.LegL = s * amp;
        p.LegR = -s * amp;
        // The knee folds while the leg swings through and stays soft on landing.
        float kneeAmp = running ? 1.5f : 0.7f;
        p.KneeL = -(Mathf.Max(0f, c * dir) * kneeAmp + (running ? 0.25f : 0.08f));
        p.KneeR = -(Mathf.Max(0f, -c * dir) * kneeAmp + (running ? 0.25f : 0.08f));
        // Sidestep: legs open and close sideways.
        p.LegLz = -side * Mathf.Max(0f, s) * 0.35f;
        p.LegRz = side * Mathf.Max(0f, -s) * 0.35f;

        float armAmp = running ? 0.9f : 0.45f;
        p.ArmLx = -s * armAmp * stride;
        p.ArmRx = s * armAmp * stride;
        p.ArmLz = -0.12f;
        p.ArmRz = 0.12f;
        p.ElbowL = running ? 1.25f + Mathf.Max(0f, s) * 0.3f : 0.3f;
        p.ElbowR = running ? 1.25f + Mathf.Max(0f, -s) * 0.3f : 0.3f;

        p.TorsoX = running ? -0.3f : -0.05f;
        if (dir < 0f)
            p.TorsoX = 0.08f;
        p.TorsoY = s * (running ? 0.16f : 0.08f) * stride;
        p.TorsoZ = -side * 0.12f;
        p.HeadX = -p.TorsoX * 0.6f;
        p.HeadY = -p.TorsoY;
        p.HipsY = 0.95f - (running ? 0.06f : 0.02f) + Mathf.Abs(c) * (running ? 0.09f : 0.035f);
    }

    // Treading water: breaststroke arms and a frog kick, faster while moving.
    void SwimPose(ref Pose p, float speed, float dt)
    {
        _phase += dt * (speed > 0.2f ? 7f : 3f);
        float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);
        p.HipsY = 0.95f + s * 0.03f;
        p.TorsoX = speed > 0.2f ? -0.45f : -0.15f;
        p.HeadX = -p.TorsoX * 0.8f;
        p.ArmLx = p.ArmRx = 1.5f + c * 0.25f;
        p.ArmLz = -0.4f - s * 0.6f;
        p.ArmRz = 0.4f + s * 0.6f;
        p.ElbowL = p.ElbowR = 0.5f + Mathf.Max(0f, -s) * 0.8f;
        p.LegL = p.LegR = -0.2f + Mathf.Max(0f, c) * 0.5f;
        p.KneeL = p.KneeR = -0.4f - Mathf.Max(0f, c) * 0.9f;
        p.LegLz = -0.25f * Mathf.Max(0f, s);
        p.LegRz = 0.25f * Mathf.Max(0f, s);
    }

    void ApplyEmote(ref Pose p)
    {
        switch (Emote)
        {
            case "wai": // ไหว้: palms together in front of the face, slight bow
                p.ArmLx = p.ArmRx = 0.9f;
                p.ElbowL = p.ElbowR = 1.9f;
                p.ArmLz = 0.35f;
                p.ArmRz = -0.35f;
                p.HeadX = -0.25f;
                p.TorsoX = -0.15f;
                break;
            case "wave":
                p.ArmRz = 2.4f;
                p.ElbowR = 0.6f + Mathf.Sin(_time * 9f) * 0.45f;
                p.HeadX = 0.1f;
                break;
            case "sweep": // both hands low on a broom, swishing side to side
                p.ArmLx = p.ArmRx = 0.7f;
                p.ArmLz = 0.3f + Mathf.Sin(_time * 2.4f) * 0.25f;
                p.ArmRz = -0.1f + Mathf.Sin(_time * 2.4f) * 0.25f;
                p.ElbowL = p.ElbowR = 0.4f;
                p.TorsoX = -0.3f;
                p.TorsoY = Mathf.Sin(_time * 2.4f) * 0.2f;
                break;
            case "fish": // sitting with a rod held out over the water
                p.HipsY = 0.3f;
                p.LegL = p.LegR = 1.5f;
                p.KneeL = p.KneeR = -1.5f;
                p.ArmLx = p.ArmRx = 1.0f + Mathf.Sin(_time * 0.7f) * 0.05f;
                p.ArmLz = 0.25f;
                p.ArmRz = -0.25f;
                p.ElbowL = p.ElbowR = 0.5f;
                break;
            case "farm": // bent over planting seedlings
                p.TorsoX = -0.95f;
                p.HeadX = 0.5f;
                p.LegL = 0.35f; p.LegR = -0.1f;
                p.KneeL = -0.5f; p.KneeR = -0.3f;
                p.ArmRx = 1.2f + Mathf.Max(0f, Mathf.Sin(_time * 1.8f)) * 0.5f;
                p.ArmLx = 0.9f;
                p.ElbowR = 0.3f;
                p.HipsY = 0.85f;
                break;
            case "sell": // calling out to passers-by, now and then waving them over
                p.ArmLx = 0.6f; p.ElbowL = 1.4f;
                if (Mathf.Sin(_time * 0.6f) > 0.6f)
                {
                    p.ArmRz = 2.2f;
                    p.ElbowR = 0.5f + Mathf.Sin(_time * 8f) * 0.4f;
                }
                break;
            case "chat": // talking with the hands
                p.ArmRx = 0.6f + Mathf.Sin(_time * 2.2f) * 0.3f;
                p.ElbowR = 1.2f;
                p.ArmLx = 0.4f + Mathf.Sin(_time * 1.7f + 1f) * 0.2f;
                p.ElbowL = 1f;
                p.HeadY = Mathf.Sin(_time * 0.9f) * 0.2f;
                break;
            case "sitchair": // sitting on a bench or stool, feet on the ground
                p.HipsY = 0.6f;
                p.LegL = p.LegR = 1.45f;
                p.KneeL = p.KneeR = -1.45f;
                p.TorsoX = 0.05f;
                p.ArmLx = p.ArmRx = 0.35f;
                p.ElbowL = p.ElbowR = 0.7f;
                p.HeadY = Mathf.Sin(_time * 0.5f) * 0.25f;
                break;
            case "sit":
                p.HipsY = 0.3f;
                p.LegL = p.LegR = 1.5f;
                p.KneeL = p.KneeR = -1.5f;
                p.TorsoX = 0.1f;
                p.ArmLx = p.ArmRx = 0.4f;
                p.ElbowL = p.ElbowR = 0.6f;
                break;
        }
    }

    void ApplyAction(ref Pose p, float t)
    {
        if (_act == Act.None)
            return;
        float bell = Mathf.Sin(t * Mathf.Pi);
        float hit = 1f - Mathf.Pow(1f - Mathf.Clamp((t - 0.3f) / 0.7f, 0f, 1f), 3f); // fast strike after a wind-up
        float wind = Mathf.Clamp(t / 0.3f, 0f, 1f);
        switch (_act)
        {
            case Act.Swing1: // raise, then chop diagonally across
                p.ArmRx = t < 0.3f ? Mathf.Lerp(0f, 2.6f, wind) : Mathf.Lerp(2.6f, 0.5f, hit);
                p.ArmRz = t < 0.3f ? 0.3f * wind : Mathf.Lerp(0.3f, -0.4f, hit);
                p.ElbowR = t < 0.3f ? 0.6f * wind : Mathf.Lerp(0.6f, 0.1f, hit);
                p.TorsoY = t < 0.3f ? 0.35f * wind : Mathf.Lerp(0.35f, -0.45f, hit);
                p.TorsoX -= 0.1f;
                p.LegL = 0.35f; p.KneeL = -0.3f; p.LegR = -0.25f;
                break;
            case Act.Swing2: // horizontal sweep from the left side to the right
                p.ArmRx = 1.45f;
                p.ArmRz = t < 0.3f ? Mathf.Lerp(0f, -0.9f, wind) : Mathf.Lerp(-0.9f, 1.2f, hit);
                p.ElbowR = t < 0.3f ? 0.9f * wind : Mathf.Lerp(0.9f, 0.05f, hit);
                p.TorsoY = t < 0.3f ? Mathf.Lerp(0f, 0.6f, wind) : Mathf.Lerp(0.6f, -0.7f, hit);
                p.LegL = 0.3f; p.KneeL = -0.4f; p.LegR = -0.3f; p.KneeR = -0.2f;
                p.HipsY = 0.9f;
                break;
            case Act.Swing3: // crouch, then a rising slash
                p.ArmRx = t < 0.3f ? Mathf.Lerp(0f, -0.4f, wind) : Mathf.Lerp(-0.4f, 2.9f, hit);
                p.ArmRz = 0.2f;
                p.ElbowR = t < 0.3f ? 0.8f * wind : Mathf.Lerp(0.8f, 0f, hit);
                p.TorsoX = t < 0.3f ? -0.35f * wind : Mathf.Lerp(-0.35f, 0.2f, hit);
                p.HipsY = t < 0.3f ? 0.95f - 0.18f * wind : Mathf.Lerp(0.77f, 1f, hit);
                p.LegL = p.LegR = t < 0.3f ? 0.5f * wind : 0.1f;
                p.KneeL = p.KneeR = t < 0.3f ? -0.9f * wind : -0.1f;
                break;
            case Act.Finisher: // hop and spin with the blade held out (the body spin is added in Animate)
                p.ArmRx = 1.55f;
                p.ArmRz = 1.25f;
                p.ElbowR = 0f;
                p.ArmLx = 0.4f;
                p.ArmLz = -1f;
                p.HipsY = 0.95f + bell * 0.35f;
                p.LegL = p.LegR = 0.5f * bell;
                p.KneeL = p.KneeR = -1.1f * bell;
                p.TorsoX = -0.2f;
                break;
            case Act.StabR: // quick forward stab with the right dagger
                p.ArmRx = t < 0.3f ? Mathf.Lerp(0.2f, 0.5f, wind) : Mathf.Lerp(0.5f, 1.55f, hit);
                p.ElbowR = t < 0.3f ? 1.6f : Mathf.Lerp(1.6f, 0f, hit);
                p.TorsoY = -0.35f * bell;
                p.LegR = 0.4f; p.KneeR = -0.3f; p.LegL = -0.2f;
                break;
            case Act.StabL: // and with the left
                p.ArmLx = t < 0.3f ? Mathf.Lerp(0.2f, 0.5f, wind) : Mathf.Lerp(0.5f, 1.55f, hit);
                p.ElbowL = t < 0.3f ? 1.6f : Mathf.Lerp(1.6f, 0f, hit);
                p.TorsoY = 0.35f * bell;
                p.LegL = 0.4f; p.KneeL = -0.3f; p.LegR = -0.2f;
                break;
            case Act.Throw: // wind back, then fling forward
                p.ArmRx = t < 0.35f ? Mathf.Lerp(0f, -0.9f, t / 0.35f) : Mathf.Lerp(-0.9f, 1.8f, (t - 0.35f) / 0.65f);
                p.ElbowR = t < 0.35f ? 1.2f : 0.1f;
                p.TorsoY = t < 0.35f ? 0.3f : -0.3f;
                break;
            case Act.Thrust: // pull back, then lunge with the blade level
                if (t < 0.25f)
                {
                    p.ArmRx = Mathf.Lerp(0f, 0.6f, t / 0.25f);
                    p.ElbowR = 1.4f;
                    p.TorsoY = 0.5f * t / 0.25f;
                }
                else
                {
                    p.ArmRx = 1.5f;
                    p.ElbowR = 0f;
                    p.TorsoY = -0.2f;
                    p.TorsoX = -0.45f;
                    p.LegL = 0.8f; p.KneeL = -0.6f;
                    p.LegR = -0.6f; p.KneeR = -0.2f;
                }
                break;
            case Act.Cast: // both arms up
                p.ArmLx = p.ArmRx = 2.4f * bell;
                p.ArmLz = 0.3f * bell;
                p.ArmRz = -0.3f * bell;
                p.HeadX = 0.25f * bell;
                break;
            case Act.Hurt:
                p.TorsoX += 0.4f * bell;
                p.HeadX += 0.3f * bell;
                break;
            case Act.Roll: // tucked into a ball
                p.HipsY = 0.75f;
                p.LegL = p.LegR = 1.6f;
                p.KneeL = p.KneeR = -2.2f;
                p.ArmLx = p.ArmRx = 1.1f;
                p.ElbowL = p.ElbowR = 1.6f;
                p.TorsoX = -0.9f;
                p.HeadX = -0.5f;
                break;
        }
    }

    // Shows equipped gear on the model: helmet, armour, trousers, boots and the off-hand item,
    // plus a glow on high-rarity or highly upgraded weapons.
    public void ApplyEquipment(ClassDef def, Dictionary<string, ItemStack> equipped)
    {
        if (_skinned)
        {
            ApplyEquipmentSkinned(def, equipped);
            return;
        }
        foreach (var n in _gear)
            if (IsInstanceValid(n))
                n.QueueFree();
        _gear.Clear();
        _mats.RemoveAll(m => m.mat.HasMeta("gear"));

        Node3D Attach(Node3D parent, string name)
        {
            var g = new Node3D { Name = name };
            parent.AddChild(g);
            _gear.Add(g);
            return g;
        }
        Color ColorOf(ItemStack s) => s.Def.Color != null ? Color.FromHtml(s.Def.Color) : GameData.RarityColor(s.Def.Rarity);

        // Worn gear replaces the body part it covers instead of sitting on top of it.
        static void Show(Node3D joint, bool visible, params string[] names)
        {
            if (joint == null)
                return;
            foreach (var n in names)
                if (joint.GetNodeOrNull<Node3D>(n) is { } part)
                    part.Visible = visible;
        }
        bool hasHelmet = equipped.ContainsKey("helmet"), hasArmor = equipped.ContainsKey("armor");
        bool hasPants = equipped.ContainsKey("pants"), hasBoots = equipped.ContainsKey("boots");
        Show(_head, !hasHelmet, "Hood", "HoodTip", "Headband", "HeadbandTail", "Hair", "Bun", "Cap", "Visor", "Ngob");
        Show(_torso, !hasArmor, "Chest", "PauldronL", "PauldronR");
        Show(_torso, !hasPants, "Robe"); // the long skirt would hide the trousers
        foreach (var leg in new[] { _legL, _legR })
            Show(leg, !hasPants, "Thigh");
        foreach (var knee in new[] { _kneeL, _kneeR })
        {
            Show(knee, !hasPants, "Shin");
            Show(knee, !hasBoots, "Foot");
        }
        // Bare hands when no weapon is worn; the class's own off-hand dagger only with one equipped.
        if (_weapon != null)
            foreach (var child in _weapon.GetChildren().OfType<Node3D>())
                child.Visible = equipped.ContainsKey("weapon");
        if (_offhand != null)
            foreach (var child in _offhand.GetChildren().OfType<Node3D>())
                if (!child.Name.ToString().StartsWith("Gear"))
                    child.Visible = equipped.TryGetValue("offhand", out var oh) && oh.Def.OffhandType != "shield";

        if (equipped.TryGetValue("helmet", out var helmet))
        {
            var g = Attach(_head, "GearHelmet");
            var c = ColorOf(helmet);
            if (helmet.Def.Set == "pha_yan")
            {
                Shapes.Cylinder(g, new Vector3(0f, 0.3f, 0f), 0.2f, 0.215f, 0.14f, c, 0f, "Wrap");
                Shapes.Box(g, new Vector3(0f, 0.3f, -0.21f), new Vector3(0.1f, 0.1f, 0.02f), new Color(0.7f, 0.15f, 0.1f), 0f, "Yan");
            }
            else
            {
                var dome = Shapes.Sphere(g, new Vector3(0f, 0.27f, 0.02f), 0.225f, c, 0f, "Dome");
                dome.Scale = new Vector3(1f, 0.75f, 1f);
                Shapes.Cylinder(g, new Vector3(0f, 0.2f, 0f), 0.235f, 0.235f, 0.05f, c.Darkened(0.2f), 0f, "Brim");
            }
        }
        if (equipped.TryGetValue("armor", out var armor))
        {
            var g = Attach(_torso, "GearArmor");
            var c = ColorOf(armor);
            Shapes.Box(g, new Vector3(0f, 0.36f, 0f), new Vector3(0.56f, 0.58f, 0.34f), c, 0f, "Plate");
            Shapes.Box(g, new Vector3(-0.33f, 0.65f, 0f), new Vector3(0.24f, 0.1f, 0.36f), c.Darkened(0.15f), 0f, "ShoulderL");
            Shapes.Box(g, new Vector3(0.33f, 0.65f, 0f), new Vector3(0.24f, 0.1f, 0.36f), c.Darkened(0.15f), 0f, "ShoulderR");
            if (armor.Def.Set == "pha_yan")
                Shapes.Box(g, new Vector3(0f, 0.4f, -0.175f), new Vector3(0.22f, 0.26f, 0.01f), new Color(0.7f, 0.15f, 0.1f), 0f, "Yan");
        }
        if (equipped.TryGetValue("pants", out var pants))
            foreach (var leg in new[] { _legL, _legR })
            {
                var g = Attach(leg, "GearPants");
                Shapes.Box(g, new Vector3(0f, -0.23f, 0f), new Vector3(0.2f, 0.46f, 0.21f), ColorOf(pants), 0f, "Thigh");
                var kneeGear = Attach(leg.GetNodeOrNull<Node3D>("Knee") ?? leg, "GearShin");
                Shapes.Box(kneeGear, new Vector3(0f, -0.19f, 0f), new Vector3(0.18f, 0.36f, 0.19f), ColorOf(pants).Darkened(0.1f), 0f, "Shin");
            }
        if (equipped.TryGetValue("boots", out var boots))
            foreach (var leg in new[] { _kneeL, _kneeR })
            {
                if (leg == null)
                    continue;
                var g = Attach(leg, "GearBoot");
                Shapes.Box(g, new Vector3(0f, -0.4f, -0.04f), new Vector3(0.19f, 0.18f, 0.31f), ColorOf(boots), 0f, "Boot");
            }
        if (_offhand != null && equipped.TryGetValue("offhand", out var off))
        {
            var g = Attach(_offhand, "GearOffhand");
            if (off.Def.OffhandType == "shield")
                Models.Shield(g, ColorOf(off).Darkened(0.2f), new Color(0.35f, 0.25f, 0.15f));
            else if (def.Offhand != "dagger")
                Models.Dagger(g);
        }
        // Weapon glow by rarity and upgrade level.
        if (_weapon != null && equipped.TryGetValue("weapon", out var weapon) && (weapon.Def.Rarity >= 2 || weapon.Plus >= 4))
        {
            var g = Attach(_weapon, "GearGlow");
            var glow = GameData.RarityColor(weapon.Def.Rarity);
            Shapes.Light(g, new Vector3(0f, 0f, -0.5f), glow, 0.6f + weapon.Plus * 0.15f, 2.5f, "GlowLight");
        }
        foreach (var g in _gear)
        {
            var before = _mats.Count;
            TrackMaterials(g, _mats);
            for (int i = before; i < _mats.Count; i++)
                _mats[i].mat.SetMeta("gear", true);
        }
    }
}
