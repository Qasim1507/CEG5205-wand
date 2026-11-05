using UnityEngine;

/// <summary>Moves a slim, non-physical forceps model with the calibrated virtual jaws.</summary>
public sealed class SurgicalForcepsVisual : MonoBehaviour
{
    [SerializeField] QuestHandRobotController controller;
    [SerializeField] Transform mount;
    [SerializeField] Transform shaft;
    [SerializeField] Transform leftTine;
    [SerializeField] Transform rightTine;
    [SerializeField] Transform leftTip;
    [SerializeField] Transform rightTip;

    void LateUpdate()
    {
        if (controller == null || !controller.IsReady || mount == null)
            return;
        SetPose(controller.LeftJawCenter, controller.RightJawCenter);
    }

    public void SetPreview(Vector3 left, Vector3 right) => SetPose(left, right);

    void SetPose(Vector3 left, Vector3 right)
    {
        var jawMidpoint = (left + right) * 0.5f;
        var forward = jawMidpoint - mount.position;
        if (forward.sqrMagnitude < 0.0001f)
            forward = mount.forward;
        forward.Normalize();
        var fork = jawMidpoint - forward * 0.036f;
        Segment(shaft, jawMidpoint - forward * 0.115f, fork, 0.009f);
        Segment(leftTine, fork, left + forward * 0.019f, 0.0035f);
        Segment(rightTine, fork, right + forward * 0.019f, 0.0035f);
        leftTip.position = left + forward * 0.019f;
        rightTip.position = right + forward * 0.019f;
        leftTip.localScale = Vector3.one * 0.009f;
        rightTip.localScale = Vector3.one * 0.009f;
    }

    static void Segment(Transform segment, Vector3 a, Vector3 b, float radius)
    {
        var delta = b - a;
        segment.position = (a + b) * 0.5f;
        segment.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        segment.localScale = new Vector3(radius * 2f, delta.magnitude * 0.5f, radius * 2f);
    }
}
