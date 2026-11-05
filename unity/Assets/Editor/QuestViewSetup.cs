using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.CompositionLayers;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using UnityEngine.XR.Hands.OpenXR;

/// <summary>
/// Creates a view-only Quest scene from the existing sorting scene. The source
/// scene and its ROS controls are intentionally left untouched.
/// </summary>
public static class QuestViewSetup
{
    const string SourceScene = "Assets/Scenes/MultiBoxSorting.unity";
    const string QuestScene = "Assets/Scenes/QuestRobotViewer.unity";
    const string HandScene = "Assets/Scenes/QuestHandTracking.unity";
    const string SettingsFolder = "Assets/XR/Settings";
    const string GeneralSettingsPath = SettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";
    const string LoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

    [MenuItem("Tools/Robotics/Quest/Prepare XR Settings")]
    public static void ConfigureProject()
    {
        EnsureFolder("Assets/XR");
        EnsureFolder(SettingsFolder);

        var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(GeneralSettingsPath);
        if (perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, GeneralSettingsPath);
        }

        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        ConfigureLoader(perTarget, BuildTargetGroup.Standalone);
        ConfigureLoader(perTarget, BuildTargetGroup.Android);

        ConfigureFeatures(BuildTargetGroup.Standalone);
        ConfigureFeatures(BuildTargetGroup.Android);

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.ceg5205.questrobotviewer");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)29;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
        AssetDatabase.SaveAssets();
        Debug.Log("Quest XR settings prepared for Windows simulator and Android/Quest.");
    }

    static void ConfigureLoader(XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup target)
    {
        if (!perTarget.HasManagerSettingsForBuildTarget(target))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(target);

        var settings = perTarget.SettingsForBuildTarget(target);
        settings.InitManagerOnStart = true;
        var manager = perTarget.ManagerSettingsForBuildTarget(target);
        if (!XRPackageMetadataStore.IsLoaderAssigned(LoaderType, target) &&
            !XRPackageMetadataStore.AssignLoader(manager, LoaderType, target))
            throw new InvalidOperationException("Could not enable OpenXR loader for " + target);
    }

    static void ConfigureFeatures(BuildTargetGroup target)
    {
        // A newly created OpenXRSettings asset starts with an empty feature
        // array until the package's feature discovery has run.
        FeatureHelpers.RefreshFeatures(target);
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(target);
        if (settings == null)
        {
            Debug.LogWarning("OpenXR feature settings unavailable for " + target +
                ". Install its Unity build module, then run Prepare XR Settings again.");
            return;
        }

        var controllers = settings.GetFeature<OculusTouchControllerProfile>();
        if (controllers != null)
            controllers.enabled = true;

        var handTracking = settings.GetFeature<HandTracking>();
        if (handTracking == null)
            throw new InvalidOperationException("OpenXR Hand Tracking feature is missing. Install XR Hands.");
        handTracking.enabled = true;

        if (target == BuildTargetGroup.Android)
        {
            var quest = settings.GetFeature<MetaQuestFeature>();
            if (quest == null)
                throw new InvalidOperationException("Meta Quest Support OpenXR feature is missing.");
            quest.enabled = true;

            var layers = settings.GetFeature<OpenXRCompositionLayersFeature>();
            if (layers == null)
                throw new InvalidOperationException("Composition Layers Support OpenXR feature is missing.");
            layers.enabled = true;
        }
        EditorUtility.SetDirty(settings);
    }

    [MenuItem("Tools/Robotics/Quest/Build Viewing Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

        // This first milestone is an inspectable VR scene. It should start
        // without the Windows-only local ROS service or a 2D sorting UI.
        Disable("ROSConnect");
        Disable("Publisher");

        var cameraObject = GameObject.Find("Main Camera");
        if (cameraObject == null)
            throw new InvalidOperationException("Main Camera is missing from the sorting scene.");
        var camera = cameraObject.GetComponent<Camera>();
        if (camera == null)
            throw new InvalidOperationException("Main Camera has no Camera component.");

        var rig = new GameObject("XR Origin - Quest viewing");
        // Bring the small workbench up into the user's forward view. Floor
        // tracking still supplies the user's actual head movement and height.
        rig.transform.position = new Vector3(0f, -0.55f, -0.85f);
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(rig.transform, false);

        cameraObject.transform.SetParent(offset.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        cameraObject.transform.localRotation = Quaternion.identity;
        camera.nearClipPlane = 0.04f;
        camera.farClipPlane = 50f;
        camera.stereoTargetEye = StereoTargetEyeMask.Both;

        var tracked = cameraObject.GetComponent<TrackedPoseDriver>();
        if (tracked == null)
            tracked = cameraObject.AddComponent<TrackedPoseDriver>();
        tracked.positionInput = new InputActionProperty(new InputAction(
            "Head Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        tracked.rotationInput = new InputActionProperty(new InputAction(
            "Head Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        tracked.trackingStateInput = new InputActionProperty(new InputAction(
            "Head Tracking", binding: "<XRHMD>/trackingState", expectedControlType: "Integer"));
        tracked.ignoreTrackingState = false;

        var origin = rig.AddComponent<XROrigin>();
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = offset;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, QuestScene))
            throw new InvalidOperationException("Could not save " + QuestScene);

        var buildScenes = EditorBuildSettings.scenes.ToList();
        if (buildScenes.All(item => item.path != QuestScene))
        {
            buildScenes.Add(new EditorBuildSettingsScene(QuestScene, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Quest viewer scene saved: " + QuestScene);
    }

    public static void PrepareAll()
    {
        ConfigureProject();
        BuildScene();
    }

    [MenuItem("Tools/Robotics/Quest/Build Hand Tracking Scene")]
    public static void BuildHandScene()
    {
        if (!System.IO.File.Exists(QuestScene))
            BuildScene();
        var scene = EditorSceneManager.OpenScene(QuestScene, OpenSceneMode.Single);
        var rig = GameObject.Find("XR Origin - Quest viewing");
        var cameraOffset = rig != null ? rig.transform.Find("Camera Offset") : null;
        if (cameraOffset == null)
            throw new InvalidOperationException("Quest scene has no XR Origin / Camera Offset.");

        // The source robot prefab has a desktop keyboard controller that draws
        // unrelated arrow-key instructions. Keep the robot visible but disable
        // that controller in this hand-tracking demonstration.
        var robot = GameObject.Find("niryo_one");
        if (robot != null)
        {
            foreach (var behaviour in robot.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().Name == "Controller")
                    behaviour.enabled = false;
            }
            // This viewer has no robot motion controller. Keep its imported
            // articulation pose from collapsing under gravity in Play mode.
            foreach (var joint in robot.GetComponentsInChildren<ArticulationBody>())
                joint.useGravity = false;
        }

        EnsureFolder("Assets/Materials/QuestHands");
        var left = GetMarkerMaterial("LeftPalm", new Color(0.05f, 0.72f, 1f));
        var right = GetMarkerMaterial("RightPalm", new Color(1f, 0.5f, 0.08f));
        var pinched = GetMarkerMaterial("Pinched", new Color(0.1f, 1f, 0.25f));
        var tips = GetMarkerMaterial("Fingertips", Color.white);

        var demoObject = new GameObject("Hand Tracking Demo - palms and pinch");
        demoObject.transform.SetParent(rig.transform, false);
        var demo = demoObject.AddComponent<QuestHandTrackingDemo>();
        var leftPalm = AddMarker("Left palm - blue, green on pinch", PrimitiveType.Cube,
            cameraOffset, 0.065f, left);
        var rightPalm = AddMarker("Right palm - orange, green on pinch", PrimitiveType.Cube,
            cameraOffset, 0.065f, right);
        var leftThumb = AddMarker("Left thumb tip", PrimitiveType.Sphere,
            cameraOffset, 0.022f, tips);
        var leftIndex = AddMarker("Left index tip", PrimitiveType.Sphere,
            cameraOffset, 0.022f, tips);
        var rightThumb = AddMarker("Right thumb tip", PrimitiveType.Sphere,
            cameraOffset, 0.022f, tips);
        var rightIndex = AddMarker("Right index tip", PrimitiveType.Sphere,
            cameraOffset, 0.022f, tips);

        var cameraTransform = cameraOffset.Find("Main Camera");
        if (cameraTransform == null)
            throw new InvalidOperationException("Quest camera is missing.");
        var labelObject = new GameObject("Hand tracking instructions");
        labelObject.transform.SetParent(cameraTransform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0.24f, 1.2f);
        var label = labelObject.AddComponent<TextMesh>();
        label.text = "RAISE YOUR HANDS\nPINCH THUMB + INDEX";
        label.fontSize = 60;
        label.characterSize = 0.004f;
        label.alignment = TextAlignment.Center;
        label.anchor = TextAnchor.MiddleCenter;
        label.color = Color.black;

        demo.Configure(leftPalm, rightPalm, leftThumb, leftIndex, rightThumb,
            rightIndex, left, right, pinched, label);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, HandScene))
            throw new InvalidOperationException("Could not save " + HandScene);

        var buildScenes = EditorBuildSettings.scenes.ToList();
        if (buildScenes.All(item => item.path != HandScene))
        {
            buildScenes.Add(new EditorBuildSettingsScene(HandScene, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Quest hand tracking scene saved: " + HandScene);
    }

    static Transform AddMarker(string name, PrimitiveType shape, Transform parent,
        float size, Material material)
    {
        var marker = GameObject.CreatePrimitive(shape);
        marker.name = name;
        marker.transform.SetParent(parent, false);
        marker.transform.localScale = Vector3.one * size;
        marker.GetComponent<MeshRenderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.SetActive(false);
        return marker.transform;
    }

    static Material GetMarkerMaterial(string name, Color color)
    {
        var path = "Assets/Materials/QuestHands/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("Unity Standard shader is missing.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    [MenuItem("Tools/Robotics/Quest/Build Hand Tracking APK")]
    public static void BuildHandAndroid()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            throw new InvalidOperationException("Unity Android Build Support is not installed.");
        ConfigureProject();
        if (!System.IO.File.Exists(HandScene))
            BuildHandScene();

        var previousIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var output = System.IO.Path.GetFullPath("Builds/QuestHandTracking.apk");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        try
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                "com.ceg5205.questhandtracking");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { HandScene },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Quest hand APK build failed: " + report.summary.result);
            Debug.Log("Quest hand tracking APK: " + output);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousIdentifier);
        }
    }

    [MenuItem("Tools/Robotics/Quest/Build Quest Viewer APK")]
    public static void BuildAndroid()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            throw new InvalidOperationException("Unity Android Build Support is not installed for this Editor.");
        ConfigureProject();
        if (!System.IO.File.Exists(QuestScene))
            BuildScene();

        var output = System.IO.Path.GetFullPath("Builds/QuestRobotViewer.apk");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { QuestScene },
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Quest viewer APK build failed: " + report.summary.result);
        Debug.Log("Quest viewer APK: " + output);
    }

    static void Disable(string name)
    {
        var item = GameObject.Find(name);
        if (item == null)
            throw new InvalidOperationException("Sorting scene is missing " + name);
        item.SetActive(false);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
