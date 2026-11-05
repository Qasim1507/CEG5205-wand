# Test Robot Surgery Trainer with Meta XR Simulator on Windows

1. Close this Unity project if it is already open.
2. In File Explorer, double-click `Start-Quest-Simulator.cmd` in this project folder. It opens Meta XR Simulator and Unity 6000.6.4f1 with the simulator selected as Unity's OpenXR runtime.
3. In Unity, open `Assets/Scenes/QuestSurgeryGame.unity` and press **Play**.
4. In the simulator's Inputs panel, select **Meta Quest 3** and set both **Left input** and **Right input** to **Hand**.
5. Check that the operating room, patient, robot, hearts and instrument appear in the simulator's left-eye view. Use the simulator viewport and its Input bindings panel to test camera and hand movement. The `1` key returns a hand to its default pose; `3` selects the pinch pose.
6. For separate left and right gestures, turn off **Use same input for both sides**. Under **Keyboard and mouse > Action input**, select **Left - Hand** to operate the arm clutch, or **Right - Hand** to operate the forceps grip. Select the simulator viewport before using the pose keys. The game uses left thumb-index pinch for the clutch and right thumb-index pinch for grip.

With **Use same input for both sides** enabled, keyboard poses affect both hands together. A complete two-hand interaction test needs separately selected inputs or real tracked hands. Simulator success does not prove real-headset comfort, tracking quality, or frame rate.

The simulator files on this PC are under `%LOCALAPPDATA%\Programs\MetaXRSimulatorPortable\PFiles\MetaXRSimulator\v207.1`. The launcher uses `XR_RUNTIME_JSON` only for the Unity process and does not need the Windows-wide OpenXR runtime changed. The game is self-contained and does not require ROS.

Reference: [Meta XR Simulator hand tracking controls](https://developers.meta.com/vr/documentation/unity/xrsim-hand-tracking/).
