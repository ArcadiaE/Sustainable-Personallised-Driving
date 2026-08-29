# =============================================================================
#  build_master.py — 把 ParticipantData/P* 归档合成一张分析就绪的长表
#  Sustainable Personalised Driving study (UCL)
#
#  运行（在 ParticipantData\analysis\ 下，任何带 pandas 的 python 均可）：
#      python build_master.py
#  输出：
#      master_rounds.csv   每行 = 一位参与者的一轮（tidy long format，可直接进 R/pandas）
#
#  归档新参与者后重跑一次即可把新数据并进来（脚本自动扫描 ../P0*）。
#
#  合并逻辑与数据修正：
#   - Unity 侧 unity/rounds.csv 与 BO 侧 bo/ObservationsPerEvaluation.csv 按
#     round == Iteration 连接（两边行数在归档时已校验一致）。
#   - phase / isPareto 只存在于 BO 侧 —— rounds.csv 分不出 sampling/optimization/
#     finaldesign，统计必须要这列（finaldesign 轮是重复驾驶，大多数分析要排除）。
#   - rounds.csv 的 durationS 含问卷作答时间，NOT 驾驶时长（见 config/study_config.md）。
#     真实驾驶时长 driveDurationS 由 averages_roundNN.csv 的数据行数得出（1 Hz，1 行=1 秒）。
#   - p0..p6 是标准化 [0,1] 提案值；另给出物理量列（*_phys）：
#         sizes: x*1.3 ；size_speed: 0.6+x*0.7 ；opacity: 0.10+0.90*x
#     映射常量来自 EcoFeedbackHUD.ApplyDesignParams（各归档 config/src/ 有快照）。
#   - participant 取归档文件夹名（P01..），user_id 取 BO 侧原值（P01 是 -1，历史原因）。
# =============================================================================
import glob
import os
import sys

import pandas as pd

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

PARAM_NAMES = ["size_leaf", "size_score", "size_feedback", "size_speed",
               "size_accel", "size_labels", "opacity"]
SIZE_MAX, SPEED_MIN, OPACITY_MIN = 1.3, 0.6, 0.10


def phys(name, x):
    if name == "size_speed":
        return SPEED_MIN + x * (SIZE_MAX - SPEED_MIN)
    if name == "opacity":
        return OPACITY_MIN + x * (1.0 - OPACITY_MIN)
    return x * SIZE_MAX


def load_participant(pdir):
    pid = os.path.basename(pdir)
    rounds = pd.read_csv(os.path.join(pdir, "unity", "rounds.csv"), sep=";")
    obs = pd.read_csv(os.path.join(pdir, "bo", "ObservationsPerEvaluation.csv"), sep=";")

    if len(rounds) != len(obs):
        sys.exit(f"{pid}: rounds.csv {len(rounds)} 行 != ObservationsPerEvaluation {len(obs)} 行 — 先人工核对再合并。")

    # BO 侧携带的列（round 号即 Iteration）
    obs = obs.rename(columns={"Iteration": "round"})
    bo_part = obs[["round", "Phase", "IsPareto", "UserID", "ConditionID", "GroupID"]].rename(
        columns={"Phase": "phase", "IsPareto": "isPareto",
                 "UserID": "user_id", "ConditionID": "condition", "GroupID": "group"})
    df = rounds.merge(bo_part, on="round", validate="1:1")

    # 交叉校验：两侧独立记录的 7 个参数应当一致（容忍打印精度差）
    for i, name in enumerate(PARAM_NAMES):
        bad = (df[f"p{i}"] - obs[name]).abs().max()
        if bad > 0.005:
            sys.exit(f"{pid}: p{i} 与 BO 侧 {name} 最大偏差 {bad:.4f} — 两侧数据可能未对齐。")

    # 真实驾驶时长：averages 文件的数据行数（1 Hz）
    dur = {}
    for f in glob.glob(os.path.join(pdir, "unity", "averages_round*.csv")):
        rnd = int(os.path.basename(f)[len("averages_round"):-len(".csv")])
        with open(f, encoding="utf-8-sig") as fh:
            dur[rnd] = sum(1 for _ in fh) - 1
    df["driveDurationS"] = df["round"].map(dur)

    # 物理量列
    for i, name in enumerate(PARAM_NAMES):
        df[f"{name}_phys"] = df[f"p{i}"].map(lambda x, n=name: round(phys(n, x), 4))

    # HUD 显隐（EcoFeedbackHUD：物理尺寸 <= HideEps=0.6 的元素整体隐藏；速度永不隐藏。
    # feedback 同时控制建议语和表情图标；labels 控制其他车的叶子标记。）
    HIDE_EPS = 0.6
    for name in ["size_leaf", "size_score", "size_feedback", "size_accel", "size_labels"]:
        df[f"visible_{name[5:]}"] = df[f"{name}_phys"] > HIDE_EPS
    # 速度读数的实际 alpha 有 0.4 地板（法规可读性）
    df["speed_alpha"] = df["opacity_phys"].map(lambda a: round(max(a, 0.4), 4))

    df.insert(0, "participant", pid)
    # apparatus condition (2026-08-22 chair failure): participants 1-8 ran with the
    # motion seat working normally (experimenter-confirmed); from participant 9 the
    # study continues without motion (desk-mounted controls).
    df.insert(1, "setup", "motion" if int(pid[1:]) <= 8 else "desk")

    # 每个归档文件夹内落一份逐轮 HUD 状态表（用户 2026-08-22：HUD 记录也要归档）
    hud_cols = (["participant", "round"]
                + [f"p{i}" for i in range(len(PARAM_NAMES))]
                + [f"{n}_phys" for n in PARAM_NAMES]
                + [f"visible_{n[5:]}" for n in ["size_leaf", "size_score", "size_feedback",
                                                "size_accel", "size_labels"]]
                + ["speed_alpha"])
    hud = df[hud_cols].rename(columns={f"p{i}": f"{n}_norm" for i, n in enumerate(PARAM_NAMES)})
    hud.to_csv(os.path.join(pdir, "hud_design_per_round.csv"), index=False)
    return df


def main():
    pdirs = sorted(d for d in glob.glob(os.path.join(ROOT, "P[0-9][0-9]")) if os.path.isdir(d))
    if not pdirs:
        sys.exit("找不到任何 P0x 归档文件夹。")
    master = pd.concat([load_participant(d) for d in pdirs], ignore_index=True)

    # durationS 容易被误用，改名自明；p0..p6 换成有含义的列名
    master = master.rename(columns={"durationS": "durationInclSurveyS"})
    master = master.rename(columns={f"p{i}": f"{n}_norm" for i, n in enumerate(PARAM_NAMES)})

    front = ["participant", "setup", "user_id", "condition", "group", "round", "phase", "isPareto",
             "route", "endedAt", "driveDurationS", "durationInclSurveyS"]
    master = master[front + [c for c in master.columns if c not in front]]

    # 2026-08-25（用户要求）：动感组与静态组分成两张独立统计表
    for cond, fname in [("motion", "master_rounds_motion.csv"),
                        ("desk",   "master_rounds_static.csv")]:
        part = master[master["setup"] == cond]
        if len(part) == 0:
            continue
        out = os.path.join(HERE, fname)
        part.to_csv(out, index=False)   # 逗号分隔、UTF-8 —— R/SPSS/pandas 默认即读
        print(f"OK: {out}  ({part['participant'].nunique()} participants, {len(part)} rows)")
    print(f"    rows={len(master)} = " + " + ".join(
        f"{p}:{(master.participant == os.path.basename(d)).sum()}"
        for d, p in ((d, os.path.basename(d)) for d in pdirs)))
    print("    phase:", dict(master.phase.value_counts()))


if __name__ == "__main__":
    main()
