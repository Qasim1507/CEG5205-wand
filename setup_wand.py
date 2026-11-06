#!/usr/bin/env python3
"""One-time Mac/Windows software setup. Run again to install/update the Quest app."""
import argparse
from pathlib import Path
import platform
import shutil
import ssl
import subprocess
import sys
import tempfile
import urllib.request
import urllib.error
import venv
import zipfile

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "python"))
from wand_tools import find_adb, authorized_devices


def install_platform_tools(system):
    suffix = {"Darwin": "darwin", "Windows": "windows"}[system]
    url = f"https://dl.google.com/android/repository/platform-tools-latest-{suffix}.zip"
    target = ROOT / ".tools"
    target.mkdir(exist_ok=True)
    print("Downloading Android Platform-Tools from Google...", flush=True)
    with tempfile.TemporaryDirectory() as temporary:
        archive = Path(temporary) / "platform-tools.zip"
        try:
            with urllib.request.urlopen(url, timeout=90) as response, archive.open("wb") as output:
                while chunk := response.read(1024 * 1024):
                    output.write(chunk)
        except urllib.error.URLError as exc:
            # python.org installs on Mac can lack configured CA roots. System
            # curl uses its own trusted certificate store; TLS stays verified.
            curl = shutil.which("curl")
            if not isinstance(exc.reason, ssl.SSLCertVerificationError) or not curl:
                raise
            print("Using the system certificate store for this download...", flush=True)
            subprocess.run([curl, "--fail", "--location", "--silent", "--show-error",
                            "--retry", "2", "--connect-timeout", "20", "--max-time", "180",
                            "--output", str(archive), url], check=True)
        with zipfile.ZipFile(archive) as package:
            for member in package.infolist():
                path = (target / member.filename).resolve()
                if not path.is_relative_to(target.resolve()) or not member.filename.startswith("platform-tools/"):
                    raise RuntimeError("Unexpected path in Android tools archive")
            package.extractall(target)
    adb = target / "platform-tools" / ("adb.exe" if system == "Windows" else "adb")
    if not adb.is_file():
        raise RuntimeError("Android tools download did not contain adb")
    if system != "Windows":
        adb.chmod(0o755)
    return str(adb)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skip-apk", action="store_true", help="configure laptop only; leave Quest unchanged")
    parser.add_argument("--device", help="ADB serial when several devices are connected")
    args = parser.parse_args()
    system = platform.system()
    if system not in ("Darwin", "Windows"):
        parser.error("These setup launchers support macOS and Windows.")
    if sys.version_info < (3, 12):
        parser.error("Install 64-bit Python 3.12, 3.13 or 3.14 from python.org, then retry.")
    environment = ROOT / ".venv"
    python = environment / ("Scripts/python.exe" if system == "Windows" else "bin/python")
    if not python.exists():
        if environment.exists():
            raise RuntimeError("Existing .venv is incomplete or from another OS. Rename it, then rerun setup.")
        print("Creating the project's Python environment...", flush=True)
        venv.EnvBuilder(with_pip=True).create(environment)
    print("Installing Python dependencies (first setup may take several minutes)...", flush=True)
    subprocess.run([str(python), "-m", "pip", "install", "--only-binary=:all:",
                    "-r", str(ROOT / "python/requirements.txt")], cwd=ROOT, check=True)
    print("Checking the included trained model...", flush=True)
    subprocess.run([str(python), "-c", "import sys; sys.path.insert(0, 'python'); "
                    "from live_predict import load_model, DEFAULT_MODEL_PATH; "
                    "model=load_model(DEFAULT_MODEL_PATH); print('Model ready:', model.name)"],
                   cwd=ROOT, check=True)
    adb = find_adb() or install_platform_tools(system)
    print(f"Android connection tools ready: {adb}", flush=True)
    installed = False
    if not args.skip_apk:
        devices = authorized_devices(adb)
        if args.device and args.device not in devices:
            raise RuntimeError("Selected device is not authorized. Accept USB debugging in Quest and rerun setup.")
        device = args.device or (devices[0] if len(devices) == 1 else None)
        if device:
            apk = ROOT / "unity/Builds/WandSpellsMeetingDemo.apk"
            if not apk.is_file():
                raise RuntimeError("APK missing. Download/clone the complete repository and rerun setup.")
            print("Installing/updating Wand Spells on the connected Quest...", flush=True)
            subprocess.run([adb, "-s", device, "install", "-r", str(apk)], check=True)
            installed = True
        else:
            print("\nLAPTOP SOFTWARE IS READY; QUEST INSTALLATION IS STILL PENDING.", flush=True)
            print("Connect one Quest by USB, enable developer mode, accept USB debugging, "
                  "then run this setup again. For multiple devices use --device SERIAL.", flush=True)
    if installed:
        print("\nQuest app installed successfully.")
    launcher = "Start Quest Wand.cmd" if system == "Windows" else "Start Quest Wand.command"
    print(f"\nTo play, double-click {launcher}.")
    print("Keep Arduino connected by USB; put laptop and Quest on the same Wi-Fi.")
    print("Leave the wand on the table until calibration finishes and the live window appears.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, subprocess.SubprocessError, zipfile.BadZipFile) as exc:
        print(f"\nSetup did not finish: {exc}\nSee the README troubleshooting section.", file=sys.stderr)
        sys.exit(1)
