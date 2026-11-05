using System.Collections;
using NUnit.Framework;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

[TestFixture, Category("RosIntegration")]
public class MultiBoxSortingRosTests
{
    [UnityTest, Timeout(480000)]
    public IEnumerator EachBoxIsPlannedAndPlacedInItsBin()
    {
        SceneManager.LoadScene("MultiBoxSorting");
        yield return null;

        var sorter = GameObject.Find("Publisher").GetComponent<MultiBoxSortingController>();
        Assert.IsNotNull(sorter);

        var ros = ROSConnection.GetOrCreateInstance();
        var connectionDeadline = Time.realtimeSinceStartup + 45f;
        while (Time.realtimeSinceStartup < connectionDeadline &&
               (ros.LastMessageReceivedRealtime <= 0 || ros.HasConnectionError))
            yield return null;
        Assert.IsFalse(ros.HasConnectionError, "ROS Part 3 did not connect.");

        for (var job = 0; job < 3; job++)
        {
            sorter.RunJob(job);
            var deadline = Time.realtimeSinceStartup + 140f;
            while (Time.realtimeSinceStartup < deadline && !sorter.IsCompleted(job) &&
                   !sorter.Status.Contains("missed") && !sorter.Status.Contains("could not"))
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(sorter.IsCompleted(job), "Box " + (job + 1) + " was not placed: " + sorter.Status);
        }
    }
}
