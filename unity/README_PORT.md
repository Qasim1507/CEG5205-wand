# Unity Robotics Hub pick and place — Unity 6000.6.4f1

This is one Unity project for the complete **pick-and-place tutorial sequence, Parts 0–4**, ported from Unity Robotics Hub commit `b8d1e1d4c76c9ce75fc145cdbf55a9072cc77364`. The original tutorial project used Unity 2020.3.11f1. The original tutorial text is included in `TutorialDocs` for reference. The working Unity 6 scenes and commands below supersede its old editor instructions.

## Stage map

| Tutorial | Unity scene | ROS startup | What to expect |
| --- | --- | --- | --- |
| Part 0: ROS setup | — | `Start-ROS.ps1` | Ubuntu WSL and Docker host the included ROS Noetic/MoveIt workspace. The script builds its image if missing. |
| Part 1: import the robot | `Assets/Scenes/Part1DoneScene.unity` | None | Inspect and manually move the Niryo One arm, table, target box, and placement zone. |
| Part 2: ROS–Unity TCP | `Assets/Scenes/Part2RosTopic.unity` | `-Part 2` | Press **Publish to ROS**. Unity publishes the joint, box and destination poses on `/niryo_joints`; the ROS subscriber logs `I heard:`. |
| Part 3: simulated pick and place | `Assets/Scenes/PickAndPlaceAutomatic.unity` | `-Part 3` | Unity calls MoveIt for four trajectories, executes them on the simulated arm, grips the box and places it in the target area. Starts automatically after ROS connects. |
| Part 4: simulated and real flow | `Assets/Scenes/Part4SimAndReal.unity` | `-Part 4Sim` for local simulation | Press **Start Part 4**. Unity publishes a request, listens to RobotMove action goals, and mirrors the trajectory and gripper commands. A local action server stands in for a real robot. |
| Multi-box sorting extension | `Assets/Scenes/MultiBoxSorting.unity` | `-Part 3` | A low platform replaces the original table. Three real rigidbody boxes can be picked and placed in two bins using the three Game view buttons. |
| Desktop hand target prototype | `Assets/Scenes/DesktopHandTarget.unity` | `-Part 3` | Drag a yellow target in Game view and ask MoveIt to move the Niryo gripper there. This is the PC input stand-in for later Quest hand tracking. |
| Quest 3 viewing milestone | `Assets/Scenes/QuestRobotViewer.unity` | None | View the arm, three boxes, and two bins through an XR camera. The ROS controls are disabled in this view-only scene. See `QUEST_VIEW.md`. |

Open this folder with Unity **6000.6.4f1** from Unity Hub. The tutorial scenes and the multi-box extension are in Build Settings. `DemoScene` and `TutorialScene` from the original repository remain available as reference scenes.

## Run a stage

1. Open PowerShell in this project folder and start the corresponding ROS stage, for example:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Start-ROS.ps1 -Part 2
   powershell -ExecutionPolicy Bypass -File .\Start-ROS.ps1 -Part 3
   powershell -ExecutionPolicy Bypass -File .\Start-ROS.ps1 -Part 4Sim
   ```

   Run **one command at a time** for the stage you want. The script stops the other stage's container so port 10000 remains free. The first run may take time if Docker has to build the ROS image.

2. Open that stage's scene in Unity and press **Play**. Parts 2 and 4 have buttons in the Game view. Part 3 runs automatically when it connects and also has a button. The scenes use `127.0.0.1:10000` for the local ROS container.
3. To check a ROS container's output, run `wsl -d Ubuntu -u root -- /usr/bin/docker logs unity-pnp-part2`, `... logs unity-pick-and-place-ros`, or `... logs unity-pnp-part4-sim` as appropriate.
4. To stop the running stage, use `wsl -d Ubuntu -u root -- /usr/bin/docker stop <container-name>`.

## Part 4 with a physical Niryo One

The **Unity code and ROS message/action types are ported**. The physical path requires a Niryo One running a compatible ROS 1 stack and the `niryo_moveit` package from this project's `ROS/src`. Build that package in the robot's catkin workspace and launch `part_4.launch` on the robot. In `Part4SimAndReal`, set `ROSConnect` → **ROS IP Address** to the robot's reachable IP. The local `4Sim` mode is for testing the same Unity action-goal flow without hardware; it does not command a physical arm. We have not connected a real Niryo One, so its motion and safety behavior are not verified.

The Unity 6 connector cannot publish a ROS service request as a topic, as the original Part 4 code did. The port uses `niryo_moveit/RealSimRequest.msg` with the same three fields, on both the Unity and ROS sides. Rebuild the current `niryo_moveit` package on a real robot before trying Part 4.

## Verification on this PC

- Unity 6000.6.4f1 imported and compiled the project. Each stage scene had **zero missing scripts** and the publisher/robot/box references were checked.
- The original four Unity Play Mode tests passed during the initial Unity 6 port.
- Part 2 Unity published `/niryo_joints`, and its ROS subscriber logged receipt.
- Part 3 Unity and MoveIt completed an automatic placement integration run.
- Part 4 local simulation received all six action trajectories, closed and opened the gripper, and Unity reported the box **InsidePlaced**.

The Quest 3 **viewing** scene and Android build setup are described in `QUEST_VIEW.md`. The separate hand-tracking scene and APK are described in `HAND_TRACKING.md`. ROS control from the headset remains a later step.

## Multi-box sorting scene

Open `Assets/Scenes/MultiBoxSorting.unity`. Start ROS with `powershell -ExecutionPolicy Bypass -File .\Start-ROS.ps1 -Part 3`, then press Play in Unity. In the Game view, press **Pick: Box 1 to Bin A**, wait for the completion message, then use the buttons for Box 2 and Box 3. Each button reads that box's current Unity position and the corresponding drop target under a bin, sends them to the existing MoveIt service, and executes the returned joint trajectories. The gripper closes and opens as in Part 3. The bin's solid bottom supports the box, and its interior trigger checks whether the box landed inside. The bin walls are visual because the original MoveIt planning scene does not include them; colliding walls pushed grasped boxes out of the gripper during validation.

The boxes and bins are saved scene objects, not runtime decoration. You can move a box or an entire bin in the Unity Scene view before pressing Play; the controller reads the new positions when you click its button. Keep changes small and within the robot's reach, because MoveIt may return no trajectory for unreachable positions. Move a whole bin root to keep its drop targets with it. The **Tools > Robotics > Build Multi-Box Sorting Scene** editor command regenerates this scene from Part 3 and overwrites manual edits to it.

An end-to-end Play Mode integration test, `MultiBoxSortingRosTests.EachBoxIsPlannedAndPlacedInItsBin`, passed with all three boxes placed using local ROS Part 3. This extension is still a desktop ROS simulation; it does not yet read Quest hand tracking.

## Desktop hand target prototype

1. Run `powershell -ExecutionPolicy Bypass -File .\Start-ROS.ps1 -Part 3` from this project folder. The first run of this updated image rebuilds the ROS container to add the single-target planner.
2. Open `Assets/Scenes/DesktopHandTarget.unity` and press Play. Look for the yellow sphere above the boxes.
3. Click inside the **Game** view so it receives keyboard input. Use **W/S** for forward/back across the table, **A/D** for left/right, and **Q/E** for down/up. Hold **Shift** for faster movement. You can also drag the yellow sphere with the left mouse button and use the **Target height** slider. The target is confined to a small workspace near the arm.
4. Click **Move Arm to Target**. Unity sends the target pose and six current joint angles to the `niryo_moveit_target` ROS service. MoveIt returns one joint trajectory, which Unity plays on the simulated arm. Wait for **Arm movement complete** before sending another request.

This scene leaves the boxes and bins visible for orientation, but the target button only moves the arm. It does **not** grasp a box, continuously follow mouse motion, read Quest hands, or command a physical robot. MoveIt can reject points that the robot cannot reach; move the target closer and retry. The target uses the same fixed gripper orientation as the working pick-and-place example. The new ROS service reuses the tutorial's `MoverService` request/response message format, with `pick_pose` meaning the desired gripper pose and `place_pose` unused. The original pick-and-place service remains available for the sorting scene.

The `DesktopTargetRosTests.TargetPosePlansAndMovesTheArm` Play Mode integration test exercises two target positions through the local ROS/MoveIt service. To regenerate this scene from the sorting scene, use **Tools > Robotics > Build Desktop Hand Target Scene**; that overwrites manual edits to `DesktopHandTarget.unity`.
