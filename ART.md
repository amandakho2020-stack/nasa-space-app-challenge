# Lunar Outpost: Procedural Art Kit

Every model is generated in C#: no external 3D software and no imported meshes. The look follows the reference sheets: weathered cream and sea-green panels, rust and brass accents, chunky faceted shapes and ink outlines.

**It's already applied to `Main.tscn`.** To regenerate the models, select the **ArtReplacer** node and click **Replace Graybox With Models**, then press **Ctrl+S**.
**Restore Graybox** puts every placeholder back. The swap is fully reversible, because nothing is deleted.

> Order matters: **EnvironmentBuilder → Build** rebuilds the graybox (base, surface, rover), so run **ArtReplacer → Replace** again after any rebuild.

## Files

| File | Role |
|---|---|
| `Scripts/Art/StyleKit.cs` | Palette, the four shared materials, outline settings: the single source of truth for the style |
| `Scripts/Art/MeshKit.cs` | Procedural modelling kit writing straight into `ArrayMesh` (box, cylinder/cone, sphere, torus, beam, pipe, extrude, arc plate; transform stack) |
| `Scripts/Art/ModelLibrary*.cs` | The models (3 files) |
| `Scripts/Art/ArtReplacer.cs` | `[Tool]` node: finds the placeholders, swaps in the models, preserves transforms and collision, restores |
| `Shaders/style_outline.gdshader` | Screen-space inverted-hull ink outline (next pass on Paint and Metal) |
| `Shaders/style_glow.gdshader` | Vertex-colour emissive material for screens, LEDs and lamps |
| `Materials/Models/*.res` | Generated meshes, baked collision shapes (`collision/`), rock MultiMeshes (`rocks/`), style materials |

## How the style stays consistent

1. **One palette.** Every colour comes from `StyleKit`: cream, teal, rust, orange, gunmetal, brass, hazard yellow, solar blue, leaf/radish, plus the glow colours. To re-theme every model at once, change a colour there and re-run Replace.
2. **One modelling vocabulary.** Everything is built from the same MeshKit primitives at low segment counts: 8–16 sided cylinders, 4–7 ring spheres, square beams. That gives every model the same chunky, faceted silhouette.
3. **Flat shading with a hand-painted feel.** Each face gets its own normal and a ±5 % brightness jitter, which reads as painted panels.
4. **Four shared materials.** Every surface uses one of `Paint`, `Metal`, `Glass` or `Glow`. Colour lives in vertex colours, so no model has a one-off material.
5. **Uniform ink outline.** The outline pass pushes a back-face hull out in *screen* space along smoothed normals that MeshKit bakes into `CUSTOM0`. Every object, from a 0.1 m radish to the 16 m drill rig, gets the same 2.2 px line. Change **Outline Pixels** on the ArtReplacer to adjust it, or turn **Outlines** off.

## Model → placeholder mapping

| Model (`ModelLibrary.*`) | Reference | Replaces (placeholder) | Transform rule |
|---|---|---|---|
| `Astronaut()` | chibi astronaut | `Player/Visual` capsule, helmet, visor, backpack meshes | Identity under `Visual` (faces −Z like before) |
| `RoverBody()` + `RoverWheel()` ×4 | lunar rover | `LunarRover` CSG parts, and `Tire`/`Hub` under each `VehicleWheel3D` | Identity; right wheels turned 180° so hub caps face out. Wheels still spin and steer. |
| `GeodesicDome(r, doorDir)` | geodesic greenhouse (inside and outside) | `Base/PlantDomeGlass` + `PlantDomeRibs` | Dome transform. The door gap is read from the CSG door cut. Includes the pipe ring, grow lights and ECLSS tank. |
| `HydroPlanter(size)` with radishes | hydroponic planter, radish | `Interiors/Plant/Planter0..2` + 27 plant spheres | Planter transform; sized from the CSG box |
| `O2Cluster()` ×2 | O2 tank cluster | `Interiors/Oxygen/O2Tank0..5` + `Manifold` | Centroid of each group of three tanks, on the floor |
| `ControlStation()` | O2 control panel | `Interiors/Oxygen/Electrolyzer` | Placeholder transform + 90° so it faces into the room |
| `SolarTracker(w, d, tilt)` ×8 + broken ×1 | solar tracker, broken panel | `Base/SolarField/Post*` + `Panel*` | Post transform, on the ground; panel size and tilt read from the CSG |
| `CommTower()` | lattice comm tower | `Base/LobbyDishMast` + `LobbyDish` | Mast base (roof) |
| `DrillRig()` | drill rig | `Base/DrillSite/Rig` (whole CSG group) | Rig transform |
| `SampleBasket(size)` | sample crate | `DrillSite/SampleRack` + core samples | Rack transform; sized from the CSG |
| `PressureTank(r, h)` ×2 | pressure tanks | `DrillSite/CoolantTankA/B` | CSG transform (lying on its side, as placed) |
| `HabPod()` | crew pod | `DrillSite/FieldHab` (new placeholder) | Box transform, on the ground |
| `DeskConsole(width)` + `Chair()` ×6 | control console | Each lobby `Desk*`'s `Body`, `Kickplate`, `Keyboard*`, `Chair` | Desk-local. The slanted face matches, so the **live Label3D screens are kept** and sit on the new consoles. |
| `RingScanner()` | ring device | `Interiors/Telescope/Pier`, `Mount`, `OpticalTube`, `Aperture` | Pier base |
| `RocketLander()` | rocket lander | `Base/LaunchPad/Lander` (new placeholder by the dome) | Cylinder base |
| `CrystalRock(seed)` ×3 | crystal asteroid rock | Mesh of every rock-field `MultiMeshInstance3D` | All ~1,100 instance transforms are kept; only the mesh changes |
| *(re-skin)* | base modules | `Base/Shell` hull and interior CSG materials | Cream exterior and sage interior from the palette |

Not replaced: mission consoles (their objective glow is gameplay), the airlock door, room racks and screens, and the walkable base shell. The shell is re-skinned instead of replaced, so the tube doorways and collision stay exact.

## How transforms and collision are preserved

For each placeholder the replacer:
1. Adds a `MeshInstance3D` as a **sibling** using the placeholder's local `Transform`, so position, rotation and scale are unchanged. Models are authored so their origin matches the placeholder's (centre, base or floor point, as listed above).
2. If the placeholder is a root CSG shape with **Use Collision**, it calls `CSGShape3D.BakeCollisionShape()`. The exact faces go into a `StaticBody3D` + `CollisionShape3D` at the same transform, with the same collision layer and mask, saved in `Materials/Models/collision/`. The placeholder's own collision is then turned off. If a bake ever fails, the hidden placeholder keeps its collision instead, so physics never changes.
3. Hides the placeholder and tags it (`art_placeholder` group + metadata) so **Restore Graybox** can bring it back exactly.

Tested: walking every corridor and room ends at the identical positions with and without the art, including against the planters, the telescope instrument and the desk rows. Restore → Replace round-trips to an identical node state.

**Batch / command-line use:** a CSG node builds its geometry one frame after entering the scene tree. From a script that isn't the editor, call `PrimeCollisionBakes()`, wait a frame or two, then call `ReplaceAll()`. Also run it with a renderer (not `--headless`), or the rock MultiMesh data saves empty.

## Adding a new model

1. Write `public static ArrayMesh MyThing()` in `ModelLibrary`:
   - `var k = new MeshKit(seed);`
   - build it with `k.Box/Cylinder/Sphere/Beam/Pipe/Extrude…`, using only `StyleKit` colours and the `P`/`M`/`G`/`Gl` surfaces
   - `return k.Commit();`
2. Add a `ReplaceMyThing()` step in `ArtReplacer`: `Retire(placeholder)` + `AddModel(parent, name, transform, Mesh("my_thing", ModelLibrary.MyThing))`.
3. Click **Replace Graybox With Models**.
