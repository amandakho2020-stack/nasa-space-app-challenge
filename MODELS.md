# Lunar Outpost: Final Models, Camera Fixes, Rover Boarding & Camera Transitions

## Rebuild order (Inspector buttons, then Ctrl+S)

1. **EnvironmentBuilder** → *Build Lunar Environment*: surface, base, rooms, corridors (collision + span markers), lights, rover body.
2. **SpriteSheetSlicer** → *Slice Sheet + Install On Player*: 2.5D sprite (kept as a fallback; hidden when the 3D astronaut is used).
3. **ArtReplacer** → *Replace Graybox With Models*: procedural props for anything without a final model.
4. **ModelBaseBuilder** → *Build Models*: the final `.glb` models from `res://Art/models/`.

Step 4 alone is enough when you only change model settings.

---

## 1. Automated model placement: `Scripts/Models/ModelBaseBuilder.cs`

### Setup (already done in `Main.tscn`)

1. Add a **Node3D** at the scene root and attach `ModelBaseBuilder.cs`. Name it `ModelBaseBuilder`.
2. Click **Reset Placements To Defaults**. That fills *Placements* with one entry per model, plus one *Corridor: …* entry per model corridor.
3. Click **Build Models**.

### Tweaking offsets (live)

Expand **Placements → [n]** in the Inspector. Every entry is a `ModelPlacement` resource with:

| Field | Meaning |
|---|---|
| **Position** *(Vector3)* | Where the model goes, relative to *Parent Path* (the scene root when empty) |
| **Rotation Degrees** *(Vector3)* | Its rotation; Y turns it left/right |
| **Scale** | Extra uniform scale on top of the auto-fit |

With **Live Update** on, these three apply instantly in the editor while you drag the values. No rebuild needed.
Everything else (fit, pivot, collision, hiding) needs **Build Models** again.

| Field | Meaning |
|---|---|
| Fit / Fit Size | Auto-scale by Height, Width or Footprint. This fixes models exported at odd scales. |
| Unit Scale | Applied first. `hallway.glb` was exported in centimeters, so it uses 0.01. |
| Center XZ / Ground Y | Moves the model so its footprint is centered and its lowest point sits on the origin. This fixes off-center and floating pivots. |
| Exclude Mesh Names | Meshes to drop. The corridor drops "Door module" so the tiles are open at both ends. |
| Parent Path | Attach under another node, e.g. `Player/Visual` or `LunarRover`. |
| Hide Nodes / Hide Other Geometry Under Parent | Hides the graybox it replaces. **Rendering only, so its collision keeps working.** |
| Collision | None, Box or Convex. Generated on layers *world* + *camera_blocker*. |
| Animation | Autoplays and loops the model's own animation (holo-table, shield generator). |
| Double Sided Shadows | Stops sunlight leaking through thin walls (used on the corridors). |
| Fill Length / Interior Camera Blockers | Corridor tiling and camera walls (see §2). |

### What each model needed (measured on import)

| Model | Native size / problem | Default placement |
|---|---|---|
| astronaut(character) | 1.0 m tall, static (no skeleton or animations) | `Player/Visual`, fit to 1.75 m, turned 180° to face forward. Replaces the sprite; walking is suggested procedurally (hop and lean). |
| Lunar_rover (Transport Outdoor) | 3.1 m long along **X** | `LunarRover`, Y −0.76 (wheels on the ground), rotated −90° so its nose points down the vehicle's +Z drive axis. The old rover graybox is hidden; the physics wheels are unchanged. |
| Communication tower | Pivot 8.4 m off-center, 2.5 m below ground | (20, 0, −17), next to the lobby. Replaces the procedural roof tower. |
| control_room_monitor | Pivot at mid-height; faces +X | Lobby north wall, 6 m wide, rotated −90°. Replaces the procedural monitor bank. |
| holo-table | Animated globe | Lobby center, 2.6 m wide. Replaces the CSG holo projector. Animation loops. |
| computer desk (lobby) ×2 | **16 m** wide, pivot 40 m away | Lobby, either side of the spawn point, fit to 3.4 m wide |
| oxygen_station | Pivot 3.4 m above its base | Oxygen room east side, fit to 3 m tall |
| monitor (power room) | Wall-mounted (bottom at 1.26 m) | Power room north wall, at its native mounting height |
| shield_generator | 9.5 m wide, animated | Shield room center, fit to 3.2 m tall (3.8 m ceiling) |
| solar_panel_5x10 | Pivot off-center | Replaces the solar field (20 m footprint) |
| Solar station | 12.5 m tall | Beside the solar field |
| hallway | **394 m** (centimeters); a **kit of 6 separate 2 m tiles** | Unit Scale 0.01 × Scale 1.5, as an annex by the Rover Bay. To use single tiles, set *Exclude Mesh Names* to the tiles you don't want. |
| corridor | 5.4 × 4.2 × 17.5 m; 3 open 5 m sections plus a closed **door module** | One entry per model corridor: door module excluded, fit to 4.33 m wide (2 m interior headroom), tiled and stretched to exactly fill the tube between the two rooms. |

**Corridors:** EnvironmentBuilder's **Model Corridor Tubes** lists which tubes use `corridor.glb`. The list is West, Oxygen, Power, Shield, Plant and Telescope.
- **The tube's own shell is skipped.** Its procedural liner stays as invisible collision, and the doorways are still cut into the rooms.
- **East corridor and Rover Bay spur stay procedural.** They form a T-junction, and a straight module would put a wall across the airlock spur.
- **Performance:** corridor.glb is about 337k triangles per 12 m tile. If frame rate suffers, disable some corridor entries; those tubes fall back to the procedural look after an EnvironmentBuilder rebuild with the tube removed from the list.

---

## 2. Corridor visibility fix (camera clipping and light leaks)

There were three causes, and each has a fix.

**A. Camera clipping (`Player/CameraPivot/SpringArm3D/Camera3D`)**

| Node | Property | Value |
|---|---|---|
| Camera3D | **Near** | **0.05**. It's exported on PlayerController as *Camera Near*. Don't go far below this: 0.01 works but causes z-fighting on distant terrain. |
| Camera3D | Far | 2000 |
| SpringArm3D | **Collision Mask** | **world (1) + camera_blocker (4)** |
| SpringArm3D | **Shape** | SphereShape3D, radius **0.3**. The arm sweeps a sphere, not a ray, so the camera can't slip through gaps. |
| SpringArm3D | **Margin** | **0.25** |
| (rover) SpringArm3D | Same mask; margin 0.3; it excludes the rover's own body | |

PlayerController applies these in `ConfigureCamera()`, so the Inspector exports are the single place to tune them.

**B. Collision layers keep the camera inside**

- *Project Settings → Layer Names → 3D Physics*: layer 1 = `world`, layer 4 = `camera_blocker`.
- The base shell and every corridor structure use **Collision Layer = world + camera_blocker** (EnvironmentBuilder → *Wall Collision Layers*).
- Model corridors get an extra **CameraBlockers** body on `camera_blocker` only: ceiling and wall boxes matching the model's interior (half-width 2.3, height 2.48 native, scaled). The camera can't rise into the model's ceiling cavity, where it would see the sky, and these walls don't affect the player.

**C. Rendering**

- **Double-sided materials:** interior wall, deck and steel materials have **Cull Mode = Disabled**. If the camera is ever pressed into a wall, it sees steel, not the surface beyond.
- **Double-sided shadows:** the shell, the corridor structures and the corridor models use **Cast Shadow = Double-Sided**, so the sun can't leak through thin walls.

---

## 3. Rover boarding: the astronaut stays visible

Rewritten in `PlayerController.EnterVehicle / ExitVehicle` and `RoverController.Board / Exit`.

**Boarding:**
- The `CharacterBody3D` stops: `SetPhysicsProcess(false)`, `SetProcessUnhandledInput(false)`, collision shape disabled. Nothing is freed or hidden.
- **`Player/Visual` is reparented onto the rover's `Seat` Marker3D**, using the pose from *Seated Offset* (0, −1.0, 0.05) and *Seated Rotation Degrees* (−10, 0, 0) on RoverController. The astronaut model or sprite rides along, sitting in the rover.
- The collision-less player body is carried with the seat, so the minimap, the pressurized-zone check and the O2 logic still follow the rover.

**Exit (E, only below 2 m/s):** `Visual` is reparented back to the player with its original transform, physics and input resume, and collision returns after two physics frames. That delay keeps the teleport from shoving the rover.

**Inspector:**
- Select `LunarRover/Seat` (Marker3D, rotated 180° so the astronaut faces the rover's nose) and move it onto the seat of your rover model.
- Then tune **Seated Offset / Seated Rotation Degrees** on the `LunarRover` node.
- The imported astronaut has no sitting animation, so the seated pose is the standing model lowered and tilted back. A rigged astronaut with a "sit" animation would drop straight in.

---

## 4. Smooth camera transitions: `Scripts/CameraBlender.cs`

`CameraBlender.Blend(from, to, seconds)` creates a temporary camera at the source camera's exact transform and FOV. Each frame it eases toward the **live** target camera:
- **Ease:** smootherstep.
- **Rotation:** quaternion slerp.
- **Path:** a slight upward crane arc.
- **Field of view:** interpolated too.

It then makes the target current and frees itself. Starting a new blend mid-blend continues from where the camera is, so there's no pop.

| Moment | Blend | Setting (on `LunarRover`) |
|---|---|---|
| Board | Player camera → rover chase camera | *Board Blend Seconds* = 1.0 |
| Exit | Rover chase camera → player camera, which is pre-aimed along the rover's heading so the move is short | *Exit Blend Seconds* = 0.8 |

**Chase-camera lag:** the rover's `CameraPivot` is now top-level and follows the rover with exponential smoothing.
- *Chase Position Stiffness* = 10.
- *Chase Yaw Stiffness* = 4: the camera swings round lazily when you turn, like a modern driving game.
- Mouse orbit still works on top, and the camera recenters after 1.2 s.
