# Robot Surgery Trainer — Windows handoff

## Open the Unity project

1. Clone this repository or extract its ZIP to a short local path, for example `C:\UnityProjects\CEG5205-Robot-Surgery`. Do not open Unity from inside the ZIP.
2. In Unity Hub, install **Unity 6000.6.4f1** with **Android Build Support**, Android SDK & NDK Tools, and OpenJDK. This project was built with that editor version.
3. In Unity Hub choose **Add project from disk** and select the extracted `CEG5205-Robot-Surgery` folder. Allow Unity to import packages and recreate its Library cache; this can take several minutes and needs internet access for the Git package in Packages/manifest.json.
4. Open `Assets/Scenes/QuestSurgeryGame.unity`. The patient, exposed upper chest, bedside robot, anatomical hearts, lower-body sterile drape and two trays on a trolley are in this scene. Do not use an empty Untitled scene.
5. To rebuild the Quest app, use **Tools > Robotics > Quest > Build Surgery Game APK**. If Unity warns about Active Input Handling being Both, choose Ignore. Unity writes `Builds/QuestSurgeryGame.mac.apk` (the `.mac.apk` name is historical; it is a normal Android APK and can be built/installed from Windows).

## Run on Quest

Connect the Quest by USB, enable Developer Mode and accept the USB debugging prompt in the headset. In a Windows terminal with Android platform-tools on PATH, run:

    adb devices
    adb install -r Builds\QuestSurgeryGame.mac.apk
    adb shell monkey -p com.ceg5205.questsurgerygame 1

You can also open **Robot Surgery Trainer** from Unknown Sources in the Quest library. Hand tracking must be enabled. Hold a left thumb-index pinch to move the arm with the right hand; pinch the right thumb and index finger to close the forceps. The robot is positioned beside the patient's arm; both trays are on the instrument trolley.

This repository includes the Unity source and assets, the current surgery Android APK, ROS files and project documentation. `Library`, `Logs`, `Temp`, `UserSettings`, and generated C# solution files are omitted because Unity regenerates them. The surgery game runs locally on Quest and does not need ROS or Arduino.
