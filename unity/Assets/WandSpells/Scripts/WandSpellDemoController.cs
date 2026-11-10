using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Meeting vertical slice. Keyboard is a simulated classifier; the same
/// CastSpell entry point accepts predictions from the Python IMU model.
/// </summary>
public sealed class WandSpellDemoController : MonoBehaviour {
    public static event Action<string> GameModeRequested;
    public static event Action<int, Quaternion> DuelPoseReceived;
    public static event Action<int, string, float> DuelCastReceived;

    [Serializable]
    sealed class SensorPacket {
        public string type;
        public string session;
        public int id;
        public int pose_id;
        public int origin;
        public int player;
        public string mode;
        public string spell;
        public float conf;
        public float[] q;
    }

    sealed class IncomingPacket {
        public string json;
        public IPEndPoint endpoint;
    }

    [Serializable]
    sealed class Reply {
        public string type = "ack";
        public string session;
        public int id;
        public string status;
        public int pose_count;
        public float pose_angle;
    }

    public Transform wand;
    public Transform wandTip;
    public Transform[] wandStyles;
    public Transform[] wandDisplays;
    public Transform[] targets;
    public Renderer[] targetCrystals;
    public TextMesh statusText;
    public TextMesh scoreText;
    public TextMesh choiceText;
    public Renderer lavaRenderer;
    public Transform sceneRig;
    public Transform[] entranceDoors;
    public Transform aimMarker;
    public Transform crosshair;
    public int udpPort = 5055;

    readonly Queue<IncomingPacket> incoming = new Queue<IncomingPacket>();
    string activeSession;
    int lastPacketId;
    readonly string[] duelSessions = new string[2];
    readonly int[] duelLastCastIds = new int[2];
    readonly int[] duelLastPoseIds = new int[2];
    readonly int[] duelPoseOrigins = { int.MinValue, int.MinValue };
    int gazeChoice = -1;
    float gazeStarted;
    string poseSession;
    int poseOrigin;
    int lastPoseId;
    int poseCount;
    bool hasPose;
    bool recenterButtonDown;
    Quaternion wandRest;
    Quaternion poseAnchor;
    Quaternion poseReference;
    Quaternion latestPose;
    Quaternion poseTarget;
    float lastPoseAt;
    UdpClient receiver;
    Thread reader;
    volatile bool listening;
    int selected;
    int score;
    int wandStyle = -1;
    bool busy;
    bool arenaReady;
    bool[] broken;
    Camera viewCamera;
    TextMesh gestureText;
    Material hudPanelMaterial;
    Transform hudRoot;
    // The only four spell types accepted by the game and the UDP classifier.
    static readonly string[] SpellNames = { "fire", "water", "wind", "lightning" };
    static readonly Color[] SpellColors = {
        new Color(1f, .02f, .02f),   // Fire
        new Color(.10f, .55f, 1f),   // Water: blue
        new Color(.02f, .85f, .08f), // Wind
        new Color(1f, .90f, .12f)    // Lightning: yellow
    };
    float lastSpellTime = -100f;
    Quaternion recenter = Quaternion.identity;
    Vector3[] crystalHome;
    Vector2 aimViewport = new Vector2(.5f, .5f);
    bool skipMouseDelta;

    [SerializeField] private Renderer runicAlcoveShadow;   // the Renderer on "Runic alcove shadow"
    [SerializeField] private Material whiteMaterial;       // your existing White material asset

    [Header("Level 2 dummy")]
    [SerializeField] private DummyHitReaction dummyReaction;   // LEVEL2: the DummyHitReaction on the dummy
    [SerializeField] private float dummyAimRadius = 0.3f;      // LEVEL2: forgiveness when aiming at the dummy
    private bool level2Active;                                 // LEVEL2: set true at the end of EnterLevel2()
    private int level2Hits;                                    // LEVEL2

    [Header("IMU aiming")]
    [Tooltip("Cone (degrees) around the dummy centre that counts as a hit for IMU casts. Lower it as calibration improves.")]
    [SerializeField] private float imuAimToleranceDeg = 8f;
    private int level2Casts; 

    [Tooltip("Level 2: also allow left click to cast. Turn off once IMU gestures are the only way to cast.")]
    [SerializeField] private bool allowMouseClickCast = true;
    
    // Desktop uses the mouse reticle; Quest aims along the tracked head direction.
    Ray GetCrosshairRay()
    {
    if (XRSettings.isDeviceActive && viewCamera != null)
        return new Ray(viewCamera.transform.position, viewCamera.transform.forward);
    var vp = new Vector3(aimViewport.x, aimViewport.y, 0f);
    return viewCamera != null ? viewCamera.ViewportPointToRay(vp) : new Ray(wand.position, wand.forward);
    }

    void Start()
    {
        crystalHome = new Vector3[targets.Length];
        broken = new bool[targets.Length];
        for (var i = 0; i < targets.Length; ++i) crystalHome[i] = targets[i].localPosition;
        SetWandStyle(-1);
        viewCamera = Camera.main;
        ConfigureReadableHud();
        StartCoroutine(AnchorLobbyHudAfterTracking());
        wandRest = wand != null ? wand.localRotation : Quaternion.identity;
        Application.runInBackground = true;
        if (aimMarker != null) aimMarker.gameObject.SetActive(false);
        if (crosshair != null) crosshair.gameObject.SetActive(false);
        UpdateLabels("CHOOSE A WAND", "");
        if (scoreText != null)
        {
            scoreText.text = "0 / " + targets.Length + " CRYSTALS";
            scoreText.gameObject.SetActive(false);
        }
        try
        {
            receiver = new UdpClient(udpPort);
            receiver.Client.ReceiveTimeout = 250;
            listening = true;
            reader = new Thread(ReadPackets) { IsBackground = true, Name = "Wand spell UDP" };
            reader.Start();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Wand UDP input unavailable: " + exception.Message);
        }
    }

    void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        listening = false;
        receiver?.Close();
        if (hudPanelMaterial != null) Destroy(hudPanelMaterial);
        if (hudRoot != null) Destroy(hudRoot.gameObject);
        if (reader != null && reader.IsAlive) reader.Join(300);
    }

    void ReadPackets()
    {
        var endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (listening)
        {
            try
            {
                var bytes = receiver.Receive(ref endpoint);
                var json = Encoding.UTF8.GetString(bytes);
                lock (incoming)
                {
                    if (incoming.Count < 32) incoming.Enqueue(new IncomingPacket {
                        json = json,
                        endpoint = new IPEndPoint(endpoint.Address, endpoint.Port) });
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { break; }
        }
    }

    void Update() {
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        bool rightPressed, leftPressed;
        right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out rightPressed);
        left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out leftPressed);
        bool pressed = rightPressed || leftPressed;
        if (pressed && !recenterButtonDown) RecenterWand();
        recenterButtonDown = pressed;
        // The physical wand is the spell input. Head gaze lets Quest users
        // choose a cosmetic wand without a keyboard or a spare controller.
        if (XRSettings.isDeviceActive && wandStyle < 0 && viewCamera != null) {
            int choice = -1;
            RaycastHit hit;
            if (Physics.Raycast(new Ray(viewCamera.transform.position, viewCamera.transform.forward), out hit, 20f))
                for (int i = 0; wandDisplays != null && i < wandDisplays.Length; i++)
                    if (hit.collider.transform == wandDisplays[i]) { choice = i; break; }
            if (choice != gazeChoice) { gazeChoice = choice; gazeStarted = Time.time; }
            if (choiceText != null)
                choiceText.text = choice < 0 ? "LOOK AT A WAND TO SELECT IT" : "KEEP LOOKING TO SELECT WAND " + (choice + 1);
            if (choice >= 0 && Time.time - gazeStarted >= 1.2f) SetWandStyle(choice);
        }
        for (var i = 0; i < targets.Length; ++i) {
            if (broken[i]) continue;
            targets[i].Rotate(Vector3.up, Time.deltaTime * (24f + i * 7f), Space.World);
            targets[i].localPosition = crystalHome[i] +
                Vector3.up * (0.035f * Mathf.Sin(Time.time * 1.8f + i * 1.9f));
            targets[i].localScale = Vector3.one * (i == selected ?
                .66f + .035f * Mathf.Sin(Time.time * 5f) : .60f);
        }
        if (lavaRenderer != null)
            lavaRenderer.material.mainTextureOffset = new Vector2(Time.time * .025f, Time.time * .013f);
        var keyboard = Keyboard.current;
        if (keyboard != null) {
            if (wandStyle < 0) {
                if (keyboard.digit1Key.wasPressedThisFrame) SetWandStyle(0);
                if (keyboard.digit2Key.wasPressedThisFrame) SetWandStyle(1);
                if (keyboard.digit3Key.wasPressedThisFrame) SetWandStyle(2);
            }
            if (keyboard.rKey.wasPressedThisFrame) {
                RecenterWand();
                aimViewport = new Vector2(.5f, .5f);
            }
        }
        var mouse = Mouse.current;
        if (wandStyle < 0 && mouse != null && mouse.leftButton.wasPressedThisFrame && viewCamera != null) {
            RaycastHit displayHit;
            if (Physics.Raycast(viewCamera.ScreenPointToRay(mouse.position.ReadValue()), out displayHit, 20f))
                for (int i = 0; wandDisplays != null && i < wandDisplays.Length; i++)
                    if (displayHit.collider.transform == wandDisplays[i]) { SetWandStyle(i); break; }
        }
        if (!XRSettings.isDeviceActive && wandStyle >= 0 && arenaReady && mouse != null && viewCamera != null) {
            if (Cursor.lockState == CursorLockMode.Locked) {
                if (skipMouseDelta) skipMouseDelta = false;
                else aimViewport = AdvanceAim(aimViewport, mouse.delta.ReadValue());
            } else
                Cursor.visible = true;
            var ray = viewCamera.ViewportPointToRay(new Vector3(aimViewport.x, aimViewport.y, 0f));
            if (crosshair != null) {
                var depth = 1.2f;
                var halfHeight = Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * .5f) * depth;
                crosshair.localPosition = new Vector3((aimViewport.x - .5f) * 2f * halfHeight * viewCamera.aspect,
                    (aimViewport.y - .5f) * 2f * halfHeight, depth);
            }
            var desiredPosition = new Vector3(.27f + (aimViewport.x - .5f) * 1.1f,
                -.29f + (aimViewport.y - .5f) * .68f, .48f);
            wand.localPosition = Vector3.Lerp(wand.localPosition, desiredPosition, Time.deltaTime * 12f);
            var aimPoint = ray.GetPoint(6f);
            Vector3 hitPoint;
            // LEVEL2: in level 2 the aim target is the dummy, not the crystals (never touches targets[selected])
            bool hitTarget = level2Active ? TryGetAimedDummy(ray, out hitPoint) : TryGetAimedCrystal(ray, out hitPoint);
            if (hitTarget) aimPoint = hitPoint;
            var localDirection = wand.parent.InverseTransformDirection((aimPoint - wand.position).normalized);
            var desiredRotation = Quaternion.LookRotation(localDirection, Vector3.up);
            wand.localRotation = Quaternion.Slerp(wand.localRotation, desiredRotation, Time.deltaTime * 16f);
            if (aimMarker != null) {
                aimMarker.gameObject.SetActive(hitTarget);
                if (hitTarget) aimMarker.position = level2Active ? hitPoint : targets[selected].position;   // LEVEL2
            }
            if (mouse.leftButton.wasPressedThisFrame && !busy) {
                if (Cursor.lockState != CursorLockMode.Locked) {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    skipMouseDelta = true;
                }
                if (level2Active) {                                                                          // LEVEL2
                    CastSpellAtDummy(SpellNames[level2Casts++ % SpellNames.Length], 1f, "MOUSE AIM", ray, false);
                } else if (selected < targets.Length) {
                    // Mouse testing casts the spell configured on the active crystal.                       // REMOTE
                    var crystalTarget = targets[selected].GetComponent<CrystalTarget>();
                    if (crystalTarget != null)
                        CastSpell(crystalTarget.requiredSpell.ToString().ToLowerInvariant(), 1f, "MOUSE AIM");
                    else
                        UpdateLabels("CRYSTAL SETUP MISSING", "ADD CRYSTALTARGET COMPONENT");
                }
            }
        }
 
        if (XRSettings.isDeviceActive && wandStyle >= 0 && arenaReady && viewCamera != null)
            UpdateHeadAim();

        while (true) {
            IncomingPacket message;
            lock (incoming) {
                if (incoming.Count == 0) break;
                message = incoming.Dequeue();
            }
            try {
                var packet = JsonUtility.FromJson<SensorPacket>(message.json);
                if (packet == null) continue;
                if (packet.mode == "duel" || packet.mode == "single")
                    GameModeRequested?.Invoke(packet.mode);
                if (packet.player == 1 || packet.player == 2) {
                    int playerIndex = packet.player - 1;
                    if (packet.type == "pose") {
                        if (string.IsNullOrEmpty(packet.session) || packet.q == null || packet.q.Length != 4)
                            continue;
                        if (duelSessions[playerIndex] != packet.session) {
                            duelSessions[playerIndex] = packet.session;
                            duelLastCastIds[playerIndex] = 0;
                            duelLastPoseIds[playerIndex] = 0;
                            duelPoseOrigins[playerIndex] = int.MinValue;
                        }
                        if (packet.origin != duelPoseOrigins[playerIndex]) {
                            duelPoseOrigins[playerIndex] = packet.origin;
                            duelLastPoseIds[playerIndex] = 0;
                        }
                        if (packet.pose_id <= duelLastPoseIds[playerIndex]) continue;
                        duelLastPoseIds[playerIndex] = packet.pose_id;
                        var pose = new Quaternion(packet.q[1], packet.q[2], packet.q[3], packet.q[0]);
                        float norm = Mathf.Sqrt(pose.x * pose.x + pose.y * pose.y + pose.z * pose.z + pose.w * pose.w);
                        if (norm > 0.1f) DuelPoseReceived?.Invoke(packet.player, pose.normalized);
                        continue;
                    }
                    if (packet.type == "cast") {
                        if (string.IsNullOrEmpty(packet.session) || packet.id <= 0) continue;
                        if (duelSessions[playerIndex] != packet.session) {
                            duelSessions[playerIndex] = packet.session;
                            duelLastCastIds[playerIndex] = 0;
                            duelLastPoseIds[playerIndex] = 0;
                            duelPoseOrigins[playerIndex] = int.MinValue;
                        }
                        if (packet.id <= duelLastCastIds[playerIndex]) {
                            SendReply(message.endpoint, packet, "duplicate");
                            continue;
                        }
                        duelLastCastIds[playerIndex] = packet.id;
                        DuelCastReceived?.Invoke(packet.player, packet.spell, packet.conf);
                        SendReply(message.endpoint, packet, "cast");
                        continue;
                    }
                }
                if (packet.type == "ping") {
                    SendReply(message.endpoint, packet, packet.mode == "duel" ? "duel" :
                        packet.mode == "single" ? "single" :
                        arenaReady ? "ready" : "choose_wand");
                    continue;
                }
                if (packet.type == "pose") {
                    ApplyPose(packet);
                    continue; // Pose updates are frequent; heartbeat reports reception.
                }
                // Bridge casts have sequence numbers; old manual packets remain
                // supported. Never cast a replayed datagram twice.
                if (packet.type == "cast") {
                    if (string.IsNullOrEmpty(packet.session) || packet.id <= 0) continue;
                    if (activeSession != packet.session) { activeSession = packet.session; lastPacketId = 0; }
                    if (packet.id <= lastPacketId) { SendReply(message.endpoint, packet, "duplicate"); continue; }
                    lastPacketId = packet.id;
                }
                if (packet.q != null && packet.q.Length == 4) {
                    var rotation = new Quaternion(packet.q[1], packet.q[2], packet.q[3], packet.q[0]);
                    var lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y +
                        rotation.z * rotation.z + rotation.w * rotation.w;
                    if (!level2Active && !hasPose && lengthSquared > 0.1f) wand.localRotation = recenter * rotation.normalized;   // LEVEL2
                }
                // The classifier selects the spell; the mouse/head crosshair selects its direction.
                bool cast = !string.IsNullOrEmpty(packet.spell) &&
                    (level2Active
                        ? CastSpellAtDummy(packet.spell, packet.conf, "IMU / UDP", GetCrosshairRay(), false)
                        : CastSpell(packet.spell, packet.conf, "IMU / UDP"));
                SendReply(message.endpoint, packet, cast ? "cast" : "ignored_not_ready_busy_or_invalid");
            }
            catch (Exception exception) {
                Debug.LogWarning("Invalid wand packet: " + exception.Message);
            }
        }
        RefreshGestureHint();
    }

    void ConfigureReadableHud()
    {
        if (viewCamera == null) return;
        hudRoot = new GameObject("Stationary room instruction board").transform;
        AnchorHudInRoom();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        PlaceHudText(statusText, font, new Vector3(0f, .16f, 0f), .028f);
        PlaceHudText(scoreText, font, new Vector3(0f, .63f, 0f), .027f);
        PlaceHudText(choiceText, font, new Vector3(0f, -.28f, 0f), .028f);
        var hint = new GameObject("Current target - spell gesture instructions");
        gestureText = hint.AddComponent<TextMesh>();
        PlaceHudText(gestureText, font, new Vector3(0f, -.48f, 0f), .032f);
        gestureText.gameObject.SetActive(false);
        var panelTemplate = Resources.Load<Material>("WandHudBackground");
        hudPanelMaterial = panelTemplate != null ? new Material(panelTemplate) : new Material(Shader.Find("Unlit/Color"));
        hudPanelMaterial.color = new Color(.025f, .035f, .055f);
        CreateHudPanel("Stationary instruction board background", new Vector3(0f, 0f, .03f), new Vector3(3.2f, 1.65f, 1f));
    }

    IEnumerator AnchorLobbyHudAfterTracking()
    {
        yield return new WaitForSeconds(.5f);
        if (wandStyle < 0) AnchorHudInRoom();
    }

    void AnchorHudInRoom()
    {
        if (hudRoot == null || viewCamera == null) return;
        var forward = Vector3.ProjectOnPlane(sceneRig != null ? sceneRig.forward : viewCamera.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
        var right = Vector3.Cross(Vector3.up, forward);
        var offset = forward * 3.2f - right * 2.8f;
        hudRoot.position = viewCamera.transform.position + offset;
        hudRoot.rotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
        // Placed once per room, never parented to or moved with the head camera.
    }

    void PlaceHudText(TextMesh text, Font font, Vector3 position, float size)
    {
        if (text == null) return;
        text.transform.SetParent(hudRoot, false);
        text.transform.localPosition = position;
        text.transform.localRotation = Quaternion.identity;
        text.transform.localScale = Vector3.one;
        text.font = font;
        text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        text.fontSize = 64;
        text.characterSize = size;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.lineSpacing = 1.15f;
        text.color = Color.white;
    }

    GameObject CreateHudPanel(string label, Vector3 position, Vector3 scale)
    {
        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = label;
        panel.transform.SetParent(hudRoot, false);
        panel.transform.localPosition = position;
        panel.transform.localScale = scale;
        Destroy(panel.GetComponent<Collider>());
        panel.GetComponent<Renderer>().sharedMaterial = hudPanelMaterial;
        return panel;
    }

    static string GestureInstruction(string spell)
    {
        switch (spell == null ? "" : spell.ToLowerInvariant()) {
            case "fire": return "FIRE: SWIPE DOWN\nOne stroke: top to bottom";
            case "water": return "WATER: CLOCKWISE CIRCLE\nOne full circle, then pause";
            case "wind": return "WIND: SWIPE RIGHT\nOne stroke: left to right";
            case "lightning": return "LIGHTNING: DRAW A Z\nRight, down-left, then right";
            default: return "MATCH THE TARGET'S COLOUR\nPause briefly between gestures";
        }
    }

    void RefreshGestureHint()
    {
        if (gestureText == null) return;
        gestureText.gameObject.SetActive(wandStyle >= 0);
        string instruction;
        if (!arenaReady) instruction = "GET READY\nEntering the next arena";
        else if (level2Active && dummyReaction != null)
            instruction = dummyReaction.IsDown ? "TARGET RECOVERING\nWait for its next crystal" : GestureInstruction(dummyReaction.RequiredSpell);
        else if (targets != null && selected < targets.Length) {
            var target = targets[selected].GetComponent<CrystalTarget>();
            instruction = GestureInstruction(target != null ? target.requiredSpell.ToString() : null);
        } else instruction = GestureInstruction(null);
        if (gestureText.text != instruction) gestureText.text = instruction;
    }

    void UpdateHeadAim()
    {
        aimViewport = new Vector2(.5f, .5f);
        if (crosshair != null) {
            crosshair.localPosition = new Vector3(0f, 0f, 1.2f);
            crosshair.gameObject.SetActive(true);
        }
        var ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        Vector3 point;
        bool hit = level2Active ? TryGetAimedDummy(ray, out point) : TryGetAimedCrystal(ray, out point);
        if (aimMarker != null) {
            aimMarker.gameObject.SetActive(hit);
            if (hit) aimMarker.position = point;
        }
    }

    void ApplyPose(SensorPacket packet)
    {
        if ((level2Active && !XRSettings.isDeviceActive) || !arenaReady || wand == null || string.IsNullOrEmpty(packet.session) ||
            packet.pose_id <= 0 || packet.q == null || packet.q.Length != 4) return;
        var rotation = new Quaternion(packet.q[1], packet.q[2], packet.q[3], packet.q[0]);
        foreach (var value in packet.q)
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
        if (Quaternion.Dot(rotation, rotation) < .5f || Quaternion.Dot(rotation, rotation) > 1.5f) return;
        rotation = rotation.normalized;
        bool newStream = !hasPose || packet.session != poseSession;
        if (!newStream && packet.pose_id <= lastPoseId) return;
        latestPose = rotation;
        if (newStream || packet.origin != poseOrigin) {
            poseAnchor = viewCamera != null ? viewCamera.transform.rotation : Quaternion.identity;
            poseReference = Quaternion.Inverse(rotation);
        }
        poseSession = packet.session;
        poseOrigin = packet.origin;
        lastPoseId = packet.pose_id;
        poseCount++;
        hasPose = true;
        lastPoseAt = Time.unscaledTime;
        poseTarget = poseAnchor * poseReference * rotation * wandRest;
    }

    void RecenterWand() {
        if (!hasPose) return;
        poseAnchor = viewCamera != null ? viewCamera.transform.rotation : Quaternion.identity;
        poseReference = Quaternion.Inverse(latestPose);
        poseTarget = poseAnchor * wandRest;
    }

    void LateUpdate()
    {
        // Freeze the last orientation on link loss, rather than replaying motion.
        // World rotation keeps a head turn from also rotating the physical wand.
        if ((!level2Active || XRSettings.isDeviceActive) && hasPose && wand != null && arenaReady && Time.unscaledTime - lastPoseAt < .5f)
            wand.rotation = Quaternion.Slerp(wand.rotation, poseTarget,
                1f - Mathf.Exp(-35f * Time.unscaledDeltaTime));
    }

    void SendReply(IPEndPoint endpoint, SensorPacket packet, string status) {
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Reply {
            session = packet.session,
            id = packet.id,
            status = status,
            pose_count = poseCount,
            pose_angle = hasPose ? Quaternion.Angle(poseAnchor * wandRest, poseTarget) : 0f
        }));
        try { receiver.Send(bytes, bytes.Length, endpoint); }
        catch (SocketException exception) { Debug.LogWarning("Wand reply failed: " + exception.Message); }
        if (packet.id > 0) Debug.Log("WAND_BRIDGE " + packet.id + " " + status + " " + packet.spell);
    }

    static Vector2 AdvanceAim(Vector2 current, Vector2 mouseDelta) {
        const float sensitivity = .0015f;
        return new Vector2(Mathf.Clamp(current.x + mouseDelta.x * sensitivity, .07f, .93f),
            Mathf.Clamp(current.y + mouseDelta.y * sensitivity, .12f, .88f));
    }

    bool TryGetAimedCrystal(Ray ray, out Vector3 point) {
        point = Vector3.zero;
        RaycastHit hit;
        Physics.SyncTransforms();
        if (targets == null || selected >= targets.Length || !Physics.Raycast(ray, out hit, 30f) ||
            !hit.collider.transform.IsChildOf(targets[selected])) return false;
        point = hit.point;
        return true;
    }

    void SetWandStyle(int style) {
        wandStyle = style;
        if (choiceText != null) choiceText.gameObject.SetActive(style < 0);
        if (wandStyles != null)
            for (int i = 0; i < wandStyles.Length; i++)
                if (wandStyles[i] != null) wandStyles[i].gameObject.SetActive(i == style);
        if (style >= 0) {
            UpdateLabels("THE GATE AWAKENS", "ENTERING THE ARENA");
            if (sceneRig != null) StartCoroutine(EnterArena());
            else arenaReady = true;
        }
    }

    IEnumerator EnterArena() {
        arenaReady = false;
        var from = sceneRig.position;
        var to = new Vector3(0f, 0f, -3.25f);
        var doorLeft = entranceDoors != null && entranceDoors.Length > 0 ? entranceDoors[0] : null;
        var doorRight = entranceDoors != null && entranceDoors.Length > 1 ? entranceDoors[1] : null;
        var leftHome = doorLeft != null ? doorLeft.localPosition : Vector3.zero;
        var rightHome = doorRight != null ? doorRight.localPosition : Vector3.zero;
        float elapsed = 0f;
        while (elapsed < 2.7f) {
            elapsed += Time.deltaTime;
            float gate = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .85f));
            if (doorLeft != null) doorLeft.localPosition = leftHome + Vector3.left * gate * 1.7f;
            if (doorRight != null) doorRight.localPosition = rightHome + Vector3.right * gate * 1.7f;
            float travel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - .45f) / 2.2f));
            if (!XRSettings.isDeviceActive) sceneRig.position = Vector3.Lerp(from, to, travel);
            yield return null;
        }
        sceneRig.position = to;
        AnchorHudInRoom();
        arenaReady = true;
        aimViewport = new Vector2(.5f, .5f);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        skipMouseDelta = true;
        if (crosshair != null) crosshair.gameObject.SetActive(true);
        if (scoreText != null) scoreText.gameObject.SetActive(true);
        UpdateLabels("CRYSTAL 1 IS ACTIVE", InputHint());
    }

    string InputHint() => XRSettings.isDeviceActive
        ? "LOOK TO AIM / MATCH COLOUR / GESTURE TO CAST"
        : "MOVE MOUSE TO AIM  /  LEFT CLICK TO FIRE";

    public bool CastSpell(string spell, float confidence, string source) {
        if (string.IsNullOrWhiteSpace(spell) || float.IsNaN(confidence) || confidence < .55f || confidence > 1f ||
            wandStyle < 0 || !arenaReady || busy || Time.time - lastSpellTime < 0.25f ||
            targets == null || selected >= targets.Length) return false;
        spell = spell.Trim().ToLowerInvariant();
        int spellIndex = Array.IndexOf(SpellNames, spell);
        if (spellIndex < 0) return false;
        // Snapshot the crosshair at cast time; neither a miss nor a wrong target
        // can be corrected by looking at the active crystal after firing.
        var ray = GetCrosshairRay();
        Vector3 destination;
        bool aimedAtCrystal = TryGetAimedCrystal(ray, out destination);
        if (!aimedAtCrystal) {
            RaycastHit impact;
            destination = Physics.Raycast(ray, out impact, 30f) ? impact.point : ray.GetPoint(30f);
        }
        lastSpellTime = Time.time;
        Debug.Log("WAND_CRYSTAL_AIM hit=" + aimedAtCrystal + " crystal=" + (selected + 1) + " spell=" + spell);
        StartCoroutine(CastAndShatter(spellIndex, confidence, source, destination, aimedAtCrystal));
        return true;
    }

    IEnumerator CastAndShatter(int spellIndex, float confidence, string source, Vector3 destination, bool aimedAtCrystal) {
        busy = true;
        int index = selected;
        var color = SpellColors[spellIndex];
        UpdateLabels(SpellNames[spellIndex].ToUpperInvariant() + (aimedAtCrystal ? "  /  CRYSTAL " + (index + 1) : "  /  MISSED"),
            source + "  " + Mathf.Clamp01(confidence).ToString("P0"));
        yield return StartCoroutine(SpellEffect(SpellNames[spellIndex], color, destination));
        if (!aimedAtCrystal) {
            UpdateLabels("MISSED / NO POINT", "AIM AT THE GLOWING CRYSTAL");
            busy = false;
            yield break;
        }
        // Only the currently selected crystal can shatter, and only with its configured spell.
        var crystalTarget = targets[index].GetComponent<CrystalTarget>();
        if (crystalTarget == null) {
            Debug.LogWarning("Crystal " + (index + 1) + " is missing CrystalTarget.");
            UpdateLabels("CRYSTAL SETUP MISSING", "ADD CRYSTALTARGET TO ACTIVE CRYSTAL");
            busy = false;
            yield break;
        }
        if (!crystalTarget.CanShatter(SpellNames[spellIndex])) {
            UpdateLabels("WRONG SPELL FOR CRYSTAL " + (index + 1),
                "REQUIRES " + crystalTarget.requiredSpell.ToString().ToUpperInvariant());
            busy = false;
            yield break;
        }
        yield return StartCoroutine(Shatter(index, color));
        score++; selected++;
        if (scoreText != null) scoreText.text = score + " / " + targets.Length + " CRYSTALS";
        if (selected >= targets.Length) {
            UpdateLabels("SANCTUM CLEARED", "THE GATE TO LEVEL 2 OPENS");
            // if (sanctuaryKeystone != null) sanctuaryKeystone.SetActive(false);
            yield return new WaitForSeconds(2.0f);                       // let the message be read; shorten if you like
            if (sceneRig != null) yield return StartCoroutine(EnterLevel2());   // wait until the move finishes
            UpdateLabels("LEVEL 2", InputHint());
            busy = false;
            yield break;                                                 // skip the "CRYSTAL n IS ACTIVE" label below
        }
        UpdateLabels("CRYSTAL " + (selected + 1) + " IS ACTIVE", InputHint());
        busy = false;
    }

    IEnumerator SpellEffect(string spell, Color color, Vector3 destination) {
        var start = wandTip.position;
        if (spell == "lightning") {
            // Original Storm effect: nine points, one fixed random zigzag.
            var bolt = new GameObject("Lightning spell - temporary storm arc");
            var line = bolt.AddComponent<LineRenderer>();
            line.positionCount = 9;
            line.startWidth = 0.055f;
            line.endWidth = 0.015f;
            var material = Glow(color);
            line.material = material;
            // Screen-vertical axis, perpendicular to the bolt's travel direction.
            Vector3 travel = (destination - start).normalized;
            Vector3 cameraUp = viewCamera != null ? viewCamera.transform.up : Vector3.up;
            Vector3 vertical = Vector3.ProjectOnPlane(cameraUp, travel).normalized;
            if (vertical.sqrMagnitude < 0.001f)
                vertical = Vector3.ProjectOnPlane(Vector3.right, travel).normalized;
            for (var i = 0; i < 9; ++i) {
                float t = i / 8f;
                // Alternate sides to create obvious, wide zigzags.
                float displacement = (i == 0 || i == 8) ? 0f :
                    (i % 2 == 0 ? 1f : -1f) * UnityEngine.Random.Range(.17f, .25f);
                line.SetPosition(i, Vector3.Lerp(start, destination, t) + vertical * displacement);
            }
            Destroy(bolt, 0.28f);
            Destroy(material, 0.28f);
            yield return new WaitForSeconds(.24f);
            yield break;
        }

        // Build an orthonormal frame around the direction of travel.
        // Water uses a screen-vertical axis; wind uses both axes (3D helix).
        var direction = (destination - start).normalized;
        if (direction.sqrMagnitude < .0001f) yield break;
        var reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .95f
            ? Vector3.right : Vector3.up;
        var right = Vector3.Cross(direction, reference).normalized;
        var up = Vector3.Cross(right, direction).normalized;
        // Align water up-and-down in the player's view.
        var cameraUpAxis = viewCamera != null ? viewCamera.transform.up : Vector3.up;
        var waterVertical = Vector3.ProjectOnPlane(cameraUpAxis, direction).normalized;
        if (waterVertical.sqrMagnitude < .001f) waterVertical = up;

        const int points = 32;
        var root = new GameObject(spell + " spell - temporary lines");
        int lineCount = spell == "water" ? 1 : spell == "wind" ? 2 : 3;
        var lines = new LineRenderer[lineCount];
        var materials = new Material[lineCount];
        for (int j = 0; j < lineCount; ++j) {
            var child = new GameObject("Ribbon " + (j + 1));
            child.transform.SetParent(root.transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.textureMode = LineTextureMode.Stretch;
            Color ribbonColor = color;
            if (spell == "fire")
                ribbonColor = j == 0 ? new Color(1f, .20f, .02f) :
                    j == 1 ? new Color(1f, .10f, .01f) : new Color(.90f, .02f, .01f);
            else if (spell == "wind" && j == 1)
                ribbonColor = new Color(.02f, .85f, .08f);
            materials[j] = Glow(ribbonColor);
            line.material = materials[j];
            line.startWidth = spell == "fire" ? (j == 0 ? .075f : .038f) :
                spell == "water" ? .045f : .024f;
            line.endWidth = spell == "water" ? .022f : .005f;
            if (spell == "fire") {
                // Thick base, tapering to sharp flame tips.
                line.widthCurve = new AnimationCurve(
                    new Keyframe(0f, .15f), new Keyframe(.18f, 1f),
                    new Keyframe(.55f, .85f), new Keyframe(.82f, .50f),
                    new Keyframe(1f, 0f));
                line.widthMultiplier = j == 0 ? .105f : .065f;
            }
            lines[j] = line;
        }

        // Short-lived animated beams; no prefab or separate VFX asset needed.
        float elapsed = 0f;
        const float duration = .38f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float time = elapsed * 12f;
            for (int j = 0; j < lineCount; ++j) {
                for (int i = 0; i < points; ++i) {
                    float t = i / (float)(points - 1);
                    var center = Vector3.Lerp(start, destination, t);
                    // Both ends stay attached to the wand and crystal.
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    Vector3 offset;
                    if (spell == "water") {
                        // Two smooth cycles in one plane; gently travels forward.
                        offset = waterVertical * (Mathf.Sin(t * Mathf.PI * 4f - time * 1.3f)
                            * .22f * envelope);
                    } else if (spell == "wind") {
                        // Two intertwined helices around an imaginary tube.
                        float angle = t * Mathf.PI * 6f - time * 1.7f + j * Mathf.PI;
                        float radius = .085f * envelope;
                        offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                    } else // fire
                      {
                        // Three tapered, uneven flame ribbons with upward tongues.
                        float phase = j * 1.7f;
                        float wave = Mathf.Sin(t * Mathf.PI * (3.5f + j * .5f) - time * 2f + phase);
                        float flicker = Mathf.Sin(t * 35f - time * 3.2f + phase) * .025f;
                        float spread = j == 0 ? 0f : j == 1 ? .085f : -.07f;
                        offset = up * (envelope * (wave * .065f + flicker + spread)) +
                            right * (envelope * Mathf.Sin(t * 12f + time + phase) * .025f);
                    }
                    lines[j].SetPosition(i, center + offset);
                }
            }
            yield return null;
        }
        Destroy(root);
        foreach (var material in materials) Destroy(material);
    }

    IEnumerator Shatter(int index, Color color) {
        var crystal = targets[index];
        var impactPoint = crystal.position;
        var mesh = targetCrystals[index].GetComponent<MeshFilter>().sharedMesh;
        var pieces = new List<Transform>();
        var directions = new List<Vector3>();
        for (int i = 0; i < 20; i++) {
            var piece = new GameObject("Flying crystal shard");
            piece.transform.position = crystal.position + UnityEngine.Random.insideUnitSphere * .13f;
            piece.transform.rotation = UnityEngine.Random.rotation;
            piece.transform.localScale = Vector3.one * UnityEngine.Random.Range(.11f, .24f);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            piece.AddComponent<MeshRenderer>().material = Glow(color);
            pieces.Add(piece.transform);
            directions.Add((UnityEngine.Random.onUnitSphere + Vector3.up * .4f).normalized);
        }
        var burst = new GameObject("Crystal impact particles");
        burst.transform.position = crystal.position;
        var particles = burst.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.duration = .35f; main.startLifetime = .8f; main.startSpeed = 2.6f;
        main.startSize = .065f; main.startColor = color; main.loop = false; main.maxParticles = 100;
        var emission = particles.emission; emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 65) });
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .1f;
        particles.Play();
        var impact = new GameObject("Expanding impact halo");
        var halo = impact.AddComponent<LineRenderer>();
        halo.positionCount = 48;
        halo.loop = true;
        halo.startWidth = .05f;
        halo.endWidth = .05f;
        halo.material = Glow(color);
        var impactLight = impact.AddComponent<Light>();
        impactLight.type = LightType.Point;
        impactLight.color = color;
        impactLight.range = 4f;
        impactLight.intensity = 5f;
        broken[index] = true;
        crystal.gameObject.SetActive(false);
        float elapsed = 0f;
        while (elapsed < .8f) {
            var dt = Time.deltaTime; elapsed += dt;
            float radius = .18f + elapsed * 1.35f;
            var right = viewCamera != null ? viewCamera.transform.right : Vector3.right;
            var up = viewCamera != null ? viewCamera.transform.up : Vector3.up;
            for (int j = 0; j < 48; j++) {
                float angle = j * Mathf.PI * 2f / 48f;
                halo.SetPosition(j, impactPoint +
                    (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
            }
            halo.startWidth = halo.endWidth = Mathf.Max(0f, .05f * (1f - elapsed / .8f));
            impactLight.intensity = 5f * (1f - elapsed / .8f);
            for (int i = 0; i < pieces.Count; i++) {
                pieces[i].position += (directions[i] * 2f + Vector3.down * elapsed * 1.5f) * dt;
                pieces[i].Rotate(90f * dt, 120f * dt, 70f * dt);
                pieces[i].localScale *= 1f - dt * 1.1f;
            }
            yield return null;
        }
        foreach (var piece in pieces) Destroy(piece.gameObject);
        Destroy(impact);
        Destroy(burst, 1f);
    }

    static Material Glow(Color color) {
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 2f);
        return material;
    }

    void UpdateLabels(string headline, string subline) {
        if (statusText != null) statusText.text = WrapHudLine(headline) + "\n" + WrapHudLine(subline);
    }

    static string WrapHudLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        var result = new StringBuilder();
        int length = 0;
        foreach (var word in line.Split(' ')) {
            if (length > 0 && length + 1 + word.Length > 28) { result.Append('\n'); length = 0; }
            if (length > 0) { result.Append(' '); length++; }
            result.Append(word); length += word.Length;
        }
        return result.ToString();
    }

    // Implementation for Dummy Training Scene - Level 2
    IEnumerator EnterLevel2()
    {
    arenaReady = false;                                     // pause aiming/casting during the move
    level2Active = false;
    var from = sceneRig.position;
    var to = from + new Vector3(0f, 0f, 15.0f);             // relative, so it works wherever the rig ended up

    foreach (var go in GameObject.FindGameObjectsWithTag("Level1Dynamic"))
        go.SetActive(false);
    if (runicAlcoveShadow != null && whiteMaterial != null)
        runicAlcoveShadow.sharedMaterial = whiteMaterial;   // Obsidian -> White

    float elapsed = 0f;
    while (elapsed < 3.0f)
    {
        elapsed += Time.deltaTime;
        float travel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 3.0f));
        if (!XRSettings.isDeviceActive) sceneRig.position = Vector3.Lerp(from, to, travel);
        yield return null;
    }
    sceneRig.position = to;                                // VR: snaps here, same as EnterArena
    AnchorHudInRoom();
    
    // ---- same "start playing" setup that EnterArena does ----
    level2Active = true;                                   // switches Update() from crystals to the dummy
    level2Hits = 0;
    arenaReady = true;
    aimViewport = new Vector2(.5f, .5f);                   // recentre the crosshair after the move
    Cursor.lockState = CursorLockMode.Locked;              // re-lock in case Esc/alt-tab freed it
    Cursor.visible = false;
    skipMouseDelta = true;                                 // ignore the first mouse delta after re-locking
    if (crosshair != null) crosshair.gameObject.SetActive(true);
    if (scoreText != null)
    {
        scoreText.gameObject.SetActive(true);
        scoreText.text = "0 HITS";                           // the old "N / N CRYSTALS" text no longer applies
    }
    if (aimMarker != null) aimMarker.gameObject.SetActive(false);
    
    // start the dummy(s) sliding
    foreach (var dummy in GameObject.FindGameObjectsWithTag("Level2Dummy"))
    {
        var slider = dummy.GetComponent<SlidingDummy>();
        if (slider != null) slider.enabled = true;
    }
    
    UpdateLabels("LEVEL 2", InputHint());
    // level2Move = null;
 }

    bool TryGetAimedDummy(Ray ray, out Vector3 point)
    {
    point = Vector3.zero;
    if (dummyReaction == null) return false;
    RaycastHit hit;
    
    if (Physics.SphereCast(ray, dummyAimRadius, out hit, 40f) &&
        hit.collider.transform.IsChildOf(dummyReaction.transform))
    {
        // Debug.Log($"[aim] ray hit {hit.collider.name}, child of dummy = {hit.collider.transform.IsChildOf(dummyReaction.transform)}");
        point = hit.point;
        return true;
    }
    return false;
    }
    Ray GetImuAimRay()
    {
    var origin = viewCamera != null ? viewCamera.transform.position : wand.position;
    bool poseLive = hasPose && Time.unscaledTime - lastPoseAt < .5f;
    var direction = poseLive ? poseTarget * Vector3.forward : wand.forward;
    return new Ray(origin, direction);
    }
    
    Vector3 DummyCenter()
    {
    var col = dummyReaction.GetComponentInChildren<Collider>();
    return col != null ? col.bounds.center : dummyReaction.transform.position;
    }
    
    // Hit test: physical SphereCast, OR (for IMU casts) within an angular cone of the dummy centre.
    bool IsAimedAtDummy(Ray ray, bool useCone, out Vector3 point)
    {
    if (TryGetAimedDummy(ray, out point)) return true;
    if (useCone && dummyReaction != null)
    {
        var toDummy = DummyCenter() - ray.origin;
        if (toDummy.sqrMagnitude > .01f && Vector3.Angle(ray.direction, toDummy) <= imuAimToleranceDeg)
        {
        point = DummyCenter();
        return true;
        }
    }
    return false;
    }
    
    static int SpellIndexFromName(string spell)
    {
        if (string.IsNullOrWhiteSpace(spell)) return -1;
        return Array.IndexOf(SpellNames, spell.Trim().ToLowerInvariant());   // fire, water, wind, lightning
    }
    
    // Always plays the spell. Only a real hit triggers the dummy's isHit animation.
    bool CastSpellAtDummy(string spell, float confidence, string source, Ray aimRay, bool useCone)
    {
    if (!arenaReady || busy || dummyReaction == null || wandStyle < 0 ||
        Time.time - lastSpellTime < 0.25f ||
        float.IsNaN(confidence) || confidence < .55f || confidence > 1f) return false;
    int spellIndex = SpellIndexFromName(spell);
    if (spellIndex < 0) return false;
    lastSpellTime = Time.time;
    
    Vector3 point = Vector3.zero;
    bool hit = !dummyReaction.IsDown && IsAimedAtDummy(aimRay, useCone, out point);
    
    // TEMP DIAGNOSTIC: remove once aiming is tuned.
    var toDummy = DummyCenter() - aimRay.origin;
    Debug.Log($"[aim] {source} hit={hit} angle={Vector3.Angle(aimRay.direction, toDummy):0.0}deg " +
                $"dist={toDummy.magnitude:0.0}m tolerance={imuAimToleranceDeg}deg");
    
    Vector3 destination;
    if (hit) destination = point;
    else
    {
        RaycastHit block;
        destination = Physics.Raycast(aimRay, out block, 30f) ? block.point : aimRay.GetPoint(30f);
    }
    StartCoroutine(CastAtDummy(spellIndex, confidence, source, destination, hit));
    return true;                                            // a cast happened, hit or miss
    }
    
    IEnumerator CastAtDummy(int spellIndex, float confidence, string source, Vector3 destination, bool aimedAtDummy)
    {
    busy = true;
    var color = SpellColors[spellIndex];
    var spellName = SpellNames[spellIndex];
    UpdateLabels(spellName.ToUpperInvariant() + (aimedAtDummy ? "  /  DUMMY" : "  /  MISSED"),
        source + "  " + Mathf.Clamp01(confidence).ToString("P0"));
    yield return StartCoroutine(SpellEffect(spellName, color, destination));
    
    if (!aimedAtDummy || dummyReaction == null)
    {
        UpdateLabels("MISSED", InputHint());                            // aim was off the dummy
    }
    else if (dummyReaction.TryHit(spellName))                         // only the matching element kills it
    {
        level2Hits++;
        if (scoreText != null) scoreText.text = level2Hits + " HITS";
        UpdateLabels("DIRECT HIT  /  " + level2Hits, InputHint());
    }
    else
    {
        UpdateLabels(spellName.ToUpperInvariant() + "  /  WRONG ELEMENT", "MATCH THE CRYSTAL COLOUR");
    }
    busy = false;
    }
}
