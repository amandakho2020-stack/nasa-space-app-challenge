# Main Menu, Intro Video, Game, Outro Video

```
MainMenu.tscn ──Start──▶ intro video "Earth to moon" ──finished──▶ Main.tscn (the game)
     ▲                                                                   │ game beaten (rocket landed)
     └──────────── finished ◀── outro video "Moon to Earth" ◀────────────┘
```

**All of this is already set up and tested.**
- `MainMenu.tscn` is the project's main scene (*Project Settings → Application → Run → Main Scene*).
- F5 starts at the menu. F6 on `Main.tscn` still jumps straight into the game.

## 0. Videos: Godot 4 plays `.ogv` only

`VideoStreamPlayer` in Godot 4.7 supports **Ogg Theora (`.ogv`)** only; `.mp4` can't be loaded. Both videos were converted with Blender (same size, frame rate and audio):

| Source | Used by the game |
|---|---|
| `Art/Earth to moon.mp4` | `res://Art/Earth to moon.ogv` (1024×576, 30 fps, 19.4 s) |
| `Art/Moon to Earth.mp4` | `res://Art/Moon to Earth.ogv` |

> ⚠ **`Moon to Earth.mp4` is byte-for-byte identical to `Earth to moon.mp4`**, so the outro currently shows the same Earth launch.
> Put the real outro MP4 in `Art/` and convert it with:
>
> ```
> & "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --factory-startup --python Tools/mp4_to_ogv.py -- "Art/Moon to Earth.mp4" "Art/Moon to Earth.ogv"
> ```
>
> No code changes are needed; the game always plays `Art/Moon to Earth.ogv`. Any other converter works too, as long as it outputs **Theora + Vorbis in an .ogg/.ogv container**.

---

## 1. `MainMenu.tscn`: node structure

```
MainMenu (Control, Full Rect)                 ← Scripts/Flow/MainMenu.cs
├── Black (ColorRect, Full Rect, black)          shown behind the video
├── %Background (TextureRect, Full Rect)         Texture = res://Art/AeroKidz_Mainmenu.jpg
│                                                Expand Mode = Ignore Size, Stretch Mode = Keep Aspect Covered
├── %MenuUI (Control, Full Rect, Mouse = Ignore)  everything hidden when Start is pressed
│   ├── %StartButton (Button, Flat)              invisible button over the START painted on the artwork
│   └── %ScoreLabel (Label, top-right)           "YOUR SCORE / HIGH SCORE" after a game
├── %IntroPlayer (VideoStreamPlayer, Full Rect)  Stream = res://Art/Earth to moon.ogv, Expand = on, Visible = off
└── %SkipHint (Label, bottom-right)              "Press SPACE to skip", Visible = off
```

`%Name` = right-click the node → **Access as Unique Name**. The script finds nodes that way, so you can rearrange them freely.

**To build it by hand:**
1. *Scene → New Scene → User Interface*. Rename the root `MainMenu`, then *Layout → Full Rect*.
2. Add **TextureRect** `Background`:
   - drag `Art/AeroKidz_Mainmenu.jpg` into *Texture*
   - *Layout → Full Rect*
   - *Expand Mode = Ignore Size*, *Stretch Mode = Keep Aspect Covered*
3. Add **Control** `MenuUI` (*Full Rect*, *Mouse → Filter = Ignore*). Inside it:
   - a **Button** `StartButton` with *Flat* on, and Styles → normal/focus = *StyleBoxEmpty* (hover = a soft glow if you like)
   - a **Label** `ScoreLabel`
4. Add **VideoStreamPlayer** `IntroPlayer` (*Full Rect*, *Expand* on, *Visible* off) and a **Label** `SkipHint`.
5. Mark the five nodes *Access as Unique Name*. Attach **`Scripts/Flow/MainMenu.cs`** to the root, then save as `res://MainMenu.tscn`.

### Why the Start button is invisible

The artwork already has a big START button painted on it. A normal Godot button would cover it, so the real `Button` is transparent and laid exactly over the painted one.
- `LayoutStartButton()` recalculates its position whenever the window resizes. This matters because *Keep Aspect Covered* crops the image differently at each aspect ratio.
- The painted button's position on the 1024×548 image is the `StartButtonOnImage` export: (345, 432, 340 × 103).
- Hovering shows a gold glow. Enter also starts.

### Signals (connected in code in `_Ready`, so nothing to connect in the Node dock)

```csharp
_startButton.Pressed += OnStartPressed;   // Button.pressed
_intro.Finished      += OnIntroFinished;  // VideoStreamPlayer.finished
```

- `OnStartPressed()`: hides `MenuUI` and `Background`, then shows and plays `IntroPlayer`.
- `OnIntroFinished()`, on `finished` or a skip with Space / Enter / Esc / click:
  1. calls `AeroKidz.GameManager.ResetCampaign()`, which starts a new game while keeping the high score
  2. then `GameFlow.GoTo(GetTree(), "res://Main.tscn")`, a wrapper around `GetTree().ChangeSceneToFile` that also unpauses and frees the mouse

---

## 2. Outro: `OutroVideo` in `Main.tscn`

```
Main (Node3D)
└── OutroVideo (CanvasLayer, Layer 100, Process Mode = Always, Visible = off)   ← Scripts/Flow/OutroVideo.cs
    ├── Backdrop (ColorRect, Full Rect, black)
    ├── Player (VideoStreamPlayer, Full Rect, Expand on)   Stream = res://Art/Moon to Earth.ogv
    └── SkipHint (Label)
```

**Call it from anywhere at the end of the gameplay:**

```csharp
OutroVideo.Play(this);   // finds the scene's OutroVideo (or builds one) and plays it
// or
GetNode<OutroVideo>("/root/Main/OutroVideo").PlayOutro();
```

`PlayOutro()`:
1. pauses the game, so the 3D world, minigames and sounds freeze
2. shows the layer over everything and plays "Moon to Earth"
3. on `finished`, or a skip, runs `GameFlow.GoTo(GetTree(), "res://MainMenu.tscn")`

*Process Mode = Always* is what lets the video run while the game is paused.

**Already hooked up:** `AeroKidz/Scripts/World3DBridge.cs` listens for `LevelController.GameBeaten`, which fires after the rocket game lands on Earth. It stores the score and calls `OutroVideo.Play(this)`. The menu then shows **YOUR SCORE / HIGH SCORE**.

**Building it by hand:**
1. In `Main.tscn` add a **CanvasLayer** `OutroVideo`: *Layer* 100, *Process → Mode = Always*, *Visible* off.
2. Add children **ColorRect** `Backdrop` (black, Full Rect), **VideoStreamPlayer** `Player` (Full Rect, Expand) and **Label** `SkipHint`.
3. Attach **`Scripts/Flow/OutroVideo.cs`** to `OutroVideo`.

---

## 3. One fix needed for the menu to work

AeroKidz used to attach its HUD and minigames to the 3D world **only on the first frame after launch**. Once the game starts at a menu, that first scene isn't `Main.tscn`, so the minigames would never appear.
`AeroKidz/Scripts/GameManager.cs` now also attaches on **`SceneTree.SceneChanged`**, so it attaches every time `Main.tscn` loads.

## Tested (full run from the menu)

1. Menu → Start: the menu hides and the intro plays (1024×576).
2. Intro `finished` → `Main.tscn` with the AeroKidz game attached; the "Welcome to Aero Kidz!" pop-up appears.
3. `GameBeaten` → the outro covers the 3D world and the game is paused.
4. Outro `finished` → back at the menu, unpaused, showing "YOUR SCORE: 1234 | HIGH SCORE: 1514".
5. Start again → Space skips the intro → `Main.tscn` with the game attached again.

| File | Role |
|---|---|
| `Scripts/Flow/GameFlow.cs` | Scene and video paths, `GoTo()` (unpause + change scene), last score |
| `Scripts/Flow/MainMenu.cs` + `MainMenu.tscn` | Menu, intro video, start of game |
| `Scripts/Flow/OutroVideo.cs` | Outro video over the 3D world, back to the menu |
| `Tools/mp4_to_ogv.py` | Blender script: any video → `.ogv` (the folder has `.gdignore`, so Godot skips it) |
