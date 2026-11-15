using Godot;

/// <summary>
/// Scene and video paths for the whole game flow, plus a safe scene switch:
///   MainMenu.tscn --Start--> intro video --finished--> Main.tscn --game beaten--> outro video --finished--> MainMenu.tscn
///
/// Godot 4 plays Ogg Theora (.ogv) video only, so the .mp4 files in Art/ were converted to .ogv
/// (same names). Replace a video by overwriting the .ogv file (see VIDEOS.md for how to convert).
/// </summary>
public static class GameFlow
{
	public const string MainMenuScene = "res://MainMenu.tscn";
	public const string GameScene = "res://Main.tscn";
	public const string IntroVideo = "res://Art/Earth to moon.ogv";
	public const string OutroVideo = "res://Art/Moon to Earth.ogv";

	/// <summary>Score of the game that just ended (shown on the main menu), or null.</summary>
	public static int? LastFinalScore;

	/// <summary>Unpauses, frees the mouse and changes scene (a paused tree would stay paused in the new scene).</summary>
	public static void GoTo(SceneTree tree, string scenePath)
	{
		tree.Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		tree.ChangeSceneToFile(scenePath);
	}
}
