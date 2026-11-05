# Quest 3 viewing milestone

Open this project with **Unity 6000.6.4f1**. The new `Assets/Scenes/QuestRobotViewer.unity` scene contains the Niryo arm, three boxes, two bins, and a tracked XR viewing camera. It is copied from `MultiBoxSorting.unity`; the desktop sorting scene and original ROS tutorial scenes remain separate.

This first scene is **view only**. Its `ROSConnect` and `Publisher` objects are disabled, so it can open without a ROS server. It does not read Quest hands or move the robot. The headset wearer can move their head and look around the workbench. The XR Origin is positioned for a close view of the small robot; adjust its position in the scene if the initial viewpoint needs tuning on the real headset.

For the next milestone, open the separate `QuestHandTracking.unity` scene and read `HAND_TRACKING.md`. It adds tracked palm and fingertip markers plus pinch detection.

## Check without a headset

1. Install the standalone **Meta XR Simulator** for Windows from Meta's developer documentation: https://developers.meta.com/vr/documentation/unity/unity-simulate-xrsim/
2. Activate it as the computer's OpenXR runtime using Meta's global activation instructions. This project uses Unity OpenXR and does not require the Meta XR Core SDK just to display this scene.
3. In Unity, run **Tools > Robotics > Quest > Prepare XR Settings** if the project was freshly extracted.
4. Open `Assets/Scenes/QuestRobotViewer.unity` and press **Play**. In the simulator, use mouse and keyboard to look around. Select the Quest 3 device profile if needed.

The simulator can verify the scene layout and XR tracking path. It cannot measure real headset comfort, rendering performance, or actual hand-tracking quality.

On the development PC, Unity 6000.6.4f1 successfully compiled the XR project and built `Builds/QuestRobotViewer.apk` for Android/ARM64. Meta XR Simulator is not installed on this PC, and no physical Quest 3 has run this APK yet.

## Build for Quest 3

1. In Unity Hub, add **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK** to Unity 6000.6.4f1.
2. Open the project and run **Tools > Robotics > Quest > Prepare XR Settings**. Verify **Project Settings > XR Plug-in Management**: OpenXR should be enabled for Android and `Meta Quest Support` should be enabled in Android OpenXR features.
3. Choose **Tools > Robotics > Quest > Build Quest Viewer APK**. It builds only `QuestRobotViewer.unity` to `Builds/QuestRobotViewer.apk`. Alternatively use Unity's **File > Build Profiles > Meta Quest > Build** after selecting this scene.
4. When the Quest 3 is available, enable Developer Mode, connect it by USB, and install the APK with Unity **Build and Run** or Meta Quest Developer Hub. Put on the headset and open the app.

The project targets ARM64 with IL2CPP and Android API level 29. The APK is a headset build, not a phone UI. After the headset test, adjust XR Origin placement and graphics quality based on what the wearer actually sees.

## Rebuilding the scene

The editor command **Tools > Robotics > Quest > Build Viewing Scene** regenerates `QuestRobotViewer.unity` from `MultiBoxSorting.unity` and overwrites manual changes to the Quest scene. Keep manual scene edits in a copy or update the scene builder first.

`Assets/Editor/QuestViewSetup.cs` holds the XR configuration, scene generation, and APK build commands. This milestone does not connect ROS to a headset. If that is added later, `127.0.0.1` must be replaced by the reachable IP address of the ROS PC; localhost inside Quest refers to the headset.
