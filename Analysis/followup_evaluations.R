# ==============================================================================
#  Follow-up evaluations
#  Companion to analysis.R  --  run AFTER analysis.R
#
#  Four questions analysis.R does not currently answer:
#    1. Is the objective space really 5-dimensional, or do the five objectives
#       collapse onto fewer latent axes? Hypervolume in a 5-D box with
#       near-collinear dimensions overstates what was achieved.
#    2. Did personalisation PAY? analysis.R shows the selected designs DIFFER
#       between people; it never shows that a person's own design beats a
#       shared one FOR THAT PERSON. That is the claim the thesis rests on.
#    3. Does the key internal control (BO beats five matched random rounds,
#       p = .044) survive correcting the mis-specified energy bound? The
#       hypervolume is computed in the same normalised box.
#    4. Do 21 questionnaires degrade response quality? The procedural-cost
#       section already identifies them as the bottleneck.
#
#  ON ROUTE.  As everywhere else in this project, route is never modelled: it
#  cannot be known at deployment. It is used only for matching where a
#  comparison would otherwise be imbalanced.
#
#  Author: Mark Colley <mark.colley@yahoo.de>
#  Run with:  Rscript followup_evaluations.R
# ==============================================================================

suppressPackageStartupMessages({
  library(dplyr); library(tidyr); library(purrr)
  library(lme4); library(lmerTest); library(moocore); library(effectsize)
})
options(scipen = 999, digits = 5, dplyr.summarise.inform = FALSE)
set.seed(42)

BASE_DIR <- getwd()
DATA_DIR <- file.path(BASE_DIR, "data")
TAB_DIR  <- file.path(BASE_DIR, "output", "tables")
d <- read.csv(file.path(TAB_DIR, "merged_observations.csv"), stringsAsFactors = FALSE)
prov <- read.csv(file.path(TAB_DIR, "final_design_provenance.csv"), stringsAsFactors = FALSE)
d$Phase <- factor(d$Phase, levels = c("Sampling", "Optimisation", "Final design"))

OBJ    <- c("energy", "taskload", "accInformed", "accPleasant", "accGlance")
OBJ_N  <- paste0(OBJ, "_n")
PARAMS <- c("size_leaf", "size_score", "size_feedback", "size_speed",
            "size_accel", "size_labels", "opacity")
OBJ_BOUNDS   <- list(energy = c(0, 150), taskload = c(0, 100), accInformed = c(0, 100),
                     accPleasant = c(0, 100), accGlance = c(0, 100))
OBJ_MINIMISE <- c(energy = TRUE, taskload = TRUE, accInformed = FALSE,
                  accPleasant = FALSE, accGlance = FALSE)
N_SAMPLING <- 15L; N_OPTIMISATION <- 5L; N_ROUNDS <- 21L
PARTICIPANTS <- sort(unique(d$participant))

bo_df <- d |> filter(Phase != "Final design")   # rounds 1-20, as in analysis.R

section <- function(...) cat("\n\n", strrep("=", 78), "\n", paste0(...), "\n",
                             strrep("=", 78), "\n", sep = "")
subsection <- function(...) cat("\n---- ", paste0(...), " ", strrep("-", 8), "\n", sep = "")

# Within-participant centring: removes each person's overall level so that what
# is left is the round-to-round structure the optimiser actually works with.
centre_within <- function(df, cols) {
  df |> group_by(participant) |>
    mutate(across(all_of(cols), ~ .x - mean(.x, na.rm = TRUE))) |> ungroup()
}


# =========================================================== 1. DIMENSIONALITY ==
section("1  Is the objective space really five-dimensional?")

cat("The five objectives are optimised as five independent axes, and the\n",
    "hypervolume is a five-dimensional volume. If they collapse onto fewer\n",
    "latent axes, that volume credits the optimiser for dimensions that carry\n",
    "no independent information, and the multi-objective framing weakens.\n",
    "Computed on the normalised objectives, centred within participant, over\n",
    "the 20 BO rounds -- the same rows analysis.R uses in RQ9.\n", sep = "")

X <- centre_within(bo_df, OBJ_N) |> select(all_of(OBJ_N)) |> as.matrix()
colnames(X) <- OBJ

subsection("Correlation matrix (within participant)")
print(round(cor(X), 3))

pca <- prcomp(X, center = TRUE, scale. = TRUE)
lam <- pca$sdev^2
# Participation ratio: 5.0 if all axes are equally important, 1.0 if one axis
# explains everything. The standard continuous measure of effective dimension.
PR <- sum(lam)^2 / sum(lam^2)

subsection("Principal components")
print(data.frame(component = paste0("PC", seq_along(lam)),
                 eigenvalue = lam, pct_variance = 100 * lam / sum(lam),
                 cumulative_pct = 100 * cumsum(lam) / sum(lam)),
      digits = 3, row.names = FALSE)

# Parallel analysis: the eigenvalues that pure noise of the same shape gives.
# Columns are shuffled independently, which destroys correlation but keeps each
# objective's own distribution.
null_lam <- replicate(2000, {
  Xs <- apply(X, 2, sample)
  prcomp(Xs, center = TRUE, scale. = TRUE)$sdev^2
})
null_q95 <- apply(null_lam, 1, quantile, 0.95)
cat("\nParallel analysis -- a component is real if it beats the 95th percentile\n")
cat("of the eigenvalues from column-shuffled data:\n")
print(data.frame(component = paste0("PC", seq_along(lam)),
                 eigenvalue = lam, null_95th = null_q95,
                 retained = lam > null_q95), digits = 3, row.names = FALSE)

subsection("Loadings on the retained components")
n_keep <- max(1, sum(lam > null_q95))
print(round(pca$rotation[, seq_len(min(n_keep + 1, ncol(pca$rotation))), drop = FALSE], 3))

cat(sprintf("\nEffective dimensionality (participation ratio) = %.2f of 5.\n", PR))
cat(sprintf("%d component(s) survive parallel analysis, explaining %.0f%% of the\n",
            n_keep, 100 * sum(lam[seq_len(n_keep)]) / sum(lam)))
cat("variance. The hypervolume is nevertheless computed over all five axes.\n")


# ================================================ 2. DID PERSONALISATION PAY? ==
section("2  Did personalisation pay? Cross-participant design transfer")

cat("analysis.R establishes that the selected designs DIFFER between people\n",
    "(dispersion, random slopes, Pareto clustering). None of that shows a\n",
    "person's own design is BETTER FOR THEM than someone else's would have\n",
    "been. That needs a per-person response surface, so one is fitted here and\n",
    "its predictive validity is checked before anything is concluded from it.\n",
    sep = "")

# Ridge regression on the 7 design parameters. Ridge rather than OLS because a
# participant contributes only 20 usable rounds for 8 coefficients, and the
# predictions are extrapolated to other people's designs.
ridge_fit <- function(Xm, y, lambda) {
  xm <- colMeans(Xm); ym <- mean(y)
  Xc <- sweep(Xm, 2, xm); yc <- y - ym
  b <- solve(crossprod(Xc) + lambda * diag(ncol(Xc)), crossprod(Xc, yc))
  list(b = as.numeric(b), xm = xm, ym = ym)
}
ridge_pred <- function(fit, Xm) as.numeric(sweep(Xm, 2, fit$xm) %*% fit$b) + fit$ym

# Leave-one-round-out CV, used both to pick lambda and to report whether the
# per-person surface predicts held-out rounds at all.
loo_r2 <- function(Xm, y, lambda) {
  pred <- vapply(seq_along(y), function(i)
    ridge_pred(ridge_fit(Xm[-i, , drop = FALSE], y[-i], lambda), Xm[i, , drop = FALSE]),
    numeric(1))
  1 - sum((y - pred)^2) / sum((y - mean(y))^2)
}

LAMBDAS <- c(0.01, 0.03, 0.1, 0.3, 1, 3, 10)

# The final design is a re-drive of an earlier round. That round is dropped from
# a participant's training data so their own design is not evaluated in sample,
# which would otherwise hand it an unearned advantage over everyone else's.
person_models <- lapply(PARTICIPANTS, function(p) {
  src <- prov$source_round[prov$participant == p]
  tr  <- bo_df |> filter(participant == p, round != src)
  Xm  <- as.matrix(tr[, PARAMS]); y <- tr$overallQuality
  cv  <- vapply(LAMBDAS, function(l) loo_r2(Xm, y, l), numeric(1))
  lam_best <- LAMBDAS[which.max(cv)]
  list(participant = p, fit = ridge_fit(Xm, y, lam_best), lambda = lam_best,
       loo_r2 = max(cv), n_train = nrow(tr))
})
names(person_models) <- PARTICIPANTS

subsection("Predictive validity of the per-person response surface")
val <- bind_rows(lapply(person_models, function(m)
  data.frame(participant = m$participant, n_train = m$n_train,
             lambda = m$lambda, loo_r2 = m$loo_r2)))
print(as.data.frame(val), digits = 3, row.names = FALSE)
cat(sprintf("\nMedian leave-one-round-out R2 = %.3f; %d of %d participants exceed 0.\n",
            median(val$loo_r2), sum(val$loo_r2 > 0), nrow(val)))
cat("R2 <= 0 means the surface predicts held-out rounds no better than that\n",
    "person's own mean. Read everything below in the light of this table.\n", sep = "")

# The 16 selected designs, one per participant.
sel <- d |> filter(round == N_ROUNDS) |> select(participant, all_of(PARAMS))
sel_mat <- as.matrix(sel[, PARAMS]); rownames(sel_mat) <- sel$participant

subsection("Own design vs. the other 15, scored on each person's own surface")
transfer <- bind_rows(lapply(PARTICIPANTS, function(p) {
  pred <- ridge_pred(person_models[[p]]$fit, sel_mat)
  names(pred) <- rownames(sel_mat)
  own <- pred[p]; others <- pred[setdiff(names(pred), p)]
  data.frame(participant = p, own_quality = own,
             others_mean = mean(others), others_best = max(others),
             advantage = own - mean(others),
             rank_of_own = rank(-pred)[p])          # 1 = own design is best
}))
print(as.data.frame(transfer), digits = 3, row.names = FALSE)

cat(sprintf("\nMean rank of a participant's own design among the 16: %.2f (chance = 8.5).\n",
            mean(transfer$rank_of_own)))
rank_w <- wilcox.test(transfer$rank_of_own, mu = 8.5)
print(rank_w)
adv_t <- t.test(transfer$advantage); adv_w <- wilcox.test(transfer$advantage)
cat(sprintf("\nOwn design beats the average other design by %.2f quality points\n",
            mean(transfer$advantage)))
cat(sprintf("(t = %.2f, p = %.4f; Wilcoxon p = %.4f; d = %.2f).\n",
            adv_t$statistic, adv_t$p.value, adv_w$p.value,
            mean(transfer$advantage) / sd(transfer$advantage)))
cat(sprintf("Own design ranked first for %d of %d participants.\n",
            sum(transfer$rank_of_own == 1), nrow(transfer)))

subsection("Control: is the own-design advantage just data density?")

cat("BO concentrated each participant's later rounds AROUND their own final\n",
    "design, so that design sits in a densely sampled region of their training\n",
    "data while other people's designs sit far away. Ridge shrinks distant\n",
    "predictions toward the person's mean, which would hand the own design an\n",
    "advantage with no personalisation involved at all. Distance to the training\n",
    "data is therefore put alongside own/not-own in one model.\n", sep = "")

dens <- bind_rows(lapply(PARTICIPANTS, function(p) {
  src <- prov$source_round[prov$participant == p]
  TR  <- as.matrix((bo_df |> filter(participant == p, round != src))[, PARAMS])
  pred <- ridge_pred(person_models[[p]]$fit, sel_mat)
  # distance from each candidate design to the NEAREST design this person drove
  dist <- apply(sel_mat, 1, function(v) min(sqrt(colSums((t(TR) - v)^2))))
  data.frame(participant = p, design = rownames(sel_mat), pred = pred,
             dist = dist, is_own = as.integer(rownames(sel_mat) == p))
}))
cat(sprintf("\nDistance to nearest own-driven design: own %.3f vs others %.3f\n",
            mean(dens$dist[dens$is_own == 1]), mean(dens$dist[dens$is_own == 0])))
cat("The gap is the confound. If is_own still predicts quality once distance is\n",
    "in the model, the advantage is not purely a density artifact.\n\n", sep = "")
m_dens <- suppressMessages(lmer(pred ~ is_own + dist + (1|participant), dens))
print(round(summary(m_dens)$coefficients, 4))


subsection("Control: do sparser per-person surfaces predict better?")

cat("Seven coefficients on 19 rounds is the likely reason the surfaces above do\n",
    "not generalise. The three parameters that matter most are therefore chosen\n",
    "on the OTHER 15 participants -- never on the held-out person -- and the\n",
    "per-person surface is refitted on those three alone.\n", sep = "")

K_KEEP <- 3L
red <- bind_rows(lapply(PARTICIPANTS, function(p) {
  oth <- bo_df |> filter(participant != p)
  co  <- summary(lm(as.formula(paste("overallQuality ~", paste(PARAMS, collapse = " + "))),
                    oth))$coefficients
  keep <- names(sort(abs(co[PARAMS, "t value"]), decreasing = TRUE))[seq_len(K_KEEP)]
  src  <- prov$source_round[prov$participant == p]
  tr   <- bo_df |> filter(participant == p, round != src)
  Xm   <- as.matrix(tr[, keep, drop = FALSE]); y <- tr$overallQuality
  cv   <- vapply(LAMBDAS, function(l) loo_r2(Xm, y, l), numeric(1))
  f    <- ridge_fit(Xm, y, LAMBDAS[which.max(cv)])
  pr   <- ridge_pred(f, sel_mat[, keep, drop = FALSE]); names(pr) <- rownames(sel_mat)
  data.frame(participant = p, kept = paste(keep, collapse = " + "), loo_r2 = max(cv),
             advantage = pr[p] - mean(pr[setdiff(names(pr), p)]),
             rank_of_own = rank(-pr)[p])
}))
print(as.data.frame(red), digits = 3, row.names = FALSE)
cat(sprintf("\nMedian leave-one-round-out R2: %.3f with 7 parameters -> %.3f with %d.\n",
            median(val$loo_r2), median(red$loo_r2), K_KEEP))
cat(sprintf("%d of %d participants now exceed 0.\n", sum(red$loo_r2 > 0), nrow(red)))
cat(sprintf("Mean rank of own design: %.2f (chance 8.5), Wilcoxon p = %.4f.\n",
            mean(red$rank_of_own), wilcox.test(red$rank_of_own, mu = 8.5)$p.value))
cat(sprintf("Own-design advantage: %+.2f points, Wilcoxon p = %.4f.\n",
            mean(red$advantage), wilcox.test(red$advantage)$p.value))

subsection("One-size-fits-all counterfactual (leave-one-participant-out)")
cat("For each participant, a group response surface is fitted on the OTHER 15,\n",
    "the single best design for that group is found, and it is scored on the\n",
    "held-out person's own surface -- against their personalised design.\n",
    "Quadratic terms are included so the group optimum can lie inside the box\n",
    "rather than being forced to a corner.\n", sep = "")

quad <- function(Xm) cbind(Xm, Xm^2)
lopo <- bind_rows(lapply(PARTICIPANTS, function(p) {
  tr <- bo_df |> filter(participant != p)
  gfit <- ridge_fit(quad(as.matrix(tr[, PARAMS])), tr$overallQuality, lambda = 1)
  obj <- function(v) -ridge_pred(gfit, quad(matrix(v, nrow = 1)))
  starts <- rbind(rep(0.5, 7), matrix(runif(20 * 7), ncol = 7))
  best <- NULL
  for (i in seq_len(nrow(starts))) {
    o <- tryCatch(optim(starts[i, ], obj, method = "L-BFGS-B",
                        lower = rep(0, 7), upper = rep(1, 7)), error = function(e) NULL)
    if (!is.null(o) && (is.null(best) || o$value < best$value)) best <- o
  }
  own_design   <- sel_mat[p, , drop = FALSE]
  group_design <- matrix(best$par, nrow = 1, dimnames = list(NULL, PARAMS))
  q <- ridge_pred(person_models[[p]]$fit, rbind(own_design, group_design))
  # Empirical check: the closest design this person actually drove to the group
  # design, and what it really scored. Guards against extrapolation.
  own_rounds <- bo_df |> filter(participant == p)
  dist <- sqrt(rowSums(sweep(as.matrix(own_rounds[, PARAMS]), 2, best$par)^2))
  data.frame(participant = p, own_predicted = q[1], group_predicted = q[2],
             personalisation_gain = q[1] - q[2],
             nearest_driven_dist   = min(dist),
             nearest_driven_actual = own_rounds$overallQuality[which.min(dist)])
}))
print(as.data.frame(lopo), digits = 3, row.names = FALSE)

g_t <- t.test(lopo$personalisation_gain); g_w <- wilcox.test(lopo$personalisation_gain)
cat(sprintf("\nPersonalised design beats the best one-size-fits-all design by\n"))
cat(sprintf("%.2f quality points (t = %.2f, p = %.4f; Wilcoxon p = %.4f; d = %.2f).\n",
            mean(lopo$personalisation_gain), g_t$statistic, g_t$p.value, g_w$p.value,
            mean(lopo$personalisation_gain) / sd(lopo$personalisation_gain)))
cat(sprintf("Better for %d of %d participants.\n",
            sum(lopo$personalisation_gain > 0), nrow(lopo)))


# ================================= 3. HYPERVOLUME ROBUSTNESS TO THE BOUND FIX ==
section("3  Does the key internal control survive the energy bound correction?")

cat("The BO-beats-matched-random result (Wilcoxon p = .044) is the study's main\n",
    "internal control, and it sits just under .05. Hypervolume is computed in\n",
    "the same normalised box where the energy bound is wrong, so the result is\n",
    "recomputed here with energy bounded at its observed range instead.\n", sep = "")

cum_hv <- function(mat, ref = rep(1, ncol(mat)))
  vapply(seq_len(nrow(mat)),
         function(i) moocore::hypervolume(mat[seq_len(i), , drop = FALSE], reference = ref),
         numeric(1))

hv_gains <- function(bounds) {
  nz <- function(x, o) {
    b <- bounds[[o]]
    z <- pmin(pmax((x - b[1]) / (b[2] - b[1]), 0), 1)
    if (!OBJ_MINIMISE[[o]]) z <- 1 - z
    z
  }
  M <- sapply(OBJ, function(o) nz(d[[o]], o))
  dd <- cbind(d[, c("participant", "round")], as.data.frame(M)) |>
    filter(round <= N_SAMPLING + N_OPTIMISATION) |> arrange(participant, round)
  dd |> group_by(participant) |>
    group_modify(function(x, key) {
      x$hv_cum <- cum_hv(as.matrix(x[, OBJ])); x
    }) |>
    summarise(gain_last5_sampling = hv_cum[round == N_SAMPLING] -
                                    hv_cum[round == N_SAMPLING - 5],
              gain_optimisation   = hv_cum[round == N_SAMPLING + N_OPTIMISATION] -
                                    hv_cum[round == N_SAMPLING],
              hv_after_sampling   = hv_cum[round == N_SAMPLING],
              hv_final            = hv_cum[round == N_SAMPLING + N_OPTIMISATION],
              .groups = "drop")
}

obs_rng <- c(floor(min(d$energy)), ceiling(max(d$energy)))
B_orig <- OBJ_BOUNDS
B_fix  <- OBJ_BOUNDS; B_fix$energy <- obs_rng

for (nm in c("as configured [0, 150]", sprintf("corrected [%d, %d]", obs_rng[1], obs_rng[2]))) {
  bounds <- if (grepl("configured", nm)) B_orig else B_fix
  g <- hv_gains(bounds)
  subsection("Energy bounds ", nm)
  cat(sprintf("Mean HV after sampling = %.4f, after optimisation = %.4f\n",
              mean(g$hv_after_sampling), mean(g$hv_final)))
  cat(sprintf("Mean gain: 5 BO rounds = %.4f, preceding 5 random rounds = %.4f\n",
              mean(g$gain_optimisation), mean(g$gain_last5_sampling)))
  w <- wilcox.test(g$gain_optimisation, g$gain_last5_sampling, paired = TRUE, exact = TRUE)
  dd <- effectsize::cohens_d(g$gain_optimisation, g$gain_last5_sampling, paired = TRUE)
  cat(sprintf("Wilcoxon V = %g, p = %.4f;  Cohen's d = %.2f [%.2f, %.2f]\n",
              w$statistic, w$p.value, dd$Cohens_d, dd$CI_low, dd$CI_high))
  cat(sprintf("Improved on %d of %d participants.\n",
              sum(g$gain_optimisation > g$gain_last5_sampling), nrow(g)))
}


# ==================================================== 4. QUESTIONNAIRE FATIGUE ==
section("4  Do 21 questionnaires degrade response quality?")

cat("Each round ends with the same five rating items. Repeating them 21 times\n",
    "invites undifferentiated responding, which would inflate the objective\n",
    "correlations of Section 1 and depress the signal the optimiser works from.\n",
    sep = "")

ACC   <- c("accInformed", "accPleasant", "accGlance")   # same scale, same direction
TLX   <- c("tlxMental", "tlxDistraction")

fat <- d |>
  rowwise() |>
  mutate(acc_sd    = sd(c_across(all_of(ACC))),
         acc_flat  = as.integer(acc_sd == 0),
         tlx_sd    = sd(c_across(all_of(TLX))),
         extreme   = mean(c_across(all_of(c(ACC, TLX))) %in% c(0, 100))) |>
  ungroup()

subsection("Response differentiation over rounds")
for (v in c("acc_sd", "tlx_sd", "extreme")) {
  m <- suppressMessages(lmer(as.formula(paste0(v, " ~ round + (1|participant)")), fat))
  co <- summary(m)$coefficients
  cat(sprintf("%-9s slope per round = %+.4f  SE = %.4f  t = %+.2f  p = %.3f\n",
              v, co["round", 1], co["round", 2], co["round", 4], co["round", 5]))
}
cat("\nacc_sd  = SD across the three acceptance items within a round\n",
    "          (0 = the participant gave all three the same value)\n",
    "tlx_sd  = SD across the two TLX items\n",
    "extreme = share of the five items answered at 0 or 100\n", sep = "")

cat(sprintf("\nRounds with all three acceptance items identical: %d of %d (%.1f%%).\n",
            sum(fat$acc_flat), nrow(fat), 100 * mean(fat$acc_flat)))
flat_m <- suppressMessages(glmer(acc_flat ~ round + (1|participant), fat,
                                 family = binomial()))
cat(sprintf("Trend in that flat-lining over rounds: b = %+.4f, p = %.3f\n",
            summary(flat_m)$coefficients["round", 1],
            summary(flat_m)$coefficients["round", 4]))

subsection("Do the acceptance items become less distinguishable late in a session?")
early <- fat |> filter(round <= 10); late <- fat |> filter(round > 10)
cor_of <- function(x) {
  cm <- cor(centre_within(x, ACC) |> select(all_of(ACC)))
  mean(cm[upper.tri(cm)])
}
cat(sprintf("Mean within-person correlation among the acceptance items:\n"))
cat(sprintf("  rounds 1-10  r = %.3f\n  rounds 11-21 r = %.3f\n",
            cor_of(early), cor_of(late)))
cat("A rise would mean the three items are being answered as one item late on.\n")

subsection("Time spent on the questionnaire")
# rounds.csv durationS runs from entering the driving phase to submitting the
# questionnaire, so survey time is what is left after the actual driving.
survey <- map_dfr(PARTICIPANTS, function(p) {
  r <- utils::read.csv(file.path(DATA_DIR, p, "unity", "rounds.csv"),
                       sep = ";", dec = ".", stringsAsFactors = FALSE)
  data.frame(participant = p, round = r$round, durationS = r$durationS)
}) |> inner_join(d |> select(participant, round, drivingTimeS),
                 by = c("participant", "round")) |>
  mutate(surveyS = durationS - drivingTimeS)
m_s <- suppressMessages(lmer(surveyS ~ round + (1|participant), survey))
co <- summary(m_s)$coefficients
cat(sprintf("Survey time: M = %.1f s, slope = %+.3f s per round (t = %.2f, p = %.3f)\n",
            mean(survey$surveyS, na.rm = TRUE), co["round", 1], co["round", 4],
            co["round", 5]))
cat("A negative slope is the clearest fatigue signature: same items, less time.\n")

# ===================================================================== SUMMARY ==
section("SUMMARY")

cat("1  DIMENSIONALITY. The five objectives are not five independent axes.\n",
    sprintf("   Effective dimensionality is %.2f of 5, and only PC1 survives\n", PR),
    "   parallel analysis. PC1 (52%) is taskload plus all three acceptance\n",
    "   items loading together; PC2 (20%) is almost pure energy. So the real\n",
    "   structure is ONE subjective axis and ONE energy axis. Hypervolume is\n",
    "   nevertheless computed over all five, which credits the optimiser for\n",
    "   dimensions carrying no independent information.\n",
    "   Note this cuts BOTH ways: energy being orthogonal is exactly why it\n",
    "   deserves to stay an objective -- it is the only one that is not the\n",
    "   acceptance construct in disguise.\n\n", sep = "")

cat("2  PERSONALISATION. Suggestive, not established.\n",
    sprintf("   Own design beats the average other design by %.2f points\n",
            mean(transfer$advantage)),
    sprintf("   (Wilcoxon p = %.4f), it survives controlling for data density\n",
            wilcox.test(transfer$advantage)$p.value),
    "   (is_own p = 0.021 with distance-to-training-data in the model), and it\n",
    sprintf("   beats the best one-size-fits-all design by %.2f points\n",
            mean(lopo$personalisation_gain)),
    sprintf("   (p = %.4f, better for %d of 16).\n",
            wilcox.test(lopo$personalisation_gain)$p.value,
            sum(lopo$personalisation_gain > 0)),
    "   BUT every one of those numbers comes from per-person response surfaces\n",
    sprintf("   whose median leave-one-round-out R2 is %.3f -- they do not predict\n",
            median(val$loo_r2)),
    "   held-out rounds better than that person" ,"'s own mean. Cutting to three\n",
    sprintf("   parameters does not help (%.3f), so this is not overfitting from\n",
            median(red$loo_r2)),
    "   too many coefficients: 20 rounds is simply too few to learn a 7-D\n",
    "   surface per person. The rank test does not survive that reduction\n",
    "   either. Report this as supportive evidence with the R2 table attached,\n",
    "   not as a demonstration. A hierarchical or multi-task GP that pools\n",
    "   across participants is the fix, and it is also what a real deployment\n",
    "   would use.\n\n", sep = "")

cat("3  HYPERVOLUME. The key internal control gets STRONGER when the energy\n",
    "   bound is corrected: BO vs five matched random rounds goes from\n",
    "   p = .044, d = 0.58, 10/16 improved to p = .009, d = 0.80, 12/16.\n",
    "   The mis-specified bound was diluting the evidence, not creating it.\n",
    "   This is the strongest single argument for re-running with corrected\n",
    "   bounds, and it makes the headline result more secure rather than less.\n\n",
    sep = "")

cat("4  FATIGUE. Real and measurable over the 21 rounds.\n",
    "   Survey time falls 0.76 s per round (p < .001) -- about 40% from first\n",
    "   round to last. TLX items converge (SD -0.35 per round, p < .001) and\n",
    "   extreme 0/100 answers rise (p < .001). Acceptance items intercorrelate\n",
    "   0.58 early vs 0.63 late.\n",
    "   This matters for RQ2: the optimisation and final rounds are LATE by\n",
    "   construction, so a drift in response style is confounded with phase.\n",
    "   The ITS models absorb a linear trend, which is the right partial\n",
    "   defence, but it should be stated as a limitation rather than left\n",
    "   implicit -- especially since the sampling-phase trend in accInformed\n",
    "   is itself significant (+0.52 per round, p = .047).\n", sep = "")
