#!/usr/bin/env python3
"""
live_plot.py - real-time visualisation of the wand IMU signal (course Task 1).

Shows the six IMU channels scrolling live in a rolling 5 s window, with a
status bar for sample rate, dropped percentage and the latest values.

Run:
    python live_plot.py                 # auto-detect the Arduino
    python live_plot.py --port /dev/cu.usbmodem14101
    python live_plot.py --fake          # no hardware needed
    python live_plot.py --no-calibrate  # skip the still-hold gyro calibration
"""

from __future__ import annotations

import argparse
import sys

import numpy as np
import pyqtgraph as pg
from pyqtgraph.Qt import QtCore, QtWidgets

import imu_common as imu

# --- configuration ---------------------------------------------------------
WINDOW_SECONDS = 5.0                       # width of the rolling view
BUFFER_LEN = int(WINDOW_SECONDS * imu.NOMINAL_RATE_HZ * 1.2)
REFRESH_MS = 33                            # ~30 frames per second
ACC_YRANGE = (-4.5, 4.5)                   # g,     matches the +-4 g setting
GYRO_YRANGE = (-1100.0, 1100.0)            # deg/s, matches +-1000 dps
#: pyqtgraph pen colours for ax, ay, az / gx, gy, gz
PEN_COLORS = ("#ff6b6b", "#51cf66", "#4dabf7", "#e599f7", "#ffd43b", "#63e6be")


class LivePlotWindow(QtWidgets.QWidget):
    """A Qt window with two stacked plots (accelerometer, gyroscope)."""

    def __init__(self, source: imu.ImuSource) -> None:
        super().__init__()
        self.source = source
        self.setWindowTitle("CEG5205 wand - live IMU (Task 1)")
        self.resize(1000, 640)

        # One ring buffer per channel, plus the shared time axis.
        self._t = np.zeros(BUFFER_LEN, dtype=float)
        self._y = np.zeros((6, BUFFER_LEN), dtype=float)
        self._count = 0            # how many slots hold real data
        self._t0: float | None = None

        layout = QtWidgets.QVBoxLayout(self)
        layout.setContentsMargins(8, 8, 8, 8)

        pg.setConfigOptions(antialias=True, background="#17161a", foreground="#dcdcdc")
        self.acc_plot = self._make_plot("acceleration (g)", ACC_YRANGE)
        self.gyro_plot = self._make_plot("rotation rate (deg/s)", GYRO_YRANGE)
        self.gyro_plot.setXLink(self.acc_plot)
        layout.addWidget(self.acc_plot, 1)
        layout.addWidget(self.gyro_plot, 1)

        self.curves: list[pg.PlotDataItem] = []
        for index, name in enumerate(imu.CHANNELS):
            plot = self.acc_plot if index < 3 else self.gyro_plot
            self.curves.append(
                plot.plot(pen=pg.mkPen(PEN_COLORS[index], width=2), name=name)
            )

        self.status = QtWidgets.QLabel("starting...")
        self.status.setStyleSheet(
            "font-family: Menlo, monospace; font-size: 12px; color: #d8d8d8;"
        )
        layout.addWidget(self.status)

        self.timer = QtCore.QTimer(self)
        self.timer.timeout.connect(self._tick)
        self.timer.start(REFRESH_MS)

    def _make_plot(self, title: str, yrange: tuple[float, float]) -> pg.PlotWidget:
        plot = pg.PlotWidget(title=title)
        plot.showGrid(x=True, y=True, alpha=0.25)
        plot.setYRange(*yrange)
        plot.setLabel("bottom", "time", units="s")
        plot.addLegend(offset=(10, 10))
        return plot

    # -- data ---------------------------------------------------------------

    def _append(self, samples: list[imu.Sample]) -> None:
        """Push new samples into the ring buffers (oldest shifted out)."""
        if not samples:
            return
        if self._t0 is None:
            self._t0 = samples[0].t_ms / 1000.0
        chunk = len(samples)
        if chunk >= BUFFER_LEN:                 # a very large backlog
            samples = samples[-BUFFER_LEN:]
            chunk = len(samples)
        self._t = np.roll(self._t, -chunk)
        self._y = np.roll(self._y, -chunk, axis=1)
        self._t[-chunk:] = [s.t_ms / 1000.0 - self._t0 for s in samples]
        self._y[:, -chunk:] = np.array([s.values for s in samples]).T
        self._count = min(BUFFER_LEN, self._count + chunk)

    def _tick(self) -> None:
        """Called by the Qt timer: drain the queue, redraw, update the status."""
        self._append(self.source.drain(limit=2000))
        stats = self.source.stats()

        if self._count >= 2:
            t = self._t[-self._count:]
            for index, curve in enumerate(self.curves):
                curve.setData(t, self._y[index, -self._count:])
            self.acc_plot.setXRange(max(0.0, t[-1] - WINDOW_SECONDS), t[-1],
                                    padding=0)
            latest = self._y[:, -1]
            values = "  ".join(
                f"{name}={latest[i]:+8.3f}" for i, name in enumerate(imu.CHANNELS)
            )
        else:
            values = "waiting for data..."

        self.status.setText(f"{stats.summary()}\n{values}")

    def closeEvent(self, event: object) -> None:  # noqa: N802  (Qt naming)
        self.source.stop()
        super().closeEvent(event)  # type: ignore[arg-type]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", help="serial port, e.g. /dev/cu.usbmodem14101")
    parser.add_argument("--fake", action="store_true",
                        help="use synthetic data instead of hardware")
    parser.add_argument("--no-calibrate", action="store_true",
                        help="skip the 2 s still-hold gyro calibration")
    args = parser.parse_args(argv)

    source = imu.make_source(args.fake, args.port, on_message=lambda m: print("[imu]", m))
    source.start()
    print("[imu] waiting for the link...")
    if not source.wait_connected(timeout=20.0):
        print("[imu] no data. Check the cable, or run with --fake.", file=sys.stderr)
        source.stop()
        return 1
    print(f"[imu] header: {source.header}")

    if not args.no_calibrate:
        try:
            imu.calibrate_gyro(source, on_status=lambda m: print("[cal]", m))
        except imu.CalibrationError as exc:
            print(f"[cal] {exc}\n[cal] continuing without a bias correction.")

    app = QtWidgets.QApplication(sys.argv[:1])
    window = LivePlotWindow(source)
    window.show()
    try:
        return app.exec()
    finally:
        source.stop()


if __name__ == "__main__":
    raise SystemExit(main())
