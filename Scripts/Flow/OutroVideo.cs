using Godot;

/// <summary>
/// End-of-game video over the 3D world. Lives in Main.tscn as a hidden CanvasLayer:
///   OutroVideo (CanvasLayer, Layer 100, Process Mode = Always, Visible off)  ← this script
///   ├── Backdrop   (ColorRect, black, Full Rect)
///   ├── Player     (VideoStreamPlayer, Full Rect, Expand on)
///   └── SkipHint   (Label)
///
/// Call it at the end of the gameplay:
///     OutroVideo.Play(this);          // from any node in the scene
///     // or: GetNode&lt;OutroVideo&gt;("/root/Main/OutroVideo").PlayOutro();
///
/// It pauses the game, plays "Moon to Earth", and when the VideoStreamPlayer's <c>finished</c>
/// signal fires it goes back to MainMenu.tscn. Space / Enter / Esc / click skips.
/// In this project World3DBridge calls it automatically when the game is beaten (rocket landed).
/// </summary>
public partial class OutroVideo : CanvasLayer
{
	private VideoStreamPlayer _player;
	private Label _skipHint;
	private bool _playing;
	private bool _leaving;

	public override void _Ready()
	{
		_player = GetNode<VideoStreamPlayer>("Player");
		_skipHint = GetNode<Label>("SkipHint");
		ProcessMode = ProcessModeEnum.Always; // keeps running while the game is paused
		_player.Finished += OnOutroFinished;
		Visible = false;
	}

	/// <summary>Finds the scene's OutroVideo (or makes one if the scene has none) and plays it.</summary>
	public static void Play(Node anyNodeInScene)
	{
		SceneTree tree = anyNodeInScene.GetTree();
		Node scene = tree.CurrentScene ?? tree.Root;
		var outro = scene.GetNodeOrNull<OutroVideo>("OutroVideo") ?? CreateIn(scene);
		outro.PlayOutro();
	}

	/// <summary>Shows the video over everything and plays it; returns to the main menu when it finishes.</summary>
	public void PlayOutro()
	{
		if (_playing || _leaving)
			return;
		var stream = ResourceLoader.Exists(GameFlow.OutroVideo) ? GD.Load<VideoStream>(GameFlow.OutroVideo) : null;
		if (stream == null)
		{
			GD.PushWarning($"OutroVideo: '{GameFlow.OutroVideo}' not found - going straight back to the menu.");
			OnOutroFinished();
			return;
		}
		_playing = true;
		GetTree().Paused = true;              // freeze the 3D world, minigames and their sounds
		Input.MouseMode = Input.MouseModeEnum.Hidden;
		_player.Stream = stream;
		Visible = true;
		_skipHint.Visible = true;
		_player.Play();
	}

	/// <summary>VideoStreamPlayer.finished (or skip): back to the main menu.</summary>
	private void OnOutroFinished()
	{
		if (_leaving)
			return;
		_leaving = true;
		_player.Stop();
		GameFlow.GoTo(GetTree(), GameFlow.MainMenuScene);
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (!_playing || _leaving)
			return;
		if (e.IsActionPressed("ui_accept") || e.IsActionPressed("ui_cancel")
			|| e is InputEventKey { Pressed: true, Keycode: Key.Space }
			|| e is InputEventMouseButton { Pressed: true })
		{
			GetViewport().SetInputAsHandled();
			OnOutroFinished();
		}
	}

	/// <summary>Fallback when Main.tscn has no OutroVideo node: builds the same nodes in code.</summary>
	private static OutroVideo CreateIn(Node scene)
	{
		var outro = new OutroVideo { Name = "OutroVideo", Layer = 100 };
		var backdrop = new ColorRect { Name = "Backdrop", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop };
		backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var player = new VideoStreamPlayer { Name = "Player", Expand = true, MouseFilter = Control.MouseFilterEnum.Ignore };
		player.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var hint = MakeSkipHint();
		outro.AddChild(backdrop);
		outro.AddChild(player);
		outro.AddChild(hint);
		scene.AddChild(outro);
		return outro;
	}

	internal static Label MakeSkipHint()
	{
		var hint = new Label { Name = "SkipHint", Text = "Press SPACE to skip", HorizontalAlignment = HorizontalAlignment.Right };
		hint.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		hint.OffsetLeft = -320; hint.OffsetTop = -56; hint.OffsetRight = -24; hint.OffsetBottom = -20;
		hint.AddThemeFontSizeOverride("font_size", 20);
		hint.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
		hint.AddThemeColorOverride("font_outline_color", Colors.Black);
		hint.AddThemeConstantOverride("outline_size", 6);
		return hint;
	}
}
