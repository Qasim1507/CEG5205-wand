"""
imu_common.py - shared IMU plumbing for the CEG5205 mixed-reality wand.

Everything that talks to the hardware lives here, so that `live_plot.py`,
`collect.py` and (later) the live-inference script all behave identically.

Contents
--------
Sample / StreamStats   small data holders
ImuSource              abstract base: background thread + sample queue
SerialImuSource        real hardware over USB (auto port detection, reconnect)
FakeImuSource          synthetic data for `--fake`, no hardware needed
calibrate_gyro()       2 s still-hold gyro bias measurement
GestureTracer          integrates gyro rates into a 2D "air mouse" path
trace_image() /        small OpenCV drawing helpers, shared by collect.py
channels_image()       and check_dataset.py

Design notes
------------
* The only module-level mutable things are constants (upper case). All state
  lives inside objects.
* The gyro bias is subtracted inside the source, so every consumer sees
  bias-corrected rates. `Sample.gyro_raw` still carries the uncorrected value.
* We never double-integrate acceleration into position: the 2D trace comes
  from integrating *rotation rates* only (see GestureTracer).
"""

from __future__ import annotations

import math
import queue
import threading
import time
from abc import ABC, abstractmethod
from collections import deque
from dataclasses import dataclass, replace
from typing import Callable, Iterable, Sequence

import numpy as np

try:  # pyserial is only needed for real hardware, not for --fake
    import serial
    from serial.tools import list_ports
except ImportError:  # pragma: no cover - keeps --fake usable on a bare install
    serial = None
    list_ports = None


# ===========================================================================
# Constants
# ===========================================================================

BAUD_RATE = 115200
NOMINAL_RATE_HZ = 100.0
NOMINAL_DT = 1.0 / NOMINAL_RATE_HZ
SEQ_MODULO = 65536  # the Arduino's seq counter wraps here

#: Channel order used everywhere (CSV columns, plots, feature vectors).
CHANNELS: tuple[str, ...] = ("ax", "ay", "az", "gx", "gy", "gz")

#: macOS device-name prefixes, most likely first.
#: Always the /dev/cu.* ("call-up") device, never /dev/tty.*: opening a
#: /dev/tty.* device on macOS blocks waiting for a carrier signal.
PORT_PREFIXES: tuple[str, ...] = (
    "/dev/cu.usbmodem",      # genuine Arduino Mega (native USB CDC)
    "/dev/cu.wchusbserial",  # clone with a CH340 chip (needs the CH340 driver)
    "/dev/cu.usbserial",     # clone with an FTDI/CH340 chip, older naming
)

HEADER_TIMEOUT_S = 5.0     # how long we wait for "#READY" after opening
HEADER_NUDGE_S = 1.5       # ...and when we send 'R' to ask for it again
RECONNECT_DELAY_S = 1.0    # pause between USB reconnection attempts
RATE_WINDOW = 200          # samples used for the live sample-rate estimate

# --- GestureTracer axis mapping --------------------------------------------
# The IMU is glued near the wand tip with its "up" arrow marked, so the axes
# are always mounted the same way. These two constants say which gyro axis
# moves the on-screen cursor sideways and which moves it up/down.
#   gyro index: 0 = gx, 1 = gy, 2 = gz
# If your trace comes out mirrored or rotated, flip a sign or swap the axes
# here - that is the only place this mapping is defined.
# Measured from p01's first real session (2026-10-30): a left-to-right swipe
# rotates about gx (about 67 deg net, vs about 20 deg on gz), and a top-to-bottom
# swipe gives a POSITIVE gy, so the vertical sign has to be inverted for the
# cursor to move down the screen.
TRACER_X_AXIS = 0        # gx -> horizontal cursor movement
TRACER_Y_AXIS = 1        # gy -> vertical cursor movement
TRACER_X_SIGN = +1.0
TRACER_Y_SIGN = -1.0
TRACER_DEG_PER_UNIT = 90.0   # 90 deg of rotation == 1.0 trace unit


# ===========================================================================
# Exceptions
# ===========================================================================

class ImuError(Exception):
    """Base class for every error raised by this module."""


class NoPortError(ImuError):
    """No Arduino-looking serial port was found."""


class LineError(ValueError):
    """A received line could not be parsed."""


class ChecksumError(LineError):
    """A received line had a bad XOR checksum."""


class CalibrationError(ImuError):
    """The wand moved during the still-hold calibration."""


# ===========================================================================
# Data holders
# ===========================================================================

@dataclass(slots=True)
class Sample:
    """One IMU sample, in physical units."""

    seq: int                 # Arduino sample counter (wraps at 65535)
    t_ms: int                # Arduino millis() timestamp
    t_host: float            # time.monotonic() on the Mac when parsed
    acc: np.ndarray          # shape (3,), g
    gyro: np.ndarray         # shape (3,), deg/s, bias-corrected
    gyro_raw: np.ndarray     # shape (3,), deg/s, as received

    @property
    def values(self) -> np.ndarray:
        """The 6 channels in CHANNELS order, as a (6,) float array."""
        return np.concatenate((self.acc, self.gyro))

    @property
    def gyro_mag(self) -> float:
        """Magnitude of the (bias-corrected) rotation rate, deg/s."""
        return float(np.linalg.norm(self.gyro))


@dataclass
class StreamStats:
    """A snapshot of link health. Plain numbers, safe to copy around."""

    samples: int = 0            # good samples parsed
    dropped: int = 0            # samples missing, counted from seq gaps
    checksum_errors: int = 0    # lines with a bad checksum
    bad_lines: int = 0          # lines we could not parse at all
    i2c_errors: int = 0         # last count reported by the Arduino
    rate_hz: float = 0.0        # measured sample rate
    connected: bool = False

    @property
    def dropped_pct(self) -> float:
        """Percentage of expected samples that never arrived."""
        expected = self.samples + self.dropped
        return 100.0 * self.dropped / expected if expected else 0.0

    def summary(self) -> str:
        """One short line for a status bar."""
        state = "OK" if self.connected else "DISCONNECTED"
        return (
            f"{state} | {self.rate_hz:5.1f} Hz | dropped {self.dropped_pct:4.1f}% "
            f"| cs-err {self.checksum_errors} | i2c-err {self.i2c_errors}"
        )


# ===========================================================================
# Line parsing
# ===========================================================================

def parse_line(line: str, t_host: float, bias: np.ndarray) -> Sample:
    """
    Parse one ``$,...*CS`` data line into a :class:`Sample`.

    The checksum covers every character between the ``$`` and the ``*``,
    which includes the comma straight after the ``$`` - exactly what the
    Arduino XORs together.

    Raises
    ------
    ChecksumError  the checksum did not match
    LineError      the line was malformed
    """
    if not line.startswith("$"):
        raise LineError(f"not a data line: {line!r}")
    star = line.rfind("*")
    if star < 0:
        raise LineError(f"no '*' in line: {line!r}")

    body = line[1:star]          # e.g. ",123,45678,0.01,...,-3.2"
    checksum_text = line[star + 1:].strip()

    try:
        want = int(checksum_text, 16)
    except ValueError as exc:
        raise LineError(f"bad checksum field: {checksum_text!r}") from exc

    got = 0
    for ch in body:
        got ^= ord(ch)
    if got != want:
        raise ChecksumError(f"checksum {got:02X} != {want:02X}")

    # body starts with ',', so the first split piece is an empty string.
    fields = body.split(",")[1:]
    if len(fields) != 8:
        raise LineError(f"expected 8 fields, got {len(fields)}: {line!r}")

    try:
        seq = int(fields[0])
        t_ms = int(fields[1])
        numbers = [float(v) for v in fields[2:]]
    except ValueError as exc:
        raise LineError(f"non-numeric field in {line!r}") from exc

    acc = np.array(numbers[0:3], dtype=float)
    gyro_raw = np.array(numbers[3:6], dtype=float)
    return Sample(
        seq=seq,
        t_ms=t_ms,
        t_host=t_host,
        acc=acc,
        gyro=gyro_raw - bias,
        gyro_raw=gyro_raw,
    )


# ===========================================================================
# Sources
# ===========================================================================

class ImuSource(ABC):
    """
    Base class for anything that produces :class:`Sample` objects.

    A source runs its own background thread and pushes samples onto a
    thread-safe queue; the application drains that queue whenever it likes.
    """

    def __init__(self, queue_size: int = 20_000) -> None:
        self.queue: "queue.Queue[Sample]" = queue.Queue(maxsize=queue_size)
        self.header: str | None = None
        self._bias = np.zeros(3, dtype=float)
        self._lock = threading.Lock()
        self._stats = StreamStats()
        self._recent = deque(maxlen=RATE_WINDOW)   # host timestamps
        self._last_seq: int | None = None
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self._epoch = 0        # +1 on every successful (re)connect

    # -- lifecycle ----------------------------------------------------------

    def start(self) -> None:
        """Start the background thread (idempotent)."""
        if self._thread is not None:
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, daemon=True,
                                        name=type(self).__name__)
        self._thread.start()

    def stop(self) -> None:
        """Ask the background thread to finish and wait briefly for it."""
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=2.0)
            self._thread = None

    def __enter__(self) -> "ImuSource":
        self.start()
        return self

    def __exit__(self, *exc_info: object) -> None:
        self.stop()

    @abstractmethod
    def _run(self) -> None:
        """Body of the background thread."""

    # -- consuming ----------------------------------------------------------

    def get(self, timeout: float = 0.1) -> Sample | None:
        """Pop one sample, or ``None`` if none arrived within `timeout`."""
        try:
            return self.queue.get(timeout=timeout)
        except queue.Empty:
            return None

    def drain(self, limit: int = 1000) -> list[Sample]:
        """Pop every sample waiting right now (up to `limit`)."""
        out: list[Sample] = []
        for _ in range(limit):
            try:
                out.append(self.queue.get_nowait())
            except queue.Empty:
                break
        return out

    def clear(self) -> None:
        """Throw away any queued samples (used before a new recording)."""
        self.drain(limit=10**9)

    # -- state --------------------------------------------------------------

    @property
    def epoch(self) -> int:
        """Increments on every successful connect; a change means 'recalibrate'."""
        with self._lock:
            return self._epoch

    @property
    def bias(self) -> np.ndarray:
        """Current gyro bias in deg/s (a copy)."""
        with self._lock:
            return self._bias.copy()

    def set_bias(self, bias: Sequence[float]) -> None:
        """Set the gyro bias that is subtracted from every later sample."""
        with self._lock:
            self._bias = np.asarray(bias, dtype=float).copy()

    def stats(self) -> StreamStats:
        """A copy of the current link statistics."""
        with self._lock:
            snap = replace(self._stats)
            if len(self._recent) >= 2:
                span = self._recent[-1] - self._recent[0]
                snap.rate_hz = (len(self._recent) - 1) / span if span > 0 else 0.0
            return snap

    def wait_connected(self, timeout: float = 30.0) -> bool:
        """Block until the source reports a live link, or `timeout` passes."""
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if self.stats().connected:
                return True
            time.sleep(0.05)
        return False

    # -- helpers for subclasses --------------------------------------------

    def _publish(self, sample: Sample) -> None:
        """Record statistics for `sample` and put it on the queue."""
        with self._lock:
            self._stats.samples += 1
            if self._last_seq is not None:
                gap = (sample.seq - self._last_seq - 1) % SEQ_MODULO
                # A huge "gap" means the board restarted; do not count that.
                if 0 < gap < 1000:
                    self._stats.dropped += gap
            self._last_seq = sample.seq
            self._recent.append(sample.t_host)
        try:
            self.queue.put_nowait(sample)
        except queue.Full:
            # The consumer is too slow. Drop the oldest sample instead of
            # blocking the reader thread (a stalled reader loses the link).
            try:
                self.queue.get_nowait()
                self.queue.put_nowait(sample)
            except (queue.Empty, queue.Full):
                pass

    def _note(self, **fields: object) -> None:
        """Update statistics fields under the lock."""
        with self._lock:
            for key, value in fields.items():
                setattr(self._stats, key, value)

    def _bump(self, field_name: str, amount: int = 1) -> None:
        with self._lock:
            setattr(self._stats, field_name,
                    getattr(self._stats, field_name) + amount)

    def _mark_connected(self, connected: bool) -> None:
        with self._lock:
            self._stats.connected = connected
            if connected:
                self._epoch += 1
                self._last_seq = None
                self._recent.clear()


# --------------------------------------------------------------------------

def list_candidate_ports() -> list[str]:
    """Find likely Arduino serial ports on Windows, macOS and Linux."""
    if list_ports is None:
        return []

    ports = list(list_ports.comports())

    keywords = (
        "arduino",
        "usb serial",
        "usb-serial",
        "ch340",
        "ch341",
        "cp210",
        "ftdi",
    )

    preferred = []
    others = []

    for port in ports:
        device = port.device
        description = (port.description or "").lower()
        manufacturer = (port.manufacturer or "").lower()

        # Identify likely Arduino or USB-to-serial devices
        if any(k in description or k in manufacturer for k in keywords):
            preferred.append(device)

        # Also recognize macOS/Linux serial port names
        elif device.startswith((
            "/dev/cu.usb",
            "/dev/ttyUSB",
            "/dev/ttyACM",
        )):
            preferred.append(device)

        else:
            others.append(device)

    # Prefer Arduino-like ports; use an unambiguous fallback
    if preferred:
        return preferred

    if len(others) == 1:
        return others

    return []


def find_port(preferred: str | None = None) -> str:
    """
    Return the serial port to use.

    `preferred` (from ``--port``) wins if given. Otherwise the first
    Arduino-looking ``/dev/cu.*`` device is chosen.
    """
    if preferred:
        return preferred
    ports = list_candidate_ports()
    if not ports:
        seen = [p.device for p in list_ports.comports()] if list_ports else []
        raise NoPortError(
            "No Arduino serial port found.\n"
            f"  Looked for: {', '.join(PORT_PREFIXES)}\n"
            f"  Ports present: {', '.join(seen) if seen else '(none)'}\n"
            "  Check the USB cable; CH340 clones need the CH340 driver.\n"
            "  Or pass --port /dev/cu.something, or run with --fake."
        )
    return ports[0]


class SerialImuSource(ImuSource):
    """
    Reads IMU lines from the Arduino over USB.

    Opening the port resets the Mega, so after every (re)connect we wait for
    the ``#READY`` header before trusting any data. If the cable is pulled the
    thread keeps retrying instead of raising, and ``epoch`` changes so the
    application knows it must recalibrate.
    """

    def __init__(
        self,
        port: str | None = None,
        baud: int = BAUD_RATE,
        auto_reconnect: bool = True,
        on_message: Callable[[str], None] | None = None,
    ) -> None:
        super().__init__()
        if serial is None:  # pragma: no cover
            raise ImuError("pyserial is not installed; use --fake or "
                           "`pip install -r requirements.txt`")
        self.requested_port = port
        self.baud = baud
        self.auto_reconnect = auto_reconnect
        self.on_message = on_message or (lambda msg: None)
        self.port: str | None = None
        self._ser: "serial.Serial | None" = None

    # -- commands to the board ---------------------------------------------

    def send_command(self, char: str) -> None:
        """Send a single-character command ('R', 'P' or 'S'). Never raises."""
        ser = self._ser
        if ser is None:
            return
        try:
            ser.write(char.encode("ascii"))
            ser.flush()
        except Exception:
            pass

    # -- thread body --------------------------------------------------------

    def _run(self) -> None:
        while not self._stop.is_set():
            if self._ser is None and not self._open_and_handshake():
                if not self.auto_reconnect:
                    return
                self._stop.wait(RECONNECT_DELAY_S)
                continue
            try:
                self._read_forever()
            except Exception as exc:        # unplugged cable, USB stall, ...
                self.on_message(f"link lost: {exc}")
            self._close()
            if not self.auto_reconnect:
                return
            self.on_message("reconnecting...")
            self._stop.wait(RECONNECT_DELAY_S)

    def _open_and_handshake(self) -> bool:
        """Open the port and wait for ``#READY``. True if the link is ready."""
        try:
            self.port = find_port(self.requested_port)
            self.on_message(f"opening {self.port}")
            self._ser = serial.Serial(self.port, self.baud, timeout=0.2)
        except Exception as exc:
            self.on_message(f"cannot open port: {exc}")
            self._close()
            return False

        # The Mega reboots when the port opens: ignore the boot noise, then
        # wait for the header (and ask for it with 'R' if it is late).
        deadline = time.monotonic() + HEADER_TIMEOUT_S
        nudge_at = time.monotonic() + HEADER_NUDGE_S
        nudged = False
        while time.monotonic() < deadline and not self._stop.is_set():
            if not nudged and time.monotonic() >= nudge_at:
                self.send_command("R")
                nudged = True
            line = self._readline()
            if line is None:
                continue
            if line.startswith("#READY"):
                self.header = line
                self.on_message(f"header: {line}")
                self._mark_connected(True)
                return True
            if line.startswith("#NO_IMU"):
                self.on_message("Arduino says: no IMU on the I2C bus "
                                "(check SDA=20, SCL=21, 5V, GND)")

        self.on_message("no #READY header - is wand_imu.ino flashed?")
        self._close()
        return False

    def _readline(self) -> str | None:
        """One decoded, stripped line, or None on timeout."""
        assert self._ser is not None
        raw = self._ser.readline()
        if not raw:
            return None
        return raw.decode("ascii", errors="ignore").strip()

    def _read_forever(self) -> None:
        """Parse lines until the link dies or we are asked to stop."""
        while not self._stop.is_set():
            line = self._readline()
            if not line:
                continue
            if line[0] == "$":
                try:
                    self._publish(parse_line(line, time.monotonic(), self.bias))
                except ChecksumError:
                    self._bump("checksum_errors")
                except LineError:
                    self._bump("bad_lines")
            elif line.startswith("#I2C_ERR"):
                parts = line.split(",")
                if len(parts) > 1 and parts[1].isdigit():
                    self._note(i2c_errors=int(parts[1]))
            elif line.startswith("#READY"):
                self.header = line          # board was reset or 'R' was sent
            elif line.startswith("#"):
                self.on_message(f"board: {line}")

    def _close(self) -> None:
        self._mark_connected(False)
        if self._ser is not None:
            try:
                self._ser.close()
            except Exception:
                pass
        self._ser = None


# --------------------------------------------------------------------------

class FakeImuSource(ImuSource):
    """
    Synthetic 100 Hz stream, so every script runs with no hardware attached.

    By default it loops through the five gestures with short idle gaps in
    between, at amplitudes high enough to fire the motion trigger in
    ``collect.py``. :meth:`request_gesture` overrides the next shape, so a
    ``--fake`` dry run of ``collect.py`` produces the gesture it is asking
    for and the saved traces carry the right label.
    """

    #: (label, duration in seconds) of the shapes the generator cycles through.
    SHAPES: tuple[tuple[str, float], ...] = (
        ("vertical_swipe", 0.45),
        ("horizontal_swipe", 0.45),
        ("zigzag", 0.90),
        ("circle", 1.00),
        ("idle", 1.20),
    )
    PEAK_DPS = 260.0        # peak rotation rate of a synthetic swipe
    GAP_S = 1.00            # idle time between gestures
    NOISE_DPS = 1.5         # gyro noise
    NOISE_G = 0.01          # accel noise
    BIAS_DPS = (1.7, -2.4, 0.9)   # a plausible constant gyro bias to calibrate out

    INITIAL_STILL_S = 3.0   # stay still at startup so calibration can run

    def __init__(self, rate_hz: float = NOMINAL_RATE_HZ, seed: int = 0) -> None:
        super().__init__()
        self.rate_hz = rate_hz
        self.rng = np.random.default_rng(seed)
        self.header = "#READY,rate=100,acc=4g,gyro=1000dps,fw=1.0-fake"
        self._shape_seconds = dict(self.SHAPES)
        self._still_until = 0.0
        self._req_lock = threading.Lock()
        self._requested: str | None = None
        self._request_is_new = False

    def request_gesture(self, label: str) -> None:
        """
        Make the next synthetic gesture be `label`.

        Any gesture in progress is cut short, so the motion trigger in
        ``collect.py`` cannot catch the tail of the previous shape and file it
        under the wrong label.
        """
        with self._req_lock:
            self._requested = label
            self._request_is_new = True

    def request_still(self, duration: float) -> None:
        """
        Pretend the wand is being held still for `duration` seconds.

        :func:`calibrate_gyro` calls this if the source has it, so the
        still-hold calibration also works in ``--fake`` mode.
        """
        self._still_until = max(self._still_until, time.monotonic() + duration)

    # -- gesture shapes -----------------------------------------------------

    def _rates(self, label: str, u: float) -> np.ndarray:
        """
        Rotation rates (deg/s) for gesture `label` at phase ``u`` in [0, 1].

        These follow the real wand's axes (see the TRACER_* constants): gx is
        the horizontal stroke axis and gy the vertical one, with a positive gy
        meaning the tip travels downwards.
        """
        peak = self.PEAK_DPS
        gx = gy = gz = 0.0
        if label == "vertical_swipe":
            # one sweep, top -> bottom (positive gy == downwards)
            gy = peak * math.sin(math.pi * u)
        elif label == "horizontal_swipe":
            # one sweep, left -> right
            gx = peak * math.sin(math.pi * u)
        elif label == "zigzag":
            # "Z": right, diagonal down-left, right
            seg, frac = divmod(u * 3.0, 1.0)
            env = math.sin(math.pi * frac)
            if seg < 1:
                gx = peak * env
            elif seg < 2:
                gx = -0.8 * peak * env
                gy = 0.8 * peak * env       # down-left
            else:
                gx = peak * env
        elif label == "circle":
            # One CLOCKWISE circle. Integrating gives x = -sin, y = -cos on
            # screen, i.e. bottom -> left -> top -> right.
            gx = -peak * math.cos(2 * math.pi * u)
            gy = -peak * math.sin(2 * math.pi * u)
        else:  # "idle": slow drift and small fidgets only
            gy = 12.0 * math.sin(2 * math.pi * 0.7 * u)
            gx = 9.0 * math.sin(2 * math.pi * 0.4 * u + 1.0)
        return np.array([gx, gy, gz], dtype=float)

    def _run(self) -> None:
        dt = 1.0 / self.rate_hz
        bias = np.asarray(self.BIAS_DPS, dtype=float)
        angles = np.zeros(3)        # integrated attitude, for the gravity vector
        prev_rates = np.zeros(3)
        seq = 0
        t0 = time.monotonic()
        next_t = t0

        in_gap = True               # idle between gestures
        phase_t = 0.0
        label = "idle"
        cycle = 0

        self._still_until = max(self._still_until, t0 + self.INITIAL_STILL_S)
        self._mark_connected(True)
        while not self._stop.is_set():
            # A fresh request cuts the current shape short and starts a gap.
            with self._req_lock:
                if self._request_is_new:
                    self._request_is_new = False
                    in_gap = True
                    phase_t = 0.0

            if time.monotonic() < self._still_until:
                rates = np.zeros(3)             # told to hold still
            elif in_gap:
                rates = self._rates("idle", phase_t)
                phase_t += dt
                if phase_t >= self.GAP_S:
                    with self._req_lock:
                        if self._requested is not None:
                            label, self._requested = self._requested, None
                        else:
                            label = self.SHAPES[cycle % len(self.SHAPES)][0]
                            cycle += 1
                    in_gap, phase_t = False, 0.0
            else:
                duration = self._shape_seconds.get(label, 1.0)
                rates = self._rates(label, min(1.0, phase_t / duration))
                phase_t += dt
                if phase_t >= duration:
                    in_gap, phase_t = True, 0.0

            rates = rates + self.rng.normal(0.0, self.NOISE_DPS, 3)
            angles += np.deg2rad(rates) * dt

            # Gravity projected onto the sensor axes (pitch = angles[1],
            # roll = angles[0]), plus a tangential term from the angular
            # acceleration, plus noise. Realistic enough for a dry run.
            pitch, roll = angles[1], angles[0]
            gravity = np.array([
                -math.sin(pitch),
                math.sin(roll) * math.cos(pitch),
                math.cos(roll) * math.cos(pitch),
            ])
            tangential = 0.0025 * (rates - prev_rates)
            prev_rates = rates
            acc = gravity + tangential + self.rng.normal(0.0, self.NOISE_G, 3)

            now = time.monotonic()
            self._publish(Sample(
                seq=seq % SEQ_MODULO,
                t_ms=int((now - t0) * 1000.0),
                t_host=now,
                acc=acc,
                gyro=(rates + bias) - self.bias,
                gyro_raw=rates + bias,
            ))
            seq += 1

            next_t += dt
            self._stop.wait(max(0.0, next_t - time.monotonic()))
        self._mark_connected(False)


# --------------------------------------------------------------------------

def make_source(
    fake: bool,
    port: str | None = None,
    on_message: Callable[[str], None] | None = None,
) -> ImuSource:
    """Build the right source for the command-line flags (not started yet)."""
    if fake:
        if on_message:
            on_message("--fake: using synthetic IMU data, no hardware needed")
        return FakeImuSource()
    return SerialImuSource(port=port, on_message=on_message)


# ===========================================================================
# Gyro calibration
# ===========================================================================

CAL_DURATION_S = 2.0        # how long the wand must be held still
CAL_MAX_STD_DPS = 4.0       # per-axis standard deviation allowed while still
CAL_MAX_P2P_DPS = 25.0      # per-axis peak-to-peak allowed while still
CAL_MIN_SAMPLES = 50


def calibrate_gyro(
    source: ImuSource,
    duration: float = CAL_DURATION_S,
    max_std: float = CAL_MAX_STD_DPS,
    max_p2p: float = CAL_MAX_P2P_DPS,
    on_status: Callable[[str], None] | None = None,
) -> np.ndarray:
    """
    Measure the gyro bias from `duration` seconds of still data.

    The measured bias is stored on the source, so every later sample arrives
    already corrected.

    Raises
    ------
    CalibrationError  the wand moved, or too few samples arrived.
    """
    say = on_status or (lambda msg: None)
    # The fake source can be told to hold still, so --fake calibrates too.
    hold_still = getattr(source, "request_still", None)
    if callable(hold_still):
        hold_still(duration + 0.5)
    source.set_bias(np.zeros(3))      # measure the raw bias
    source.clear()
    say(f"Hold the wand completely still for {duration:.0f} s...")

    collected: list[np.ndarray] = []
    deadline = time.monotonic() + duration
    while time.monotonic() < deadline:
        sample = source.get(timeout=0.2)
        if sample is not None:
            collected.append(sample.gyro_raw)

    if len(collected) < CAL_MIN_SAMPLES:
        raise CalibrationError(
            f"only {len(collected)} samples in {duration:.1f} s - "
            "is the Arduino streaming?"
        )

    data = np.asarray(collected)                 # (n, 3)
    std = data.std(axis=0)
    p2p = data.max(axis=0) - data.min(axis=0)
    if np.any(std > max_std) or np.any(p2p > max_p2p):
        raise CalibrationError(
            "The wand moved during calibration "
            f"(std {np.round(std, 1).tolist()} deg/s, "
            f"peak-to-peak {np.round(p2p, 1).tolist()} deg/s). "
            "Put it down, hold still, and try again."
        )

    bias = data.mean(axis=0)
    source.set_bias(bias)
    say(f"Gyro bias = {np.round(bias, 2).tolist()} deg/s  "
        f"(from {len(collected)} samples)")
    return bias


# ===========================================================================
# GestureTracer - the "air mouse"
# ===========================================================================

class GestureTracer:
    """
    Turns gyro rates into a 2D path, the way an air mouse works.

    Pointing the wand somewhere is a *rotation*, so integrating the rotation
    rate once gives an angle, and the angle is what moves the cursor. We never
    integrate acceleration into position: accelerometer noise and the 1 g
    gravity term make double integration drift away within a second, which
    would make the trace useless for labelling gestures.
    """

    def __init__(
        self,
        x_axis: int = TRACER_X_AXIS,
        y_axis: int = TRACER_Y_AXIS,
        x_sign: float = TRACER_X_SIGN,
        y_sign: float = TRACER_Y_SIGN,
        deg_per_unit: float = TRACER_DEG_PER_UNIT,
        max_dt: float = 0.05,
    ) -> None:
        self.x_axis = x_axis
        self.y_axis = y_axis
        self.x_sign = x_sign
        self.y_sign = y_sign
        self.deg_per_unit = deg_per_unit
        self.max_dt = max_dt
        self._points: list[tuple[float, float]] = [(0.0, 0.0)]
        self._pos = np.zeros(2)
        self._last_t_ms: int | None = None

    def reset(self) -> None:
        """Start a new trace at the origin."""
        self._points = [(0.0, 0.0)]
        self._pos = np.zeros(2)
        self._last_t_ms = None

    def update(self, sample: Sample) -> tuple[float, float]:
        """Add one sample and return the new cursor position."""
        if self._last_t_ms is None:
            dt = 1.0 / NOMINAL_RATE_HZ
        else:
            dt = (sample.t_ms - self._last_t_ms) / 1000.0
            if dt <= 0.0 or dt > self.max_dt:
                dt = 1.0 / NOMINAL_RATE_HZ      # dropped lines or a wrap
        self._last_t_ms = sample.t_ms

        self._pos[0] += self.x_sign * sample.gyro[self.x_axis] * dt / self.deg_per_unit
        self._pos[1] += self.y_sign * sample.gyro[self.y_axis] * dt / self.deg_per_unit
        point = (float(self._pos[0]), float(self._pos[1]))
        self._points.append(point)
        return point

    def feed(self, samples: Iterable[Sample]) -> None:
        """Add many samples at once."""
        for sample in samples:
            self.update(sample)

    @property
    def path(self) -> np.ndarray:
        """The whole trace so far, shape (n, 2)."""
        return np.asarray(self._points, dtype=float)

    @staticmethod
    def path_from_samples(samples: Sequence[Sample], **kwargs: object) -> np.ndarray:
        """Convenience: build a trace from an already-recorded sample list."""
        tracer = GestureTracer(**kwargs)  # type: ignore[arg-type]
        tracer.feed(samples)
        return tracer.path

    @staticmethod
    def path_from_arrays(t_s: np.ndarray, gyro: np.ndarray, **kwargs: object) -> np.ndarray:
        """
        Build a trace from CSV columns: `t_s` (n,) seconds and `gyro` (n, 3).

        Used by ``check_dataset.py``, which reads saved CSV files.
        """
        tracer = GestureTracer(**kwargs)  # type: ignore[arg-type]
        zeros = np.zeros(3)
        for i in range(len(t_s)):
            tracer.update(Sample(
                seq=i,
                t_ms=int(t_s[i] * 1000.0),
                t_host=0.0,
                acc=zeros,
                gyro=gyro[i],
                gyro_raw=gyro[i],
            ))
        return tracer.path


# ===========================================================================
# Small OpenCV drawing helpers (shared by collect.py and check_dataset.py)
# ===========================================================================

BG_COLOR = (24, 22, 20)        # BGR, dark background
FG_COLOR = (235, 235, 235)
#: BGR colours for ax, ay, az, gx, gy, gz
CHANNEL_COLORS = (
    (90, 90, 235), (90, 200, 90), (235, 180, 70),      # accel: r, g, b-ish
    (200, 120, 235), (120, 220, 220), (235, 235, 120),  # gyro
)


def _fit_path(path: np.ndarray, width: int, height: int, margin: int = 12) -> np.ndarray:
    """Scale a 2D path to fit the given pixel box, keeping its aspect ratio."""
    if len(path) < 2:
        return np.zeros((0, 2), dtype=np.int32)
    lo = path.min(axis=0)
    hi = path.max(axis=0)
    span = np.maximum(hi - lo, 1e-6)
    scale = min((width - 2 * margin) / span[0], (height - 2 * margin) / span[1])
    centred = (path - (lo + hi) / 2.0) * scale
    # Screen y grows downwards, and our y axis is "up", so flip it.
    pixels = np.empty_like(centred)
    pixels[:, 0] = centred[:, 0] + width / 2.0
    pixels[:, 1] = -centred[:, 1] + height / 2.0
    return pixels.astype(np.int32)


def draw_path(image: "np.ndarray", path: np.ndarray, box: tuple[int, int, int, int],
              color: tuple[int, int, int] = (90, 230, 255),
              thickness: int = 2) -> None:
    """
    Draw a 2D gesture path into ``box = (x, y, w, h)`` of `image`.

    The start is marked with a green dot and the end with a red one, which
    makes the stroke direction obvious when checking labels later.
    """
    import cv2  # imported lazily so imu_common works without OpenCV

    x, y, w, h = box
    pts = _fit_path(path, w, h)
    if len(pts) < 2:
        return
    pts[:, 0] += x
    pts[:, 1] += y
    cv2.polylines(image, [pts], False, color, thickness, cv2.LINE_AA)
    cv2.circle(image, tuple(pts[0]), 5, (120, 255, 120), -1, cv2.LINE_AA)
    cv2.circle(image, tuple(pts[-1]), 5, (90, 90, 255), -1, cv2.LINE_AA)


def trace_image(path: np.ndarray, width: int = 420, height: int = 420,
                title: str = "") -> "np.ndarray":
    """A standalone image of one gesture trace (used for the saved PNG)."""
    import cv2

    img = np.full((height, width, 3), BG_COLOR, dtype=np.uint8)
    cv2.rectangle(img, (0, 0), (width - 1, height - 1), (70, 68, 66), 1)
    draw_path(img, path, (0, 24, width, height - 24))
    if title:
        cv2.putText(img, title, (10, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.5,
                    FG_COLOR, 1, cv2.LINE_AA)
    return img


def channels_image(values: np.ndarray, width: int = 520, height: int = 220,
                   title: str = "") -> "np.ndarray":
    """
    A small 6-channel plot: 3 accelerometer traces above 3 gyro traces.

    `values` has shape (n, 6) in CHANNELS order.
    """
    import cv2

    img = np.full((height, width, 3), BG_COLOR, dtype=np.uint8)
    if values.ndim != 2 or values.shape[0] < 2:
        return img

    top = 18 if title else 4
    panel_h = (height - top - 6) // 2
    for panel, (lo_ch, hi_ch) in enumerate(((0, 3), (3, 6))):
        y0 = top + panel * (panel_h + 4)
        block = values[:, lo_ch:hi_ch]
        peak = max(float(np.abs(block).max()), 1e-6)
        mid = y0 + panel_h // 2
        cv2.line(img, (0, mid), (width, mid), (60, 58, 56), 1)
        xs = np.linspace(0, width - 1, len(block)).astype(np.int32)
        for channel in range(lo_ch, hi_ch):
            ys = (mid - values[:, channel] / peak * (panel_h / 2 - 2)).astype(np.int32)
            pts = np.stack([xs, ys], axis=1)
            cv2.polylines(img, [pts], False, CHANNEL_COLORS[channel], 1, cv2.LINE_AA)
        label = "acc (g)" if panel == 0 else "gyro (deg/s)"
        cv2.putText(img, f"{label}  +-{peak:.2f}", (6, y0 + 12),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.38, (160, 160, 160), 1, cv2.LINE_AA)
    if title:
        cv2.putText(img, title, (6, 13), cv2.FONT_HERSHEY_SIMPLEX, 0.45,
                    FG_COLOR, 1, cv2.LINE_AA)
    return img


def samples_to_arrays(samples: Sequence[Sample]) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """
    Split a recorded sample list into ``(t_s, values, seq)``.

    ``t_s`` is seconds from the first sample, ``values`` is (n, 6) in CHANNELS
    order and ``seq`` is the Arduino counter - exactly the saved CSV columns.
    """
    if not samples:
        return (np.zeros(0), np.zeros((0, 6)), np.zeros(0, dtype=int))
    t0 = samples[0].t_ms
    t_s = np.array([(s.t_ms - t0) / 1000.0 for s in samples], dtype=float)
    values = np.array([s.values for s in samples], dtype=float)
    seq = np.array([s.seq for s in samples], dtype=int)
    return t_s, values, seq


def sample_dropped_pct(samples: Sequence[Sample]) -> float:
    """Percentage of samples missing inside one recording, from the seq gaps."""
    if len(samples) < 2:
        return 0.0
    expected = (samples[-1].seq - samples[0].seq) % SEQ_MODULO + 1
    if expected <= 0:
        return 0.0
    missing = max(0, expected - len(samples))
    return 100.0 * missing / expected
