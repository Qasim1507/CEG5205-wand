# CEG5205 — IMU Wand Spellcasting on Meta Quest 3

Move a physical IMU wand to rotate a virtual wand and cast spells in a Quest 3
wizard game. A trained classifier runs on the laptop; the Quest runs the included
Unity Android app. The repository also contains the recording and training tools.

For step-by-step setup, desktop play, Quest play, and mixed-device PvP, see the
[Player Manual](PLAYER_MANUAL.md).

```text
MPU-6050 → Arduino Mega → USB → laptop Python → Wi-Fi → Quest 3
                                live rotation + recognized spells
```

## Directory Structure

```
.
├── arduino/
├── data/
├── models/
├── python/
├── unity/
├── Setup Quest Wand.cmd
├── Start Quest Wand.cmd
├── Setup Quest Wand.command
├── Start Quest Wand.command
├── LAPTOP_QUEST.md
└── README.md
```

### Folder Overview

| Folder | Description |
|--------|-------------|
| `arduino/` | Arduino sketch to be uploaded to the **Arduino Mega 2560**. ([Flash the Arduino](#2-flash-the-arduino)) |
| `data/` | Gesture data collected by team members for model training. Contains **5 gesture classes** with **25 images per class**. ([Data Collection](#6-collecting-a-real-dataset)) |
| `models/` | Trained model output. The final model is saved as `gesture_model.joblib` after training. ([Model Training](#7-training-and-live-recognition)) |
| `python/` | Python scripts for (1) IMU data collection and visualisation, (2) model training, and (3) a UDP link that streams predictions to the Unity application server. |
| `unity/` | Unity project folder. ([Unity Setup](#run-the-unity-scene-on-your-computer)) |

## Play on your computer with the Arduino (Meta Quest Headset)

### What you need

- **Mac or Windows laptop** with internet for the initial setup.
- **64-bit Python 3.12, 3.13 or 3.14** from [python.org](https://www.python.org/downloads/).
  On Windows, enable **Add Python to PATH** if the installer offers it.
- **Arduino Mega + MPU-6050 wand**, connected to the laptop by USB, running
  [`arduino/wand_imu/wand_imu.ino`](arduino/wand_imu/wand_imu.ino).
  For a new board, follow [wiring](#1-wiring) and [firmware upload](#2-flash-the-arduino) below.
- **Meta Quest 3**, with developer mode enabled and a USB data cable to the laptop.
- Laptop and Quest on the **same Wi-Fi**. Close Arduino Serial Monitor and any
  other recording/recognition program before starting.

The trained model and ready-to-install Quest APK are included. **Unity is only
needed if you want to edit the game or build a new APK.**

### First-time setup

1. Download this repository with **Code → Download ZIP** and extract it, or clone
   `https://github.com/Qasim1507/CEG5205-wand.git`. Open the extracted repo folder.
2. Connect Quest by USB. Put it on and accept **Allow USB debugging**; select
   **Always allow from this computer** if shown.
3. Run the setup file for your laptop:

| Laptop | Run once to set up | Run each time to play |
| --- | --- | --- |
| macOS | Double-click **Setup Quest Wand.command** | Double-click **Start Quest Wand.command** |
| Windows | Double-click **Setup Quest Wand.cmd** | Double-click **Start Quest Wand.cmd** |

Setup creates `.venv`, installs the Python dependencies, checks the included model,
finds Android connection tools or downloads them from Google's official server,
and installs/updates the included APK on one authorized USB headset.
It keeps downloaded tools inside ignored `.tools/`; no Android Studio installation
or manual PATH changes are needed for those tools.

If setup reports **QUEST INSTALLATION IS STILL PENDING**, the laptop setup is done
but the headset is not ready. Accept its USB debugging prompt, then run setup again.
Keep the folder after setup: the launchers use the files inside it. If you move it
to another computer, run setup there to create that computer's environment.

**Mac if double-clicking fails:** open Terminal in the repo folder and run:

```bash
python3 setup_wand.py
```

**Windows terminal alternative:** open Command Prompt in the repo folder and run:

```bat
py -3 setup_wand.py
```

If Python is already installed but that command selects an older version, choose
the installed supported version explicitly, e.g. `python3.14 setup_wand.py` on Mac
or `py -3.14 setup_wand.py` on Windows.

### Every time you play

1. Connect the Arduino by USB. Keep the Quest USB cable connected for automatic
   headset detection and game launch; game data travels over Wi-Fi.
2. Put the wand on the table, then double-click your **Start Quest Wand** file.
3. Wait for calibration to finish and the live recognition window to appear.
   Look for **Quest link: CONNECTED**. Calibration retries if the wand moves.
4. In Quest, look at a displayed wand for **1.2 seconds** to select it and enter
   the arena. Pick up the physical wand, perform a gesture, then pause briefly.
5. To recenter, point the physical wand forward and press **R** in the laptop
   recognition window, or **A/X** on a Quest controller. Quit with **Q/Esc** in
   the laptop window or **Ctrl+C** in its terminal.

The wand follows tilt and swing continuously, including between casts. Its grip
stays at a fixed offset from the view; the IMU does not measure room position.
In the crystal stage, aim the crosshair at the active crystal and cast its matching
element. Spells fire toward your aimed point even when you miss; a point is awarded
only for a correctly aimed, matching spell. Hitting another crystal does not score.
Clearing all five opens the moving-dummy stage. On Quest, **look at the dummy to
aim the centered crosshair**, then cast the element matching its crystal: red
fire, blue water, green wind or yellow lightning. The wand still rotates with
your IMU; head direction controls the shot, so a gesture does not pull the aim off
target. The left joystick is not needed. See [the integration guide](LAPTOP_QUEST.md) for gesture/effect
mapping, rotation details, logs and advanced options.

Instructions appear on a large, dark board fixed in the room, to the left of the
targets at eye level. It stays still when you turn your head and is placed again
when you enter the next arena. Its gesture card shows the current target's motion: **fire = swipe down**,
**water = clockwise circle**, **wind = swipe right**, **lightning = draw a Z**
(right, diagonally down-left, then right). It updates when the dummy changes element.

### Manual launch / useful options

Run these from the repo folder; virtual-environment activation is optional.

| Task | macOS | Windows (Command Prompt) |
| --- | --- | --- |
| Start game + recognition | `.venv/bin/python python/quest_wand.py` | `.venv\Scripts\python.exe python\quest_wand.py` |
| Check Quest connection only | `.venv/bin/python python/quest_wand.py --check` | `.venv\Scripts\python.exe python\quest_wand.py --check` |
| Choose Arduino port | Add `--port /dev/cu.usbmodemXXXX` | Add `--port COM3` (use your actual port) |
| Set up without changing the headset | `python3 setup_wand.py --skip-apk` | `py -3 setup_wand.py --skip-apk` |

Once the app is installed, you can disconnect Quest USB, open **Wand Spells MR**
in Quest's **Unknown Sources** apps, and use `--quest-ip YOUR_QUEST_WIFI_IP --no-launch`
on the launch command. The Arduino must stay connected to the laptop.

### Troubleshooting setup and play

| What you see | What to do |
| --- | --- |
| Python missing / too old | Install the supported Python version above, then rerun setup. |
| Mac refuses to open a `.command` file | Use the Terminal alternatives above; for launch use `.venv/bin/python python/quest_wand.py`. |
| Python packages fail to download | Check internet, use 64-bit Python 3.12–3.14, and rerun setup. The included model uses scikit-learn 1.9.1, pinned in requirements. |
| Quest unauthorized / installation pending | Wear the headset, accept USB debugging, then rerun setup. Try a USB data cable rather than a charge-only cable. |
| More than one Android device connected | Disconnect the others, or pass `--device SERIAL` to setup and launch. |
| APK signature mismatch on install | Ask the team which build to keep; an app installed with another signing key cannot be updated directly. Setup does not uninstall it or erase its data. |
| Arduino not found / port busy | Close Serial Monitor and other Python runs. Check the USB cable, board driver and actual COM/serial port. |
| No `#READY` header | Upload the supplied Mega firmware using Arduino IDE; setup installs laptop software, while firmware upload is a separate step. |
| Calibration retries repeatedly | Leave the wand resting on the table until the live window appears. |
| No Quest reply / disconnected | Keep the game open and headset awake. Check both devices share Wi-Fi; a campus/guest network may isolate devices. Try a shared hotspot/private router. |
| Windows firewall blocks the connection | Allow the Python app on the private network you use for the demo, then retry `--check`. |
| Wand direction drifts | Point forward and recenter with R or A/X. |

Mac + Quest hardware playback is verified. Windows launchers and discovery are
provided, but still need verification on a Windows laptop with the actual hardware.

## Play on your computer with the Arduino (no Meta Quest Headset)

You can play in **Unity Editor** using the physical Arduino/IMU wand. A Quest
headset and Wi-Fi are not required: Python sends movement and spells to Unity
on the same computer. This mode uses the editor; a standalone desktop game is
not included.

1. Install Unity Hub and Unity Editor **6000.6.4f1**. Android Build Support is
   optional for this mode.
2. Set up the Python environment using your platform's setup file above. You can
   skip headset installation with `python3 setup_wand.py --skip-apk` on Mac or
   `py -3 setup_wand.py --skip-apk` on Windows. If setup says Quest installation
   is pending, that does not prevent desktop play.
3. Connect the Arduino by USB and close Serial Monitor/other recognition runs.
4. In Unity Hub, add the repo's **`unity`** folder as a project. Open
   **`Assets/Scenes/WandSpellsMeetingDemo.unity`** and press **Play**.
5. Click the **Game** view and press **1**, **2**, or **3** to choose a wand.
6. From the repo folder, run the command for your laptop:

**macOS:**

```bash
.venv/bin/python python/live_predict.py --unity-host 127.0.0.1
```

**Windows (Command Prompt):**

```bat
.venv\Scripts\python.exe python\live_predict.py --unity-host 127.0.0.1
```

Leave the physical wand still until calibration finishes and the live window
appears. After Unity enters the arena, swing/tilt the wand and perform gestures
to cast the element matching the active crystal. Use the mouse crosshair to aim
in both stages while the physical wand selects the spell. Point forward and press **R** in the Python window
to recenter. Focus that window and press **Q/Esc** to stop recognition; stop Play
mode in Unity when finished.

`127.0.0.1` means this computer. Use the commands above instead of **Start Quest
Wand**, which looks for a headset. If no connection is reported, check Unity is
in Play mode with the correct scene and UDP port **5055** is available. You can
also play with the mouse alone using the desktop controls below.

## Desktop game: play solo or PvP

The desktop launcher uses connected Arduinos as controllers and selects the
match automatically. One wand starts a solo match against the Wand Bot; two
wands start local PvP. Each wand has its own gesture recognition. On desktop,
the mouse cursor aims, and spells only hit when the aim ray reaches the opponent.
Press **Space** to lock/unlock the cursor position while making a wand gesture
to cast. Both players start with 100 health, and lightning hits hardest.
Solo mode uses the same level against a bot; the bot moves and adapts its casting
pace to the health lead. In two-player mode, the existing level is shown in
split-screen, with each player moving from the keyboard. The first player to
lose all health loses the round; health resets after a short pause. The IMUs
recognize spell gestures; the mouse aims and the keyboard handles movement.
Keeping the camera level avoids roll from wand motion.

To try the flow in Unity Editor, open
`unity/Assets/Scenes/WandSpellsMeetingDemo.unity` but leave it out of Play mode.
Connect one or two Arduino boards, start the Python command, then switch to
Unity and press **Play while the wand(s) are calibrating**:

**macOS:**

```bash
.venv/bin/python python/play_wand.py
```

**Windows (Command Prompt):**

```bat
.venv\Scripts\python.exe python\play_wand.py
```

Keep each wand still until calibration finishes. The Unity game selects bot or
PvP mode from the number of connected controllers. Press **R** in a recognition
window to recenter the wand(s); press **Q/Esc** to quit. Port order can be set
with `--player1-port` and optional `--player2-port`.

In the game, **WASD** moves Player 1. In two-wand PvP, **arrow keys** move
Player 2. Move the mouse into a player's screen half to aim for them; **Space**
locks or releases the aim point. A recognized wand gesture casts the spell.
Press **F2** to restart a round.

The project includes Unity OpenXR and Meta OpenXR support. Quest duel mode uses
two headsets, one per player. Each player sees a full-screen first-person view,
looks to aim, and uses the left controller stick to move. Both headsets show
their own health as **YOU** and the other player's as **OPPONENT**.

### Quest PvP with two headsets

Build the updated APK from Unity (**Tools → Wand Spells → Build Quest APK**) and
install that same build on both headsets. Put both Quests and the laptop on the
same Wi-Fi. Start the game on each headset. Connect both Arduino wands to the
laptop, then start the Python controller link, routing it to Player 1's Quest IP.
Use the port names shown by your operating system:

**macOS:**

```bash
.venv/bin/python python/play_wand.py --player1-port /dev/cu.usbmodem11101 --player2-port /dev/cu.usbmodem11102 --unity-host 192.168.1.20
```

**Windows (Command Prompt):**

```bat
.venv\Scripts\python.exe python\play_wand.py --player1-port COM3 --player2-port COM4 --unity-host 192.168.1.20
```

Replace the example ports and IP with your actual serial ports and Player 1's
Wi-Fi IP. In Player 1's headset, press **A** on the right
controller to host; in Player 2's headset, press **X** on the left controller to
join. Each headset then has its own tracked POV. Head direction aims spells,
left stick movement moves around the larger courtyard, and each wand's gesture
casts its assigned player's spell. The laptop sends both wand streams to the
host Quest; the headsets exchange movement, aim, health, and spell effects over
UDP port **5060**. The laptop-to-Quest wand link uses UDP port **5055**.

The Quest duel is a LAN prototype: it needs Wi-Fi that allows devices to talk
to each other, and the headset build must be rebuilt after Unity code changes.
The packaged APK may not contain the latest duel code. Local desktop PvP remains
available separately through Unity Editor or the desktop launcher.

### Mixed Quest and desktop PvP (two laptops)

One player can use a Quest while the other plays on a desktop monitor. Each
player connects one Arduino to their own laptop. Both laptops, the Quest, and
both game clients must be on the same Wi-Fi network. Build and install this
branch's APK on the Quest first.

On the Quest player's laptop, start the Python bridge with the Quest's Wi-Fi IP.
Then launch the app on the headset. On the desktop player's laptop, open the
Unity project and duel scene, start Python, and press **Play** in Unity while
the wand is calibrating. Each player should run one of these commands, using
their own Arduino port:

**Mac — Quest player's laptop:**

```bash
.venv/bin/python python/play_wand.py --online-duel --player1-port /dev/cu.usbmodemXXXX --unity-host QUEST_WIFI_IP
```

**Mac — desktop player's laptop:**

```bash
.venv/bin/python python/play_wand.py --online-duel --player1-port /dev/cu.usbmodemYYYY --unity-host 127.0.0.1
```

**Windows — Quest player's laptop (Command Prompt):**

```bat
.venv\Scripts\python.exe python\play_wand.py --online-duel --player1-port COM3 --unity-host QUEST_WIFI_IP
```

**Windows — desktop player's laptop (Command Prompt):**

```bat
.venv\Scripts\python.exe python\play_wand.py --online-duel --player1-port COM4 --unity-host 127.0.0.1
```

Replace the example Arduino ports and `QUEST_WIFI_IP`. Once both games are
running, choose a host: the Quest host presses **A**; the desktop host presses
**H**. The other player joins by pressing **X** on Quest or **J** on desktop.
Each player sees only their own full-screen point of view. The Quest player aims
with their head and moves with the left stick; the desktop player aims with the
mouse and moves with **WASD**. Wand gestures cast spells for the local player.

This peer-to-peer setup is for the same local network. Guest Wi-Fi or client
isolation may block discovery. Playing from different networks would require an
internet relay or a publicly reachable server.

### Build a one-file desktop launcher

The release launcher packages the trained model, Python recognizer, and Unity
desktop game together. Build it on the target operating system; Windows
produces `WandSpells.exe`, while macOS produces `WandSpells.app`.

1. In Unity, use **Tools → Wand Spells → Build Desktop Player → Windows x64**
   or **macOS**. The matching Unity Build Support module must be installed.
2. From the repository root, install packaging dependencies:

   ```bash
   python -m pip install -r python/requirements.txt -r tools/desktop-packaging-requirements.txt
   ```

3. Build the launcher on that same platform:

   ```bash
   python -m PyInstaller desktop_bundle.spec --clean
   ```

The result is in `dist/`. Double-clicking it starts the game and chooses solo
or PvP from the detected Arduino count. End users do not need Unity, Python, or
the setup scripts. The launcher still requires the USB/serial driver for the
connected Arduino boards.

## Editing the Unity game

Open [`unity/`](unity/) in Unity Hub with **6000.6.4f1**, then open
`Assets/Scenes/WandSpellsMeetingDemo.unity`. Install **Android Build Support**,
including SDK, NDK and OpenJDK, if building for Quest. Use
**Tools → Wand Spells → Build Quest APK**. The APK is written to
`unity/Builds/WandSpellsMeetingDemo.apk`; install it on each headset you want to
use. Rerun setup to update a single USB-connected headset.
See [Unity scene instructions](unity/WAND_SPELLS_MEETING.md) for desktop controls.

## Data collection and training reference

The remaining sections explain wiring, recording and training. They are optional
for playing with the included trained model.

## Gestures

| label              | spell     | motion                                        |
|--------------------|-----------|-----------------------------------------------|
| `vertical_swipe`   | fire      | one fast stroke, top to bottom                |
| `horizontal_swipe` | wind      | one fast stroke, left to right                |
| `zigzag`           | lightning | a "Z": right, diagonal down-left, right       |
| `circle`           | water     | one full clockwise circle                     |
| `idle`             | none      | natural non-spell movement, never a spell shape |

## 1. Wiring

| MPU-6050 (GY-521) | Arduino Mega 2560 |
|-------------------|-------------------|
| VCC               | 5V                |
| GND               | GND               |
| SDA               | pin 20            |
| SCL               | pin 21            |

Mount the IMU near the wand tip with the marked "up" arrow always pointing the
same way, and use a long, flexible cable to the Mega so the arm can move freely.

## 2. Flash the Arduino

1. Open `arduino/wand_imu/wand_imu.ino` in the Arduino IDE.
2. Tools > Board > **Arduino Mega or Mega 2560**, Tools > Port > the
   `/dev/cu.usbmodem*` (genuine) or `/dev/cu.wchusbserial*` (CH340 clone) entry.
   A CH340 clone needs the CH340 driver installed first.
3. Upload, then open the Serial Monitor at **115200 baud**.

You should see the header once, then ~100 lines per second:

```
#READY,rate=100,acc=4g,gyro=1000dps,fw=1.0
$,0,1532,0.012,-0.004,1.001,0.183,-0.061,0.122*5B
$,1,1542,0.013,-0.006,0.999,0.244,-0.122,0.061*6E
```

Line format: `$,<seq>,<t_ms>,<ax>,<ay>,<az>,<gx>,<gy>,<gz>*<CS>`, acceleration
in g, rotation in deg/s, and `CS` = XOR of every character between `$` and `*`.
Commands you can type in the Serial Monitor: `R` resend header, `P` pause,
`S` resume.

**Close the Serial Monitor before running the Python scripts** - only one
program can hold the serial port.

## 3. Python setup for data collection

The quick-start setup script above also installs the data-collection dependencies.
For manual setup on macOS:

```bash
cd CEG5205-wand
python3 -m venv .venv
source .venv/bin/activate
pip install -r python/requirements.txt
```

On Windows use `py -3 -m venv .venv`, then
`.venv\Scripts\python.exe -m pip install -r python\requirements.txt`.
To run the utilities below, activate with `.venv\Scripts\activate.bat` in Command
Prompt, or call that environment's Python executable directly.

## 4. Running

Every script auto-detects the Arduino port, waits for the `#READY` header, and
runs a 2 s still-hold gyro calibration. Add `--port /dev/cu.usbmodemXXXX` to
pick the port by hand, or `--fake` to run with synthetic data and no hardware.

```bash
cd python

# Task 1: live 6-channel plot
python live_plot.py
python live_plot.py --fake

# Collect a dataset: 25 reps of each gesture for participant p01
python collect.py --participant <your name> --reps 25 --camera     # with webcam view
python collect.py --participant test --reps 3 --fake       # dry run

# Check what was collected
python check_dataset.py
python check_dataset.py --participant <above used name>

```

### Keys during collection

The OpenCV window must have focus.

| key             | action            |
|-----------------|-------------------|
| Enter / Space   | keep the sample   |
| R               | redo it           |
| S               | skip it           |
| P               | pause / resume    |
| Q or Esc        | save and quit     |

### Recording modes

- `--mode motion` (default): recording starts by itself when the rotation rate
  passes **80 deg/s**, and keeps the **0.2 s before** the trigger. It stops once
  the rate stays below **40 deg/s for 0.3 s**. Samples shorter than 0.25 s or
  longer than 3 s are rejected and you are asked to redo them. `idle` is
  recorded as a fixed 2 s window instead, because it may never move enough to
  trigger.
- `--mode timed`: a 3 s countdown, then a fixed `--window` recording (5 s by
  default).

Every threshold is in the `CONFIGURATION` block at the top of `collect.py`.

## 5. Output layout

```
data/
  index.csv                                  one row per kept sample
  p01/
    session.json                             config, firmware header, gyro bias
    vertical_swipe/
      vertical_swipe_001.csv                 t_s, ax..gz (bias-corrected), seq
      vertical_swipe_001.png                 2D trace of that sample
    ...
  _check/traces_<label>.png                  grids written by check_dataset.py
```

`index.csv` columns: `participant, label, spell, rep, file, n_samples,
duration_s, mode, dropped_pct, timestamp`.

Collection is **resumable**: re-running the same participant continues from the
counts already on disk. If the USB cable is pulled the app shows "reconnecting",
waits for `#READY` again, recalibrates, and carries on.

### Augmented training data

Run `python/augment_dataset.py` to generate repetitions 26–100 for each gesture
of each participant except Jay, whose 100-per-gesture set was already present.
Each of the 25 recorded repetitions supplies three variants, using a distinct
deterministic seed. The script makes small, smooth timing changes (up to 7%
duration and 3.5% local bend), 3% channel gain changes, small baseline offsets,
and smoothed sensor noise. It keeps the IMU axes fixed and clips to sensor
limits. Re-running it leaves existing CSVs untouched.

Generated rows have `mode=augmented` in `data/index.csv`.
`data/augmentation_manifest.csv` gives each generated file's recorded source,
random seed, and transformation parameters. The generated `seq` column is a
sample counter, not a hardware packet sequence. Arun and Sujyot had recorded
CSVs missing from the index; those are indexed with blank capture timestamps
and packet-loss fields because that metadata is unavailable. Earlier
Windows-style index paths are normalized to forward slashes.

There are 100 CSVs per gesture and 500 per participant. These are still only
25 independent recordings per gesture for each newly augmented participant.
Split by participant or recorded source before augmentation; never put an
augmented trace and its source in different train/test folds. For an unbiased
final evaluation, use held-out recorded traces or held-out participants, not
augmented traces as independent test observations. Jay's existing augmented
files predate this manifest and are not attributed to source recordings here.

## 6. Collecting a real dataset

Follow this once per session. Steps 1 and 2 only matter the first time, or
after you remount the IMU.

### Step 1 - check the trace orientation (2 min, once)

```bash
cd python && python collect.py --participant check --reps 1
```

Perform each gesture as written and look at the review trace. It must look like
the gesture you just did, with the green dot where you started:

| what you see | fix in `python/imu_common.py` |
|---|---|
| vertical swipe draws a **horizontal** line | swap `TRACER_X_AXIS` and `TRACER_Y_AXIS` |
| vertical swipe goes **bottom to top** | flip `TRACER_Y_SIGN` |
| horizontal swipe goes **right to left** | flip `TRACER_X_SIGN` |

Fix vertical and horizontal first; the Z and the circle then come out right on
their own. The mapping measured on this wand is `x = gx`, `y = -gy`.

While you are here, check the trigger: if recording does not start on a gesture
you consider fast, lower `START_DPS` in `collect.py`; if it fires while you are
just holding the wand, raise it.

### Step 2 - clear the throwaway data

```bash
rm -rf ../data/check
python check_dataset.py --prune
```

`--prune` rewrites `index.csv` with only the rows whose CSV still exists, and
keeps the old index as `index.<timestamp>.bak.csv`. Run it any time you delete
samples by hand - otherwise the index keeps pointing at files that are gone.

### Step 3 - the real run, one person at a time

```bash
python collect.py --participant <your name> --reps 25 --camera
```

Budget **15-20 min per person**. Use `p01`, `p02`, ... and never reuse an id:
the model is evaluated on people it has not seen, so the split is by
participant.

- Same grip, same starting position, same spot in the room, every rep.
- Pause about a second between reps so the trigger can re-arm.
- Press **R** whenever the trace does not look like the gesture. That is what
  the review screen is for.
- Watch the circle direction - **clockwise**. It is the easiest gesture to do
  backwards without noticing.
- For `idle`, do what a player actually does between spells: hold the wand,
  fidget, lower the arm, look around. Never a spell shape, and vary it across
  the 25 reps instead of holding still every time.
- Press **Q** any time; rerunning the same command continues where you stopped.

Drop `--camera` if the laptop cannot see the person. It only helps you re-check
labels later.

### Step 4 - after each person

```bash
python check_dataset.py --participant <above used name>
```

Then open `data/_check/traces_<label>.png`. One trace that does not match the
other 24 is a mislabelled sample: note the participant and rep from the cell
title, delete that CSV and PNG, and run `--prune`.

Aim for **at least 4-5 people**, 3 absolute minimum, or you cannot hold a person
out for testing.

### If something goes wrong

| symptom | cause / fix |
|---|---|
| `No Arduino serial port found` | Serial Monitor still open, cable not in, or a CH340 clone without its driver |
| no `#READY` header | `wand_imu.ino` not flashed, or the wrong board selected |
| calibration keeps failing | the wand moved - put it on the table and press any key to retry |
| status bar shows dropped > 0 % | bad USB cable or hub; try a different port |
| gyro peaks pinned near 1000 deg/s | the swipes are saturating the range; see `GYRO_CONFIG` in the sketch |
| traces look like noise | the axis mapping is wrong - go back to step 1 |

## 7. Training and live recognition

Two scripts, both run from `python/`. Training compares several classifiers and
keeps only the winner; the demo loads that winner.

### Train

```bash
python train_gestures.py
```

It reads `data/index.csv`, builds 142 features per sample
(`gesture_features.py`), and fits every model in `build_models()` over the same
folds:

| model | what it is |
| --- | --- |
| `logreg` | multinomial logistic regression on scaled features |
| `svm_rbf` | RBF-kernel SVM, probability-calibrated |
| `knn` | 7 nearest neighbours, distance-weighted |
| `random_forest` | 400 trees |
| `extra_trees` | 400 randomised trees |
| `hist_gb` | histogram gradient boosting |
| `mlp` | 128-64 neural net with early stopping |

The comparison table is the output that matters. The winner (highest accuracy,
macro-F1 breaking ties) is refitted on everything and saved to
`models/gesture_model.joblib`, with `models/gesture_model_confusion.png`
alongside it for the report.

Useful flags:

```bash
python train_gestures.py --dry-run             # report the dataset, fit nothing
python train_gestures.py --real-only           # recorded samples only
python train_gestures.py --models svm_rbf,random_forest
python train_gestures.py --keep-all            # include the excluded classes
```

### How the dataset is handled

Three known problems in `data/` are dealt with in the loader, so nobody has to
edit anyone else's CSVs:

- **Jay's repetitions 26-100 are augmented but recorded as `mode=motion`** and
  are absent from the manifest. The `mode` column is therefore not trusted: a
  row counts as augmented if the manifest lists it *or* its CSV restarts `seq`
  at 0, which only the augmenter does.
- **devam's `idle` recordings contain vigorous movement** - more total rotation
  than his own swipes, and 21 of 25 would fire the live motion trigger. They
  teach the model that a hard swing means "cast nothing", so they are dropped
  by default (`EXCLUDED_CLASSES`, re-enable with `--keep-all`).
- **An augmented trace must never be scored against the recording it came
  from.** Folds are grouped by participant, which keeps every derived trace
  with its source automatically.

With two or more people the split is leave-one-participant-out: train on
everyone else, test on the person held out. That is the only number worth
putting in the report, and the script prints what it means next to it. With one
participant it falls back to grouping by source recording and says plainly that
the result does not predict how the model handles someone new.

### Live demo

```bash
python live_predict.py --camera
```

It uses the same motion trigger as `collect.py`, so a live movement is cut the
same way a training sample was. Each finished movement gets a predicted spell,
a confidence, and a bar per class. Below `--min-confidence` (default 0.55) it
shows "unsure" and casts nothing.

| key | does |
| --- | --- |
| `1`-`5` | tag what the last gesture really was |
| `C` | clear the trace |
| `Q` / Esc | quit and print the summary |

Tagging is what makes a demo run measurable: the summary prints live accuracy
over everything tagged, and lists what it got wrong. Every segment is saved to
`data/_live/` with its trace PNG, plus `live_log.csv` for the whole run, so a
bad run can be looked at afterwards instead of guessed about.

```bash
python live_predict.py --fake            # no hardware
python live_predict.py --min-confidence 0.40
python live_predict.py --no-log
```

If the webcam does not open, the demo keeps running on a plain background and
says so - on macOS grant camera access under System Settings > Privacy &
Security > Camera.

## 8. Notes

- The 2D trace is an "air mouse": it integrates the **gyro** rates once.
  Acceleration is never double-integrated into position - that drifts away
  within a second and would make the traces useless.
- The trace mapping was measured from p01's first real session and is now
  `x = gx`, `y = -gy` (`TRACER_X_AXIS` / `TRACER_Y_AXIS` / the sign constants at
  the top of `python/imu_common.py`). If you remount the IMU and the traces come
  out mirrored or rotated, change those four constants - that is the only place
  the mapping is defined, and it only affects the on-screen trace, never the
  saved CSV data. After changing them, run
  `python check_dataset.py --redraw` to re-render the PNGs of data you already
  collected.
- The Arduino sends through one constant, `#define OUT Serial`. Changing it to
  `Serial1` (TX1 = pin 18, RX1 = pin 19) sends the same stream to a wireless
  module later, with no other change to the sketch.
- The first time you use `--camera`, macOS asks for camera permission for your
  terminal app: System Settings > Privacy & Security > Camera.

## 9. Test plan

### Step 1 - Arduino alone, in the Serial Monitor

1. Upload `wand_imu.ino`, open the Serial Monitor at 115200 baud.
2. Check that `#READY,rate=100,acc=4g,gyro=1000dps,fw=1.0` appears once.
3. Check that data lines stream and that `seq` increases by 1 each line.
4. With the wand still: `az` should be about +/-1.000 (whichever axis points
   up or down), and all three gyro values should sit within a few deg/s of 0.
5. Rotate the wand slowly through 90 degrees: the matching gyro channel should
   swing to a few tens of deg/s and return to 0. A fast swipe should peak in the
   hundreds without sticking at 1000 (that would mean saturation).
6. Type `P` then Enter: the stream stops. Type `S`: it resumes. Type `R`: the
   header is printed again.
7. Unplug the IMU's SDA wire: `#I2C_ERR,<count>` lines should appear about once
   per second. Plug it back in and the stream should recover.

### Step 2 - Python live plot

1. **Close the Serial Monitor.**
2. `python live_plot.py`
3. The console should print the detected port and the `#READY` header, then ask
   you to hold still for 2 s and print the gyro bias.
4. In the window: the status bar should read about **100 Hz**, **0.0 % dropped**
   and 0 checksum errors. The accelerometer traces should sit near 0/0/1 g and
   the gyro traces near 0 when the wand is still, and all six should respond
   immediately when you move it.
5. `python live_plot.py --fake` should do the same with no hardware attached.

### Step 3 - a 3-rep dry run of collect.py

```bash
python collect.py --participant test --reps 3 --fake
```

1. The window shows the connection, then the calibration, then
   "FIRE: vertical swipe, 1 / 3" with the motion text.
2. After "Get ready" it arms, the synthetic gesture fires the trigger, and the
   review screen appears with a 2D trace and the 6-channel plot.
3. Press Enter to keep, R to redo, S to skip; try each one.
4. Let it run through all 15 samples, then check:

```bash
python check_dataset.py --participant test
```

The counts table should show the gestures you kept, no problems should be
flagged, and `data/_check/traces_<label>.png` should show traces that match
their labels (a vertical line, a horizontal line, a Z, a clockwise circle, and a
wandering squiggle for idle).

5. Repeat the same 3-rep run with the real wand
   (`python collect.py --participant test --reps 3`), then pull the USB cable
   mid-session: the status bar should turn red, the app should say
   "reconnecting", and after you plug it back in it should recalibrate and carry
   on without crashing.
6. Delete the throwaway data when you are done:
   `rm -rf data/test && ` remove its rows from `data/index.csv`.

## Run the Unity scene on your computer

For physical wand control without a headset, follow
[Play on your computer with the Arduino](#play-on-your-computer-with-the-arduino-no-quest)
above. The steps here describe mouse-only desktop play.

1. Install **Unity Hub** and Unity Editor **6000.6.4f1**. Android Build Support is only needed if you plan to build for Quest.
2. Download this repository (or run `git clone https://github.com/Qasim1507/CEG5205-wand.git`).
3. In Unity Hub, choose **Add project from disk** and select the **`unity`** folder inside the downloaded repository. Open it and let Unity finish importing packages.
4. In the Unity Project window, open **`Assets/Scenes/WandSpellsMeetingDemo.unity`**, then press **Play**.
5. Press **1**, **2**, or **3** to choose a wand. In the arena, move the mouse to aim and **left-click** to shoot the active crystal. Press **R** to recenter the crosshair or **Esc** to release the mouse.

The desktop scene runs without an IMU or Quest headset. The included Quest APK is
at `unity/Builds/WandSpellsMeetingDemo.apk`; live IMU gestures, continuous rotation,
and Quest gaze selection are connected. Use the quick start above to play on Quest.
