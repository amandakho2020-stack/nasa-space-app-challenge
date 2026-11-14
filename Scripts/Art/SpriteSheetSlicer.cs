using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Turns the astronaut spritesheet into a <see cref="SpriteFrames"/> resource and installs a
/// Y-billboarded <see cref="AnimatedSprite3D"/> on the player.
///
/// The sheet is NOT a uniform grid (rows of 5 / 3 / 5, uneven spacing), so instead of fixed
/// hframes/vframes the slicer finds every astronaut by its alpha silhouette (connected
/// components), sorts them in reading order (frame 0 = top-left), and wraps each one in an
/// AtlasTexture whose margin pads it to one shared frame size with the feet on a common
/// baseline - so frames never jitter when the animation plays.
///
/// Usage: add this node to the scene, click "Slice Sheet + Install On Player", Ctrl+S.
/// </summary>
[Tool]
public partial class SpriteSheetSlicer : Node
{
	[ExportToolButton("Slice Sheet + Install On Player", Icon = "AnimatedSprite3D")]
	public Callable SliceButton => Callable.From(SliceAndInstall);

	[Export(PropertyHint.File, "*.png")] public string SheetPath = "res://Art/Sprites/astronaut_sheet.png";
	[Export] public string FramesPath = "res://Art/Sprites/astronaut_frames.tres";
	[Export] public NodePath PlayerVisual = "../Player/Visual";

	[ExportGroup("Detection")]
	[Export(PropertyHint.Range, "0.01,1")] public float AlphaThreshold = 0.2f;
	/// <summary>Small fragments closer than this (px) to an astronaut are attached to it.</summary>
	[Export] public int MergeGap = 6;
	[Export] public int MinPixels = 300;

	[ExportGroup("Sprite")]
	/// <summary>Height of the tallest frame in metres.</summary>
	[Export] public float TargetHeight = 1.75f;

	/// <summary>
	/// Animations as "name:fps:loop:frames". Frame indices are in reading order of the sheet:
	///   row 1:  0 front idle | 1 back | 2 side (faces left) | 3 side walk (faces left) | 4 front 3/4
	///   row 2:  5 stride (faces right) | 6 walk 3/4 right | 7 wave
	///   row 3:  8 stride right | 9 walk toward camera | 10 cheer | 11 crouch-step right | 12 low-g bound right
	/// The side-walk frames face RIGHT, so PlayerController flips the sprite (FlipH) when moving left.
	/// </summary>
	[Export(PropertyHint.MultilineText)] public string Animations =
		"idle:1:true:0\n" +
		"walk:6:true:8,5,12,11\n" +
		"walk_front:4:true:9,6\n" +
		"walk_back:2:true:1\n" +
		"jump:3:true:12,5\n" +
		"wave:2.5:true:7,10,7,0";

	public void SliceAndInstall()
	{
		var tex = GD.Load<Texture2D>(SheetPath);
		Image img = Image.LoadFromFile(ProjectSettings.GlobalizePath(SheetPath));
		if (tex == null || img == null)
		{
			GD.PushError($"SpriteSheetSlicer: cannot load {SheetPath}. Let the editor import it first.");
			return;
		}

		List<Rect2I> boxes = DetectSprites(img, AlphaThreshold, MergeGap, MinPixels);
		if (boxes.Count == 0)
		{
			GD.PushError("SpriteSheetSlicer: no sprites found (is the background transparent?).");
			return;
		}

		SpriteFrames frames = BuildFrames(tex, boxes, out Vector2I cell);
		frames.TakeOverPath(FramesPath);
		Error err = ResourceSaver.Save(frames, FramesPath);
		if (err != Error.Ok)
			GD.PushError($"SpriteSheetSlicer: could not save {FramesPath} ({err}).");

		Install(frames, cell);
		GD.Print($"SpriteSheetSlicer: {boxes.Count} frames ({cell.X}x{cell.Y} px cells), animations: " +
			string.Join(", ", frames.GetAnimationNames()) + ". Save the scene (Ctrl+S).");
	}

	// ======================================================================
	//  Detection: alpha connected components -> merged boxes -> reading order
	// ======================================================================

	public static List<Rect2I> DetectSprites(Image source, float alphaThreshold, int mergeGap, int minPixels)
	{
		Image img = (Image)source.Duplicate();
		if (img.IsCompressed())
			img.Decompress();
		img.Convert(Image.Format.Rgba8);
		int w = img.GetWidth(), h = img.GetHeight();
		byte[] data = img.GetData();
		byte cut = (byte)(alphaThreshold * 255f);

		var visited = new bool[w * h];
		var comps = new List<(Rect2I box, int count)>();
		var stack = new Stack<int>();
		for (int start = 0; start < w * h; start++)
		{
			if (visited[start] || data[start * 4 + 3] <= cut)
				continue;
			int minX = w, minY = h, maxX = 0, maxY = 0, count = 0;
			visited[start] = true;
			stack.Push(start);
			while (stack.Count > 0)
			{
				int p = stack.Pop();
				int x = p % w, y = p / w;
				count++;
				minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
				minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
				for (int k = 0; k < 4; k++)
				{
					int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (nx < 0 || ny < 0 || nx >= w || ny >= h)
						continue;
					int q = ny * w + nx;
					if (visited[q] || data[q * 4 + 3] <= cut)
						continue;
					visited[q] = true;
					stack.Push(q);
				}
			}
			comps.Add((new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1), count));
		}

		// Big components are whole astronauts (never merged with each other, even when they touch);
		// small fragments (a detached glove, an antenna tip) are attached to the nearest astronaut.
		var boxes = comps.Where(c => c.count >= minPixels).Select(c => c.box).ToList();
		foreach (var (box, _) in comps.Where(c => c.count < minPixels))
		{
			int best = -1;
			float bestDist = mergeGap + 0.5f;
			for (int i = 0; i < boxes.Count; i++)
			{
				float dx = Mathf.Max(0, Mathf.Max(boxes[i].Position.X - box.End.X, box.Position.X - boxes[i].End.X));
				float dy = Mathf.Max(0, Mathf.Max(boxes[i].Position.Y - box.End.Y, box.Position.Y - boxes[i].End.Y));
				float d = Mathf.Max(dx, dy);
				if (d < bestDist)
				{
					bestDist = d;
					best = i;
				}
			}
			if (best >= 0)
				boxes[best] = boxes[best].Merge(box);
		}
		if (boxes.Count == 0)
			return boxes;

		// Reading order: cluster into rows by vertical centre, then left to right.
		float avgH = (float)boxes.Average(b => b.Size.Y);
		var rows = new List<List<Rect2I>>();
		foreach (Rect2I b in boxes.OrderBy(b => b.Position.Y + b.Size.Y / 2f))
		{
			float cy = b.Position.Y + b.Size.Y / 2f;
			List<Rect2I> row = rows.FirstOrDefault(r => Mathf.Abs(r.Average(x => x.Position.Y + x.Size.Y / 2f) - cy) < avgH * 0.5f);
			if (row == null)
				rows.Add(row = new List<Rect2I>());
			row.Add(b);
		}
		return rows.SelectMany(r => r.OrderBy(b => b.Position.X)).ToList();
	}

	// ======================================================================
	//  SpriteFrames
	// ======================================================================

	private SpriteFrames BuildFrames(Texture2D tex, List<Rect2I> boxes, out Vector2I cell)
	{
		const int pad = 2;
		cell = new Vector2I(boxes.Max(b => b.Size.X) + pad * 2, boxes.Max(b => b.Size.Y) + pad * 2);

		var atlases = new List<AtlasTexture>();
		foreach (Rect2I b in boxes)
		{
			// Centre horizontally, feet on the bottom of the cell.
			float left = Mathf.Floor((cell.X - b.Size.X) / 2f);
			float top = cell.Y - pad - b.Size.Y;
			atlases.Add(new AtlasTexture
			{
				Atlas = tex,
				Region = new Rect2(b.Position, b.Size),
				Margin = new Rect2(left, top, cell.X - b.Size.X, cell.Y - b.Size.Y),
				FilterClip = true,
			});
		}

		var frames = new SpriteFrames();
		foreach (string line in Animations.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string[] parts = line.Split(':');
			if (parts.Length < 4)
				continue;
			string name = parts[0];
			frames.AddAnimation(name);
			frames.SetAnimationSpeed(name, double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
			frames.SetAnimationLoopMode(name, parts[2].Trim().ToLowerInvariant() == "true" ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
			foreach (string idx in parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries))
				if (int.TryParse(idx.Trim(), out int i) && i >= 0 && i < atlases.Count)
					frames.AddFrame(name, atlases[i]);
		}
		if (frames.HasAnimation("default") && frames.GetAnimationNames().Length > 1)
			frames.RemoveAnimation("default");
		return frames;
	}

	// ======================================================================
	//  Player install
	// ======================================================================

	private void Install(SpriteFrames frames, Vector2I cell)
	{
		var visual = GetNodeOrNull<Node3D>(PlayerVisual);
		if (visual == null)
		{
			GD.PushWarning($"SpriteSheetSlicer: {PlayerVisual} not found; frames saved but not installed.");
			return;
		}
		Node root = Owner ?? GetParent();

		var old = visual.GetNodeOrNull("Sprite");
		if (old != null)
		{
			visual.RemoveChild(old);
			old.Free();
		}

		float pixel = TargetHeight / cell.Y;
		var sprite = new AnimatedSprite3D
		{
			Name = "Sprite",
			SpriteFrames = frames,
			Animation = frames.HasAnimation("idle") ? "idle" : frames.GetAnimationNames()[0],
			Autoplay = frames.HasAnimation("idle") ? "idle" : "",
			PixelSize = pixel,
			Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, // Y-billboard: upright, turns to face the camera
			Shaded = false,                                     // flat-lit like the illustration; PlayerController dims it in shadow
			DoubleSided = true,
			AlphaCut = SpriteBase3D.AlphaCutMode.Discard,       // crisp edges + proper depth + shadows
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
			Position = new Vector3(0f, cell.Y * pixel / 2f, 0f), // feet on the ground
		};
		visual.AddChild(sprite);
		sprite.Owner = root;

		// Hide the old 3D body (capsule meshes / procedural astronaut model).
		foreach (Node child in visual.GetChildren())
			if (child is GeometryInstance3D g && child != sprite)
				g.Visible = false;
	}
}
