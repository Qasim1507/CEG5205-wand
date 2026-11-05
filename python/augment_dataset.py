#!/usr/bin/env python3
"""Reproducibly augment recorded IMU gestures, keeping source provenance.

Run from the repository root: py -3.12 python/augment_dataset.py
Only repetitions 1..25 are used as sources. Existing samples are never changed.
"""

from __future__ import annotations

import csv
import hashlib
from datetime import datetime, timezone
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[1] / "data"
LABEL_SPELL = {
    "vertical_swipe": "fire", "horizontal_swipe": "wind",
    "zigzag": "lightning", "circle": "water", "idle": "none",
}
CHANNELS = ("ax", "ay", "az", "gx", "gy", "gz")
INDEX_FIELDS = ("participant", "label", "spell", "rep", "file", "n_samples",
                "duration_s", "mode", "dropped_pct", "timestamp")
MANIFEST_FIELDS = ("file", "source_file", "seed", "speed_factor", "time_bend",
                   "channel_scales", "channel_offsets")
TARGET_REPS = 100
RECORDED_REPS = 25


def read_sample(path: Path) -> tuple[np.ndarray, np.ndarray]:
    with path.open(newline="") as handle:
        rows = list(csv.DictReader(handle))
    if not rows:
        raise ValueError(f"Empty recording: {path}")
    t = np.array([float(row["t_s"]) for row in rows])
    x = np.array([[float(row[c]) for c in CHANNELS] for row in rows])
    if not np.isfinite(t).all() or not np.isfinite(x).all() or np.any(np.diff(t) <= 0):
        raise ValueError(f"Invalid recording: {path}")
    return t, x


def augment(t: np.ndarray, x: np.ndarray, rng: np.random.Generator):
    # The sampling rate remains 100 Hz. A small speed change alters duration;
    # a smooth, monotone bend varies local timing without shuffling the motion.
    speed = float(rng.uniform(0.93, 1.07))
    n = max(26, round((len(t) - 1) * speed) + 1)
    u = np.linspace(0.0, 1.0, n)
    bend = float(rng.uniform(-0.035, 0.035))
    source_u = u + bend * np.sin(np.pi * u) * np.sin(2 * np.pi * u)
    source_t = t[0] + source_u * (t[-1] - t[0])
    warped = np.column_stack([np.interp(source_t, t, x[:, i]) for i in range(6)])

    # Keep channel axes and signs intact: rotating an IMU trace can change its
    # gesture label. Perturbations are deliberately small and sample-specific.
    scales = rng.uniform(0.97, 1.03, size=6)
    offsets = np.r_[rng.normal(0, 0.008, 3), rng.normal(0, 0.6, 3)]
    noise_sd = np.r_[np.full(3, 0.003), np.full(3, 0.35)]
    noise = rng.normal(size=(n + 4, 6))
    noise = (noise[:-4] + 2 * noise[1:-3] + 3 * noise[2:-2]
             + 2 * noise[3:-1] + noise[4:]) / np.sqrt(19)
    output = warped * scales + offsets + noise * noise_sd
    # Hardware limits are +/-4 g and +/-1000 deg/s. Reject extrapolation.
    output[:, :3] = np.clip(output[:, :3], -3.9, 3.9)
    output[:, 3:] = np.clip(output[:, 3:], -980, 980)
    return output, speed, bend, scales, offsets


def main() -> None:
    index_path = ROOT / "index.csv"
    with index_path.open(newline="") as handle:
        raw_index = list(csv.DictReader(handle))
    # Some recorded rows used Windows separators. Normalize those paths so
    # filesystem checks and duplicate detection work on every platform.
    index = []
    seen = set()
    for row in raw_index:
        row["file"] = row["file"].replace("\\", "/")
        if row["file"] not in seen:
            index.append(row)
            seen.add(row["file"])
    paths = {row["file"] for row in index}
    manifest_path = ROOT / "augmentation_manifest.csv"
    manifest = []
    if manifest_path.exists():
        with manifest_path.open(newline="") as handle:
            manifest = list(csv.DictReader(handle))
    participants = sorted(p for p in ROOT.iterdir() if p.is_dir() and not p.name.startswith("_"))
    added = 0
    for participant in participants:
        if participant.name == "Jay":
            continue  # Jay's 100 samples per gesture already exist.
        for label, spell in LABEL_SPELL.items():
            folder = participant / label
            sources = [folder / f"{label}_{rep:03d}.csv" for rep in range(1, RECORDED_REPS + 1)]
            if not all(path.exists() for path in sources):
                raise ValueError(f"Expected 25 source recordings in {folder}")
            # Two participants have files on disk but were omitted from index.csv.
            for rep, source in enumerate(sources, 1):
                rel = source.relative_to(ROOT).as_posix()
                if rel not in paths:
                    t, _ = read_sample(source)
                    index.append(dict(participant=participant.name, label=label, spell=spell,
                                      rep=rep, file=rel, n_samples=len(t),
                                      duration_s=f"{t[-1] - t[0]:.3f}", mode="motion",
                                      dropped_pct="", timestamp=""))
                    paths.add(rel)
            for rep in range(RECORDED_REPS + 1, TARGET_REPS + 1):
                target = folder / f"{label}_{rep:03d}.csv"
                rel = target.relative_to(ROOT).as_posix()
                if target.exists():
                    if rel not in paths:
                        raise ValueError(f"Existing file is absent from index: {rel}")
                    continue
                source = sources[(rep - RECORDED_REPS - 1) % RECORDED_REPS]
                source_rel = source.relative_to(ROOT).as_posix()
                seed = int.from_bytes(hashlib.sha256(rel.encode()).digest()[:8], "big")
                rng = np.random.default_rng(seed)
                t, x = read_sample(source)
                y, speed, bend, scales, offsets = augment(t, x, rng)
                with target.open("w", newline="") as handle:
                    writer = csv.writer(handle)
                    writer.writerow(("t_s", *CHANNELS, "seq"))
                    for i, values in enumerate(y):
                        writer.writerow((f"{i / 100:.5f}", *(f"{v:.5f}" for v in values), i))
                index.append(dict(participant=participant.name, label=label, spell=spell,
                                  rep=rep, file=rel, n_samples=len(y),
                                  duration_s=f"{(len(y) - 1) / 100:.3f}", mode="augmented",
                                  dropped_pct="0.0", timestamp=datetime.now(timezone.utc).isoformat()))
                manifest.append(dict(file=rel, source_file=source_rel, seed=seed,
                                     speed_factor=f"{speed:.6f}", time_bend=f"{bend:.6f}",
                                     channel_scales=";".join(f"{v:.6f}" for v in scales),
                                     channel_offsets=";".join(f"{v:.6f}" for v in offsets)))
                paths.add(rel)
                added += 1
    with index_path.open("w", newline="") as handle:
        writer = csv.DictWriter(handle, INDEX_FIELDS, lineterminator="\n")
        writer.writeheader()
        writer.writerows(index)
    with manifest_path.open("w", newline="") as handle:
        writer = csv.DictWriter(handle, MANIFEST_FIELDS, lineterminator="\n")
        writer.writeheader()
        writer.writerows(manifest)
    print(f"Created {added} augmented samples; index has {len(index)} rows.")


if __name__ == "__main__":
    main()
