using System.IO;
using System.Linq;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RosPortSetup
{
    public static void GenerateMessages()
    {
        var root = Directory.GetParent(Application.dataPath).FullName;
        var ros = Path.Combine(root, "ROS", "src");
        const string output = "Assets/RosMessages";

        MessageAutoGen.GenerateSingleMessage(
            Path.Combine(ros, "moveit_msgs", "msg", "RobotTrajectory.msg"), output, "moveit_msgs");
        MessageAutoGen.GenerateDirectoryMessages(
            Path.Combine(ros, "niryo_moveit", "msg"), output);
        ServiceAutoGen.GenerateSingleService(
            Path.Combine(ros, "niryo_moveit", "srv", "MoverService.srv"), output, "niryo_moveit");

        var moveitMessages = Path.Combine(ros, "moveit_msgs", "msg");
        foreach (var name in new[] { "CollisionObject", "AttachedCollisionObject", "RobotState" })
            MessageAutoGen.GenerateSingleMessage(Path.Combine(moveitMessages, name + ".msg"), output, "moveit_msgs");

        var niryoMessages = Path.Combine(ros, "niryo_one_ros", "niryo_one_msgs");
        MessageAutoGen.GenerateDirectoryMessages(Path.Combine(niryoMessages, "msg"), output);
        ActionAutoGen.GenerateDirectoryActions(Path.Combine(niryoMessages, "action"), output);

        AssetDatabase.Refresh();
        Debug.Log("ROS_MESSAGES_GENERATED");
    }

    public static void CreateAutomaticScene()
    {
        const string scenePath = "Assets/Scenes/PickAndPlaceAutomatic.unity";
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Part1DoneScene.unity");
        var roots = scene.GetRootGameObjects();
        var robot = roots.Single(r => r.name == "niryo_one");
        var target = roots.Single(r => r.name == "Target");
        var placement = roots.Single(r => r.name == "TargetPlacement");

        var ros = new GameObject("ROSConnect").AddComponent<ROSConnection>();
        ros.RosIPAddress = "127.0.0.1";
        ros.RosPort = 10000;
        ros.ShowHud = true;

        var publisher = new GameObject("Publisher");
        var planner = publisher.AddComponent<TrajectoryPlanner>();
        planner.NiryoOne = robot;
        planner.Target = target;
        planner.TargetPlacement = placement;
        publisher.AddComponent<PickAndPlaceLauncher>().Planner = planner;

        EditorSceneManager.SaveScene(scene, scenePath, true);
        AssetDatabase.Refresh();
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(scenePath, true),
            new EditorBuildSettingsScene("Assets/Scenes/Part1DoneScene.unity", true),
            new EditorBuildSettingsScene("Assets/Tests/PlayMode/PlayModeTestScenes/TargetPlacementTest.unity", true)
        };
        Debug.Log($"AUTOMATIC_SCENE_CREATED {scenePath}");
    }

    public static void CreateRemainingTutorialScenes()
    {
        CreateStageScene("Part2RosTopic", 2);
        CreateStageScene("Part4SimAndReal", 4);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Part1DoneScene.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Part2RosTopic.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/PickAndPlaceAutomatic.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Part4SimAndReal.unity", true),
            new EditorBuildSettingsScene("Assets/Tests/PlayMode/PlayModeTestScenes/TargetPlacementTest.unity", true)
        };
        AssetDatabase.Refresh();
        Debug.Log("TUTORIAL_SCENES_CREATED");
    }

    static void CreateStageScene(string sceneName, int stage)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Part1DoneScene.unity");
        var roots = scene.GetRootGameObjects();
        var robot = roots.Single(r => r.name == "niryo_one");
        var target = roots.Single(r => r.name == "Target");
        var placement = roots.Single(r => r.name == "TargetPlacement");

        var ros = new GameObject("ROSConnect").AddComponent<ROSConnection>();
        ros.RosIPAddress = "127.0.0.1";
        ros.RosPort = 10000;
        ros.ShowHud = true;

        var publisher = new GameObject("Publisher");
        if (stage == 2)
        {
            var component = publisher.AddComponent<SourceDestinationPublisher>();
            component.NiryoOne = robot;
            component.Target = target;
            component.TargetPlacement = placement;
            publisher.AddComponent<RosTopicDemoButton>().Publisher = component;
        }
        else
        {
            var component = publisher.AddComponent<RealSimPickAndPlace>();
            component.NiryoOne = robot;
            component.Target = target;
            component.TargetPlacement = placement;
            publisher.AddComponent<RealSimDemoButton>().Publisher = component;
        }

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/" + sceneName + ".unity", true);
    }
}
