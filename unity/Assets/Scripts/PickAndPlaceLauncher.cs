using System.Collections;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public class PickAndPlaceLauncher : MonoBehaviour
{
    [SerializeField] TrajectoryPlanner m_Planner;
    [SerializeField] bool m_RunWhenRosConnects = true;
    bool m_Sent;

    public TrajectoryPlanner Planner { get => m_Planner; set => m_Planner = value; }

    IEnumerator Start()
    {
        if (!m_RunWhenRosConnects)
            yield break;

        var ros = ROSConnection.GetOrCreateInstance();
        yield return new WaitUntil(() => ros.LastMessageReceivedRealtime > 0 && !ros.HasConnectionError);
        yield return new WaitForSeconds(2f);
        StartPickAndPlace();
    }

    public void StartPickAndPlace()
    {
        if (m_Sent || m_Planner == null)
            return;

        m_Sent = true;
        Debug.Log("PICK_AND_PLACE_REQUEST_SENT");
        m_Planner.PublishJoints();
    }

    void OnGUI()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        GUI.Box(new Rect(12, 12, 310, 80), "Pick and Place");
        GUI.Label(new Rect(24, 38, 280, 22),
            ros.HasConnectionError ? "Waiting for ROS on 127.0.0.1:10000" :
            m_Sent ? "Motion request sent" : "Press the button to run the demo");
        if (GUI.Button(new Rect(330, 20, 180, 56), "Pick and Place"))
            StartPickAndPlace();
    }
}
