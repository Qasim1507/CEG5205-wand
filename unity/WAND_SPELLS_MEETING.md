# Wand Spells — Unity 6 demo

Open `Assets/Scenes/WandSpellsMeetingDemo.unity` in Unity 6000.6.4f1 and press **Play**. Click the **Game** view so it receives input.

1. The game opens in the wand hall. Click one of the three displayed wands, or press **1 Emberwood**, **2 Frostspire**, or **3 Sunforged**. The gate opens and the view travels into the arena.
2. After the gate opens, the game captures the mouse and shows a cyan crosshair. Move the mouse to steer the crosshair and wand together. The target ring appears when you are aimed at the active crystal.
3. **Left-click** to fire. Only the current crystal can be broken. Hit the five crystals from **left to right**; all five reform automatically at the end. There is no Q/E target selection or draw-to-cast step. Press **R** to center the crosshair. Press **Esc** to release the mouse from the Game view; click the Game view to capture it again.

The arena floor is an animated lava lake with a narrow stone walkway. The crystals are original procedural 3D meshes inspired by the supplied references; no Shutterstock art was copied. The opening hall, gate travel, mouse aim and impact effects are implemented in this Unity scene.

The laptop's trained IMU classifier now sends completed gestures over UDP port **5055**.
See [the laptop/Quest guide](../LAPTOP_QUEST.md) for the launcher, setup and gesture mappings.
Manual packets such as `{"spell":"fire","conf":0.93}` remain supported for diagnostics;
`wand_demo_sender.py` is a simulated sender, not the live ML integration.

The Android Quest build is `Builds/WandSpellsMeetingDemo.apk`. Install on a USB-authorized
Quest with `./install-quest-mac.sh wand`. In Quest, look at a displayed wand for
1.2 seconds to select it, then cast by moving the real IMU wand. The view teleports
into the arena after the gate opens. Sensor casts automatically hit the active
crystal, preserving the existing game behaviour. The scene uses a virtual chamber
rather than passthrough. The virtual grip is camera attached, while its orientation
follows continuous IMU pose packets. Physical wand position is not tracked. Press
A/X on a Quest controller (or R in the laptop predictor) to center its direction.
Mouse aiming/clicking remains available on desktop.

The scene uses an original generated floor texture, [Poly Haven Medieval Blocks 03](https://polyhaven.com/a/medieval_blocks_03) CC0 wall textures, and [Cinzel](https://github.com/google/fonts/tree/main/ofl/cinzel) under the SIL Open Font License. The font license is included in `Assets/WandSpells/Fonts/OFL.txt`.
