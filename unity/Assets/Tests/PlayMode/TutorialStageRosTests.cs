using System.Collections;
using NUnit.Framework;
using Unity.Robotics.PickAndPlace;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

[TestFixture, Category("RosIntegration")]
public class TutorialStageRosTests
{
    static IEnumerator WaitForRos()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        var deadline = Time.realtimeSinceStartup + 40f;
        while (Time.realtimeSinceStartup < deadline &&
               (ros.LastMessageReceivedRealtime <= 0 || ros.HasConnectionError))
            yield return null;
        Assert.IsFalse(ros.HasConnectionError, "Unity could not connect to the matching ROS stage.");
    }

    [UnityTest, Timeout(60000)]
    public IEnumerator Part2PublishesRobotAndTargetPoses()
    {
        SceneManager.LoadScene("Part2RosTopic");
        yield return null;
        yield return WaitForRos();
        GameObject.Find("Publisher").GetComponent<SourceDestinationPublisher>().Publish();
        Debug.Log("PART_2_POSES_PUBLISHED");
        yield return new WaitForSeconds(2f);
    }

    [UnityTest, Timeout(180000)]
    public IEnumerator Part4SimulatedActionMovesTargetToPlacement()
    {
        SceneManager.LoadScene("Part4SimAndReal");
        yield return null;
        var placement = GameObject.Find("TargetPlacement").GetComponent<TargetPlacement>();
        Assert.AreEqual(TargetPlacement.PlacementState.Outside, placement.CurrentState);
        yield return WaitForRos();
        GameObject.Find("Publisher").GetComponent<RealSimPickAndPlace>().PublishJoints();
        Debug.Log("PART_4_POSES_PUBLISHED");

        var deadline = Time.realtimeSinceStartup + 95f;
        while (Time.realtimeSinceStartup < deadline &&
               placement.CurrentState != TargetPlacement.PlacementState.InsidePlaced)
            yield return new WaitForFixedUpdate();
        Debug.Log($"PART_4_FINAL target={GameObject.Find("Target").transform.position} placement={placement.transform.position} state={placement.CurrentState}");
        Assert.AreEqual(TargetPlacement.PlacementState.InsidePlaced, placement.CurrentState,
            "The Part 4 action goals did not place the target box.");
    }
}
