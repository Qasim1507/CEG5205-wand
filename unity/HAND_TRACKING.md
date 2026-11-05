# Quest 3 hand-tracking milestone

Open this project in **Unity 6000.6.4f1**. Open `Assets/Scenes/QuestHandTracking.unity` to see the robot, three boxes, two bins, and the Quest camera. This is a separate scene from `QuestRobotViewer.unity` and the ROS scenes.

## What it does

- Uses Unity OpenXR plus XR Hands to read the Quest's tracked left and right palms, thumb tips, and index tips.
- Shows a **blue cube** at the left palm and an **orange cube** at the right palm, with small spheres on each thumb and index tip. Markers hide when tracking is lost.
- Detects thumb-to-index pinch separately on each hand. A pinching palm cube turns **green**; releasing returns its original colour. The Unity Console also logs `PINCH` and `RELEASE`.
- Keeps the robot and boxes visible for context. The hands do **not** command the arm or pick up boxes in this milestone. The ROS connector is disabled in this scene.

## Test on a Quest 3

1. Enable Developer Mode on the headset and connect it to the PC by USB. Allow USB debugging in the headset.
2. In Unity, use **Tools > Robotics > Quest > Build Hand Tracking APK**, or install the prebuilt `Builds/QuestHandTracking.apk` with Meta Quest Developer Hub or `adb install -r Builds/QuestHandTracking.apk`.
3. In the headset's input settings, enable hand tracking (or automatic hand/controller switching). Put down the controllers, launch **CEG5205 Quest Robotic Arm** from Unknown Sources, and hold your hands in view of the headset cameras.
4. Check that each coloured palm cube follows the matching hand and each pair of small spheres follows the thumb and index. Pinch thumb to index: that palm cube should turn green. Release: it should return to blue or orange. Move a hand out of camera view: its markers should disappear.

The instruction text in view changes from `RAISE YOUR HANDS` to `LEFT HAND`, `RIGHT HAND`, or `BOTH HANDS` according to tracking. It stays in the upper view as you look around.

Without a Quest or a hand-capable OpenXR simulator, Unity's Game view can show the robot scene and instruction text, but it cannot supply real hand joints. Invisible markers in desktop Play mode are expected.

## Project files

- `Assets/Scenes/QuestHandTracking.unity`: headset scene.
- `Assets/QuestHands/QuestHandTrackingDemo.cs`: marker positioning and pinch detection.
- `Assets/Editor/QuestViewSetup.cs`: Unity menus to configure OpenXR, regenerate the scene, and build the APK.
- `Packages/manifest.json`: `com.unity.xr.hands` dependency.

The Unity menu **Build Hand Tracking Scene** regenerates this scene from `QuestRobotViewer.unity` and overwrites edits made directly to the hand scene. Update the scene builder before regenerating if you customize it.

Unity import and Android build are tested on this PC. Physical Quest 3 hand tracking has **not** been tested here; verify the marker positions and pinch threshold on the headset when available.
