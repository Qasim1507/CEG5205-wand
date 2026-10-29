#!/usr/bin/env python3
"""
collect.py - labelled gesture recorder for the CEG5205 mixed-reality wand.

Records one CSV per gesture sample so the ML pair can train a classifier.

Examples
--------
    python collect.py --participant p01 --reps 25
    python collect.py --participant p01 --reps 25 --camera
    python collect.py --participant p02 --reps 10 --shuffle
    python collect.py --participant test --reps 3 --fake      # no hardware
    python collect.py --participant p01 --mode timed --window 4

Keys (the OpenCV window must have focus)
----------------------------------------
    Enter / Space  keep this sample
    R              redo it
    S              skip it
    P              pause / resume
    Q or Esc       save and quit

Output layout
-------------
    data/index.csv                                   one row per kept sample
    data/<participant>/session.json                  config + calibration
    data/<participant>/<label>/<label>_001.csv       the sample itself
    data/<participant>/<label>/<label>_001.png       its 2D trace
    data/<participant>/<label>/<label>_001.mp4       only with --save-video
"""

from __future__ import annotations

import argparse
import json
import random
import sys
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Sequence

import cv2
import numpy as np
import pandas as pd

import imu_common as imu

# ===========================================================================
# CONFIGURATION - every threshold and timing lives here
# ===========================================================================

# -- motion-triggered recording ---------------------------------------------
START_DPS = 80.0          # start recording above this rotation rate (deg/s)
STOP_DPS = 40.0           # ...and stop once it stays below this
STOP_HOLD_S = 0.30        # for this long without moving
PREROLL_S = 0.20          # keep this much data from *before* the trigger
MIN_DURATION_S = 0.25     # anything shorter is rejected
MAX_DURATION_S = 3.00     # anything longer is rejected
IDLE_WINDOW_S = 2.00      # "idle" may not move, so record a fixed window

# -- timed recording --------------------------------------------------------
DEFAULT_COUNTDOWN_S = 3.0
DEFAULT_WINDOW_S = 5.0

# -- pacing -----------------------------------------------------------------
GET_READY_S = 1.2         # "Get ready" message before arming
AFTER_KEEP_S = 0.4        # short pause after a sample is kept

# -- window ------------------------------------------------------------------
WINDOW_NAME = "CEG5205 wand - data collection"
CANVAS_W, CANVAS_H = 1180, 680
FONT = cv2.FONT_HERSHEY_SIMPLEX

# -- colours (BGR) ----------------------------------------------------------
COL_BG = (24, 22, 20)
COL_TEXT = (238, 238, 238)
COL_DIM = (150, 148, 146)
COL_ACCENT = (90, 230, 255)
COL_READY = (120, 200, 255)
COL_REC = (80, 80, 255)
COL_OK = (120, 230, 140)
COL_WARN = (80, 190, 255)

# -- keys -------------------------------------------------------------------
KEYS_KEEP = (13, 10, 32)   # Enter (both codes) and Space
KEY_REDO = ord("r")
KEY_SKIP = ord("s")
KEY_PAUSE = ord("p")
KEYS_QUIT = (ord("q"), 27)


# ===========================================================================
# Gestures
# ===========================================================================

@dataclass(frozen=True)
class Gesture:
    """One gesture class: its label, the spell it casts, and how to perform it."""

    label: str
    spell: str
    motion: str
    fixed_window_s: float | None = None   # set for gestures that may not move


GESTURES: tuple[Gesture, ...] = (
    Gesture("vertical_swipe", "fire",
            "ONE fast stroke, TOP to BOTTOM"),
    Gesture("horizontal_swipe", "wind",
            "ONE fast stroke, LEFT to RIGHT"),
    Gesture("zigzag", "lightning",
            'A "Z": right, diagonal down-left, right'),
    Gesture("circle", "water",
            "ONE full CLOCKWISE circle"),
    Gesture("idle", "none",
            "Natural non-spell movement: hold still, small fidgets,\n"
            "lower your arm, look around. NEVER a spell shape.",
            fixed_window_s=IDLE_WINDOW_S),
)

INDEX_COLUMNS = ("participant", "label", "spell", "rep", "file",
                 "n_samples", "duration_s", "mode", "dropped_pct", "timestamp")


# ===========================================================================
# Drawing helpers
# ===========================================================================

def blank_canvas() -> np.ndarray:
    """A plain dark canvas, used when --camera is off."""
    return np.full((CANVAS_H, CANVAS_W, 3), COL_BG, dtype=np.uint8)


def fit_camera_frame(frame: np.ndarray) -> np.ndarray:
    """Letterbox a webcam frame onto the canvas, dimmed so text stays readable."""
    canvas = blank_canvas()
    h, w = frame.shape[:2]
    scale = min(CANVAS_W / w, CANVAS_H / h)
    resized = cv2.resize(frame, (int(w * scale), int(h * scale)))
    rh, rw = resized.shape[:2]
    y0, x0 = (CANVAS_H - rh) // 2, (CANVAS_W - rw) // 2
    canvas[y0:y0 + rh, x0:x0 + rw] = (resized * 0.65).astype(np.uint8)
    return canvas


def put_text(img: np.ndarray, text: str, org: tuple[int, int], scale: float,
             color: tuple[int, int, int], thickness: int = 2) -> None:
    """putText with a soft shadow, so it reads on top of a camera image."""
    x, y = org
    cv2.putText(img, text, (x + 2, y + 2), FONT, scale, (0, 0, 0),
                thickness + 2, cv2.LINE_AA)
    cv2.putText(img, text, (x, y), FONT, scale, color, thickness, cv2.LINE_AA)


def put_block(img: np.ndarray, text: str, org: tuple[int, int], scale: float,
              color: tuple[int, int, int], line_h: int, thickness: int = 2) -> int:
    """Draw a multi-line string; returns the y of the line after the block."""
    x, y = org
    for line in text.split("\n"):
        put_text(img, line, (x, y), scale, color, thickness)
        y += line_h
    return y


# ===========================================================================
# The application
# ===========================================================================

class CollectApp:
    """Runs the whole collection session: UI, recording, validation, saving."""

    def __init__(self, args: argparse.Namespace) -> None:
        self.args = args
        self.data_root = Path(args.data_dir)
        self.participant_dir = self.data_root / args.participant
        self.index_path = self.data_root / "index.csv"

        self.source = imu.make_source(args.fake, args.port,
                                      on_message=self._log)
        self.tracer = imu.GestureTracer()
        self.bias = np.zeros(3)
        self.epoch = -1

        # recording state
        self.preroll: list[imu.Sample] = []
        self.preroll_len = max(1, int(PREROLL_S * imu.NOMINAL_RATE_HZ))
        self.recording: list[imu.Sample] = []
        self.video_frames: list[np.ndarray] = []
        self.below_since: float | None = None
        self.state = "prompt"
        self.state_t = time.monotonic()
        self.paused_from = "prompt"
        self.message = ""
        self.review_rejected = False
        self.record_limit_s: float | None = None

        # progress
        self.counts: dict[str, int] = {}
        self.tasks: list[Gesture] = []
        self.task: Gesture | None = None
        self.kept = 0
        self.skipped = 0

        self.camera: cv2.VideoCapture | None = None
        self.last_frame: np.ndarray | None = None

    # -- small utilities ----------------------------------------------------

    @staticmethod
    def _log(text: str) -> None:
        print(f"[imu] {text}", flush=True)

    def _set_state(self, state: str, message: str = "") -> None:
        self.state = state
        self.state_t = time.monotonic()
        self.message = message

    def begin_prompt(self, message: str = "") -> None:
        """Show "Get ready" for the current task and start the trigger cycle."""
        self._request_fake_gesture()
        self._set_state("prompt", message)

    def _request_fake_gesture(self) -> None:
        """
        In --fake mode, ask the generator for the gesture we are prompting for.

        With real hardware the source has no such method and nothing happens;
        this only makes a dry run produce correctly-shaped traces.
        """
        ask = getattr(self.source, "request_gesture", None)
        if callable(ask) and self.task is not None:
            ask(self.task.label)

    @property
    def _elapsed(self) -> float:
        return time.monotonic() - self.state_t

    def sample_dir(self, label: str) -> Path:
        return self.participant_dir / label

    # -- resume -------------------------------------------------------------

    def scan_existing(self) -> None:
        """Count the samples already on disk, so a session can be resumed."""
        for gesture in GESTURES:
            folder = self.sample_dir(gesture.label)
            existing = sorted(folder.glob(f"{gesture.label}_*.csv")) if folder.is_dir() else []
            self.counts[gesture.label] = len(existing)
        total = sum(self.counts.values())
        if total:
            print(f"Resuming {self.args.participant}: "
                  + ", ".join(f"{g.label}={self.counts[g.label]}" for g in GESTURES))

    def build_tasks(self) -> None:
        """Queue the remaining repetitions, grouped by gesture or shuffled."""
        self.tasks = []
        for gesture in GESTURES:
            remaining = max(0, self.args.reps - self.counts[gesture.label])
            self.tasks += [gesture] * remaining
        if self.args.shuffle:
            random.shuffle(self.tasks)

    def next_index(self, label: str) -> int:
        """The next free file number for `label` (never overwrites a file)."""
        index = self.counts[label] + 1
        while (self.sample_dir(label) / f"{label}_{index:03d}.csv").exists():
            index += 1
        return index

    # -- connection ---------------------------------------------------------

    def connect_and_calibrate(self) -> bool:
        """Wait for the link, then calibrate the gyro. False if the user quits."""
        self._set_state("connecting")
        while True:
            self._draw_and_show("Connecting to the wand...",
                                "waiting for the Arduino #READY header")
            key = cv2.waitKey(30) & 0xFF
            if key in KEYS_QUIT:
                return False
            if self.source.stats().connected:
                break
        print(f"IMU header: {self.source.header}")

        while True:
            self._draw_and_show("Calibration",
                                f"Put the wand down and hold it STILL "
                                f"({imu.CAL_DURATION_S:.0f} s)")
            cv2.waitKey(400)
            try:
                self.bias = imu.calibrate_gyro(
                    self.source, on_status=lambda m: print(f"[cal] {m}", flush=True))
                self.epoch = self.source.epoch
                return True
            except imu.CalibrationError as exc:
                print(f"[cal] {exc}", flush=True)
                for _ in range(60):
                    self._draw_and_show("Calibration failed",
                                        "The wand moved. Hold still.\n"
                                        "Any key = retry,  Q = quit")
                    key = cv2.waitKey(30) & 0xFF
                    if key in KEYS_QUIT:
                        return False
                    if key != 255:
                        break

    def check_link(self) -> bool:
        """Handle a pulled USB cable. False if the user quits while reconnecting."""
        if self.source.epoch == self.epoch and self.source.stats().connected:
            return True
        print("[imu] link changed - reconnecting and recalibrating", flush=True)
        self.recording = []
        self.video_frames = []
        if not self.connect_and_calibrate():
            return False
        self.begin_prompt("reconnected")
        return True

    # -- camera -------------------------------------------------------------

    def open_camera(self) -> None:
        if not self.args.camera:
            return
        cap = cv2.VideoCapture(0)
        if not cap.isOpened():
            print("Could not open the webcam - continuing without it.\n"
                  "(macOS asks for camera permission for your terminal the "
                  "first time; allow it in System Settings > Privacy & "
                  "Security > Camera.)")
            cap.release()
            return
        self.camera = cap

    def read_frame(self) -> np.ndarray:
        """The background for this UI frame: webcam if available, else dark."""
        if self.camera is not None:
            ok, frame = self.camera.read()
            if ok:
                frame = cv2.flip(frame, 1)          # mirror: feels natural
                self.last_frame = frame
                return fit_camera_frame(frame)
        return blank_canvas()

    # ===================================================================
    # Main loop
    # ===================================================================

    def run(self) -> int:
        self.participant_dir.mkdir(parents=True, exist_ok=True)
        for gesture in GESTURES:
            self.sample_dir(gesture.label).mkdir(parents=True, exist_ok=True)
        self.scan_existing()
        self.build_tasks()
        if not self.tasks:
            print(f"{self.args.participant} already has {self.args.reps} "
                  "reps of every gesture. Nothing to do.")
            return 0

        self.source.start()
        cv2.namedWindow(WINDOW_NAME, cv2.WINDOW_AUTOSIZE)
        self.open_camera()

        try:
            if not self.connect_and_calibrate():
                return 1
            self.write_session_json()
            self.task = self.tasks.pop(0)
            self.begin_prompt()

            while True:
                if not self.check_link():
                    break
                self.pump_samples()
                self.update_state()
                frame = self.render()
                cv2.imshow(WINDOW_NAME, frame)
                key = cv2.waitKey(10) & 0xFF
                if self.handle_key(key) == "quit":
                    break
                if self.state == "done":
                    self._draw_and_show("All done!", self.summary_text())
                    cv2.waitKey(1500)
                    break
        finally:
            self.write_session_json()
            self.source.stop()
            if self.camera is not None:
                self.camera.release()
            cv2.destroyAllWindows()

        print("\n" + self.summary_text().replace("\n", "  "))
        print(f"Index: {self.index_path}")
        return 0

    # -- sample flow --------------------------------------------------------

    def pump_samples(self) -> None:
        """Move queued samples into the pre-roll buffer / the recording."""
        new = self.source.drain(limit=2000)
        if not new:
            return
        if self.state == "recording":
            self.recording += new
            self.tracer.feed(new)
        else:
            self.preroll += new
            self.preroll = self.preroll[-self.preroll_len:]
            if self.state == "armed":
                self.tracer.feed(new)

    def arm(self) -> None:
        """Get ready to catch a motion-triggered gesture."""
        self.tracer.reset()
        self.below_since = None
        self._set_state("armed")

    def start_recording(self, limit_s: float | None, with_preroll: bool) -> None:
        """Begin a recording, optionally keeping the pre-roll samples."""
        self.recording = list(self.preroll) if with_preroll else []
        self.video_frames = []
        self.below_since = None
        self.record_limit_s = limit_s
        self.tracer.reset()
        self.tracer.feed(self.recording)
        self._set_state("recording")

    def update_state(self) -> None:
        """One step of the prompt -> record -> review state machine."""
        assert self.task is not None
        gesture = self.task

        if self.state == "prompt":
            if self._elapsed >= GET_READY_S:
                if self.args.mode == "timed":
                    self._set_state("countdown")
                elif gesture.fixed_window_s is not None:
                    self.start_recording(gesture.fixed_window_s, with_preroll=False)
                else:
                    self.arm()

        elif self.state == "countdown":
            if self._elapsed >= self.args.countdown:
                self.start_recording(self.args.window, with_preroll=False)

        elif self.state == "armed":
            # The pre-roll buffer always holds the newest samples, so the
            # trigger test and the 0.2 s of pre-roll data come from one place.
            if self.preroll and self.preroll[-1].gyro_mag > START_DPS:
                self.start_recording(None, with_preroll=True)

        elif self.state == "recording":
            self._update_recording()

    def _update_recording(self) -> None:
        """Decide when the current recording should stop."""
        if self.args.camera and self.args.save_video and self.last_frame is not None:
            self.video_frames.append(self.last_frame.copy())

        if self.record_limit_s is not None:
            if self._elapsed >= self.record_limit_s:
                self.finish_recording()
            return

        # Motion-triggered: stop once the wand has been quiet long enough.
        now = time.monotonic()
        quiet = bool(self.recording) and self.recording[-1].gyro_mag < STOP_DPS
        if quiet:
            if self.below_since is None:
                self.below_since = now
            elif now - self.below_since >= STOP_HOLD_S:
                self.finish_recording()
                return
        else:
            self.below_since = None

        if self._elapsed >= MAX_DURATION_S + STOP_HOLD_S:
            self.finish_recording()

    def finish_recording(self) -> None:
        """Validate the captured samples and move on to the review screen."""
        samples = self.recording
        duration = self.sample_duration(samples)
        self.review_rejected = False

        if len(samples) < 5:
            self.review_rejected = True
            self._set_state("review", "Almost no data captured - press R to redo")
            return

        if self.record_limit_s is None:          # only motion mode is validated
            if duration < MIN_DURATION_S:
                self.review_rejected = True
                self._set_state("review",
                                f"Too short ({duration:.2f} s < "
                                f"{MIN_DURATION_S:.2f} s) - press R to redo")
                return
            if duration > MAX_DURATION_S:
                self.review_rejected = True
                self._set_state("review",
                                f"Too long ({duration:.2f} s > "
                                f"{MAX_DURATION_S:.2f} s) - press R to redo")
                return

        self._set_state("review",
                        f"{duration:.2f} s, {len(samples)} samples, "
                        f"{imu.sample_dropped_pct(samples):.1f}% dropped")

    @staticmethod
    def sample_duration(samples: Sequence[imu.Sample]) -> float:
        """Length of a recording in seconds, from the Arduino timestamps."""
        if len(samples) < 2:
            return 0.0
        return (samples[-1].t_ms - samples[0].t_ms) / 1000.0

    # -- keys ---------------------------------------------------------------

    def handle_key(self, key: int) -> str | None:
        """Act on a keypress. Returns "quit" when the session should end."""
        if key == 255:                      # no key pressed
            return None
        if key in KEYS_QUIT:
            return "quit"
        if key == KEY_PAUSE:
            if self.state == "paused":
                self._set_state(self.paused_from)
            else:
                self.paused_from = "prompt"
                self._set_state("paused")
            return None
        if self.state != "review":
            return None

        if key in KEYS_KEEP:
            if self.review_rejected:
                self.message = "Rejected sample - press R to redo or S to skip"
                return None
            self.save_sample()
            if not self.hold_frame(AFTER_KEEP_S):
                return "quit"
            self.advance_task()
        elif key == KEY_REDO:
            self.begin_prompt("redo")
        elif key == KEY_SKIP:
            self.skipped += 1
            self.advance_task()
        return None

    def hold_frame(self, seconds: float) -> bool:
        """
        Keep the current frame on screen for a moment (e.g. after saving).

        Waits in small slices rather than one long blocking call, so a Q
        pressed during the pause is still noticed. False means "quit".
        """
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            if (cv2.waitKey(20) & 0xFF) in KEYS_QUIT:
                return False
        return True

    def advance_task(self) -> None:
        """Take the next queued repetition, or finish the session."""
        if not self.tasks:
            self.task = None
            self._set_state("done")
            return
        self.task = self.tasks.pop(0)
        self.recording = []
        self.video_frames = []
        self.begin_prompt()

    # -- saving -------------------------------------------------------------

    def save_sample(self) -> None:
        """Write the CSV, the trace PNG, the optional clip, and the index row."""
        assert self.task is not None
        gesture = self.task
        samples = self.recording
        index = self.next_index(gesture.label)
        stem = f"{gesture.label}_{index:03d}"
        folder = self.sample_dir(gesture.label)

        t_s, values, seq = imu.samples_to_arrays(samples)
        frame = pd.DataFrame(values, columns=list(imu.CHANNELS))
        frame.insert(0, "t_s", t_s)
        frame["seq"] = seq
        csv_path = folder / f"{stem}.csv"
        frame.to_csv(csv_path, index=False, float_format="%.5f")

        path = imu.GestureTracer.path_from_arrays(t_s, values[:, 3:6])
        cv2.imwrite(str(folder / f"{stem}.png"),
                    imu.trace_image(path, title=f"{gesture.label} #{index}"))

        if self.args.save_video and self.video_frames:
            self.write_video(folder / f"{stem}.mp4")

        duration = self.sample_duration(samples)
        row = {
            "participant": self.args.participant,
            "label": gesture.label,
            "spell": gesture.spell,
            "rep": index,
            "file": str(csv_path.relative_to(self.data_root)),
            "n_samples": len(samples),
            "duration_s": round(duration, 3),
            "mode": self.args.mode,
            "dropped_pct": round(imu.sample_dropped_pct(samples), 2),
            "timestamp": datetime.now().isoformat(timespec="seconds"),
        }
        self.append_index_row(row)

        self.counts[gesture.label] += 1
        self.kept += 1
        print(f"saved {csv_path}  ({len(samples)} samples, {duration:.2f} s)",
              flush=True)

    def append_index_row(self, row: dict[str, object]) -> None:
        """Append one row to data/index.csv, writing the header if it is new."""
        frame = pd.DataFrame([row], columns=list(INDEX_COLUMNS))
        header = not self.index_path.exists()
        frame.to_csv(self.index_path, mode="a", header=header, index=False)

    def write_video(self, path: Path) -> None:
        """Save the webcam frames of this sample as a short clip."""
        height, width = self.video_frames[0].shape[:2]
        fourcc = cv2.VideoWriter_fourcc(*"mp4v")
        fps = max(5.0, len(self.video_frames) / max(0.1, self.sample_duration(self.recording)))
        writer = cv2.VideoWriter(str(path), fourcc, fps, (width, height))
        for frame in self.video_frames:
            writer.write(frame)
        writer.release()

    def write_session_json(self) -> None:
        """Record the configuration and calibration of this run (appends)."""
        path = self.participant_dir / "session.json"
        entry = {
            "date": datetime.now().isoformat(timespec="seconds"),
            "participant": self.args.participant,
            "firmware_header": self.source.header,
            "gyro_bias_dps": [round(float(v), 4) for v in self.bias],
            "fake": bool(self.args.fake),
            "config": {
                "reps": self.args.reps,
                "mode": self.args.mode,
                "window_s": self.args.window,
                "countdown_s": self.args.countdown,
                "shuffle": bool(self.args.shuffle),
                "camera": bool(self.args.camera),
                "save_video": bool(self.args.save_video),
                "start_dps": START_DPS,
                "stop_dps": STOP_DPS,
                "stop_hold_s": STOP_HOLD_S,
                "preroll_s": PREROLL_S,
                "min_duration_s": MIN_DURATION_S,
                "max_duration_s": MAX_DURATION_S,
                "idle_window_s": IDLE_WINDOW_S,
                "nominal_rate_hz": imu.NOMINAL_RATE_HZ,
            },
            "counts": dict(self.counts),
            "kept_this_run": self.kept,
            "skipped_this_run": self.skipped,
        }
        sessions: list[dict[str, object]] = []
        if path.exists():
            try:
                existing = json.loads(path.read_text())
                sessions = existing.get("sessions", [])
            except (json.JSONDecodeError, OSError):
                sessions = []
        if sessions and sessions[-1].get("date") == entry["date"]:
            sessions[-1] = entry
        else:
            sessions.append(entry)
        path.write_text(json.dumps({"sessions": sessions}, indent=2))

    # -- rendering ----------------------------------------------------------

    def summary_text(self) -> str:
        counts = ", ".join(f"{g.label} {self.counts[g.label]}" for g in GESTURES)
        return (f"kept {self.kept} this run, skipped {self.skipped}\n"
                f"totals: {counts}")

    def _draw_and_show(self, title: str, body: str) -> None:
        """Draw a simple full-screen message (used while connecting)."""
        canvas = self.read_frame()
        put_text(canvas, title, (50, 140), 1.4, COL_ACCENT, 3)
        put_block(canvas, body, (50, 210), 0.8, COL_TEXT, 40)
        put_text(canvas, self.source.stats().summary(), (50, CANVAS_H - 30),
                 0.55, COL_DIM, 1)
        cv2.imshow(WINDOW_NAME, canvas)

    def render(self) -> np.ndarray:
        """Build the UI frame for the current state."""
        canvas = self.read_frame()
        if self.state == "review":
            self.render_review(canvas)
        else:
            self.render_prompt(canvas)
        self.render_status(canvas)
        return canvas

    def render_prompt(self, canvas: np.ndarray) -> None:
        """Prompt / countdown / armed / recording screen."""
        gesture = self.task
        if gesture is None:
            return
        rep = self.counts[gesture.label] + 1
        done = sum(self.counts.values())
        total = self.args.reps * len(GESTURES)

        put_text(canvas, f"{gesture.spell.upper()}: "
                         f"{gesture.label.replace('_', ' ')}",
                 (40, 70), 1.3, COL_ACCENT, 3)
        put_text(canvas, f"{rep} / {self.args.reps}", (40, 115), 0.9, COL_TEXT, 2)
        put_block(canvas, gesture.motion, (40, 165), 0.75, COL_TEXT, 36)

        if self.state == "paused":
            put_text(canvas, "PAUSED", (40, 330), 1.6, COL_WARN, 3)
            put_text(canvas, "press P to resume, Q to save and quit",
                     (40, 375), 0.7, COL_TEXT, 2)
        elif self.state == "prompt":
            put_text(canvas, "Get ready...", (40, 330), 1.3, COL_READY, 3)
        elif self.state == "countdown":
            left = max(0.0, self.args.countdown - self._elapsed)
            put_text(canvas, f"{int(left) + 1}", (40, 380), 3.5, COL_READY, 6)
        elif self.state == "armed":
            put_text(canvas, "GO - perform the gesture", (40, 330), 1.2,
                     COL_READY, 3)
            put_text(canvas, f"waiting for motion above {START_DPS:.0f} deg/s",
                     (40, 372), 0.6, COL_DIM, 1)
        elif self.state == "recording":
            cv2.circle(canvas, (CANVAS_W - 60, 60), 18, COL_REC, -1, cv2.LINE_AA)
            put_text(canvas, "REC", (CANVAS_W - 160, 70), 0.9, COL_REC, 2)
            shown = self._elapsed
            if self.record_limit_s is not None:
                shown = min(shown, self.record_limit_s)
                put_text(canvas, f"{self.record_limit_s - shown:4.1f} s left",
                         (40, 330), 1.0, COL_REC, 2)
            else:
                put_text(canvas, f"recording  {shown:4.2f} s", (40, 330),
                         1.0, COL_REC, 2)

        # The live "air mouse" trace, drawn on top of the camera view.
        if self.state in ("armed", "recording"):
            draw_box = (CANVAS_W - 470, 120, 430, 430)
            cv2.rectangle(canvas, (draw_box[0], draw_box[1]),
                          (draw_box[0] + draw_box[2], draw_box[1] + draw_box[3]),
                          (70, 68, 66), 1)
            imu.draw_path(canvas, self.tracer.path, draw_box, COL_ACCENT, 2)

        done_text = f"total progress  {done} / {total}"
        put_text(canvas, done_text, (40, CANVAS_H - 70), 0.7, COL_DIM, 2)
        if self.message:
            put_text(canvas, self.message, (40, CANVAS_H - 100), 0.6, COL_WARN, 1)

    def render_review(self, canvas: np.ndarray) -> None:
        """Review screen: the 2D trace plus the 6-channel mini plot."""
        gesture = self.task
        if gesture is None:
            return
        canvas[:] = COL_BG                      # review is easier on a plain bg
        t_s, values, _ = imu.samples_to_arrays(self.recording)
        path = imu.GestureTracer.path_from_arrays(t_s, values[:, 3:6])

        put_text(canvas, f"Review - {gesture.spell.upper()}: "
                         f"{gesture.label.replace('_', ' ')}",
                 (40, 60), 1.1, COL_ACCENT, 3)
        colour = COL_WARN if self.review_rejected else COL_OK
        put_text(canvas, self.message, (40, 100), 0.65, colour, 2)

        trace = imu.trace_image(path, 420, 420, "2D trace (gyro integrated)")
        canvas[130:550, 40:460] = trace
        plot = imu.channels_image(values, 600, 420, "6 channels")
        canvas[130:550, 500:1100] = plot

        hint = ("Enter / Space = keep    R = redo    S = skip    "
                "P = pause    Q = save and quit")
        put_text(canvas, hint, (40, CANVAS_H - 60), 0.62, COL_TEXT, 2)

    def render_status(self, canvas: np.ndarray) -> None:
        stats = self.source.stats()
        colour = COL_DIM if stats.connected else COL_REC
        put_text(canvas, stats.summary(), (40, CANVAS_H - 25), 0.55, colour, 1)


# ===========================================================================
# Command line
# ===========================================================================

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Record labelled wand gestures from the MPU-6050.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter)
    parser.add_argument("--participant", required=True,
                        help="participant id, e.g. p01 (one folder per person)")
    parser.add_argument("--reps", type=int, default=25,
                        help="target repetitions per gesture")
    parser.add_argument("--mode", choices=("motion", "timed"), default="motion",
                        help="motion-triggered or fixed-window recording")
    parser.add_argument("--window", type=float, default=DEFAULT_WINDOW_S,
                        help="recording length in seconds for --mode timed")
    parser.add_argument("--countdown", type=float, default=DEFAULT_COUNTDOWN_S,
                        help="countdown before a timed recording")
    parser.add_argument("--camera", action="store_true",
                        help="show the Mac webcam behind the prompts")
    parser.add_argument("--save-video", action="store_true",
                        help="also save a short webcam clip per sample")
    parser.add_argument("--shuffle", action="store_true",
                        help="mix the gestures instead of doing one at a time")
    parser.add_argument("--port", help="serial port, e.g. /dev/cu.usbmodem14101")
    parser.add_argument("--fake", action="store_true",
                        help="synthetic IMU data, no hardware needed")
    parser.add_argument("--data-dir", default=str(Path(__file__).resolve().parent.parent / "data"),
                        help="where the dataset is stored")
    args = parser.parse_args(argv)
    if args.save_video and not args.camera:
        parser.error("--save-video needs --camera")
    if args.reps < 1:
        parser.error("--reps must be at least 1")
    return args


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    return CollectApp(args).run()


if __name__ == "__main__":
    sys.exit(main())
