using Godot;
using System;

/// <summary>
/// One cell of the pipe-connection minigame. Drawn procedurally (no textures):
/// orange pipe arms toward each connected side. Left-click rotates it 90° clockwise.
/// </summary>
public partial class PipeTile : Control
{
	public const int N = 1, E = 2, S = 4, W = 8;

	/// <summary>Bitmask of open sides (N/E/S/W).</summary>
	public int Mask;
	public bool IsSource;
	public bool IsSink;
	/// <summary>Set by the puzzle when this tile is connected to the source.</summary>
	public bool Flowing;

	public event Action Rotated;

	private static readonly Color Background = new(0.04f, 0.09f, 0.28f);
	private static readonly Color GridLine = new(0.2f, 0.35f, 0.9f, 0.6f);
	private static readonly Color PipeIdle = new(0.6f, 0.32f, 0.1f);
	private static readonly Color PipeFlow = new(1f, 0.55f, 0.1f);
	private static readonly Color Flange = new(0.85f, 0.42f, 0.05f);

	public PipeTile()
	{
		CustomMinimumSize = new Vector2(64, 64);
		MouseFilter = MouseFilterEnum.Stop;
		MouseDefaultCursorShape = CursorShape.PointingHand;
	}

	public static int RotateMask(int mask) => ((mask << 1) | (mask >> 3)) & 15;

	public void RotateClockwise()
	{
		Mask = RotateMask(Mask);
		QueueRedraw();
		Rotated?.Invoke();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
		{
			RotateClockwise();
			AcceptEvent();
		}
	}

	public override void _Draw()
	{
		Vector2 s = Size;
		Vector2 c = s / 2f;
		float w = s.X * 0.24f;
		Color pipe = Flowing ? PipeFlow : PipeIdle;

		DrawRect(new Rect2(Vector2.Zero, s), Background);
		DrawRect(new Rect2(Vector2.Zero, s), GridLine, false, 1f);
		// small "+" marks at the corners like a blueprint grid
		foreach (Vector2 corner in new[] { Vector2.Zero, new Vector2(s.X, 0), new Vector2(0, s.Y), s })
		{
			DrawLine(corner - new Vector2(4, 0), corner + new Vector2(4, 0), GridLine, 2f);
			DrawLine(corner - new Vector2(0, 4), corner + new Vector2(0, 4), GridLine, 2f);
		}

		float f = w * 1.35f; // flange width
		if ((Mask & N) != 0)
		{
			DrawRect(new Rect2(c.X - w / 2, 0, w, c.Y + w / 2), pipe);
			DrawRect(new Rect2(c.X - f / 2, 2, f, 5), Flange);
		}
		if ((Mask & E) != 0)
		{
			DrawRect(new Rect2(c.X - w / 2, c.Y - w / 2, s.X - c.X + w / 2, w), pipe);
			DrawRect(new Rect2(s.X - 7, c.Y - f / 2, 5, f), Flange);
		}
		if ((Mask & S) != 0)
		{
			DrawRect(new Rect2(c.X - w / 2, c.Y - w / 2, w, s.Y - c.Y + w / 2), pipe);
			DrawRect(new Rect2(c.X - f / 2, s.Y - 7, f, 5), Flange);
		}
		if ((Mask & W) != 0)
		{
			DrawRect(new Rect2(0, c.Y - w / 2, c.X + w / 2, w), pipe);
			DrawRect(new Rect2(2, c.Y - f / 2, 5, f), Flange);
		}

		// Valve markers on the start / finish tiles.
		if (IsSource || IsSink)
		{
			DrawCircle(c, w * 0.95f, Colors.White);
			DrawCircle(c, w * 0.7f, pipe);
			DrawLine(c - new Vector2(0, w * 0.6f), c + new Vector2(0, w * 0.6f), Colors.White, 3f);
		}
	}
}
