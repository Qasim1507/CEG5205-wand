# Quest heart replacement game

Open `Assets/Scenes/QuestSurgeryGame.unity` in Unity. The Android build is `Builds/QuestSurgeryGame.mac.apk`; install it with `adb install -r Builds/QuestSurgeryGame.mac.apk` and open **Robot Surgery Trainer** in the Quest's Unknown Sources list.

## Game

The scene shows a practice cadaver on an operating bed, an exposed chest field, a native heart, a donor heart on a sterile tray, a disposal tray, and the Niryo arm with a slender forceps visual. This is a stylized VR game, not a clinical simulation.

1. Pinch the left thumb and index finger to engage arm movement. Move the right hand to position the arm and forceps.
2. Pinch the right thumb and index finger to grip the native heart. Move it into the **left** disposal tray and release. This completes stage 1 (+100).
3. Pick up the donor heart from the **right** tray. Release it in the open chest cavity. This completes stage 2 (+150), with a time bonus.
4. A release outside the target costs 5 points. After completion, hold both pinches for two seconds to replay.

The hearts remain where they are released so a missed placement can be tried again. The six-joint hand mapping, movement gain, and speed are the same as the original cube hand-control app. Heart objects are carried without parenting a Rigidbody into the robot articulation. The original turquoise gripper and its articulated finger joints are removed from this scene; the steel forceps open and close through virtual jaws driven by the right pinch, with a short visual smoothing step to prevent tip jitter.

The chest rim is generated against the patient mesh surface, and triangles under the opening are removed. The patient has a MakeHuman skin texture; the visible internal anatomy comes from a CC0 torso model. The two graspable hearts use a smaller, separate anatomical heart model with ventricles and major vessels. Sterile drapes cover the lower body and upper torso, leaving the surgical opening exposed. Rebuild the scene if the patient mesh or its pose changes.

## Rebuild

In Unity, choose **Tools → Robotics → Quest → Build Surgery Game Scene**, then **Build Surgery Game APK**. In the Android input warning, choose **Ignore**. The builder uses the tested `QuestHandRobotControl` scene as its source, so the surgery game stays separate from the original robot-control app.

## Model

The patient body mesh and skin texture derive from MakeHuman CC0 assets. The internal organ meshes come from Innerscene's CC0 *Torso with removable organs*, and the graspable hearts from its CC0 *Heart with major blood vessels*. See `Assets/Surgery/Models/MODEL_SOURCE.md`, `ThirdPartySources/Anatomy/SOURCE.md`, and `Assets/Surgery/Models/LICENSE.ASSETS.md` for provenance and licensing. The organ arrangement is for a visual training game and has not been medically validated.
