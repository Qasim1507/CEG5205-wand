"""Platform-independent discovery of Android tools (no third-party imports)."""
import os
from pathlib import Path
import platform
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def find_adb(root=ROOT, system=None):
    system = system or platform.system()
    executable = "adb.exe" if system == "Windows" else "adb"
    candidates = [root / ".tools/platform-tools" / executable, shutil.which(executable)]
    for variable in ("ANDROID_HOME", "ANDROID_SDK_ROOT"):
        if os.environ.get(variable):
            candidates.append(Path(os.environ[variable]) / "platform-tools" / executable)
    if system == "Windows":
        candidates.append(Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData/Local")) /
                          "Android/Sdk/platform-tools/adb.exe")
        editors = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Unity/Hub/Editor"
        candidates.extend(sorted(editors.glob("*/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"), reverse=True))
    else:
        candidates.append(Path.home() / "Library/Android/sdk/platform-tools/adb")
        candidates.extend(sorted(Path("/Applications/Unity/Hub/Editor").glob(
            "*/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"), reverse=True))
        # Compatibility with the original development Mac's tool installation.
        candidates.append(Path.home() / "Documents/CEG5205-wand-local/tools/platform-tools/adb")
    return next((str(p) for p in candidates if p and Path(p).is_file()), None)


def authorized_devices(adb):
    listing = subprocess.check_output([adb, "devices"], text=True, timeout=30)
    return [parts[0] for line in listing.splitlines()
            if len(parts := line.split()) >= 2 and parts[1] == "device"]
