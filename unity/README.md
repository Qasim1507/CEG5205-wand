# CEG5205 Robot Surgery Trainer

Unity 6000.6.4f1 project containing the Quest surgery training scene, earlier robotics tutorial scenes, ROS support files, and the latest Android surgery APK.

## Open the project

Clone this repository, then in Unity Hub choose **Add project from disk** and select the repository folder. Install Unity **6000.6.4f1** with Android Build Support. Unity will recreate the ignored `Library/` cache on first open.

Open `Assets/Scenes/QuestSurgeryGame.unity` for the current surgery scene. Its patient has upper- and lower-body drapes, an exposed chest opening, two anatomical hearts, two trays, and the Niryo arm with visible forceps. The current Quest build is `Builds/QuestSurgeryGame.mac.apk`; despite the historical filename, it is an Android APK.

See `QUEST_SURGERY_GAME.md` for the interaction, `QUEST_SIMULATOR.md` for simulator setup, and `WINDOWS_README.md` for Windows build and installation steps. The surgery game runs locally without ROS; `ROS/` and the tutorial scenes are retained for the earlier robotics demonstrations.

The saved scene, object references and Android build have been checked in Unity. Live hand-controlled grasping and placement still require a simulator or headset test; see `SURGERY_SCENE_VALIDATION.md`.
