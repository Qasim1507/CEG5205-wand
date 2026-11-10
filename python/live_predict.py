#!/usr/bin/env python3
"""
live_predict.py - the live demo: wave the wand, see the spell it casts.

Loads the model train_gestures.py picked as best, watches the IMU stream for a
movement using the SAME trigger collect.py recorded with, and classifies each
movement as it finishes. Optionally shows the webcam behind the overlay, so a
demo video shows the person and the prediction together.

Examples
--------
    python live_predict.py                        # IMU only
    python live_predict.py --camera               # webcam behind the overlay
    python live_predict.py --fake                 # no hardware, synthetic wand
    python live_predict.py --min-confidence 0.40
    python live_predict.py --model ../models/alt.joblib

Keys (the OpenCV window must have focus)
----------------------------------------
    1-5            tag what the last gesture REALLY was, to score the demo
    C              clear the trace
    Q or Esc       quit and print the summary

Every segment is written to data/_live/ with its trace PNG, so a run that goes
wrong can be looked at afterwards instead of guessed about.
"""

from __future__ import annotations

import argparse
import sys
import time
from collections import Counter
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Sequence

import cv2
import joblib
import numpy as np
import pandas as pd

import gesture_features as gf
import imu_common as imu
from unity_bridge import UnityBridge
from wand_orientation import WandOrientation
# Reusing collect.py's thresholds is deliberate: if the recording trigger and
# the live trigger ever differ, the model sees segments unlike its training set.
from collect import (CANVAS_H, CANVAS_W, COL_ACCENT, COL_DIM, COL_OK, COL_REC,
                     COL_TEXT, COL_WARN, GESTURES, KEYS_QUIT, MAX_DURATION_S,
                     MIN_DURATION_S, PREROLL_S, START_DPS, STOP_DPS,
                     STOP_HOLD_S, blank_canvas, fit_camera_frame, put_text)

# ===========================================================================
# CONFIGURATION
# ===========================================================================

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_MODEL_PATH = REPO_ROOT / "models" / "gesture_model.joblib"
DEFAULT_LOG_DIR = REPO_ROOT / "data" / "_live"

DEFAULT_MIN_CONFIDENCE = 0.55   # below this the demo says "unsure" and casts nothing
RESULT_HOLD_S = 2.5             # how long a result stays on screen
TRACE_BOX = (CANVAS_W - 460, 150, 420, 420)   # x, y, w, h

WINDOW_NAME = "CEG5205 wand - live spell recognition"
SPELL_BY_LABEL = {g.label: g.spell for g in GESTURES}
MOTION_BY_LABEL = {g.label: g.motion for g in GESTURES}
# Keys 1..5 tag the previous gesture with its true label, in GESTURES order.
TAG_KEYS = {ord(str(i + 1)): g.label for i, g in enumerate(GESTURES)}
KEY_CLEAR = ord("c")


# ===========================================================================
# Model loading
# ===========================================================================

@dataclass
class Model:
    """The trained bundle, with the metadata needed to report it honestly."""

    estimator: object
    name: str
    labels: list[str]
    accuracy: float
    scheme: str
    caveat: str
    trained_at: str
    participants: list[str]
    compared: dict[str, float] = field(default_factory=dict)
    worst_fold: float | None = None
    winner: str = ""
    winner_accuracy: float = 0.0
    chosen_by_hand: bool = False

    def predict(self, features: np.ndarray) -> tuple[str, float, dict[str, float]]:
        """Return ``(label, confidence, probability per label)`` for one vector."""
        row = features.reshape(1, -1)
        probabilities = self.estimator.predict_proba(row)[0]
        classes = [str(c) for c in self.estimator.classes_]
        order = int(np.argmax(probabilities))
        per_label = dict(zip(classes, (float(p) for p in probabilities)))
        return classes[order], float(probabilities[order]), per_label


def load_model(path: Path) -> Model:
    """
    Load the saved bundle and refuse it if its features no longer match.

    gesture_features.py changing is the one failure that would silently ruin
    the demo: the model would still predict, just from the wrong numbers.
    """
    if not path.exists():
        raise SystemExit(f"no model at {path}\nTrain one first:\n"
                         f"  python train_gestures.py")
    bundle = joblib.load(path)

    saved = list(bundle.get("feature_names", []))
    current = list(gf.feature_names())
    if saved != current:
        raise SystemExit(
            f"{path} was trained on {len(saved)} features but "
            f"gesture_features.py now builds {len(current)}.\n"
            "Retrain before demoing:\n  python train_gestures.py")

    return Model(
        estimator=bundle["model"],
        name=str(bundle.get("model_name", "model")),
        labels=list(bundle.get("labels", [])),
        accuracy=float(bundle.get("cv_accuracy", float("nan"))),
        scheme=str(bundle.get("cv_scheme", "unknown")),
        caveat=str(bundle.get("cv_caveat", "")),
        trained_at=str(bundle.get("trained_at", "unknown")),
        participants=list(bundle.get("participants", [])),
        compared=dict(bundle.get("compared_models", {})),
        worst_fold=bundle.get("cv_worst_fold"),
        winner=str(bundle.get("comparison_winner", "")),
        winner_accuracy=float(bundle.get("comparison_winner_accuracy", 0.0)),
        chosen_by_hand=bool(bundle.get("chosen_by_hand", False)),
    )


# ===========================================================================
# One classified gesture
# ===========================================================================

@dataclass
class Cast:
    """A finished gesture, its prediction, and what the user said it was."""

    n: int
    label: str
    confidence: float
    probabilities: dict[str, float]
    duration_s: float
    peak_dps: float
    accepted: bool
    reason: str
    path: np.ndarray
    at: float
    truth: str | None = None

    @property
    def spell(self) -> str:
        """The spell this prediction casts, or "none"."""
        return SPELL_BY_LABEL.get(self.label, "none")


# ===========================================================================
# The live app
# ===========================================================================

class LivePredictor:
    """Owns the window, the IMU stream, the segmenter and the running tally."""

    def __init__(self, model: Model, source: imu.ImuSource,
                 min_confidence: float, log_dir: Path | None,
                 camera: cv2.VideoCapture | None,
                 bridge: UnityBridge | None = None,
                 wand_axes: str = "y,x,-z", player_id: int = 0) -> None:
        self.model = model
        self.source = source
        self.min_confidence = min_confidence
        self.log_dir = log_dir
        self.camera = camera
        self.bridge = bridge
        self.player_id = player_id
        self.window_name = (f"CEG5205 P{player_id} - live spell recognition"
                            if player_id else WINDOW_NAME)
        self.orientation = WandOrientation(wand_axes)

        self.segmenter = gf.GestureSegmenter(
            start_dps=START_DPS, stop_dps=STOP_DPS, stop_hold_s=STOP_HOLD_S,
            preroll_s=PREROLL_S, min_duration_s=MIN_DURATION_S,
            max_duration_s=MAX_DURATION_S)
        self.tracer = imu.GestureTracer()

        self.casts: list[Cast] = []
        self.last: Cast | None = None
        self.epoch = source.epoch
        self.live_mag = 0.0
        self.message = ""

    # -- the stream ---------------------------------------------------------

    def pump(self) -> None:
        """Drain whatever the reader thread has, and classify completed moves."""
        if self.bridge is not None:
            self.bridge.pump()
        if self.source.epoch != self.epoch:
            # The USB cable was replugged: the bias is stale, so start clean.
            self.epoch = self.source.epoch
            self.segmenter.reset()
            self.tracer.reset()
            self.orientation.reset()
            self.message = "reconnected - restart to recalibrate while still"

        samples = self.source.drain()
        if not samples:
            return
        self.live_mag = samples[-1].gyro_mag
        self.tracer.feed(samples)
        for sample in samples:
            self.orientation.update(sample)
        if self.bridge is not None:
            self.bridge.pose(self.orientation.unity_quaternion(),
                             self.orientation.origin, self.player_id)

        for segment in self.segmenter.push_many(samples):
            self.classify(segment)

    def classify(self, segment: gf.Segment) -> None:
        """Turn one segment into a :class:`Cast` and record it."""
        t_s, values, _ = imu.samples_to_arrays(segment.samples)
        peak = float(np.linalg.norm(values[:, 3:6], axis=1).max()) if len(t_s) else 0.0
        path = imu.GestureTracer.path_from_arrays(t_s, values[:, 3:6]) \
            if len(t_s) > 1 else np.zeros((0, 2))

        if not segment.accepted:
            cast = Cast(n=len(self.casts) + 1, label="", confidence=0.0,
                        probabilities={}, duration_s=segment.duration_s,
                        peak_dps=peak, accepted=False, reason=segment.reason,
                        path=path, at=time.monotonic())
        else:
            features = gf.features_from_samples(segment.samples)
            label, confidence, probabilities = self.model.predict(features)
            cast = Cast(n=len(self.casts) + 1, label=label, confidence=confidence,
                        probabilities=probabilities, duration_s=segment.duration_s,
                        peak_dps=peak, accepted=True, reason="",
                        path=path, at=time.monotonic())

        self.casts.append(cast)
        self.last = cast
        print(f"Gesture: {cast.label or cast.reason} ({cast.confidence:.0%})", flush=True)
        if self.bridge is not None:
            self.bridge.cast(cast, self.min_confidence, self.player_id)
        self.tracer.reset()
        if self.log_dir is not None:
            self.log_segment(cast, t_s, values)

    # -- logging ------------------------------------------------------------

    def log_segment(self, cast: Cast, t_s: np.ndarray, values: np.ndarray) -> None:
        """Save the raw segment and its trace, named so it cannot clash."""
        self.log_dir.mkdir(parents=True, exist_ok=True)
        stem = f"seg_{cast.n:03d}"
        frame = pd.DataFrame(values, columns=list(imu.CHANNELS))
        frame.insert(0, "t_s", t_s)
        frame.to_csv(self.log_dir / f"{stem}.csv", index=False, float_format="%.5f")
        if cast.path.shape[0] > 1:
            cv2.imwrite(str(self.log_dir / f"{stem}.png"),
                        imu.trace_image(cast.path))

    def write_log(self) -> None:
        """Write one row per cast, including any truth the user tagged."""
        if self.log_dir is None or not self.casts:
            return
        rows = [{
            "n": c.n,
            "predicted": c.label,
            "spell": c.spell if c.accepted else "",
            "confidence": round(c.confidence, 4),
            "above_threshold": bool(c.accepted and c.confidence >= self.min_confidence),
            "truth": c.truth or "",
            "duration_s": round(c.duration_s, 3),
            "peak_dps": round(c.peak_dps, 1),
            "accepted": c.accepted,
            "reason": c.reason,
        } for c in self.casts]
        path = self.log_dir / "live_log.csv"
        pd.DataFrame(rows).to_csv(path, index=False)
        print(f"log -> {path}")

    # -- tagging and the summary -------------------------------------------

    def tag_last(self, label: str) -> None:
        """Record what the previous gesture actually was."""
        if self.last is None:
            self.message = "nothing to tag yet"
            return
        self.last.truth = label
        self.message = f"#{self.last.n} tagged {label}"

    def report(self) -> None:
        """Print the run summary, and real accuracy if anything was tagged."""
        total = len(self.casts)
        usable = [c for c in self.casts if c.accepted]
        confident = [c for c in usable if c.confidence >= self.min_confidence]
        print(f"\n{'=' * 60}\nLIVE RUN SUMMARY\n{'=' * 60}")
        print(f"  model          : {self.model.name}")
        print(f"  stream         : {self.source.stats().summary()}")
        print(f"  movements seen : {total}")
        print(f"  classified     : {len(usable)}")
        print(f"  above {self.min_confidence:.2f}    : {len(confident)}")
        if usable:
            print(f"  mean confidence: "
                  f"{np.mean([c.confidence for c in usable]):.1%}")
            counts = Counter(c.label for c in usable)
            print("  predictions    : "
                  + ", ".join(f"{k}={v}" for k, v in counts.most_common()))

        tagged = [c for c in self.casts if c.truth]
        if tagged:
            correct = sum(1 for c in tagged if c.label == c.truth)
            print(f"\n  tagged         : {len(tagged)}")
            print(f"  LIVE ACCURACY  : {correct}/{len(tagged)} "
                  f"= {correct / len(tagged):.1%}")
            for c in tagged:
                if c.label != c.truth:
                    print(f"    #{c.n}: said {c.label or '-'} "
                          f"({c.confidence:.0%}), was {c.truth}")
        else:
            print("\n  nothing tagged - press 1-5 after a gesture to score the run")
        self.write_log()

    # -- drawing ------------------------------------------------------------

    def background(self) -> np.ndarray:
        """The canvas for this frame: webcam if available, plain dark if not."""
        if self.camera is not None:
            ok, frame = self.camera.read()
            if ok:
                return fit_camera_frame(cv2.flip(frame, 1))
        return blank_canvas()

    def render(self) -> np.ndarray:
        """Draw one frame: status, the live trace, and the latest result."""
        canvas = self.background()

        title = (f"PLAYER {self.player_id} - LIVE SPELL RECOGNITION"
                 if self.player_id else "LIVE SPELL RECOGNITION")
        put_text(canvas, title, (50, 60), 0.9, COL_TEXT)
        put_text(canvas, f"{self.model.name}   {self.model.accuracy:.0%} "
                         f"({self.model.scheme})", (50, 95), 0.5, COL_DIM, 1)

        if self.segmenter.recording:
            put_text(canvas, "RECORDING", (50, 140), 0.7, COL_REC)
            cv2.circle(canvas, (CANVAS_W - 60, 60), 18, COL_REC, -1, cv2.LINE_AA)
        else:
            put_text(canvas, f"ready   {self.live_mag:5.0f} deg/s",
                     (50, 140), 0.6, COL_DIM, 1)

        if self.bridge is not None:
            connected = self.bridge.was_connected
            status = self.bridge.game_status or "waiting for game"
            put_text(canvas, f"QUEST: {status if connected else 'DISCONNECTED'}",
                     (50, 185), .55, COL_OK if connected else COL_WARN, 1)
            put_text(canvas, f"WAND: {self.bridge.pose_count} poses received  "
                     f"{self.bridge.pose_angle:.0f} deg from neutral", (50, 215), .45, COL_DIM, 1)

        imu.draw_path(canvas, self.tracer.path, TRACE_BOX)
        self.draw_result(canvas)

        put_text(canvas, "R center wand    1-5 tag    C clear    Q quit",
                 (50, CANVAS_H - 70), 0.5, COL_DIM, 1)
        put_text(canvas, self.message or self.source.stats().summary(),
                 (50, CANVAS_H - 30), 0.45, COL_DIM, 1)
        return canvas

    def draw_result(self, canvas: np.ndarray) -> None:
        """Draw the most recent prediction, or why it was thrown away."""
        cast = self.last
        if cast is None:
            put_text(canvas, "wave the wand", (50, 260), 0.8, COL_DIM, 1)
            return

        fading = time.monotonic() - cast.at > RESULT_HOLD_S
        if not cast.accepted:
            put_text(canvas, f"ignored ({cast.reason})", (50, 260), 0.7, COL_WARN)
            put_text(canvas, f"{cast.duration_s:.2f}s   peak {cast.peak_dps:.0f} deg/s",
                     (50, 300), 0.5, COL_DIM, 1)
            return

        if cast.confidence < self.min_confidence:
            put_text(canvas, "unsure", (50, 260), 0.9, COL_WARN)
            put_text(canvas, f"best guess {cast.label} ({cast.confidence:.0%})",
                     (50, 300), 0.55, COL_DIM, 1)
        else:
            colour = COL_DIM if fading else COL_OK
            put_text(canvas, cast.spell.upper(), (50, 270), 1.6, colour, 3)
            put_text(canvas, f"{cast.label}   {cast.confidence:.0%}",
                     (50, 315), 0.6, COL_ACCENT if not fading else COL_DIM, 1)

        put_text(canvas, f"#{cast.n}   {cast.duration_s:.2f}s   "
                         f"peak {cast.peak_dps:.0f} deg/s"
                         + (f"   tagged {cast.truth}" if cast.truth else ""),
                 (50, 350), 0.45, COL_DIM, 1)
        self.draw_probabilities(canvas, cast)

    def draw_probabilities(self, canvas: np.ndarray, cast: Cast) -> None:
        """A small bar per class, so the demo shows what it nearly said."""
        x, y, width = 50, 400, 300
        for label in (g.label for g in GESTURES):
            probability = cast.probabilities.get(label, 0.0)
            filled = int(width * probability)
            cv2.rectangle(canvas, (x, y - 11), (x + width, y + 3), (60, 58, 56), -1)
            if filled > 0:
                colour = COL_OK if label == cast.label else (120, 118, 116)
                cv2.rectangle(canvas, (x, y - 11), (x + filled, y + 3), colour, -1)
            put_text(canvas, f"{label:17s}{probability:4.0%}",
                     (x + width + 14, y), 0.42, COL_DIM, 1)
            y += 26

    # -- the loop -----------------------------------------------------------

    def run(self, headless: bool = False, duration: float | None = None) -> None:
        """Show the window until the user quits."""
        if not headless:
            cv2.namedWindow(self.window_name, cv2.WINDOW_AUTOSIZE)
        started = time.monotonic()
        while duration is None or time.monotonic() - started < duration:
            self.pump()
            if headless:
                time.sleep(.02)
                continue
            cv2.imshow(self.window_name, self.render())
            key = cv2.waitKey(20) & 0xFF
            if key in KEYS_QUIT:
                break
            if key in TAG_KEYS:
                self.tag_last(TAG_KEYS[key])
            elif key == ord("r"):
                self.orientation.reset()
                self.message = "wand centered - point forward when pressing R"
            elif key == KEY_CLEAR:
                self.tracer.reset()
                self.last = None
        cv2.destroyAllWindows()


# ===========================================================================
# Entry point
# ===========================================================================

def open_camera(enabled: bool) -> cv2.VideoCapture | None:
    """Open the webcam, or return None (and say so) if it is unavailable."""
    if not enabled:
        return None
    capture = cv2.VideoCapture(0)
    if not capture.isOpened():
        print("camera: could not open device 0 - continuing without it "
              "(macOS: System Settings > Privacy & Security > Camera)")
        return None
    return capture


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Command-line options."""
    parser = argparse.ArgumentParser(
        description="Live wand gesture recognition using the trained model.")
    parser.add_argument("--model", default=str(DEFAULT_MODEL_PATH),
                        help="model bundle from train_gestures.py")
    parser.add_argument("--camera", action="store_true",
                        help="show the webcam behind the overlay")
    parser.add_argument("--min-confidence", type=float,
                        default=DEFAULT_MIN_CONFIDENCE,
                        help=f"cast nothing below this (default "
                             f"{DEFAULT_MIN_CONFIDENCE})")
    parser.add_argument("--log-dir", default=str(DEFAULT_LOG_DIR),
                        help="where to save each segment (default data/_live/)")
    parser.add_argument("--no-log", action="store_true",
                        help="do not save segments to disk")
    parser.add_argument("--port", help="serial port, e.g. /dev/cu.usbmodem14101")
    parser.add_argument("--fake", action="store_true",
                        help="synthetic IMU data, no hardware needed")
    parser.add_argument("--unity-host", help="Quest Wi-Fi IP (or 127.0.0.1 for editor)")
    parser.add_argument("--wand-axes", default="y,x,-z", help="IMU rotation axes mapped to Unity x,y,z")
    parser.add_argument("--unity-port", type=int, default=5055)
    parser.add_argument("--headless", action="store_true", help="run without an overlay window")
    parser.add_argument("--duration", type=float, help="stop after this many seconds (diagnostics)")
    args = parser.parse_args(argv)
    try:
        WandOrientation(args.wand_axes)
    except ValueError as exc:
        parser.error(str(exc))
    if not 0 <= args.min_confidence <= 1:
        parser.error("--min-confidence must be between 0 and 1")
    if not 1 <= args.unity_port <= 65535:
        parser.error("--unity-port must be between 1 and 65535")
    if args.duration is not None and args.duration <= 0:
        parser.error("--duration must be positive")
    if args.fake and args.unity_host:
        parser.error("--fake cannot send spells to a real game")
    return args


def main(argv: Sequence[str] | None = None) -> int:
    """Load the model, connect to the wand, and run the demo."""
    args = parse_args(argv)

    model = load_model(Path(args.model).expanduser().resolve())
    print(f"model      : {model.name}  (trained {model.trained_at})")
    print(f"validation : {model.accuracy:.1%} {model.scheme}")
    if model.caveat:
        print(f"             {model.caveat}")
    if model.worst_fold is not None:
        print(f"worst fold : {model.worst_fold:.1%} (the hardest person to "
              f"generalise to)")
    if model.chosen_by_hand:
        # Not the top scorer, and the demo should not imply otherwise.
        print(f"chosen     : by hand over {model.winner} "
              f"({model.winner_accuracy:.1%} mean)")
    elif model.compared:
        others = ", ".join(f"{k} {v:.1%}" for k, v in model.compared.items()
                           if k != model.name)
        if others:
            print(f"beat       : {others}")
    print(f"people     : {len(model.participants)} "
          f"({', '.join(model.participants)})")
    print(f"threshold  : {args.min_confidence:.2f}")

    source = imu.make_source(args.fake, args.port, on_message=lambda m: print(m))
    camera = open_camera(args.camera)
    bridge = UnityBridge(args.unity_host, args.unity_port) if args.unity_host else None
    if bridge:
        print(f"Quest target: {args.unity_host}:{args.unity_port}; waiting for app reply", flush=True)

    try:
        with source:
            if not source.wait_connected(timeout=30.0):
                print("no wand detected - is it plugged in? (or use --fake)")
                return 1
            for attempt in range(10):
                try:
                    imu.calibrate_gyro(source,
                                       on_status=lambda m: print(m, flush=True))
                    break
                except imu.CalibrationError as exc:
                    print(f"Calibration attempt {attempt + 1}/10: {exc}", flush=True)
                    print("SET THE WAND DOWN. Retrying in 2 seconds; wait for the live window.", flush=True)
                    time.sleep(2)
            else:
                print("Calibration could not finish. Set the wand down and restart.")
                return 1

            log_dir = None if args.no_log else Path(args.log_dir).expanduser()
            app = LivePredictor(model, source, args.min_confidence,
                                log_dir, camera, bridge, args.wand_axes)
            source.clear()
            try:
                app.run(args.headless, args.duration)
            except KeyboardInterrupt:
                # Ctrl-C in the terminal is a normal way to stop this, so exit
                # through the same path as Q and still print the summary.
                print()
            app.report()
    except KeyboardInterrupt:
        return 130
    finally:
        if bridge:
            bridge.close()
        if camera is not None:
            camera.release()
        cv2.destroyAllWindows()
    return 0


if __name__ == "__main__":
    sys.exit(main())
