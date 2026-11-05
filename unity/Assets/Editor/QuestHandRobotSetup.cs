using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Creates a separate headset app for direct, simulated Niryo control.</summary>
public static class QuestHandRobotSetup
{
    const string SourceScene = "Assets/Scenes/QuestRealHands.unity";
    const string ControlScene = "Assets/Scenes/QuestHandRobotControl.unity";
    const string Output = "Builds/QuestHandRobotControl.mac.apk";

    [MenuItem("Tools/Robotics/Quest/Inspect Hand Gripper")]
    public static void InspectGripper()
    {
        EditorSceneManager.OpenScene(ControlScene);
        var robot = GameObject.Find("niryo_one");
        var stem = "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link/hand_link/tool_link/gripper_base/servo_head/";
        foreach (var side in new[] { "control_rod_left/left_gripper", "control_rod_right/right_gripper" })
        {
            var finger = robot.transform.Find(stem + side);
            if (finger == null) throw new InvalidOperationException("Missing " + side);
            Debug.Log(side + " colliders=" + finger.GetComponentsInChildren<Collider>(true).Length +
                " meshes=" + finger.GetComponentsInChildren<MeshFilter>(true).Length +
                " renderers=" + finger.GetComponentsInChildren<Renderer>(true).Length +
                " world=" + finger.position);
            foreach (var mesh in finger.GetComponentsInChildren<MeshFilter>(true))
                Debug.Log("  " + mesh.transform.name + " mesh=" + mesh.sharedMesh.name +
                    " bounds=" + mesh.sharedMesh.bounds + " world=" + mesh.transform.position +
                    " renderBounds=" + mesh.GetComponent<Renderer>().bounds);
        }
        foreach (var body in UnityEngine.Object.FindObjectsByType<Rigidbody>())
            if (body.name.StartsWith("Box "))
                Debug.Log(body.name + " world=" + body.position +
                    " collider=" + body.GetComponent<Collider>().bounds);
    }

    [MenuItem("Tools/Robotics/Quest/Build Hand Robot Control Scene")]
    public static void BuildScene()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play mode before creating the hand control scene.");

        var scene = EditorSceneManager.OpenScene(SourceScene);
        var robot = GameObject.Find("niryo_one");
        var rig = GameObject.Find("XR Origin - Quest viewing");
        var cameraOffset = rig != null ? rig.transform.Find("Camera Offset") : null;
        var cameraTransform = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;
        if (robot == null || rig == null || cameraOffset == null || cameraTransform == null)
            throw new InvalidOperationException("The real-hands scene is missing its robot or XR camera rig.");

        var oldDemo = rig.GetComponentInChildren<QuestHandTrackingDemo>(true);
        if (oldDemo != null)
            oldDemo.enabled = false;

        var labelTransform = cameraTransform.Find("Hand tracking instructions");
        if (labelTransform == null)
            throw new InvalidOperationException("The headset instruction label is missing.");
        var label = labelTransform.GetComponent<TextMesh>();
        if (label == null)
            throw new InvalidOperationException("The headset instruction text is missing.");
        labelTransform.localPosition = new Vector3(0f, 0.22f, 1.1f);
        label.fontSize = 42;
        label.characterSize = 0.0032f;
        label.anchor = TextAnchor.UpperCenter;
        label.text = "LEFT PINCH: HOLD TO MOVE\nRIGHT MOVE/TILT: J1-J6\nRIGHT PINCH: GRIP";

        var controllerObject = new GameObject("Hand control - virtual Niryo only");
        controllerObject.transform.SetParent(rig.transform, false);
        var controller = controllerObject.AddComponent<QuestHandRobotController>();
        var fields = new SerializedObject(controller);
        fields.FindProperty("robot").objectReferenceValue = robot.transform;
        fields.FindProperty("trackingSpace").objectReferenceValue = cameraOffset;
        fields.FindProperty("headCamera").objectReferenceValue = cameraTransform.GetComponent<Camera>();
        fields.FindProperty("statusText").objectReferenceValue = label;
        fields.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ControlScene))
            throw new InvalidOperationException("Could not save " + ControlScene);

        var buildScenes = EditorBuildSettings.scenes.ToList();
        if (buildScenes.All(item => item.path != ControlScene))
        {
            buildScenes.Add(new EditorBuildSettingsScene(ControlScene, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Quest hand robot control scene saved: " + ControlScene);
    }

    [MenuItem("Tools/Robotics/Quest/Build Hand Robot Control APK")]
    public static void BuildAndroid()
    {
        QuestViewSetup.ConfigureProject();
        if (!System.IO.File.Exists(ControlScene))
            BuildScene();
        var priorId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var priorName = PlayerSettings.productName;
        var output = System.IO.Path.GetFullPath(Output);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        try
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                "com.ceg5205.questhandrobot");
            PlayerSettings.productName = "Niryo Hand Control";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ControlScene },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Hand robot APK build failed: " + report.summary.result);
            Debug.Log("Quest hand robot APK: " + output);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, priorId);
            PlayerSettings.productName = priorName;
        }
    }
}
