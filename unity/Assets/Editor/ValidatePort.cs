using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ValidatePort
{
    const string ReadyScene = "Assets/Scenes/Part1DoneScene.unity";

    [MenuItem("Tools/Pick and Place/Open Ready Scene")]
    public static void OpenReadyScene()
    {
        EditorSceneManager.OpenScene(ReadyScene);
    }

    public static void Configure()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ReadyScene, true),
            new EditorBuildSettingsScene("Assets/Tests/PlayMode/PlayModeTestScenes/TargetPlacementTest.unity", true)
        };
        OpenReadyScene();
        Debug.Log($"PORT_CONFIGURED buildScene={ReadyScene}");
        Run();
    }

    [MenuItem("Tools/Pick and Place/Validate Scenes")]
    public static void Run()
    {
        foreach (var path in new[]
        {
            "Assets/Scenes/PickAndPlaceAutomatic.unity",
            "Assets/Scenes/Part2RosTopic.unity",
            "Assets/Scenes/Part4SimAndReal.unity",
            "Assets/Scenes/Part1DoneScene.unity",
            "Assets/Scenes/TutorialScene.unity",
            "Assets/Scenes/DemoScene.unity"
        })
        {
            var scene = EditorSceneManager.OpenScene(path);
            var roots = scene.GetRootGameObjects();
            var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var missing = all.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var names = string.Join(", ", roots.Select(r => r.name));
            Debug.Log($"PORT_SCENE {path} roots=[{names}] objects={all.Length} missingScripts={missing}");
            foreach (var name in new[] { "niryo_one", "Table", "Target", "TargetPlacement" })
            {
                Debug.Log($"PORT_OBJECT {path} {name}={all.Any(t => t.name == name)}");
            }
            if (path.Contains("Part1DoneScene"))
            {
                var robot = all.First(t => t.name == "niryo_one");
                var articulations = robot.GetComponentsInChildren<ArticulationBody>(true);
                var baseLink = all.First(t => t.name == "base_link").GetComponent<ArticulationBody>();
                Debug.Log($"PORT_ROBOT articulations={articulations.Length} baseImmovable={baseLink.immovable}");
            }
            if (path.Contains("PickAndPlaceAutomatic"))
            {
                var planner = roots.Single(r => r.name == "Publisher").GetComponent<TrajectoryPlanner>();
                var launcher = planner.GetComponent<PickAndPlaceLauncher>();
                var hand = planner.NiryoOne.transform.Find("world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link/hand_link");
                var leftFinger = planner.NiryoOne.transform.Find("world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link/hand_link/tool_link/gripper_base/servo_head/control_rod_left/left_gripper");
                var rightFinger = planner.NiryoOne.transform.Find("world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link/hand_link/tool_link/gripper_base/servo_head/control_rod_right/right_gripper");
                Debug.Log($"PORT_PLANNER robot={planner.NiryoOne != null} target={planner.Target != null} placement={planner.TargetPlacement != null} launcher={launcher.Planner == planner} hand={hand != null} fingers={leftFinger != null && rightFinger != null}");
            }
            if (path.Contains("Part2RosTopic"))
            {
                var publisher = roots.Single(r => r.name == "Publisher").GetComponent<SourceDestinationPublisher>();
                Debug.Log($"PORT_PART2 robot={publisher.NiryoOne != null} target={publisher.Target != null} placement={publisher.TargetPlacement != null} button={publisher.GetComponent<RosTopicDemoButton>().Publisher == publisher}");
            }
            if (path.Contains("Part4SimAndReal"))
            {
                var publisher = roots.Single(r => r.name == "Publisher").GetComponent<RealSimPickAndPlace>();
                Debug.Log($"PORT_PART4 robot={publisher.NiryoOne != null} target={publisher.Target != null} placement={publisher.TargetPlacement != null} button={publisher.GetComponent<RealSimDemoButton>().Publisher == publisher}");
            }
        }
    }

}
