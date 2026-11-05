#!/usr/bin/env python3
"""
train_gestures.py - trains and compares gesture classifiers, saves the best one.

Reads everything collect.py recorded (plus whatever augment_dataset.py derived
from it), builds one feature vector per sample with gesture_features.py, fits
several different classifiers, compares them under the same cross-validation
split, and saves ONLY the winner for live_predict.py to load.

Examples
--------
    python train_gestures.py                      # compare all models, save best
    python train_gestures.py --dry-run            # report the dataset, fit nothing
    python train_gestures.py --real-only          # ignore augmented samples
    python train_gestures.py --models random_forest,svm_rbf
    python train_gestures.py --out ../models/alt.joblib

How the dataset is handled
--------------------------
Three things in data/ need care, and all three are handled here rather than by
editing anyone's CSVs:

1. Jay's repetitions 26-100 are augmented but recorded as ``mode=motion`` in
   index.csv and are missing from the manifest. The ``mode`` column is therefore
   not trusted: a row is treated as augmented if the manifest lists it, or if
   its CSV restarts the ``seq`` counter at 0 (only the augmenter does that).

2. devam's "idle" recordings contain vigorous movement - as much rotation as
   his actual spells - so they teach the model that a hard swing means "cast
   nothing". They are dropped by default (see EXCLUDED_CLASSES, --keep-all).

3. An augmented trace must never land in a different fold from the recording it
   came from, or the score is measuring memorisation. Folds are grouped by
   participant, which keeps every derived trace with its source automatically.
"""

from __future__ import annotations

import argparse
import sys
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Sequence

import cv2
import joblib
import numpy as np
import pandas as pd
from sklearn.base import BaseEstimator, clone
from sklearn.calibration import CalibratedClassifierCV
from sklearn.ensemble import (ExtraTreesClassifier, HistGradientBoostingClassifier,
                              RandomForestClassifier)
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import accuracy_score, confusion_matrix, f1_score
from sklearn.model_selection import LeaveOneGroupOut, StratifiedGroupKFold
from sklearn.neighbors import KNeighborsClassifier
from sklearn.neural_network import MLPClassifier
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.svm import SVC

import gesture_features as gf
import imu_common as imu

# ===========================================================================
# CONFIGURATION
# ===========================================================================

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DATA_DIR = REPO_ROOT / "data"
DEFAULT_MODEL_PATH = REPO_ROOT / "models" / "gesture_model.joblib"

# The order used in every printed table and in the saved confusion matrix.
LABEL_ORDER = ("vertical_swipe", "horizontal_swipe", "zigzag", "circle", "idle")

# (participant, label) pairs left out of training. devam's idle recordings are
# movement, not idling: median peak 169 deg/s against 2-11 for everyone else,
# and more total rotation than his own swipes. Keeping them corrupts the idle
# class for every fold, not just his own.
EXCLUDED_CLASSES: tuple[tuple[str, str], ...] = (("devam", "idle"),)

RANDOM_STATE = 0
N_FOLDS_FALLBACK = 5      # used when there is only one participant to split on

# Confusion-matrix image
CM_CELL = 86
CM_MARGIN_L, CM_MARGIN_T = 220, 134
CM_BG = (24, 22, 20)
CM_FG = (238, 238, 238)
CM_DIM = (150, 148, 146)
FONT = cv2.FONT_HERSHEY_SIMPLEX


# ===========================================================================
# The candidate models
# ===========================================================================

def build_models() -> dict[str, BaseEstimator]:
    """
    The classifiers that get compared, one per algorithm family.

    Every one exposes ``predict_proba``, because live_predict.py shows a
    confidence and refuses to cast below a threshold. The tree ensembles take
    raw features; everything distance- or gradient-based is wrapped in a
    pipeline with a scaler, since the features mix g, deg/s and trace units.
    """
    return {
        "logreg": Pipeline([
            ("scale", StandardScaler()),
            ("clf", LogisticRegression(max_iter=4000, C=1.0,
                                       random_state=RANDOM_STATE)),
        ]),
        "svm_rbf": Pipeline([
            ("scale", StandardScaler()),
            # SVC(probability=True) is deprecated in sklearn 1.9, so calibrate
            # explicitly to keep predict_proba available.
            ("clf", CalibratedClassifierCV(
                SVC(kernel="rbf", C=10.0, gamma="scale",
                    random_state=RANDOM_STATE),
                method="sigmoid", ensemble=False, cv=3)),
        ]),
        "knn": Pipeline([
            ("scale", StandardScaler()),
            ("clf", KNeighborsClassifier(n_neighbors=7, weights="distance")),
        ]),
        "random_forest": RandomForestClassifier(
            n_estimators=400, min_samples_leaf=1, n_jobs=-1,
            random_state=RANDOM_STATE),
        "extra_trees": ExtraTreesClassifier(
            n_estimators=400, min_samples_leaf=1, n_jobs=-1,
            random_state=RANDOM_STATE),
        "hist_gb": HistGradientBoostingClassifier(
            max_iter=300, learning_rate=0.1, random_state=RANDOM_STATE),
        "mlp": Pipeline([
            ("scale", StandardScaler()),
            ("clf", MLPClassifier(hidden_layer_sizes=(128, 64), max_iter=1200,
                                  early_stopping=True,
                                  random_state=RANDOM_STATE)),
        ]),
    }


# ===========================================================================
# Loading the dataset
# ===========================================================================

@dataclass
class Dataset:
    """Feature matrix plus everything needed to split it honestly."""

    X: np.ndarray                     # (n, n_features)
    y: np.ndarray                     # (n,) labels
    participants: np.ndarray          # (n,) who performed it
    source_keys: np.ndarray           # (n,) the recording each row derives from
    augmented: np.ndarray             # (n,) bool
    files: np.ndarray                 # (n,) path relative to data/
    feature_names: tuple[str, ...] = field(default_factory=gf.feature_names)
    skipped: list[str] = field(default_factory=list)

    def __len__(self) -> int:
        return len(self.y)

    @property
    def n_participants(self) -> int:
        """How many people are represented."""
        return len(np.unique(self.participants))


def _starts_at_seq_zero(path: Path) -> bool:
    """
    True if the CSV's first ``seq`` is 0, which marks an augmented trace.

    collect.py writes the Arduino's own packet counter, which is already in the
    thousands by the time anyone records; augment_dataset.py writes a fresh
    0-based sample index. This is how Jay's mislabelled rows are caught.
    """
    try:
        with path.open() as handle:
            handle.readline()                     # header
            first = handle.readline().strip()
        return first != "" and int(float(first.split(",")[-1])) == 0
    except (OSError, ValueError, IndexError):
        return False


def load_manifest(data_dir: Path) -> set[str]:
    """The set of files augment_dataset.py claims to have generated."""
    path = data_dir / "augmentation_manifest.csv"
    if not path.exists():
        return set()
    manifest = pd.read_csv(path)
    return set(manifest["file"].astype(str))


def _source_map(data_dir: Path) -> dict[str, str]:
    """Map each augmented file to the recorded file it was derived from."""
    path = data_dir / "augmentation_manifest.csv"
    if not path.exists():
        return {}
    manifest = pd.read_csv(path)
    return dict(zip(manifest["file"].astype(str),
                    manifest["source_file"].astype(str)))


def load_dataset(data_dir: Path = DEFAULT_DATA_DIR,
                 real_only: bool = False,
                 keep_all: bool = False,
                 progress: bool = True) -> Dataset:
    """
    Read index.csv, extract features for every usable sample, and return them.

    ``real_only`` keeps only recorded samples. ``keep_all`` disables
    EXCLUDED_CLASSES. Rows whose CSV is missing or unreadable are skipped and
    reported rather than crashing the run.
    """
    index_path = data_dir / "index.csv"
    if not index_path.exists():
        raise FileNotFoundError(f"no dataset index at {index_path}")

    index = pd.read_csv(index_path)
    index["file"] = index["file"].astype(str).str.replace("\\", "/", regex=False)
    manifest = load_manifest(data_dir)
    sources = _source_map(data_dir)
    excluded = set() if keep_all else set(EXCLUDED_CLASSES)

    rows: list[np.ndarray] = []
    labels, who, srcs, augs, files = [], [], [], [], []
    skipped: list[str] = []

    for n, row in enumerate(index.itertuples(index=False), start=1):
        rel = str(row.file)
        label, participant = str(row.label), str(row.participant)

        if (participant, label) in excluded:
            continue

        csv_path = data_dir / rel
        if not csv_path.exists():
            skipped.append(f"{rel}: file missing")
            continue

        is_aug = rel in manifest or _starts_at_seq_zero(csv_path)
        if real_only and is_aug:
            continue

        try:
            frame = pd.read_csv(csv_path)
            t_s = frame["t_s"].to_numpy(dtype=float)
            values = frame[list(imu.CHANNELS)].to_numpy(dtype=float)
        except (OSError, KeyError, ValueError) as exc:
            skipped.append(f"{rel}: unreadable ({exc})")
            continue

        if len(t_s) < 2:
            skipped.append(f"{rel}: only {len(t_s)} samples")
            continue

        rows.append(gf.extract_features(t_s, values))
        labels.append(label)
        who.append(participant)
        augs.append(is_aug)
        files.append(rel)
        # An augmented row the manifest cannot attribute (Jay's) is grouped with
        # the rest of that person's gesture, which is the conservative choice:
        # it can never be split away from the recordings it came from.
        srcs.append(sources.get(rel, rel if not is_aug
                                else f"{participant}/{label}/unattributed"))

        if progress and n % 500 == 0:
            print(f"  ... {n}/{len(index)} rows read", flush=True)

    if not rows:
        raise RuntimeError("no usable samples found")

    return Dataset(
        X=np.vstack(rows),
        y=np.array(labels),
        participants=np.array(who),
        source_keys=np.array(srcs),
        augmented=np.array(augs, dtype=bool),
        files=np.array(files),
        skipped=skipped,
    )


def describe_dataset(data: Dataset) -> None:
    """Print what was loaded, per participant and per gesture."""
    print(f"\nLoaded {len(data)} samples, {data.X.shape[1]} features, "
          f"{data.n_participants} participants")
    print(f"  recorded : {int((~data.augmented).sum())}")
    print(f"  augmented: {int(data.augmented.sum())}")

    labels = [l for l in LABEL_ORDER if l in set(data.y)]
    header = "  " + f"{'participant':24s}" + "".join(f"{l[:9]:>11s}" for l in labels)
    print("\n" + header)
    for person in sorted(np.unique(data.participants), key=str.lower):
        mask = data.participants == person
        counts = "".join(f"{int(((data.y == l) & mask).sum()):11d}" for l in labels)
        print(f"  {person[:24]:24s}{counts}")
    totals = "".join(f"{int((data.y == l).sum()):11d}" for l in labels)
    print(f"  {'TOTAL':24s}{totals}")

    if data.skipped:
        print(f"\n  {len(data.skipped)} row(s) skipped:")
        for line in data.skipped[:10]:
            print(f"    {line}")
        if len(data.skipped) > 10:
            print(f"    ... and {len(data.skipped) - 10} more")


# ===========================================================================
# Cross-validation and model comparison
# ===========================================================================

@dataclass
class Result:
    """How one model did across the folds."""

    name: str
    accuracy: float                  # pooled over all held-out predictions
    macro_f1: float
    fold_scores: list[float]
    fit_seconds: float
    predictions: np.ndarray          # (n,) out-of-fold prediction per sample

    @property
    def fold_std(self) -> float:
        """Spread across folds - how much the score depends on who is held out."""
        return float(np.std(self.fold_scores)) if self.fold_scores else 0.0


def make_splitter(data: Dataset) -> tuple[object, np.ndarray, str, str]:
    """
    Choose the fold scheme, and say plainly what its score does and does not mean.

    With two or more people, hold one person out at a time: that is the only
    split that answers "does this work on someone new". With one person, fall
    back to grouping by source recording so an augmented trace is never scored
    against its own original - a much weaker claim, and the caveat says so.
    """
    if data.n_participants >= 2:
        return (LeaveOneGroupOut(), data.participants,
                "leave-one-participant-out",
                "on people the model never saw in training")

    n_groups = len(np.unique(data.source_keys))
    n_splits = min(N_FOLDS_FALLBACK, n_groups)
    return (StratifiedGroupKFold(n_splits=n_splits, shuffle=True,
                                 random_state=RANDOM_STATE),
            data.source_keys,
            f"{n_splits}-fold grouped by source recording (one participant only)",
            "on held-out reps from the SAME person - NOT a measure of how it "
            "handles someone new")


def cross_validate(models: dict[str, BaseEstimator],
                   data: Dataset,
                   splitter: object,
                   groups: np.ndarray) -> list[Result]:
    """Run every model over the same folds and return their results, best first."""
    splits = list(splitter.split(data.X, data.y, groups))
    results: list[Result] = []

    for name, model in models.items():
        print(f"  {name:16s}", end="", flush=True)
        predictions = np.empty(len(data), dtype=object)
        fold_scores: list[float] = []
        started = time.perf_counter()

        for train_idx, test_idx in splits:
            fitted = clone(model).fit(data.X[train_idx], data.y[train_idx])
            fold_pred = fitted.predict(data.X[test_idx])
            predictions[test_idx] = fold_pred
            fold_scores.append(float(accuracy_score(data.y[test_idx], fold_pred)))

        elapsed = time.perf_counter() - started
        predictions = predictions.astype(str)
        results.append(Result(
            name=name,
            accuracy=float(accuracy_score(data.y, predictions)),
            macro_f1=float(f1_score(data.y, predictions, average="macro")),
            fold_scores=fold_scores,
            fit_seconds=elapsed,
            predictions=predictions,
        ))
        print(f"{results[-1].accuracy:8.1%}  ({elapsed:.1f}s)", flush=True)

    # Rank by accuracy, then macro-F1 to break ties on an imbalanced class mix.
    results.sort(key=lambda r: (r.accuracy, r.macro_f1), reverse=True)
    return results


def print_comparison(results: Sequence[Result], scheme: str) -> None:
    """The model-comparison table: the whole point of running this script."""
    print(f"\n{'=' * 72}\nMODEL COMPARISON ({scheme})\n{'=' * 72}")
    print(f"  {'model':16s}{'accuracy':>10s}{'macro F1':>10s}"
          f"{'worst fold':>12s}{'fold std':>10s}{'time':>9s}")
    for n, r in enumerate(results):
        mark = "  <-- best" if n == 0 else ""
        worst = min(r.fold_scores) if r.fold_scores else float("nan")
        print(f"  {r.name:16s}{r.accuracy:9.1%}{r.macro_f1:10.3f}"
              f"{worst:11.1%}{r.fold_std:10.3f}{r.fit_seconds:8.1f}s{mark}")


def print_per_participant(data: Dataset, result: Result) -> None:
    """Accuracy for the best model with each person held out, worst first."""
    print(f"\nPer-participant accuracy ({result.name}, each held out in turn):")
    scores = []
    for person in np.unique(data.participants):
        mask = data.participants == person
        scores.append((float(accuracy_score(data.y[mask], result.predictions[mask])),
                       person, int(mask.sum())))
    lowest = min(s for s, _, _ in scores)
    alone = sum(1 for s, _, _ in scores if s == lowest) == 1
    for score, person, n in sorted(scores):
        flag = "   <-- weakest" if alone and score == lowest else ""
        print(f"  {person[:26]:26s} {score:6.1%}   ({n} samples){flag}")


def print_confusion(data: Dataset, result: Result,
                    labels: Sequence[str]) -> np.ndarray:
    """Print the confusion matrix as text and return it."""
    matrix = confusion_matrix(data.y, result.predictions, labels=list(labels))
    print(f"\nConfusion matrix for {result.name} (rows = truth, cols = predicted):")
    print("  " + " " * 18 + "".join(f"{l[:9]:>11s}" for l in labels))
    for i, label in enumerate(labels):
        row = "".join(f"{v:11d}" for v in matrix[i])
        recall = matrix[i, i] / max(1, matrix[i].sum())
        print(f"  {label:18s}{row}    {recall:5.1%}")
    return matrix


def print_top_features(model: BaseEstimator, names: Sequence[str],
                       count: int = 12) -> None:
    """List the most useful features, when the winning model can report them."""
    importances = getattr(model, "feature_importances_", None)
    if importances is None:
        print("\n(this model does not expose feature importances)")
        return
    order = np.argsort(importances)[::-1][:count]
    print(f"\nTop {count} features:")
    for rank, i in enumerate(order, start=1):
        print(f"  {rank:2d}. {names[i]:24s} {importances[i]:.4f}")


# ===========================================================================
# Confusion-matrix image
# ===========================================================================

def save_confusion_image(matrix: np.ndarray, labels: Sequence[str],
                         path: Path, title: str) -> None:
    """Draw the confusion matrix to a PNG, so it can go straight in the report."""
    n = len(labels)
    width = CM_MARGIN_L + n * CM_CELL + 30
    height = CM_MARGIN_T + n * CM_CELL + 40
    img = np.full((height, width, 3), CM_BG, dtype=np.uint8)

    cv2.putText(img, title, (20, 36), FONT, 0.6, CM_FG, 1, cv2.LINE_AA)
    cv2.putText(img, "rows = truth, columns = predicted", (20, 62),
                FONT, 0.45, CM_DIM, 1, cv2.LINE_AA)

    row_totals = matrix.sum(axis=1, keepdims=True).clip(min=1)
    shade = matrix / row_totals

    for i in range(n):
        y = CM_MARGIN_T + i * CM_CELL
        cv2.putText(img, labels[i][:16], (14, y + CM_CELL // 2 + 5),
                    FONT, 0.45, CM_FG, 1, cv2.LINE_AA)
        for j in range(n):
            x = CM_MARGIN_L + j * CM_CELL
            level = float(shade[i, j])
            # Correct cells go green, mistakes go red, both scaled by how often.
            colour = ((60, 200, 120) if i == j else (70, 70, 230))
            cell = tuple(int(CM_BG[k] + (colour[k] - CM_BG[k]) * level)
                         for k in range(3))
            cv2.rectangle(img, (x, y), (x + CM_CELL, y + CM_CELL), cell, -1)
            cv2.rectangle(img, (x, y), (x + CM_CELL, y + CM_CELL), CM_BG, 1)
            text = str(int(matrix[i, j]))
            (tw, _), _ = cv2.getTextSize(text, FONT, 0.5, 1)
            cv2.putText(img, text, (x + (CM_CELL - tw) // 2, y + CM_CELL // 2 + 5),
                        FONT, 0.5, CM_FG, 1, cv2.LINE_AA)

    for j in range(n):
        x = CM_MARGIN_L + j * CM_CELL
        # "horizontal_swipe" does not fit one cell wide, so break it at the
        # underscore and stack the parts instead of truncating the name.
        parts = labels[j].split("_")
        y = CM_MARGIN_T - 12 - 14 * (len(parts) - 1)
        for part in parts:
            cv2.putText(img, part[:11], (x + 4, y), FONT, 0.4, CM_DIM, 1,
                        cv2.LINE_AA)
            y += 14

    path.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(path), img)
    print(f"  confusion matrix -> {path}")


# ===========================================================================
# Saving
# ===========================================================================

def save_bundle(path: Path, model: BaseEstimator, data: Dataset,
                result: Result, scheme: str, caveat: str,
                compared: Sequence[Result], winner: Result) -> None:
    """
    Write the winning model plus everything needed to use and judge it later.

    live_predict.py checks ``feature_names`` against its own build of
    gesture_features.py and refuses to run a model whose features have moved.
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    joblib.dump({
        "model": model,
        "model_name": result.name,
        "labels": sorted(str(label) for label in set(data.y)),
        "feature_names": list(data.feature_names),
        "resample_points": gf.RESAMPLE_POINTS,
        "cv_accuracy": result.accuracy,
        "cv_macro_f1": result.macro_f1,
        "cv_scheme": scheme,
        "cv_caveat": caveat,
        "cv_fold_scores": result.fold_scores,
        "compared_models": {r.name: r.accuracy for r in compared},
        "comparison_winner": winner.name,
        "comparison_winner_accuracy": winner.accuracy,
        "chosen_by_hand": result.name != winner.name,
        "cv_worst_fold": min(result.fold_scores) if result.fold_scores else None,
        "n_samples": len(data),
        "n_recorded": int((~data.augmented).sum()),
        "n_augmented": int(data.augmented.sum()),
        "participants": sorted(set(data.participants)),
        "excluded_classes": [list(p) for p in EXCLUDED_CLASSES],
        "trained_at": datetime.now().isoformat(timespec="seconds"),
    }, path)
    print(f"  model            -> {path}")


# ===========================================================================
# Entry point
# ===========================================================================

def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Command-line options."""
    parser = argparse.ArgumentParser(
        description="Compare gesture classifiers and save the best one.")
    parser.add_argument("--data-dir", default=str(DEFAULT_DATA_DIR),
                        help="dataset root holding index.csv (default: data/)")
    parser.add_argument("--out", default=str(DEFAULT_MODEL_PATH),
                        help="where to write the winning model")
    parser.add_argument("--models", default="all",
                        help="comma-separated subset of: "
                             + ", ".join(build_models()) + " (default: all)")
    parser.add_argument("--real-only", action="store_true",
                        help="train on recorded samples only, ignore augmented")
    parser.add_argument("--keep-all", action="store_true",
                        help="do not drop the classes in EXCLUDED_CLASSES")
    parser.add_argument("--select", metavar="MODEL",
                        help="save this model instead of the comparison "
                             "winner (the comparison still runs and is "
                             "recorded, so the choice stays visible)")
    parser.add_argument("--dry-run", action="store_true",
                        help="load and report the dataset, fit nothing")
    parser.add_argument("--no-save", action="store_true",
                        help="compare models but do not write the winner")
    return parser.parse_args(argv)


def select_models(spec: str) -> dict[str, BaseEstimator]:
    """Resolve the --models argument into estimators."""
    available = build_models()
    if spec.strip().lower() == "all":
        return available
    chosen = {}
    for name in (s.strip() for s in spec.split(",") if s.strip()):
        if name not in available:
            raise SystemExit(f"unknown model {name!r}; choose from "
                             + ", ".join(available))
        chosen[name] = available[name]
    if not chosen:
        raise SystemExit("--models selected nothing")
    return chosen


def main(argv: Sequence[str] | None = None) -> int:
    """Load the data, compare the models, save the winner."""
    args = parse_args(argv)
    data_dir = Path(args.data_dir).expanduser().resolve()
    # Resolved before the dataset is read: on the full set that read takes a
    # couple of minutes, and a typo in --models should not cost that.
    models = select_models(args.models)
    if args.select and args.select not in models:
        raise SystemExit(f"--select {args.select!r} is not among the models "
                         f"being compared: " + ", ".join(models))

    print(f"Reading {data_dir / 'index.csv'}")
    if not args.keep_all and EXCLUDED_CLASSES:
        dropped = ", ".join(f"{p}/{l}" for p, l in EXCLUDED_CLASSES)
        print(f"  excluding: {dropped}  (--keep-all to include)")

    data = load_dataset(data_dir, real_only=args.real_only,
                        keep_all=args.keep_all)
    describe_dataset(data)

    splitter, groups, scheme, caveat = make_splitter(data)
    print(f"\nValidation: {scheme}")
    if data.n_participants < 3:
        print("  NOTE: fewer than 3 participants - treat the number as indicative.")

    if args.dry_run:
        print("\n--dry-run: nothing was fitted.")
        return 0

    print(f"\nFitting {len(models)} model(s) over "
          f"{splitter.get_n_splits(data.X, data.y, groups)} folds:")
    results = cross_validate(models, data, splitter, groups)

    labels = [l for l in LABEL_ORDER if l in set(data.y)]
    print_comparison(results, scheme)

    winner = results[0]
    best = winner
    if args.select:
        best = next(r for r in results if r.name == args.select)

    if best is winner:
        print(f"\nBest model: {best.name}  ({best.accuracy:.1%} {caveat})")
    else:
        # Saving the runner-up is a legitimate call - the top model can win on
        # mean accuracy while being the shakier choice on an unseen person -
        # but it must never look like it simply won.
        print(f"\nSaving {best.name} ({best.accuracy:.1%}) by request "
              f"(--select); the comparison winner was {winner.name} "
              f"({winner.accuracy:.1%}).")
        print(f"  worst fold: {best.name} {min(best.fold_scores):.1%} vs "
              f"{winner.name} {min(winner.fold_scores):.1%}")
    if data.n_participants >= 2:
        print_per_participant(data, best)
    matrix = print_confusion(data, best, labels)

    print(f"\nRefitting {best.name} on all {len(data)} samples ...")
    final = clone(models[best.name]).fit(data.X, data.y)
    print_top_features(final, data.feature_names)

    if args.no_save:
        print("\n--no-save: nothing written.")
        return 0

    out_path = Path(args.out).expanduser().resolve()
    print()
    save_bundle(out_path, final, data, best, scheme, caveat, results, winner)
    save_confusion_image(matrix, labels,
                         out_path.with_name(out_path.stem + "_confusion.png"),
                         f"{best.name}  -  {best.accuracy:.1%}  ({scheme})")
    print(f"\nRun the live demo with:\n  python live_predict.py --camera")
    return 0


if __name__ == "__main__":
    sys.exit(main())
