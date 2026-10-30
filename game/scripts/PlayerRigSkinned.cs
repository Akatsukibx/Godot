using Godot;
using System.Collections.Generic;
using System.Linq;

// The skinned half of PlayerRig (see SkinModel): instead of posing box joints by code, it plays the
// Unreal-skeleton animations on the mannequin and recolours / dresses it for the equipment.
public partial class PlayerRig
{
	bool _skinned;
	Node3D _skinModel, _skinRoot, _headFrame, _torsoFrame;
	AnimationPlayer _anim;
	ShaderMaterial _bodyMat;
	string _curAnim = "";
	bool _curReverse;
	int _actSerial, _playedSerial;
	float _offsetYaw;
	Color _shownTint = Colors.White;
	float _shownTintW = -1f;

	// Natural travel speeds (m/s) of the walk / jog / sprint cycles, to scale their playback.
	const float WalkSpeed = 1.5f, JogSpeed = 3.8f, SprintSpeed = 6.2f;

	void AttachSkinned(Node3D model)
	{
		AddChild(model);
		_skinned = true;
		_skinModel = model;
		_skinRoot = model.GetNode<Node3D>("Skin");
		_anim = (AnimationPlayer)model.FindChild("AnimationPlayer", true, false);
		_bodyMat = (ShaderMaterial)model.GetMeta("bodyMat");
		_weapon = model.FindChild("Weapon", true, false) as Node3D;
		_offhand = model.FindChild("Offhand", true, false) as Node3D;
		_lantern = model.FindChild("Lantern", true, false) as Node3D;
		_headFrame = model.FindChild("HeadFrame", true, false) as Node3D;
		_torsoFrame = model.FindChild("TorsoFrame", true, false) as Node3D;
		TrackMaterials(model, _mats);
		PlayAnim("Idle", 0f, 1f, false);
	}

	void PlayAnim(string name, float blend, float speed, bool reverse)
	{
		string full = "Rig|" + name;
		if (!_anim.HasAnimation(full))
			full = "Rig|Idle";
		_anim.SpeedScale = speed;
		if (full != _curAnim || reverse != _curReverse || _playedSerial != _actSerial)
		{
			_curAnim = full;
			_curReverse = reverse;
			_playedSerial = _actSerial;
			_anim.Play(full, blend, reverse ? -1f : 1f, reverse);
		}
	}

	static string ActAnimName(Act a) => a switch
	{
		Act.Swing1 or Act.Swing2 or Act.Swing3 or Act.Finisher or Act.Thrust => "Sword_Attack",
		Act.StabL => "Punch_Jab",
		Act.StabR => "Punch_Cross",
		Act.Throw => "Spell_Simple_Shoot",
		Act.Cast => "Spell_Simple_Enter",
		Act.Hurt => "Hit_Chest",
		Act.Roll => "Roll",
		_ => "Idle",
	};

	static string EmoteAnimName(string emote) => emote switch
	{
		"sit" or "sitchair" or "fish" => "Sitting_Idle",
		"wave" or "wai" => "Interact",
		"sweep" => "Push",
		"farm" => "Fixing_Kneeling",
		"sell" or "chat" => "Idle_Talking",
		"dance" => "Dance",
		_ => "Idle",
	};

	void AnimateSkinned(float dt, Vector2 v, bool running, bool dead, bool airborne)
	{
		_time += dt;
		if (_act != Act.None)
		{
			_actTime += dt;
			if (_actTime >= _actDur)
				_act = Act.None;
		}
		float t = _act == Act.None ? 0f : _actTime / _actDur;
		_spin = _act == Act.Finisher ? Mathf.SmoothStep(0.15f, 0.75f, t) * Mathf.Tau : 0f;
		float yaw = _act == Act.Roll ? Mathf.Atan2(-_rollDir.X, -_rollDir.Z) : Yaw + _spin;
		Transform = new Transform3D(Basis.FromEuler(new Vector3(0f, yaw, 0f)), Vector3.Zero);

		float speed = v.Length();
		float fwd = speed > 0.2f ? v.Y / speed : 1f, side = speed > 0.2f ? v.X / speed : 0f;
		bool back = fwd < -0.3f;
		bool locomoting = _act == Act.None && !dead && !Swim && !airborne && speed > 0.25f;

		// There are no strafe clips: turn the body toward its travel direction instead.
		float targetOffset = locomoting && !back ? -Mathf.Atan2(side, Mathf.Max(fwd, 0.05f)) : 0f;
		_offsetYaw = Mathf.LerpAngle(_offsetYaw, targetOffset, 1f - Mathf.Exp(-12f * dt));
		_skinRoot.Rotation = new Vector3(0f, Mathf.Pi + _offsetYaw, 0f);

		string name;
		float rate = 1f, blend = 0.18f;
		bool reverse = false;
		if (dead)
		{
			name = "Death01";
			blend = 0.1f;
		}
		else if (_act != Act.None)
		{
			name = ActAnimName(_act);
			blend = 0.06f;
			float len = _anim.HasAnimation("Rig|" + name) ? (float)_anim.GetAnimation("Rig|" + name).Length : 1f;
			rate = Mathf.Clamp(len / (_actDur + 0.2f), 0.8f, 3.5f);
		}
		else if (Swim)
			name = speed > 0.3f ? "Swim_Fwd" : "Swim_Idle";
		else if (airborne)
			name = "Jump";
		else if (Crouch)
		{
			name = speed > 0.3f ? "Crouch_Fwd" : "Crouch_Idle";
			rate = speed > 0.3f ? Mathf.Clamp(speed / 1.6f, 0.6f, 1.5f) : 1f;
		}
		else if (speed > 0.25f)
		{
			if (back)
			{
				name = "Walk";
				reverse = true;
				rate = Mathf.Clamp(speed / WalkSpeed, 0.5f, 1.8f);
			}
			else if (running)
			{
				name = "Sprint";
				rate = Mathf.Clamp(speed / SprintSpeed, 0.6f, 1.5f);
			}
			else if (speed > 4f)
			{
				name = "Jog_Fwd";
				rate = Mathf.Clamp(speed / JogSpeed, 0.7f, 1.5f);
			}
			else
			{
				name = "Walk";
				rate = Mathf.Clamp(speed / WalkSpeed, 0.5f, 1.8f);
			}
		}
		else if (Emote != null)
			name = EmoteAnimName(Emote);
		else
			name = "Idle";
		PlayAnim(name, blend, rate, reverse);

		// Flash / buff tint on the body shader and on the mounted pieces.
		if (_tintWeight != _shownTintW || _tint != _shownTint)
		{
			_shownTintW = _tintWeight;
			_shownTint = _tint;
			_bodyMat.SetShaderParameter("tint", _tint);
			_bodyMat.SetShaderParameter("tint_w", _tintWeight);
			foreach (var (mat, color) in _mats)
				mat.AlbedoColor = _tintWeight > 0f ? color.Lerp(new Color(_tint, color.A), _tintWeight) : color;
		}
	}

	// Equipment on the mannequin: armour, trousers and boots recolour their body region; helmets,
	// shoulder plates, shields and weapon glows are mounted pieces.
	void ApplyEquipmentSkinned(ClassDef def, Dictionary<string, ItemStack> equipped)
	{
		foreach (var n in _gear)
			if (IsInstanceValid(n))
				n.QueueFree();
		_gear.Clear();
		_mats.RemoveAll(m => m.mat.HasMeta("gear"));

		Color ColorOf(ItemStack s) => s.Def.Color != null ? Color.FromHtml(s.Def.Color) : GameData.RarityColor(s.Def.Rarity);
		Node3D Attach(Node3D parent, string name)
		{
			var g = new Node3D { Name = name };
			parent.AddChild(g);
			_gear.Add(g);
			return g;
		}

		var torsoColor = (Color)_skinModel.GetMeta("c1");
		var sleeveColor = (Color)_skinModel.GetMeta("c2");
		var pantsColor = (Color)_skinModel.GetMeta("c3");
		var shoeColor = (Color)_skinModel.GetMeta("c4");
		bool hasHelmet = equipped.TryGetValue("helmet", out var helmet);
		if (equipped.TryGetValue("armor", out var armor))
		{
			torsoColor = ColorOf(armor);
			sleeveColor = torsoColor.Darkened(0.1f);
		}
		if (equipped.TryGetValue("pants", out var pants))
			pantsColor = ColorOf(pants);
		if (equipped.TryGetValue("boots", out var boots))
			shoeColor = ColorOf(boots);
		_bodyMat.SetShaderParameter("c1", torsoColor);
		_bodyMat.SetShaderParameter("c2", sleeveColor);
		_bodyMat.SetShaderParameter("c3", pantsColor);
		_bodyMat.SetShaderParameter("c4", shoeColor);

		// Hair and hats give way to a helmet.
		if (_headFrame != null)
			foreach (var c in _headFrame.GetChildren().OfType<Node3D>())
				if (!c.Name.ToString().StartsWith("Eye") && !c.Name.ToString().StartsWith("Gear"))
					c.Visible = !hasHelmet;
		if (hasHelmet && _headFrame != null)
		{
			var g = Attach(_headFrame, "GearHelmet");
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
		if (equipped.TryGetValue("armor", out _) && _torsoFrame != null)
		{
			var g = Attach(_torsoFrame, "GearArmor");
			var c = torsoColor;
			Shapes.Box(g, new Vector3(-0.3f, 0.66f, 0f), new Vector3(0.24f, 0.1f, 0.36f), c.Darkened(0.15f), 0f, "ShoulderL");
			Shapes.Box(g, new Vector3(0.3f, 0.66f, 0f), new Vector3(0.24f, 0.1f, 0.36f), c.Darkened(0.15f), 0f, "ShoulderR");
			if (armor.Def.Set == "pha_yan")
				Shapes.Box(g, new Vector3(0f, 0.4f, -0.175f), new Vector3(0.22f, 0.26f, 0.01f), new Color(0.7f, 0.15f, 0.1f), 0f, "Yan");
		}

		// Hands: weapon visible only when one is worn; off-hand shield or dagger.
		if (_weapon != null)
			foreach (var c in _weapon.GetChildren().OfType<Node3D>())
				c.Visible = equipped.ContainsKey("weapon");
		if (_offhand != null)
		{
			foreach (var c in _offhand.GetChildren().OfType<Node3D>())
				if (!c.Name.ToString().StartsWith("Gear"))
					c.Visible = equipped.TryGetValue("offhand", out var oh) && oh.Def.OffhandType != "shield";
			if (equipped.TryGetValue("offhand", out var off))
			{
				var g = Attach(_offhand, "GearOffhand");
				if (off.Def.OffhandType == "shield")
					Models.Shield(g, ColorOf(off).Darkened(0.2f), new Color(0.35f, 0.25f, 0.15f));
				else if (def.Offhand != "dagger")
					Models.Dagger(g);
			}
		}
		if (_weapon != null && equipped.TryGetValue("weapon", out var weapon) && (weapon.Def.Rarity >= 2 || weapon.Plus >= 4))
		{
			var g = Attach(_weapon, "GearGlow");
			Shapes.Light(g, new Vector3(0f, 0f, -0.5f), GameData.RarityColor(weapon.Def.Rarity), 0.6f + weapon.Plus * 0.15f, 2.5f, "GlowLight");
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
