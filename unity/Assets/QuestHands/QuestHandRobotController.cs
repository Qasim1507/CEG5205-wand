using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

/// <summary>
/// Controls only the simulated Niryo articulation in this Quest scene. It never
/// publishes ROS commands. Hold a left-hand pinch to clutch right-hand motion;
/// releasing the left pinch freezes the six joint targets.
/// </summary>
public sealed class QuestHandRobotController : MonoBehaviour
{
    public event Action ControlEngaged;
    public event Action<Rigidbody> ObjectGrasped;
    public event Action<Rigidbody> ObjectReleased;

    public bool IsReady => ready;
    public bool TwoHandPinchHeld => clutchHeld && gripperCloseAmount > 0.8f;
    public Vector3 JawCenter => useVirtualSurgicalJaws || (leftGripper != null && rightGripper != null)
        ? (LeftJawCenter + RightJawCenter) * 0.5f
        : transform.position;
    public Vector3 LeftJawCenter => useVirtualSurgicalJaws
        ? VirtualJawCenter(-1f)
        : useJawCalibration ? leftGripper.transform.TransformPoint(leftJawLocalCenter) : leftFingerMesh.bounds.center;
    public Vector3 RightJawCenter => useVirtualSurgicalJaws
        ? VirtualJawCenter(1f)
        : useJawCalibration ? rightGripper.transform.TransformPoint(rightJawLocalCenter) : rightFingerMesh.bounds.center;

    static readonly string[] JointPaths =
    {
        "world/base_link/shoulder_link",
        "world/base_link/shoulder_link/arm_link",
        "world/base_link/shoulder_link/arm_link/elbow_link",
        "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link",
        "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link",
        "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link/hand_link"
    };

    // Niryo One URDF joint limits, in radians, converted to Unity drive degrees.
    static readonly float[] MinDegrees =
        { -3.05433f, -1.91986f, -1.397485f, -3.05433f, -1.74533f, -2.57436f };
    static readonly float[] MaxDegrees =
        { 3.05433f, 0.640187f, 1.5707963f, 3.05433f, 1.91986f, 2.57436f };

    [SerializeField] Transform robot;
    [SerializeField] Transform trackingSpace;
    [SerializeField] Camera headCamera;
    [SerializeField] TextMesh statusText;
    [SerializeField] bool useJawCalibration;
    [SerializeField] Vector3 leftJawLocalCenter;
    [SerializeField] Vector3 rightJawLocalCenter;
    [SerializeField] float graspRadius = 0.038f;
    [SerializeField] float graspJawAxisTolerance = 0.018f;
    [SerializeField] bool surgeryPrecisionMode;
    [SerializeField] bool carryWithoutParenting;
    [SerializeField] bool useVirtualSurgicalJaws;
    [SerializeField] Transform surgicalToolMount;
    [SerializeField] Vector3 surgicalJawMidLocal;
    [SerializeField] Vector3 surgicalJawAxisLocal;
    [SerializeField] float surgicalOpenJawWidth;
    [SerializeField] float surgicalClosedJawWidth = 0.006f;

    [Header("Motion gain")]
    [SerializeField] float baseDegreesPerMeter = 480f;
    [SerializeField] float shoulderDegreesPerMeter = 360f;
    [SerializeField] float elbowDegreesPerMeter = 360f;
    [SerializeField] float wristDegreesPerDegree = 1f;
    [SerializeField] float maximumJointSpeedDegreesPerSecond = 180f;

    readonly ArticulationBody[] joints = new ArticulationBody[6];
    readonly float[] targets = new float[6];
    readonly float[] referenceTargets = new float[6];
    readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
    ArticulationBody leftGripper;
    ArticulationBody rightGripper;
    Renderer leftFingerMesh;
    Renderer rightFingerMesh;
    Transform gripAnchor;
    Rigidbody[] pickableBoxes;
    Rigidbody heldBox;
    Transform heldBoxOriginalParent;
    bool heldBoxUsedGravity;
    bool heldBoxWasKinematic;
    Collider heldBoxCollider;
    bool heldColliderWasEnabled;
    Vector3 heldLocalOffset;
    Quaternion heldLocalRotation;
    Collider[] fingerColliders;
    XRHandSubsystem handSubsystem;
    Vector3 referencePalmPosition;
    Quaternion referencePalmRotation;
    Vector3 referenceRight;
    Vector3 referenceUp;
    Vector3 referenceForward;
    bool clutchHeld;
    bool ready;
    bool appActive = true;
    float gripperCloseAmount;
    float visualCloseAmount;
    float lastHandUpdate;
    float lastPrecisionUpdate;
    Vector3 filteredPalmPosition;
    Quaternion filteredPalmRotation;
    string state = "WAITING FOR HANDS";

    void Awake()
    {
        if (robot == null || trackingSpace == null || headCamera == null)
        {
            Debug.LogError("Quest hand robot control is missing its robot or tracking references.");
            enabled = false;
            return;
        }

        for (var i = 0; i < joints.Length; ++i)
        {
            var link = robot.Find(JointPaths[i]);
            joints[i] = link != null ? link.GetComponent<ArticulationBody>() : null;
            if (joints[i] == null)
            {
                Debug.LogError("Missing Niryo articulation: " + JointPaths[i]);
                enabled = false;
                return;
            }
            targets[i] = joints[i].xDrive.target;
            ConfigureDrive(joints[i]);
        }

        if (useVirtualSurgicalJaws)
        {
            if (surgicalToolMount == null || surgicalJawAxisLocal.sqrMagnitude < 0.000001f)
            {
                Debug.LogError("Surgical forceps mount or jaw calibration is missing.");
                enabled = false;
                return;
            }
            gripAnchor = surgicalToolMount;
            fingerColliders = Array.Empty<Collider>();
        }
        else
        {
            const string fingerRoot =
                "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link/hand_link" +
                "/tool_link/gripper_base/servo_head";
            var left = robot.Find(fingerRoot + "/control_rod_left/left_gripper");
            var right = robot.Find(fingerRoot + "/control_rod_right/right_gripper");
            leftGripper = left != null ? left.GetComponent<ArticulationBody>() : null;
            rightGripper = right != null ? right.GetComponent<ArticulationBody>() : null;
            if (leftGripper == null || rightGripper == null)
            {
                Debug.LogError("Missing Niryo gripper articulations.");
                enabled = false;
                return;
            }
            ConfigureDrive(leftGripper);
            ConfigureDrive(rightGripper);
            leftFingerMesh = leftGripper.GetComponentInChildren<Renderer>();
            rightFingerMesh = rightGripper.GetComponentInChildren<Renderer>();
            gripAnchor = leftGripper.transform.parent.parent;
            if (leftFingerMesh == null || rightFingerMesh == null || gripAnchor == null)
            {
                Debug.LogError("Niryo gripper jaw meshes are missing.");
                enabled = false;
                return;
            }
            var leftColliders = leftGripper.GetComponentsInChildren<Collider>(true);
            var rightColliders = rightGripper.GetComponentsInChildren<Collider>(true);
            fingerColliders = new Collider[leftColliders.Length + rightColliders.Length];
            leftColliders.CopyTo(fingerColliders, 0);
            rightColliders.CopyTo(fingerColliders, leftColliders.Length);
        }
        var allBodies = FindObjectsByType<Rigidbody>();
        var boxes = new List<Rigidbody>();
        foreach (var body in allBodies)
            if (body.name.StartsWith("Box ") && body.GetComponent<Collider>() != null)
                boxes.Add(body);
        pickableBoxes = boxes.ToArray();
        ready = true;
    }

    static void ConfigureDrive(ArticulationBody body)
    {
        // The imported URDF has zero stiffness and damping. A target change alone
        // therefore cannot actively move or brake the joint.
        var drive = body.xDrive;
        drive.driveType = ArticulationDriveType.Target;
        drive.stiffness = 20000f;
        drive.damping = 1000f;
        drive.forceLimit = 1000f;
        drive.targetVelocity = 0f;
        body.xDrive = drive;
    }

    Vector3 VirtualJawCenter(float side)
    {
        var effectiveClose = heldBox != null ? Mathf.Min(visualCloseAmount, 0.43f) : visualCloseAmount;
        var width = Mathf.Lerp(surgicalOpenJawWidth, surgicalClosedJawWidth, effectiveClose);
        return surgicalToolMount.TransformPoint(
            surgicalJawMidLocal + surgicalJawAxisLocal.normalized * (side * width * 0.5f));
    }

    void Update()
    {
        if (!ready)
            return;
        if (handSubsystem == null || !handSubsystem.running)
        {
            Unbind();
            Freeze("WAITING FOR HANDS");
            FindHandSubsystem();
        }
        else if (Time.unscaledTime - lastHandUpdate > 0.3f)
            Freeze("TRACKING LOST - ARM HELD");

        if (statusText != null)
        {
            var message = "LEFT PINCH: HOLD TO MOVE\n" +
                          "RIGHT MOVE/TILT: J1-J6 | RIGHT PINCH: GRIP\n" + state;
            if (heldBox != null)
                message += "\nHOLDING " + heldBox.name;
            if (statusText.text != message)
                statusText.text = message;
        }
    }

    void FixedUpdate()
    {
        if (!ready)
            return;
        var maximumStep = maximumJointSpeedDegreesPerSecond * Time.fixedDeltaTime;
        for (var i = 0; i < joints.Length; ++i)
        {
            var drive = joints[i].xDrive;
            var next = Mathf.MoveTowards(drive.target, targets[i], maximumStep);
            if (Mathf.Abs(next - drive.target) < 0.0001f)
                continue;
            drive.target = next;
            joints[i].xDrive = drive;
        }

        if (!useVirtualSurgicalJaws)
        {
            var leftDrive = leftGripper.xDrive;
            var rightDrive = rightGripper.xDrive;
            // The original cube scene still uses its articulated gripper.
            var effectiveCloseAmount = heldBox != null
                ? Mathf.Min(gripperCloseAmount, 0.43f) : gripperCloseAmount;
            var leftTarget = Mathf.Lerp(0.01f, -0.01f, effectiveCloseAmount);
            var rightTarget = -leftTarget;
            var fingerStep = 0.04f * Time.fixedDeltaTime;
            leftDrive.target = Mathf.MoveTowards(leftDrive.target, leftTarget, fingerStep);
            rightDrive.target = Mathf.MoveTowards(rightDrive.target, rightTarget, fingerStep);
            leftGripper.xDrive = leftDrive;
            rightGripper.xDrive = rightDrive;
        }
        else
        {
            // Tracking samples vary slightly frame to frame. Smooth only the
            // cosmetic jaw opening; the six robot motors keep their tested gain.
            visualCloseAmount = Mathf.MoveTowards(
                visualCloseAmount, gripperCloseAmount, 8f * Time.fixedDeltaTime);
        }

        // The imported joints can push a small cube out of the jaw gap. Once a
        // cube is actually centered at the jaws, carry it as a grasped object.
        if (heldBox == null && gripperCloseAmount >= 0.45f)
            TryGraspBox();
        else if (heldBox != null && gripperCloseAmount <= 0.20f)
            ReleaseBox();

        if (carryWithoutParenting && heldBox != null)
            UpdatePrecisionHeldPose();
    }

    void TryGraspBox()
    {
        var leftCenter = LeftJawCenter;
        var rightCenter = RightJawCenter;
        var center = (leftCenter + rightCenter) * 0.5f;
        var jawAxis = (rightCenter - leftCenter).normalized;
        Rigidbody nearest = null;
        Collider nearestCollider = null;
        var bestDistance = graspRadius;
        foreach (var box in pickableBoxes)
        {
            if (box == null || !box.gameObject.activeInHierarchy)
                continue;
            var boxCollider = box.GetComponent<Collider>();
            var offset = boxCollider.bounds.center - center;
            var distance = offset.magnitude;
            // Reject a box beside one finger; its center must be between jaws.
            if (distance >= bestDistance ||
                Mathf.Abs(Vector3.Dot(offset, jawAxis)) > graspJawAxisTolerance)
                continue;
            nearest = box;
            nearestCollider = boxCollider;
            bestDistance = distance;
        }
        if (nearest == null)
            return;

        heldBox = nearest;
        heldBoxCollider = nearestCollider;
        heldBoxOriginalParent = nearest.transform.parent;
        heldBoxUsedGravity = nearest.useGravity;
        heldBoxWasKinematic = nearest.isKinematic;
        if (!nearest.isKinematic)
        {
            nearest.linearVelocity = Vector3.zero;
            nearest.angularVelocity = Vector3.zero;
        }
        nearest.useGravity = false;
        nearest.isKinematic = true;
        if (carryWithoutParenting)
        {
            // Keep the heart Rigidbody outside the articulated robot. Nesting
            // it under a moving ArticulationBody destabilizes the Quest solver.
            heldLocalOffset = gripAnchor.InverseTransformPoint(nearest.position);
            heldLocalRotation = Quaternion.Inverse(gripAnchor.rotation) * nearest.rotation;
            heldColliderWasEnabled = nearestCollider.enabled;
            nearestCollider.enabled = false;
        }
        else
        {
            foreach (var fingerCollider in fingerColliders)
                Physics.IgnoreCollision(nearestCollider, fingerCollider, true);
            nearest.transform.SetParent(gripAnchor, true);
        }
        Debug.Log("Quest robot grasped " + nearest.name);
        ObjectGrasped?.Invoke(nearest);
    }

    void UpdatePrecisionHeldPose()
    {
        heldBox.position = gripAnchor.TransformPoint(heldLocalOffset);
        heldBox.rotation = gripAnchor.rotation * heldLocalRotation;
    }

    void ReleaseBox()
    {
        if (heldBox == null)
            return;
        var box = heldBox;
        if (carryWithoutParenting)
        {
            UpdatePrecisionHeldPose();
            if (heldBoxCollider != null)
                heldBoxCollider.enabled = heldColliderWasEnabled;
        }
        else
        {
            box.transform.SetParent(heldBoxOriginalParent, true);
        }
        if (!carryWithoutParenting && heldBoxCollider != null)
            foreach (var fingerCollider in fingerColliders)
                if (fingerCollider != null)
                    Physics.IgnoreCollision(heldBoxCollider, fingerCollider, false);
        box.isKinematic = heldBoxWasKinematic;
        box.useGravity = heldBoxUsedGravity;
        if (!box.isKinematic)
        {
            box.linearVelocity = Vector3.zero;
            box.angularVelocity = Vector3.zero;
        }
        Debug.Log("Quest robot released " + box.name);
        heldBox = null;
        heldBoxCollider = null;
        ObjectReleased?.Invoke(box);
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
            lastHandUpdate = Time.unscaledTime;
            state = "LEFT PINCH TO ENGAGE";
            Debug.Log("Quest robot hand control connected; simulation only.");
            break;
        }
    }

    void OnHandsUpdated(XRHandSubsystem subsystem,
        XRHandSubsystem.UpdateSuccessFlags successFlags,
        XRHandSubsystem.UpdateType updateType)
    {
        if (updateType != XRHandSubsystem.UpdateType.Dynamic || !ready || !appActive)
            return;
        lastHandUpdate = Time.unscaledTime;

        var leftHand = subsystem.leftHand;
        var rightHand = subsystem.rightHand;
        if (!leftHand.isTracked || !rightHand.isTracked ||
            !rightHand.GetJoint(XRHandJointID.Palm).TryGetPose(out var rightPalm) ||
            !TryPinchDistance(leftHand, out var leftDistance))
        {
            Freeze("RAISE BOTH HANDS - ARM HELD");
            return;
        }

        var shouldClutch = leftDistance < (clutchHeld ? 0.035f : 0.025f);
        if (!shouldClutch)
        {
            Freeze("LEFT PINCH TO ENGAGE");
            return;
        }

        var palmPosition = trackingSpace.TransformPoint(rightPalm.position);
        var palmRotation = trackingSpace.rotation * rightPalm.rotation;
        if (!clutchHeld)
            CaptureReference(palmPosition, palmRotation);

        if (surgeryPrecisionMode)
        {
            var dt = Mathf.Clamp(Time.unscaledTime - lastPrecisionUpdate, 0f, 0.05f);
            var blend = 1f - Mathf.Exp(-dt / 0.12f);
            filteredPalmPosition = Vector3.Lerp(filteredPalmPosition, palmPosition, blend);
            filteredPalmRotation = Quaternion.Slerp(filteredPalmRotation, palmRotation, blend);
            lastPrecisionUpdate = Time.unscaledTime;
            palmPosition = filteredPalmPosition;
            palmRotation = filteredPalmRotation;
        }

        clutchHeld = true;
        state = "MOVING VIRTUAL ARM";
        var displacement = palmPosition - referencePalmPosition;
        var rotationDelta = palmRotation * Quaternion.Inverse(referencePalmRotation);
        rotationDelta.ToAngleAxis(out var angle, out var axis);
        if (angle > 180f)
            angle -= 360f;
        var rotationDegrees = axis * angle;

        // Translation is measured in the user's view frame captured at clutch.
        // The last three joints follow changes in right-palm orientation.
        var right = Vector3.Dot(displacement, referenceRight);
        var up = Vector3.Dot(displacement, referenceUp);
        var forward = Vector3.Dot(displacement, referenceForward);
        if (surgeryPrecisionMode)
        {
            right = Deadband(right, 0.007f);
            up = Deadband(up, 0.007f);
            forward = Deadband(forward, 0.007f);
            rotationDegrees.x = Deadband(rotationDegrees.x, 2.5f);
            rotationDegrees.y = Deadband(rotationDegrees.y, 2.5f);
            rotationDegrees.z = Deadband(rotationDegrees.z, 2.5f);
        }
        targets[0] = Limit(0, referenceTargets[0] +
            -right * baseDegreesPerMeter);
        targets[1] = Limit(1, referenceTargets[1] +
            up * shoulderDegreesPerMeter);
        targets[2] = Limit(2, referenceTargets[2] +
            forward * elbowDegreesPerMeter);
        targets[3] = Limit(3, referenceTargets[3] +
            Vector3.Dot(rotationDegrees, referenceUp) * wristDegreesPerDegree);
        targets[4] = Limit(4, referenceTargets[4] +
            Vector3.Dot(rotationDegrees, referenceRight) * wristDegreesPerDegree);
        targets[5] = Limit(5, referenceTargets[5] +
            Vector3.Dot(rotationDegrees, referenceForward) * wristDegreesPerDegree);

        if (TryPinchDistance(rightHand, out var rightDistance))
            gripperCloseAmount = Mathf.Clamp01((0.05f - rightDistance) / 0.035f);
    }

    void CaptureReference(Vector3 palmPosition, Quaternion palmRotation)
    {
        referencePalmPosition = palmPosition;
        referencePalmRotation = palmRotation;
        filteredPalmPosition = palmPosition;
        filteredPalmRotation = palmRotation;
        lastPrecisionUpdate = Time.unscaledTime;
        referenceRight = headCamera.transform.right;
        referenceUp = headCamera.transform.up;
        referenceForward = headCamera.transform.forward;
        for (var i = 0; i < joints.Length; ++i)
            referenceTargets[i] = joints[i].xDrive.target;
        Debug.Log("Quest robot clutch engaged.");
        ControlEngaged?.Invoke();
    }

    static float Deadband(float value, float threshold)
    {
        return Mathf.Sign(value) * Mathf.Max(0f, Mathf.Abs(value) - threshold);
    }

    static bool TryPinchDistance(XRHand hand, out float distance)
    {
        distance = 0f;
        if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb) ||
            !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index))
            return false;
        distance = Vector3.Distance(thumb.position, index.position);
        return true;
    }

    static float Limit(int index, float degrees)
    {
        return Mathf.Clamp(degrees,
            MinDegrees[index] * Mathf.Rad2Deg,
            MaxDegrees[index] * Mathf.Rad2Deg);
    }

    void Freeze(string newState)
    {
        if (clutchHeld)
        {
            for (var i = 0; i < joints.Length; ++i)
            {
                if (joints[i].dofCount > 0)
                {
                    // Cancel the old drive command immediately; otherwise the
                    // simulated arm keeps travelling after the clutch opens.
                    targets[i] = Limit(i, joints[i].jointPosition[0] * Mathf.Rad2Deg);
                    var drive = joints[i].xDrive;
                    drive.target = targets[i];
                    drive.targetVelocity = 0f;
                    joints[i].xDrive = drive;
                }
            }
            Debug.Log("Quest robot clutch released; joint targets held.");
        }
        clutchHeld = false;
        state = newState;
    }

    void Unbind()
    {
        if (handSubsystem == null)
            return;
        handSubsystem.updatedHands -= OnHandsUpdated;
        handSubsystem = null;
    }

    void OnDisable()
    {
        Unbind();
        Freeze("CONTROL OFF");
        ReleaseBox();
    }

    void OnApplicationPause(bool paused)
    {
        appActive = !paused;
        if (paused)
            Freeze("APP PAUSED - ARM HELD");
    }

    void OnApplicationFocus(bool focused)
    {
        appActive = focused;
        if (!focused)
            Freeze("APP UNFOCUSED - ARM HELD");
    }
}
