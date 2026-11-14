# Lunar Outpost — Godot 4 (.NET / C#) Hackathon Skeleton

Open the folder in **Godot 4.3+ .NET edition** → press **Build** (hammer, top-right) → **F5**.
The game is fully playable end-to-end right away (map → 3 missions → choices → fail/retry → game beaten).
Each "challenge day" is a survival timer until your team plugs in the real minigames.

```
LunarOutpost/
├── project.godot            GameManager registered as Autoload, 1280x720
├── LunarOutpost.csproj
├── Art/
│   ├── base_map.jpg         Map background (your base concept art)
│   └── Concepts/            All 8 mockups for reference
├── Scenes/Main.tscn         The ONE main scene (HUD + map + popups + overlays)
├── Scripts/
│   ├── GameManager.cs       Autoload: resources, modifiers, level index, ResetLevel()
│   ├── LevelController.cs   Day flow, timers, drains, win/loss, retry/next
│   └── UIManager.cs         Bars, Option A/B, wire repair, Retry/Next screens
└── Minigames/
    └── MinigameTemplate.cs  Copy this for each minigame
```

---

## PART 1 — `Main.tscn` node hierarchy

`%` = **Access as Unique Name** is ON (right-click node → *Access as Unique Name*).
**Designer rule:** you can move, restyle, re-parent, or wrap any `%` node in containers. Just **don't rename it or change its type**. The code finds `%` nodes by name, not by path.

```
Main (Control, Full Rect)
├── %LevelController (Node)                ← LevelController.cs
├── Background (TextureRect)               base_map.jpg, Expand Mode = Ignore Size, Stretch = Keep Aspect Covered
├── %MapLayer (Control, Full Rect, Mouse = Ignore)          mission select over the base art
│   ├── %Mission1Button (Button)           over BIO-DOME
│   ├── %Mission2Button (Button)           over CORE DRILL
│   └── %Mission3Button (Button)           over SHIELD GENERATOR
├── %MinigameContainer (Control, Full Rect, Mouse = Ignore) minigame scenes get spawned in here
└── UI (CanvasLayer)                       ← UIManager.cs
    ├── HUD (Control, Full Rect, Mouse = Ignore)
    │   ├── TopBar (PanelContainer, top-center)            neon panel
    │   │   └── Bars (HBoxContainer)
    │   │       ├── OxygenGroup (VBoxContainer)
    │   │       │   ├── OxygenHeader (HBox) → OxygenTitle (Label) + %OxygenValue (Label)
    │   │       │   └── %OxygenBar (ProgressBar)   cyan   #40D9FF
    │   │       ├── PowerGroup  → PowerHeader → PowerTitle + %PowerValue,   %PowerBar  (orange #FFAD1F)
    │   │       └── ShieldGroup → ShieldHeader → ShieldTitle + %ShieldValue, %ShieldBar (magenta #D959FF)
    │   ├── SciencePanel (PanelContainer, top-left) → %ScienceLabel (Label)
    │   ├── %DayPanel (PanelContainer, top-right) → DayVBox → %DayLabel, %TimerLabel   ("Day 3: The Harvest" / "T-MINUS 00:49")
    │   ├── %ObjectiveBanner (PanelContainer) → %ObjectiveLabel             ("M1: HARVEST THE RADISHES!")
    │   └── %NovaToast (PanelContainer, bottom) → NovaRow → NovaName, %NovaLabel   robot assistant messages
    ├── %TradeOffPanel (Control, Full Rect, hidden)       Day 2 / 5 / 8 popup
    │   ├── Dim (ColorRect)                                blocks clicks behind
    │   └── Card (PanelContainer) → VBox
    │       ├── %TradeOffTitle (Label)                     "DONKI ALERT! - SOLAR FLARE!"
    │       ├── SpeechRow → SpeechBubble (white Panel) → %TradeOffMessage (Label) + NovaTag
    │       └── Buttons (HBox) → %OptionAButton (yellow/red) + %OptionBButton (blue)
    ├── %WireRepairPanel (Control, hidden)                 Mission 2 Day 6, panels broken
    │   └── Card → VBox → Columns
    │       ├── LeftCol  → %WireLeft1..3 (Button)
    │       └── RightCol → %WireRight1..3 (Button)
    ├── %MissionFailedScreen (Control, hidden) → Dim + Card(red)
    │   └── %FailReasonLabel, %RetryButton, %FailMapButton
    ├── %LevelCompleteScreen (Control, hidden) → Dim + Card(green)
    │   └── %CompleteSummaryLabel, %NextMissionButton, %CompleteMapButton
    └── %GameBeatenScreen (Control, hidden) → Dim + Card(gold)
        └── %FinalScoreLabel, %PlayAgainButton
```

**Styling (already set in the scene, easy to swap for textures):**
- ProgressBars: `Theme Overrides > Styles > Background / Fill` = StyleBoxFlat with 10px corner radius + 2px neon border. *Show Percentage* is off because `%XxxValue` labels show the number.
- To use painted art like the mockups, replace the StyleBoxFlat with a **StyleBoxTexture**, or swap a ProgressBar for a **TextureProgressBar**. If you swap the type, tell the coder: `UIManager` expects `ProgressBar` (change the 3 `U<ProgressBar>` lines to `U<TextureProgressBar>`; both have `.Value`).
- Nova portrait: add a `TextureRect` next to `NovaName` / `NovaTag` with the robot sprite. No code needed.
- Everything that should not block clicks uses **Mouse > Filter = Ignore**.

---

## PART 2 — How the scripts talk

```
           signals                         signals
GameManager ───────► UIManager ◄─────── LevelController
 (Autoload)            │  button presses        ▲
     ▲                 └────────────────────────┘
     └── LevelController / minigames call ApplyDrain(), ChangeResources(), ApplyTradeOff()
```

| Who | Owns | Never touches |
|---|---|---|
| `GameManager` | Oxygen/Power/Shield, MaxPower, PlayerSpeedMultiplier, OxygenDrainMultiplier, SolarPanelsBroken, level index, science, `ResetLevel()` | Any node |
| `LevelController` | Day loop, timer, drains, win/loss, retry/next | Any UI node |
| `UIManager` | Every HUD/popup/overlay node | Game rules |

### Mission flow
| Mission | Day (timer) | Choice day | Final day | Win |
|---|---|---|---|---|
| 1 Bio-Dome | Day 1 · 30 s | Day 2 | Day 3 · 45 s | survive → unlock M2 |
| 2 Core Drill | Day 4 · 30 s | Day 5 | Day 6 · 45 s (+ wire repair if panels broken) | survive → unlock M3 |
| 3 Finale | Day 7 · 30 s | Day 8 | Day 9 Gauntlet · **3:00** | timer 0:00 → GAME BEATEN |

Any resource at 0% → **Mission Failed** → **Retry Mission** calls `GameManager.ResetLevel()`: all resources go back to 100%, modifiers are cleared, and science from that attempt is discarded.
All durations and drain rates are in the `Missions` table at the top of `LevelController.cs`.

### Design decision you should know about
**Mission 3 Choice A sets Shield to 0%**, and the rule is "any resource at 0% = fail". Taken literally, Choice A would fail instantly. So the shield is marked **OFFLINE** instead: it shows "OFFLINE", it isn't drained, and it doesn't count for the loss check. The danger comes from the ×3 oxygen drain. If you'd rather make it brutal, delete the `!ShieldOffline &&` check in `GameManager.GetDepletedResource()` and set `Shield = 10f` instead of 0.

---

## Plugging in a minigame (C# coder)
1. Copy `Minigames/MinigameTemplate.cs`. Make a scene with a full-rect `Control` root and attach the script.
2. Keep the signal `Finished(bool won)`. Emit `true` to win early and `false` to fail. If you never emit it, the player wins when the day timer hits 0:00.
3. Select `Main/LevelController` and drag the scene into its Inspector slot (e.g. **Mission 1 → Mission1 Final Scene**).
4. Use `GameManager.Instance.PlayerSpeedMultiplier`, `.ApplyDrain(...)` and `.ChangeResources(...)`. The HUD updates by itself.

Suggested mapping from the mockups: M1 Day 1 = water-drop catch, M1 Day 3 = radish harvest, M2 Day 4 = electrolysis, M2 Day 6 = drill temperature gauge, M3 Day 7 = solar dust storm, M3 Day 9 = shield deflector.

## 48-hour team split
- **UI designer:** only edits `Main.tscn` styling and art, and adds minigame visuals. Never rename `%` nodes.
- **C# coder:** only edits `Scripts/` and `Minigames/*.cs`.
- **Third person:** builds minigame scenes from the template and tunes the `Missions` table.
- Because they're in separate files, Git merge conflicts stay rare. Avoid two people editing `Main.tscn` at the same time.
