# Wand Spells — Unity 6 demo

Open `Assets/Scenes/WandSpellsMeetingDemo.unity` in Unity 6000.6.4f1 and press **Play**. Click the **Game** view so it receives input.

1. The game opens in the wand hall. Click one of the three displayed wands, or press **1 Emberwood**, **2 Frostspire**, or **3 Sunforged**. The gate opens and the view travels into the arena.
2. After the gate opens, the game captures the mouse and shows a cyan crosshair. Move the mouse to steer the crosshair and wand together. The target ring appears when you are aimed at the active crystal.
3. **Left-click** to fire the active crystal's configured element during desktop testing.
   Sensor casts must match its element. Clear the five crystals from **left to right**
   to enter the moving-dummy stage. Aim at the dummy and match its crystal colour;
   a matching hit knocks it down before it recovers with another element.
   Press **R** to center the crosshair. Press **Esc** to release the mouse from the
   Game view; click the Game view to capture it again.

The arena floor is an animated lava lake with a narrow stone walkway. The crystals are original procedural 3D meshes inspired by the supplied references; no Shutterstock art was copied. The opening hall, gate travel, mouse aim and impact effects are implemented in this Unity scene.

The laptop's trained IMU classifier now sends completed gestures over UDP port **5055**.
See [the laptop/Quest guide](../LAPTOP_QUEST.md) for the launcher, setup and gesture mappings.
Manual packets such as `{"spell":"fire","conf":0.93}` remain supported for diagnostics;
`wand_demo_sender.py` is a simulated sender, not the live ML integration.

The Android Quest build is `Builds/WandSpellsMeetingDemo.apk`. Install on a USB-authorized
Quest with `./install-quest-mac.sh wand`. In Quest, look at a displayed wand for
1.2 seconds to select it, then cast by moving the real IMU wand. The view teleports
into the arena after the gate opens. In stage one, aim at the active crystal and
match its element to score. Every spell goes toward the crosshair, including
misses, which give no points. In both stages, **head tracking controls aiming**: look
through the centered crosshair at the dummy and cast the element matching its
crystal. The left joystick is not used. Head aiming updates the target marker
continuously while the physical IMU still animates the wand. The scene uses a virtual chamber
rather than passthrough. The virtual grip is camera attached, while its orientation
follows continuous IMU pose packets. Physical wand position is not tracked. Press
A/X on a Quest controller (or R in the laptop predictor) to center its direction.
Mouse aiming/clicking remains available on desktop.

## Local solo and PvP modes

The desktop game selects its mode from the connected Arduino count. Connect one
Arduino for a match against the Wand Bot, or two for local PvP. In Unity Editor,
open this scene but leave Play mode stopped. Start `python/play_wand.py` from the
repository root, then press Unity's **Play** button while the wand(s) calibrate.
Each wand is recognized independently, and spells travel toward the
opponent when the aim ray hits. The duel scene has an expanded courtyard, glowing
runes, floating crystals, spell trails, and impact sparks.

For Quest PvP, build and install the updated APK on **two headsets**. Put both
headsets and the laptop on the same Wi-Fi. Connect two Arduino wands to the
laptop and run `python/play_wand.py --player1-port PORT1 --player2-port PORT2
--unity-host PLAYER1_QUEST_IP` from the repository root. In Player 1's headset,
press **A** on the right controller to host; Player 2 presses **X** on the left
controller to join. Each headset renders one full-screen first-person view;
head direction aims spells, the left stick moves, and each wand controls its
player's casts. Player 1's Quest receives both Arduino streams from the laptop,
then relays duel state and spell effects to Player 2 over the LAN. See the
repository README for the complete command and APK build location.

A Quest player can also play against a desktop player on a second laptop. Each
laptop connects one Arduino and runs `python/play_wand.py --online-duel` with its
own port. The Quest laptop routes Python to the headset IP; the desktop laptop
uses `--unity-host 127.0.0.1`, opens this scene, starts Python, then presses Play
while calibration is running. On Quest press **A** to host or **X** to join; on
desktop press **H** to host or **J** to join. Both game clients must be on the
same Wi-Fi. Each client renders only its local player's view. The Quest uses
head aim and left-stick movement; desktop uses mouse aim and WASD movement.

The scene uses an original generated floor texture, [Poly Haven Medieval Blocks 03](https://polyhaven.com/a/medieval_blocks_03) CC0 wall textures, and [Cinzel](https://github.com/google/fonts/tree/main/ofl/cinzel) under the SIL Open Font License. The font license is included in `Assets/WandSpells/Fonts/OFL.txt`.
