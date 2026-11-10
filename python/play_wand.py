#!/usr/bin/env python3
"""Run the wand game with one or two connected Arduino controllers."""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Sequence

import cv2

import imu_common as imu
from live_predict import LivePredictor, load_model
from unity_bridge import UnityBridge
from wand_orientation import WandOrientation

REPO_ROOT = Path(__file__).resolve().parents[1]
BUNDLE_ROOT = Path(getattr(sys, "_MEIPASS", REPO_ROOT))
APP_DATA_ROOT = Path(os.environ.get("LOCALAPPDATA", Path.home() / ".wand-spells"))


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player1-port", help="Player 1 Arduino serial port")
    parser.add_argument("--player2-port", help="Player 2 Arduino serial port")
    parser.add_argument("--online-duel", action="store_true",
                        help="use the LAN host/join duel mode with one Arduino on this laptop")
    parser.add_argument("--model", default=str(BUNDLE_ROOT / "models" / "gesture_model.joblib"))
    parser.add_argument("--unity-host", default="127.0.0.1",
                        help="IP address of the Unity game (Quest or desktop; default: this computer)")
    parser.add_argument("--unity-port", type=int, default=5055)
    parser.add_argument("--min-confidence", type=float, default=0.55)
    parser.add_argument("--wand-axes", default="y,x,-z")
    parser.add_argument("--log-dir", default=str(APP_DATA_ROOT / "local_runs" / "desktop-game"))
    parser.add_argument("--duration", type=float)
    args = parser.parse_args(argv)
    if args.player2_port and not args.player1_port:
        parser.error("--player2-port requires --player1-port")
    if args.online_duel and args.player2_port:
        parser.error("--online-duel uses one Arduino per game instance; omit --player2-port")
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


def choose_ports(args: argparse.Namespace) -> tuple[str, ...]:
    if args.player1_port:
        return ((args.player1_port, args.player2_port) if args.player2_port
                else (args.player1_port,))
    ports = imu.list_candidate_ports()
    if not ports:
        raise RuntimeError("no Arduino serial port detected. Connect a wand or "
                           "pass --player1-port to select it.")
    if args.online_duel:
        if len(ports) > 1:
            print("LAN PvP uses one Arduino per game instance; using the first detected port.")
        return (ports[0],)
    if len(ports) > 2:
        print("More than two Arduino ports found; using the first two. "
              "Use the --playerN-port options to choose explicitly.")
    return tuple(ports[:2])


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


def packaged_game_executable() -> Path:
    """Return the Unity player embedded in a one-file desktop package."""
    game_root = BUNDLE_ROOT / "game"
    if sys.platform == "darwin":
        return game_root / "WandSpells.app" / "Contents" / "MacOS" / "WandSpells"
    return game_root / "WandSpells.exe"


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        ports = choose_ports(args)
    except RuntimeError as exc:
        print(exc)
        return 1

    mode = "online" if args.online_duel else ("single" if len(ports) == 1 else "duel")

    model = load_model(Path(args.model).expanduser().resolve())
    print(f"model: {model.name} ({model.accuracy:.1%} {model.scheme})")
    print("Connected controllers: " + " | ".join(
        f"Player {i + 1}: {port}" for i, port in enumerate(ports)))
    mode_label = {"single": "you vs the Wand Bot", "duel": "two-player local PvP",
                  "online": "LAN PvP"}[mode]
    print("Mode: " + mode_label)
    print("Keep the connected wand(s) still for calibration."
          if getattr(sys, "frozen", False) else
          "Keep the connected wand(s) still for calibration and start the "
          "Unity Editor scene or Quest app before continuing.", flush=True)

    sources = [imu.make_source(False, port, on_message=lambda msg, n=i + 1:
                               print(f"P{n}: {msg}", flush=True))
               for i, port in enumerate(ports)]
    bridge = UnityBridge(args.unity_host, args.unity_port)
    bridge.set_mode(mode)
    apps: list[LivePredictor] = []
    game_process = None
    try:
        if getattr(sys, "frozen", False):
            game_executable = packaged_game_executable()
            if not game_executable.is_file():
                print(f"Bundled Unity game is missing: {game_executable}")
                return 1
            game_process = subprocess.Popen([str(game_executable)],
                                            cwd=str(game_executable.parent))
            print("Started the bundled desktop game.", flush=True)
        for source in sources:
            source.start()
        for index, source in enumerate(sources, start=1):
            if not source.wait_connected(timeout=30):
                print(f"Player {index}: no IMU data from {ports[index - 1]}")
                return 1
        for index, source in enumerate(sources, start=1):
            if not calibrate(source, index):
                print(f"Player {index}: calibration failed; restart with the "
                      "connected wand(s) resting on the table.")
                return 1

        log_root = Path(args.log_dir).expanduser()
        for index, source in enumerate(sources, start=1):
            apps.append(LivePredictor(
                model, source, args.min_confidence,
                log_root / f"player{index}", None, bridge, args.wand_axes,
                player_id=index))

        for app in apps:
            cv2.namedWindow(app.window_name, cv2.WINDOW_AUTOSIZE)
        print("Game ready. Q/Esc quits; R recenters the wand(s).", flush=True)
        started = time.monotonic()
        while args.duration is None or time.monotonic() - started < args.duration:
            if game_process is not None and game_process.poll() is not None:
                break
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
        if game_process is not None and game_process.poll() is None:
            game_process.terminate()
            try:
                game_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                game_process.kill()


if __name__ == "__main__":
    raise SystemExit(main())
