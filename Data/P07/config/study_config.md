# Study configuration - P07 (session P17_20260822_123356)

Extracted from the project source at archive time. The per-file hash manifest is `code_versions.txt`; the verbatim script snapshots are withheld from this public release pending re-identification review and are preserved in the private archive.

## Optimised parameters (HUD design dimensions)

The search space is normalised per dimension; the CSVs record the normalised values.

| # | Parameter | Lower | Upper |
|---|---|---|---|
| p0 | size_leaf | 0 | 1 |
| p1 | size_score | 0 | 1 |
| p2 | size_feedback | 0 | 1 |
| p3 | size_speed | 0 | 1 |
| p4 | size_accel | 0 | 1 |
| p5 | size_labels | 0 | 1 |
| p6 | opacity | 0 | 1 |

`p0..p6` in `rounds.csv` follow this order, which is also the order applied by `ApplyDesignParams`.

## Normalised value -> physical value

Mapping constants in `EcoFeedbackHUD.ApplyDesignParams`:

- `SizeMax` = 1.3
- `SpeedMin` = 0.6
- `OpacityMin` = 0.10

Each size dimension: physical = normalised x SizeMax; `size_speed` = SpeedMin + normalised x (SizeMax - SpeedMin) (the speed reading has a legibility floor); `opacity` starts from OpacityMin.

## Objectives

| Objective | Lower | Upper | Direction |
|---|---|---|---|
| energy | 0 | 150 | minimise |
| taskload | 0 | 100 | minimise |
| accInformed | 0 | 100 | maximise |
| accPleasant | 0 | 100 | maximise |
| accGlance | 0 | 100 | maximise |

`energy` is in kWh/100 km; the rest are 0-100 scales. `taskload` combines the two TLX-style items (mental / distraction); the three acceptance objectives are independent. The raw items are the last five columns of `unity/rounds.csv`.

## Routes

Geometry in `final_routes.json` (scene-plane point sequences with per-point road half-width). Routes rotate as `iteration % routeCount`, independent of the participant, so every participant saw the same route sequence.

| Route | Length (m) | Streets |
|---|---|---|
| R1 | 215.3 | Draycott Place |
| R2 | 164.9 | Cliveden Place |
| R3 | 282.6 | Basil Street -> Hans Road -> Brompton Road |
| R7 | 169.8 | Astell Street -> Burnsall Street -> King's Road |

## Round structure

The authoritative per-round phases are the `Phase` column of `bo/ObservationsPerEvaluation.csv`: 15 sampling rounds, 5 optimisation rounds, and 1 final-design round.

## Reading the data

`durationS` in `rounds.csv` is NOT the driving duration: it spans from entering the driving phase to questionnaire submission, so it includes questionnaire time. Use `driveDurationS` in the release's `Data/master_rounds.csv` for the trajectory-verified driving duration. The aggregates `avgSpeedKmh` / `maxSpeedKmh` / `avgEcoScore` accumulate during the driving phase only and are unaffected.
