# Run this project on this Mac

This is a copy of the original project. Run `./open-unity-mac.sh` to open it in **Unity 6000.6.4f1**. The matching editor includes Android Build Support, Android SDK & NDK Tools, and OpenJDK to rebuild Quest APKs.

## Quest 3 hand tracking

The original prebuilt app is `Builds/QuestHandTracking.apk`. With Developer Mode enabled on the headset, connect it with a USB data cable and accept the USB debugging prompt in the headset. Run:

```bash
adb devices -l
adb install -r Builds/QuestHandTracking.apk
```

You can also run `./install-quest-mac.sh hands`; it checks that exactly one authorized device is connected. Use `./install-quest-mac.sh viewer` for the fallback app.

Launch **CEG5205 Quest Robotic Arm** from the headset's sideloaded apps. Put the controllers down and enable hand tracking. The palm cubes and fingertip spheres should follow the hands; pinch should turn the matching palm cube green. This scene does not command the robot. `Builds/QuestRobotViewer.apk` is a view-only fallback.

Both APKs were also rebuilt successfully on this Mac as `Builds/QuestHandTracking.mac.apk` and `Builds/QuestRobotViewer.mac.apk`. These use this Mac's Unity debug signing certificate, which differs from the original Windows builds. To replace an already installed original with a Mac build, Android may require uninstalling the old app first; that removes its local app data. For the first headset test, use the original APK above.

## Desktop ROS scenes

The Mac launcher uses Colima and Docker to run the project's ROS Noetic image under amd64 emulation on Apple Silicon. The first image build can take a long time.

```bash
./start-ros-mac.sh 2       # Part 2
./start-ros-mac.sh 3       # Part 3, sorting, desktop target
./start-ros-mac.sh 4Sim    # Part 4 local simulation
./start-ros-mac.sh logs    # follow ROS output
./start-ros-mac.sh stop
```

Open the matching scene in Unity and press Play. The desktop scenes use `127.0.0.1:10000`, matching the Docker port. Only one stage runs at a time.

The Quest scenes run without ROS. Unity's macOS Game view cannot provide real Quest hand joints without a compatible simulator or headset. Physical Quest testing requires the headset.

## Articulated hands and viewpoint

`Assets/Scenes/QuestRealHands.unity` uses the Unity XR Hands 1.7.3 sample's rigged hand meshes instead of the original palm cubes and fingertip balls. Its headset app is `Builds/QuestRealHands.mac.apk`, shown as **Robotic Arm Hands** in the Quest's Unknown Sources library. It has a separate package ID (`com.ceg5205.questrealhands`), so the original **PickAndPlace** app remains installed. Rebuild it with **Tools > Robotics > Quest > Build Real Hands APK**. The scene can be regenerated from the original debug scene with **Build Real Hands Scene**, which replaces edits to the real-hands scene.

The Quest camera follows the headset. Turn or move your head to inspect the workbench from another angle. To change the default position for a future build, stop Play mode, open `QuestRealHands.unity`, select **XR Origin - Quest viewing**, and edit its Transform position or Y rotation in the Inspector. Its current position is `(0, -0.55, -0.85)`; changing X moves the viewpoint sideways, Y changes its height, and Z changes its distance. Edit the XR Origin rather than the tracked **Main Camera**, save the scene, rebuild the real-hands APK, and reinstall it with `adb install -r Builds/QuestRealHands.mac.apk`.
