# Laptop recognition -> Quest 3

Arduino + IMU --USB--> laptop Python model --Wi-Fi UDP 5055--> Quest Unity game.
The Arduino stays plugged into the laptop. Quest runs the Android game itself;
its USB cable is needed to install the APK and conveniently find its Wi-Fi IP.
There is no Quest Link/PC VR requirement, and no Arduino USB plugin on Quest.

## First setup

1. Follow the [README quick start](README.md#quick-start-play-the-game): install
   Python 3.12–3.14, connect the Quest by USB and accept USB debugging, then run
   **Setup Quest Wand.command** (Mac) or **Setup Quest Wand.cmd** (Windows).
   Setup installs dependencies, locates/downloads Android tools, and installs the
   included `unity/Builds/WandSpellsMeetingDemo.apk`. Unity is optional for playing.
2. For scene changes, open `unity/` with Unity **6000.6.4f1** plus Android Build
   Support, SDK, NDK and OpenJDK. Build with **Tools > Wand Spells > Build Quest APK**,
   then rerun setup to install the rebuilt app.
3. Put laptop and Quest on the same Wi-Fi. Campus or guest networks may isolate
   devices; use a shared hotspot/private router if the game never replies.

## Run the demo

Double-click **Start Quest Wand.command** on Mac or **Start Quest Wand.cmd** on Windows.
It finds the authorized USB Quest,
launches Wand Spells, obtains its Wi-Fi IP, and starts the live predictor.
Keep the physical wand completely still during the initial calibration.
If movement interrupts calibration, startup retries automatically up to ten times.
Wait for the live window before picking up the wand.
Wait for **Quest link: CONNECTED** in the laptop output.

In Quest, look directly at one of the three displayed wands for 1.2 seconds to
select it. The gate opens and you enter the arena. Then perform a gesture and
pause briefly to finish it. Match the active crystal's element to break it.
After the five crystals, the game enters the moving-dummy stage. On Quest, look
at the dummy to aim the centered crosshair, then cast the element matching its
crystal (red fire, blue water, green wind, yellow lightning). Head tracking steers
the shot independently of wand gestures; the target marker shows when the ray
is on the dummy. On desktop, aiming uses the mouse crosshair.
The crystal stage retains automatic targeting for sensor casts; the dummy stage
requires aiming. The system does
not track the physical wand's position in the room. Its grip stays at a fixed
offset from the view, but its rotation now follows the IMU continuously, independent
of the classifier. Desktop mouse aiming and clicks still work in the editor.

## Real-time wand rotation

The laptop integrates the calibrated gyroscope at the sensor rate and sends the
latest orientation up to 50 times per second. Gentle accelerometer gravity correction
reduces tilt drift while the wand is relatively still; heading can drift because
MPU-6050 has no magnetometer. These calculations do not change the classifier inputs.
Unity smooths the orientation and rejects reordered pose packets. The laptop overlay
shows how many poses Quest received and the angle from neutral.

Point the physical wand forward and press **R** in the laptop recognition window,
or **A/X** on a Quest controller, to set the current direction as neutral. Recenter
again if heading drifts. The receiver anchors rotation to the direction you were
facing at recenter time, so turning your head does not deliberately steer the wand.
Pose packets and spell predictions are separate: movement appears before a gesture
finishes, and idle/uncertain gestures still move the wand without casting spells.

Default axis mapping matches the collection tracer (`gx` horizontal, `gy` vertical).
If a different sensor mounting swaps directions, use `--wand-axes y,x,-z` with
an appropriate signed permutation of x,y,z. Check physical directions while wearing
the headset; a quaternion unit test cannot establish the mounting orientation.
There is no measured translation: moving a wand sideways without rotating it will
not move the virtual grip. Attaching a tracked Quest controller to the wand would
be a separate way to obtain position.

| Gesture | Dataset spell | Unity effect |
| --- | --- | --- |
| Vertical swipe | fire | fire |
| Horizontal swipe | wind | wind (green) |
| Zigzag | lightning | lightning (yellow) |
| Circle | water | water (blue) |
| Idle / rejected / confidence below 55% | none | no spell |

Dataset/model labels are unchanged. The updated game accepts the four canonical
elements fire, water, wind and lightning directly.

Predictions go through the same segmentation/features as dataset collection.
Only completed confident gestures are sent. Ping replies verify that the Unity
app is actually receiving packets; cast replies distinguish accepted casts from
ones ignored while selecting a wand or playing an animation. Sequence IDs reject
duplicates. Casts are sent once, without retry or a disconnected backlog, so a
lost Wi-Fi packet requires another gesture rather than firing late.

Quit the laptop overlay with Q/Esc (focus that window), or Ctrl-C in its terminal.
Each run is saved under ignored `local_runs/`; it is not added to training data.

## Diagnostics / running without the Quest USB cable

```sh
.venv/bin/python python/quest_wand.py --check
.venv/bin/python python/quest_wand.py --quest-ip 192.168.1.123 --no-launch
.venv/bin/python python/quest_wand.py --headless --duration 15 --no-log
# Test laptop model with an editor game listening locally:
.venv/bin/python python/live_predict.py --unity-host 127.0.0.1
```

The IP can change when reconnecting Wi-Fi. USB auto-discovery avoids typing it.
For multiple Android devices, pass `--device SERIAL`. A reply confirms network
delivery; a successful physical gesture still needs testing while wearing Quest.
If serial calibration fails, set the wand down and restart. If the USB Arduino
reconnects mid-demo, restart to measure a fresh bias.

## Verified on this Mac / Quest — 6 November 2026

- Arduino firmware streamed at 100 Hz with 0% dropped readings and no checksum/I2C errors.
- Ten Python bridge/orientation tests and three Unity PlayMode tests passed; Android APK built and installed.
- The installed Quest app acknowledged Wi-Fi pings and reached the arena through gaze selection.
- Live Arduino gestures were classified on the laptop; Quest acknowledged accepted fire,
  frost and arcane casts. A cast during an animation was correctly reported as ignored.
- Recognition was left running. This verifies the live sensor/model/network/game path;
  player confirmation of appearance and comfort is still useful.
- The rotation update was rebuilt and installed. Actual Quest telemetry increased from
  7 to 360 received poses over ten seconds, with changing angles of 9–31 degrees
  during physical wand movement; live spell casts continued to be acknowledged.
