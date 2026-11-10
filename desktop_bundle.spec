# PyInstaller one-file bundle for the Unity desktop player plus Python runtime.
from pathlib import Path
import sys
from PyInstaller.utils.hooks import collect_all

root = Path(SPECPATH).resolve()
model = root / "models" / "gesture_model.joblib"
if sys.platform == "darwin":
    game = root / "unity" / "Builds" / "DesktopPlayer" / "Mac" / "WandSpells.app"
else:
    game = root / "unity" / "Builds" / "DesktopPlayer" / "Windows"
    if not (game / "WandSpells.exe").exists():
        raise SystemExit("Build the Windows desktop player from Unity first.")

if not model.exists():
    raise SystemExit("The trained gesture model is missing: " + str(model))
if not game.exists():
    raise SystemExit("Build the desktop Unity player first: " + str(game))

datas = [(str(model), "models"), (str(game), "game")]
binaries = []
hiddenimports = []
for package in ("cv2", "numpy", "pandas", "sklearn", "joblib"):
    package_datas, package_binaries, package_imports = collect_all(package)
    datas += package_datas
    binaries += package_binaries
    hiddenimports += package_imports

a = Analysis(
    [str(root / "python" / "play_wand.py")],
    pathex=[str(root / "python")],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="WandSpells",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=(sys.platform != "darwin"),
    disable_windowed_traceback=False,
)

if sys.platform == "darwin":
    app = BUNDLE(exe, name="WandSpells.app",
                 bundle_identifier="edu.nus.ceg5205.wandspells")
