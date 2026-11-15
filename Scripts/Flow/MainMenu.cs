using Godot;

/// <summary>
/// Main menu: full-screen AeroKidz artwork with a Start button. Start hides the menu, plays the
/// intro video ("Earth to moon"), and when the VideoStreamPlayer's <c>finished</c> signal fires it
/// loads the game (res://Main.tscn). Space / Enter / Esc / click skips the video.
///
/// Attach to the root Control of MainMenu.tscn. Child nodes (found by Unique Name, %Name):
///   %Background   TextureRect        the menu artwork (Expand Mode = Ignore Size, Stretch = Keep Aspect Covered)
///   %MenuUI       Control            everything that belongs to the menu (hidden when the video starts)
///   %StartButton  Button             invisible button laid over the START painted on the artwork
///   %ScoreLabel   Label              "HIGH SCORE" / "YOUR SCORE" after a finished game
///   %IntroPlayer  VideoStreamPlayer  the intro video (hidden until Start)
///   %SkipHint     Label              "Press SPACE to skip"
/// </summary>
public partial class MainMenu : Control
{
	/// <summary>Where the START button is painted on the artwork, in the image's own pixels (1024 x 548).</summary>
	[Export] public Rect2 StartButtonOnImage = new(345f, 432f, 340f, 103f);

	private TextureRect _background;
	private Control _menuUi;
	private Button _startButton;
	private Label _scoreLabel;
	private VideoStreamPlayer _intro;
	private Label _skipHint;
	private bool _leaving;

	public override void _Ready()
	{
		_background = GetNode<TextureRect>("%Background");
		_menuUi = GetNode<Control>("%MenuUI");
		_startButton = GetNode<Button>("%StartButton");
		_scoreLabel = GetNode<Label>("%ScoreLabel");
		_intro = GetNode<VideoStreamPlayer>("%IntroPlayer");
		_skipHint = GetNode<Label>("%SkipHint");

		GetTree().Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		// Button + signal connections (equivalent to connecting them in the Node dock).
		_startButton.Pressed += OnStartPressed;
		_intro.Finished += OnIntroFinished;
		Resized += LayoutStartButton;
		_background.Resized += LayoutStartButton;

		_intro.Visible = false;
		_skipHint.Visible = false;
		ShowScores();
		CallDeferred(MethodName.LayoutStartButton);
		_startButton.GrabFocus(); // Enter / gamepad A also starts
	}

	private void OnStartPressed()
	{
		if (_leaving)
			return;

		// 1. hide the menu  2. play the intro
		_menuUi.Visible = false;
		_background.Visible = false;

		var stream = ResourceLoader.Exists(GameFlow.IntroVideo) ? GD.Load<VideoStream>(GameFlow.IntroVideo) : null;
		if (stream == null)
		{
			GD.PushWarning($"MainMenu: intro video '{GameFlow.IntroVideo}' not found - starting the game directly.");
			OnIntroFinished();
			return;
		}
		_intro.Stream = stream;
		_intro.Visible = true;
		_skipHint.Visible = true;
		_intro.Play();
		Input.MouseMode = Input.MouseModeEnum.Hidden;
	}

	/// <summary>VideoStreamPlayer.finished (or skip): start a fresh game in the 3D outpost.</summary>
	private void OnIntroFinished()
	{
		if (_leaving)
			return;
		_leaving = true;
		_intro.Stop();
		AeroKidz.GameManager.Instance?.ResetCampaign(); // new game every time; the high score is kept
		GameFlow.LastFinalScore = null;
		GameFlow.GoTo(GetTree(), GameFlow.GameScene);
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (!_intro.Visible || _leaving)
			return;
		bool skip = e.IsActionPressed("ui_accept") || e.IsActionPressed("ui_cancel")
			|| (e is InputEventKey { Pressed: true, Keycode: Key.Space })
			|| (e is InputEventMouseButton { Pressed: true });
		if (skip)
		{
			GetViewport().SetInputAsHandled();
			OnIntroFinished();
		}
	}

	/// <summary>Keeps the invisible Start button exactly over the START painted on the artwork, at any window size.</summary>
	private void LayoutStartButton()
	{
		if (_background.Texture == null)
			return;
		Vector2 img = _background.Texture.GetSize();
		Vector2 area = _background.Size;
		float scale = Mathf.Max(area.X / img.X, area.Y / img.Y); // Keep Aspect Covered
		Vector2 offset = (area - img * scale) / 2f + _background.Position;
		_startButton.Position = offset + StartButtonOnImage.Position * scale;
		_startButton.Size = StartButtonOnImage.Size * scale;
	}

	private void ShowScores()
	{
		int high = AeroKidz.GameManager.Instance?.HighScore ?? 0;
		if (GameFlow.LastFinalScore is int last)
			_scoreLabel.Text = $"YOUR SCORE: {last}\nHIGH SCORE: {high}";
		else
			_scoreLabel.Text = high > 0 ? $"HIGH SCORE: {high}" : "";
	}
}
