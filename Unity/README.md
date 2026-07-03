# Unity Tools & Scripts — Usage

How to install and use each tool in this folder. Everything targets a scene that has
a `CesiumGeoreference` + `Cesium3DTileset` (photogrammetry backdrop) and a CityGen3D
city (terrain, `MapRoads`, buildings) already in it.

## Installation

- `Runtime/` scripts → copy into `Assets/Scripts/` (any subfolder).
- `Editor/` scripts → must go under `Assets/Editor/`, otherwise Unity will not compile them as editor tools.
- `Shaders/RoadCutoutDualOverlay/` → copy the whole folder into `Assets/`, keeping the `.meta` files so the material keeps its Shader Graph reference.

Packages needed: Cesium for Unity, Unity Splines (used by the road cut), TextMeshPro, Input System.

---

## Editor tools

All three open from the menu **Tools > CityGen3D x Cesium**. Recommended order:
align first, probe if heights look wrong, then cut.

### 1. Rigid Align (`CesiumRigidAlign.cs`)

Vertical alignment between the Cesium tileset and the CityGen3D terrain. It measures
the median height gap (Cesium minus terrain) over the road corridors and compensates
it through `CesiumGeoreference.height`. The terrain heightmap is never modified.

1. Open **Tools > CityGen3D x Cesium > Rigid Align**, press **Detect references**.
2. Press **MEASURE (read-only)** — nothing is changed yet.
3. Press **APPLY: shift Cesium by … m**.
4. Wait for the tiles to reload, then **MEASURE** again. Expect |median| well under 0.5 m.
5. **REVERT to original height** restores the starting value if needed.

Residuals of a metre here and there are expected; the road-corridor cut hides them.

### 2. Cesium Height Probe (`CesiumHeightProbe.cs`)

Diagnostic only — samples a grid of Cesium surface heights with
`SampleHeightMostDetailed` and prints the height distribution. It never modifies the
scene. Use it to check whether ground and buildings separate cleanly by height before
running a cut, or to sanity-check the alignment.

1. Open **Tools > CityGen3D x Cesium > Cesium Height Probe**, press **Detect**.
2. Press **Run probe sample** and read the report (results are polled while tiles load, so give it a moment).

### 3. Road Only Cut v2 (`RoadOnlyCut_v2.cs`)

The production cut: removes the Cesium mesh **only over the drivable carriageways**
(Main Road / Minor Road / Dual Carriageway / Turning Circle from `MapRoads`;
Footpaths are excluded via the `excludeTokens` field), revealing the collidable
CityGen road underneath. Buildings and pavements are never touched.

1. Open **Tools > CityGen3D x Cesium > Road Only Cut v2**, press **Detect** — it finds the tileset, georeference, `MapRoads` and the DualClip material automatically.
2. Press **Build road cut**. This creates a "Road Only Cut v2 Polygons" object holding the `CesiumCartographicPolygon`s and a `CesiumPolygonRasterOverlay` with material key `RoadCutout`.
3. **Clear** removes the overlay and polygons again (the study-area `Clipping` overlay is kept).

Works together with the height gate in the DualClip material (`_CutHeightThreshold`):
with the gate active only the low part of the corridor is cut, so facades and
overhangs leaning over the road survive.

Useful fields: `roadMargin` (extra metres around the carriageway), `excludeTokens`
(name tokens to skip, default `Footpath`), `limitToStudyArea` + area bounds.

---

## Shader (`Shaders/RoadCutoutDualOverlay/`)

`CesiumDefaultTilesetDualClippingShader` is the Cesium default tileset shader extended
with **two** clipping overlays (`Clipping` for the study area, `RoadCutout` for the
road corridor) and a world-height gate `_CutHeightThreshold` (fragments above the
threshold are not cut). Assign `CesiumDefaultTilesetDualClippingMaterial` as the
tileset's *Opaque Material* — Road Only Cut v2 does this automatically when it builds.
`HybridMask.png` is the mask texture used by the overlay.

---

## Runtime scripts

### Vehicle (`Runtime/Vehicle/`)

- **`CarController.cs`** — arcade car physics (Rigidbody based, Input System). Put it on the car root; it exposes `currentSpeed` / `currentAcceleration`, which the eco metric reads.
- **`CarFollowCamera.cs`** — third-person chase camera with NaN/teleport guards. Set `target` to the car and assign the `CesiumGeoreference`; it registers the camera with `CesiumCameraManager` so tiles load around the car view.

### Study loop (`Runtime/EcoHUD/`)

One HITL-BO round = apply candidate HUD parameters → drive the fixed route → survey →
write objectives back → next candidate. Wiring:

| Script | Attach to | Notes |
|---|---|---|
| `EcoScore` | the car (or a manager) | Road-load energy model; outputs live 0–100 score + kWh/100km per round. |
| `EcoFeedbackHUD` | the HUD canvas | Renders the score under 7 continuous design parameters; UI references are null-guarded, wire whichever elements exist. |
| `AutoDriver` | the car | Pure-pursuit autopilot along the baked centreline route; `engaged` off = keyboard drives. |
| `RoadBoundaryWalls` | an empty (e.g. "RoadWalls") | In the **editor**, right-click the component and run **Build Walls** (runtime meshes are static-batched and unreadable, so keep *Build On Start* OFF). **Clear Walls** removes them. |
| `RoundController` | a "StudyManager" empty | The loop itself. References auto-resolve via `FindFirstObjectByType`, or assign them. `autoDrive` = autopilot vs participant; `lapsPerRound`, `startOnPlay`. |
| `MockOptimizerBridge` | the StudyManager | Random-search stand-in so the whole loop runs without the BO backend. Assign on `RoundController.optimizer`. |
| `BoForUnityBridge` | the StudyManager | Adapter for the Bayesian-Optimization-for-Unity asset: declare the 7 parameters / 3 objectives in the asset's manager (energy MIN, task load MIN, acceptance MAX), fill the TODO bodies with the asset's API, then swap it onto `RoundController.optimizer`. |
| `SimpleStudyQuestionnaire` | the survey panel | NASA-TLX + van der Laan sliders, any min/max, normalized to 0–100. With no UI wired and `autoCompleteIfNoUI` on, it returns neutral scores so the loop still cycles. |

`OptimizerBridge` and `StudyQuestionnaire` are the abstract seams — swap
implementations without touching `RoundController`. `EcoDrivingHUD` is an earlier
minimal HUD (score text + bar + colour thresholds) kept for simple demos.

The route array inside `AutoDriver` is generated offline by
[`Tools/route_centerline_snap.py`](../Tools/route_centerline_snap.py) — run it and
paste the printed C# array in.
