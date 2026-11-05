using System.Collections;
using NUnit.Framework;
using Unity.Robotics.PickAndPlace;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

[TestFixture, Category("RosIntegration")]
public class RosPickAndPlaceTests
{
    [UnityTest, Timeout(180000)]
    public IEnumerator RobotPicksAndPlacesTargetWithRos()
    {
        SceneManager.LoadScene("PickAndPlaceAutomatic");
        yield return null;

        var placement = GameObject.Find("TargetPlacement").GetComponent<TargetPlacement>();
        Assert.AreEqual(TargetPlacement.PlacementState.Outside, placement.CurrentState);

        var deadline = Time.realtimeSinceStartup + 150f;
        while (Time.realtimeSinceStartup < deadline &&
               placement.CurrentState != TargetPlacement.PlacementState.InsidePlaced)
            yield return new WaitForFixedUpdate();

        Assert.AreEqual(TargetPlacement.PlacementState.InsidePlaced, placement.CurrentState,
            "The target was not placed by the ROS-planned trajectory.");
    }
}
