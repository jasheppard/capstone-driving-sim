# NCATMAPP

A driving-safety simulator set on a photorealistic North Carolina A&T campus.
You drive a shuttle bus along a route, obeying stop signs, crosswalks and speed
limits, and the game scores how safely you did it. Every run also writes raw
telemetry for the CST499 data-analysis work.

CST499 Senior Project II.

---

## What you need

* **Unity 2022.3.62f3.** Not a different patch version. A different one will
  reserialize the scene and produce noisy diffs.
* Nothing else. Cesium for Unity installs itself from `Packages/manifest.json`
  the first time you open the project. Give it a few minutes.

The campus streams from Google Photorealistic 3D Tiles at runtime, so it is not
in this repo and there is no account to set up. It just appears.

> The Google Maps API key that loads it is in the scene file. **Keep this repo
> private.** If it ever goes public, regenerate the key in Google Cloud Console
> and paste the new one into the Cesium3DTileset's URL field.

## Running it

Open `Assets/Scenes/MainMenu` and press Play. Or open `SampleScene` to go
straight to driving.

If the campus is missing when you press Play, the tiles have not streamed yet.
The loading screen holds you until they have.

---

## Controls

| | Keyboard | Controller |
|---|---|---|
| Throttle / brake | `W` `S` | RT / LT |
| Steer | `A` `D` | Left stick |
| Hard brake | `Space` | Cross |
| Camera view | `C` | D-pad Up |
| Look around | Right-drag | Right stick |
| Respawn if stuck | `R` | Circle |
| End run, see report | `Esc` | Options |
| Reopen the briefing | `H` | |

---

## The NCAT menu

Everything is under **NCAT** in Unity's menu bar. These are one-time setup steps
that add objects to the scene, so **save the scene afterward** or the work is
lost on the next reload.

### Building a route

Routes are laid out by driving them. On photogrammetry terrain you cannot tell
from the editor view what the bus can physically reach, so the only reliable way
to place a waypoint is to have driven to it.

1. `Route > 1. Arm Route Recording`, then save
2. Press Play. `N` picks the waypoint type, `B` drops one, `Backspace` undoes.
   End with a **Finish** waypoint
3. Press **Stop**. This is what writes the route file
4. `Route > 2. Import Recorded Route`
5. `Route > 3. Set Up Route HUD`
6. `Route > Disarm Route Recording`, then save

Waypoint order is the order of the children under the `Route` object, so
reordering the route means dragging in the Hierarchy.

### Everything else

| Menu item | What it adds |
|---|---|
| `Set Up Player Car` | Rigidbody, collider, physics material |
| `Attach Vehicle Model` | Puts the bus mesh on the chassis |
| `Set Up Speedometer` | Dial, digital mph, live stopping distance |
| `Set Up Minimap` | Top-down view, bottom left |
| `Set Up Drive Debrief` | The post-run safety report |
| `Set Up Driver Briefing` | Controls and scoring, shown before you drive |
| `Set Up Loading Screen` | Waits for the campus to stream in |
| `Set Up Campus Boundary` | Limits the world to campus |
| `Set Up Pedestrians` | Spawns walking NPCs |
| `Set Up Telemetry Logging` | Records the CSV |
| `Enable Cesium Physics Meshes` | Without this the bus falls through the world |
| `Configure Build Scenes` | Adds both scenes to the build |

---

## How the scoring works

Driving events are counted **per minute** and route rules **per opportunity**, so
neither a longer drive nor a longer route loses you points for its length alone.

| Rule | Worth |
|---|---|
| Full stop at every stop sign | 30 |
| Yield at every crossing | 25 |
| Time under the speed limit | 25 |
| Collisions | up to 30 |
| Pedestrian near misses | up to 20 |
| Harsh braking, over 0.30 g | up to 10 |
| Harsh cornering, over 0.25 g | up to 10 |

Modelled driver profiles score: clean run 98, careful driver who rolls one stop
sign 89, ordinary driver 71, hurried 49, reckless 0.

The event thresholds follow insurance-telematics convention. **The penalty
weights are a rubric we defined, not values derived from crash data**, and should
be described that way in the report.

Tune anything on the `DriveScorer` component on `PlayerCar`.

---

## The physics

The bus is modelled rather than tuned by feel, so the numbers are defensible:

* Acceleration is limited by traction at low speed and by engine power above it,
  which is why it tails off. 0 to 30 mph takes about 7 seconds, 0 to 60 about 36
* Top speed is governed at 65 mph. Left alone, drag would cap it at 78
* Braking is 5.0 m/s^2, about 0.51 g, matching APTA's in-service standard for
  transit buses. From 25 mph that is roughly 41 feet
* Cornering is capped at 0.35 g, near where a high-floor bus lifts a wheel

Change `massKg`, `enginePowerKW` and `dragArea` on `VehicleController` to model a
different vehicle.

---

## Telemetry and analysis

Each run writes `TelemetryData/drive_session_<timestamp>.csv`: 14 columns at
10 Hz covering speed, throttle, brake, steering, yaw rate, lateral acceleration,
pedestrian proximity and collisions. Ending a run also writes a readable
`drive_report_<timestamp>.txt`.

To analyse:

```
pip install pandas numpy matplotlib seaborn
python driving_analysis.py
```

It picks the newest session, prints a correlation matrix, finds the two most
strongly related variables and saves four figures to `figures/`. Use `--all` to
combine every session, or `--csv <path>` for a specific one.

Do not mix sessions recorded under different physics settings. They describe
different vehicles.

---

## Project layout

```
Assets/
  Scripts/     runtime behaviour: vehicle, camera, route, scoring, HUD
  Editor/      the NCAT menu. Setup only, never runs in a build
  Scenes/      MainMenu and SampleScene
  Prefabs/     pedestrians
  Models/      the shuttle bus
TelemetryData/ recorded runs (committed on purpose, the coursework needs them)
Routes/        recorded route files
driving_analysis.py
```

`Library/` is generated by Unity and is gitignored. Never commit it.

---

## If something looks wrong

**Main menu does not appear on Play.** Unity plays whichever scene is open, not
build index 0. Open MainMenu, or run `NCAT > Always Start Play at Main Menu`.

**Waypoints floating in mid-air.** The tiles under them had not loaded. Fly the
Scene view over the route, then `Route > Snap Route To Ground`.

**The bus cannot reach a waypoint.** Kerbs, bollards and scanned trees are solid
geometry. Move the waypoint somewhere you have actually driven, or widen its
radius.

**A score that seems unfairly low.** Check harsh events per minute in the report.
A real driver produces one to three. Ten or more means the collider is bouncing
on scanned terrain, not that you drove badly.

**Everything you set up vanished.** The scene was not saved. Every setup menu
item changes the scene and none of them save it for you.
