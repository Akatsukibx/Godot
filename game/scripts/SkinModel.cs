using Godot;
using System.Collections.Generic;
using System.Linq;

// A skinned humanoid built from the Unreal-skeleton animation library (Unreal Engine/AL_Standard.fbx):
// the mannequin mesh, its 65-bone skeleton and its 45 animations. The mannequin is recoloured by body
// region (skin / torso / sleeves / trousers / shoes) with a shader that reads a region id from the
// vertex colour, so every character gets its own clothes without a texture. Hair, hats, cloaks,
// weapons, shields and the belt lantern are small meshes mounted on bones (see Mount).
//
// The node tree is  Model > Skin(scale, facing) > AL_Standard > Rig > Skeleton3D (+ AnimationPlayer).
// PlayerRig drives the AnimationPlayer; it falls back to the box rig when the FBX is missing.
public static class SkinModel
{
	const string FbxPath = "res://Unreal Engine/AL_Standard.fbx";
	public const float BaseScale = 1.04f;

	static PackedScene _scene;
	static bool _tried;
	static ArrayMesh _bodyMesh;
	static Shader _shader;

	static readonly string[] LoopedAnims =
	{
		"Idle", "Walk", "Jog_Fwd", "Sprint", "Crouch_Idle", "Crouch_Fwd", "Swim_Fwd", "Swim_Idle", "Sitting_Idle",
		"Sitting_Talking", "Idle_Talking", "Idle_Torch", "Interact", "Push", "Fixing_Kneeling", "Dance", "Spell_Simple_Idle",
	};

	static PackedScene Scene
	{
		get
		{
			if (!_tried)
			{
				_tried = true;
				_scene = ResourceLoader.Exists(FbxPath) ? ResourceLoader.Load<PackedScene>(FbxPath) : null;
			}
			return _scene;
		}
	}

	public static bool Available => Scene != null;

	const string ShaderCode = @"
shader_type spatial;
uniform vec3 c0 : source_color = vec3(0.86, 0.68, 0.52);
uniform vec3 c1 : source_color = vec3(0.6);
uniform vec3 c2 : source_color = vec3(0.55);
uniform vec3 c3 : source_color = vec3(0.2);
uniform vec3 c4 : source_color = vec3(0.25, 0.18, 0.12);
uniform vec3 tint : source_color = vec3(1.0);
uniform float tint_w = 0.0;
varying flat int region;
void vertex() { region = int(COLOR.r * 10.0 + 0.5); }
void fragment() {
	vec3 c = c0;
	if (region == 1) c = c1;
	else if (region == 2) c = c2;
	else if (region == 3) c = c3;
	else if (region == 4) c = c4;
	ALBEDO = mix(c, tint, tint_w);
	ROUGHNESS = 0.85;
}";

	static int RegionOf(string bone)
	{
		if (bone is "Head" or "neck_01" || bone.StartsWith("hand") || bone.StartsWith("index") || bone.StartsWith("middle")
			|| bone.StartsWith("ring") || bone.StartsWith("pinky") || bone.StartsWith("thumb"))
			return 0;
		if (bone.StartsWith("lowerarm"))
			return 2;
		if (bone.StartsWith("pelvis") || bone.StartsWith("thigh") || bone.StartsWith("calf"))
			return 3;
		if (bone.StartsWith("foot") || bone.StartsWith("ball"))
			return 4;
		return 1; // spine, clavicle, upperarm
	}

	// The mannequin mesh again, with the body region of each vertex (from its strongest bone weight) in
	// the red channel of the vertex colour. Built once and shared by every character.
	static ArrayMesh BuildBodyMesh(Skeleton3D skel, MeshInstance3D source)
	{
		var src = (ArrayMesh)source.Mesh;
		var dst = new ArrayMesh();
		var skin = source.Skin;
		for (int s = 0; s < src.GetSurfaceCount(); s++)
		{
			var arrays = src.SurfaceGetArrays(s);
			var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
			var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
			int per = bones.Length / verts.Length;
			var colors = new Color[verts.Length];
			var regionCache = new Dictionary<int, int>();
			for (int i = 0; i < verts.Length; i++)
			{
				int best = 0;
				float bw = -1f;
				for (int k = 0; k < per; k++)
					if (weights[i * per + k] > bw)
					{
						bw = weights[i * per + k];
						best = bones[i * per + k];
					}
				if (!regionCache.TryGetValue(best, out int region))
				{
					string name = skin.GetBindName(best);
					if (string.IsNullOrEmpty(name))
						name = skel.GetBoneName(skin.GetBindBone(best));
					regionCache[best] = region = RegionOf(name);
				}
				colors[i] = new Color(region / 10f, 0f, 0f, 1f);
			}
			arrays[(int)Mesh.ArrayType.Color] = colors;
			dst.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
				per == 8 ? Mesh.ArrayFormat.FlagUse8BoneWeights : 0);
		}
		return dst;
	}

	static Transform3D ChainTo(Node node, Node ancestor)
	{
		var t = Transform3D.Identity;
		for (var c = node; c != ancestor && c is Node3D n; c = c.GetParent())
			t = n.Transform * t;
		return t;
	}

	// A frame fixed to a bone: its origin sits at `originOld` and its axes are the old box rig's
	// (up = +Y, forward = -Z, right = +X), as they are in the T-pose, so meshes written for the box
	// rig can be put inside unchanged. originOld is in the character's space (feet at y = 0).
	static Node3D Mount(Node3D fbx, Skeleton3D skel, string bone, Vector3 originOld, string name)
	{
		int bi = skel.FindBone(bone);
		var att = new BoneAttachment3D { Name = "Att_" + name };
		skel.AddChild(att);
		att.BoneName = bone;
		var mount = new Node3D { Name = name };
		att.AddChild(mount);
		var toModel = ChainTo(skel, fbx) * skel.GetBoneGlobalRest(bi);
		var desired = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(-originOld.X, originOld.Y, -originOld.Z));
		mount.Transform = toModel.AffineInverse() * desired;
		return mount;
	}

	public static Node3D Build(string name, Models.Look k)
	{
		var scene = Scene;
		if (scene == null)
			return null;
		var fbx = scene.Instantiate<Node3D>();
		var skel = fbx.FindChild("Skeleton3D", true, false) as Skeleton3D;
		var anim = fbx.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
		var mi = skel?.GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
		if (skel == null || anim == null || mi == null)
		{
			fbx.Free();
			return null;
		}

		var root = new Node3D { Name = name };
		root.SetMeta("skinned", true);
		var skinRoot = new Node3D { Name = "Skin", Rotation = new Vector3(0f, Mathf.Pi, 0f), Scale = Vector3.One * BaseScale * k.Height };
		root.AddChild(skinRoot);
		skinRoot.AddChild(fbx);

		// Per-character body colours.
		_shader ??= new Shader { Code = ShaderCode };
		_bodyMesh ??= BuildBodyMesh(skel, mi);
		mi.Mesh = _bodyMesh;
		var shoe = new Color(0.25f, 0.18f, 0.12f);
		var mat = new ShaderMaterial { Shader = _shader };
		mat.SetShaderParameter("c0", k.Skin);
		mat.SetShaderParameter("c1", k.Cloth);
		mat.SetShaderParameter("c2", k.Cloth.Darkened(0.05f));
		mat.SetShaderParameter("c3", k.Pants);
		mat.SetShaderParameter("c4", shoe);
		mi.MaterialOverride = mat;
		root.SetMeta("bodyMat", mat);
		root.SetMeta("c1", k.Cloth);
		root.SetMeta("c2", k.Cloth.Darkened(0.05f));
		root.SetMeta("c3", k.Pants);
		root.SetMeta("c4", shoe);

		foreach (var n in LoopedAnims)
			if (anim.HasAnimation("Rig|" + n))
				anim.GetAnimation("Rig|" + n).LoopMode = Animation.LoopModeEnum.Linear;

		// Head: eyes, then hair or a hat, in a frame scaled to the mannequin's smaller head.
		var head = Mount(fbx, skel, "Head", new Vector3(0f, 1.555f, 0f), "HeadMount");
		var headFrame = new Node3D { Name = "HeadFrame", Scale = Vector3.One * 0.62f, Position = new Vector3(0f, 0.01f, 0f) };
		head.AddChild(headFrame);
		var dark = new Color(0.1f, 0.08f, 0.08f);
		Shapes.Box(headFrame, new Vector3(-0.07f, 0.2f, -0.165f), new Vector3(0.05f, 0.025f, 0.02f), dark, 0f, "EyeL");
		Shapes.Box(headFrame, new Vector3(0.07f, 0.2f, -0.165f), new Vector3(0.05f, 0.025f, 0.02f), dark, 0f, "EyeR");
		Models.BuildHat(headFrame, k);

		// Torso: sash, cloak, shoulder pads, apron, monk's sash.
		var torso = Mount(fbx, skel, "spine_02", new Vector3(0f, 0.95f, 0f), "TorsoMount");
		var tf = new Node3D { Name = "TorsoFrame", Scale = new Vector3(0.62f, 1f, 0.8f) };
		torso.AddChild(tf);
		Shapes.Box(tf, new Vector3(0f, 0.04f, 0f), new Vector3(0.56f, 0.1f, 0.33f), k.Sash, 0f, "Sash");
		if (k.Apron)
			Shapes.Box(tf, new Vector3(0f, 0.05f, -0.17f), new Vector3(0.44f, 0.75f, 0.03f), new Color(0.32f, 0.24f, 0.16f), 0f, "Apron");
		if (k.HasCloak)
			Shapes.Box(tf, new Vector3(0f, 0.22f, 0.21f), new Vector3(0.62f, 0.8f, 0.04f), k.Cloak, 0f, "Cloak").Rotation = new Vector3(0.12f, 0f, 0f);
		if (k.Pauldrons)
		{
			Shapes.Box(tf, new Vector3(-0.32f, 0.64f, 0f), new Vector3(0.22f, 0.09f, 0.34f), new Color(0.35f, 0.28f, 0.2f), 0f, "PauldronL");
			Shapes.Box(tf, new Vector3(0.32f, 0.64f, 0f), new Vector3(0.22f, 0.09f, 0.34f), new Color(0.35f, 0.28f, 0.2f), 0f, "PauldronR");
		}
		if (k.Robe)
			Shapes.Box(tf, new Vector3(0.05f, 0.4f, 0f), new Vector3(0.2f, 0.72f, 0.32f), k.Sash, 0f, "ShoulderSash").Rotation = new Vector3(0f, 0f, 0.5f);

		// Skirts and robes hang from the pelvis.
		if (k.Skirt || k.Robe)
		{
			var hips = Mount(fbx, skel, "pelvis", new Vector3(0f, 0.95f, 0f), "HipsMount");
			var hf = new Node3D { Name = "HipsFrame", Scale = new Vector3(0.8f, 1f, 0.8f) };
			hips.AddChild(hf);
			if (k.Skirt)
				Shapes.Cylinder(hf, new Vector3(0f, -0.25f, 0f), 0.29f, 0.4f, 0.55f, k.Cloth.Darkened(0.08f), 0f, "SkirtCloth");
			if (k.Robe)
				Shapes.Cylinder(hf, new Vector3(0f, -0.45f, 0f), 0.3f, 0.36f, 0.9f, k.Cloth, 0f, "RobeLong");
		}

		// Hands: weapon on the right, off-hand on the left, belt lantern on the left hip.
		var weapon = Mount(fbx, skel, "hand_r", new Vector3(0.8f, 1.425f, 0.07f), "Weapon");
		Models.FillWeapon(weapon, k);
		var offhand = Mount(fbx, skel, "hand_l", new Vector3(-0.8f, 1.425f, 0.07f), "Offhand");
		if (k.OffhandDagger)
			Models.Dagger(offhand);
		if (k.Lantern)
		{
			var lantern = Mount(fbx, skel, "pelvis", new Vector3(-0.2f, 0.97f, 0.06f), "Lantern");
			Models.BuildLantern(lantern);
		}
		return root;
	}
}
