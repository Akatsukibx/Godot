using Godot;
using System.Collections.Generic;
using System.Linq;

// Everyday props that make the towns and villages look lived in: market stalls with different wares,
// handcarts, water jars, firewood, rice sacks, laundry racks, planters and little fences, plus
// puddles and treasure chests. Placement works off the road list in WorldLayout (the props sit just
// beside a road, never on it, and never overlap a building or another prop).
// Every mesh uses a name Textures.cs knows, so wood, cloth, clay and so on get their surfaces.
public static partial class WorldBuilder
{
	static readonly Color Straw = new(0.72f, 0.6f, 0.34f);
	static readonly Color Clay = new(0.55f, 0.3f, 0.2f);

	// ---------- stalls ----------

	enum Ware { Fruit, Vegetables, DriedFish, Sweets, Flowers, Baskets, Herbs, Cloth }

	static readonly string[] WareNames =
	{
		"แผงผลไม้", "แผงผัก", "แผงปลาแห้ง", "แผงขนม", "แผงดอกไม้ธูปเทียน", "แผงเครื่องจักสาน", "แผงยาสมุนไพร", "แผงผ้า",
	};

	static void Stall(Node3D parent, string name, Vector3 pos, float yaw, Ware ware)
	{
		var s = Shapes.Group(parent, name, pos, yaw);
		Shapes.MapTag(s, "map_house", new Vector2(3.6f, 2.4f));
		var awning = ware switch
		{
			Ware.Fruit => new Color(0.82f, 0.28f, 0.2f), Ware.Vegetables => new Color(0.3f, 0.55f, 0.25f), Ware.DriedFish => new Color(0.3f, 0.4f, 0.6f),
			Ware.Sweets => new Color(0.9f, 0.6f, 0.7f), Ware.Flowers => new Color(0.65f, 0.35f, 0.7f), Ware.Baskets => new Color(0.75f, 0.6f, 0.3f),
			Ware.Herbs => new Color(0.35f, 0.5f, 0.45f), _ => new Color(0.8f, 0.7f, 0.25f),
		};
		foreach (var x in new[] { -1.6f, 1.6f })
		{
			Shapes.Box(s, new Vector3(x, 1.2f, -0.9f), new Vector3(0.1f, 2.4f, 0.1f), DarkWood, 0f, "PostFront");
			Shapes.Box(s, new Vector3(x, 1.5f, 0.9f), new Vector3(0.1f, 3f, 0.1f), DarkWood, 0f, "PostBack");
		}
		// Striped cloth awning sloping up to the back, plus a hanging valance.
		for (int i = 0; i < 6; i++)
		{
			var strip = Shapes.Box(s, new Vector3(-1.5f + i * 0.6f, 2.55f, 0f), new Vector3(0.6f, 0.06f, 2.3f), i % 2 == 0 ? awning : awning.Lightened(0.35f), 0f, "ClothAwning");
			strip.Rotation = new Vector3(-0.2f, 0f, 0f);
		}
		Shapes.Box(s, new Vector3(0f, 2.3f, -1.15f), new Vector3(3.6f, 0.25f, 0.04f), awning.Darkened(0.1f), 0f, "ClothValance");
		// Counter with a plank top and a crate beneath.
		Shapes.Box(s, new Vector3(0f, 0.45f, -0.6f), new Vector3(3.2f, 0.9f, 0.9f), Wood, 0f, "Counter");
		Shapes.Solid(s, new Vector3(0f, 0.45f, -0.6f), new Vector3(3.2f, 0.9f, 0.9f));
		Shapes.Box(s, new Vector3(0f, 0.93f, -0.6f), new Vector3(3.3f, 0.06f, 1f), Wood.Lightened(0.08f), 0f, "CounterTop");
		Shapes.Box(s, new Vector3(1.25f, 0.3f, 0.45f), new Vector3(0.7f, 0.6f, 0.6f), DarkWood.Lightened(0.1f), 0f, "Crate");
		Shapes.Light(s, new Vector3(0f, 2.1f, -0.6f), WindowGlow, 1.1f, 6f, "StallLight");

		var rng = _rng;
		switch (ware)
		{
			case Ware.Fruit:
				foreach (var (fx, fc) in new[] { (-1.1f, new Color(0.95f, 0.5f, 0.1f)), (-0.4f, new Color(0.9f, 0.85f, 0.25f)), (0.3f, new Color(0.8f, 0.2f, 0.2f)), (1.0f, new Color(0.45f, 0.7f, 0.25f)) })
				{
					Shapes.Cylinder(s, new Vector3(fx, 1.05f, -0.6f), 0.28f, 0.22f, 0.15f, Straw, 0f, "BasketWeave");
					for (int k = 0; k < 5; k++)
						Shapes.Sphere(s, new Vector3(fx + rng.RandfRange(-0.15f, 0.15f), 1.17f, -0.6f + rng.RandfRange(-0.15f, 0.15f)), 0.09f, fc, 0f, "FruitPiece");
				}
				break;
			case Ware.Vegetables:
				foreach (var (vx, vc) in new[] { (-1.1f, new Color(0.25f, 0.55f, 0.2f)), (-0.35f, new Color(0.85f, 0.45f, 0.15f)), (0.4f, new Color(0.5f, 0.65f, 0.25f)), (1.1f, new Color(0.6f, 0.25f, 0.45f)) })
					for (int k = 0; k < 4; k++)
						Shapes.Cylinder(s, new Vector3(vx + rng.RandfRange(-0.2f, 0.2f), 1.07f, -0.6f + rng.RandfRange(-0.2f, 0.2f)), 0.05f, 0.08f, 0.25f, vc, 0f, "LeafVeg").Rotation = new Vector3(0f, 0f, rng.RandfRange(-0.8f, 0.8f));
				break;
			case Ware.DriedFish:
				Shapes.Box(s, new Vector3(0f, 2.0f, 0.2f), new Vector3(3f, 0.05f, 0.05f), DarkWood, 0f, "HangBar");
				for (int k = 0; k < 9; k++)
					Shapes.Box(s, new Vector3(-1.3f + k * 0.32f, 1.7f, 0.2f), new Vector3(0.12f, 0.5f, 0.03f), new Color(0.6f, 0.55f, 0.45f), 0f, "FishDry");
				break;
			case Ware.Sweets:
				foreach (var x in new[] { -1f, 0f, 1f })
				{
					Shapes.Cylinder(s, new Vector3(x, 1.12f, -0.6f), 0.3f, 0.3f, 0.2f, Clay.Lightened(0.15f), 0f, "ClayPlate");
					for (int k = 0; k < 4; k++)
						Shapes.Sphere(s, new Vector3(x + rng.RandfRange(-0.15f, 0.15f), 1.28f, -0.6f + rng.RandfRange(-0.15f, 0.15f)), 0.08f, new Color(0.95f, 0.8f, 0.55f).Darkened(rng.RandfRange(0f, 0.3f)), 0f, "SweetBun");
				}
				break;
			case Ware.Flowers:
				foreach (var x in new[] { -1.1f, -0.3f, 0.5f, 1.2f })
				{
					Shapes.Cylinder(s, new Vector3(x, 1.12f, -0.6f), 0.14f, 0.1f, 0.3f, Clay, 0f, "ClayVase");
					for (int k = 0; k < 4; k++)
						Shapes.Sphere(s, new Vector3(x + rng.RandfRange(-0.12f, 0.12f), 1.4f + rng.RandfRange(0f, 0.15f), -0.6f + rng.RandfRange(-0.12f, 0.12f)), 0.07f, k % 2 == 0 ? new Color(1f, 0.85f, 0.2f) : new Color(0.95f, 0.5f, 0.7f), 0.3f, "PetalBloom");
				}
				Shapes.Box(s, new Vector3(1.3f, 1.05f, -0.55f), new Vector3(0.2f, 0.25f, 0.1f), new Color(0.9f, 0.2f, 0.15f), 0f, "IncenseBundle");
				break;
			case Ware.Baskets:
				for (int k = 0; k < 5; k++)
					Shapes.Cylinder(s, new Vector3(-1.2f + k * 0.6f, 1.15f + (k % 2) * 0.1f, -0.6f), 0.22f + 0.04f * (k % 3), 0.17f, 0.3f, Straw.Darkened(0.05f * k), 0f, "BasketWeave");
				Shapes.Cylinder(s, new Vector3(1.3f, 0.25f, 0.5f), 0.4f, 0.35f, 0.5f, Straw, 0f, "BasketWeave");
				break;
			case Ware.Herbs:
				for (int k = 0; k < 6; k++)
					Shapes.Sphere(s, new Vector3(-1.2f + k * 0.48f, 1.1f, -0.6f), 0.14f, new Color(0.3f, 0.45f, 0.2f).Lightened(rng.RandfRange(0f, 0.25f)), 0f, "HerbBunch");
				foreach (var x in new[] { -1.3f, 1.3f })
					Shapes.Cylinder(s, new Vector3(x, 1.2f, -0.2f), 0.14f, 0.16f, 0.35f, Clay, 0f, "ClayJar");
				break;
			default:
				for (int k = 0; k < 4; k++)
					Shapes.Box(s, new Vector3(-1.1f + k * 0.7f, 1.12f, -0.6f), new Vector3(0.5f, 0.25f, 0.35f), new Color(0.8f, 0.3f, 0.3f).Lerp(new Color(0.3f, 0.5f, 0.8f), k / 3f), 0f, "ClothFolded");
				Shapes.Box(s, new Vector3(0f, 1.9f, 0.2f), new Vector3(2.4f, 1f, 0.03f), new Color(0.85f, 0.7f, 0.3f), 0f, "ClothHanging");
				break;
		}
		Shapes.Child(s, new Label3D
		{
			Text = WareNames[(int)ware],
			Font = Game.ThaiFont,
			FontSize = 36,
			PixelSize = 0.0042f,
			Modulate = new Color(1f, 0.95f, 0.8f),
			OutlineSize = 6,
			OutlineModulate = new Color(0.1f, 0.06f, 0.04f),
			DoubleSided = false,
			Position = new Vector3(0f, 2.3f, -1.18f),
			Rotation = new Vector3(0f, Mathf.Pi, 0f),
		}, "StallSign");
	}

	// ---------- small props ----------

	enum Prop { Cart, Jars, Firewood, Sacks, Laundry, Planter, Barrel, Bench, Shrine }

	static float PropRadius(Prop p) => p switch { Prop.Cart => 2.4f, Prop.Laundry => 2f, Prop.Bench => 1.5f, Prop.Shrine => 1f, _ => 1.1f };

	static void SmallProp(Node3D parent, string name, Vector3 pos, float yaw, Prop kind)
	{
		var g = Shapes.Group(parent, name, pos, yaw);
		var rng = _rng;
		switch (kind)
		{
			case Prop.Cart: // wooden handcart with two big wheels, loaded with sacks
				Shapes.Box(g, new Vector3(0f, 0.75f, 0f), new Vector3(1.4f, 0.14f, 2.2f), Wood, 0f, "CartBed");
				foreach (var side in new[] { -1f, 1f })
				{
					Shapes.Box(g, new Vector3(side * 0.7f, 1.0f, 0f), new Vector3(0.08f, 0.5f, 2.2f), DarkWood, 0f, "CartSide");
					Shapes.Cylinder(g, new Vector3(side * 0.82f, 0.5f, 0.3f), 0.5f, 0.5f, 0.1f, DarkWood.Lightened(0.05f), 0f, "WheelWood").Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
					Shapes.Box(g, new Vector3(side * 0.5f, 0.7f, -1.9f), new Vector3(0.08f, 0.08f, 1.4f), DarkWood, 0f, "CartShaft");
				}
				for (int k = 0; k < 3; k++)
					Shapes.Sphere(g, new Vector3(rng.RandfRange(-0.3f, 0.3f), 1.15f, -0.5f + k * 0.6f), 0.32f, Straw.Darkened(0.1f * k), 0f, "SackBulge");
				Shapes.Solid(g, new Vector3(0f, 0.8f, 0f), new Vector3(1.8f, 1.2f, 2.2f));
				break;
			case Prop.Jars: // two or three big clay water jars on a plank
				Shapes.Box(g, new Vector3(0f, 0.05f, 0f), new Vector3(1.6f, 0.1f, 0.9f), Wood, 0f, "Plank");
				for (int k = 0; k < 3; k++)
				{
					float r = 0.38f - k * 0.05f;
					Shapes.Sphere(g, new Vector3(-0.5f + k * 0.5f, r + 0.1f, 0f), r, Clay.Darkened(0.05f * k), 0f, "ClayJar");
					Shapes.Cylinder(g, new Vector3(-0.5f + k * 0.5f, r * 2f + 0.12f, 0f), r * 0.55f, r * 0.7f, 0.12f, Clay, 0f, "ClayJar");
				}
				Shapes.Solid(g, new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 1f, 0.9f));
				break;
			case Prop.Firewood: // a stack of split logs under a little lean-to
				for (int row = 0; row < 4; row++)
				for (int k = 0; k < 5 - row; k++)
					Shapes.Cylinder(g, new Vector3(-0.8f + k * 0.34f + row * 0.17f, 0.17f + row * 0.3f, 0f), 0.13f, 0.13f, 1.2f, DarkWood.Lightened(rng.RandfRange(0f, 0.12f)), 0f, "LogEnd").Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
				Shapes.Solid(g, new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 1.2f, 1.2f));
				break;
			case Prop.Sacks:
				for (int k = 0; k < 5; k++)
					Shapes.Sphere(g, new Vector3((k % 3) * 0.55f - 0.5f, 0.28f + (k / 3) * 0.45f, (k / 3) * 0.1f), 0.32f, Straw.Darkened(rng.RandfRange(0f, 0.15f)), 0f, "SackBulge").Scale = new Vector3(1f, 0.85f, 0.9f);
				Shapes.Solid(g, new Vector3(0f, 0.4f, 0f), new Vector3(1.8f, 0.8f, 0.9f));
				break;
			case Prop.Laundry: // two posts with a line of hanging cloth
				foreach (var x in new[] { -1.6f, 1.6f })
					Shapes.Cylinder(g, new Vector3(x, 1.1f, 0f), 0.06f, 0.08f, 2.2f, DarkWood, 0f, "Pole");
				Shapes.Box(g, new Vector3(0f, 2.05f, 0f), new Vector3(3.2f, 0.04f, 0.04f), DarkWood, 0f, "PoleBar");
				for (int k = 0; k < 4; k++)
					Shapes.Box(g, new Vector3(-1.1f + k * 0.75f, 1.6f, 0f), new Vector3(0.55f, 0.85f, 0.03f), new Color(0.85f, 0.8f, 0.7f).Lerp(new Color(0.4f, 0.5f, 0.75f), (k % 3) / 2f), 0f, "ClothHanging");
				Shapes.Solid(g, new Vector3(-1.6f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 0.2f));
				Shapes.Solid(g, new Vector3(1.6f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 0.2f));
				break;
			case Prop.Planter: // clay pot with a leafy shrub and a flower or two
				Shapes.Cylinder(g, new Vector3(0f, 0.3f, 0f), 0.4f, 0.28f, 0.6f, Clay, 0f, "ClayPot");
				Shapes.Sphere(g, new Vector3(0f, 0.85f, 0f), 0.5f, new Color(0.16f, 0.34f, 0.16f), 0f, "LeafBush").Scale = new Vector3(1f, 0.8f, 1f);
				for (int k = 0; k < 3; k++)
					Shapes.Sphere(g, new Vector3(rng.RandfRange(-0.3f, 0.3f), 1.1f, rng.RandfRange(-0.3f, 0.3f)), 0.07f, k % 2 == 0 ? new Color(1f, 0.85f, 0.2f) : new Color(0.95f, 0.45f, 0.65f), 0.3f, "PetalBloom");
				Shapes.Solid(g, new Vector3(0f, 0.6f, 0f), new Vector3(0.9f, 1.2f, 0.9f));
				break;
			case Prop.Barrel:
				Shapes.Cylinder(g, new Vector3(0f, 0.5f, 0f), 0.42f, 0.42f, 1f, DarkWood.Lightened(0.1f), 0f, "BarrelStave");
				foreach (var y in new[] { 0.2f, 0.8f })
					Shapes.Cylinder(g, new Vector3(0f, y, 0f), 0.44f, 0.44f, 0.06f, Colors.DimGray, 0f, "BarrelHoop");
				Shapes.Solid(g, new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 1f, 0.9f));
				break;
			case Prop.Bench:
				Shapes.Box(g, new Vector3(0f, 0.5f, 0f), new Vector3(1.5f, 0.08f, 0.45f), Wood, 0f, "BenchTop");
				foreach (var x in new[] { -0.6f, 0.6f })
					Shapes.Box(g, new Vector3(x, 0.25f, 0f), new Vector3(0.08f, 0.5f, 0.4f), DarkWood, 0f, "BenchLeg");
				Shapes.Solid(g, new Vector3(0f, 0.3f, 0f), new Vector3(1.5f, 0.6f, 0.45f));
				break;
			default: // little spirit house by the wayside
				Shapes.Cylinder(g, new Vector3(0f, 0.6f, 0f), 0.08f, 0.1f, 1.2f, new Color(0.88f, 0.86f, 0.8f), 0f, "ShrinePillar");
				Shapes.Box(g, new Vector3(0f, 1.4f, 0f), new Vector3(0.7f, 0.5f, 0.6f), new Color(0.7f, 0.2f, 0.15f), 0f, "ShrineHouse");
				Shapes.Prism(g, new Vector3(0f, 1.85f, 0f), new Vector3(0.95f, 0.4f, 0.85f), Gold, "RoofShrine");
				Shapes.Sphere(g, new Vector3(0f, 1.3f, -0.32f), 0.05f, new Color(1f, 0.7f, 0.3f), 3f, "GlowCandle");
				Shapes.Solid(g, new Vector3(0f, 1f, 0f), new Vector3(0.7f, 2f, 0.6f));
				break;
		}
	}

	// ---------- scattering along roads ----------

	// Walks the road segments and drops props at random along their edges. `perRoad` props per 100 m of
	// road (roughly), faced toward the road, never overlapping anything already recorded.
	static void ScatterRoadside(Node3D parent, string name, float density, int maxProps, System.Func<Vector2, bool> allowed = null)
	{
		var root = Shapes.Group(parent, name);
		int made = 0, stalls = 0;
		var segs = _roads.Where(r => (r.a - r.b).Length() > 6f).ToList();
		if (segs.Count == 0)
			return;
		for (int attempt = 0; attempt < maxProps * 30 && made < maxProps; attempt++)
		{
			var (a, b, half) = segs[_rng.RandiRange(0, segs.Count - 1)];
			if (_rng.Randf() > density)
				continue;
			var d = (b - a).Normalized();
			var side = new Vector2(-d.Y, d.X) * (_rng.Randf() < 0.5f ? -1f : 1f);
			var at = a.Lerp(b, _rng.RandfRange(0.1f, 0.9f)) + side * (half + 1.7f);
			var pos = new Vector3(at.X, 0f, at.Y);
			if (allowed != null && !allowed(at))
				continue;
			float yaw = Mathf.Atan2(side.X, side.Y);
			if (_rng.Randf() < 0.28f)
			{
				// A stall needs more room and some space between neighbours.
				if (Blocked(pos, 2.6f) || DistToRoads(pos) < 1.4f || _stallSpots.Any(s => s.DistanceTo(at) < 24f))
					continue;
				Stall(root, $"Stall{stalls}", pos, yaw, (Ware)(stalls % 8));
				Occupy(pos, 2.6f);
				_stallSpots.Add(at);
				stalls++;
				made++;
				continue;
			}
			var kind = (Prop)_rng.RandiRange(0, 8);
			float r = PropRadius(kind);
			if (Blocked(pos, r) || DistToRoads(pos) < r * 0.6f)
				continue;
			SmallProp(root, $"Prop{made}", pos, yaw, kind);
			Occupy(pos, r);
			made++;
		}
	}

	static readonly List<Vector2> _stallSpots = new();

	// ---------- fences ----------

	enum FenceKind { Slat, Bamboo, Rope, Branches }

	// A run of fence between two points: posts every 2 m plus rails (or woven bamboo, rope, twigs).
	static void FenceRun(Node3D parent, string name, Vector3 a, Vector3 b, FenceKind kind)
	{
		var g = Shapes.Group(parent, name);
		var d = b - a;
		float len = new Vector2(d.X, d.Z).Length();
		if (len < 2f)
			return;
		var dir = new Vector3(d.X, 0f, d.Z).Normalized();
		float yaw = Mathf.Atan2(dir.X, dir.Z);
		int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2f) + 1);
		float h = kind == FenceKind.Rope ? 0.9f : 1.05f;
		var color = kind switch { FenceKind.Bamboo => new Color(0.6f, 0.58f, 0.3f), FenceKind.Branches => DarkWood, _ => Wood };
		for (int i = 0; i < posts; i++)
		{
			var p = a + dir * (len * i / (posts - 1f));
			Shapes.Cylinder(g, new Vector3(p.X, h * 0.5f, p.Z), 0.05f, 0.07f, h, color.Darkened(0.15f), 0f, "FencePost");
		}
		var mid = (a + b) * 0.5f;
		var rail = Shapes.Group(g, "Rails", new Vector3(mid.X, 0f, mid.Z), yaw);
		switch (kind)
		{
			case FenceKind.Slat:
				for (float t = -len * 0.5f + 0.2f; t < len * 0.5f; t += 0.28f)
					Shapes.Box(rail, new Vector3(0f, h * 0.5f, t), new Vector3(0.04f, h * 0.9f, 0.14f), color, 0f, "FenceSlat");
				break;
			case FenceKind.Bamboo:
				foreach (var y in new[] { 0.3f, 0.65f, 0.95f })
					Shapes.Cylinder(rail, new Vector3(0f, y, 0f), 0.035f, 0.035f, len, color, 0f, "BambooRail").Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
				for (float t = -len * 0.5f + 0.15f; t < len * 0.5f; t += 0.3f)
					Shapes.Cylinder(rail, new Vector3(0.02f, h * 0.5f, t), 0.025f, 0.025f, h * 0.95f, color.Lightened(0.05f), 0f, "BambooRail");
				break;
			case FenceKind.Rope:
				foreach (var y in new[] { 0.5f, 0.85f })
					Shapes.Box(rail, new Vector3(0f, y, 0f), new Vector3(0.03f, 0.03f, len), new Color(0.75f, 0.65f, 0.4f), 0f, "RopeLine");
				break;
			default: // twig fence: leaning bundles
				for (float t = -len * 0.5f + 0.2f; t < len * 0.5f; t += 0.35f)
					Shapes.Cylinder(rail, new Vector3(0f, h * 0.5f, t), 0.02f, 0.035f, h * 1.05f, color, 0f, "TwigStick").Rotation = new Vector3(_rng.RandfRange(-0.2f, 0.2f), 0f, _rng.RandfRange(-0.25f, 0.25f));
				break;
		}
		// Slim collider so the fence is felt but never traps (low enough to step off, 0.25 m thick).
		Shapes.Solid(rail, new Vector3(0f, 0.5f, 0f), new Vector3(0.25f, 1f, len));
	}

	// Rustic fences running beside the roads in short pieces with gaps (for lanes and junctions),
	// kept clear of houses, their paths, paddies and trees.
	static void FenceRoads(Node3D parent, string name, float chance, Vector2 pieceLen, float maxRoadHalf, System.Func<Vector2, bool> allowed = null)
	{
		var root = Shapes.Group(parent, name);
		int n = 0;
		foreach (var (a, b, half) in _roads.ToList())
		{
			var d = b - a;
			float len = d.Length();
			if (len < 8f)
				continue;
			d /= len;
			var normal = new Vector2(-d.Y, d.X);
			foreach (float sign in new[] { -1f, 1f })
			{
				float t = _rng.RandfRange(2f, 6f);
				while (t < len - 4f)
				{
					float piece = _rng.RandfRange(pieceLen.X, pieceLen.Y);
					if (t + piece > len - 2f)
						break;
					if (_rng.Randf() < chance)
					{
						var p0 = a + d * t + normal * sign * (half + 1.3f);
						var p1 = a + d * (t + piece) + normal * sign * (half + 1.3f);
						var w0 = new Vector3(p0.X, 0f, p0.Y);
						var w1 = new Vector3(p1.X, 0f, p1.Y);
						var mid = (w0 + w1) * 0.5f;
						if (!Blocked(w0, 0.8f) && !Blocked(w1, 0.8f) && !Blocked(mid, 0.8f) && (allowed == null || allowed(p0) && allowed(p1)))
						{
							FenceRun(root, $"Fence{n++}", w0, w1, (FenceKind)_rng.RandiRange(0, 3));
						}
					}
					t += piece + _rng.RandfRange(5f, 12f);
				}
			}
		}
	}

	// ---------- puddles ----------

	static void Puddles(Node3D parent, int count, System.Func<Vector2, bool> allowed = null)
	{
		var root = Shapes.Group(parent, "Puddles");
		var mat = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.15f, 0.2f, 0.72f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.04f, Metallic = 0.35f };
		for (int i = 0; i < count * 8 && count > 0; i++)
		{
			float x = _rng.RandfRange(-Half + 10f, Half - 10f), z = _rng.RandfRange(-Half + 10f, Half - 10f);
			var pos = new Vector3(x, 0f, z);
			if (allowed != null && !allowed(new Vector2(x, z)))
				continue;
			// Puddles gather at the edges of roads and in yards, never inside a building or on a pier.
			if (Blocked(pos, 2.5f) || DistToRoads(pos) > 5f || DistToRoads(pos) < 1.6f)
				continue;
			float rx = _rng.RandfRange(0.9f, 2.2f), rz = _rng.RandfRange(0.7f, 1.6f);
			var mi = Shapes.Child(root, new MeshInstance3D
			{
				Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.02f, RadialSegments = 20 },
				MaterialOverride = mat,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Position = new Vector3(x, 0.075f, z),
				Scale = new Vector3(rx, 1f, rz),
				Rotation = new Vector3(0f, _rng.Randf() * Mathf.Tau, 0f),
			}, "PuddleWater");
			Occupy(pos, 1.5f);
			count--;
			i += 8;
		}
	}

	// ---------- treasure chests ----------

	// A mystery chest hidden away from the roads. `id` is saved when opened (see Main.Interact).
	static void TreasureChest(Node3D parent, string id, Vector3 pos, float yaw)
	{
		var c = Shapes.Group(parent, $"Chest_{id}", pos, yaw);
		c.AddToGroup("chest", true);
		c.SetMeta("chest_id", id);
		var wood = new Color(0.34f, 0.22f, 0.13f);
		var iron = new Color(0.28f, 0.29f, 0.32f);
		Shapes.Box(c, new Vector3(0f, 0.35f, 0f), new Vector3(1.1f, 0.7f, 0.7f), wood, 0f, "ChestBody");
		var lid = Shapes.Cylinder(c, new Vector3(0f, 0.72f, 0f), 0.35f, 0.35f, 1.1f, wood.Lightened(0.05f), 0f, "ChestLid");
		lid.Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
		foreach (var x in new[] { -0.4f, 0.4f })
			Shapes.Box(c, new Vector3(x, 0.45f, 0f), new Vector3(0.08f, 0.9f, 0.75f), iron, 0f, "ChestBand");
		Shapes.Box(c, new Vector3(0f, 0.6f, -0.37f), new Vector3(0.16f, 0.2f, 0.05f), Gold, 0f, "ChestLock");
		// A yantra-marked paper strip and a faint glow so it can be spotted at night.
		Shapes.Box(c, new Vector3(0f, 0.8f, 0f), new Vector3(0.22f, 0.02f, 0.5f), new Color(0.9f, 0.8f, 0.35f), 0.6f, "GlowYan");
		Shapes.Light(c, new Vector3(0f, 1.1f, 0f), new Color(1f, 0.8f, 0.4f), 0.7f, 4f, "ChestGlow");
		Shapes.Solid(c, new Vector3(0f, 0.4f, 0f), new Vector3(1.1f, 0.8f, 0.7f));
	}
}

public static partial class WorldBuilder
{
	// Five chests in a zone, hidden off the beaten track (well away from roads and buildings, on dry land).
	static void PlaceChests(Node3D parent, string zoneId, int count, System.Func<Vector2, bool> ok, System.Func<Vector2, float> groundY = null)
	{
		var root = Shapes.Group(parent, "Chests");
		int placed = 0;
		for (int attempt = 0; attempt < 600 && placed < count; attempt++)
		{
			float x = _rng.RandfRange(-Half + 14f, Half - 14f), z = _rng.RandfRange(-Half + 14f, Half - 14f);
			var p = new Vector2(x, z);
			var pos = new Vector3(x, 0f, z);
			if (!ok(p) || Blocked(pos, 3.5f) || DistToRoads(pos) < 6f || NearLandmark(pos, 6f))
				continue;
			// Spread them: no two chests close together.
			if (root.GetChildren().OfType<Node3D>().Any(c => new Vector2(c.Position.X - x, c.Position.Z - z).Length() < 70f))
				continue;
			pos.Y = groundY?.Invoke(p) ?? 0f;
			TreasureChest(root, $"{zoneId}_{placed + 1}", pos, _rng.Randf() * Mathf.Tau);
			Occupy(pos, 3f);
			placed++;
		}
	}
}

public static partial class WorldBuilder
{
	// Irrigation ditches (คลองส่งน้ำ) in the 2 m gaps between paddy rows: a strip of water with muddy
	// banks, broken where it would cross a road or a house; a plank bridge carries each road over it.
	static void Canals(Node3D world, int count)
	{
		var root = Shapes.Group(world, "Canals");
		var water = WaterMaterial();
		var bank = new Color(0.3f, 0.25f, 0.17f);
		int made = 0;
		for (int attempt = 0; attempt < 60 && made < count; attempt++)
		{
			// Gap lines lie half-way between plot centres (plots are 13 m apart).
			bool alongX = _rng.Randf() < 0.6f;
			float line = (_rng.RandiRange(-12, 12) + 0.5f) * 13f;
			if (!alongX && Mathf.Abs(line) < 30f || alongX && (line < ThungNaShoreZ + 30f || line > Half - 20f))
				continue;
			float start = _rng.RandfRange(-Half + 30f, 20f), length = _rng.RandfRange(60f, 140f);
			var g = Shapes.Group(root, $"Canal{made}");
			bool any = false;
			for (float t = start; t < start + length; t += 2f)
			{
				var p = alongX ? new Vector3(t, 0f, line) : new Vector3(line, 0f, t);
				if (Mathf.Abs(p.X) > Half - 10f || p.Z < ThungNaShoreZ + 14f || p.Z > Half - 10f)
					continue;
				float road = DistToRoads(p);
				if (Blocked(p, 0.6f) && road > 0.5f)
					continue;
				if (road < 0.6f)
				{
					// A road crosses here: lay planks over the ditch.
					var plank = Shapes.Box(g, p + new Vector3(0f, 0.12f, 0f), alongX ? new Vector3(2.2f, 0.1f, 2.6f) : new Vector3(2.6f, 0.1f, 2.2f), Wood, 0f, "PlankBridge");
					continue;
				}
				var seg = Shapes.Child(g, new MeshInstance3D
				{
					Mesh = new BoxMesh { Size = alongX ? new Vector3(2.05f, 0.02f, 1.1f) : new Vector3(1.1f, 0.02f, 2.05f) },
					MaterialOverride = water,
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
					Position = p + new Vector3(0f, 0.09f, 0f),
				}, "CanalWater");
				seg.AddToGroup("water_surface", true);
				foreach (float s in new[] { -0.75f, 0.75f })
					Shapes.Box(g, p + (alongX ? new Vector3(0f, 0.09f, s) : new Vector3(s, 0.09f, 0f)), alongX ? new Vector3(2.05f, 0.14f, 0.4f) : new Vector3(0.4f, 0.14f, 2.05f), bank, 0f, "MudBank");
				any = true;
			}
			if (any)
			{
				Shapes.MapTag(Shapes.Group(g, "CanalMap", alongX ? new Vector3(start + length * 0.5f, 0f, line) : new Vector3(line, 0f, start + length * 0.5f)), "map_water", alongX ? new Vector2(length, 1.2f) : new Vector2(1.2f, length));
				made++;
			}
		}
	}
}

public static partial class WorldBuilder
{
	// Things that belong to a villager, set around them so you can tell what they do at a glance.
	// Offsets are in the villager's own frame (they face -Z); the group is added to the NPC node.
	public static void NpcDecor(Node3D npc, string id, float yaw)
	{
		_rng ??= new RandomNumberGenerator { Seed = 7 };
		var g = Shapes.Group(npc, "Decor", Vector3.Zero, yaw);
		void Table(Vector3 at, Vector3 size)
		{
			Shapes.Box(g, at + new Vector3(0f, 0.75f, 0f), new Vector3(size.X, 0.08f, size.Z), Wood, 0f, "TableTop");
			foreach (var sx in new[] { -1f, 1f })
			foreach (var sz in new[] { -1f, 1f })
				Shapes.Box(g, at + new Vector3(sx * (size.X * 0.5f - 0.08f), 0.37f, sz * (size.Z * 0.5f - 0.08f)), new Vector3(0.07f, 0.74f, 0.07f), DarkWood, 0f, "TableLeg");
			Shapes.Solid(g, at + new Vector3(0f, 0.4f, 0f), new Vector3(size.X, 0.8f, size.Z));
		}
		void FishTrap(Vector3 at) => Shapes.Cylinder(g, at + new Vector3(0f, 0.3f, 0f), 0.18f, 0.32f, 0.6f, Straw, 0f, "BasketWeave");
		switch (id)
		{
			case "yai_jan": // a little altar: candles, an incense pot, jars of herbal liquor
				Table(new Vector3(0f, 0f, 1.6f), new Vector3(1.4f, 0f, 0.7f));
				Shapes.Cylinder(g, new Vector3(0f, 0.95f, 1.6f), 0.14f, 0.1f, 0.3f, Clay, 0f, "ClayPot");
				foreach (var x in new[] { -0.5f, 0.5f })
					Shapes.Sphere(g, new Vector3(x, 0.92f, 1.55f), 0.05f, new Color(1f, 0.7f, 0.3f), 4f, "GlowCandle");
				for (int i = 0; i < 3; i++)
					Shapes.Cylinder(g, new Vector3(-1.3f + i * 0.28f, 0.22f, 1.2f), 0.1f, 0.12f, 0.45f, new Color(0.45f, 0.32f, 0.15f, 0.9f), 0f, "ClayJar");
				break;
			case "thongdee": // alms bowl on a stand, a monk's fan
				Shapes.Cylinder(g, new Vector3(0.9f, 0.4f, 0.6f), 0.05f, 0.05f, 0.8f, DarkWood, 0f, "Pole");
				Shapes.Sphere(g, new Vector3(0.9f, 0.9f, 0.6f), 0.22f, new Color(0.15f, 0.13f, 0.12f), 0f, "AlmsBowl").Scale = new Vector3(1f, 0.75f, 1f);
				Shapes.Box(g, new Vector3(-0.8f, 0.8f, 0.5f), new Vector3(0.45f, 0.6f, 0.03f), new Color(0.85f, 0.55f, 0.2f), 0f, "ClothFan");
				break;
			case "khon_khap": // the timetable board of the last bus
				Shapes.Cylinder(g, new Vector3(1.4f, 0.9f, 0.3f), 0.05f, 0.05f, 1.8f, Colors.DimGray, 0f, "Pole");
				Shapes.Box(g, new Vector3(1.4f, 1.6f, 0.3f), new Vector3(0.9f, 0.6f, 0.05f), new Color(0.9f, 0.85f, 0.7f), 0f, "SignBoard");
				Shapes.Cylinder(g, new Vector3(-1.2f, 0.4f, 0.8f), 0.3f, 0.3f, 0.8f, new Color(0.6f, 0.15f, 0.12f), 0f, "OilDrum");
				break;
			case "ta_saeng":
			case "lung_khiao": // hay, a hoe and a sickle
				Shapes.Sphere(g, new Vector3(-1.4f, 0.5f, 1f), 0.8f, Straw, 0f, "HayPile").Scale = new Vector3(1f, 0.6f, 1f);
				var hoe = Shapes.Box(g, new Vector3(1.2f, 0.75f, 0.8f), new Vector3(0.05f, 1.5f, 0.05f), Wood, 0f, "HoeHandle");
				hoe.Rotation = new Vector3(0.25f, 0f, 0f);
				Shapes.Box(g, new Vector3(1.2f, 0.08f, 0.55f), new Vector3(0.3f, 0.04f, 0.2f), Colors.DimGray, 0f, "HoeBlade");
				break;
			case "mae_ploy":
			case "mae_khai_rua":
			case "pa_bua": // a charcoal stove with a pot or a grill, and a basket of food
				Shapes.Cylinder(g, new Vector3(0f, 0.35f, -1f), 0.3f, 0.25f, 0.7f, Clay, 0f, "ClayStove");
				Shapes.Sphere(g, new Vector3(0f, 0.72f, -1f), 0.05f, new Color(1f, 0.45f, 0.15f), 3f, "GlowEmber");
				Shapes.Cylinder(g, new Vector3(0f, 0.85f, -1f), 0.28f, 0.25f, 0.28f, new Color(0.25f, 0.25f, 0.27f), 0f, "IronPot");
				FishTrap(new Vector3(0.9f, 0f, -0.7f));
				Shapes.Solid(g, new Vector3(0f, 0.4f, -1f), new Vector3(0.6f, 0.8f, 0.6f));
				break;
			case "lung_puk":
			case "lung_pla":
			case "dek_bet": // fish trap, a net hung to dry
				FishTrap(new Vector3(0.8f, 0f, 0.3f));
				Shapes.Box(g, new Vector3(-1f, 0.5f, 0.6f), new Vector3(0.9f, 0.9f, 0.02f), new Color(0.7f, 0.68f, 0.6f, 0.9f), 0f, "RopeNet");
				break;
			case "pa_yaem": // herbs drying on a tray, mortar and pestle
				Shapes.Cylinder(g, new Vector3(1f, 0.55f, 0.6f), 0.5f, 0.5f, 0.05f, Straw, 0f, "BasketWeave");
				for (int i = 0; i < 8; i++)
					Shapes.Sphere(g, new Vector3(1f + _rng.RandfRange(-0.35f, 0.35f), 0.6f, 0.6f + _rng.RandfRange(-0.35f, 0.35f)), 0.06f, new Color(0.35f, 0.5f, 0.25f), 0f, "HerbBunch");
				Shapes.Cylinder(g, new Vector3(1f, 0.27f, 0.6f), 0.06f, 0.06f, 0.54f, DarkWood, 0f, "Pole");
				Shapes.Cylinder(g, new Vector3(-0.9f, 0.2f, -0.6f), 0.22f, 0.18f, 0.4f, Stone, 0f, "StoneMortar");
				break;
			case "dek_noi":
			case "ai_tee":
			case "dek_kwai": // a rattan ball (takraw) and a kite
				Shapes.Sphere(g, new Vector3(0.7f, 0.12f, -0.6f), 0.12f, Straw, 0f, "BasketWeave");
				var kite = Shapes.Box(g, new Vector3(-0.8f, 0.35f, 0.6f), new Vector3(0.5f, 0.5f, 0.02f), new Color(0.85f, 0.3f, 0.25f), 0f, "ClothKite");
				kite.Rotation = new Vector3(-1.3f, 0f, Mathf.Pi * 0.25f);
				break;
		}
		Textures.Apply(g);
	}
}
