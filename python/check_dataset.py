#!/usr/bin/env python3
"""
check_dataset.py - a quick sanity check on the collected gesture dataset.

Prints how many samples exist per participant and gesture, flags samples that
look wrong (too many dropped packets, odd duration, saturated sensors), and
saves one image per gesture showing a grid of its 2D traces, so you can spot a
mislabelled recording in a second.

Run:
    python check_dataset.py
    python check_dataset.py --participant p01
    python check_dataset.py --no-images
"""

from __future__ import annotations

import argparse
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np
import pandas as pd

import imu_common as imu

# --- configuration ---------------------------------------------------------
MAX_DROPPED_PCT = 2.0        # flag a sample above this
MIN_DURATION_S = 0.25        # flag a sample shorter than this
MAX_DURATION_S = 3.00        # flag a sample longer than this (idle excepted)
IDLE_MAX_DURATION_S = 2.50
ACC_SATURATION_G = 3.9       # the +-4 g range is effectively clipped here
GYRO_SATURATION_DPS = 980.0  # ...and the +-1000 deg/s range here
GRID_CELL = 180              # pixel size of one trace in the grid image
GRID_COLUMNS = 8


INDEX_COLUMNS = ["participant", "label", "spell", "rep", "file", "n_samples",
                 "duration_s", "mode", "dropped_pct", "timestamp"]


def read_index_raw(data_root: Path) -> pd.DataFrame:
    """Read data/index.csv exactly as it is on disk, duplicates and all."""
    path = data_root / "index.csv"
    if not path.exists():
        print(f"No index at {path} - run collect.py first.")
        return pd.DataFrame(columns=INDEX_COLUMNS)
    return pd.read_csv(path)


def load_index(data_root: Path) -> pd.DataFrame:
    """The index with duplicate rows for the same file collapsed."""
    frame = read_index_raw(data_root)
    # Deleting a sample's CSV frees its file number, so a later run can write
    # the same path again and leave two rows behind. The newer row wins.
    return frame.drop_duplicates(subset=["participant", "file"], keep="last")


def prune_index(data_root: Path) -> tuple[int, int, int]:
    """
    Rewrite index.csv with only the rows backed by a CSV that still exists.

    Drops duplicate rows for the same file (keeping the newest) and rows whose
    CSV has been deleted. The previous index is kept as ``index.csv.bak``.

    Returns ``(duplicates_removed, orphans_removed, rows_kept)``.
    """
    path = data_root / "index.csv"
    raw = read_index_raw(data_root)
    if raw.empty:
        return (0, 0, 0)

    deduped = raw.drop_duplicates(subset=["participant", "file"], keep="last")
    alive = deduped[deduped["file"].map(lambda f: (data_root / str(f)).exists())]

    # A fixed .bak name would be overwritten by a second prune, destroying the
    # only copy of the original index, so stamp each backup with the time.
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = path.with_name(f"index.{stamp}.bak.csv")
    path.replace(backup)
    alive.to_csv(path, index=False)
    print(f"previous index kept as {backup.name}")
    return (len(raw) - len(deduped), len(deduped) - len(alive), len(alive))


def print_counts(frame: pd.DataFrame) -> None:
    """A participant x gesture table of sample counts."""
    print("\n=== samples per participant x gesture ===")
    table = pd.crosstab(frame["participant"], frame["label"])
    labels = [g for g in ("vertical_swipe", "horizontal_swipe", "zigzag",
                          "circle", "idle") if g in table.columns]
    table = table[labels]
    table["TOTAL"] = table.sum(axis=1)
    print(table.to_string())
    print(f"\ntotal samples: {len(frame)}")


def check_samples(frame: pd.DataFrame, data_root: Path) -> pd.DataFrame:
    """Open every CSV and return a frame of the problems found."""
    problems: list[dict[str, object]] = []
    for row in frame.itertuples():
        path = data_root / str(row.file)
        notes: list[str] = []
        if not path.exists():
            problems.append({"file": row.file, "problem": "CSV is missing"})
            continue

        data = pd.read_csv(path)
        missing = [c for c in ("t_s", *imu.CHANNELS) if c not in data.columns]
        if missing:
            problems.append({"file": row.file,
                             "problem": f"columns missing: {missing}"})
            continue

        if float(row.dropped_pct) > MAX_DROPPED_PCT:
            notes.append(f"dropped {row.dropped_pct:.1f}%")
        duration = float(data["t_s"].iloc[-1]) if len(data) else 0.0
        upper = IDLE_MAX_DURATION_S if row.label == "idle" else MAX_DURATION_S
        if duration < MIN_DURATION_S:
            notes.append(f"short ({duration:.2f}s)")
        elif duration > upper:
            notes.append(f"long ({duration:.2f}s)")

        acc_peak = float(np.abs(data[["ax", "ay", "az"]].to_numpy()).max())
        gyro_peak = float(np.abs(data[["gx", "gy", "gz"]].to_numpy()).max())
        if acc_peak >= ACC_SATURATION_G:
            notes.append(f"accel saturated ({acc_peak:.2f} g)")
        if gyro_peak >= GYRO_SATURATION_DPS:
            notes.append(f"gyro saturated ({gyro_peak:.0f} dps)")
        if row.label != "idle" and gyro_peak < 60.0:
            notes.append(f"barely moved ({gyro_peak:.0f} dps) - mislabelled?")

        rate = (len(data) - 1) / duration if duration > 0 else 0.0
        if duration > 0.3 and not (0.7 * imu.NOMINAL_RATE_HZ <= rate
                                   <= 1.3 * imu.NOMINAL_RATE_HZ):
            notes.append(f"rate {rate:.0f} Hz")

        if notes:
            problems.append({"file": row.file, "problem": ", ".join(notes)})

    return pd.DataFrame(problems)


def redraw_sample_traces(frame: pd.DataFrame, data_root: Path) -> int:
    """
    Re-render every sample's own PNG from its CSV, using the current mapping.

    Needed after the TRACER_* constants in ``imu_common.py`` change: the CSV
    data is untouched, but the pictures beside it were drawn with the old axes.
    """
    count = 0
    for row in frame.itertuples():
        path = data_root / str(row.file)
        if not path.exists():
            continue
        data = pd.read_csv(path)
        trace = imu.GestureTracer.path_from_arrays(data["t_s"].to_numpy(),
                                                   data[["gx", "gy", "gz"]].to_numpy())
        cv2.imwrite(str(path.with_suffix(".png")),
                    imu.trace_image(trace, title=f"{row.label} #{row.rep}"))
        count += 1
    return count


def save_trace_grids(frame: pd.DataFrame, data_root: Path, out_dir: Path) -> None:
    """One PNG per gesture: a grid of all its traces, for a visual check."""
    out_dir.mkdir(parents=True, exist_ok=True)
    for label, group in frame.groupby("label"):
        cells: list[np.ndarray] = []
        for row in group.itertuples():
            path = data_root / str(row.file)
            if not path.exists():
                continue
            data = pd.read_csv(path)
            gyro = data[["gx", "gy", "gz"]].to_numpy()
            trace = imu.GestureTracer.path_from_arrays(data["t_s"].to_numpy(), gyro)
            cell = imu.trace_image(trace, GRID_CELL, GRID_CELL,
                                   f"{row.participant} #{row.rep}")
            cells.append(cell)
        if not cells:
            continue

        columns = min(GRID_COLUMNS, len(cells))
        rows = (len(cells) + columns - 1) // columns
        sheet = np.full((rows * GRID_CELL, columns * GRID_CELL, 3),
                        imu.BG_COLOR, dtype=np.uint8)
        for i, cell in enumerate(cells):
            r, c = divmod(i, columns)
            sheet[r * GRID_CELL:(r + 1) * GRID_CELL,
                  c * GRID_CELL:(c + 1) * GRID_CELL] = cell
        out_path = out_dir / f"traces_{label}.png"
        cv2.imwrite(str(out_path), sheet)
        print(f"wrote {out_path}  ({len(cells)} traces)")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data-dir",
                        default=str(Path(__file__).resolve().parent.parent / "data"),
                        help="dataset root (holds index.csv)")
    parser.add_argument("--participant", help="only check this participant")
    parser.add_argument("--no-images", action="store_true",
                        help="skip writing the trace grid images")
    parser.add_argument("--prune", action="store_true",
                        help="rewrite index.csv without duplicate rows or rows "
                             "whose CSV has been deleted (keeps a .bak)")
    parser.add_argument("--redraw", action="store_true",
                        help="re-render each sample's PNG from its CSV "
                             "(use after changing the TRACER_* constants)")
    args = parser.parse_args(argv)

    data_root = Path(args.data_dir)
    if args.prune and (data_root / "index.csv").exists():
        print("=== pruning index.csv ===")
        duplicates, orphans, kept = prune_index(data_root)
        print(f"removed {duplicates} duplicate rows and {orphans} rows whose "
              f"CSV was deleted; {kept} rows kept")

    frame = load_index(data_root)
    if frame.empty:
        return 1
    if args.participant:
        frame = frame[frame["participant"] == args.participant]
        if frame.empty:
            print(f"No samples for participant {args.participant!r}.")
            return 1

    print_counts(frame)

    if args.redraw:
        print("\n=== redrawing sample traces ===")
        print(f"redrew {redraw_sample_traces(frame, data_root)} PNGs "
              f"with x={imu.CHANNELS[3 + imu.TRACER_X_AXIS]}, "
              f"y={imu.CHANNELS[3 + imu.TRACER_Y_AXIS]} "
              f"(signs {imu.TRACER_X_SIGN:+.0f}, {imu.TRACER_Y_SIGN:+.0f})")

    print("\n=== per-sample checks ===")
    problems = check_samples(frame, data_root)
    if problems.empty:
        print("no problems found")
    else:
        print(problems.to_string(index=False))
        print(f"\n{len(problems)} of {len(frame)} samples flagged")

    if not args.no_images:
        print("\n=== trace grids ===")
        save_trace_grids(frame, data_root, data_root / "_check")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
