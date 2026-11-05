#!/usr/bin/env python3
"""
gesture_features.py - turns one IMU recording into a fixed-length feature vector.

Both halves of the ML pipeline import this module, so a model trained by
train_gestures.py always sees exactly the features live_predict.py produces:

    train_gestures.py   CSV on disk   -> extract_features()
    live_predict.py     live samples  -> features_from_samples()

It also holds GestureSegmenter, the motion trigger that cuts a continuous
IMU stream into gesture-sized chunks. collect.py applies the same rule while
recording, so a live segment has the same shape as a training sample.

Deliberately NOT a feature: the duration of the recording. "idle" was captured
with a fixed 2 s window while the spells stop as soon as the wand does, so
duration tells the classifier which protocol produced the row rather than what
the hand did. Leaving it out costs no accuracy and removes the leak.
"""

from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache
from typing import Sequence

import numpy as np

import imu_common as imu

# ===========================================================================
# CONFIGURATION
# ===========================================================================

RESAMPLE_POINTS = 12      # samples per channel in the resampled waveform
QUIET_DPS = 30.0          # below this rotation rate the wand counts as still
EPS = 1e-9                # guards divisions on degenerate recordings

GYRO_AXES = ("gx", "gy", "gz")
ACC_AXES = ("ax", "ay", "az")


# ===========================================================================
# Feature extraction
# ===========================================================================

def _describe(t_s: np.ndarray, values: np.ndarray) -> list[tuple[str, float]]:
    """
    The single source of truth for the feature vector: ``[(name, value), ...]``.

    ``t_s`` is seconds from the start of the recording, ``values`` is (n, 6) in
    imu.CHANNELS order (ax, ay, az, gx, gy, gz). Names and values are produced
    together so the two can never drift apart.
    """
    out: list[tuple[str, float]] = []
    acc = values[:, 0:3]
    gyro = values[:, 3:6]
    dt = np.gradient(t_s) if len(t_s) > 1 else np.array([imu.NOMINAL_DT])

    # -- per-channel amplitude statistics (6 channels x 7) ------------------
    for i, name in enumerate(imu.CHANNELS):
        col = values[:, i]
        out += [
            (f"{name}_mean", float(col.mean())),
            (f"{name}_std", float(col.std())),
            (f"{name}_min", float(col.min())),
            (f"{name}_max", float(col.max())),
            (f"{name}_range", float(col.max() - col.min())),
            (f"{name}_rms", float(np.sqrt(np.mean(col ** 2)))),
            (f"{name}_mean_abs", float(np.abs(col).mean())),
        ]

    # -- how hard and how continuously the wand was rotating ----------------
    mag = np.linalg.norm(gyro, axis=1)
    out += [
        ("gyro_mag_mean", float(mag.mean())),
        ("gyro_mag_std", float(mag.std())),
        ("gyro_mag_max", float(mag.max())),
        ("gyro_mag_median", float(np.median(mag))),
        ("gyro_quiet_frac", float(np.mean(mag < QUIET_DPS))),
    ]

    # -- rotation actually travelled, by axis -------------------------------
    # net_rot is signed, so a down-stroke and an up-stroke cancel; abs_rot
    # does not, so the pair separates "one stroke" from "out and back".
    net_rot = (gyro * dt[:, None]).sum(axis=0)
    abs_rot = (np.abs(gyro) * dt[:, None]).sum(axis=0)
    for i, name in enumerate(GYRO_AXES):
        out.append((f"{name}_net_rot", float(net_rot[i])))
    for i, name in enumerate(GYRO_AXES):
        out.append((f"{name}_abs_rot", float(abs_rot[i])))

    # -- direction reversals, which is what makes a zigzag a zigzag ---------
    for i, name in enumerate(GYRO_AXES):
        col = gyro[:, i]
        loud = col[np.abs(col) > QUIET_DPS]
        crossings = int(np.sum(np.diff(np.sign(loud)) != 0)) if loud.size > 1 else 0
        out.append((f"{name}_sign_changes", float(crossings)))

    # -- cross-axis coupling: which axes move together ----------------------
    for block, names in ((gyro, GYRO_AXES), (acc, ACC_AXES)):
        for a in range(3):
            for b in range(a + 1, 3):
                out.append((f"corr_{names[a]}_{names[b]}",
                            _safe_corr(block[:, a], block[:, b])))

    # -- geometry of the 2D trace the wand drew -----------------------------
    path = imu.GestureTracer.path_from_arrays(t_s, gyro)
    out += _path_features(path)

    # -- coarse waveform shape, resampled to a fixed length -----------------
    grid = np.linspace(0.0, 1.0, RESAMPLE_POINTS)
    span = t_s[-1] - t_s[0]
    unit_t = (t_s - t_s[0]) / span if span > EPS else np.linspace(0.0, 1.0, len(t_s))
    for i, name in enumerate(imu.CHANNELS):
        resampled = np.interp(grid, unit_t, values[:, i])
        for k, v in enumerate(resampled):
            out.append((f"{name}_w{k:02d}", float(v)))

    return out


def _safe_corr(a: np.ndarray, b: np.ndarray) -> float:
    """Pearson correlation that returns 0.0 instead of NaN on a flat channel."""
    if a.size < 2 or a.std() < EPS or b.std() < EPS:
        return 0.0
    return float(np.corrcoef(a, b)[0, 1])


def _path_features(path: np.ndarray) -> list[tuple[str, float]]:
    """Shape descriptors for the integrated-gyro trace (the 'air mouse' path)."""
    if path.shape[0] < 2:
        return [("path_length", 0.0), ("bbox_w", 0.0), ("bbox_h", 0.0),
                ("bbox_aspect", 0.0), ("net_disp", 0.0), ("straightness", 0.0),
                ("signed_area", 0.0), ("turn_reversals", 0.0)]

    steps = np.diff(path, axis=0)
    length = float(np.linalg.norm(steps, axis=1).sum())
    width = float(path[:, 0].max() - path[:, 0].min())
    height = float(path[:, 1].max() - path[:, 1].min())
    net = float(np.linalg.norm(path[-1] - path[0]))

    # Shoelace formula: positive one way round, negative the other, so this is
    # what separates a clockwise circle from a counter-clockwise one.
    x, y = path[:, 0], path[:, 1]
    signed_area = float(0.5 * np.sum(x[:-1] * y[1:] - x[1:] * y[:-1]))

    heading = np.arctan2(steps[:, 1], steps[:, 0])
    turn = np.diff(np.unwrap(heading)) if heading.size > 1 else np.zeros(0)
    reversals = int(np.sum(np.diff(np.sign(turn)) != 0)) if turn.size > 1 else 0

    return [
        ("path_length", length),
        ("bbox_w", width),
        ("bbox_h", height),
        ("bbox_aspect", width / (height + EPS)),
        ("net_disp", net),
        ("straightness", net / (length + EPS)),
        ("signed_area", signed_area),
        ("turn_reversals", float(reversals)),
    ]


def extract_features(t_s: np.ndarray, values: np.ndarray) -> np.ndarray:
    """
    Feature vector for one recording, aligned with :func:`feature_names`.

    ``t_s`` is (n,) seconds from the start, ``values`` is (n, 6) in
    imu.CHANNELS order. A recording shorter than 2 samples returns zeros.
    """
    t_s = np.asarray(t_s, dtype=float)
    values = np.asarray(values, dtype=float)
    if values.ndim != 2 or values.shape[1] != len(imu.CHANNELS):
        raise ValueError(f"values must be (n, {len(imu.CHANNELS)}), got {values.shape}")
    if len(t_s) < 2:
        return np.zeros(len(feature_names()), dtype=float)
    vec = np.array([v for _, v in _describe(t_s, values)], dtype=float)
    return np.nan_to_num(vec, nan=0.0, posinf=0.0, neginf=0.0)


@lru_cache(maxsize=1)
def feature_names() -> tuple[str, ...]:
    """Names of the features, in the order :func:`extract_features` returns them."""
    n = max(RESAMPLE_POINTS, 4)
    t_s = np.linspace(0.0, 1.0, n)
    values = np.zeros((n, len(imu.CHANNELS)))
    values[:, 3] = np.linspace(-1.0, 1.0, n)   # one non-flat channel
    return tuple(name for name, _ in _describe(t_s, values))


def features_from_samples(samples: Sequence[imu.Sample]) -> np.ndarray:
    """Feature vector for a list of live :class:`imu_common.Sample` objects."""
    t_s, values, _ = imu.samples_to_arrays(samples)
    return extract_features(t_s, values)


# ===========================================================================
# Motion segmentation
# ===========================================================================

@dataclass
class Segment:
    """One candidate gesture cut out of a live stream."""

    samples: list[imu.Sample]
    duration_s: float
    accepted: bool
    reason: str = ""


class GestureSegmenter:
    """
    Watches a live IMU stream and emits one :class:`Segment` per movement.

    The rule matches collect.py: arm above ``start_dps``, keep a little
    pre-roll from before the trigger so the start of the stroke is not lost,
    and close once the wand stays below ``stop_dps`` for ``stop_hold_s``.
    Segments outside the duration limits are still returned, marked
    ``accepted=False``, so a caller can show the user why nothing was cast.
    """

    def __init__(self,
                 start_dps: float,
                 stop_dps: float,
                 stop_hold_s: float,
                 preroll_s: float,
                 min_duration_s: float,
                 max_duration_s: float,
                 rate_hz: float = imu.NOMINAL_RATE_HZ) -> None:
        self.start_dps = start_dps
        self.stop_dps = stop_dps
        self.stop_hold_s = stop_hold_s
        self.preroll_s = preroll_s
        self.min_duration_s = min_duration_s
        self.max_duration_s = max_duration_s
        self._preroll_len = max(1, int(preroll_s * rate_hz))
        self._recording = False
        self._buffer: list[imu.Sample] = []
        self._preroll: list[imu.Sample] = []
        self._quiet_since: float | None = None

    def reset(self) -> None:
        """Forget any part-recorded gesture (use after a reconnect)."""
        # Fresh lists, not .clear(): _close() hands its buffer to the caller,
        # and clearing in place would empty the segment it just returned.
        self._recording = False
        self._buffer = []
        self._preroll = []
        self._quiet_since = None

    @property
    def recording(self) -> bool:
        """True while a movement is being captured."""
        return self._recording

    def push(self, sample: imu.Sample) -> Segment | None:
        """Feed one sample; returns a :class:`Segment` when a movement ends."""
        moving = sample.gyro_mag >= self.start_dps

        if not self._recording:
            self._preroll.append(sample)
            if len(self._preroll) > self._preroll_len:
                self._preroll.pop(0)
            if moving:
                self._recording = True
                self._buffer = list(self._preroll)
                self._preroll.clear()
                self._quiet_since = None
            return None

        self._buffer.append(sample)

        if sample.gyro_mag < self.stop_dps:
            if self._quiet_since is None:
                self._quiet_since = sample.t_host
            elif sample.t_host - self._quiet_since >= self.stop_hold_s:
                return self._close()
        else:
            self._quiet_since = None

        # collect.py lets a run-on movement reach max + stop_hold and then
        # rejects it as too long. Matching that matters: truncating at exactly
        # max_duration_s would instead hand the classifier a 3 s slice of
        # continuous waving and let it cast a spell from it.
        if self._duration(self._buffer) >= self.max_duration_s + self.stop_hold_s:
            return self._close()
        return None

    def push_many(self, samples: Sequence[imu.Sample]) -> list[Segment]:
        """Feed a batch of samples; returns every segment they completed."""
        out = []
        for s in samples:
            seg = self.push(s)
            if seg is not None:
                out.append(seg)
        return out

    def _close(self) -> Segment:
        """Finish the current recording and judge whether it is usable."""
        samples = self._buffer
        duration = self._duration(samples)
        self.reset()

        if duration < self.min_duration_s:
            return Segment(samples, duration, False, "too short")
        if duration > self.max_duration_s:
            return Segment(samples, duration, False, "too long")
        return Segment(samples, duration, True)

    @staticmethod
    def _duration(samples: Sequence[imu.Sample]) -> float:
        """Length of a sample list in seconds, from the Arduino timestamps."""
        if len(samples) < 2:
            return 0.0
        return (samples[-1].t_ms - samples[0].t_ms) / 1000.0
