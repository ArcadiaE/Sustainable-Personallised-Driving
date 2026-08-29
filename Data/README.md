# Data

Pseudonymous study data for the 16 participants (P01-P16), released under the scope stated in the thesis Open Science section.

## Included

| Path | Content |
|---|---|
| `master_rounds.csv` | One row per participant per round (336 rows): phase, route, per-round driving summaries, the five objective values, the seven design parameters (normalised and physical), Pareto membership. Entry table for the analysis scripts. |
| `master_rounds_motion.csv` / `master_rounds_static.csv` | The same table split by motion-platform condition (P01-P08 motion, P09-P16 static). |
| `build_master.py` | Builds the master table from the per-participant folders. |
| `P??/hud_design_per_round.csv` | Design parameters applied in each round (normalised, physical, and per-element visibility). |
| `P??/unity/rounds.csv` | Per-round driving and questionnaire summary written by the simulator. |
| `P??/bo/ObservationsPerEvaluation.csv` | Parameters and objective values per optimiser evaluation, with phase labels. |
| `P??/bo/HypervolumePerEvaluation.csv` | Cumulative hypervolume per evaluation from the optimiser log. |
| `P??/bo/ExecutionTimes.csv` | Optimiser timing per evaluation. |
| `P??/config/` | Study configuration, route geometry (`final_routes.json`), and the per-file hash manifest of the simulator scripts at session time (`code_versions.txt`). |

## Withheld

Raw post-session questionnaire exports (demographic combinations and free text), continuous high-frequency driving trajectories, and detailed console logs are withheld pending a re-identification-risk review, as stated in the thesis. Verbatim per-session script snapshots are preserved in the private archive; their published hashes allow verification.

## Notes

- `durationS` in `rounds.csv` includes questionnaire time; use `driveDurationS` in `master_rounds.csv` for driving duration.
- Timestamps are wall-clock session times; participant identifiers are pseudonyms assigned in session order.
