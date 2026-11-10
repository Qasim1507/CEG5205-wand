# Player Manual

This manual covers the wand game modes on this branch: desktop solo, desktop
local PvP, Quest solo, two-Quest PvP, and mixed Quest-versus-desktop PvP.

## 1. What you need

- A Windows or macOS laptop with 64-bit Python 3.12–3.14.
- One Arduino Mega with an MPU-6050 wand for each player. Each Arduino connects
  to that player's laptop over USB.
- For Quest play, a Meta Quest with the game APK installed. Developer Mode and
  USB debugging are needed to install a newly built APK.
- For network PvP, both game clients and laptops must be on the same Wi-Fi.
  Guest networks and campus networks may block devices from finding each other.

The Arduino firmware is in `arduino/wand_imu/wand_imu.ino`. Wiring and upload
steps are in [the repository README](README.md#1-wiring). Close Arduino Serial
Monitor before running Python; it holds the serial port open.

## 2. One-time laptop setup

From the repository folder, run the setup launcher for your system. It creates
`.venv`, installs Python packages and Android connection tools, and installs the
APK in `unity/Builds/WandSpellsMeetingDemo.apk` on one connected Quest.

| System | Setup launcher | Terminal alternative |
| --- | --- | --- |
| macOS | Double-click `Setup Quest Wand.command` | `python3 setup_wand.py` |
| Windows | Double-click `Setup Quest Wand.cmd` | `py -3 setup_wand.py` |

For desktop-only play, you can set up Python without installing anything to a
Quest: use `python3 setup_wand.py --skip-apk` on Mac or
`py -3 setup_wand.py --skip-apk` on Windows.

## 3. Open the Unity project

Unity is required for Unity Editor desktop play and for building a new Quest APK.

1. Install Unity Hub and Unity Editor **6000.6.4f1**. For Quest builds, include
   **Android Build Support**, Android SDK & NDK Tools, and OpenJDK.
2. In Unity Hub, choose **Projects → Add → Add project from disk** and select
   this repository's `unity` folder.
3. Wait for Unity to import the project assets. They are already included; do
   not import the `Assets` folder separately.
4. In Unity's Project panel, open `Assets/Scenes/WandSpellsMeetingDemo.unity`.

To build the Quest app, select **Tools → Wand Spells → Build Quest APK**. The
APK is saved to `unity/Builds/WandSpellsMeetingDemo.apk`. Connect each headset
to the laptop in turn and run the setup launcher to install the same build.

## 4. Desktop play on one computer

Open the Unity project and duel scene, but leave Play mode stopped. Connect the
Arduino wand(s), then start the Python command below. Switch back to Unity and
press **Play while Python is calibrating**. Keep each wand still until
calibration finishes.

### One player against the bot

**macOS:**

```bash
.venv/bin/python python/play_wand.py --player1-port /dev/cu.usbmodemXXXX
```

**Windows Command Prompt:**

```bat
.venv\Scripts\python.exe python\play_wand.py --player1-port COM3
```

### Two players on one computer

**macOS:**

```bash
.venv/bin/python python/play_wand.py --player1-port /dev/cu.usbmodem11101 --player2-port /dev/cu.usbmodem11102
```

**Windows Command Prompt:**

```bat
.venv\Scripts\python.exe python\play_wand.py --player1-port COM3 --player2-port COM4
```

Replace the example ports with the ports assigned to your boards. On Windows,
find them in **Device Manager → Ports (COM & LPT)**. On Mac, they commonly start
with `/dev/cu.usbmodem` or `/dev/cu.usbserial`.

### Desktop controls

- Player 1 moves with **WASD**. In local two-player mode, Player 2 moves with
  the **arrow keys**.
- Aim with the mouse. In local PvP, move the mouse into your side of the screen.
- Press **Space** to lock or release the aim point, then make a wand gesture to
  cast. A hit requires the aim ray to reach the opponent.
- Press **R** in the Python recognition window to recenter. Press **Q** or
  **Esc** there to stop recognition; stop Unity Play mode when finished.

The game selects bot or local PvP from the number of connected Arduino wands.

## 5. Quest solo against the bot

Install the APK on the Quest and connect the Arduino to the laptop. Start the
Quest app, then run Python with the Quest's Wi-Fi IP. This duel command starts a
bot match; the ordinary `Start Quest Wand` launcher starts the tutorial flow.

**macOS:**

```bash
.venv/bin/python python/play_wand.py --player1-port /dev/cu.usbmodemXXXX --unity-host 192.168.1.20
```

**Windows Command Prompt:**

```bat
.venv\Scripts\python.exe python\play_wand.py --player1-port COM3 --unity-host 192.168.1.20
```

Replace `192.168.1.20` with the Quest's current Wi-Fi IP. Look to aim; use the
Quest left stick to move. Perform a wand gesture to cast. To stop, quit the
Python window or close the game on the headset.

## 6. PvP with two Quest headsets

Build and install the same branch APK on both headsets. Connect both Arduino
wands to one laptop and put both headsets and the laptop on the same Wi-Fi.
Start both apps, then run Python on the laptop, targeting Player 1's Quest IP:

**macOS:**

```bash
.venv/bin/python python/play_wand.py --player1-port /dev/cu.usbmodem11101 --player2-port /dev/cu.usbmodem11102 --unity-host 192.168.1.20
```

**Windows Command Prompt:**

```bat
.venv\Scripts\python.exe python\play_wand.py --player1-port COM3 --player2-port COM4 --unity-host 192.168.1.20
```

Player 1 presses **A** on the right controller to host. Player 2 presses **X**
on the left controller to join. Each headset renders that player's own view;
look to aim and use the left stick to move.

## 7. Mixed PvP: one Quest and one desktop player

Each player uses their own laptop and one Arduino. Both laptops and the Quest
must be on the same Wi-Fi. Build and install the branch APK on the Quest first.

### Quest player's laptop

Start the Quest app. Then run Python, using the Quest's Wi-Fi IP:

**macOS:**

```bash
.venv/bin/python python/play_wand.py --online-duel --player1-port /dev/cu.usbmodemXXXX --unity-host 192.168.1.20
```

**Windows Command Prompt:**

```bat
.venv\Scripts\python.exe python\play_wand.py --online-duel --player1-port COM3 --unity-host 192.168.1.20
```

Replace the example port and IP with the Quest player's Arduino port and Quest
Wi-Fi IP.

### Desktop player's laptop

Open the Unity project and duel scene, but leave Play mode stopped. Connect the
desktop player's Arduino and run Python. Press Unity **Play while calibration
is running**.

**macOS:**

```bash
.venv/bin/python python/play_wand.py --online-duel --player1-port /dev/cu.usbmodemYYYY --unity-host 127.0.0.1
```

**Windows Command Prompt:**

```bat
.venv\Scripts\python.exe python\play_wand.py --online-duel --player1-port COM4 --unity-host 127.0.0.1
```

Each player sees only their own full-screen view. The Quest player aims with
their head and moves with the left stick. The desktop player aims with the
mouse and moves with **WASD**. Each wand casts for its local player.

For either mixed-PvP client, the host presses **A** on Quest or **H** on desktop.
The other player joins with **X** on Quest or **J** on desktop. Choose exactly
one host.

## 8. Network and troubleshooting

- LAN PvP uses UDP **5055** for laptop-to-game wand input and UDP **5060** for
  game-to-game state and spells. Allow Python and Unity through the private
  network firewall if prompted.
- Both duel clients must be on the same local network. Guest Wi-Fi, campus
  client isolation, or separate networks can prevent host/join discovery.
- If Python cannot find an Arduino, close Serial Monitor and check the selected
  port and USB cable.
- If calibration keeps retrying, put the wand on a stable surface and leave it
  still until Python reports calibration complete.
- If no game connection appears, confirm the Quest app or Unity scene is
  running and that `--unity-host` points to the correct device. `127.0.0.1` is
  only for a Unity game running on that same laptop.
- For Quest installation failures, wear the headset, enable Developer Mode,
  accept the USB debugging prompt, and retry setup. Without Developer Mode, a
  newly built APK cannot be sideloaded.

## 9. Current limits

- Mixed Quest/desktop PvP is an experimental same-Wi-Fi mode. It does not connect
  players over the public internet; that would need an internet relay or server.
- The two-headset and mixed-device modes need hands-on validation with the actual
  Quest(s), Arduino(s), and separate laptops. Unity Editor builds are not
  performed by the Python launcher.
- The IMU tracks wand rotation, not its position in the physical room.
