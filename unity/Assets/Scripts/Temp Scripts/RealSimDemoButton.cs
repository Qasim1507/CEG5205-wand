using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public class RealSimDemoButton : MonoBehaviour
{
    [SerializeField] RealSimPickAndPlace m_Publisher;
    public RealSimPickAndPlace Publisher { get => m_Publisher; set => m_Publisher = value; }

    void OnGUI()
    {
        var connected = !ROSConnection.GetOrCreateInstance().HasConnectionError;
        GUI.Box(new Rect(12, 12, 540, 75), "Part 4: simulated and real robot");
        GUI.Label(new Rect(24, 37, 325, 30),
            connected ? "Ready to send a robot motion request" : "Connect to the ROS robot endpoint first");
        if (GUI.Button(new Rect(365, 25, 170, 48), "Start Part 4") && connected && m_Publisher != null)
        {
            m_Publisher.PublishJoints();
            Debug.Log("PART_4_POSES_PUBLISHED");
        }
    }
}
