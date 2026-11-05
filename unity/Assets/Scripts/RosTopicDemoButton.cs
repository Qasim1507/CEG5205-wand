using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public class RosTopicDemoButton : MonoBehaviour
{
    [SerializeField] SourceDestinationPublisher m_Publisher;
    public SourceDestinationPublisher Publisher { get => m_Publisher; set => m_Publisher = value; }

    void OnGUI()
    {
        var connected = !ROSConnection.GetOrCreateInstance().HasConnectionError;
        GUI.Box(new Rect(12, 12, 520, 75), "Part 2: ROS topic publisher");
        GUI.Label(new Rect(24, 37, 330, 30),
            connected ? "Send the arm, box and destination poses to ROS" : "Start ROS Part 2, then wait for connection");
        if (GUI.Button(new Rect(360, 25, 155, 48), "Publish to ROS") && connected && m_Publisher != null)
        {
            m_Publisher.Publish();
            Debug.Log("PART_2_POSES_PUBLISHED");
        }
    }
}
