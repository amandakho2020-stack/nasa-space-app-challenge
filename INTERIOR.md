# Lunar Outpost: 2.5D Astronaut, Starship Interior & Lighting

All of this is already applied to `Main.tscn`. To rebuild after changes, use the three tool nodes **in this order**:

1. **EnvironmentBuilder** → *Build Lunar Environment*: surface, base, corridors, rooms, console, lights.
2. **SpriteSheetSlicer** → *Slice Sheet + Install On Player*: the 2.5D astronaut.
3. **ArtReplacer** → *Replace Graybox With Models*: the 3D props. It keeps the sprite and doesn't add the 3D astronaut.

Then press **Ctrl+S**.

---

## 1. 2.5D player sprite

### Node tree

```
Player (CharacterBody3D + PlayerController.cs)
├── CollisionShape3D            CapsuleShape3D r 0.35, h 1.8 (unchanged)
├── Visual (Node3D)             still turns toward movement (used by the minimap arrow)
│   ├── Body/Helmet/Visor/Backpack (MeshInstance3D)   hidden (old 3D body, kept for Restore)
│   └── Sprite (AnimatedSprite3D)                     ← new
└── CameraPivot / SpringArm3D / Camera3D              unchanged third-person orbit camera
```

### The sheet is not a uniform 5×3 grid

`Art/Sprites/astronaut_sheet.png` (500×500, transparent) has rows of **5 / 3 / 5** astronauts with uneven spacing.
Slicing it with *Add frames from sprite sheet* at 5×3 cuts the middle row in half. Two options:

**Automatic (used here):** `SpriteSheetSlicer.cs` finds each astronaut by its transparency outline (13 found) and numbers them in reading order. Each frame is padded to one shared 103×149 cell with the feet on a common baseline, so the animation doesn't jitter. The result is saved as `Art/Sprites/astronaut_frames.tres`.

| Index | Pose | Index | Pose |
|---|---|---|---|
| 0 | front idle | 7 | wave |
| 1 | back | 8 | stride (faces right) |
| 2 | side (faces left) | 9 | walking toward camera |
| 3 | side walk (faces left) | 10 | cheer (arms up) |
| 4 | front ¾ | 11 | crouch-step (faces right) |
| 5 | stride (faces right) | 12 | low-gravity bound (faces right) |
| 6 | walk ¾ (faces right) | | |

**Manual Inspector steps (if you want to do it by hand):**
1. Select `Player/Visual`, add a child **AnimatedSprite3D** named `Sprite`. In **Sprite Frames**, choose **New SpriteFrames**, then click it to open the SpriteFrames panel.
2. Rename `default` to `idle`. Click **Add frames from a sprite sheet** (grid icon), choose `astronaut_sheet.png`, and set **Horizontal 5, Vertical 3**. Select the frames you need. The middle row's three astronauts won't line up with the grid, so for those, instead add an **AtlasTexture** frame with a custom **Region** (in pixels: (43,188,95,136), (206,187,81,137), (360,188,89,136)).
3. Create the animations below with their **FPS** and **Loop** settings:

| Animation | Frames | FPS | Loop |
|---|---|---|---|
| `idle` | 0 | 1 | on |
| `walk` | 8, 5, 12, 11 | 6 | on |
| `walk_front` | 9, 6 | 4 | on |
| `walk_back` | 1 | 2 | on |
| `jump` | 12, 5 | 3 | on |
| `wave` | 7, 10, 7, 0 | 2.5 | on |

4. Set the **AnimatedSprite3D** Inspector properties:

| Property | Value | Why |
|---|---|---|
| Flags → **Billboard** | **Y-Billboard** | Stays upright and turns to face the camera |
| Flags → Shaded | off | So a back-lit sprite never turns into a black cut-out. PlayerController dims it in shadow instead. |
| Flags → Double Sided | on | |
| Flags → **Alpha Cut** | **Discard** | Crisp edges, correct depth sorting, casts shadows |
| Pixel Size | **0.01174** | 149 px cell → 1.75 m tall |
| Position | (0, 0.875, 0) | Feet on the ground |
| Texture Filter | Linear Mipmaps | |
| Autoplay | `idle` | |

5. In the **Import** dock for the PNG, set *Detect 3D → Compress To* to **Disabled**, then click **Reimport**. Otherwise Godot VRAM-compresses it the first time it's seen in 3D, which blurs the edges.

### How `PlayerController.cs` drives the sprite (`UpdateSprite`)

- **Camera-relative velocity:** horizontal velocity is projected onto the camera's right and forward axes (the orbit yaw).
- **Animation choice:**
  - Airborne → `jump`.
  - Moving mostly *into* the screen → `walk_back`.
  - Moving mostly *toward* the camera → `walk_front`.
  - Moving otherwise → `walk`.
  - Standing still → `idle`; after 8 s standing it plays `wave`.
- **FlipH:** the side-walk frames face right (`SpriteFacesRight = true`). When moving screen-left (sideways component < −0.25 m/s) the sprite flips; small diagonal jitter is ignored.
- **Speed and bounce:** animation speed follows walking speed, so it slows after the Sol 2 muscle-atrophy penalty. A low-gravity bounce plays while walking, and a gentle breathing bob while idle.
- **Shadow dimming:** every 0.15 s outdoors, a ray toward the sun checks for shadow. In shadow the sprite fades to 42 % brightness; indoors it stays at 95 %.

Settings exported on the Player: `SpriteFacesRight`, `WalkThreshold`, `WaveAfterIdleSeconds`, `WalkBounce`.

---

## 2. Opaque corridors

Each corridor gets two combiners under `Base/Interiors/Corridors`:

```
<Tube>Structure (CSGCombiner3D, Use Collision ON, Cast Shadow = Double-Sided)
├── Liner        CSGPolygon3D Spin 360°, 8 sides, profile r 1.50–1.56 → octagonal tube, flat floor & ceiling
├── Rib1..n      CSGPolygon3D Spin, r 1.36–1.60, 0.18 m thick, every ~2.4 m      (dark steel)
│   └── FloorCut CSGBox3D Subtraction  (removes the rib where it would cross the floor)
├── Bulkhead*    CSGPolygon3D Spin, r 1.24–1.60, at both doorways                 (gray steel)
└── Opening_*    CSGBox3D Subtraction where a spur joins (Rover Bay)
<Tube>Trim (CSGCombiner3D, no collision, no shadows)
├── RibGlow*     cyan rings on every other rib
├── HazardRing*  glowing hazard-stripe ring inside each bulkhead
├── FloorGrate, FloorGlow± (cyan, floor level), WallGlow± (blue), Conduit±, CeilingLight
```

Plus one **ReflectionProbe** per corridor (Once, Interior, Box Projection).

**Why they're fully opaque:**
- The liner is a closed solid ring inside the old shell.
- The shell now casts **double-sided** shadows, so sunlight can't leak through its thin walls.
- The corridors have no windows. The only windows left are the two big lobby windows and the glass Bio-Dome, which is intentional.

**The octagon trick:** a 360° spin with 8 sides gives an octagon with a corner at the profile start. The ring's frame is rotated 22.5° about the tube axis (`TubeRingBasis`), so a flat face lands exactly at floor level: 1.65 − 1.5·cos 22.5° = 0.264 m, just above the 0.25 m room floors.

---

## 3. Rooms, command console, materials

### Room detailing (`<Room>Detail`, one CSGCombiner3D per room = one mesh)

| Room type | Generated |
|---|---|
| Box rooms (Oxygen, Power, Shield) | Dark steel columns every ~2.4 m (blue light bar on every other one); white plate wall panels; dark kick plates; gray upper panels; continuous cyan accent band; ceiling beams; center floor grate; glowing hazard threshold at each door |
| Octagonal (Lobby, hubs) | Corner columns with cyan/blue light bars; panels on every facet except doors and lobby windows; radial ceiling beams; cyan ceiling halo ring; octagonal floor grate; door thresholds |
| Telescope | Ring of columns, blue accent ring, floor grate |

Panel layouts skip the doorway on any wall a corridor enters, so doors stay clear (verified by walking every route).

### Command console (Lobby)

```
CommandConsole
├── Body        CSGPolygon3D Spin 124°, 28 sides, desk profile (lip 0.8 m → slanted face → deck 1.2 m), collision ON
├── Kickplate, LipGlow (cyan), FloorGlow (blue), BackGlow (cyan)       same spin, thin profiles
├── Segment0..6 (Node3D, one per 17.7°)
│   ├── PanelScreen   bezel + emissive panel + Label3D on the slanted face   (live data)
│   └── Monitor       angled monitor (tilted 14°) facing the operator         (live data)
└── Hardware (CSGCombiner3D): monitor housings and stands, keyboards and key rows, 4 toggle switches
    (tilted levers) with LEDs, and a 3×2 colored button grid per segment
HoloProjector
├── Base / Step (dark and gray steel), EmitterRing (cyan torus), EmitterDisc (blue), 8 vents
├── LightCone   CSGCylinder3D cone, flipped, additive hologram material, no shadow
├── Hologram (Node3D + HoloSpin.cs): Moon sphere, orbit ring, satellite (spins and bobs)
└── HoloLabel   "LUNAR OUTPOST / SURFACE OPS" (billboard Label3D)
```

The arc is centered on north, facing the monitor bank. Operators stand on the inside of the arc, facing out toward the monitors.
Godot 4.7.2 sweeps a Spin profile from +X toward −Z (measured: `SpinSweepSign = -1`), and the console rotation is computed from that.

**Screens:**
- Each screen is a near-black panel with a dim emissive glow in its text color, plus a `Label3D` with the text.
- A `LiveScreen.cs` on the label replaces `{MET}`, `{SOL}`, `{O2}`, `{PWR}`, `{OBJ}`, `{STATUS}`, `{SCI}`, `{TIME}` at runtime. Example: "FLIGHT STATUS: NOMINAL / SOL 1/9 MET 0:00:02:25".

### Material setup (`Materials/Interior/`, generated)

All plate materials use a procedural normal map and are mapped with **World Triplanar**, so CSG UVs never matter.

| Material | Albedo | Metallic / Roughness | Normal map | UV1 scale |
|---|---|---|---|---|
| `wall_white` | (0.90, 0.91, 0.93) × grime | 0.35 / 0.28 (glossy) | Steel plates: staggered plates, bevelled seams, rivets | 0.28 (~1.8 × 0.9 m plates) |
| `metal_gray` | (0.50, 0.52, 0.56) | 0.75 / 0.32 | Steel plates | 0.28 |
| `metal_dark` | (0.13, 0.14, 0.16) | 0.80 / 0.38 | Steel plates | 0.28 |
| `deck` (+ per-room tint, 35 %) | (0.34, 0.35, 0.38) | 0.70 / 0.42 | 1 m deck plates with diamond tread | 0.30 |
| `grate` | Dark gaps, light bars | 0.80 / 0.45 | Bar grid | 1.0 |
| `hazard_stripes` | Yellow and black diagonal | — / 0.5 | — | 2.0, **Emission = same texture, 0.45** |
| `glow_cyan` / `glow_blue` / `glow_white` | Emission (0.25, 0.85, 1) / (0.22, 0.42, 1) / (0.95, 0.97, 1) | — | — | Energy 3 / 2.6 / 2 |
| `hologram` | (0.25, 0.8, 1, **0.35**) | Unshaded, **Blend Add**, Cull Disabled | — | — |

**How the normal maps are made:** a height image is drawn in C# (`PlateHeight`, `GrateHeight`), then turned into a normal map with `Image.BumpMapToNormalMap()`. Seams are also darkened in the albedo for grime.

**To do it by hand:** NormalMap ON → New **NoiseTexture2D** or an image texture with *As Normal Map*; UV1 → Triplanar ON, World Triplanar ON.

---

## 4. Lighting

| Where | Setting |
|---|---|
| WorldEnvironment → Ambient Light | Color **(0.11, 0.11, 0.125)**, Energy 1. Shadowed moon areas are now dim gray instead of black (was 0.02). |
| WorldEnvironment | SSAO ON (radius 1.2, intensity 1.6) and **SSIL ON** (radius 4, intensity 1.1) for soft indoor bounce light; Reflected Light = Sky; ACES; Glow threshold 2.5 |
| Sun (DirectionalLight3D) | Energy **5** (was 6); **Shadow Opacity 0.82** (was 1); Shadow Blur 1.2; Angular Distance **0.53°** (real sun, soft penumbra); Project Settings soft-shadow filter **Soft Low** (was Hard) |
| Lobby | 4 × **SpotLight3D** key lights (white and pale cyan, energy 3.2, 42°, **shadows ON**, blur 2.5) aimed at the console ring; cyan halo wash; existing center and ring omnis retuned to white/cyan; cyan HoloGlow omni |
| Each room | 1 × shadowed SpotLight3D straight down from the ceiling (energy 2.6, 58°) + a soft blue fill OmniLight3D; Shield room uses warm white |
| Bio-Dome | Grow lights retuned to **soft faint green-white** (0.78, 1, 0.8) at energy 0.8, and the dome light to (0.92, 1, 0.94) at 0.6. Sun also comes through the glass. |
| Corridors | Cool-white OmniLight3D every ~5 m (no shadows) + the emissive strips |
| Reflections | One **ReflectionProbe** per room and corridor (Update Once, Interior, Box Projection), so the glossy steel reflects the room instead of black space |
