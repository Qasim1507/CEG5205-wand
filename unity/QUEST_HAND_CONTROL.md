# Quest hand control for the virtual Niryo One

Open **Niryo Hand Control** in the Quest app library (Unknown Sources, if that is where locally installed apps appear). This scene shows the Niryo arm and tracked hand models. It controls the **Unity simulation only**; it does not send commands to the physical Niryo robot or ROS.

## How to use it

1. Raise both empty hands where the Quest can track them. Keep the system menu closed.
2. Touch the **left thumb and left index fingertip** together and **hold**. This is the clutch. At that moment, the app records the right palm pose, the current six robot joint targets, and your viewing directions.
3. While holding the left pinch, move or rotate your **right palm** as shown below. Pinch the **right thumb and index** to close the gripper.
4. Release the **left pinch** to hold the arm at its current pose. Reposition your hands, then pinch left again to continue from that pose.

To pick up a cube, bring the gripper so the **cube's center is between the two finger pads**, then close your right thumb and index while holding the left pinch. The app grips only a box within about 4 cm of the jaw center; it does not grab a distant box. The fingers stop at the cube's width while holding it. Open the right thumb and index to release and drop the cube.

The headset is your camera. Move your head or body to see the robot from another angle; this does not itself command the arm. The directions in the table are locked to where you were looking at the moment you engaged the left pinch.

## Exact gesture-to-joint map

All right-hand movements below are relative to the right palm pose captured at the start of each left pinch. The hand mesh is visual feedback; the **palm joint** supplies arm position and orientation. Positive/negative motor signs refer to Unity articulation drive targets, so the resulting apparent direction depends on the robot joint axis in the URDF.

| Input while left pinch is held | Niryo joint / motor | Mapping | URDF target limits |
| --- | --- | --- | --- |
| Move right palm right / left across your view | `joint_1`, base rotation | − / + 48° per 10 cm (reversed to match the apparent arm motion) | −175° to +175° |
| Move right palm up / down | `joint_2`, shoulder | + / − 36° per 10 cm | −110° to +36.7° |
| Move right palm forward / backward | `joint_3`, elbow | + / − 36° per 10 cm | −80.1° to +90° |
| Turn right palm left / right around the vertical viewing axis (yaw) | `joint_4`, forearm rotation | 1° target per 1° hand rotation | −175° to +175° |
| Tilt right palm up / down around the horizontal viewing axis (pitch) | `joint_5`, wrist bend | 1° target per 1° hand rotation | −100° to +110° |
| Roll right palm clockwise / counterclockwise around the forward viewing axis | `joint_6`, tool rotation | 1° target per 1° hand rotation | −147.5° to +147.5° |
| Separate / pinch **right thumb and index fingertips** | Left and right gripper finger drives, mirrored | 50 mm fingertip gap = open; 15 mm or less = closed; proportional in between. Finger drive targets run from +10/−10 mm (open) to −10/+10 mm (closed) when empty. With a cube held, closure stops at about 43% so the fingers do not pass through it. | Within the URDF's ±25.74 mm finger limits |

The left thumb and index fingertips must be **closer than 25 mm** to engage control. Once engaged, the clutch releases when their gap exceeds **35 mm**. The difference prevents flicker at the threshold. The six joint drive targets advance at no more than **180° per second** and are clamped to the joint limits. Gripper targets advance at up to **40 mm per second**. On clutch release, the drive targets snap to the measured joint angles and command zero target velocity, instead of continuing toward the previous hand pose.

If either hand is lost, the left pinch is released, the app loses focus, or the app pauses, the arm target is held. The headset label reports `MOVING VIRTUAL ARM`, `LEFT PINCH TO ENGAGE`, or a tracking/paused state.

## Project files

- Scene: `Assets/Scenes/QuestHandRobotControl.unity`
- Runtime controller: `Assets/QuestHands/QuestHandRobotController.cs`
- Scene and APK build menu: `Assets/Editor/QuestHandRobotSetup.cs`
- APK: `Builds/QuestHandRobotControl.mac.apk`
- Unity menu for rebuilding: **Tools → Robotics → Quest → Build Hand Robot Control APK**

The existing **Robotic Arm Hands** and **PickAndPlace** apps remain separate.
