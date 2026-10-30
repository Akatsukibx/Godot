using Godot;
using System.Collections.Generic;

// A small 3D stage showing a character model: the class-select showcase (spotlight, floating motes)
// and the character in the bag window (wearing the current equipment).
public partial class ModelPreview : SubViewportContainer
{
	SubViewport _vp;
	Node3D _stage;
	PlayerRig _rig;
	ClassDef _def;
	Camera3D _cam;
	float _time;
	public bool Turn { get; set; } = true;

	public static ModelPreview Create(ClassDef def, Vector2 size)
	{
		var p = CreateEmpty(size);
		p.SetClass(def);
		return p;
	}

	public static ModelPreview CreateEmpty(Vector2 size, bool showcase = false)
	{
		var preview = new ModelPreview { Stretch = true, CustomMinimumSize = size, MouseFilter = MouseFilterEnum.Pass };
		var vp = new SubViewport { OwnWorld3D = true, TransparentBg = !showcase, Msaa3D = Viewport.Msaa.Msaa2X, Size = showcase ? new Vector2I(1280, 720) : (Vector2I)size };
		preview._vp = vp;
		preview.AddChild(vp);
		vp.AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = showcase ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.ClearColor,
				BackgroundColor = new Color(0.045f, 0.04f, 0.045f),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = showcase ? new Color(0.3f, 0.28f, 0.25f) : new Color(0.35f, 0.4f, 0.6f),
				AmbientLightEnergy = 0.8f,
				TonemapMode = Godot.Environment.ToneMapper.Filmic,
				GlowEnabled = true,
				GlowIntensity = showcase ? 0.6f : 0.8f,
			},
		});
		var key = new DirectionalLight3D { LightColor = showcase ? new Color(1f, 0.85f, 0.6f) : new Color(0.75f, 0.82f, 1f), LightEnergy = showcase ? 0.9f : 0.9f };
		vp.AddChild(key);
		key.RotationDegrees = new Vector3(-30f, 30f, 0f);
		vp.AddChild(new OmniLight3D { Position = new Vector3(-1.2f, 1.2f, 1.5f), LightColor = new Color(1f, 0.65f, 0.35f), LightEnergy = 1.4f, OmniRange = 5f });

		preview._stage = new Node3D();
		vp.AddChild(preview._stage);
		Shapes.Cylinder(preview._stage, new Vector3(0f, -0.05f, 0f), 0.75f, 0.8f, 0.1f, new Color(0.22f, 0.16f, 0.11f));
		if (showcase)
		{
			vp.AddChild(new SpotLight3D { Position = new Vector3(0f, 4.5f, 1.2f), RotationDegrees = new Vector3(-75f, 0f, 0f), LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = 2.2f, SpotRange = 8f, SpotAngle = 28f });
			vp.AddChild(new GpuParticles3D
			{
				Amount = 40,
				Lifetime = 5f,
				Preprocess = 5f,
				Position = new Vector3(0f, 1f, 0f),
				ProcessMaterial = new ParticleProcessMaterial
				{
					EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
					EmissionBoxExtents = new Vector3(1.6f, 1.2f, 1f),
					Gravity = new Vector3(0f, 0.06f, 0f),
					InitialVelocityMin = 0.02f,
					InitialVelocityMax = 0.12f,
					Spread = 180f,
				},
				DrawPass1 = new SphereMesh
				{
					Radius = 0.011f, Height = 0.022f, RadialSegments = 6, Rings = 3,
					Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(2.5f, 2f, 1.1f) },
				},
			});
		}
		preview._cam = new Camera3D { Fov = showcase ? 30f : 32f };
		vp.AddChild(preview._cam);
		var eye = showcase ? new Vector3(0f, 1.35f, 6.4f) : new Vector3(0f, 1.25f, 4.6f);
		preview._cam.Transform = new Transform3D(Basis.Identity, eye).LookingAt(new Vector3(0f, showcase ? 1.05f : 0.95f, 0f), Vector3.Up);

		preview.MouseEntered += () => { if (preview._rig != null) preview._rig.Emote = "wave"; };
		preview.MouseExited += () => { if (preview._rig != null) preview._rig.Emote = null; };
		return preview;
	}

	public void SetClass(ClassDef def)
	{
		if (_def == def && _rig != null)
			return;
		_def = def;
		_rig?.QueueFree();
		_rig = new PlayerRig { Yaw = Mathf.Pi };
		_vp.AddChild(_rig);
		_rig.Build(def);
	}

	public void ShowEquipment(ClassDef def, Dictionary<string, ItemStack> equipped)
	{
		SetClass(def);
		_rig.ApplyEquipment(def, equipped);
	}

	public void PlayAction(PlayerRig.Act act, float duration) => _rig?.Play(act, duration);

	public override void _Process(double delta)
	{
		if (_rig == null)
			return;
		float dt = (float)delta;
		_time += dt;
		_rig.Yaw = Turn ? Mathf.Pi + Mathf.Sin(_time * 0.6f) * 0.7f : Mathf.Pi + 0.35f;
		_rig.Animate(dt, Vector2.Zero, false, false);
	}
}
