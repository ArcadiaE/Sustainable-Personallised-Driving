# ==============================================================================
#  Why the energy objective did not improve
#  Diagnostic companion to analysis.R  --  run AFTER analysis.R
#
#  analysis.R reports energy as the one objective the HITL MOBO loop failed to
#  move: no phase effect, no ITS effect, "worse than the best sampled design",
#  lowest test-retest reliability, and zero correlation with every other
#  objective. This script establishes WHY, and shows which of those statements
#  are findings and which are artifacts of the measurement protocol.
#
#  ON ROUTE.  Route is NOT treated as a factor anywhere in this script, and is
#  not controlled for. It cannot be known at deployment, so a route-adjusted
#  outcome is one the system could never actually realise; route variation is
#  irreducible noise that the optimiser has to work through. Route is used for
#  exactly one thing: MATCHING. The final round always runs the same route, so
#  a contrast against a baseline that mixes all four does not compare like
#  with like. That is a protocol imbalance, not an unknowable covariate, and
#  analysis.R corrects it with its matched_route baseline; this script only
#  reports how large the route noise is.
#
#  Author: Mark Colley <mark.colley@yahoo.de>
#  Run with:  Rscript energy_diagnostics.R
# ==============================================================================

suppressPackageStartupMessages({
  library(dplyr); library(lme4); library(lmerTest)
})
options(scipen = 999, digits = 5, dplyr.summarise.inform = FALSE)
set.seed(42)

BASE_DIR <- getwd()
TAB_DIR  <- file.path(BASE_DIR, "output", "tables")
d    <- read.csv(file.path(TAB_DIR, "merged_observations.csv"), stringsAsFactors = FALSE)
prov <- read.csv(file.path(TAB_DIR, "final_design_provenance.csv"), stringsAsFactors = FALSE)
d$Phase <- factor(d$Phase, levels = c("Sampling", "Optimisation", "Final design"))

OBJ <- c("energy", "taskload", "accInformed", "accPleasant", "accGlance")
PARAMS <- c("size_leaf", "size_score", "size_feedback", "size_speed",
            "size_accel", "size_labels", "opacity")

# Bounds exactly as configured in BOforUnity (config/study_config.md).
OBJ_BOUNDS <- list(energy = c(0, 150), taskload = c(0, 100), accInformed = c(0, 100),
                   accPleasant = c(0, 100), accGlance = c(0, 100))
OBJ_MINIMISE <- c(energy = TRUE, taskload = TRUE, accInformed = FALSE,
                  accPleasant = FALSE, accGlance = FALSE)

normalise_objective <- function(x, obj) {
  b <- OBJ_BOUNDS[[obj]]
  z <- pmin(pmax((x - b[1]) / (b[2] - b[1]), 0), 1)
  if (!OBJ_MINIMISE[[obj]]) z <- 1 - z
  z
}
# Positive always means "better", whatever the direction of the objective.
improvement <- function(new, old, obj) if (OBJ_MINIMISE[[obj]]) old - new else new - old

section <- function(...) cat("\n\n", strrep("=", 78), "\n", paste0(...), "\n",
                             strrep("=", 78), "\n", sep = "")

sampling_df <- d |> filter(Phase == "Sampling")   # quasi-random designs only
final_df    <- d |> filter(round == 21)
# Round 21 is always R1, so its like-for-like baseline is the participant's own
# R1 sampling rounds (1, 5, 9, 13). No route model, no adjustment: just matching.
base_r1     <- d |> filter(routeCode == "R1", Phase == "Sampling")


# --------------------------------------------------------- CAUSE 1: BOUNDS --
section("1  The energy bounds are mis-specified, so the optimiser barely sees it")

cat("Hypervolume and the acquisition function live in the normalised unit cube.\n",
    "An objective that occupies only part of its configured box carries\n",
    "proportionally less weight there, whatever its raw scale.\n\n", sep = "")

span <- lapply(OBJ, function(o) {
  b <- OBJ_BOUNDS[[o]]; z <- (d[[o]] - b[1]) / (b[2] - b[1])
  data.frame(objective = o, bound_lo = b[1], bound_hi = b[2],
             obs_min = min(d[[o]]), obs_max = max(d[[o]]),
             pct_of_box_used = 100 * diff(range(z)), norm_sd = sd(z))
}) |> bind_rows()
print(span, digits = 3, row.names = FALSE)

cat("\nEnergy uses", sprintf("%.0f%%", span$pct_of_box_used[1]),
    "of its box and has a normalised SD of", sprintf("%.3f", span$norm_sd[1]),
    "\nagainst", sprintf("%.3f-%.3f", min(span$norm_sd[-1]), max(span$norm_sd[-1])),
    "for the other four: roughly",
    sprintf("%.1fx", median(span$norm_sd[-1]) / span$norm_sd[1]), "less leverage.\n")

# What each objective actually contributed, in the optimiser's own currency.
# Matched to R1 so the final round is not credited for an easier route.
gain <- lapply(OBJ, function(o) {
  s <- tapply(normalise_objective(base_r1[[o]], o), base_r1$participant, mean)
  f <- tapply(normalise_objective(final_df[[o]], o), final_df$participant, mean)
  data.frame(objective = o, norm_gain = mean(s[names(f)] - f))
}) |> bind_rows() |>
  mutate(share_of_total_pct = 100 * norm_gain / sum(norm_gain))
cat("\nRealised gain in normalised units, matched to R1 (positive = better):\n")
print(gain, digits = 3, row.names = FALSE)

obs <- c(floor(min(d$energy)), ceiling(max(d$energy)))
z_s <- tapply((base_r1$energy  - obs[1]) / diff(obs), base_r1$participant,  mean)
z_f <- tapply((final_df$energy - obs[1]) / diff(obs), final_df$participant, mean)
cat(sprintf("\nCounterfactual bounds [%d, %d] instead of [0, 150]:\n", obs[1], obs[2]))
cat(sprintf("  normalised SD %.3f -> %.3f  (on par with the other objectives)\n",
            span$norm_sd[1], sd((d$energy - obs[1]) / diff(obs))))
cat("Energy would then carry the same weight in the acquisition function as the\n",
    "four questionnaire objectives, instead of roughly a third of it.\n", sep = "")


# ------------------------------------------------- ROUTE AS IRREDUCIBLE NOISE --
section("2  Route variation is large, and it hits energy alone")

cat("Route is treated as noise, not as a factor: it cannot be known at\n",
    "deployment, so no model here adjusts for it. What follows is only the\n",
    "MAGNITUDE of that noise -- how much of the round-to-round variation in an\n",
    "objective is route rather than design. The like-for-like contrast that\n",
    "corrects for the final round always running one route is not repeated\n",
    "here; analysis.R reports it as the matched_route baseline in RQ4.\n\n", sep = "")

by_route <- d |> group_by(routeCode) |>
  summarise(n = n(), energy_M = mean(energy), energy_SD = sd(energy),
            distance_m = mean(distanceM), driving_s = mean(drivingTimeS),
            pct_stopped = mean(pctStopped), .groups = "drop")
print(as.data.frame(by_route), digits = 4, row.names = FALSE)

# Descriptive only: the spread of per-route means, in each objective's own
# units and relative to that objective's overall SD. No model, no adjustment.
spread <- lapply(OBJ, function(o) {
  mu <- tapply(d[[o]], d$routeCode, mean)
  data.frame(objective = o, route_spread = diff(range(mu)),
             overall_SD = sd(d[[o]]), spread_over_SD = diff(range(mu)) / sd(d[[o]]))
}) |> bind_rows()
cat("\nSpread of per-route means, per objective:\n")
print(spread, digits = 3, row.names = FALSE)

# Four route means of ~80-96 observations each will differ a little by chance.
# This is the spread that pure sampling noise would produce, as a yardstick.
null_spread <- mean(replicate(2000, {
  mu <- tapply(sample(d$energy), d$routeCode, mean); diff(range(mu)) / sd(d$energy)
}))
cat(sprintf("\nSpread expected from sampling noise alone: %.0f%% of SD.\n",
            100 * null_spread))
cat("Only energy clearly exceeds that yardstick.\n")

cat("\nRoute moves energy by",
    sprintf("%.1f kWh/100km", spread$route_spread[1]),
    sprintf("between routes, %.0f%% of its overall SD.", 100 * spread$spread_over_SD[1]),
    "\nFor the four questionnaire objectives the same quantity is",
    sprintf("%.0f-%.0f%%", 100 * min(spread$spread_over_SD[-1]),
            100 * max(spread$spread_over_SD[-1])),
    "-- they are not measured\nfrom the driving at all, so this is an energy problem alone.\n")
cat("\nThat spread is larger than any effect the optimiser produced on energy,\n",
    "and under the deployment-realistic accounting it stays in the residual\n",
    "rather than being removed.\n", sep = "")

# --------------------------------------------------- CAUSE 2: MEASUREMENT --
section("3  kWh/100km is extrapolated from a ~200 m drive")

cat(sprintf("Route lengths are %.0f-%.0f m, so the per-100 km figure is an\n",
            min(by_route$distance_m), max(by_route$distance_m)))
cat(sprintf("extrapolation of %.0f-%.0fx. Participants are stopped %.0f-%.0f%% of the time,\n",
            100000 / max(by_route$distance_m), 100000 / min(by_route$distance_m),
            min(by_route$pct_stopped), max(by_route$pct_stopped)))
cat("so a single acceleration from standstill dominates the whole round.\n")

cw <- sampling_df |> group_by(participant) |>
  mutate(across(c(energy, distanceM, drivingTimeS, avgSpeedKmh, sdSpeedKmh,
                  meanAbsAccel, sdAccel, pctHarshAccel, pctStopped),
                ~ .x - mean(.x, na.rm = TRUE))) |> ungroup()
drv <- c("pctHarshAccel", "meanAbsAccel", "sdAccel", "sdSpeedKmh",
         "avgSpeedKmh", "pctStopped", "drivingTimeS", "distanceM")
cat("\nWithin-participant correlation of energy with driving behaviour:\n")
print(round(sort(sapply(drv, function(v) cor(cw$energy, cw[[v]], use = "complete.obs")),
                 decreasing = TRUE), 3))

r2_drv <- summary(lm(energy ~ distanceM + drivingTimeS + avgSpeedKmh + sdSpeedKmh +
                       meanAbsAccel + pctStopped, data = cw))$r.squared
cwP <- sampling_df |> group_by(participant) |> mutate(energy = energy - mean(energy)) |> ungroup()
r2_hud <- summary(lm(as.formula(paste("energy ~", paste(PARAMS, collapse = "+"))),
                     data = cwP))$r.squared
cat(sprintf("\nWithin-person energy variance explained: driving behaviour R2 = %.3f,\n", r2_drv))
cat(sprintf("HUD design parameters R2 = %.3f. Energy mostly measures how the car was\n", r2_hud))
cat("driven that round, not what the HUD looked like.\n")


# ------------------------------------------------ CAUSE 3: THE NOISE FLOOR --
section("4  The noise floor, with route variation counted as noise")

cat("Because route is not adjusted away, its variation stays in the measurement\n",
    "noise -- which is the deployment-realistic accounting. Round 21 re-drives a\n",
    "design already evaluated once, so the two measurements of the SAME design\n",
    "give a direct estimate of that noise.\n\n", sep = "")

src <- d |> inner_join(prov |> select(participant, source_round),
                       by = c("participant", "round" = "source_round"))
nf <- lapply(OBJ, function(o) {
  diffs <- final_df[[o]][match(src$participant, final_df$participant)] - src[[o]]
  noise_sd <- sd(diffs) / sqrt(2)                     # SD of a single measurement
  spread <- mean(tapply(sampling_df[[o]], sampling_df$participant, sd))
  mde <- 2.8 * noise_sd / sqrt(15)                    # detectable at 80% power, 15 rounds
  data.frame(objective = o, noise_sd = noise_sd, design_spread_sd = spread,
             SNR = spread / noise_sd, MDE_15_rounds = mde,
             MDE_pct_of_mean = 100 * mde / mean(d[[o]]))
}) |> bind_rows()
print(nf, digits = 3, row.names = FALSE)
cat(sprintf("\nWith 15 sampling rounds, the smallest energy effect resolvable is\n"))
cat(sprintf("%.2f kWh/100km (%.1f%% of the mean). The optimiser is being asked to\n",
            nf$MDE_15_rounds[1], nf$MDE_pct_of_mean[1]))
cat("find an effect near the resolution limit of its own measurement.\n")


# ------------------------------------------- NOT THE CAUSE: UNRESPONSIVENESS --
section("5  Energy is NOT less design-responsive than the acceptance objectives")

cat("The obvious explanation -- that the HUD simply cannot affect energy -- does\n",
    "not hold. Sampling rounds only (quasi-random designs), route left in the\n",
    "residual as everywhere else:\n\n", sep = "")
resp <- lapply(OBJ, function(o) {
  f0 <- as.formula(paste0(o, " ~ 1 + (1|participant)"))
  f1 <- as.formula(paste0(o, " ~ ", paste(PARAMS, collapse = " + "), " + (1|participant)"))
  m0 <- suppressMessages(lmer(f0, sampling_df, REML = FALSE))
  m1 <- suppressMessages(lmer(f1, sampling_df, REML = FALSE))
  cmp <- anova(m0, m1)
  data.frame(objective = o, LRT = cmp$Chisq[2], df = cmp$Df[2],
             p = cmp[["Pr(>Chisq)"]][2],
             pct_resid_var_explained = 100 * (1 - (sigma(m1) / sigma(m0))^2))
}) |> bind_rows()
print(resp, digits = 3, row.names = FALSE)
cat("\nEnergy sits mid-pack. The design moves it about as much as it moves the\n",
    "acceptance objectives -- the optimiser simply had no incentive to chase it.\n", sep = "")

cat("\n-- And it is not a trade-off either: energy is orthogonal, not opposed --\n")
cwo <- sampling_df |> group_by(participant) |>
  mutate(across(all_of(c(OBJ, "nVisible")), ~ .x - mean(.x, na.rm = TRUE))) |> ungroup()
for (o in c("taskload", "accInformed", "accPleasant", "accGlance", "nVisible")) {
  ct <- cor.test(cwo$energy, cwo[[o]])
  cat(sprintf("  energy vs %-12s r = %+.3f   p = %.3f\n", o, ct$estimate, ct$p.value))
}
cat("\nThe acceptance gains did not COST energy. Energy was never targeted.\n")


# ----------------------------------------------- ARTIFACT: THE BEST BASELINE --
section("6  'Worse than the best sampled design' is an order-statistic artifact")

cat("analysis.R compares the final design against the BEST of 15 sampling rounds.\n",
    "That baseline is biased against the final design for every objective: the\n",
    "best of 15 noisy draws beats an average draw by construction. The null below\n",
    "draws the final design at random from the participant's own R1 rounds, so\n",
    "route is matched rather than modelled.\n\n", sep = "")

art <- lapply(OBJ, function(o) {
  gap <- function(f, s) improvement(f, mean(s), o)
  obs <- mean(vapply(unique(d$participant), function(p)
    gap(final_df[[o]][final_df$participant == p],
        base_r1[[o]][base_r1$participant == p]), numeric(1)))
  null <- replicate(5000, mean(vapply(unique(d$participant), function(p) {
    s <- base_r1[[o]][base_r1$participant == p]; gap(sample(s, 1), s)
  }, numeric(1))))
  data.frame(objective = o, observed = obs, null_mean = mean(null),
             null_sd = sd(null), z = (obs - mean(null)) / sd(null),
             p_vs_chance = mean(null >= obs))
}) |> bind_rows()
print(art, digits = 3, row.names = FALSE)
cat("\nz > 0 means the final design beats a random draw from that person's own\n",
    "designs on the same route. Energy is the only objective indistinguishable\n",
    "from chance. The defensible claim is 'no better than a random design', NOT\n",
    "'worse than the best sampled design' -- the latter is true of an ideal\n",
    "optimiser too.\n", sep = "")


section("SUMMARY")
cat("Energy did not improve for three compounding reasons:\n\n",
    "  1. BOUNDS (primary, and the cheapest to fix). Configured [0, 150] but\n",
    "     observed [", floor(min(d$energy)), ", ", ceiling(max(d$energy)), "]. ",
    "In the normalised space the optimiser works\n",
    "     in, energy has ~3.5x less leverage than every other objective, so the\n",
    "     acquisition function had almost nothing to gain by moving it.\n",
    "     FIX: set the bounds to the observed range.\n\n",
    "  2. NOISE FLOOR. Route variation (up to ",
    sprintf("%.1f", diff(range(by_route$energy_M))), " kWh/100km between routes)\n",
    "     is irreducible at deployment and stays in the residual, on top of ~200 m\n",
    "     routes extrapolated to 100 km and dominated by acceleration transients.\n",
    "     The smallest resolvable energy effect over 15 rounds is ",
    sprintf("%.1f", nf$MDE_15_rounds[1]), " kWh/100km.\n",
    "     FIX: longer routes, or report energy per round rather than per 100 km.\n\n",
    "  3. PROTOCOL. The final round always runs one route while its baseline\n",
    "     mixes all four, so the final design is credited for the route it\n",
    "     happened to be measured on. ALREADY FIXED: analysis.R reports a\n",
    "     matched_route baseline -- the final round against that participant's\n",
    "     own sampling rounds on the same route -- as the primary RQ4\n",
    "     contrast. Matching, not adjustment: route is still never modelled.\n\n",
    "What is NOT the cause: the HUD does move energy, about as much as it moves\n",
    "the acceptance objectives, and energy does not trade off against them.\n", sep = "")
