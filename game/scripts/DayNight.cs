using Godot;

// Game clock and lighting. By default 1 real second = 1 game minute, so a full day is 24 real minutes;
// day runs 06:00–18:00 and night 18:00–06:00 like the real world. Settings.TimeScale speeds it up for testing.
// At night the directional light becomes moonlight and the sky shader draws the moon where it shines from.
public partial class DayNight : Node
{
    public float Hour { get; private set; }
    public bool IsNight { get; private set; }
    // 0 in full daylight, 1 in full night; eases through twilight.
    public float NightFactor { get; private set; }
    // 0 = clear, 1 = heavy rain clouds (set by Weather).
    public float Overcast { get; set; }
    public event System.Action<bool> PhaseChanged;

    DirectionalLight3D _sun;
    Godot.Environment _env;
    ShaderMaterial _sky;

    static GameConfig Cfg => GameData.Config;

    public static ShaderMaterial CreateSkyMaterial() => new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type sky;
uniform vec3 top_color : source_color;
uniform vec3 horizon_color : source_color;
uniform vec3 ground_color : source_color;
uniform float night = 0.0;
uniform float overcast = 0.0;
uniform vec3 moon_dir = vec3(0.25, 0.2, -0.95);

float hash(vec3 p) {
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(vec3(i, 1.0)), b = hash(vec3(i + vec2(1, 0), 1.0));
    float c = hash(vec3(i + vec2(0, 1), 1.0)), d = hash(vec3(i + vec2(1, 1), 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
    return v;
}

void sky() {
    vec3 d = EYEDIR;
    float h = d.y;
    vec3 col = h > 0.0
        ? mix(horizon_color, top_color, pow(clamp(h, 0.0, 1.0), 0.45))
        : mix(horizon_color, ground_color, clamp(-h * 5.0, 0.0, 1.0));

    if (h > 0.0) {
        // Stars
        vec3 cell = floor(d * 260.0);
        float s = hash(cell);
        float star = step(0.9972, s) * night * smoothstep(0.02, 0.3, h) * (1.0 - overcast);
        col += vec3(0.9, 0.95, 1.0) * star * (0.55 + 0.45 * sin(TIME * 1.7 + s * 80.0));

        // Wispy clouds lit by the moon at night, warm by day
        vec2 uv = d.xz / (h + 0.18) * 1.6 + vec2(TIME * 0.004, 0.0);
        float cl = smoothstep(0.5 - overcast * 0.45, 0.85 - overcast * 0.3, fbm(uv)) * smoothstep(0.0, 0.25, h);
        vec3 cloudCol = mix(vec3(0.95, 0.9, 0.85), vec3(0.22, 0.26, 0.38), night);
        col = mix(col, cloudCol * (1.0 - overcast * 0.45), cl * (0.7 + overcast * 0.3));
    }

    // Sun by day where the light shines from; by night a large moon low over the lagoon so the camera sees it.
    if (LIGHT0_ENABLED) {
        float m = dot(d, normalize(moon_dir));
        float sm = dot(d, LIGHT0_DIRECTION);
        float disc = smoothstep(0.9988, 0.99915, m);
        float halo = pow(max(m, 0.0), 300.0) * 0.6 + pow(max(m, 0.0), 24.0) * 0.12;
        vec3 moon = vec3(1.0, 0.97, 0.86) * (disc * 2.2) + vec3(0.55, 0.65, 1.0) * halo;
        // Faint moon craters
        moon *= mix(1.0, 0.82, disc * smoothstep(0.45, 0.7, fbm(d.xy * 400.0)));
        vec3 sun = vec3(1.0, 0.9, 0.7) * (smoothstep(0.9993, 0.9996, sm) * 6.0 + pow(max(sm, 0.0), 300.0) * 0.9);
        col += mix(sun, moon, night) * (1.0 - overcast * 0.85);
    }
    COLOR = col;
}",
        },
    };

    public void Setup(DirectionalLight3D sun, Godot.Environment env, ShaderMaterial sky)
    {
        _sun = sun;
        _env = env;
        _sky = sky;
        SetHour(Cfg.StartHour);
    }

    public void SetHour(float hour)
    {
        Hour = Mathf.PosMod(hour, 24f);
        bool night = NightAt(Hour);
        if (night != IsNight)
        {
            IsNight = night;
            PhaseChanged?.Invoke(night);
        }
        Apply();
    }

    static bool NightAt(float hour) => hour < Cfg.DayStartHour || hour >= Cfg.NightStartHour;

    public override void _Process(double delta)
    {
        if (_sun == null)
            return;
        SetHour(Hour + (float)delta * Cfg.MinutesPerSecond * Settings.TimeScale / 60f);
    }

    // 0..1 progress of the sun across the day; outside daytime it is clamped.
    float SunProgress() => (Hour - Cfg.DayStartHour) / (Cfg.NightStartHour - Cfg.DayStartHour);

    void Apply()
    {
        float p = SunProgress();
        float elevation = p is >= 0f and <= 1f ? Mathf.Sin(p * Mathf.Pi) : -0.3f;
        // Twilight starts a little before sunrise and ends a little after sunset.
        float k = Mathf.SmoothStep(-0.05f, 0.3f, elevation);
        float pc = Mathf.Clamp(p, 0f, 1f);
        NightFactor = 1f - k;

        // The moon sits low over the lagoon (north) so the third-person camera can see it.
        var sunRot = new Vector3(-12f - 58f * Mathf.Sin(pc * Mathf.Pi), 40f + 100f * pc, 0f);
        var moonRot = new Vector3(-24f, 160f, 0f);
        _sun.RotationDegrees = moonRot.Lerp(sunRot, k);
        var dawnTint = new Color(1f, 0.7f, 0.45f).Lerp(new Color(1f, 0.95f, 0.85f), Mathf.Clamp(elevation * 3f, 0f, 1f));
        _sun.LightColor = new Color(0.5f, 0.6f, 0.95f).Lerp(dawnTint, k);
        _sun.LightEnergy = Mathf.Lerp(0.45f, 1.15f, k) * (1f - Overcast * 0.55f);

        _env.AmbientLightColor = new Color(0.2f, 0.25f, 0.45f).Lerp(new Color(0.72f, 0.74f, 0.7f), k);
        _env.AmbientLightEnergy = Mathf.Lerp(0.85f, 0.9f, k);
        _env.FogLightColor = new Color(0.06f, 0.08f, 0.16f).Lerp(new Color(0.78f, 0.82f, 0.85f), k);
        _env.FogDensity = Mathf.Lerp(0.012f, 0.004f, k) + Overcast * 0.012f;

        _sky.SetShaderParameter("top_color", new Color(0.015f, 0.025f, 0.08f).Lerp(new Color(0.3f, 0.5f, 0.82f), k));
        _sky.SetShaderParameter("horizon_color", new Color(0.08f, 0.1f, 0.2f).Lerp(new Color(0.85f, 0.72f, 0.58f), k));
        _sky.SetShaderParameter("ground_color", new Color(0.02f, 0.02f, 0.04f).Lerp(new Color(0.25f, 0.27f, 0.2f), k));
        _sky.SetShaderParameter("night", 1f - k);
        _sky.SetShaderParameter("overcast", Overcast);
    }

    public string ClockText()
    {
        int h = (int)Hour;
        int m = (int)((Hour - h) * 60f);
        return $"{h:00}:{m:00}";
    }
}
