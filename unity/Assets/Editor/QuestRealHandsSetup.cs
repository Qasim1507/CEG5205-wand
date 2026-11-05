using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Hands.Samples.VisualizerSample;

/// <summary>Builds a separate Quest app with Unity XR Hands' articulated hand meshes.</summary>
public static class QuestRealHandsSetup
{
    const string SourceScene = "Assets/Scenes/QuestHandTracking.unity";
    const string RealHandsScene = "Assets/Scenes/QuestRealHands.unity";
    const string SampleRoot = "Assets/Samples/XR Hands/1.7.3/HandVisualizer/";
    const string Output = "Builds/QuestRealHands.mac.apk";

    [MenuItem("Tools/Robotics/Quest/Build Real Hands Scene")]
    public static void BuildScene()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play mode before building the real hands scene.");

        var scene = EditorSceneManager.OpenScene(SourceScene);
        var rig = GameObject.Find("XR Origin - Quest viewing");
        if (rig == null)
            throw new InvalidOperationException("The Quest XR Origin is missing.");

        var cameraOffset = rig.transform.Find("Camera Offset");
        if (cameraOffset == null)
            throw new InvalidOperationException("The Quest Camera Offset is missing.");

        // The original scene remains available for comparison. Only the new
        // scene removes its six debug shapes.
        foreach (var child in cameraOffset.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("Left palm -") ||
                child.name.StartsWith("Right palm -") ||
                child.name.StartsWith("Left thumb tip") ||
                child.name.StartsWith("Left index tip") ||
                child.name.StartsWith("Right thumb tip") ||
                child.name.StartsWith("Right index tip"))
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        var demo = rig.GetComponentInChildren<QuestHandTrackingDemo>(true);
        if (demo == null)
            throw new InvalidOperationException("The hand tracking status component is missing.");
        demo.gameObject.name = "Hand tracking and pinch detection";
        var demoFields = new SerializedObject(demo);
        foreach (var name in new[] { "leftPalmMarker", "rightPalmMarker", "leftThumbMarker",
                     "leftIndexMarker", "rightThumbMarker", "rightIndexMarker" })
            demoFields.FindProperty(name).objectReferenceValue = null;
        demoFields.ApplyModifiedPropertiesWithoutUndo();

        var visualizerObject = new GameObject("Articulated Quest hands");
        visualizerObject.transform.SetParent(rig.transform, false);
        var visualizer = visualizerObject.AddComponent<HandVisualizer>();
        var fields = new SerializedObject(visualizer);
        Set(fields, "m_MetaQuestLeftHandMesh", SampleRoot + "Models/LeftHand.fbx");
        Set(fields, "m_MetaQuestRightHandMesh", SampleRoot + "Models/RightHand.fbx");
        Set(fields, "m_AndroidXRLeftHandMesh", SampleRoot + "Models/LeftHandAndroidXR.fbx");
        Set(fields, "m_AndroidXRRightHandMesh", SampleRoot + "Models/RightHandAndroidXR.fbx");
        Set(fields, "m_DebugDrawPrefab", SampleRoot + "Prefabs/Joint.prefab");
        Set(fields, "m_VelocityPrefab", SampleRoot + "Prefabs/VelocityPrefab.prefab");
        fields.FindProperty("m_HandMeshMaterial").objectReferenceValue = GetHandMaterial();
        fields.FindProperty("m_DrawMeshes").boolValue = true;
        fields.FindProperty("m_DebugDrawJoints").boolValue = false;
        fields.FindProperty("m_VelocityType").enumValueIndex = 2;
        fields.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, RealHandsScene))
            throw new InvalidOperationException("Could not save " + RealHandsScene);

        var buildScenes = EditorBuildSettings.scenes.ToList();
        if (buildScenes.All(item => item.path != RealHandsScene))
        {
            buildScenes.Add(new EditorBuildSettingsScene(RealHandsScene, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Real hands scene saved: " + RealHandsScene);
    }

    [MenuItem("Tools/Robotics/Quest/Build Real Hands APK")]
    public static void BuildAndroid()
    {
        QuestViewSetup.ConfigureProject();
        // Preserve any viewpoint or material edits made in the saved scene.
        if (!System.IO.File.Exists(RealHandsScene))
            BuildScene();
        var priorId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var priorName = PlayerSettings.productName;
        var output = System.IO.Path.GetFullPath(Output);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        try
        {
            // A distinct package installs beside the already tested debug app.
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                "com.ceg5205.questrealhands");
            PlayerSettings.productName = "Robotic Arm Hands";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { RealHandsScene },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Real hands APK build failed: " + report.summary.result);
            Debug.Log("Real hands APK: " + output);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, priorId);
            PlayerSettings.productName = priorName;
        }
    }

    static void Set(SerializedObject fields, string field, string assetPath)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null)
            throw new InvalidOperationException("Missing XR Hands asset: " + assetPath);
        fields.FindProperty(field).objectReferenceValue = asset;
    }

    static Material GetHandMaterial()
    {
        const string path = "Assets/Materials/QuestHands/ArticulatedHands.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;
        var shader = Shader.Find("Standard");
        if (shader == null)
            throw new InvalidOperationException("Unity Standard shader is missing.");
        material = new Material(shader) { color = new Color(0.78f, 0.59f, 0.47f) };
        material.SetFloat("_Glossiness", 0.1f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
