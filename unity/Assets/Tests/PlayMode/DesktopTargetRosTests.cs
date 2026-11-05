using System.Collections;
using NUnit.Framework;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

[TestFixture, Category("RosIntegration")]
public class DesktopTargetRosTests
{
    [UnityTest, Timeout(180000)]
    public IEnumerator TargetPosePlansAndMovesTheArm()
    {
        SceneManager.LoadScene("DesktopHandTarget");
        yield return null;

        var controller = GameObject.Find("Publisher").GetComponent<DesktopTargetController>();
        Assert.IsNotNull(controller);
        Assert.IsNotNull(controller.Target);

        var ros = ROSConnection.GetOrCreateInstance();
        var connectionDeadline = Time.realtimeSinceStartup + 45f;
        while (Time.realtimeSinceStartup < connectionDeadline &&
               (ros.HasConnectionError || ros.LastMessageReceivedRealtime <= 0))
            yield return null;
        Assert.IsFalse(ros.HasConnectionError, "ROS Part 3 did not connect.");
        Assert.Greater(ros.LastMessageReceivedRealtime, 0, "No message received from ROS.");

        foreach (var position in new[]
        {
            new Vector3(0.216f, 0.746f, -0.157f),
            new Vector3(-0.198f, 0.74f, -0.18f)
        })
        {
            controller.SetTargetPosition(position);
            Assert.IsTrue(controller.RequestMove(), "The controller rejected target " + position);
            var deadline = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < deadline && controller.IsBusy)
                yield return new WaitForFixedUpdate();

            Assert.IsFalse(controller.IsBusy, "The arm never finished target " + position);
            StringAssert.StartsWith("Arm movement complete", controller.Status);
        }
    }
}
