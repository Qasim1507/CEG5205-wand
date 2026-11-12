using System.Collections;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public class MultiBoxSortingController : MonoBehaviour
{
    [System.Serializable]
    public class SortJob
    {
        public string Label;
        public GameObject Box;
        public GameObject DropTarget;
        public SortingBin Bin;
    }

    [SerializeField] TrajectoryPlanner m_Planner;
    [SerializeField] SortJob[] m_Jobs;

    bool[] m_Completed;
    int m_ActiveJob = -1;
    string m_Status = "Start ROS Part 3, then choose a box.";
    public string Status => m_Status;

    public bool IsCompleted(int index)
    {
        return m_Completed != null && index >= 0 && index < m_Completed.Length && m_Completed[index];
    }

    void Awake()
    {
        m_Completed = new bool[m_Jobs == null ? 0 : m_Jobs.Length];
    }

    void OnEnable()
    {
        if (m_Planner != null)
            m_Planner.OperationCompleted += OnOperationCompleted;
    }

    void OnDisable()
    {
        if (m_Planner != null)
            m_Planner.OperationCompleted -= OnOperationCompleted;
    }

    public void RunJob(int index)
    {
        if (m_Planner == null || m_Jobs == null || index < 0 || index >= m_Jobs.Length ||
            m_ActiveJob >= 0 || m_Completed[index])
            return;

        var ros = ROSConnection.GetOrCreateInstance();
        if (ros.HasConnectionError || ros.LastMessageReceivedRealtime <= 0)
        {
            m_Status = "Waiting for ROS Part 3 on 127.0.0.1:10000.";
            return;
        }

        var job = m_Jobs[index];
        if (job.Box == null || job.DropTarget == null || job.Bin == null)
        {
            m_Status = "Job is missing its box, bin, or drop target.";
            return;
        }

        if (m_Planner.TryPickAndPlace(job.Box, job.DropTarget))
        {
            m_ActiveJob = index;
            m_Status = "MoveIt is planning for " + job.Label + "...";
            Debug.Log("SORT_REQUEST box=" + job.Box.name + " bin=" + job.Bin.name);
        }
    }

    void OnOperationCompleted(bool trajectoriesExecuted)
    {
        if (m_ActiveJob < 0)
            return;

        if (!trajectoriesExecuted)
        {
            m_Status = "MoveIt could not plan this move. Adjust the box/bin and retry.";
            m_ActiveJob = -1;
            return;
        }

        StartCoroutine(CheckPlacementAfterLanding(m_ActiveJob));
    }

    IEnumerator CheckPlacementAfterLanding(int index)
    {
        m_Status = "Box released; checking that it landed inside the bin...";
        yield return new WaitForSeconds(2.5f);

        var job = m_Jobs[index];
        if (job.Bin.Contains(job.Box))
        {
            m_Completed[index] = true;
            m_Status = job.Label + " landed in " + job.Bin.name + ".";
            Debug.Log("SORT_SUCCESS box=" + job.Box.name + " bin=" + job.Bin.name);
        }
        else
        {
            m_Status = job.Label + " missed " + job.Bin.name + ". Reposition and retry.";
            Debug.LogWarning("SORT_MISSED box=" + job.Box.name + " boxPosition=" +
                job.Box.transform.position + " dropTarget=" + job.DropTarget.transform.position +
                " bin=" + job.Bin.name);
        }

        m_ActiveJob = -1;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 400, 225), GUI.skin.box);
        GUILayout.Label("Multi-box sorting | ROS Part 3");
        GUILayout.Label(m_Status);

        if (m_Jobs != null)
        {
            for (var i = 0; i < m_Jobs.Length; i++)
            {
                GUI.enabled = m_ActiveJob < 0 && !m_Completed[i] && m_Planner != null && !m_Planner.IsBusy;
                if (GUILayout.Button((m_Completed[i] ? "Done: " : "Pick: ") + m_Jobs[i].Label, GUILayout.Height(35)))
                    RunJob(i);
            }
        }

        GUI.enabled = true;
        GUILayout.EndArea();
    }
}
