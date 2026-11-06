#!/usr/bin/env python3
"""Find a USB-authorized Quest's Wi-Fi address and start laptop recognition."""
import argparse
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

from unity_bridge import UnityBridge

PACKAGE = "com.ceg5205.wandspellsdemo"


def find_adb():
    candidates = [shutil.which("adb"),
                  str(Path.home() / "Library/Android/sdk/platform-tools/adb"),
                  str(Path.home() / "Documents/CEG5205-wand-local/tools/platform-tools/adb")]
    candidates += [str(p) for p in Path("/Applications/Unity/Hub/Editor").glob(
        "*/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb")]
    return next((p for p in candidates if p and Path(p).is_file()), None)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--quest-ip", help="use this Wi-Fi IP when USB is disconnected")
    parser.add_argument("--device", help="ADB serial if more than one device is connected")
    parser.add_argument("--check", action="store_true", help="check game connectivity without opening Arduino")
    parser.add_argument("--no-launch", action="store_true", help="do not launch the installed app using USB")
    args, extra = parser.parse_known_args()
    adb = find_adb()
    device = args.device
    if adb:
        listing = subprocess.check_output([adb, "devices"], text=True)
        devices = re.findall(r"^(\S+)\s+device$", listing, re.MULTILINE)
        if device is None and len(devices) == 1:
            device = devices[0]
    command = [adb, "-s", device] if adb and device else None
    host = args.quest_ip
    if not host and command:
        addresses = subprocess.check_output(command + ["shell", "ip", "-4", "addr", "show", "wlan0"], text=True)
        match = re.search(r"inet (\d+\.\d+\.\d+\.\d+)/", addresses)
        if match:
            host = match.group(1)
    if not host:
        parser.error("connect and authorize one Quest over USB, or supply --quest-ip ADDRESS")
    print(f"Quest Wi-Fi address: {host}; keep laptop and Quest on the same Wi-Fi", flush=True)
    if command and not args.no_launch:
        result = subprocess.run(command + ["shell", "pm", "path", PACKAGE], capture_output=True, text=True)
        if "package:" not in result.stdout:
            parser.error("updated Wand Spells APK is not installed yet; see LAPTOP_QUEST.md")
        subprocess.run(command + ["shell", "monkey", "-p", PACKAGE, "-c", "android.intent.category.LAUNCHER", "1"],
                       check=True, stdout=subprocess.DEVNULL)
    if args.check:
        bridge = UnityBridge(host)
        try:
            deadline = time.monotonic() + 12
            while time.monotonic() < deadline:
                bridge.pump()
                if bridge.was_connected:
                    return 0
                time.sleep(.05)
            print("No reply. Keep the game open and headset awake; check Wi-Fi/client isolation.")
            return 1
        finally:
            bridge.close()
    from live_predict import main as predict
    # Keep demo logs out of the training dataset and separate each run.
    log_dir = Path(__file__).resolve().parents[1] / "local_runs" / time.strftime("%Y%m%d-%H%M%S")
    return predict(["--unity-host", host, "--log-dir", str(log_dir), *extra])


if __name__ == "__main__":
    sys.exit(main())
