using System.Collections;
using System.Linq;
using RosMessageTypes.Geometry;
using RosMessageTypes.NiryoMoveit;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using UnityEngine;

/// <summary>
/// Desktop stand-in for a tracked hand. Move the target with WASD/QE or drag
/// it in the Game view, then request one MoveIt trajectory; no grasp is sent.
/// </summary>
public class DesktopTargetController : MonoBehaviour
{
    const float HorizontalSpeed = 0.14f;
    const float VerticalSpeed = 0.12f;

    static readonly string[] JointPaths =
    {
        "world/base_link/shoulder_link",
        "world/base_link/shoulder_link/arm_link",
        "world/base_link/shoulder_link/arm_link/elbow_link",
        "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link",
        "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link",
        "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link/hand_link"
    };

    [SerializeField] GameObject m_Robot;
    [SerializeField] Transform m_Target;
    [SerializeField] Camera m_Camera;
    [SerializeField] string m_ServiceName = "niryo_moveit_target";
    [SerializeField] Vector2 m_XBounds = new Vector2(-0.24f, 0.24f);
    [SerializeField] Vector2 m_ZBounds = new Vector2(-0.33f, 0.02f);
    [SerializeField] Vector2 m_HeightBounds = new Vector2(0.71f, 1.02f);

    ArticulationBody[] m_Joints;
    ROSConnection m_Ros;
    bool m_Dragging;
    bool m_Busy;
    string m_Status = "Start ROS Part 3. Move the yellow target, then press Move Arm.";

    public bool IsBusy => m_Busy;
    public string Status => m_Status;
    public Transform Target => m_Target;

    void Start()
    {
        if (m_Robot == null || m_Target == null)
        {
            m_Status = "Robot or target is missing from this scene.";
            enabled = false;
            return;
        }

        if (m_Camera == null)
            m_Camera = Camera.main;
        m_Joints = JointPaths.Select(path => m_Robot.transform.Find(path))
            .Select(link => link == null ? null : link.GetComponent<ArticulationBody>()).ToArray();
        if (m_Joints.Any(joint => joint == null))
        {
            m_Status = "Could not find all six Niryo arm joints.";
            enabled = false;
            return;
        }

        m_Ros = ROSConnection.GetOrCreateInstance();
        m_Ros.RegisterRosService<MoverServiceRequest, MoverServiceResponse>(m_ServiceName);
    }

    void Update()
    {
        if (m_Target == null || m_Busy)
            return;

        var sideways = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        var forward = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        var vertical = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        var horizontal = Vector3.ClampMagnitude(new Vector3(sideways, 0f, forward), 1f);
        var speedMultiplier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 2f : 1f;
        if (horizontal.sqrMagnitude > 0f || vertical != 0f)
            SetTargetPosition(m_Target.position +
                (horizontal * HorizontalSpeed + Vector3.up * vertical * VerticalSpeed) *
                speedMultiplier * Time.deltaTime);

        if (m_Camera == null)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            var ray = m_Camera.ScreenPointToRay(Input.mousePosition);
            m_Dragging = Physics.RaycastAll(ray, 20f, ~0, QueryTriggerInteraction.Collide).Any(hit =>
                hit.collider.transform == m_Target || hit.collider.transform.IsChildOf(m_Target));
        }

        if (Input.GetMouseButtonUp(0))
            m_Dragging = false;

        if (m_Dragging && Input.GetMouseButton(0))
        {
            var ray = m_Camera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, m_Target.position);
            if (plane.Raycast(ray, out var distance))
            {
                var point = ray.GetPoint(distance);
                SetTargetPosition(new Vector3(point.x, m_Target.position.y, point.z));
            }
        }
    }

    public void SetTargetPosition(Vector3 position)
    {
        if (m_Target == null)
            return;
        m_Target.position = new Vector3(
            Mathf.Clamp(position.x, m_XBounds.x, m_XBounds.y),
            Mathf.Clamp(position.y, m_HeightBounds.x, m_HeightBounds.y),
            Mathf.Clamp(position.z, m_ZBounds.x, m_ZBounds.y));
    }

    public bool RequestMove()
    {
        if (m_Busy || m_Ros == null || m_Target == null || m_Joints == null)
            return false;

        if (m_Ros.HasConnectionError || m_Ros.LastMessageReceivedRealtime <= 0)
        {
            m_Status = "Waiting for ROS Part 3 on 127.0.0.1:10000.";
            return false;
        }

        var joints = new NiryoMoveitJointsMsg();
        for (var index = 0; index < m_Joints.Length; index++)
            joints.joints[index] = m_Joints[index].jointPosition[0];

        var pose = new PoseMsg
        {
            position = m_Target.position.To<FLU>(),
            orientation = Quaternion.Euler(90f, 90f, 0f).To<FLU>()
        };
        // The service uses the tutorial request type. Only pick_pose is read
        // by niryo_moveit_target; place_pose is intentionally unused.
        var request = new MoverServiceRequest(joints, pose, new PoseMsg());
        m_Busy = true;
        m_Status = "MoveIt is planning one arm movement...";
        m_Ros.SendServiceMessage<MoverServiceResponse>(m_ServiceName, request, OnPlanReceived);
        return true;
    }

    void OnPlanReceived(MoverServiceResponse response)
    {
        if (response == null || response.trajectories == null || response.trajectories.Length != 1 ||
            response.trajectories[0].joint_trajectory.points.Length == 0)
        {
            m_Status = "No path found. Move the target closer to the arm and retry.";
            m_Busy = false;
            return;
        }

        StartCoroutine(Execute(response));
    }

    IEnumerator Execute(MoverServiceResponse response)
    {
        m_Status = "Moving arm to the target...";
        foreach (var point in response.trajectories[0].joint_trajectory.points)
        {
            if (point.positions == null || point.positions.Length < m_Joints.Length)
            {
                m_Status = "ROS returned an incomplete joint trajectory.";
                m_Busy = false;
                yield break;
            }

            for (var index = 0; index < m_Joints.Length; index++)
            {
                var drive = m_Joints[index].xDrive;
                drive.target = (float)point.positions[index] * Mathf.Rad2Deg;
                m_Joints[index].xDrive = drive;
            }
            yield return new WaitForSeconds(0.1f);
        }

        yield return new WaitForSeconds(0.5f);
        m_Busy = false;
        m_Status = "Arm movement complete. Move the target and try another point.";
        Debug.Log("DESKTOP_TARGET_MOVE_COMPLETE position=" + m_Target.position);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 440, 195), GUI.skin.box);
        GUILayout.Label("Desktop hand target | ROS MoveIt");
        GUILayout.Label(m_Status);
        GUILayout.Label("WASD: across table | Q/E: down/up | Shift: faster");
        GUILayout.Label("Click Game view for keyboard input, or drag the yellow target.");
        if (m_Target != null)
        {
            GUILayout.Label("Target height: " + m_Target.position.y.ToString("F2") + " m");
            GUI.enabled = !m_Busy;
            var height = GUILayout.HorizontalSlider(m_Target.position.y, m_HeightBounds.x, m_HeightBounds.y);
            if (!Mathf.Approximately(height, m_Target.position.y))
                SetTargetPosition(new Vector3(m_Target.position.x, height, m_Target.position.z));
        }
        GUI.enabled = !m_Busy;
        if (GUILayout.Button("Move Arm to Target", GUILayout.Height(32)))
            RequestMove();
        GUI.enabled = true;
        GUILayout.EndArea();
    }
}
