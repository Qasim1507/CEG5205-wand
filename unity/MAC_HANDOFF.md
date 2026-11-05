# CEG5205 Quest robotic arm — handoff for Codex on Mac

## Goal

This is a Unity 6000.6.4f1 port of the Unity Robotics Hub Niryo One pick-and-place tutorial, extended with a three-box/two-bin workspace and two Quest 3 scenes. The next immediate task is to install the existing Android APK on a real Quest 3 and record what works or fails. Longer term, the team wants hand-driven robot control and an ML component; neither is implemented in the Quest scenes yet.

## What is in this package

- `Assets/Scenes/Part1DoneScene.unity`, `Part2RosTopic.unity`, `PickAndPlaceAutomatic.unity`, and `Part4SimAndReal.unity`: the ported tutorial stages.
- `Assets/Scenes/MultiBoxSorting.unity`: Niryo arm, low dark platform, three movable boxes, and two bins. It uses the desktop ROS Part 3 service for planned moves and sorting.
- `Assets/Scenes/DesktopHandTarget.unity`: keyboard-controlled yellow target as a PC prototype. It also uses desktop ROS Part 3.
- `Assets/Scenes/QuestRobotViewer.unity`: view-only Quest XR scene of the robot workspace.
- `Assets/Scenes/QuestHandTracking.unity`: Quest XR scene with the robot workspace, two palm cubes, thumb/index markers, and pinch detection. It does not control the robot.
- `Builds/QuestRobotViewer.apk` and `Builds/QuestHandTracking.apk`: prebuilt Android/Quest APKs. The hand-tracking APK includes the robot workspace and is the first one to test.
- `Assets/Editor/QuestViewSetup.cs`: editor menus under Tools > Robotics > Quest for XR setup, scene generation, and APK builds. Do not regenerate a scene after manual edits unless intentional.
- `Start-ROS.ps1`, `docker/`, `ROS/`: Windows/WSL/Docker ROS Noetic and MoveIt setup for desktop tutorial/sorting scenes.
- `RUN_COMMANDS.md`, `README_PORT.md`, `QUEST_VIEW.md`, `HAND_TRACKING.md`, `QUEST_SIMULATOR.md`: usage details.

## Verified on the Windows development PC

- Unity 6000.6.4f1 imported the project and built both APKs successfully.
- Meta XR Simulator rendered `QuestHandTracking.unity` in Unity Play mode. Unity reported both hands tracked; pressing the simulator's pinch key generated left/right PINCH and RELEASE events. The blue/orange palm cubes turned green during pinch.
- The desktop multi-box scene was validated with the local ROS Part 3 service. See `README_PORT.md` for the stage details.

## Not yet verified or implemented

- Neither APK has run on a physical Quest 3 yet. Check initial viewpoint, stereo comfort, real hand tracking, pinch threshold, and frame rate on the headset.
- Quest hand poses currently move only the marker cubes and fingertip spheres. They do not move the Niryo arm, grip boxes, or call ROS/MoveIt.
- The Quest scenes have ROS connection objects disabled. Localhost `127.0.0.1` inside the headset would refer to the headset, not a development PC.
- The Windows `.cmd`/`.ps1` launchers and WSL commands do not run on macOS unchanged. The simulator setup on Windows is not part of the Mac installation.

## Tomorrow's real-headset test using a Mac

1. The Mac only needs `Builds/QuestHandTracking.apk` to install the prebuilt app; Unity is optional for this first test.
2. Install Meta Quest Developer Hub for Mac. Ensure the headset owner has Developer Mode enabled, then connect the Quest 3 with a USB-C data cable and accept USB debugging inside the headset.
3. In Developer Hub, use Device Manager > Add Build to install `QuestHandTracking.apk`. Launch it from the headset's sideloaded/Unknown Sources apps.
4. Turn on headset hand tracking (Settings > Movement tracking > Hand and body tracking), put controllers down, and show both hands to the cameras. Check that the blue/orange palm cubes and thumb/index spheres follow, that the scene says BOTH HANDS, and that a thumb-index pinch turns the relevant palm cube green.
5. Record screenshots/video and report any install error, crash, wrong viewpoint, missing hand markers, or gesture mismatch to Codex. The view-only APK is a useful fallback if hand tracking fails.

## Opening the Unity project on Mac later

Install Unity 6000.6.4f1 with Android Build Support, SDK/NDK, and OpenJDK; extract this ZIP; add the extracted project folder (containing `Assets`, `Packages`, `ProjectSettings`) in Unity Hub. Allow package import. The project's Git package dependencies need network access. Apple Silicon Mac simulation may require Meta XR SDK setup beyond this existing Windows OpenXR setup. Work on a copy and do not assume the Windows launchers/ROS runtime are portable.

## Prompt to paste into a new Mac Codex chat

> I have extracted the CEG5205 Quest robotic arm Unity project. Read `MAC_HANDOFF.md`, `HAND_TRACKING.md`, and `QUEST_VIEW.md` first. The prebuilt `Builds/QuestHandTracking.apk` has worked in Windows Meta XR Simulator but has not been tested on a real Quest 3. Help me install it from this Mac onto the headset, verify the workspace and hand/pinch tracking, and diagnose any actual errors. Do not assume the Quest app controls the robot yet. Use the project files as the source of truth.
