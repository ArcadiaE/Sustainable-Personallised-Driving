# Sustainable Personalized Driving

Supporting code for the COMP0190 P87 project:

**Sustainable Personalized Driving: Three-Layer HITL Bayesian Optimization for Eco-Driving Behaviour, Interface Design & Route Recommendation**

The driving environment is built in Unity from Cesium 3D Tiles (photogrammetry backdrop) combined with CityGen3D roads, with a hybrid road pipeline: corridor cutting on the Cesium mesh, OSM building-footprint protection, and a self-built drivable road surface.

## Repository Layout

- `Unity/Runtime/EcoHUD/`
  Runtime scripts for the eco-driving study: eco-feedback HUD and scoring (`EcoFeedbackHUD`, `EcoScore`, `EcoDrivingHUD`), automated route driving (`AutoDriver`), study round management and in-scene questionnaires (`RoundController`, `SimpleStudyQuestionnaire`, `StudyQuestionnaire`), road boundary walls, and bridges to Bayesian-Optimization-for-Unity (`BoForUnityBridge`, `OptimizerBridge`, `MockOptimizerBridge`).

- `Unity/Runtime/Vehicle/`
  Vehicle control (`CarController`) and chase camera (`CarFollowCamera`).

- `Unity/Editor/`
  Unity Editor pipeline tools for merging Cesium and CityGen3D: rigid alignment (`CesiumRigidAlign`), terrain height probing (`CesiumHeightProbe`), and height-limited road corridor cutting (`RoadOnlyCut_v2`, the tool used to cut the drivable carriageways out of the Cesium mesh).

- `Unity/Shaders/RoadCutoutDualOverlay/`
  Shader Graph, material, and mask texture for dual clipping of the Cesium tileset along the road corridor. `.meta` files are included so the material keeps its shader reference when imported into a Unity project.

- `UnityTools/CesiumCityGenAligner/`
  Unity Editor tool for aligning CityGen3D Landscape tiles with Cesium 3D Tiles.

- `Tools/`
  Python utilities: `route_centerline_snap.py` snaps a planned route to the OSM road centerline for `AutoDriver`.

## Notes

- CityGen3D is a paid Unity Asset Store plugin and is not included in this repository.
- Cesium for Unity, the Unity project itself, and third-party assets (vehicle model, Bayesian-Optimization-for-Unity) are not included; only self-written code is published here.
