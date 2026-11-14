# Aero Kidz: Lunar Outpost — Godot 4 (.NET / C#)

Collect as many **Science points** as you can across 3 missions, while keeping **Oxygen, Power and Shield** above 0%.
Only after **Missions 1, 2 and 3** are complete can the Commander take the rocket back to Earth.

**Run it:** open the folder in Godot 4 .NET → **Build** (hammer icon, top-right) → **F5**.
All art and sounds are made in code, so there are no image or audio files to import.

```
LunarOutpost/
├── project.godot / LunarOutpost.csproj
├── Scenes/Main.tscn               HUD, pop-ups and overlays (the one main scene)
├── Scripts/
│   ├── GameManager.cs             Autoload: resources, choice effects, science, SAVE/LOAD, TimersEnabled switch
│   ├── NasaData.cs                Autoload: NASA DONKI space-weather data (offline fallback built in)
│   ├── LevelController.cs         Day flow as STEPS (pop-up / game), ALL story text, win/loss
│   ├── UIManager.cs               HUD / pop-ups / overlays / QUIT & SAVE button
│   ├── NovaPopup.cs               Nova, the robot that pops up and talks
│   ├── BaseMap.cs                 The outpost map (laid out like the team sketch) - to be replaced by the 3D base
│   ├── Sfx.cs                     Sound effects generated in code
│   └── Art.cs                     Shared drawing helpers
└── Minigames/
    ├── MinigameBase.cs            Timer, Win()/Fail(), Say() for Nova, Sound(), floating texts
    ├── WaterCatchGame.cs          M1 Day 1
    ├── HarvestGame.cs             M1 Day 3
    ├── MoonWalkGame.cs            M2: walk OUT to the drill / walk BACK (finds the broken circuit)
    ├── DrillGame.cs               M2 Day 4 - temperature gauge
    ├── HeatProbeGame.cs           M2 Day 6 - HMR heat probe: heat up the germs in the water (has a timer, uses Power)
    ├── BlindCircuitGame.cs        M2 Day 6
    ├── SunWatcherGame.cs          M3 Day 7
    ├── StormGauntletGame.cs       M3 Day 9
    ├── RocketLaunchGame.cs        the very end: fly home
    └── RoomTasks/                 Day 9 room repairs: PipeTask (O2), SolarPuzzleTask (Power), ShieldOrbitTask (Shield)
```

## The outpost (BaseMap.cs) - you really WALK around it
```
            OXYGEN                  DRILL ② (outside, through the airlock)
   POWER ──JUNCTION─────── HUB ─────────── PLANT ①
            SHIELD                    TELESCOPE ③              NOVA
```
- **Click a room** and the astronaut walks there through the corridors (or use WASD / arrows).
- Walk INTO a mission room (Plant / Drill / Telescope) to start that mission.
- Walk INTO the Oxygen / Power / Shield room during a mission to do its repair and refill that bar.
- The Core Drill is **outside**: the path goes out through the airlock.

## The flow
Game start → **Welcome to Aero Kidz! 🚀** (goal: science points) → the outpost map.
Starting Mission 1 → Nova pops up and says *"Welcome to the Moon! … space farmers …"*.

**Mission 1 (Plant Room)**
1. Catch water drops (the plant lights use Power: catch batteries).
2. After 6 drops the game freezes: **ALERT! Oxygen leak** → Oxygen −30%.
   - 🔵 **Run to the Oxygen Room** (safe, +100): the water game stops (the water you caught is kept) → you WALK through the corridors to the Oxygen Room → pipe puzzle → Oxygen 100% → walk back to the Plant Room.
   - 🟢 **Keep Collecting Water** (science, +500): the same water game continues, but you are SLOW and oxygen leaks faster.
3. **Harvest**: the plants grew from your water. Lots of water = big healthy radishes; little water = small, withered and unripe ones (less science).

| Mission | Day 1 | Hazard (bar drops 30%) | 🔵 A | 🟢 B | Final day |
|---|---|---|---|---|---|
| **2 Core Drill** | **Drill (~20 s): hold to drill, keep the dial in the GREEN, red = jam** | Earth germs → **Power −30%** | HMR heater: **+500**, Power 10%, darkout | Save power (+100): walk to the **Power Room** → puzzle → Power 100% | Walk back → *found a broken circuit!* → circuit game |
| **3 Telescope** | Sun watcher | Solar flare (live NASA class) → **Shield −30%** | Keep studying: **+1000**, Shield 0% at the storm | Walk to the **Shield Room** → meteors → Shield 100%, +50 | 3-minute storm (NASA data) |

The **safe** option always means WALKING to that bar's room to earn it back (Oxygen Room / Power Room / Shield Room).
After all 3 missions: **TIME TO GO HOME!** → walk to the rocket → 3, 2, 1, LIFTOFF → final score.

## Buttons during any mission (bottom-left)
- **LEAVE - WALK TO ROOMS**: pause the game and go back to the outpost. Walk to the Oxygen / Power / Shield room to refill that bar (+45), then walk back INTO the mission room to carry on exactly where you stopped. (Hidden on Day 9, where the storm map has its own rooms.)
- **SAVE & EXIT**: keep this attempt's science points, save to disk and go back to the map. The mission is not marked complete; start it again to finish it.
- Progress (best score per mission, completed missions, choices) is saved to `user://aero_kidz_save.cfg`. "Play Again" wipes it.

## Controls
- Water catch: just **move the mouse** (or A / D).
- Outpost map: **click a room** to walk there through the corridors.
- Harvest, moon walk, storm, rocket: **click where to walk** (or hold the mouse; WASD / arrows also work).
- All mouse input uses click EVENTS, so quick touchpad taps work too.
- Drill: hold the mouse button or Space. Telescope: aim with the mouse, click.
- Circuit + solar puzzle: **click, then click** where it goes (dragging also works).

## Nova (one message at a time)
- Nova shows ONE message at a time. Nothing disappears by itself: kids read at their own speed and click **NEXT ▶** (or press Enter).
- Story lines (`Step.NovaSay(...)`) pause the game until NEXT is clicked. Hints during play are `Tip`s: only one at a time, a newer one replaces the old one.
- Nova hides while any pop-up, hazard alert or repair panel is open, so nothing overlaps.
- Nova only SPEAKS (computer voice) in the Mission 3 storm, where she reads the real NASA numbers out loud.

## Bars (Oxygen / Power / Shield)
- They carry over from mission to mission. They only refill to 100% when a mission is FINISHED (every task done).
- If a mission fails, is retried or is left with SAVE & EXIT, the bars go back to what they were when that mission started.
- The storm (Mission 3, Day 9) lasts **2 minutes** and the bars drop faster there (`NasaData.StormSpeedUp`).

## Writing for kids (6-12)
Short sentences, simple words, one idea per line, bullet points for controls. KEEP the real science words (hydroponics, muscle atrophy, regolith, bioburden, HMR, planetary protection, electrolysis, Kp index, CME, proton flux) and explain each one in a few words. Keep Nova's lines extra short - she says them out loud.

## Testing switches (GameManager.cs)
- `UnlockAllForTesting = true` → all 3 missions can be started straight away. Set `false` for the real game.
- `TimersEnabled = false` → no time limits / rocket clock.

## Timers
`GameManager.TimersEnabled = false` turns OFF every time limit and the rocket clock. Set it to `true` to bring them all back.
Day 9 still lasts 3:00, because surviving the storm for 3 minutes is how you win it.

## NASA data (Day 8 + Day 9)
`NasaData.cs` loads the last 60 days from `https://ccmc.gsfc.nasa.gov/DONKI-API` (no key needed):
strongest flare **classType** → Day 8 alert, **Kp index** → Power drain, **CME speed** → Shield drain, **proton flux** → Oxygen drain.
No internet → saved real DONKI data from May 2014 (the pop-up says so).

## Where to change things
- **Story text, buttons, which games run in which order** → `Missions` table in `LevelController.cs` (each day is a list of `Step.Show(...)` pop-ups, `Step.Play(...)` games and `Step.Walk(room, ...)` walks)
- **Outpost layout** (room positions, corridors) → `_nodes` and `Corridors` at the top of `BaseMap.cs`
- **What each choice does, science points, hazard drop** → `ApplyTradeOff()`, `ApplyHazardDrop()`, `HazardDrop` in `GameManager.cs`
- **Difficulty** → the `// ---- Tuning ----` constants at the top of each minigame

## Main.tscn (unique `%` nodes the code needs: don't rename them)
```
Main (Control)
├── %LevelController (Node)            LevelController.cs
├── %MapLayer (Control)                BaseMap.cs
├── %MinigameContainer (Control)       minigames are created here in code
└── UI (CanvasLayer)                   UIManager.cs
    ├── HUD → TopBar (%OxygenBar/%PowerBar/%ShieldBar + %…Value), %ScienceLabel,
    │         %DayPanel (%DayLabel, %TimerLabel), %ObjectiveBanner (%ObjectiveLabel),
    │         %RocketPanel (%RocketClockLabel), %RocketWarning (%RocketWarningLabel),
    │         ActionButtons (%LeaveButton, %QuitSaveButton)
    ├── %TradeOffPanel   %TradeOffTitle, %TradeOffMessage, %OptionAButton (blue), %OptionBButton (green)
    ├── %InfoPanel       %InfoTag, %InfoTitle, %InfoBody, %InfoButton
    ├── %MissionFailedScreen  %FailReasonLabel, %RetryButton, %FailMapButton
    ├── %LevelCompleteScreen  %CompleteSummaryLabel, %NextMissionButton, %CompleteMapButton
    ├── %GameBeatenScreen     %FinalScoreLabel, %PlayAgainButton
    └── %Nova                 NovaPopup.cs (any script: Say("..."))
```

## Playing in the team's 3D outpost (res://Main.tscn)
Press **F5** on the project: the 3D base opens and the AeroKidz games run on top of it.
`World3DBridge.cs` joins them (it starts by itself when the 3D scene runs):
- Every room has a glowing **screen** (`RoomScreen3D.cs`). Walk up to it and press **E** to play that room's game:
  Plant = Mission 1, **Core Drill** = Mission 2 (outside: take the rover from the Rover Bay and drive south), Telescope = Mission 3,
  Oxygen / Power / Shield = the repair games that refill the bars.
- The room you should go to glows yellow and shows a **▼ GO HERE ▼** marker. You can see it through walls.
- Mission 2: after the germ game you **drive back** and walk in through the airlock (in 3D this replaces the 2D moon walk).
- While a game, pop-up or Nova message is open, the astronaut stops and the mouse is free to click.
  When you walk again, the mouse turns the camera (WASD to move, E to use, Esc frees the mouse).
- The 3D team's own test loop (suit O2 drain, Sols, the old pipe consoles, their stats panel) is switched off,
  so there is ONE set of bars. This uses the `ExternalControl` / `ExternalInputLock` switches in `Scripts/GameManager.cs`.
- Screen positions: the `Screens` table at the top of `World3DBridge.cs`.
- `AeroKidz/Scenes/Main.tscn` still runs the 2D version on its own (for testing minigames quickly).

## Art files (Assets/)
- `Assets/space/` - craftpix.net *Free Space Shooter Game Objects*: meteors, the blue shield arc, explosion frames and the heart, all used in the Shield Room.
- `Assets/energy/` - craftpix.net *Free Cyberpunk Energy Items*: battery (water game), HMR heater (germ game), oxygen cells / solar panel / dome (storm rooms, shield base).
- Licence: craftpix.net free licence (see the LICENSE files in each folder).
- Draw a picture from code with `Art.Picture(this, "res://Assets/...png", center, size)` or `Art.PictureRotated(...)`.
- Shield Room: block 10 meteors. It slowly speeds up; big meteors need 2 hits, comets are fast, later meteors can come from 2 sides, and hearts give +1 life if you let them through. Tune it at the top of ShieldOrbitTask.cs.
