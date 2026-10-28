using Godot;

// Random rain. Every Config.RainRollHours in-game hours it rolls against the current zone's rainChance;
// rain fades in and out over ~20 s and drives the rain particles around the camera, the sky and fog
// (DayNight.Overcast), the water ripples and the rain sound. Settings.RainMode can force it for testing.
public partial class Weather : Node
{
    public float Intensity { get; private set; }
    public bool Raining => _target > 0.5f;

    float _target, _hoursUntilRoll;
    GpuParticles3D _rain;
    readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _rng.Randomize();
        _hoursUntilRoll = GameData.Config.RainRollHours;
        var drop = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.75f, 0.82f, 0.95f, 0.35f),
        };
        _rain = new GpuParticles3D
        {
            Amount = 1600,
            Lifetime = 1.1f,
            Emitting = false,
            VisibilityAabb = new Aabb(new Vector3(-20f, -16f, -20f), new Vector3(40f, 34f, 40f)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(18f, 1f, 18f),
                Direction = new Vector3(0.08f, -1f, 0f),
                Spread = 2f,
                InitialVelocityMin = 20f,
                InitialVelocityMax = 24f,
                Gravity = new Vector3(0f, -10f, 0f),
                ParticleFlagAlignY = true,
            },
            DrawPass1 = new BoxMesh { Size = new Vector3(0.012f, 0.55f, 0.012f), Material = drop },
        };
        AddChild(_rain);
    }

    public void Roll()
    {
        var zone = Game.Main.CurrentZone;
        _target = _rng.Randf() < (zone?.RainChance ?? 0f) ? _rng.RandfRange(0.6f, 1f) : 0f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _hoursUntilRoll -= dt * GameData.Config.MinutesPerSecond * Settings.TimeScale / 60f;
        if (_hoursUntilRoll <= 0f)
        {
            _hoursUntilRoll = GameData.Config.RainRollHours;
            Roll();
        }
        float target = Settings.RainMode switch { 1 => 1f, 2 => 0f, _ => _target };
        Intensity = Mathf.MoveToward(Intensity, target, dt / 20f * (Settings.RainMode != 0 ? 6f : 1f));

        var cam = GetViewport().GetCamera3D();
        if (cam != null)
            _rain.GlobalPosition = cam.GlobalPosition + new Vector3(0f, 12f, 0f);
        _rain.Emitting = Intensity > 0.05f;
        _rain.AmountRatio = Mathf.Clamp(Intensity, 0.05f, 1f);
        Game.DayNight.Overcast = Intensity;
        Audio.SetRain(Intensity);
        foreach (var node in GetTree().GetNodesInGroup("water_surface"))
            if (node is MeshInstance3D { MaterialOverride: ShaderMaterial m })
                m.SetShaderParameter("rain", Intensity);
    }
}
