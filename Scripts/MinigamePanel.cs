using Godot;
using System.Collections.Generic;

/// <summary>
/// Placeholder module minigame: a pipe-connection grid. The grid is generated with a
/// guaranteed path from the left valve to the right valve, then every tile is scrambled.
/// Tiles connected to the source light up. The SOLVE button always completes the module
/// (placeholder hook) and tells the GameManager to advance the Sol.
///
/// Attach to a PanelContainer. Children are found by Unique Name:
///   %MinigameTitle (Label), %MinigameTimer (Label), %PipeGrid (GridContainer),
///   %FlowStatus (Label), %SolveButton (Button), %AbortButton (Button)
/// </summary>
public partial class MinigamePanel : PanelContainer
{
	[Export] public int Columns = 6;
	[Export] public int Rows = 5;

	private Label _title;
	private Label _timer;
	private Label _status;
	private GridContainer _grid;
	private Button _solveButton;
	private Button _abortButton;

	private PipeTile[,] _tiles;
	private Vector2I _start;
	private Vector2I _end;
	private readonly RandomNumberGenerator _rng = new();

	private static readonly Vector2I[] DirOffsets = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) }; // N E S W
	private static readonly int[] DirBits = { PipeTile.N, PipeTile.E, PipeTile.S, PipeTile.W };
	private static readonly int[] OppositeBits = { PipeTile.S, PipeTile.W, PipeTile.N, PipeTile.E };

	public override void _Ready()
	{
		_title = GetNode<Label>("%MinigameTitle");
		_timer = GetNode<Label>("%MinigameTimer");
		_status = GetNode<Label>("%FlowStatus");
		_grid = GetNode<GridContainer>("%PipeGrid");
		_solveButton = GetNode<Button>("%SolveButton");
		_abortButton = GetNode<Button>("%AbortButton");

		_solveButton.Pressed += OnSolvePressed;
		_abortButton.Pressed += OnAbortPressed;
		Visible = false;
	}

	public void Open(MissionConsole console)
	{
		_title.Text = $"{console.MissionName.ToUpper()}\n{console.MinigameTitle}";
		BuildPuzzle();
		Visible = true;
	}

	public void Close() => Visible = false;

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		var gm = GameManager.Instance;
		if (gm != null)
			_timer.Text = $"O2 {gm.Oxygen:0}%   {gm.ClockText()}";
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("ui_cancel"))
		{
			OnAbortPressed();
			GetViewport().SetInputAsHandled();
		}
	}

	private void OnSolvePressed()
	{
		Close();
		GameManager.Instance?.CompleteActiveMission();
	}

	private void OnAbortPressed()
	{
		Close();
		GameManager.Instance?.CancelActiveMission();
	}

	// ======================================================================
	//  Puzzle generation
	// ======================================================================

	private void BuildPuzzle()
	{
		foreach (Node child in _grid.GetChildren())
		{
			_grid.RemoveChild(child);
			child.QueueFree();
		}
		_grid.Columns = Columns;
		_rng.Randomize();

		int[,] masks = new int[Columns, Rows];

		// Carve a monotonic left-to-right path: within each column move vertically, then step east.
		int row = _rng.RandiRange(0, Rows - 1);
		_start = new Vector2I(0, row);
		masks[0, row] |= PipeTile.W; // inlet on the left edge
		for (int col = 0; col < Columns; col++)
		{
			int targetRow = _rng.RandiRange(0, Rows - 1);
			while (row != targetRow)
			{
				int step = targetRow > row ? 1 : -1;
				masks[col, row] |= step > 0 ? PipeTile.S : PipeTile.N;
				row += step;
				masks[col, row] |= step > 0 ? PipeTile.N : PipeTile.S;
			}
			masks[col, row] |= PipeTile.E;
			if (col < Columns - 1)
				masks[col + 1, row] |= PipeTile.W;
		}
		_end = new Vector2I(Columns - 1, row); // outlet on the right edge

		// Fill the rest with random decoy pieces.
		int[] decoys = { PipeTile.N | PipeTile.S, PipeTile.N | PipeTile.E, PipeTile.N | PipeTile.E | PipeTile.S };
		_tiles = new PipeTile[Columns, Rows];
		for (int r = 0; r < Rows; r++)
		{
			for (int c = 0; c < Columns; c++)
			{
				int mask = masks[c, r] != 0 ? masks[c, r] : decoys[_rng.RandiRange(0, decoys.Length - 1)];
				int turns = _rng.RandiRange(0, 3);
				for (int i = 0; i < turns; i++)
					mask = PipeTile.RotateMask(mask);

				var tile = new PipeTile
				{
					Mask = mask,
					IsSource = new Vector2I(c, r) == _start,
					IsSink = new Vector2I(c, r) == _end,
				};
				tile.Rotated += UpdateFlow;
				_grid.AddChild(tile); // GridContainer fills row by row
				_tiles[c, r] = tile;
			}
		}
		UpdateFlow();
	}

	/// <summary>Flood-fills from the source and lights up every connected tile.</summary>
	private void UpdateFlow()
	{
		foreach (PipeTile t in _tiles)
			t.Flowing = false;

		bool reachedEnd = false;
		PipeTile source = _tiles[_start.X, _start.Y];
		if ((source.Mask & PipeTile.W) != 0)
		{
			var queue = new Queue<Vector2I>();
			queue.Enqueue(_start);
			source.Flowing = true;
			while (queue.Count > 0)
			{
				Vector2I p = queue.Dequeue();
				PipeTile tile = _tiles[p.X, p.Y];
				if (p == _end && (tile.Mask & PipeTile.E) != 0)
					reachedEnd = true;

				for (int d = 0; d < 4; d++)
				{
					if ((tile.Mask & DirBits[d]) == 0)
						continue;
					Vector2I n = p + DirOffsets[d];
					if (n.X < 0 || n.Y < 0 || n.X >= Columns || n.Y >= Rows)
						continue;
					PipeTile next = _tiles[n.X, n.Y];
					if (next.Flowing || (next.Mask & OppositeBits[d]) == 0)
						continue;
					next.Flowing = true;
					queue.Enqueue(n);
				}
			}
		}

		foreach (PipeTile t in _tiles)
			t.QueueRedraw();

		_status.Text = reachedEnd
			? "FLOW RESTORED - press SOLVE to finish the repair."
			: "Click pipes to rotate them. (Prototype: SOLVE always completes the module.)";
		_status.Modulate = reachedEnd ? new Color(0.5f, 1f, 0.6f) : new Color(0.75f, 0.85f, 1f);
	}
}
