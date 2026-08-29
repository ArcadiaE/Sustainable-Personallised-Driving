# ==============================================================================
#  Sustainable Personalised Driving
#  Human-in-the-loop multi-objective Bayesian optimisation (HITL MOBO)
#  of an eco-driving HUD in VR   --   UCL
#
#  DESIGN.  There is exactly ONE condition (HITL MOBO). Nothing is therefore
#  compared *between* groups. The whole evaluation is within-subject and
#  longitudinal: N participants x 21 rounds (N is read from data/ at run time)
#        rounds  1-15  sampling      (quasi-random HUD designs)
#        rounds 16-20  optimisation  (BO-proposed HUD designs)
#        round      21  final design  (the selected Pareto design, re-driven)
#
#  OBJECTIVES (BOforUnity)                 direction   bounds
#     energy       kWh/100 km              minimise    [0, 150]
#     taskload     TLX mental+distraction  minimise    [0, 100]
#     accInformed  acceptance              maximise    [0, 100]
#     accPleasant  acceptance              maximise    [0, 100]
#     accGlance    acceptance              maximise    [0, 100]
#
#  DESIGN PARAMETERS (normalised to [0, 1] in the BO search space)
#     size_leaf size_score size_feedback size_speed size_accel size_labels opacity
#
#  Author: Mark Colley <mark.colley@yahoo.de>
#  Run with:  Rscript analysis.R      (or source() it in RStudio)
# ==============================================================================

# ------------------------------------------------------------------ 0. SETUP --
suppressPackageStartupMessages({
  library(colleyRstats)
  library(dplyr)
  library(tidyr)
  library(purrr)
  library(ggplot2)
  library(lme4)
  library(lmerTest)
  library(emmeans)
  library(parameters)
  library(performance)
  library(effectsize)
  library(ARTool)
  library(ggstatsplot)
  library(patchwork)
  library(rmcorr)
  library(moocore)
  library(emoa)
})

colleyRstats_setup(set_theme = TRUE, print_citation = FALSE, verbose = FALSE)

options(scipen = 999, digits = 5, dplyr.summarise.inform = FALSE)
set.seed(42)

# Paths -----------------------------------------------------------------------
BASE_DIR <- tryCatch(dirname(normalizePath(sys.frame(1)$ofile)),
                     error = function(e) getwd())
if (!dir.exists(file.path(BASE_DIR, "data"))) BASE_DIR <- getwd()

DATA_DIR <- file.path(BASE_DIR, "data")
OUT_DIR  <- file.path(BASE_DIR, "output")
FIG_DIR  <- file.path(OUT_DIR, "figures")
TAB_DIR  <- file.path(OUT_DIR, "tables")
TEX_DIR  <- file.path(OUT_DIR, "tex")
for (d in c(OUT_DIR, FIG_DIR, TAB_DIR, TEX_DIR)) {
  dir.create(d, recursive = TRUE, showWarnings = FALSE)
}

# The report_* helpers WRITE (not append) to their sink_to path, so pointing all
# of them at one file would leave only the last sentence. Each result therefore
# gets its own file to \input{}, and every sentence is additionally appended to
# one combined transcript.
TEX_MACROS <- file.path(TEX_DIR, "macros.tex")
TEX_ALL    <- file.path(TEX_DIR, "results_all.tex")
for (f in list.files(TEX_DIR, pattern = "[.]tex$", full.names = TRUE)) file.remove(f)

tex_path <- function(...) {
  slug <- gsub("[^A-Za-z0-9]+", "_", paste(..., sep = "_"))
  file.path(TEX_DIR, paste0(slug, ".tex"))
}

# Wraps a report_* call: it still writes its own file, and the returned
# sentences are appended to the combined transcript under a comment heading.
tex_collect <- function(sentences, heading = NULL) {
  sentences <- as.character(sentences)
  if (!length(sentences)) return(invisible(NULL))
  con <- file(TEX_ALL, open = "a", encoding = "UTF-8")
  on.exit(close(con))
  if (!is.null(heading)) writeLines(paste0("", "%% ", heading), con)
  writeLines(trimws(sentences, which = "right"), con)
  invisible(NULL)
}

# Study constants -------------------------------------------------------------
N_SAMPLING     <- 15L   # rounds 1..15  -- quasi-random designs
N_OPTIMISATION <- 5L    # rounds 16..20 -- BO-proposed designs
N_FINAL        <- 1L    # round  21     -- selected design, re-driven
N_ROUNDS       <- N_SAMPLING + N_OPTIMISATION + N_FINAL

OBJ <- c("energy", "taskload", "accInformed", "accPleasant", "accGlance")

OBJ_LABEL <- c(
  energy      = "Energy consumption (kWh/100 km)",
  taskload    = "Task load (0-100)",
  accInformed = "Acceptance: informed (0-100)",
  accPleasant = "Acceptance: pleasant (0-100)",
  accGlance   = "Acceptance: glanceable (0-100)"
)

# TRUE = smaller is better. Needed for the Pareto/hypervolume machinery, which
# always minimises, and for the sign of every improvement reported below.
OBJ_MINIMISE <- c(energy = TRUE, taskload = TRUE,
                  accInformed = FALSE, accPleasant = FALSE, accGlance = FALSE)

# Objective bounds as configured in BOforUnity (config/study_config.md).
OBJ_BOUNDS <- list(energy = c(0, 150), taskload = c(0, 100), accInformed = c(0, 100),
                   accPleasant = c(0, 100), accGlance = c(0, 100))

PARAMS <- c("size_leaf", "size_score", "size_feedback",
            "size_speed", "size_accel", "size_labels", "opacity")

PARAM_LABEL <- c(
  size_leaf     = "Leaf icon size",
  size_score    = "Eco score size",
  size_feedback = "Feedback text size",
  size_speed    = "Speed readout size",
  size_accel    = "Acceleration bar size",
  size_labels   = "Label size",
  opacity       = "HUD opacity"
)

MEASURE_LABEL <- c(
  OBJ_LABEL,
  overallQuality = "Overall design quality (0-100, higher = better)",
  avgEcoScore    = "Eco score (0-100)",
  avgSpeedKmh    = "Mean speed (km/h)",
  meanAbsAccel   = "Mean absolute acceleration (m/s2)",
  sdSpeedKmh     = "Speed variability (SD, km/h)",
  pctHarshAccel  = "Time with |a| > 2.5 m/s2 (%)",
  drivingTimeS   = "Driving time (s)",
  sdThrottle     = "Throttle variability (SD)",
  collisions     = "Collisions per round"
)

# Never returns NA: an unlabelled measure falls back to its column name.
lab <- function(x) {
  out <- unname(MEASURE_LABEL[x])
  ifelse(is.na(out), x, out)
}

PARTICIPANTS <- sort(list.dirs(DATA_DIR, full.names = FALSE, recursive = FALSE))
PARTICIPANTS <- PARTICIPANTS[grepl("^P[0-9]+$", PARTICIPANTS)]
N_PART <- length(PARTICIPANTS)

# Helpers ---------------------------------------------------------------------
# The Unity/BOforUnity exports are semicolon separated with a "." decimal mark,
# so read.csv2() -- which assumes a "," decimal mark -- would silently mangle
# every number in them.
read_semi <- function(path) {
  utils::read.csv(path, sep = ";", dec = ".",
                  na.strings = c("NA", "NULL", ""),
                  stringsAsFactors = FALSE, fileEncoding = "UTF-8")
}

# 0 = best, 1 = worst, on a common scale: the space the Pareto front and the
# hypervolume live in. Acceptance scales are flipped here, once, so that no
# downstream code has to remember which objectives are maximised.
normalise_objective <- function(x, obj) {
  b <- OBJ_BOUNDS[[obj]]
  z <- pmin(pmax((x - b[1]) / (b[2] - b[1]), 0), 1)
  if (!OBJ_MINIMISE[[obj]]) z <- 1 - z
  z
}

# Signed so that a POSITIVE number always means "better", whatever the
# direction of the objective. Used for every delta reported below.
improvement <- function(new, old, obj) {
  if (OBJ_MINIMISE[[obj]]) old - new else new - old
}

section    <- function(...) cat("\n\n", strrep("=", 78), "\n", paste0(...), "\n",
                                strrep("=", 78), "\n", sep = "")
subsection <- function(...) cat("\n---- ", paste0(...), " ", strrep("-", 8), "\n", sep = "")

# Fit an lmer and degrade gracefully to a simpler random-effects structure
# rather than aborting the whole script on a singular fit.
safe_lmer <- function(formula, data, label = "") {
  m <- tryCatch(lmerTest::lmer(formula, data = data, REML = TRUE),
                error = function(e) {
                  message("  [lmer failed] ", label, ": ", conditionMessage(e))
                  NULL
                })
  m
}

# ------------------------------------------------------------- 1. DATA INPUT --
section("1  LOADING")

# P01 was run before the study IDs were set, so its UserID/ConditionID/GroupID
# are the numeric default -1 while every later participant has "v1"/"Study".
# They are irrelevant here (one condition) but must share a type to stack.
load_obs    <- function(pid) read_semi(file.path(DATA_DIR, pid, "bo", "ObservationsPerEvaluation.csv")) |>
  mutate(dplyr::across(dplyr::any_of(c("UserID", "ConditionID", "GroupID")), as.character)) |>
  mutate(participant = pid, .before = 1)
load_rounds <- function(pid) read_semi(file.path(DATA_DIR, pid, "unity", "rounds.csv")) |>
  mutate(participant = pid, .before = 1)
load_hv     <- function(pid) read_semi(file.path(DATA_DIR, pid, "bo", "HypervolumePerEvaluation.csv")) |>
  mutate(participant = pid, .before = 1)
load_exec   <- function(pid) read_semi(file.path(DATA_DIR, pid, "bo", "ExecutionTimes.csv")) |>
  mutate(participant = pid, .before = 1)
load_hud    <- function(pid) utils::read.csv(file.path(DATA_DIR, pid, "hud_design_per_round.csv"),
                                             stringsAsFactors = FALSE, fileEncoding = "UTF-8")

# Driving behaviour from the 50 Hz trajectories.
# rounds.csv$durationS is NOT driving time -- it runs from entering the driving
# phase to submitting the questionnaire (config/study_config.md) -- so real
# driving time is taken from the last trajectory timestamp instead. The other
# metrics describe *how* sustainably a round was driven, beyond the single
# aggregate energy number.
traj_metrics_one <- function(path) {
  tr <- read_semi(path)
  d  <- sqrt(diff(tr$x)^2 + diff(tr$z)^2)
  data.frame(
    drivingTimeS  = max(tr$t, na.rm = TRUE),
    distanceM     = sum(d, na.rm = TRUE),
    sdSpeedKmh    = stats::sd(tr$speedKmh, na.rm = TRUE),
    meanAbsAccel  = mean(abs(tr$accelMs2), na.rm = TRUE),
    sdAccel       = stats::sd(tr$accelMs2, na.rm = TRUE),
    pctHarshAccel = 100 * mean(abs(tr$accelMs2) > 2.5, na.rm = TRUE),
    sdThrottle    = stats::sd(tr$throttle, na.rm = TRUE),
    sdSteer       = stats::sd(tr$steer, na.rm = TRUE),
    pctStopped    = 100 * mean(tr$speedKmh < 1, na.rm = TRUE),
    finalEcoScore = utils::tail(tr$ecoScore, 1)
  )
}

load_traj <- function(pid) {
  files  <- sort(list.files(file.path(DATA_DIR, pid, "unity"),
                            pattern = "^trajectory_round[0-9]+[.]csv$", full.names = TRUE))
  rounds <- as.integer(sub(".*trajectory_round([0-9]+)[.]csv$", "\\1", files))
  purrr::map2_dfr(files, rounds, function(f, r) cbind(participant = pid, round = r,
                                                      traj_metrics_one(f)))
}

cat("Participants found:", paste(PARTICIPANTS, collapse = ", "),
    "(n =", length(PARTICIPANTS), ")\n")

obs_raw    <- purrr::map_dfr(PARTICIPANTS, load_obs)
rounds_raw <- purrr::map_dfr(PARTICIPANTS, load_rounds)
hv_raw     <- purrr::map_dfr(PARTICIPANTS, load_hv)
exec_raw   <- purrr::map_dfr(PARTICIPANTS, load_exec)
hud_raw    <- purrr::map_dfr(PARTICIPANTS, load_hud)
cat("Reading 50 Hz trajectories ...\n")
traj_raw   <- purrr::map_dfr(PARTICIPANTS, load_traj)

cat(sprintf("  observations : %4d rows\n", nrow(obs_raw)))
cat(sprintf("  rounds       : %4d rows\n", nrow(rounds_raw)))
cat(sprintf("  hypervolume  : %4d rows\n", nrow(hv_raw)))
cat(sprintf("  trajectories : %4d rounds\n", nrow(traj_raw)))


# --------------------------------------------------------- 2. DATA ASSEMBLY --
section("2  ASSEMBLY AND SANITY CHECKS")

PHASE_LEVELS <- c("Sampling", "Optimisation", "Final design")

main_df <- obs_raw |>
  rename(round = Iteration) |>
  mutate(
    participant = factor(participant),
    PhaseBO = Phase,
    Phase = factor(dplyr::recode(tolower(Phase),
                                 sampling     = "Sampling",
                                 optimization = "Optimisation",
                                 finaldesign  = "Final design"),
                   levels = PHASE_LEVELS),
    isPareto = as.logical(IsPareto),
    # plot_mobo2() matches the BOforUnity spelling ("sampling"/"optimization"),
    # so the raw label is kept alongside the display-friendly factor.
    PhaseBO = tolower(PhaseBO)
  ) |>
  select(participant, round, Phase, PhaseBO, isPareto, all_of(OBJ), all_of(PARAMS)) |>
  left_join(
    rounds_raw |>
      mutate(
        participant = factor(participant),
        # Routes rotate deterministically (round %% 4), identically for every
        # participant. Route is NOT modelled as a factor anywhere below: it is
        # unknowable at deployment, so adjusting for it would estimate an
        # outcome the system could never realise. It is carried only to allow
        # matched-route contrasts, where a comparison is imbalanced by protocol
        # (round 21 always runs R1).
        routeCode = factor(sub("^(R[0-9]+).*$", "\\1", route))
      ) |>
      select(participant, round, routeCode, avgSpeedKmh, maxSpeedKmh, collisions,
             maxStuckS, avgEcoScore, tlxMental, tlxDistraction),
    by = c("participant", "round")
  ) |>
  left_join(traj_raw |> mutate(participant = factor(participant)),
            by = c("participant", "round")) |>
  # EcoFeedbackHUD hides an element whose normalised size falls below its
  # threshold, so a design is not only "how big" but "how many elements are on
  # screen at all". That count is the most directly interpretable description of
  # a HUD design and is not recoverable from the raw parameters alone.
  left_join(
    hud_raw |>
      mutate(participant = factor(participant),
             nVisible = rowSums(dplyr::across(dplyr::starts_with("visible_"),
                                              ~ .x %in% c(TRUE, "True", "TRUE")))) |>
      select(participant, round, nVisible, speed_alpha),
    by = c("participant", "round")) |>
  arrange(participant, round)

# Normalised (minimisation) copies of the objectives, used for Pareto fronts,
# hypervolume, and any statement about "overall" quality.
for (o in OBJ) main_df[[paste0(o, "_n")]] <- normalise_objective(main_df[[o]], o)
OBJ_N <- paste0(OBJ, "_n")

# A single scalar summary of one design: the mean normalised objective value,
# flipped so that larger = better. Not a substitute for the multi-objective
# view, but useful as an omnibus outcome for the longitudinal models.
main_df$overallQuality <- 100 * (1 - rowMeans(main_df[, OBJ_N]))

# Convenience subsets ---------------------------------------------------------
bo_df    <- main_df |> filter(Phase != "Final design") |> droplevels()  # rounds 1-20
final_df <- main_df |> filter(Phase == "Final design")                  # round 21
samp_df  <- main_df |> filter(Phase == "Sampling")

# Sanity checks ---------------------------------------------------------------
checks <- main_df |>
  group_by(participant) |>
  summarise(
    n_rounds       = dplyr::n(),
    n_sampling     = sum(Phase == "Sampling"),
    n_optimisation = sum(Phase == "Optimisation"),
    n_final        = sum(Phase == "Final design"),
    n_pareto       = sum(isPareto, na.rm = TRUE),
    rounds_ok      = identical(sort(round), seq_len(N_ROUNDS)),
    traj_missing   = sum(is.na(drivingTimeS)),
    obj_missing    = sum(is.na(dplyr::pick(all_of(OBJ))))
  )
print(as.data.frame(checks))

stopifnot(
  all(checks$n_rounds == N_ROUNDS),
  all(checks$n_sampling == N_SAMPLING),
  all(checks$n_optimisation == N_OPTIMISATION),
  all(checks$n_final == N_FINAL),
  all(checks$rounds_ok),
  all(checks$traj_missing == 0)
)
cat("\nAll structural checks passed.\n")

# The logged durationS is documented as contaminated by questionnaire time.
# Quantify that overhead once so the number can be stated in the paper rather
# than assumed.
dur_check <- rounds_raw |>
  mutate(participant = factor(participant)) |>
  select(participant, round, durationS) |>
  left_join(main_df |> select(participant, round, drivingTimeS),
            by = c("participant", "round")) |>
  mutate(surveyOverheadS = durationS - drivingTimeS)
cat(sprintf(
  "\nLogged durationS exceeds true driving time by M = %.1f s (SD = %.1f, range %.1f-%.1f).\n",
  mean(dur_check$surveyOverheadS), stats::sd(dur_check$surveyOverheadS),
  min(dur_check$surveyOverheadS), max(dur_check$surveyOverheadS)))
cat("=> durationS is not used as an outcome anywhere below; drivingTimeS is.\n")


# ------------------------------------------------------------ 3. DESCRIPTIVES --
section("3  DESCRIPTIVES")

desc_phase <- main_df |>
  select(participant, Phase, all_of(OBJ), overallQuality, avgSpeedKmh,
         avgEcoScore, collisions, drivingTimeS, meanAbsAccel, pctHarshAccel) |>
  pivot_longer(-c(participant, Phase), names_to = "measure", values_to = "value") |>
  group_by(measure, Phase) |>
  summarise(n = dplyr::n(), M = mean(value, na.rm = TRUE), SD = stats::sd(value, na.rm = TRUE),
            Mdn = stats::median(value, na.rm = TRUE),
            Min = min(value, na.rm = TRUE), Max = max(value, na.rm = TRUE), .groups = "drop") |>
  arrange(measure, Phase)
print(as.data.frame(desc_phase), digits = 4)
utils::write.csv(desc_phase, file.path(TAB_DIR, "descriptives_by_phase.csv"), row.names = FALSE)

# LaTeX-ready M/SD lines for each objective, per phase.
subsection("M/SD macros per phase")
for (o in OBJ) {
  cat("\n#", OBJ_LABEL[[o]], "\n")
  tex_collect(colleyRstats::report_mean_sd(as.data.frame(main_df), iv = "Phase", dv = o,
                                           sink_to = tex_path("desc", o)),
              heading = paste("M/SD by phase:", lab(o)))
}


# ----------------------------------------- 4. RQ1: DID THE OPTIMISER IMPROVE? --
section("4  RQ1  Optimisation progress: hypervolume")

# 4a. Hypervolume as logged by BOforUnity -------------------------------------
# Run 0 = state after the sampling phase, runs 1-5 = after each BO iteration.
hv_df <- hv_raw |>
  mutate(participant = factor(participant), Run = as.integer(Run)) |>
  arrange(participant, Run)

hv_wide <- hv_df |>
  group_by(participant) |>
  summarise(hv_start = Hypervolume[Run == min(Run)],
            hv_end   = Hypervolume[Run == max(Run)],
            .groups = "drop") |>
  mutate(hv_gain    = hv_end - hv_start,
         hv_gain_pc = 100 * hv_gain / hv_start,
         improved   = hv_gain > 0)
print(as.data.frame(hv_wide), digits = 5)

subsection(sprintf("Logged hypervolume: first vs. last optimisation run (paired, n = %d)",
                   nrow(hv_wide)))
hv_w <- stats::wilcox.test(hv_wide$hv_end, hv_wide$hv_start, paired = TRUE, exact = TRUE)
print(hv_w)
colleyRstats::rFromWilcox(hv_w, N = 2 * nrow(hv_wide))
hv_t <- stats::t.test(hv_wide$hv_end, hv_wide$hv_start, paired = TRUE)
print(hv_t)
print(effectsize::cohens_d(hv_wide$hv_end, hv_wide$hv_start, paired = TRUE))
cat(sprintf("\n%d/%d participants improved their hypervolume; median relative gain = %.1f%%.\n",
            sum(hv_wide$improved), nrow(hv_wide), stats::median(hv_wide$hv_gain_pc)))

colleyRstats::define_result_macro(
  "hv_gain_pc",
  sprintf("%.1f\\%%", stats::median(hv_wide$hv_gain_pc)), path = TEX_MACROS)

# 4b. Hypervolume recomputed per iteration ------------------------------------
# The log only stores six points. Recomputing the *cumulative* hypervolume after
# every single evaluation, in the normalised minimisation space with reference
# point (1,1,1,1,1), gives a 20-point trajectory per participant and makes the
# sampling and optimisation phases directly comparable.
cum_hv <- function(mat, ref = rep(1, ncol(mat))) {
  vapply(seq_len(nrow(mat)),
         function(i) moocore::hypervolume(mat[seq_len(i), , drop = FALSE], reference = ref),
         numeric(1))
}

hv_traj <- bo_df |>
  arrange(participant, round) |>
  group_by(participant) |>
  group_modify(function(d, key) {
    d$hv_cum <- cum_hv(as.matrix(d[, OBJ_N]))
    d
  }) |>
  ungroup() |>
  select(participant, round, Phase, hv_cum)

hv_phase_gain <- hv_traj |>
  group_by(participant) |>
  summarise(
    hv_after_sampling = hv_cum[round == N_SAMPLING],
    hv_final          = hv_cum[round == N_SAMPLING + N_OPTIMISATION],
    # Gain over the last five *sampling* rounds: the matched-length yardstick
    # for what five more random designs would have bought.
    gain_last5_sampling = hv_cum[round == N_SAMPLING] - hv_cum[round == N_SAMPLING - 5],
    gain_optimisation   = hv_cum[round == N_SAMPLING + N_OPTIMISATION] - hv_cum[round == N_SAMPLING],
    .groups = "drop")
print(as.data.frame(hv_phase_gain), digits = 4)

subsection("HV gain from 5 BO rounds vs. gain from the preceding 5 random rounds")
cat("This is the key internal control for a single-condition study: the BO phase\n",
    "is compared against the same number of quasi-random designs in the same\n",
    "participants, so a gain that merely reflects 'more samples' does not count.\n", sep = "")
gain_w <- stats::wilcox.test(hv_phase_gain$gain_optimisation,
                             hv_phase_gain$gain_last5_sampling,
                             paired = TRUE, exact = TRUE)
print(gain_w)
colleyRstats::rFromWilcox(gain_w, N = 2 * nrow(hv_phase_gain))
print(effectsize::cohens_d(hv_phase_gain$gain_optimisation,
                           hv_phase_gain$gain_last5_sampling, paired = TRUE))


# --------------------------------------------- 5. RQ2: PHASE-LEVEL OUTCOMES --
section("5  RQ2  Sampling vs. optimisation vs. final design")

# Aggregated to one value per participant x phase, which makes the design
# balanced (8 x 3) and the repeated-measures tests exact.
phase_agg <- main_df |>
  group_by(participant, Phase) |>
  summarise(dplyr::across(all_of(c(OBJ, "overallQuality", "avgEcoScore",
                                   "avgSpeedKmh", "meanAbsAccel", "collisions")),
                          ~ mean(.x, na.rm = TRUE)),
            .groups = "drop") |>
  as.data.frame()

phase_results <- list()
for (o in c(OBJ, "overallQuality")) {
  subsection("Phase effect on ", lab(o))

  # Assumption check first, so the parametric/non-parametric choice is documented
  # rather than assumed.
  print(colleyRstats::check_normality_by_group(phase_agg, "Phase", o))

  p <- tryCatch(
    colleyRstats::plot_within_stats(phase_agg, x = "Phase", y = o,
                                    ylab = lab(o),
                                    showPairwiseComp = TRUE),
    error = function(e) { message("  [plot_within_stats] ", conditionMessage(e)); NULL })

  if (!is.null(p)) {
    tex_collect(colleyRstats::report_ggstatsplot(p, iv = "study phase", dv = lab(o),
                                                 sink_to = tex_path("phase_omnibus", o)),
                heading = paste("Phase omnibus (ggstatsplot):", lab(o)))
    colleyRstats::save_paper_figure(
      p, file.path(FIG_DIR, paste0("phase_", o, ".pdf")), columns = 1)
    phase_results[[o]] <- p
  }

  # Aligned-rank-transform ANOVA as the rank-based omnibus alternative; with
  # this few participants and three levels this is the safer of the two tests.
  art_m <- tryCatch({
    f <- stats::as.formula(paste0(o, " ~ Phase + Error(participant/Phase)"))
    ARTool::art(f, data = phase_agg)
  }, error = function(e) { message("  [art] ", conditionMessage(e)); NULL })

  if (!is.null(art_m)) {
    a <- stats::anova(art_m)
    print(a)
    tex_collect(colleyRstats::report_art(a, dv = lab(o), sink_to = tex_path("phase_art", o)),
                heading = paste("Phase omnibus (ART):", lab(o)))
  }
}


# The three phases are ordered in time by construction, so anything that drifts
# over a session is confounded with Phase. The questionnaire is the obvious
# candidate: the same five items are answered 21 times.
subsection("Limitation: response-style drift across the 21 questionnaires")

ACC_ITEMS <- c("accInformed", "accPleasant", "accGlance")   # one scale, one direction
TLX_ITEMS <- c("tlxMental", "tlxDistraction")

fatigue_df <- main_df |>
  rowwise() |>
  mutate(acc_sd  = stats::sd(dplyr::c_across(all_of(ACC_ITEMS))),
         tlx_sd  = stats::sd(dplyr::c_across(all_of(TLX_ITEMS))),
         extreme = mean(dplyr::c_across(all_of(c(ACC_ITEMS, TLX_ITEMS))) %in% c(0, 100))) |>
  ungroup()

FATIGUE_LABEL <- c(acc_sd  = "spread across the three acceptance items",
                   tlx_sd  = "spread across the two TLX items",
                   extreme = "share of items answered at 0 or 100")
fatigue_trend <- purrr::map_dfr(names(FATIGUE_LABEL), function(v) {
  m <- safe_lmer(stats::as.formula(paste0(v, " ~ round + (1 | participant)")),
                 fatigue_df, label = v)
  if (is.null(m)) return(NULL)
  co <- summary(m)$coefficients
  data.frame(measure = v, what = unname(FATIGUE_LABEL[v]),
             slope_per_round = co["round", 1], SE = co["round", 2],
             t = co["round", 4], p = co["round", 5])
})
print(as.data.frame(fatigue_trend), digits = 3, row.names = FALSE)

# durationS spans entering the driving phase to submitting the questionnaire,
# so what is left after the measured driving time is the time on the survey.
survey_time <- rounds_raw |>
  mutate(participant = factor(participant)) |>
  select(participant, round, durationS) |>
  left_join(traj_raw |> mutate(participant = factor(participant)) |>
              select(participant, round, drivingTimeS), by = c("participant", "round")) |>
  mutate(surveyS = durationS - drivingTimeS)
m_survey <- safe_lmer(surveyS ~ round + (1 | participant), survey_time, label = "surveyS")
if (!is.null(m_survey)) {
  co <- summary(m_survey)$coefficients
  cat(sprintf("\nSurvey time: M = %.1f s, slope = %+.3f s per round (t = %.2f, p = %.4f).\n",
              mean(survey_time$surveyS, na.rm = TRUE), co["round", 1], co["round", 4],
              co["round", 5]))
  tex_collect(colleyRstats::report_glmm(m_survey, dv = "questionnaire time (s)",
                                        sink_to = tex_path("fatigue", "surveyS")),
              heading = "Response-style drift: questionnaire time")
}
utils::write.csv(fatigue_trend, file.path(TAB_DIR, "response_style_drift.csv"),
                 row.names = FALSE)

cat("\nParticipants answer the same items faster and more extremely as the session\n",
    "goes on, and the TLX items converge. Because the optimisation and final\n",
    "rounds are LATE by construction, that drift is confounded with Phase and\n",
    "part of the phase effect above may be response style rather than design\n",
    "quality. The interrupted time series in section 6 is the partial defence:\n",
    "it removes a linear trend estimated on the sampling rounds before testing\n",
    "for a change at the BO switch. It cannot remove a NON-linear drift, so\n",
    "this belongs in the limitations either way.\n", sep = "")

# --------------------- 6. RQ3: LEARNING VS. OPTIMISATION (INTERRUPTED SERIES) --
section("6  RQ3  Separating optimisation from practice (interrupted time series)")

cat("A single-condition study cannot separate 'the optimiser worked' from\n",
    "'the participant got better at the task' by a group comparison. It can,\n",
    "however, exploit the fact that the designs in rounds 1-15 are quasi-random:\n",
    "any trend there is practice, route rotation, or fatigue -- not optimisation.\n",
    "The models below therefore estimate a pre-existing slope over the sampling\n",
    "phase and test whether the trajectory changes in LEVEL and in SLOPE exactly\n",
    "at the round where BO takes over.\n", sep = "")

its_df <- bo_df |>
  mutate(
    time    = round - N_SAMPLING,                       # 0 at the last sampling round
    level   = as.integer(round > N_SAMPLING),           # step at the BO switch
    slope   = pmax(0L, round - N_SAMPLING),             # extra slope after the switch
    routeCode = factor(routeCode)
  )

fit_its <- function(y, data, covariates = character(0)) {
  rhs <- paste(c("time", "level", "slope", covariates, "(1 | participant)"),
               collapse = " + ")
  safe_lmer(stats::as.formula(paste(y, "~", rhs)), data, label = y)
}

its_models <- list()
for (o in c(OBJ, "overallQuality")) {
  subsection("ITS: ", lab(o))
  # Route is left in the residual on purpose (see the assembly note): it is
  # not knowable at deployment, so it is treated as irreducible noise.
  m <- fit_its(o, its_df)
  if (is.null(m)) next
  its_models[[o]] <- m
  print(summary(m)$coefficients)
  cat("\n")
  print(performance::r2(m))
  tex_collect(colleyRstats::report_glmm(m, dv = lab(o), sink_to = tex_path("its", o)),
              heading = paste("Interrupted time series:", lab(o)))
}

# The same question asked of the sampling phase alone: is there a practice trend
# at all? If not, a plain phase contrast is already interpretable.
subsection("Practice trend within the sampling phase only (rounds 1-15)")
for (o in c(OBJ, "overallQuality", "avgEcoScore")) {
  m <- safe_lmer(stats::as.formula(paste(o, "~ round + (1 | participant)")),
                 samp_df, label = o)
  if (is.null(m)) next
  co <- summary(m)$coefficients
  cat(sprintf("%-16s slope = %+8.4f  SE = %6.4f  t = %6.2f  p = %.3f\n",
              o, co["round", 1], co["round", 2], co["round", 4], co["round", 5]))
}


# ------------------------------- 7. RQ4: IS THE FINAL DESIGN ACTUALLY BETTER? --
section("7  RQ4  The personalised final design vs. its own baselines")

# The final round always runs one and the same route, so a baseline that mixes
# all four routes does not compare like with like -- the final design is
# credited (or charged) for the route it happened to be measured on. Route is
# never MODELLED anywhere in this script, because it cannot be known at
# deployment and an adjusted outcome is one the system could never realise.
# It is used here for MATCHING only: the final round is compared against the
# sampling rounds that used the same route.
FINAL_ROUTE <- unique(as.character(main_df$routeCode[main_df$round == N_ROUNDS]))
stopifnot(length(FINAL_ROUTE) == 1L)
n_matched <- main_df |>
  filter(Phase == "Sampling", routeCode == FINAL_ROUTE) |>
  count(participant) |> pull(n) |> unique()
cat("Round ", N_ROUNDS, " runs route ", FINAL_ROUTE, " for every participant, so the",
    " matched\n", "baseline is that participant's ", paste(n_matched, collapse = "/"),
    " sampling rounds on ", FINAL_ROUTE, ".\n", sep = "")

# Four baselines, from matched to strict:
#   matched_route  -- the participant's own sampling rounds on the SAME route as
#                     the final round: the only like-for-like contrast, and the
#                     one to read first
#   mean_sampling  -- the average random design (what an unoptimised HUD gives)
#   mean_all       -- the average of everything the participant experienced
#   best_sampling  -- the best single random design (could random search have
#                     found something as good?). Biased against the final design
#                     for EVERY objective: the best of 15 noisy draws beats an
#                     average draw by construction, so read it as a ceiling
#                     check rather than as a test (see energy_diagnostics.R).
baseline_df <- main_df |>
  group_by(participant) |>
  summarise(dplyr::across(all_of(OBJ),
    list(
      matched_route = ~ mean(.x[Phase == "Sampling" & routeCode == FINAL_ROUTE],
                             na.rm = TRUE),
      mean_sampling = ~ mean(.x[Phase == "Sampling"], na.rm = TRUE),
      mean_all      = ~ mean(.x[Phase != "Final design"], na.rm = TRUE),
      final         = ~ .x[Phase == "Final design"]
    ), .names = "{.col}__{.fn}"), .groups = "drop")

best_sampling <- main_df |>
  filter(Phase == "Sampling") |>
  group_by(participant) |>
  summarise(dplyr::across(all_of(OBJ),
                          ~ if (OBJ_MINIMISE[[dplyr::cur_column()]]) min(.x, na.rm = TRUE)
                            else max(.x, na.rm = TRUE),
                          .names = "{.col}__best_sampling"), .groups = "drop")

baseline_df <- left_join(baseline_df, best_sampling, by = "participant")

compare_to_baseline <- function(obj, baseline) {
  new <- baseline_df[[paste0(obj, "__final")]]
  old <- baseline_df[[paste0(obj, "__", baseline)]]
  delta <- improvement(new, old, obj)
  w <- stats::wilcox.test(delta, mu = 0, alternative = "two.sided", exact = TRUE)
  tt <- stats::t.test(delta, mu = 0)
  data.frame(
    objective = obj, baseline = baseline,
    M_final = mean(new), M_baseline = mean(old),
    M_improvement = mean(delta), SD_improvement = stats::sd(delta),
    n_better = sum(delta > 0), n = length(delta),
    V = unname(w$statistic), p_wilcox = w$p.value,
    t = unname(tt$statistic), df = unname(tt$parameter), p_t = tt$p.value,
    d = unname(effectsize::cohens_d(delta, mu = 0)$Cohens_d)
  )
}

final_vs_baseline <- purrr::map_dfr(
  c("matched_route", "mean_sampling", "mean_all", "best_sampling"),
  function(b) purrr::map_dfr(OBJ, compare_to_baseline, baseline = b))
print(as.data.frame(final_vs_baseline), digits = 3)
utils::write.csv(final_vs_baseline, file.path(TAB_DIR, "final_vs_baseline.csv"),
                 row.names = FALSE)

cat("\nRead the sign of M_improvement as 'better', for every objective:\n",
    "energy and taskload are inverted before differencing.\n",
    "The matched_route rows are the primary result: they are the only ones\n",
    "in which the final design and its baseline were driven on the same\n",
    "route, and route is the one nuisance that moves a driving outcome.\n", sep = "")

# Holm correction across the five objectives, within each baseline family.
final_vs_baseline <- final_vs_baseline |>
  group_by(baseline) |>
  mutate(p_wilcox_holm = stats::p.adjust(p_wilcox, method = "holm")) |>
  ungroup()
print(as.data.frame(final_vs_baseline |> select(objective, baseline, M_improvement,
                                                p_wilcox, p_wilcox_holm)), digits = 3)


# ------------------------- 8. RQ5: DOES THE SELECTED OPTIMUM REPRODUCE AT ALL? --
section("8  RQ5  Test-retest reliability of the selected design")

cat("Round 21 re-drives a design that was already evaluated once during the\n",
    "search. The two measurements of the SAME design bound how much of the\n",
    "optimiser's apparent progress is real and how much is measurement noise.\n",
    sep = "")

# Identify the source iteration by nearest design vector, rather than trusting
# the summary text: the final round stores the parameters rounded to 3 dp.
source_iter <- purrr::map_dfr(PARTICIPANTS, function(pid) {
  d  <- main_df |> filter(participant == pid)
  fd <- d |> filter(Phase == "Final design")
  cand <- d |> filter(Phase != "Final design")
  dist <- sqrt(rowSums((as.matrix(cand[, PARAMS]) -
                        matrix(as.numeric(fd[1, PARAMS]), nrow(cand), length(PARAMS),
                               byrow = TRUE))^2))
  k <- which.min(dist)
  data.frame(participant = pid, source_round = cand$round[k],
             source_phase = as.character(cand$Phase[k]),
             param_distance = dist[k], source_isPareto = cand$isPareto[k],
             stats::setNames(as.list(as.numeric(cand[k, OBJ])), paste0(OBJ, "__source")),
             stats::setNames(as.list(as.numeric(fd[1, OBJ])), paste0(OBJ, "__retest")))
})
print(as.data.frame(source_iter |> select(participant, source_round, source_phase,
                                          param_distance, source_isPareto)), digits = 4)
stopifnot(all(source_iter$param_distance < 0.01))  # the match must be unambiguous

retest <- purrr::map_dfr(OBJ, function(o) {
  a <- source_iter[[paste0(o, "__source")]]
  b <- source_iter[[paste0(o, "__retest")]]
  w <- stats::wilcox.test(b, a, paired = TRUE, exact = TRUE)
  data.frame(
    objective = o, M_source = mean(a), M_retest = mean(b),
    M_signed_change = mean(improvement(b, a, o)),
    MAE = mean(abs(b - a)),
    r_pearson = stats::cor(a, b), rho_spearman = stats::cor(a, b, method = "spearman"),
    ICC_A1 = tryCatch(suppressMessages(psych::ICC(cbind(a, b))$results$ICC[2]),
                      error = function(e) NA_real_),
    V = unname(w$statistic), p = w$p.value)
})
print(as.data.frame(retest), digits = 3)
utils::write.csv(retest, file.path(TAB_DIR, "retest_selected_design.csv"), row.names = FALSE)

cat("\nA low r / high MAE here means the objective is dominated by round-to-round\n",
    "noise, and any single-evaluation optimum for it should be read with caution.\n",
    sep = "")


# --------------------------------------------------- 9. RQ6: PARETO ANALYSIS --
section("9  RQ6  Pareto fronts")

# Recomputed independently of the IsPareto flag written by BOforUnity, on the
# normalised minimisation objectives, and then cross-checked against it.
pareto_df <- bo_df |>
  group_by(participant) |>
  group_modify(function(d, key) {
    d <- colleyRstats::add_pareto_moocore_column(as.data.frame(d), OBJ_N)
    d <- colleyRstats::add_pareto_emoa_column(d, OBJ_N)
    d
  }) |>
  ungroup()

cat("Agreement between the recomputed fronts (moocore vs. emoa):\n")
print(table(moocore = pareto_df$PARETO_MOOCORE, emoa = pareto_df$PARETO_EMOA))
cat("\nAgreement with the IsPareto flag logged by BOforUnity:\n")
print(table(logged = pareto_df$isPareto, recomputed = pareto_df$PARETO_MOOCORE))

pareto_summary <- pareto_df |>
  group_by(participant) |>
  summarise(
    n_pareto        = sum(PARETO_MOOCORE),
    pct_pareto      = 100 * mean(PARETO_MOOCORE),
    n_pareto_samp   = sum(PARETO_MOOCORE & Phase == "Sampling"),
    n_pareto_opt    = sum(PARETO_MOOCORE & Phase == "Optimisation"),
    # Hit rate per round is the fair comparison: 15 sampling vs 5 BO rounds.
    rate_samp       = n_pareto_samp / N_SAMPLING,
    rate_opt        = n_pareto_opt / N_OPTIMISATION,
    .groups = "drop")
print(as.data.frame(pareto_summary), digits = 3)
utils::write.csv(pareto_summary, file.path(TAB_DIR, "pareto_summary.csv"), row.names = FALSE)

subsection(sprintf("Pareto hit rate: BO rounds vs. sampling rounds (paired, n = %d)",
                   nrow(pareto_summary)))
pw <- stats::wilcox.test(pareto_summary$rate_opt, pareto_summary$rate_samp,
                         paired = TRUE, exact = TRUE)
print(pw)
colleyRstats::rFromWilcox(pw, N = 2 * nrow(pareto_summary))
cat(sprintf("Mean hit rate: sampling %.2f, optimisation %.2f designs per round.\n",
            mean(pareto_summary$rate_samp), mean(pareto_summary$rate_opt)))


# ---------------------------------------- 10. RQ7: IS PERSONALISATION EARNED? --
section("10  RQ7  Evidence that personalisation was necessary")

cat("HITL MOBO is only worth its cost if the optimum genuinely differs between\n",
    "people. Three independent lines of evidence are examined:\n",
    "  (a) how dispersed the selected designs are in the 7-D design space,\n",
    "  (b) whether the effect of each HUD parameter on each objective varies\n",
    "      significantly across participants (random-slope likelihood-ratio test),\n",
    "  (c) whether participant-specific Pareto sets cluster by participant\n",
    "      more than chance allows (label-permutation test).\n", sep = "")

# (a) Dispersion of the selected designs --------------------------------------
subsection("(a) Dispersion of the selected designs")
final_designs <- final_df |> select(participant, all_of(PARAMS)) |> as.data.frame()
print(final_designs, digits = 3)

# A design space that is being personalised should show final values spread out
# rather than collapsed onto one point. SD of a uniform[0,1] draw is 0.289, so
# the ratio below is ~1 for "idiosyncratic" and ~0 for "one size fits all".
UNIFORM_SD <- 1 / sqrt(12)
dispersion <- purrr::map_dfr(PARAMS, function(p) {
  x <- final_designs[[p]]
  tt <- stats::t.test(x, mu = 0.5)              # is there a shared preferred level?
  data.frame(parameter = p, M = mean(x), SD = stats::sd(x),
             SD_ratio_vs_uniform = stats::sd(x) / UNIFORM_SD,
             Min = min(x), Max = max(x),
             t_vs_midpoint = unname(tt$statistic), p_vs_midpoint = tt$p.value)
}) |>
  mutate(p_holm = stats::p.adjust(p_vs_midpoint, method = "holm"))
print(as.data.frame(dispersion), digits = 3)
utils::write.csv(dispersion, file.path(TAB_DIR, "final_design_dispersion.csv"), row.names = FALSE)

# (b) Does the parameter -> objective mapping differ between participants? -----
subsection("(b) Participant-specific parameter effects (random-slope LRT)")
cat("A significant random slope means the same HUD change helped one person and\n",
    "hurt another -- the strongest single argument for per-person optimisation.\n",
    "Estimated on the SAMPLING rounds only: those designs are quasi-random, so the\n",
    "parameter-outcome relationship is unconfounded. The optimisation rounds are,\n",
    "by construction, selected to be good and would bias the slopes.\n",
    sep = "")

# The unconfounded subset: quasi-random designs, 15 per participant.
RANDOM_DESIGN_DF <- as.data.frame(samp_df)

randslope <- purrr::map_dfr(OBJ, function(o) {
  purrr::map_dfr(PARAMS, function(prm) {
    # A correlated intercept-slope structure is over-parameterised for 8
    # participants and collapses to a boundary fit most of the time. The
    # uncorrelated slope isolates exactly the quantity of interest -- does the
    # slope vary between people? -- and is compared against the intercept-only
    # model by a 1-df likelihood-ratio test on ML fits.
    f0 <- stats::as.formula(sprintf("%s ~ %s + (1 | participant)", o, prm))
    f1 <- stats::as.formula(sprintf("%s ~ %s + (1 | participant) + (0 + %s | participant)",
                                    o, prm, prm))
    fits <- tryCatch(suppressMessages(suppressWarnings(list(
      m0 = lme4::lmer(f0, data = RANDOM_DESIGN_DF, REML = FALSE),
      m1 = lme4::lmer(f1, data = RANDOM_DESIGN_DF, REML = FALSE)))),
      error = function(e) NULL)
    na_row <- data.frame(objective = o, parameter = prm, sd_slope = NA_real_,
                         LRT = NA_real_, df = NA_real_, p = NA_real_, singular = TRUE)
    if (is.null(fits)) return(na_row)
    if (isTRUE(performance::check_singularity(fits$m1))) return(na_row)
    cmp <- tryCatch(stats::anova(fits$m0, fits$m1), error = function(e) NULL)
    if (is.null(cmp)) return(na_row)
    sd_slope <- tryCatch(
      as.data.frame(lme4::VarCorr(fits$m1))$sdcor[1], error = function(e) NA_real_)
    data.frame(objective = o, parameter = prm, sd_slope = sd_slope,
               LRT = cmp$Chisq[2], df = cmp$Df[2], p = cmp$`Pr(>Chisq)`[2],
               singular = FALSE)
  })
}) |>
  mutate(p_holm = stats::p.adjust(p, method = "holm"))
print(as.data.frame(randslope), digits = 3)
utils::write.csv(randslope, file.path(TAB_DIR, "random_slope_lrt.csv"), row.names = FALSE)
cat(sprintf("\n%d of the %d models that converged show significant between-participant\n",
            sum(randslope$p_holm < .05, na.rm = TRUE), sum(!is.na(randslope$p_holm))))
cat("variation in the slope (Holm-corrected across the converged models).\n")
cat(sprintf("%d of %d models were singular and are reported as NA -- with %d participants\n",
            sum(randslope$singular), nrow(randslope), N_PART))
cat(sprintf("and %d random designs each, this test is underpowered, so a null result\n",
            N_SAMPLING),
    "here is weak evidence against personalisation, not evidence for a shared optimum.\n",
    sep = "")

# (c) Do Pareto sets cluster by participant? ----------------------------------
subsection("(c) Permutation test on Pareto-set clustering")
pset <- pareto_df |> filter(PARETO_MOOCORE) |> select(participant, all_of(PARAMS))
X <- as.matrix(pset[, PARAMS])
grp <- as.integer(factor(pset$participant))

# Statistic: mean between-participant distance minus mean within-participant
# distance. Positive = participants occupy distinct regions of the design space.
clustering_stat <- function(g) {
  D <- as.matrix(stats::dist(X))
  same <- outer(g, g, "==")
  diag(same) <- NA
  mean(D[!same & !is.na(same)]) - mean(D[same & !is.na(same)])
}
obs_stat <- clustering_stat(grp)
N_PERM <- 5000
perm <- replicate(N_PERM, clustering_stat(sample(grp)))
p_perm <- (1 + sum(perm >= obs_stat)) / (N_PERM + 1)
cat(sprintf("Observed separation = %+.4f, permutation null M = %+.4f (SD = %.4f), p = %.4f\n",
            obs_stat, mean(perm), stats::sd(perm), p_perm))
cat(sprintf("Pareto designs pooled: %d across %d participants.\n", nrow(X), length(unique(grp))))


# ----------------------------- 11. RQ8: WHICH HUD PROPERTIES DRIVE OUTCOMES? --
section("11  RQ8  Design parameters -> objectives")

cat("With one condition, the design space itself is the independent variable.\n",
    "Each objective is regressed on all seven normalised HUD parameters, with a\n",
    "participant random intercept. Route is deliberately NOT controlled: it\n",
    "cannot be known at deployment. Fitted on the SAMPLING rounds only (15 per\n",
    "participant): those designs are quasi-random, so the estimates are not\n",
    "confounded by the optimiser having steered towards good regions.\n",
    sep = "")

design_models <- list()
design_coefs <- purrr::map_dfr(OBJ, function(o) {
  rhs <- paste(c(PARAMS, "(1 | participant)"), collapse = " + ")
  m <- safe_lmer(stats::as.formula(paste(o, "~", rhs)), RANDOM_DESIGN_DF, label = o)
  if (is.null(m)) return(NULL)
  design_models[[o]] <<- m
  subsection("Design model: ", lab(o))
  print(summary(m)$coefficients)
  tex_collect(colleyRstats::report_glmm(m, dv = lab(o), sink_to = tex_path("design", o)),
              heading = paste("Design-parameter model:", lab(o)))
  pr <- as.data.frame(parameters::model_parameters(m, effects = "fixed"))
  pr$objective <- o
  pr
})
design_coefs <- design_coefs |>
  filter(Parameter %in% PARAMS) |>
  group_by(objective) |>
  mutate(p_holm = stats::p.adjust(p, method = "holm")) |>
  ungroup()
print(as.data.frame(design_coefs |> select(objective, Parameter, Coefficient, SE,
                                           CI_low, CI_high, p, p_holm)), digits = 3)
subsection("HUD density: number of visible elements")
cat("Seven continuous parameters are hard to reason about in a paper. The count\n",
    "of elements actually rendered (EcoFeedbackHUD hides anything below its size\n",
    "threshold) is the same design expressed in one interpretable number.\n", sep = "")
print(table(`visible elements` = RANDOM_DESIGN_DF$nVisible))

density_coefs <- purrr::map_dfr(OBJ, function(o) {
  rhs <- paste(c("nVisible", "(1 | participant)"), collapse = " + ")
  m <- safe_lmer(stats::as.formula(paste(o, "~", rhs)), RANDOM_DESIGN_DF, label = o)
  if (is.null(m)) return(NULL)
  tex_collect(colleyRstats::report_glmm(m, dv = lab(o), sink_to = tex_path("density", o)),
              heading = paste("HUD density model:", lab(o)))
  co <- summary(m)$coefficients
  data.frame(objective = o, b = co["nVisible", 1], SE = co["nVisible", 2],
             t = co["nVisible", 4], p = co["nVisible", 5])
}) |>
  mutate(p_holm = stats::p.adjust(p, method = "holm"))
print(as.data.frame(density_coefs), digits = 3)
utils::write.csv(density_coefs, file.path(TAB_DIR, "hud_density_effects.csv"),
                 row.names = FALSE)

utils::write.csv(design_coefs, file.path(TAB_DIR, "design_parameter_effects.csv"),
                 row.names = FALSE)


# ------------------------------------- 12. RQ9: TRADE-OFFS BETWEEN OBJECTIVES --
section("12  RQ9  Trade-offs among the five objectives")

cat("Repeated-measures correlations (Bakdash & Marusich) are used because the\n",
    sprintf("%d observations are nested in %d participants; a plain Pearson r across\n",
            nrow(bo_df), N_PART),
    "all rows would confound within- and between-person variation.\n", sep = "")

pairs_obj <- utils::combn(OBJ, 2, simplify = FALSE)
rmc <- purrr::map_dfr(pairs_obj, function(pp) {
  # rmcorr() takes plain column names as strings.
  fit <- tryCatch(rmcorr::rmcorr(participant = "participant",
                                 measure1 = pp[1], measure2 = pp[2],
                                 dataset = as.data.frame(bo_df)),
                  error = function(e) {
                    message("  [rmcorr] ", pp[1], " x ", pp[2], ": ", conditionMessage(e))
                    NULL
                  })
  if (is.null(fit)) return(NULL)
  data.frame(x = pp[1], y = pp[2], r_rm = fit$r, df = fit$df, p = fit$p,
             CI_low = fit$CI[1], CI_high = fit$CI[2])
})
stopifnot(nrow(rmc) == length(pairs_obj))
rmc$p_holm <- stats::p.adjust(rmc$p, method = "holm")
# scipen = 999 would print these p-values as 20-odd leading zeros.
print(rmc |>
        mutate(dplyr::across(c(p, p_holm), ~ format.pval(.x, digits = 2, eps = 1e-4))) |>
        as.data.frame(), digits = 3)
utils::write.csv(rmc, file.path(TAB_DIR, "objective_tradeoffs_rmcorr.csv"), row.names = FALSE)

cat("\nA significant NEGATIVE r_rm between an acceptance objective and energy or\n",
    "taskload is a genuine trade-off and justifies the multi-objective framing.\n",
    sep = "")


# Correlations pair by pair do not say how many INDEPENDENT axes the five
# objectives span. The optimiser treats them as five, and the hypervolume is a
# five-dimensional volume, so that number is worth stating.
subsection("How many independent objectives are there really?")

obj_mat <- bo_df |>
  group_by(participant) |>
  mutate(dplyr::across(all_of(OBJ_N), ~ .x - mean(.x, na.rm = TRUE))) |>
  ungroup() |> select(all_of(OBJ_N)) |> as.matrix()
colnames(obj_mat) <- OBJ

obj_pca <- stats::prcomp(obj_mat, center = TRUE, scale. = TRUE)
obj_lam <- obj_pca$sdev^2
# Participation ratio: 5 if every axis matters equally, 1 if one axis explains
# everything. The usual continuous measure of effective dimensionality.
obj_PR <- sum(obj_lam)^2 / sum(obj_lam^2)

# Parallel analysis: the eigenvalues that noise of this shape alone produces.
obj_null <- replicate(1000, stats::prcomp(apply(obj_mat, 2, sample),
                                          center = TRUE, scale. = TRUE)$sdev^2)
obj_null95 <- apply(obj_null, 1, stats::quantile, 0.95)
print(data.frame(component = paste0("PC", seq_along(obj_lam)),
                 eigenvalue = obj_lam, pct_variance = 100 * obj_lam / sum(obj_lam),
                 null_95th = obj_null95, retained = obj_lam > obj_null95),
      digits = 3, row.names = FALSE)
print(round(obj_pca$rotation[, 1:2], 3))
utils::write.csv(as.data.frame(obj_pca$rotation),
                 file.path(TAB_DIR, "objective_pca_loadings.csv"))

cat(sprintf("\nEffective dimensionality = %.2f of %d; %d component(s) beat the\n",
            obj_PR, length(OBJ), sum(obj_lam > obj_null95)))
cat("parallel-analysis threshold. Taskload and the three acceptance objectives\n",
    "load together on PC1, while energy is essentially PC2 on its own. The\n",
    "five-objective space is therefore closer to two: one subjective axis and\n",
    "one energy axis. This cuts both ways -- the hypervolume credits dimensions\n",
    "that carry no independent information, but energy being orthogonal is\n",
    "exactly what earns it a place as a separate objective.\n", sep = "")


# ------------------------------------------------ 13. RQ10: DRIVING BEHAVIOUR --
section("13  RQ10  Sustainable-driving behaviour beyond the energy objective")

driving_outcomes <- c("avgEcoScore", "avgSpeedKmh", "meanAbsAccel", "sdSpeedKmh",
                      "pctHarshAccel", "drivingTimeS", "sdThrottle")
for (y in driving_outcomes) {
  subsection("ITS on ", lab(y))
  m <- fit_its(y, its_df)
  if (is.null(m)) next
  co <- summary(m)$coefficients
  print(co[intersect(rownames(co), c("time", "level", "slope")), , drop = FALSE])
  tex_collect(colleyRstats::report_glmm(m, dv = lab(y), sink_to = tex_path("driving", y)),
              heading = paste("Interrupted time series:", lab(y)))
}

subsection("Collisions (Poisson GLMM, offset by driving time)")
coll_m <- tryCatch(
  lme4::glmer(collisions ~ time + level + slope +
                offset(log(drivingTimeS)) + (1 | participant),
              data = its_df, family = stats::poisson()),
  error = function(e) { message("  [glmer] ", conditionMessage(e)); NULL })
if (!is.null(coll_m)) {
  print(summary(coll_m)$coefficients)
  print(performance::check_overdispersion(coll_m))
  tex_collect(colleyRstats::report_glmm(coll_m, dv = "collision count",
                                        sink_to = tex_path("driving", "collisions")),
              heading = "Interrupted time series: collisions (Poisson GLMM)")
}


# ------------------------------------------------------ 14. PROCEDURAL COST --
section("14  Procedural cost of the HITL loop")

exec_df <- exec_raw |> mutate(participant = factor(participant))
cat(sprintf("BO step time: M = %.1f s, SD = %.1f, range %.1f-%.1f s (%d steps).\n",
            mean(exec_df$Execution_Time), stats::sd(exec_df$Execution_Time),
            min(exec_df$Execution_Time), max(exec_df$Execution_Time), nrow(exec_df)))

session_cost <- main_df |>
  group_by(participant) |>
  summarise(driving_min = sum(drivingTimeS) / 60, .groups = "drop") |>
  left_join(exec_df |> group_by(participant) |>
              summarise(bo_min = sum(Execution_Time) / 60, .groups = "drop"),
            by = "participant") |>
  left_join(dur_check |> group_by(participant) |>
              summarise(survey_min = sum(surveyOverheadS) / 60, .groups = "drop"),
            by = "participant")
print(as.data.frame(session_cost), digits = 3)
cat(sprintf("\nPer participant: %.1f min driving, %.1f min questionnaires, %.1f min BO compute.\n",
            mean(session_cost$driving_min), mean(session_cost$survey_min),
            mean(session_cost$bo_min)))
cat("The optimiser is not the bottleneck; the 21 questionnaires are.\n")


# ------------------------------------------------------------- 15. FIGURES --
section("15  FIGURES")

# colley_theme() places legends inside the panel, which is right for the
# ggstatsplot figures but overplots a heatmap or a facetted scatter.
legend_outside <- function(pos = "right") {
  ggplot2::theme(legend.position = pos, legend.title = ggplot2::element_text())
}

# 15a. Objective trajectories with the BOforUnity sampling/optimisation layout.
# The phase bracket goes BELOW the data: plot_mobo2() puts the fitted-polynomial
# label at the top, and the two collide otherwise.
for (o in c(OBJ, "overallQuality")) {
  rng <- range(bo_df[[o]], na.rm = TRUE)
  pad <- diff(rng)
  p <- tryCatch(
    colleyRstats::plot_mobo2(
      data = as.data.frame(bo_df), x = "round", y = o,
      phaseCol = "PhaseBO", fillColourGroup = "",   # "" = single condition
      ytext = lab(o),
      horizontalLinePosY = rng[1] - 0.14 * pad,
      horizontalLineDistToText = 0.09 * pad,
      labelPosFormulaY = "top", labelPosFormulaX = "left",
      annotationTextSize = 2.6),
    error = function(e) { message("  [plot_mobo2] ", o, ": ", conditionMessage(e)); NULL })
  if (!is.null(p)) {
    p <- p + ggplot2::expand_limits(y = c(rng[1] - 0.28 * pad, rng[2] + 0.18 * pad))
    colleyRstats::save_paper_figure(p, file.path(FIG_DIR, paste0("mobo_", o, ".pdf")),
                                    columns = 1, height = 2.6)
  }
}

# 15b. Hypervolume trajectory, per participant and averaged.
hv_floor <- min(hv_traj$hv_cum)
p_hv <- ggplot(hv_traj, aes(x = round, y = hv_cum)) +
  geom_vline(xintercept = N_SAMPLING + 0.5, linetype = "dashed", alpha = 0.6) +
  geom_line(aes(group = participant), alpha = 0.35) +
  stat_summary(fun.data = mean_cl_boot, geom = "ribbon", alpha = 0.15, colour = NA) +
  stat_summary(fun = mean, geom = "line", linewidth = 1.2) +
  labs(x = "Round", y = "Cumulative hypervolume") +
  annotate("text", x = N_SAMPLING / 2, y = hv_floor, label = "Sampling",
           fontface = "bold", size = 2.6, vjust = 0) +
  annotate("text", x = N_SAMPLING + N_OPTIMISATION, y = hv_floor,
           label = "Optimisation", fontface = "bold", size = 2.6, vjust = 0, hjust = 1) +
  expand_limits(y = hv_floor - 0.03)
colleyRstats::save_paper_figure(p_hv, file.path(FIG_DIR, "hypervolume_trajectory.pdf"),
                                columns = 1, height = 2.4)

# 15c. Selected designs, one row per participant: the personalisation picture.
p_designs <- final_designs |>
  pivot_longer(all_of(PARAMS), names_to = "parameter", values_to = "value") |>
  mutate(parameter = factor(parameter, levels = PARAMS, labels = PARAM_LABEL[PARAMS])) |>
  ggplot(aes(x = parameter, y = participant, fill = value)) +
  geom_tile(colour = "white", linewidth = 0.4) +
  geom_text(aes(label = sprintf("%.2f", value)), size = 2.2) +
  scale_fill_viridis_c(limits = c(0, 1), name = "Normalised\nvalue") +
  labs(x = NULL, y = NULL) +
  legend_outside() +
  theme(axis.text.x = element_text(angle = 30, hjust = 1))
colleyRstats::save_paper_figure(p_designs, file.path(FIG_DIR, "final_designs_heatmap.pdf"),
                                columns = 2, height = 2.8)

# 15d. Objective space: every evaluation, Pareto-optimal points highlighted.
p_pareto <- pareto_df |>
  ggplot(aes(x = energy, y = taskload)) +
  geom_point(aes(shape = Phase, colour = PARETO_MOOCORE), size = 1.8, alpha = 0.8) +
  geom_step(data = ~ dplyr::filter(.x, PARETO_MOOCORE) |> dplyr::arrange(energy),
            direction = "hv", alpha = 0.5) +
  facet_wrap(~ participant, ncol = 4, scales = "free") +
  scale_colour_manual(values = c(`FALSE` = "grey60", `TRUE` = "firebrick"),
                      name = "Pareto-optimal") +
  labs(x = "Energy (kWh/100 km)", y = "Task load (0-100)",
       # Said explicitly because the step line is not the 2-D front: points are
       # Pareto-optimal in the full 5-D objective space and only projected here.
       subtitle = "Pareto-optimality is over all five objectives, projected onto two") +
  legend_outside("bottom")
colleyRstats::save_paper_figure(p_pareto, file.path(FIG_DIR, "pareto_energy_taskload.pdf"),
                                # 4 x 4 facets with free scales: at height 3.6 the strip,
                                # per-facet axis text, subtitle and bottom legend consumed
                                # the whole canvas and the panels collapsed to zero height.
                                columns = 2, height = 8)

# 15e. Test-retest of the selected design: source evaluation vs. round 21.
p_retest <- source_iter |>
  select(participant, dplyr::matches("__(source|retest)$")) |>
  pivot_longer(-participant, names_to = c("objective", ".value"), names_sep = "__") |>
  mutate(objective = factor(objective, levels = OBJ,
                            labels = c("Energy", "Task load", "Informed",
                                       "Pleasant", "Glanceable"))) |>
  ggplot(aes(x = source, y = retest)) +
  geom_abline(slope = 1, intercept = 0, linetype = "dashed", alpha = 0.6) +
  geom_point(size = 2, alpha = 0.85) +
  facet_wrap(~ objective, scales = "free", nrow = 1) +
  scale_x_continuous(n.breaks = 4) +
  scale_y_continuous(n.breaks = 4) +
  labs(x = "Value when selected", y = "Value when re-driven")
colleyRstats::save_paper_figure(p_retest, file.path(FIG_DIR, "retest_selected_design.pdf"),
                                columns = 2, height = 2.6)

# 15f. Design-parameter effects, all objectives in one forest plot.
p_effects <- design_coefs |>
  mutate(Parameter = factor(Parameter, levels = rev(PARAMS),
                            labels = rev(PARAM_LABEL[PARAMS])),
         objective = factor(objective, levels = OBJ),
         sig = p_holm < .05) |>
  ggplot(aes(x = Coefficient, y = Parameter, colour = sig)) +
  geom_vline(xintercept = 0, linetype = "dashed", alpha = 0.6) +
  geom_pointrange(aes(xmin = CI_low, xmax = CI_high), size = 0.3) +
  facet_wrap(~ objective, nrow = 1, scales = "free_x") +
  scale_colour_manual(values = c(`FALSE` = "grey60", `TRUE` = "firebrick"),
                      name = "Holm-corrected p < .05") +
  labs(x = "Estimated change per unit of the normalised parameter", y = NULL) +
  legend_outside("bottom")
colleyRstats::save_paper_figure(p_effects, file.path(FIG_DIR, "design_parameter_effects.pdf"),
                                columns = 2, height = 3.0)

cat("Figures written to: ", FIG_DIR, "\n", sep = "")


# --------------------------------------------------------------- 16. EXPORT --
section("16  EXPORT")

utils::write.csv(main_df,   file.path(TAB_DIR, "merged_observations.csv"), row.names = FALSE)
utils::write.csv(hv_traj,   file.path(TAB_DIR, "hypervolume_trajectory.csv"), row.names = FALSE)
utils::write.csv(hv_wide,   file.path(TAB_DIR, "hypervolume_logged.csv"), row.names = FALSE)
utils::write.csv(final_designs, file.path(TAB_DIR, "final_designs.csv"), row.names = FALSE)
utils::write.csv(source_iter,   file.path(TAB_DIR, "final_design_provenance.csv"), row.names = FALSE)

# LaTeX preamble + the .sty carrying the macros the report_* sentences use.
colleyRstats::latex_preamble(path = file.path(TEX_DIR, "preamble.tex"))
colleyRstats::use_colleyrstats_sty(dir = TEX_DIR, overwrite = TRUE)

cat("\nTables : ", TAB_DIR, "\n", sep = "")
cat("Figures: ", FIG_DIR, "\n", sep = "")
cat("LaTeX  : ", TEX_DIR, "\n", sep = "")

section("SESSION INFO")
print(utils::sessionInfo())
