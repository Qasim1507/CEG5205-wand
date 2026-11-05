# CEG5205 wand - IMU gesture data collection

Sensor side of the mixed-reality wand project: an MPU-6050 on the wand streams
to an Arduino Mega over I2C, the Mega streams ASCII lines to a MacBook over
USB, and Python records labelled gesture samples for the ML classifier.

```
MPU-6050 --I2C--> Arduino Mega 2560 --USB--> MacBook (Python)
                                               live_plot.py    (Task 1 demo)
                                               collect.py      (dataset)
                                               check_dataset.py (sanity check)
```

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

## 3. Python setup (Python 3.10+, macOS)

```bash
cd CEG5205-wand
python3 -m venv .venv
source .venv/bin/activate
pip install -r python/requirements.txt
```

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
