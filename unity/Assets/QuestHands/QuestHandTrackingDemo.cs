using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

/// <summary>
/// Displays raw Quest hand joints without moving the robot. All marker
/// transforms live under the XR Origin's tracking-space Camera Offset.
/// </summary>
public sealed class QuestHandTrackingDemo : MonoBehaviour
{
    [SerializeField] Transform leftPalmMarker;
    [SerializeField] Transform rightPalmMarker;
    [SerializeField] Transform leftThumbMarker;
    [SerializeField] Transform leftIndexMarker;
    [SerializeField] Transform rightThumbMarker;
    [SerializeField] Transform rightIndexMarker;
    [SerializeField] Material leftOpenMaterial;
    [SerializeField] Material rightOpenMaterial;
    [SerializeField] Material pinchMaterial;
    [SerializeField] TextMesh statusText;
    [SerializeField] float pinchDistance = 0.025f;
    [SerializeField] float releaseDistance = 0.035f;

    readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
    XRHandSubsystem handSubsystem;
    bool leftPinching;
    bool rightPinching;
    bool leftTracked;
    bool rightTracked;

    void OnEnable()
    {
        HideAll();
        FindHandSubsystem();
    }

    void Update()
    {
        if (handSubsystem == null || !handSubsystem.running)
        {
            Unbind();
            HideAll();
            FindHandSubsystem();
        }
        UpdateStatus();
    }

    void OnDisable()
    {
        Unbind();
        HideAll();
    }

    void FindHandSubsystem()
    {
        SubsystemManager.GetSubsystems(handSubsystems);
        foreach (var candidate in handSubsystems)
        {
            if (!candidate.running)
                continue;
            handSubsystem = candidate;
            handSubsystem.updatedHands += OnHandsUpdated;
            Debug.Log("Quest hand tracking subsystem connected.");
            break;
        }
    }

    void Unbind()
    {
        if (handSubsystem == null)
            return;
        handSubsystem.updatedHands -= OnHandsUpdated;
        handSubsystem = null;
    }

    void OnHandsUpdated(XRHandSubsystem subsystem,
        XRHandSubsystem.UpdateSuccessFlags successFlags,
        XRHandSubsystem.UpdateType updateType)
    {
        if (updateType != XRHandSubsystem.UpdateType.Dynamic)
            return;

        leftTracked = subsystem.leftHand.isTracked;
        rightTracked = subsystem.rightHand.isTracked;
        UpdateHand(subsystem.leftHand, leftPalmMarker, leftThumbMarker,
            leftIndexMarker, leftOpenMaterial, ref leftPinching, "left");
        UpdateHand(subsystem.rightHand, rightPalmMarker, rightThumbMarker,
            rightIndexMarker, rightOpenMaterial, ref rightPinching, "right");
    }

    void UpdateHand(XRHand hand, Transform palmMarker, Transform thumbMarker,
        Transform indexMarker, Material openMaterial, ref bool pinching, string side)
    {
        if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palmPose))
        {
            SetVisible(palmMarker, false);
            SetVisible(thumbMarker, false);
            SetVisible(indexMarker, false);
            pinching = false;
            return;
        }

        MoveMarker(palmMarker, palmPose);
        var hasThumb = hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbPose);
        var hasIndex = hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexPose);

        if (hasThumb)
            MoveMarker(thumbMarker, thumbPose);
        else
            SetVisible(thumbMarker, false);
        if (hasIndex)
            MoveMarker(indexMarker, indexPose);
        else
            SetVisible(indexMarker, false);

        // Hysteresis stops the colour flickering near the pinch threshold.
        var distance = hasThumb && hasIndex
            ? Vector3.Distance(thumbPose.position, indexPose.position)
            : float.PositiveInfinity;
        var nextPinching = pinching
            ? distance < releaseDistance
            : distance < pinchDistance;
        if (nextPinching != pinching)
        {
            pinching = nextPinching;
            Debug.Log("Quest " + side + " hand " + (pinching ? "PINCH" : "RELEASE"));
        }

        if (palmMarker != null && palmMarker.TryGetComponent<MeshRenderer>(out var meshRenderer))
            meshRenderer.sharedMaterial = pinching ? pinchMaterial : openMaterial;
    }

    static void MoveMarker(Transform marker, Pose pose)
    {
        if (marker == null)
            return;
        marker.localPosition = pose.position;
        marker.localRotation = pose.rotation;
        SetVisible(marker, true);
    }

    static void SetVisible(Transform marker, bool visible)
    {
        if (marker != null && marker.gameObject.activeSelf != visible)
            marker.gameObject.SetActive(visible);
    }

    void HideAll()
    {
        SetVisible(leftPalmMarker, false);
        SetVisible(rightPalmMarker, false);
        SetVisible(leftThumbMarker, false);
        SetVisible(leftIndexMarker, false);
        SetVisible(rightThumbMarker, false);
        SetVisible(rightIndexMarker, false);
        leftPinching = false;
        rightPinching = false;
        leftTracked = false;
        rightTracked = false;
    }

    void UpdateStatus()
    {
        if (statusText == null)
            return;
        var tracking = leftTracked && rightTracked ? "BOTH HANDS" : leftTracked ? "LEFT HAND" :
            rightTracked ? "RIGHT HAND" : "RAISE YOUR HANDS";
        var message = tracking + "\nPINCH THUMB + INDEX";
        if (statusText.text != message)
            statusText.text = message;
    }

#if UNITY_EDITOR
    public void Configure(Transform leftPalm, Transform rightPalm, Transform leftThumb,
        Transform leftIndex, Transform rightThumb, Transform rightIndex,
        Material leftOpen, Material rightOpen, Material pinched, TextMesh status)
    {
        leftPalmMarker = leftPalm;
        rightPalmMarker = rightPalm;
        leftThumbMarker = leftThumb;
        leftIndexMarker = leftIndex;
        rightThumbMarker = rightThumb;
        rightIndexMarker = rightIndex;
        leftOpenMaterial = leftOpen;
        rightOpenMaterial = rightOpen;
        pinchMaterial = pinched;
        statusText = status;
    }
#endif
}
