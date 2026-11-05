# Wand Spells — Unity 6 demo

Open `Assets/Scenes/WandSpellsMeetingDemo.unity` in Unity 6000.6.4f1 and press **Play**. Click the **Game** view so it receives input.

1. The game opens in the wand hall. Click one of the three displayed wands, or press **1 Emberwood**, **2 Frostspire**, or **3 Sunforged**. The gate opens and the view travels into the arena.
2. After the gate opens, the game captures the mouse and shows a cyan crosshair. Move the mouse to steer the crosshair and wand together. The target ring appears when you are aimed at the active crystal.
3. **Left-click** to fire. Only the current crystal can be broken. Hit the five crystals from **left to right**; all five reform automatically at the end. There is no Q/E target selection or draw-to-cast step. Press **R** to center the crosshair. Press **Esc** to release the mouse from the Game view; click the Game view to capture it again.

The arena floor is an animated lava lake with a narrow stone walkway. The crystals are original procedural 3D meshes inspired by the supplied references; no Shutterstock art was copied. The opening hall, gate travel, mouse aim and impact effects are implemented in this Unity scene.

An external Python program can send `{"spell":"fire","conf":0.93}` or any of the five spell names over UDP port **5055**. From the project folder, run `python wand_demo_sender.py arcane` while the Unity scene is playing. UDP sends simulated model predictions; desktop clicks cast a spell on the currently active crystal. Wand choice changes the wand's appearance.

The Android Quest build is `Builds/WandSpellsMeetingDemo.apk`. It compiled, but has **not** been tested on a headset. The scene currently uses a virtual chamber rather than passthrough, and the wand is camera attached. The mouse aiming and clicking controls are desktop controls, not Quest hand/controller controls. No real IMU or trained ML model is connected yet. To meet the project requirement, collect labelled IMU data, train/evaluate a classifier, and send its predictions to port 5055.

The scene uses an original generated floor texture, [Poly Haven Medieval Blocks 03](https://polyhaven.com/a/medieval_blocks_03) CC0 wall textures, and [Cinzel](https://github.com/google/fonts/tree/main/ofl/cinzel) under the SIL Open Font License. The font license is included in `Assets/WandSpells/Fonts/OFL.txt`.
