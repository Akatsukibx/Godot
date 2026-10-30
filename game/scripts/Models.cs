using Godot;

// Character, NPC and ghost models. Each one is baked to res://models/*.tscn (see SceneBaker) so it can be
// opened and edited in the Godot editor; the game loads the .tscn and only falls back to code when it is missing.
//
// Humanoid models (players and NPCs) must keep these node names, which PlayerRig animates:
//   Hips, Hips/LegL, Hips/LegL/Knee, Hips/LegR, Hips/LegR/Knee, Hips/Torso, Hips/Torso/Head,
//   Hips/Torso/ArmL, Hips/Torso/ArmL/Elbow, Hips/Torso/ArmR, Hips/Torso/ArmR/Elbow
// Meshes whose name starts with "Glow" are never tinted (eyes, lantern flames, windows).
public static class Models
{
	public static string ZonePath(string scene) => $"res://models/zone_{scene}.tscn";
	public const string OutskirtsPath = "res://models/zone_outskirts.tscn";
	public static string PlayerPath(string classId) => $"res://models/player_{classId}.tscn";
	public static string NpcPath(string npcId) => $"res://models/npc_{npcId}.tscn";
	public static string GhostPath(string ghostId) => $"res://models/ghost_{ghostId}.tscn";

	public static Node3D Player(ClassDef def) => BuildPlayer(def);
	public static Node3D Npc(NpcDef def) => BuildNpc(def);
	public static Node3D Ghost(GhostDef def) => Load(GhostPath(def.Id)) ?? BuildGhost(def);
	public static Node3D Zone(string scene) => Load(ZonePath(scene)) ?? WorldBuilder.BuildZone(scene);
	public static Node3D Outskirts() => Load(OutskirtsPath) ?? WorldBuilder.BuildOutskirts();

	// A missing or broken scene (e.g. an interrupted bake) falls back to the code-built version.
	static Node3D Load(string path) =>
		ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path)?.Instantiate<Node3D>() : null;

	// ---------- humanoids ----------

	public class Look
	{
		public Color Cloth, Pants, Skin = new(0.86f, 0.68f, 0.52f), Hair = new(0.08f, 0.07f, 0.07f), Cloak, Sash = new(0.75f, 0.2f, 0.15f);
		public string Hat;
		public bool Robe, Apron, Lantern, Pauldrons, Skirt, HasCloak, OffhandDagger;
		public string Weapon; // "sword" / "talisman" / "dagger" / null
		public float Height = 1f;
	}

	public static Node3D BuildPlayer(ClassDef def)
	{
		bool mage = def.Attack.Type == "projectile";
		bool rogue = def.Offhand == "dagger";
		var look = new Look
		{
			Cloth = Color.FromHtml(def.Color),
			Pants = mage ? new Color(0.42f, 0.36f, 0.3f) : rogue ? new Color(0.1f, 0.1f, 0.13f) : new Color(0.12f, 0.11f, 0.13f),
			Cloak = mage ? new Color(0.42f, 0.36f, 0.28f) : rogue ? new Color(0.14f, 0.15f, 0.22f) : new Color(0.3f, 0.14f, 0.13f),
			Sash = rogue ? new Color(0.35f, 0.38f, 0.6f) : new Color(0.75f, 0.2f, 0.15f),
			HasCloak = true,
			Hat = mage ? "hood" : rogue ? "mask" : "headband",
			Skirt = mage,
			Pauldrons = !mage && !rogue,
			Lantern = true,
			Weapon = mage ? "talisman" : rogue ? "dagger" : "sword",
			OffhandDagger = rogue,
		};
		return SkinModel.Build($"Player_{def.Id}", look) ?? BuildHumanoid($"Player_{def.Id}", look);
	}

	public static Node3D BuildNpc(NpcDef def)
	{
		var l = def.Look;
		var look = new Look
		{
			Cloth = Color.FromHtml(l.Cloth),
			Pants = Color.FromHtml(l.Pants),
			Skin = Color.FromHtml(l.Skin),
			Hair = Color.FromHtml(l.Hair),
			Hat = l.Hat,
			Robe = l.Robe,
			Apron = l.Apron,
			Lantern = l.Lantern,
			Height = l.Height,
			Sash = l.Robe ? Color.FromHtml(l.Cloth).Darkened(0.15f) : new Color(0.75f, 0.2f, 0.15f),
		};
		return SkinModel.Build($"Npc_{def.Id}", look) ?? BuildHumanoid($"Npc_{def.Id}", look);
	}

	public static Node3D BuildHumanoid(string name, Look k)
	{
		var root = new Node3D { Name = name, Scale = Vector3.One * k.Height };
		var hips = Shapes.Group(root, "Hips", new Vector3(0f, 0.95f, 0f));
		var shoe = new Color(0.25f, 0.18f, 0.12f);

		foreach (var (legName, x) in new[] { ("LegL", -0.13f), ("LegR", 0.13f) })
		{
			var leg = Shapes.Group(hips, legName, new Vector3(x, 0f, 0f));
			Shapes.Box(leg, new Vector3(0f, -0.23f, 0f), new Vector3(0.18f, 0.48f, 0.19f), k.Pants, 0f, "Thigh");
			var knee = Shapes.Group(leg, "Knee", new Vector3(0f, -0.46f, 0f));
			Shapes.Box(knee, new Vector3(0f, -0.21f, 0f), new Vector3(0.16f, 0.44f, 0.17f), k.Pants.Darkened(0.06f), 0f, "Shin");
			Shapes.Box(knee, new Vector3(0f, -0.44f, -0.05f), new Vector3(0.17f, 0.09f, 0.29f), shoe, 0f, "Foot");
		}

		var torso = Shapes.Group(hips, "Torso");
		Shapes.Box(torso, new Vector3(0f, 0.34f, 0f), new Vector3(0.52f, 0.66f, 0.3f), k.Cloth, 0f, "Chest");
		Shapes.Box(torso, new Vector3(0f, 0.04f, 0f), new Vector3(0.56f, 0.1f, 0.33f), k.Sash, 0f, "Sash");
		if (k.Skirt)
			Shapes.Cylinder(torso, new Vector3(0f, -0.25f, 0f), 0.29f, 0.4f, 0.55f, k.Cloth.Darkened(0.08f), 0f, "Robe");
		if (k.Robe)
		{
			// Monk's robe to the ankles plus the sash over one shoulder.
			Shapes.Cylinder(torso, new Vector3(0f, -0.45f, 0f), 0.3f, 0.36f, 0.9f, k.Cloth, 0f, "RobeLong");
			Shapes.Box(torso, new Vector3(0.05f, 0.4f, 0f), new Vector3(0.2f, 0.72f, 0.32f), k.Sash, 0f, "ShoulderSash").Rotation = new Vector3(0f, 0f, 0.5f);
		}
		if (k.Apron)
			Shapes.Box(torso, new Vector3(0f, 0.05f, -0.17f), new Vector3(0.44f, 0.75f, 0.03f), new Color(0.32f, 0.24f, 0.16f), 0f, "Apron");
		if (k.HasCloak)
		{
			var cape = Shapes.Box(torso, new Vector3(0f, 0.08f, 0.19f), new Vector3(0.62f, 1.1f, 0.05f), k.Cloak, 0f, "Cloak");
			cape.Rotation = new Vector3(0.1f, 0f, 0f);
		}
		if (k.Pauldrons)
		{
			Shapes.Box(torso, new Vector3(-0.32f, 0.64f, 0f), new Vector3(0.22f, 0.09f, 0.34f), new Color(0.35f, 0.28f, 0.2f), 0f, "PauldronL");
			Shapes.Box(torso, new Vector3(0.32f, 0.64f, 0f), new Vector3(0.22f, 0.09f, 0.34f), new Color(0.35f, 0.28f, 0.2f), 0f, "PauldronR");
		}

		var head = Shapes.Group(torso, "Head", new Vector3(0f, 0.7f, 0f));
		Shapes.Sphere(head, new Vector3(0f, 0.18f, 0f), 0.19f, k.Skin, 0f, "Face");
		Shapes.Box(head, new Vector3(-0.07f, 0.2f, -0.175f), new Vector3(0.05f, 0.025f, 0.02f), new Color(0.1f, 0.08f, 0.08f), 0f, "EyeL");
		Shapes.Box(head, new Vector3(0.07f, 0.2f, -0.175f), new Vector3(0.05f, 0.025f, 0.02f), new Color(0.1f, 0.08f, 0.08f), 0f, "EyeR");
		BuildHat(head, k);

		foreach (var (armName, x) in new[] { ("ArmL", -0.35f), ("ArmR", 0.35f) })
		{
			var arm = Shapes.Group(torso, armName, new Vector3(x, 0.6f, 0f));
			Shapes.Box(arm, new Vector3(0f, -0.14f, 0f), new Vector3(0.15f, 0.3f, 0.15f), k.Cloth, 0f, "Upper");
			var elbow = Shapes.Group(arm, "Elbow", new Vector3(0f, -0.29f, 0f));
			Shapes.Box(elbow, new Vector3(0f, -0.13f, 0f), new Vector3(0.13f, 0.26f, 0.13f), k.Cloth.Darkened(0.05f), 0f, "Forearm");
			Shapes.Sphere(elbow, new Vector3(0f, -0.29f, 0f), 0.075f, k.Skin, 0f, "Hand");
		}

		var rightHand = torso.GetNode<Node3D>("ArmR/Elbow");
		var weapon = Shapes.Group(rightHand, "Weapon", new Vector3(0f, -0.31f, 0f));
		FillWeapon(weapon, k);

		// Left hand: a mount for shields and off-hand daggers (PlayerRig fills it from equipment).
		var mount = Shapes.Group(torso.GetNode<Node3D>("ArmL/Elbow"), "Offhand", new Vector3(0f, -0.31f, 0f));
		if (k.OffhandDagger)
			Dagger(mount);

		if (k.Lantern)
		{
			// Lantern hung from the belt on the left hip; PlayerRig swings it as the body moves.
			var lantern = Shapes.Group(hips, "Lantern", new Vector3(-0.31f, 0.02f, 0.06f));
			BuildLantern(lantern);
		}
		return root;
	}

	// The weapon a class holds in its right hand (blade along -Z from the grip).
	public static void FillWeapon(Node3D weapon, Look k)
	{
		if (k.Weapon == "talisman")
		{
			Shapes.Box(weapon, new Vector3(0f, -0.04f, -0.12f), new Vector3(0.16f, 0.24f, 0.02f), new Color(1f, 0.85f, 0.4f), 0.8f, "Talisman");
			Shapes.Box(weapon, new Vector3(0f, -0.04f, -0.135f), new Vector3(0.1f, 0.16f, 0.005f), new Color(0.7f, 0.15f, 0.1f), 0f, "Ink");
		}
		else if (k.Weapon == "sword")
		{
			Shapes.Box(weapon, new Vector3(0f, 0f, -0.62f), new Vector3(0.05f, 0.09f, 1.0f), new Color(0.82f, 0.86f, 0.92f), 0.3f, "Blade");
			Shapes.Box(weapon, new Vector3(0f, 0f, -0.1f), new Vector3(0.26f, 0.06f, 0.06f), new Color(0.8f, 0.62f, 0.2f), 0f, "Guard");
			Shapes.Box(weapon, new Vector3(0f, 0f, 0.04f), new Vector3(0.05f, 0.05f, 0.22f), new Color(0.3f, 0.18f, 0.1f), 0f, "Grip");
		}
		else if (k.Weapon == "dagger")
			Dagger(weapon);
	}

	// Lantern hung from the belt (handle, cap, flame, base and its light).
	public static void BuildLantern(Node3D lantern)
	{
		Shapes.Box(lantern, new Vector3(0f, -0.06f, 0f), new Vector3(0.02f, 0.12f, 0.02f), new Color(0.2f, 0.2f, 0.2f), 0f, "Handle");
		Shapes.Box(lantern, new Vector3(0f, -0.13f, 0f), new Vector3(0.13f, 0.025f, 0.13f), new Color(0.25f, 0.18f, 0.1f), 0f, "Cap");
		Shapes.Sphere(lantern, new Vector3(0f, -0.22f, 0f), 0.08f, new Color(1f, 0.72f, 0.38f), 3f, "GlowFlame");
		Shapes.Box(lantern, new Vector3(0f, -0.31f, 0f), new Vector3(0.12f, 0.02f, 0.12f), new Color(0.25f, 0.18f, 0.1f), 0f, "Base");
		Shapes.Light(lantern, new Vector3(0f, -0.22f, 0f), new Color(1f, 0.72f, 0.42f), 1.6f, 10f, "LanternLight");
	}

	// Short curved knife held point-forward.
	public static void Dagger(Node3D hand)
	{
		Shapes.Box(hand, new Vector3(0f, 0f, -0.3f), new Vector3(0.04f, 0.07f, 0.42f), new Color(0.75f, 0.78f, 0.86f), 0.25f, "DaggerBlade").Rotation = new Vector3(0.08f, 0f, 0f);
		Shapes.Box(hand, new Vector3(0f, 0f, -0.07f), new Vector3(0.16f, 0.04f, 0.04f), new Color(0.3f, 0.3f, 0.35f), 0f, "DaggerGuard");
		Shapes.Box(hand, new Vector3(0f, 0f, 0.04f), new Vector3(0.045f, 0.045f, 0.16f), new Color(0.15f, 0.1f, 0.08f), 0f, "DaggerGrip");
	}

	// Round wooden shield on the left forearm.
	public static void Shield(Node3D hand, Color face, Color rim)
	{
		var s = Shapes.Cylinder(hand, new Vector3(-0.06f, 0.12f, -0.05f), 0.36f, 0.36f, 0.06f, face, 0f, "ShieldFace");
		s.Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
		var r = Shapes.Cylinder(hand, new Vector3(-0.05f, 0.12f, -0.05f), 0.39f, 0.39f, 0.04f, rim, 0f, "ShieldRim");
		r.Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
		Shapes.Box(hand, new Vector3(-0.1f, 0.12f, -0.05f), new Vector3(0.02f, 0.2f, 0.2f), new Color(0.75f, 0.2f, 0.15f), 0f, "ShieldYan");
	}

	public static void BuildHat(Node3D head, Look k)
	{
		switch (k.Hat)
		{
			case "hood":
				var hood = Shapes.Sphere(head, new Vector3(0f, 0.22f, 0.1f), 0.24f, k.Cloak, 0f, "Hood");
				hood.Scale = new Vector3(1f, 1.05f, 1.05f);
				Shapes.Cylinder(head, new Vector3(0f, 0.36f, 0.2f), 0.01f, 0.12f, 0.3f, k.Cloak, 0f, "HoodTip").Rotation = new Vector3(-1.1f, 0f, 0f);
				break;
			case "mask":
				Hair(head, k);
				Shapes.Box(head, new Vector3(0f, 0.12f, -0.12f), new Vector3(0.36f, 0.14f, 0.18f), k.Cloak, 0f, "Mask");
				Shapes.Cylinder(head, new Vector3(0f, 0.27f, 0f), 0.205f, 0.205f, 0.06f, k.Sash, 0f, "Headband");
				break;
			case "bald":
				break;
			case "bun":
				Hair(head, k);
				Shapes.Sphere(head, new Vector3(0f, 0.36f, 0.1f), 0.09f, k.Hair, 0f, "Bun");
				break;
			case "cap":
				Hair(head, k);
				Shapes.Cylinder(head, new Vector3(0f, 0.32f, 0f), 0.2f, 0.21f, 0.1f, k.Cloth, 0f, "Cap");
				Shapes.Box(head, new Vector3(0f, 0.28f, -0.22f), new Vector3(0.3f, 0.03f, 0.14f), k.Cloth.Darkened(0.2f), 0f, "Visor");
				break;
			case "ngob":
				Hair(head, k);
				Shapes.Cylinder(head, new Vector3(0f, 0.38f, 0f), 0.03f, 0.48f, 0.22f, new Color(0.72f, 0.6f, 0.36f), 0f, "Ngob");
				break;
			default: // headband
				Hair(head, k);
				Shapes.Cylinder(head, new Vector3(0f, 0.27f, 0f), 0.205f, 0.205f, 0.06f, k.Sash, 0f, "Headband");
				Shapes.Box(head, new Vector3(0.05f, 0.24f, 0.23f), new Vector3(0.05f, 0.25f, 0.02f), k.Sash, 0f, "HeadbandTail").Rotation = new Vector3(0.4f, 0f, 0.2f);
				break;
		}
	}

	static void Hair(Node3D head, Look k)
	{
		var hair = Shapes.Sphere(head, new Vector3(0f, 0.26f, 0.03f), 0.195f, k.Hair, 0f, "Hair");
		hair.Scale = new Vector3(1.05f, 0.7f, 1.05f);
	}

	// ---------- ghosts ----------

	public static Node3D BuildGhost(GhostDef def)
	{
		var root = new Node3D { Name = $"Ghost_{def.Id}" };
		switch (def.Id)
		{
			case "hun_lai_ka": BuildScarecrow(root, def); break;
			case "phi_pong": BuildPhiPong(root, def); break;
			case "winyan_long_soi": BuildLostSoul(root, def); break;
			case "phi_phrai": BuildPhiPhrai(root, def); break;
			case "phi_krahang": BuildKrahang(root, def); break;
			case "nang_tani": BuildNangTani(root, def); break;
			case "boss_hun_yak": BuildScarecrow(root, def); break;
			case "boss_krasue":
				var head = BuildKrasue();
				head.Position = new Vector3(0f, 1.7f, 0f);
				root.AddChild(head);
				break;
			default: BuildTaiHong(root, def); break;
		}
		return root;
	}

	// ผีพราย: a drowned woman risen to the waist from the water, long wet hair, arms reaching out.
	static void BuildPhiPhrai(Node3D v, GhostDef def)
	{
		var c = Color.FromHtml(def.Color);
		var skin = new Color(0.62f, 0.78f, 0.74f, 0.85f);
		var hair = new Color(0.03f, 0.05f, 0.05f);
		Shapes.Capsule(v, new Vector3(0f, 1.15f, 0f), 0.3f, 1.1f, new Color(0.3f, 0.4f, 0.42f, 0.8f), 0f, "Torso");
		Shapes.Sphere(v, new Vector3(0f, 1.85f, -0.03f), 0.23f, skin, 0f, "Head");
		var mane = Shapes.Box(v, new Vector3(0f, 1.45f, 0.1f), new Vector3(0.5f, 1.1f, 0.12f), hair, 0f, "WetHair");
		mane.Rotation = new Vector3(0.12f, 0f, 0f);
		Shapes.Sphere(v, new Vector3(0f, 1.92f, 0.05f), 0.25f, hair, 0f, "HairTop");
		foreach (var x in new[] { -0.32f, 0.32f })
			Shapes.Capsule(v, new Vector3(x, 1.45f, -0.35f), 0.07f, 0.9f, skin, 0f, "Arm").Rotation = new Vector3(1.2f, 0f, -x * 0.4f);
		Shapes.Sphere(v, new Vector3(-0.08f, 1.88f, -0.21f), 0.03f, c, 4f, "GlowEyeL");
		Shapes.Sphere(v, new Vector3(0.08f, 1.88f, -0.21f), 0.03f, c, 4f, "GlowEyeR");
		Shapes.Cylinder(v, new Vector3(0f, 0.85f, 0f), 0.75f, 0.75f, 0.02f, new Color(c, 0.35f), 1.5f, "GlowRipple");
		Shapes.Light(v, new Vector3(0f, 1.3f, -0.4f), c, 1.2f, 5f, "GlowLight");
	}

	// ผีกระหัง: a hunched man with winnowing baskets for wings and a pestle for a tail.
	static void BuildKrahang(Node3D v, GhostDef def)
	{
		var skin = new Color(0.55f, 0.45f, 0.36f);
		var basket = new Color(0.72f, 0.58f, 0.34f);
		var torso = Shapes.Box(v, new Vector3(0f, 1.25f, 0f), new Vector3(0.5f, 0.7f, 0.32f), skin, 0f, "Torso");
		torso.Rotation = new Vector3(-0.5f, 0f, 0f);
		Shapes.Box(v, new Vector3(0f, 0.85f, 0.1f), new Vector3(0.5f, 0.3f, 0.32f), new Color(0.35f, 0.15f, 0.12f), 0f, "Loincloth");
		Shapes.Sphere(v, new Vector3(0f, 1.62f, -0.32f), 0.2f, skin, 0f, "Head");
		Shapes.Sphere(v, new Vector3(-0.07f, 1.66f, -0.5f), 0.03f, new Color(1f, 0.75f, 0.2f), 4f, "GlowEyeL");
		Shapes.Sphere(v, new Vector3(0.07f, 1.66f, -0.5f), 0.03f, new Color(1f, 0.75f, 0.2f), 4f, "GlowEyeR");
		foreach (var s in new[] { -1f, 1f })
		{
			var wing = Shapes.Cylinder(v, new Vector3(s * 0.75f, 1.4f, 0.15f), 0.55f, 0.55f, 0.05f, basket, 0f, s < 0f ? "WingL" : "WingR");
			wing.Rotation = new Vector3(0.3f, 0f, s * 1.2f);
			Shapes.Cylinder(v, new Vector3(s * 0.75f, 1.4f, 0.15f), 0.45f, 0.45f, 0.06f, basket.Darkened(0.25f), 0f, "WingWeave").Rotation = wing.Rotation;
		}
		var tail = Shapes.Cylinder(v, new Vector3(0f, 0.8f, 0.55f), 0.07f, 0.1f, 1.1f, new Color(0.35f, 0.25f, 0.15f), 0f, "PestleTail");
		tail.Rotation = new Vector3(1.1f, 0f, 0f);
		foreach (var x in new[] { -0.15f, 0.15f })
			Shapes.Capsule(v, new Vector3(x, 0.55f, 0.15f), 0.07f, 0.6f, skin, 0f, "Leg").Rotation = new Vector3(0.6f, 0f, 0f);
	}

	// นางตานี: a pale woman in a banana-leaf green dress, hair over one eye.
	static void BuildNangTani(Node3D v, GhostDef def)
	{
		var dress = Color.FromHtml(def.Color);
		var skin = new Color(0.86f, 0.84f, 0.76f, 0.9f);
		Shapes.Cylinder(v, new Vector3(0f, 0.7f, 0f), 0.22f, 0.5f, 1.4f, dress.Darkened(0.2f), 0f, "Skirt");
		Shapes.Box(v, new Vector3(0f, 1.6f, 0f), new Vector3(0.42f, 0.5f, 0.26f), dress, 0f, "Bodice");
		Shapes.Sphere(v, new Vector3(0f, 2.05f, -0.02f), 0.2f, skin, 0f, "Head");
		Shapes.Sphere(v, new Vector3(0.03f, 2.1f, 0.05f), 0.22f, new Color(0.04f, 0.04f, 0.05f), 0f, "Hair");
		Shapes.Box(v, new Vector3(-0.08f, 1.75f, -0.15f), new Vector3(0.14f, 0.6f, 0.05f), new Color(0.04f, 0.04f, 0.05f), 0f, "FringeLock");
		Shapes.Sphere(v, new Vector3(0.08f, 2.06f, -0.18f), 0.03f, new Color(0.6f, 1f, 0.5f), 4f, "GlowEye");
		foreach (var s in new[] { -1f, 1f })
		{
			Shapes.Capsule(v, new Vector3(s * 0.3f, 1.45f, -0.05f), 0.06f, 0.7f, skin, 0f, "Arm").Rotation = new Vector3(0.2f, 0f, s * 0.2f);
			var leaf = Shapes.Box(v, new Vector3(s * 0.45f, 1.0f, 0.1f), new Vector3(0.08f, 1.2f, 0.35f), dress.Lightened(0.15f), 0f, "LeafSash");
			leaf.Rotation = new Vector3(0f, 0f, s * 0.25f);
		}
		Shapes.Light(v, new Vector3(0f, 1.6f, -0.3f), new Color(0.6f, 1f, 0.5f), 0.8f, 4f, "GlowLight");
	}

	static void BuildScarecrow(Node3D v, GhostDef def)
	{
		var wood = new Color(0.4f, 0.28f, 0.15f);
		var straw = new Color(0.78f, 0.66f, 0.36f);
		Shapes.Cylinder(v, new Vector3(0f, 0.9f, 0f), 0.05f, 0.06f, 1.8f, wood, 0f, "Pole");
		Shapes.Box(v, new Vector3(0f, 1.42f, 0f), new Vector3(1.7f, 0.1f, 0.1f), wood, 0f, "Crossbar");
		Shapes.Box(v, new Vector3(0f, 1.22f, 0f), new Vector3(0.62f, 0.8f, 0.32f), Color.FromHtml(def.Color), 0f, "Shirt");
		Shapes.Box(v, new Vector3(-0.62f, 1.36f, 0f), new Vector3(0.5f, 0.24f, 0.26f), Color.FromHtml(def.Color).Darkened(0.15f), 0f, "SleeveL");
		Shapes.Box(v, new Vector3(0.62f, 1.36f, 0f), new Vector3(0.5f, 0.24f, 0.26f), Color.FromHtml(def.Color).Darkened(0.15f), 0f, "SleeveR");
		foreach (var (n, p) in new[] { ("TuftL", new Vector3(-0.92f, 1.36f, 0f)), ("TuftR", new Vector3(0.92f, 1.36f, 0f)), ("TuftHem", new Vector3(0f, 0.78f, 0f)) })
			Shapes.Cylinder(v, p, 0.02f, 0.14f, 0.2f, straw, 0f, n).Rotation = n == "TuftHem" ? Vector3.Zero : new Vector3(0f, 0f, n == "TuftL" ? 1.57f : -1.57f);
		Shapes.Sphere(v, new Vector3(0f, 1.9f, 0f), 0.27f, new Color(0.78f, 0.7f, 0.5f), 0f, "SackHead");
		Shapes.Cylinder(v, new Vector3(0f, 2.16f, 0f), 0.02f, 0.55f, 0.26f, new Color(0.6f, 0.5f, 0.25f), 0f, "StrawHat");
		Shapes.Box(v, new Vector3(0f, 1.8f, -0.25f), new Vector3(0.22f, 0.025f, 0.02f), new Color(0.15f, 0.08f, 0.05f), 0f, "Mouth");
		Shapes.Sphere(v, new Vector3(-0.09f, 1.94f, -0.24f), 0.045f, new Color(1f, 0.15f, 0.1f), 4f, "GlowEyeL");
		Shapes.Sphere(v, new Vector3(0.09f, 1.94f, -0.24f), 0.045f, new Color(1f, 0.15f, 0.1f), 4f, "GlowEyeR");
	}

	static void BuildPhiPong(Node3D v, GhostDef def)
	{
		var c = Color.FromHtml(def.Color);
		var hair = new Color(0.04f, 0.05f, 0.05f);
		Shapes.Sphere(v, new Vector3(0f, 1.45f, 0f), 0.24f, new Color(0.55f, 0.62f, 0.55f), 0f, "Head");
		var mane = Shapes.Sphere(v, new Vector3(0f, 1.5f, 0.06f), 0.28f, hair, 0f, "Hair");
		mane.Scale = new Vector3(1f, 1.1f, 0.95f);
		for (int i = 0; i < 5; i++)
		{
			float x = -0.2f + i * 0.1f;
			Shapes.Box(v, new Vector3(x, 1.05f, 0.12f), new Vector3(0.07f, 0.8f, 0.05f), hair, 0f, $"Strand{i}")
				.Rotation = new Vector3(0.15f, 0f, x * 0.6f);
		}
		Shapes.Box(v, new Vector3(0f, 0.85f, 0.05f), new Vector3(0.36f, 0.7f, 0.2f), new Color(0.2f, 0.22f, 0.2f, 0.7f), 0f, "Shroud");
		Shapes.Sphere(v, new Vector3(0f, 1.43f, -0.24f), 0.07f, c, 4f, "GlowNose");
		Shapes.Sphere(v, new Vector3(-0.08f, 1.52f, -0.21f), 0.03f, c, 3f, "GlowEyeL");
		Shapes.Sphere(v, new Vector3(0.08f, 1.52f, -0.21f), 0.03f, c, 3f, "GlowEyeR");
		Shapes.Sphere(v, new Vector3(0f, 1.1f, 0.3f), 0.12f, new Color(c, 0.4f), 1.2f, "GlowTrail1");
		Shapes.Sphere(v, new Vector3(0f, 0.8f, 0.5f), 0.07f, new Color(c, 0.3f), 1f, "GlowTrail2");
		Shapes.Light(v, new Vector3(0f, 1.4f, -0.3f), c, 1.8f, 6f, "GlowLight");
	}

	static void BuildTaiHong(Node3D v, GhostDef def)
	{
		var c = Color.FromHtml(def.Color);
		var skin = new Color(c.Lightened(0.15f), 0.85f);
		var cloth = new Color(0.25f, 0.27f, 0.3f, 0.85f);
		Shapes.Capsule(v, new Vector3(0f, 1.15f, 0f), 0.42f, 1.5f, cloth, 0f, "Body");
		Shapes.Cylinder(v, new Vector3(0f, 0.4f, 0f), 0.42f, 0.55f, 0.8f, new Color(cloth, 0.6f), 0f, "TatteredHem");
		var head = Shapes.Sphere(v, new Vector3(0f, 2.05f, -0.12f), 0.27f, skin, 0f, "Head");
		head.Rotation = new Vector3(-0.4f, 0f, 0f);
		Shapes.Sphere(v, new Vector3(0f, 2.15f, 0.0f), 0.29f, new Color(0.05f, 0.05f, 0.06f, 0.9f), 0f, "WetHair");
		foreach (var (n, x) in new[] { ("ArmL", -0.5f), ("ArmR", 0.5f) })
		{
			var arm = Shapes.Capsule(v, new Vector3(x, 1.2f, -0.15f), 0.11f, 1.2f, skin, 0f, n);
			arm.Rotation = new Vector3(0.35f, 0f, x * 0.15f);
		}
		Shapes.Box(v, new Vector3(-0.1f, 2.0f, -0.36f), new Vector3(0.08f, 0.05f, 0.02f), new Color(0.05f, 0.05f, 0.08f), 0f, "EyeL");
		Shapes.Box(v, new Vector3(0.1f, 2.0f, -0.36f), new Vector3(0.08f, 0.05f, 0.02f), new Color(0.05f, 0.05f, 0.08f), 0f, "EyeR");
	}

	// A pale, translucent townsperson drifting just above the ground with its head bowed.
	static void BuildLostSoul(Node3D v, GhostDef def)
	{
		var c = Color.FromHtml(def.Color);
		var body = new Color(c, 0.55f);
		Shapes.Cylinder(v, new Vector3(0f, 0.85f, 0f), 0.22f, 0.36f, 1.2f, body, 0.4f, "Body");
		var head = Shapes.Sphere(v, new Vector3(0f, 1.62f, -0.06f), 0.2f, new Color(c.Lightened(0.3f), 0.6f), 0.4f, "Head");
		head.Rotation = new Vector3(-0.5f, 0f, 0f);
		Shapes.Sphere(v, new Vector3(0f, 1.7f, 0.04f), 0.21f, new Color(0.05f, 0.06f, 0.1f, 0.85f), 0f, "Hair");
		foreach (var (n, x) in new[] { ("ArmL", -0.28f), ("ArmR", 0.28f) })
			Shapes.Capsule(v, new Vector3(x, 1.05f, -0.05f), 0.06f, 0.75f, body, 0.4f, n).Rotation = new Vector3(0.1f, 0f, x * 0.3f);
		Shapes.Sphere(v, new Vector3(-0.07f, 1.6f, -0.24f), 0.03f, new Color(0.7f, 0.85f, 1f), 3f, "GlowEyeL");
		Shapes.Sphere(v, new Vector3(0.07f, 1.6f, -0.24f), 0.03f, new Color(0.7f, 0.85f, 1f), 3f, "GlowEyeR");
		Shapes.Light(v, new Vector3(0f, 1.2f, 0f), c, 0.8f, 4f, "GlowLight");
	}

	// กระสือ: a floating woman's head trailing glowing entrails (cutscene prop for the บึงบัวสาย boss).
	public static Node3D BuildKrasue()
	{
		var v = new Node3D { Name = "Krasue" };
		var skin = new Color(0.85f, 0.72f, 0.62f);
		Shapes.Sphere(v, Vector3.Zero, 0.28f, skin, 0f, "Head");
		var hair = Shapes.Sphere(v, new Vector3(0f, 0.06f, 0.06f), 0.31f, new Color(0.03f, 0.03f, 0.04f), 0f, "Hair");
		hair.Scale = new Vector3(1f, 1.05f, 1f);
		for (int i = 0; i < 6; i++)
			Shapes.Box(v, new Vector3(-0.22f + i * 0.09f, -0.45f, 0.14f), new Vector3(0.07f, 0.8f, 0.04f), new Color(0.03f, 0.03f, 0.04f), 0f, "Strand");
		Shapes.Sphere(v, new Vector3(-0.09f, 0.03f, -0.25f), 0.035f, new Color(1f, 0.2f, 0.15f), 4f, "GlowEyeL");
		Shapes.Sphere(v, new Vector3(0.09f, 0.03f, -0.25f), 0.035f, new Color(1f, 0.2f, 0.15f), 4f, "GlowEyeR");
		// Entrails: a chain of glowing red beads hanging below the head.
		var rng = new RandomNumberGenerator { Seed = 7 };
		for (int s = 0; s < 4; s++)
		{
			float x = -0.12f + s * 0.08f;
			for (int i = 0; i < 9; i++)
			{
				var p = new Vector3(x + Mathf.Sin(i * 0.9f + s) * 0.07f, -0.3f - i * 0.12f, rng.RandfRange(-0.05f, 0.05f));
				Shapes.Sphere(v, p, 0.06f - i * 0.004f, new Color(0.9f, 0.1f, 0.12f), 2.5f, "GlowGut");
			}
		}
		Shapes.Light(v, new Vector3(0f, -0.5f, 0f), new Color(1f, 0.15f, 0.12f), 3f, 9f, "GlowLight");
		return v;
	}
}
