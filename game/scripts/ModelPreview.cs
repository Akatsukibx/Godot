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
		var eye = showcase ? new Vector3(0f, 1.35f, 6.4f) : new Vector3(0f, 1.1f, 6.1f);
		preview._cam.Transform = new Transform3D(Basis.Identity, eye).LookingAt(new Vector3(0f, showcase ? 1.05f : 0.9f, 0f), Vector3.Up);

		preview.MouseEntered += () => { if (preview._rig != null) preview._rig.Emote = "wave"; };
		preview.MouseExited += () => { if (preview._rig != null) preview._rig.Emote = null; };
		return preview;
	}

	// A scene behind the showcased class: the talisman maker's candle-lit room, a temple yard for the
	// swordsman, moonlit rooftops for the assassin, a red-draped spirit shrine, a workbench of gadgets.
	Node3D _backdrop;

	public void SetBackdrop(string classId)
	{
		_backdrop?.QueueFree();
		var b = _backdrop = new Node3D { Name = "Backdrop" };
		_vp.AddChild(b);
		var wood = new Color(0.32f, 0.22f, 0.14f);
		var dark = new Color(0.16f, 0.11f, 0.08f);
		switch (classId)
		{
			case "mor_yan":
				Shapes.Box(b, new Vector3(0f, 2f, -2.6f), new Vector3(8f, 4.5f, 0.2f), wood, 0f, "BackWall");
				for (int i = 0; i < 14; i++)
				{
					var y = Shapes.Box(b, new Vector3(-3f + (i % 7) * 1f, 3f - (i / 7) * 0.9f, -2.48f), new Vector3(0.3f, 0.5f, 0.01f), new Color(0.78f, 0.66f, 0.34f), 0f, "Talisman");
					y.Rotation = new Vector3(0f, 0f, (i % 3 - 1) * 0.08f);
				}
				Shapes.Box(b, new Vector3(-1.8f, 0.45f, -1.8f), new Vector3(1.6f, 0.9f, 0.7f), dark, 0f, "Table");
				foreach (var x in new[] { -2.3f, -1.3f, 1.6f, 2.2f })
				{
					Shapes.Cylinder(b, new Vector3(x, x < 0f ? 1.05f : 0.2f, -1.8f), 0.04f, 0.04f, 0.3f, new Color(0.95f, 0.9f, 0.8f), 0f, "Candle");
					Shapes.Sphere(b, new Vector3(x, x < 0f ? 1.25f : 0.4f, -1.8f), 0.05f, new Color(1f, 0.7f, 0.3f), 4f, "GlowFlame");
				}
				Shapes.Cylinder(b, new Vector3(2f, 0.35f, -1.6f), 0.3f, 0.25f, 0.7f, new Color(0.5f, 0.3f, 0.18f), 0f, "ClayPot");
				Shapes.Light(b, new Vector3(-1.8f, 1.6f, -1.2f), new Color(1f, 0.65f, 0.3f), 1.6f, 6f, "CandleLight");
				break;
			case "nak_dab":
				Shapes.Box(b, new Vector3(0f, 0.1f, -3f), new Vector3(9f, 0.2f, 3f), new Color(0.75f, 0.73f, 0.68f), 0f, "Step");
				foreach (var x in new[] { -3f, -1f, 1f, 3f })
					Shapes.Cylinder(b, new Vector3(x, 2.2f, -3.2f), 0.22f, 0.25f, 4.4f, new Color(0.9f, 0.88f, 0.84f), 0f, "Column");
				Shapes.Box(b, new Vector3(0f, 4.5f, -3.2f), new Vector3(8.5f, 0.4f, 0.6f), new Color(0.62f, 0.2f, 0.14f), 0f, "Beam");
				foreach (var x in new[] { -2f, 2f })
					for (int i = 0; i < 5; i++)
						Shapes.Box(b, new Vector3(x, 3.6f - i * 0.4f, -2.8f), new Vector3(0.35f, 0.35f, 0.02f), i % 2 == 0 ? new Color(0.85f, 0.3f, 0.2f) : new Color(0.95f, 0.8f, 0.3f), 0f, "ClothTung");
				Shapes.Box(b, new Vector3(2.6f, 1f, -2.2f), new Vector3(0.1f, 2f, 0.1f), dark, 0f, "RackPost");
				Shapes.Box(b, new Vector3(2.6f, 1.6f, -2.2f), new Vector3(0.8f, 0.06f, 0.1f), dark, 0f, "RackBar");
				for (int i = 0; i < 3; i++)
					Shapes.Box(b, new Vector3(2.35f + i * 0.25f, 1.2f, -2.15f), new Vector3(0.04f, 1.1f, 0.06f), new Color(0.8f, 0.82f, 0.88f), 0.2f, "RackSword");
				Shapes.Light(b, new Vector3(0f, 3.5f, -1.5f), new Color(1f, 0.75f, 0.45f), 1.2f, 7f, "TempleLight");
				break;
			case "nak_kha":
				Shapes.Sphere(b, new Vector3(1.8f, 3.4f, -6f), 1f, new Color(0.9f, 0.92f, 1f), 2.5f, "GlowMoon");
				for (int i = 0; i < 5; i++)
				{
					float x = -4f + i * 2f, h = 1.2f + (i % 3) * 0.6f;
					Shapes.Box(b, new Vector3(x, h * 0.5f, -4.5f), new Vector3(1.8f, h, 1.5f), new Color(0.08f, 0.08f, 0.13f), 0f, "Rooftop");
					Shapes.Prism(b, new Vector3(x, h + 0.4f, -4.5f), new Vector3(2f, 0.8f, 1.6f), new Color(0.1f, 0.1f, 0.16f), "RoofSilhouette");
				}
				Shapes.Light(b, new Vector3(1.8f, 3.4f, -4.5f), new Color(0.6f, 0.7f, 1f), 2f, 9f, "MoonLight");
				break;
			case "rang_song":
				Shapes.Box(b, new Vector3(0f, 2f, -2.6f), new Vector3(8f, 4.5f, 0.2f), new Color(0.55f, 0.08f, 0.08f), 0f, "ClothDrape");
				Shapes.Box(b, new Vector3(0f, 0.6f, -2f), new Vector3(2.2f, 1.2f, 0.8f), new Color(0.75f, 0.6f, 0.2f), 0.1f, "Altar");
				foreach (var x in new[] { -0.7f, 0f, 0.7f })
					Shapes.Sphere(b, new Vector3(x, 1.4f, -2f), 0.12f, new Color(1f, 0.85f, 0.4f), 1f, "Offering");
				Shapes.Light(b, new Vector3(0f, 2f, -1.4f), new Color(1f, 0.4f, 0.3f), 1.6f, 6f, "ShrineLight");
				break;
			default:
				Shapes.Box(b, new Vector3(0f, 2f, -2.6f), new Vector3(8f, 4.5f, 0.2f), new Color(0.15f, 0.17f, 0.2f), 0f, "BackWall");
				for (int i = 0; i < 6; i++)
					Shapes.Box(b, new Vector3(-2.5f + i, 2.6f, -2.48f), new Vector3(0.7f, 0.45f, 0.02f), new Color(0.2f, 0.9f, 0.6f), 1.2f, "GlowScreen");
				Shapes.Box(b, new Vector3(-1.8f, 0.45f, -1.8f), new Vector3(1.6f, 0.9f, 0.7f), new Color(0.25f, 0.25f, 0.28f), 0f, "Workbench");
				Shapes.Light(b, new Vector3(0f, 2.4f, -1.5f), new Color(0.4f, 1f, 0.7f), 1.2f, 6f, "ScreenLight");
				break;
		}
		Textures.Apply(b);
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
