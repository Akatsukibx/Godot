using Godot;

// Layers of slow, drifting cloud far above the player (they follow the camera so there is always sky
// over you) and a thin low mist hugging the ground. Colour follows the time of day, rain and the zone:
// grey-lilac over the city, greenish low cloud over the fields, cold blue haze over the marsh.
// At night the moon-lit edges glow faintly.
public partial class CloudLayer : Node3D
{
	const string CloudShader = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled;
uniform vec3 body : source_color = vec3(0.7, 0.72, 0.78);
uniform vec3 edge : source_color = vec3(0.9, 0.9, 1.0);
uniform float cover = 0.5;
uniform float speed = 0.01;
uniform float fade_dist = 700.0;
varying vec3 wpos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) {
	float v = 0.0, a = 0.5;
	for (int i = 0; i < 5; i++) { v += a * noise(p); p = p * 2.03 + 17.0; a *= 0.5; }
	return v;
}
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	vec2 p = wpos.xz * 0.0035 + vec2(TIME * speed, TIME * speed * 0.35);
	float d = fbm(p) + 0.25 * fbm(p * 3.0 - TIME * speed * 2.0);
	float c = smoothstep(1.0 - cover, 1.0 - cover + 0.3, d);
	float rim = smoothstep(0.0, 0.35, c) - smoothstep(0.35, 1.0, c);
	float dist = length(wpos.xz - CAMERA_POSITION_WORLD.xz);
	ALBEDO = mix(body, edge, rim * 0.8);
	ALPHA = c * (1.0 - smoothstep(fade_dist * 0.5, fade_dist, dist)) * 0.85;
}";

	MeshInstance3D _high, _mid, _mist;
	ShaderMaterial _highMat, _midMat, _mistMat;

	public override void _Ready()
	{
		var shader = new Shader { Code = CloudShader };
		_highMat = new ShaderMaterial { Shader = shader };
		_midMat = new ShaderMaterial { Shader = shader };
		_mistMat = new ShaderMaterial { Shader = shader };
		_highMat.SetShaderParameter("speed", 0.006f);
		_midMat.SetShaderParameter("speed", 0.011f);
		_mistMat.SetShaderParameter("speed", 0.004f);
		_mistMat.SetShaderParameter("fade_dist", 160f);
		_high = Layer(900f, 1600f, _highMat);
		_mid = Layer(420f, 1400f, _midMat);
		// No ground-level mist sheet: seen from above it washed the whole view out; the scene fog does that job.
		_mist = Layer(3.5f, 260f, _mistMat);
		_mist.Visible = false;
	}

	MeshInstance3D Layer(float height, float size, Material mat)
	{
		var mi = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(size, size) },
			MaterialOverride = mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = new Vector3(0f, height, 0f),
			ExtraCullMargin = 4000f,
		};
		AddChild(mi);
		return mi;
	}

	public override void _Process(double delta)
	{
		var cam = GetViewport().GetCamera3D();
		if (cam == null || Game.Main == null)
			return;
		var c = cam.GlobalPosition;
		foreach (var m in new[] { _high, _mid })
			m.GlobalPosition = new Vector3(c.X, m.GlobalPosition.Y, c.Z);
		// The mist sits just above the ground around the player.
		_mist.GlobalPosition = new Vector3(c.X, 1.6f, c.Z);

		float night = Game.DayNight.NightFactor;
		float rain = Game.Main.Weather.Intensity;
		string zone = Game.Main.CurrentZone?.Id ?? "wiang_mok";
		Color dayBody = zone switch
		{
			"thung_na" => new Color(0.72f, 0.78f, 0.7f),
			"bueng_bua" => new Color(0.7f, 0.78f, 0.88f),
			_ => new Color(0.78f, 0.76f, 0.82f),
		};
		Color nightBody = zone switch
		{
			"thung_na" => new Color(0.1f, 0.16f, 0.14f),
			"bueng_bua" => new Color(0.1f, 0.15f, 0.26f),
			_ => new Color(0.12f, 0.12f, 0.2f),
		};
		var body = dayBody.Lerp(nightBody, night).Darkened(rain * 0.3f);
		var edge = new Color(1f, 0.93f, 0.85f).Lerp(new Color(0.55f, 0.65f, 0.9f), night);
		float cover = 0.42f + rain * 0.3f + (zone == "bueng_bua" ? 0.08f : 0f);
		foreach (var mat in new[] { _highMat, _midMat })
		{
			mat.SetShaderParameter("body", body);
			mat.SetShaderParameter("edge", edge);
			mat.SetShaderParameter("cover", cover);
		}
		_mistMat.SetShaderParameter("body", body.Lerp(Colors.White, 0.25f));
		_mistMat.SetShaderParameter("edge", body.Lightened(0.15f));
		_mistMat.SetShaderParameter("cover", 0.3f + night * 0.25f + rain * 0.2f + (zone == "bueng_bua" ? 0.2f : zone == "thung_na" ? 0.1f : 0f));
		// Low mist is only visible when you are not inside it: scale the quad's opacity down for a thin veil.
		_mist.Transparency = 0.45f;
	}
}
