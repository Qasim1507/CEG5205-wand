#!/usr/bin/env python3
"""Run two independent IMU gesture recognizers for the local PvP duel."""
from __future__ import annotations

import argparse
import time
from pathlib import Path
from typing import Sequence

import cv2

import imu_common as imu
from live_predict import LivePredictor, load_model
from unity_bridge import UnityBridge
from wand_orientation import WandOrientation


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player1-port", help="Player 1 Arduino serial port")
    parser.add_argument("--player2-port", help="Player 2 Arduino serial port")
    parser.add_argument("--model", default=str(Path(__file__).resolve().parents[1]
                                                 / "models" / "gesture_model.joblib"))
    parser.add_argument("--unity-host", default="127.0.0.1",
                        help="computer running the Unity game (default: this computer)")
    parser.add_argument("--unity-port", type=int, default=5055)
    parser.add_argument("--min-confidence", type=float, default=0.55)
    parser.add_argument("--wand-axes", default="y,x,-z")
    parser.add_argument("--log-dir", default=str(Path(__file__).resolve().parents[1]
                                                   / "local_runs" / "pvp"))
    parser.add_argument("--duration", type=float)
    args = parser.parse_args(argv)
    if bool(args.player1_port) != bool(args.player2_port):
        parser.error("provide both player port options, or neither to auto-detect")
    if args.player1_port and args.player1_port == args.player2_port:
        parser.error("players must use two different serial ports")
    if not 0 <= args.min_confidence <= 1:
        parser.error("--min-confidence must be between 0 and 1")
    if not 1 <= args.unity_port <= 65535:
        parser.error("--unity-port must be between 1 and 65535")
    if args.duration is not None and args.duration <= 0:
        parser.error("--duration must be positive")
    try:
        WandOrientation(args.wand_axes)
    except ValueError as exc:
        parser.error(str(exc))
    return args


def choose_ports(args: argparse.Namespace) -> tuple[str, str]:
    if args.player1_port:
        return args.player1_port, args.player2_port
    ports = imu.list_candidate_ports()
    if len(ports) < 2:
        raise RuntimeError("two Arduino serial ports are required; detected: "
                           + (", ".join(ports) if ports else "none")
                           + ". Connect both wands or pass --player1-port and --player2-port.")
    if len(ports) > 2:
        print("More than two Arduino ports found; using the first two. "
              "Use the --playerN-port options to choose explicitly.")
    return ports[0], ports[1]


def calibrate(source: imu.ImuSource, player: int) -> bool:
    for attempt in range(10):
        try:
            imu.calibrate_gyro(source, on_status=lambda message:
                               print(f"P{player}: {message}", flush=True))
            source.clear()
            return True
        except imu.CalibrationError as exc:
            print(f"P{player}: calibration {attempt + 1}/10: {exc}", flush=True)
            print(f"P{player}: keep the wand resting; retrying in 2 seconds", flush=True)
            time.sleep(2)
    return False


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        ports = choose_ports(args)
    except RuntimeError as exc:
        print(exc)
        return 1

    model = load_model(Path(args.model).expanduser().resolve())
    print(f"model: {model.name} ({model.accuracy:.1%} {model.scheme})")
    print(f"Player 1: {ports[0]}  |  Player 2: {ports[1]}")
    print("Connect both Arduinos, keep both wands still for calibration, and "
          "start the Unity Editor scene before continuing.", flush=True)

    sources = [imu.make_source(False, port, on_message=lambda msg, n=i + 1:
                               print(f"P{n}: {msg}", flush=True))
               for i, port in enumerate(ports)]
    bridge = UnityBridge(args.unity_host, args.unity_port)
    bridge.set_mode("duel")
    apps: list[LivePredictor] = []
    started = time.monotonic()
    try:
        for source in sources:
            source.start()
        for index, source in enumerate(sources, start=1):
            if not source.wait_connected(timeout=30):
                print(f"Player {index}: no IMU data from {ports[index - 1]}")
                return 1
        for index, source in enumerate(sources, start=1):
            if not calibrate(source, index):
                print(f"Player {index}: calibration failed; restart with both "
                      "wands resting on the table.")
                return 1

        log_root = Path(args.log_dir).expanduser()
        for index, source in enumerate(sources, start=1):
            apps.append(LivePredictor(
                model, source, args.min_confidence,
                log_root / f"player{index}", None, bridge, args.wand_axes,
                player_id=index))

        for app in apps:
            cv2.namedWindow(app.window_name, cv2.WINDOW_AUTOSIZE)
        print("PvP live. Q/Esc quits; R recenters both wands.", flush=True)
        while args.duration is None or time.monotonic() - started < args.duration:
            for app in apps:
                app.pump()
                cv2.imshow(app.window_name, app.render())
            key = cv2.waitKey(20) & 0xFF
            if key in (ord("q"), 27):
                break
            if key == ord("r"):
                for app in apps:
                    app.orientation.reset()
                    app.message = "recentered"
        for app in apps:
            app.report()
        return 0
    except KeyboardInterrupt:
        print("\nPvP stopped.")
        for app in apps:
            app.report()
        return 130
    finally:
        cv2.destroyAllWindows()
        for source in sources:
            source.stop()
        bridge.close()


if __name__ == "__main__":
    raise SystemExit(main())
