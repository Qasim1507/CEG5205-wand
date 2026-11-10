using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Local wand game view. A second connected Arduino enables PvP; with one wand,
/// the second duelist is a lightweight game bot.
/// </summary>
public sealed class PvPDuelController : MonoBehaviour {
    const float MaxHealth = 100f;
    const float Cooldown = 0.55f;

    readonly float[] health = { MaxHealth, MaxHealth };
    readonly float[] lastCast = { -10f, -10f };
    readonly Quaternion[] homeRotation = { Quaternion.identity, Quaternion.identity };
    readonly Transform[] wands = new Transform[2];
    readonly Transform[] playerRigs = new Transform[2];
    readonly Camera[] playerCameras = new Camera[2];
    readonly Transform[] playerBodies = new Transform[2];
    readonly Quaternion[] cameraHomeRotation = { Quaternion.identity, Quaternion.identity };
    readonly Quaternion[] poseNeutral = { Quaternion.identity, Quaternion.identity };
    readonly bool[] poseCalibrated = new bool[2];
    readonly Transform[] healthFill = new Transform[2];
    readonly List<Transform> floatingCrystals = new List<Transform>();
    readonly List<Vector3> floatingCrystalHomes = new List<Vector3>();
    readonly List<Light> arenaLights = new List<Light>();
    readonly List<Transform> animatedAccents = new List<Transform>();
    readonly List<float> accentSpinRates = new List<float>();
    TextMesh[] healthLabels = new TextMesh[2];
    TextMesh banner;
    TextMesh victoryText;
    GameObject victoryPanel;
    Transform centerRune;
    Camera gameCamera;
    bool started;
    bool roundOver;
    bool botOpponent;
    bool questRuntime;
    QuestDuelNetwork questNetwork;
    int localPlayerId = 1;
    readonly Vector3[] questHeadPositions = new Vector3[2];
    readonly Quaternion[] questHeadRotations = { Quaternion.identity, Quaternion.identity };
    float nextNetworkStateAt;
    bool aimLocked;
    int activeAimPlayer;
    int lockedAimPlayer;
    Vector2[] aimScreenPositions = new Vector2[2];
    Vector2 lockedCursorPosition;
    float nextBotCastAt;
    int lastHumanSpell = -1;
    int lastBotSpell = -1;
    float lastHumanCastAt = -10f;
    Transform botHome;
    string feedText = "";
    float feedUntil;
    Color feedColor = Color.white;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() {
        if (FindObjectOfType<PvPDuelController>() != null) return;
        new GameObject("PvP Duel Runtime").AddComponent<PvPDuelController>();
    }

    void OnEnable() {
        WandSpellDemoController.GameModeRequested += OnModeRequested;
        WandSpellDemoController.DuelPoseReceived += OnPose;
        WandSpellDemoController.DuelCastReceived += OnCast;
    }

    void OnDisable() {
        WandSpellDemoController.GameModeRequested -= OnModeRequested;
        WandSpellDemoController.DuelPoseReceived -= OnPose;
        WandSpellDemoController.DuelCastReceived -= OnCast;
    }

    void Update() {
        EnsureQuestNetwork();
        if (questRuntime && questNetwork != null && !started) HandleQuestPairingInput();
        if (started && Input.GetKeyDown(KeyCode.F2)) ResetRound();
        if (started) UpdateMouseAim();
        if (started && !roundOver) UpdatePlayerMovement();
        if (started && questRuntime) SendQuestState();
        if (started && botOpponent && !roundOver && Time.time >= nextBotCastAt)
            BotAttack();
        if (centerRune != null) {
            centerRune.Rotate(Vector3.up, Time.deltaTime * 12f, Space.World);
            float pulse = 1f + .035f * Mathf.Sin(Time.time * 2f);
            centerRune.localScale = new Vector3(2.2f * pulse, .04f, 2.2f * pulse);
        }
        for (int i = 0; i < floatingCrystals.Count; i++) {
            floatingCrystals[i].position = floatingCrystalHomes[i] +
                Vector3.up * (.12f * Mathf.Sin(Time.time * 1.5f + i));
            floatingCrystals[i].Rotate(Vector3.up, Time.deltaTime * (35f + i * 8f), Space.World);
        }
        for (int i = 0; i < arenaLights.Count; i++)
            arenaLights[i].intensity = 1.6f + .45f * Mathf.Sin(Time.time * 2.4f + i * 1.7f);
        for (int i = 0; i < animatedAccents.Count; i++) {
            if (animatedAccents[i] != null)
                animatedAccents[i].Rotate(Vector3.up, Time.deltaTime * accentSpinRates[i], Space.World);
        }
    }

    void OnModeRequested(string mode) {
        if (XRSettings.isDeviceActive && mode == "duel") {
            botOpponent = false;
            return;
        }
        bool firstModePacket = !started;
        botOpponent = mode == "single";
        if (firstModePacket) nextBotCastAt = Time.time + Random.Range(2f, 3.5f);
        BeginDuel();
    }

    void EnsureQuestNetwork() {
        if (!XRSettings.isDeviceActive || questNetwork != null) return;
        questRuntime = true;
        questNetwork = GetComponent<QuestDuelNetwork>();
        if (questNetwork == null) questNetwork = gameObject.AddComponent<QuestDuelNetwork>();
        questNetwork.SessionReady += OnQuestSessionReady;
        questNetwork.StateReceived += OnQuestStateReceived;
        questNetwork.SpellReceived += OnQuestSpellReceived;
    }

    void HandleQuestPairingInput() {
        if (questNetwork.IsActive) return;
        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool joinPressed = false;
        bool hostPressed = false;
        left.TryGetFeatureValue(CommonUsages.primaryButton, out joinPressed);
        right.TryGetFeatureValue(CommonUsages.primaryButton, out hostPressed);
        if (hostPressed || Input.GetKeyDown(KeyCode.H)) questNetwork.StartHosting();
        else if (joinPressed || Input.GetKeyDown(KeyCode.J)) questNetwork.JoinNearbyHost();
    }

    void OnQuestSessionReady(int playerId) {
        localPlayerId = playerId;
        botOpponent = false;
        SetFeed(playerId == 1 ? "HOST READY  •  WAITING FOR PLAYER 2" : "CONNECTED TO PLAYER 1", new Color(.75f, .9f, 1f));
        BeginDuel(playerId);
    }

    void OnQuestStateReceived(QuestDuelNetwork.Snapshot state) {
        if (!started || !questRuntime || state == null) return;
        int remoteIndex = 1 - (localPlayerId - 1);
        playerRigs[remoteIndex].position = state.rigPosition;
        playerRigs[remoteIndex].rotation = state.rigRotation;
        questHeadPositions[remoteIndex] = state.headPosition;
        questHeadRotations[remoteIndex] = state.headRotation;
        if (questNetwork == null || !questNetwork.IsHost) {
            health[0] = state.playerOneHealth;
            health[1] = state.playerTwoHealth;
            UpdateHealthUi();
        }
    }

    void OnQuestSpellReceived(QuestDuelNetwork.SpellEvent spellEvent) {
        if (!started || !questRuntime || spellEvent == null) return;
        StartCoroutine(ShowNetworkSpell(spellEvent));
    }

    void BeginDuel(int requestedLocalPlayerId = 1) {
        if (started) return;
        questRuntime = XRSettings.isDeviceActive;
        localPlayerId = requestedLocalPlayerId;
        int localIndex = localPlayerId - 1;
        Vector3[] spawns = { new Vector3(0f, 0f, -14f), new Vector3(0f, 0f, 14f) };
        Quaternion[] spawnRotations = {
            Quaternion.Euler(0f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f)
        };
        var demo = FindObjectOfType<WandSpellDemoController>();
        if (demo != null) {
            if (questRuntime) demo.ActivateDuelAt(spawns[localIndex], spawnRotations[localIndex]);
            else demo.ActivateDesktopDuel();
        }
        gameCamera = Camera.main;
        if (gameCamera == null) return;
        started = true;
        Transform sceneRig = demo != null && demo.DesktopRig != null ? demo.DesktopRig : gameCamera.transform;
        if (questRuntime) {
            playerRigs[localIndex] = sceneRig;
            playerRigs[localIndex].position = spawns[localIndex];
            playerRigs[localIndex].rotation = spawnRotations[localIndex];
            int remoteIndex = 1 - localIndex;
            playerRigs[remoteIndex] = new GameObject("Remote Quest player rig").transform;
            playerRigs[remoteIndex].position = spawns[remoteIndex];
            playerRigs[remoteIndex].rotation = spawnRotations[remoteIndex];
            playerCameras[localIndex] = gameCamera;
            playerCameras[remoteIndex] = null;
            gameCamera.rect = new Rect(0f, 0f, 1f, 1f);
        } else {
            playerRigs[0] = sceneRig;
            playerRigs[0].position = spawns[0];
            playerRigs[0].rotation = spawnRotations[0];
            playerCameras[0] = gameCamera;
            playerRigs[1] = new GameObject("Player 2 FPS camera rig").transform;
            playerRigs[1].position = spawns[1];
            playerRigs[1].rotation = spawnRotations[1];
            gameCamera.rect = botOpponent ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 0f, .5f, 1f);
            gameCamera.fieldOfView = 72f;
        }
        cameraHomeRotation[0] = spawnRotations[0];
        cameraHomeRotation[1] = spawnRotations[1];
        if (!questRuntime) {
            var secondCameraObject = new GameObject("Player 2 camera");
            secondCameraObject.transform.SetParent(playerRigs[1], false);
            secondCameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            secondCameraObject.transform.localRotation = Quaternion.identity;
            playerCameras[1] = secondCameraObject.AddComponent<Camera>();
            playerCameras[1].fieldOfView = 72f;
            playerCameras[1].rect = new Rect(.5f, 0f, .5f, 1f);
            playerCameras[1].depth = gameCamera.depth + 1;
            playerCameras[1].allowHDR = gameCamera.allowHDR;
            playerCameras[1].allowMSAA = gameCamera.allowMSAA;
            playerCameras[1].clearFlags = gameCamera.clearFlags;
            playerCameras[1].backgroundColor = gameCamera.backgroundColor;
            playerCameras[1].enabled = !botOpponent;
        } else playerCameras[1] = null;

        CreateFpsDuelist(0, spawns[0], new Color(.18f, .70f, 1f));
        CreateFpsDuelist(1, spawns[1], new Color(1f, .32f, .20f));
        SetDuelWorldScale(2.25f);
        SetPlayerLayer(playerBodies[0].gameObject, 30);
        SetPlayerLayer(playerBodies[1].gameObject, 31);
        if (questRuntime) gameCamera.cullingMask &= ~(1 << (localIndex == 0 ? 30 : 31));
        else {
            gameCamera.cullingMask &= ~(1 << 30);
            if (playerCameras[1] != null) playerCameras[1].cullingMask &= ~(1 << 31);
        }
        BuildArcaneDuelDecor();
        botHome = playerRigs[1];
        aimScreenPositions[0] = new Vector2(Screen.width * (botOpponent ? .5f : .25f), Screen.height * .5f);
        aimScreenPositions[1] = new Vector2(Screen.width * .75f, Screen.height * .5f);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = !questRuntime;
        UpdateHealthUi();
    }

    void SetDuelWorldScale(float scale) {
        string[] environmentNames = { "ARCANE SANCTUM - environment", "TRAINING SANCTUM - environment" };
        foreach (string environmentName in environmentNames) {
            var environment = GameObject.Find(environmentName);
            if (environment != null) environment.transform.localScale = Vector3.one * scale;
        }
    }

    void SetPlayerLayer(GameObject item, int layer) {
        item.layer = layer;
        foreach (Transform child in item.transform) SetPlayerLayer(child.gameObject, layer);
    }

    void UpdateMouseAim() {
        if (questRuntime) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Input.GetKeyDown(KeyCode.Space)) {
            aimLocked = !aimLocked;
            if (aimLocked) {
                lockedCursorPosition = Input.mousePosition;
                activeAimPlayer = botOpponent || lockedCursorPosition.x < Screen.width * .5f ? 0 : 1;
                lockedAimPlayer = activeAimPlayer;
                aimScreenPositions[lockedAimPlayer] = lockedCursorPosition;
                SetFeed("AIM LOCKED  •  WAND GESTURE TO CAST", new Color(1f, .82f, .25f));
            } else SetFeed("AIM RELEASED", new Color(.72f, .88f, 1f));
        }
        if (aimLocked) {
            aimScreenPositions[lockedAimPlayer] = lockedCursorPosition;
            if (Input.mousePosition != (Vector3)lockedCursorPosition && Mouse.current != null)
                Mouse.current.WarpCursorPosition(lockedCursorPosition);
            return;
        }
        Vector2 mousePosition = Input.mousePosition;
        activeAimPlayer = botOpponent || mousePosition.x < Screen.width * .5f ? 0 : 1;
        if (playerCameras[activeAimPlayer] == null) activeAimPlayer = 0;
        Rect viewport = playerCameras[activeAimPlayer].pixelRect;
        mousePosition.x = Mathf.Clamp(mousePosition.x, viewport.xMin + 2f, viewport.xMax - 2f);
        mousePosition.y = Mathf.Clamp(mousePosition.y, viewport.yMin + 2f, viewport.yMax - 2f);
        aimScreenPositions[activeAimPlayer] = mousePosition;
    }

    Ray GetAimRay(int playerIndex) {
        var aimCamera = playerCameras[playerIndex];
        if (questRuntime && questNetwork != null && questNetwork.IsConnected &&
            playerIndex != localPlayerId - 1)
            return new Ray(questHeadPositions[playerIndex], questHeadRotations[playerIndex] * Vector3.forward);
        if (aimCamera == null) return new Ray(playerRigs[playerIndex].position + Vector3.up, playerRigs[playerIndex].forward);
        if (questRuntime) return new Ray(aimCamera.transform.position, aimCamera.transform.forward);
        return aimCamera.ScreenPointToRay(aimScreenPositions[playerIndex]);
    }

    void CreateFpsDuelist(int index, Vector3 position, Color color) {
        var body = new GameObject(index == 0 ? "Azure spellblade" : "Crimson spellblade");
        body.transform.position = position;
        var hitbox = body.AddComponent<CapsuleCollider>();
        hitbox.height = 1.9f;
        hitbox.radius = .38f;
        hitbox.center = Vector3.up * .95f;
        playerBodies[index] = body.transform;

        Color deep = Color.Lerp(color, new Color(.035f, .045f, .10f), .55f);
        Color trim = index == 0 ? new Color(.18f, .85f, 1f) : new Color(1f, .39f, .14f);
        CreateDuelistPart(body.transform, "Layered battle robe", PrimitiveType.Cylinder,
            new Vector3(0f, .72f, 0f), new Vector3(.63f, .72f, .58f), deep, false);
        CreateDuelistPart(body.transform, "Robe mantle", PrimitiveType.Sphere,
            new Vector3(0f, 1.15f, 0f), new Vector3(.57f, .50f, .49f), color, false);
        CreateDuelistPart(body.transform, "Gilded chest plate", PrimitiveType.Cube,
            new Vector3(0f, 1.24f, -.39f), new Vector3(.48f, .52f, .09f), trim, true);
        CreateDuelistPart(body.transform, "Hood", PrimitiveType.Sphere,
            new Vector3(0f, 1.76f, .05f), new Vector3(.48f, .51f, .46f), deep, false);
        CreateDuelistPart(body.transform, "Face", PrimitiveType.Sphere,
            new Vector3(0f, 1.71f, -.29f), new Vector3(.31f, .35f, .19f),
            new Color(.91f, .75f, .58f), false);
        CreateDuelistPart(body.transform, "Wizard cap", PrimitiveType.Cylinder,
            new Vector3(0f, 2.05f, .03f), new Vector3(.44f, .10f, .44f), trim, true);
        MakeCone("Pointed wizard hood", body.transform, new Vector3(0f, 2.42f, .04f),
            .34f, .75f, deep);
        CreateDuelistPart(body.transform, "Left sleeve", PrimitiveType.Capsule,
            new Vector3(-.47f, 1.12f, -.02f), new Vector3(.22f, .46f, .22f), color, false).transform.localRotation = Quaternion.Euler(0f, 0f, -28f);
        CreateDuelistPart(body.transform, "Right sleeve", PrimitiveType.Capsule,
            new Vector3(.47f, 1.12f, -.02f), new Vector3(.22f, .46f, .22f), color, false).transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
        CreateDuelistPart(body.transform, "Left eye", PrimitiveType.Sphere,
            new Vector3(-.11f, 1.73f, -.45f), new Vector3(.045f, .05f, .035f), trim, true);
        CreateDuelistPart(body.transform, "Right eye", PrimitiveType.Sphere,
            new Vector3(.11f, 1.73f, -.45f), new Vector3(.045f, .05f, .035f), trim, true);
        var waist = MakeArcaneRing("Waist rune trim", body.transform, .42f, .48f, trim);
        waist.localPosition = new Vector3(0f, .65f, 0f);
        waist.localScale = new Vector3(1f, .75f, 1f);

        string asset = index == 0 ? "wand01_blue" : "wand01_red";
        var wand = MakeAssetVisual(asset, "Player " + (index + 1) + " wand visual",
            position + new Vector3(index == 0 ? .38f : -.38f, 1.15f, .1f),
            Vector3.one * .65f, PrimitiveType.Cylinder, new Vector3(.10f, .55f, .10f), trim);
        wand.transform.SetParent(body.transform, true);
        AlignAssetLength(wand, Vector3.up, 1.1f);
        wands[index] = wand.transform;
    }

    GameObject CreateDuelistPart(Transform parent, string name, PrimitiveType shape,
                                 Vector3 localPosition, Vector3 localScale, Color color, bool emissive) {
        var part = MakePrimitive(name, shape, parent.TransformPoint(localPosition), localScale, color, emissive);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        return part;
    }

    GameObject MakeCone(string name, Transform parent, Vector3 localPosition, float radius,
                        float height, Color color) {
        const int sides = 12;
        var vertices = new Vector3[sides + 2];
        vertices[0] = new Vector3(0f, -height * .5f, 0f);
        for (int i = 0; i < sides; i++) {
            float angle = i * Mathf.PI * 2f / sides;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, -height * .5f,
                Mathf.Sin(angle) * radius);
        }
        vertices[sides + 1] = new Vector3(0f, height * .5f, 0f);
        var triangles = new List<int>();
        for (int i = 0; i < sides; i++) {
            int next = (i + 1) % sides;
            triangles.Add(0); triangles.Add(i + 1); triangles.Add(next + 1);
            triangles.Add(i + 1); triangles.Add(sides + 1); triangles.Add(next + 1);
        }
        var mesh = new Mesh { name = name + " mesh", vertices = vertices, triangles = triangles.ToArray() };
        mesh.RecalculateNormals();
        var cone = new GameObject(name);
        cone.transform.SetParent(parent, false);
        cone.transform.localPosition = localPosition;
        var filter = cone.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = cone.AddComponent<MeshRenderer>();
        renderer.material = CreateArcaneMaterial(color, false);
        return cone;
    }

    void BuildArcaneDuelDecor() {
        Vector3 center = new Vector3(0f, .02f, 0f);
        var ground = MakePrimitive("Open world basalt courtyard", PrimitiveType.Plane,
            new Vector3(0f, -.16f, 0f), new Vector3(18f, 1f, 18f), new Color(.58f, .63f, .72f));
        ground.GetComponent<Renderer>().material.mainTexture = CreatePavingTexture();
        ground.GetComponent<Renderer>().material.mainTextureScale = new Vector2(45f, 45f);
        MakePrimitive("Raised grand dueling terrace", PrimitiveType.Cylinder,
            new Vector3(0f, -.12f, 0f), new Vector3(48f, .12f, 48f), new Color(.10f, .13f, .22f));

        var outer = MakeArcaneRing("Outer golden dueling seal", null, 22.2f, 22.55f,
            new Color(.98f, .58f, .16f));
        outer.position = center;
        var inner = MakeArcaneRing("Azure inner dueling seal", null, 14.8f, 15.05f,
            new Color(.15f, .69f, 1f));
        inner.position = center + Vector3.up * .01f;
        var innerRed = MakeArcaneRing("Crimson inner dueling seal", null, 7.8f, 8.02f,
            new Color(1f, .19f, .22f));
        innerRed.position = center + Vector3.up * .018f;

        for (int i = 0; i < 16; i++) {
            float angle = i * Mathf.PI * 2f / 16f;
            var rune = MakePrimitive("Floating arena glyph", PrimitiveType.Cube,
                center + new Vector3(Mathf.Cos(angle) * 18.4f, .055f, Mathf.Sin(angle) * 18.4f),
                new Vector3(.38f, .05f, 1.4f), i % 2 == 0 ? new Color(.2f, .72f, 1f) : new Color(1f, .54f, .17f), true);
            rune.transform.rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
        }

        MakePrimitive("Arcane centerpiece plinth", PrimitiveType.Cylinder,
            new Vector3(0f, 5.7f, 0f), new Vector3(5.5f, .45f, 5.5f),
            new Color(.07f, .10f, .20f), true);
        var core = MakeAssetVisual("orb03_blue", "Floating arcane heart",
            new Vector3(0f, 9f, 0f), Vector3.one * 3.2f,
            PrimitiveType.Cylinder, new Vector3(1.5f, 3.4f, 1.5f), new Color(.12f, .63f, 1f));
        var coreLightObject = new GameObject("Arcane heart glow");
        coreLightObject.transform.position = core.transform.position;
        var coreLight = coreLightObject.AddComponent<Light>();
        coreLight.type = LightType.Point;
        coreLight.color = new Color(.18f, .56f, 1f);
        coreLight.range = 24f;
        coreLight.intensity = 4.2f;
        arenaLights.Add(coreLight);
        animatedAccents.Add(core.transform);
        accentSpinRates.Add(22f);

        var halo = MakeArcaneRing("Floating crystal halo", null, 4.5f, 4.68f,
            new Color(.25f, .73f, 1f));
        halo.position = new Vector3(0f, 9f, 0f);
        halo.rotation = Quaternion.Euler(62f, 0f, 0f);
        animatedAccents.Add(halo);
        accentSpinRates.Add(-35f);

        BuildArcanePillar(-38f, -38f, new Color(.12f, .64f, 1f));
        BuildArcanePillar(38f, -38f, new Color(1f, .24f, .19f));
        BuildArcanePillar(-38f, 38f, new Color(.55f, .25f, 1f));
        BuildArcanePillar(38f, 38f, new Color(1f, .59f, .17f));
        BuildArcanePillar(-48f, 0f, new Color(.18f, .55f, 1f));
        BuildArcanePillar(48f, 0f, new Color(1f, .35f, .19f));
        BuildArcanePillar(0f, -48f, new Color(.38f, .29f, 1f));
        BuildArcanePillar(0f, 48f, new Color(1f, .65f, .22f));

        for (int i = 0; i < 16; i++) {
            float angle = i * Mathf.PI * 2f / 16f;
            float radius = 76f + (i % 3) * 6f;
            float height = 22f + (i % 4) * 6f;
            var mountain = MakeCone("Distant arcane highland", null,
                new Vector3(Mathf.Cos(angle) * radius, height * .5f - .2f, Mathf.Sin(angle) * radius),
                12f + (i % 3) * 3f, height, i % 2 == 0
                    ? new Color(.22f, .31f, .48f) : new Color(.32f, .30f, .51f));
            mountain.transform.rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
        }

        Color sky = new Color(.48f, .67f, .82f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = sky;
        RenderSettings.fogStartDistance = 72f;
        RenderSettings.fogEndDistance = 150f;
        RenderSettings.ambientLight = new Color(.48f, .51f, .62f);
        gameCamera.clearFlags = CameraClearFlags.SolidColor;
        gameCamera.backgroundColor = sky;
        if (playerCameras[1] != null) {
            playerCameras[1].clearFlags = CameraClearFlags.SolidColor;
            playerCameras[1].backgroundColor = sky;
        }
    }

    Texture2D CreatePavingTexture() {
        const int size = 128;
        const int block = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        var random = new System.Random(5205);
        Color[] blocks = new Color[(size / block) * (size / block)];
        for (int i = 0; i < blocks.Length; i++) {
            float shade = .72f + (float)random.NextDouble() * .22f;
            blocks[i] = new Color(shade, shade * 1.02f, shade * 1.12f);
        }
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                int bx = x / block;
                int by = y / block;
                bool seam = x % block < 2 || y % block < 2;
                float noise = .94f + (float)random.NextDouble() * .12f;
                texture.SetPixel(x, y, seam ? new Color(.16f, .19f, .27f) : blocks[by * (size / block) + bx] * noise);
            }
        }
        texture.Apply();
        return texture;
    }

    void BuildArcanePillar(float x, float z, Color color) {
        MakePrimitive("Rune-carved arena pillar", PrimitiveType.Cylinder,
            new Vector3(x, 5f, z), new Vector3(.9f, 5f, .9f),
            new Color(.09f, .10f, .18f));
        var trim = MakeArcaneRing("Pillar crown", null, .86f, 1.02f, color);
        trim.position = new Vector3(x, 9.7f, z);
        var crystal = MakeAssetVisual("orb03_blue", "Pillar spell crystal",
            new Vector3(x, 11.4f, z), Vector3.one * 1.15f,
            PrimitiveType.Cube, Vector3.one * 1.15f, color);
        var lightObject = new GameObject("Arcane pillar light");
        lightObject.transform.position = crystal.transform.position;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 13f;
        light.intensity = 2.4f;
        arenaLights.Add(light);
        animatedAccents.Add(crystal.transform);
        accentSpinRates.Add(48f);
    }

    Transform MakeArcaneRing(string name, Transform parent, float innerRadius,
                             float outerRadius, Color color) {
        const int segments = 72;
        var vertices = new Vector3[(segments + 1) * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++) {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = direction * innerRadius;
            vertices[i * 2 + 1] = direction * outerRadius;
            if (i == segments) continue;
            int v = i * 2;
            int t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        var mesh = new Mesh { name = name + " mesh", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        var ring = new GameObject(name).transform;
        if (parent != null) ring.SetParent(parent, false);
        var filter = ring.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = ring.gameObject.AddComponent<MeshRenderer>();
        renderer.material = CreateArcaneMaterial(color, true);
        return ring;
    }

    Material CreateArcaneMaterial(Color color, bool emission) {
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", .78f);
        if (emission) {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2.2f);
        }
        return material;
    }

    void UpdatePlayerMovement() {
        if (questRuntime) {
            MoveQuestPlayer();
            if (botOpponent && botHome != null)
                botHome.position = new Vector3(Mathf.Sin(Time.time * .35f) * 6f, 0f, 14f);
        } else {
            MovePlayer(0, KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D);
            if (!botOpponent) MovePlayer(1, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow);
            else if (botHome != null)
                botHome.position = new Vector3(Mathf.Sin(Time.time * .35f) * 6f, 0f, 14f);
        }
        for (int i = 0; i < 2; i++) {
            if (playerBodies[i] != null && playerRigs[i] != null) {
                if (questRuntime && playerCameras[i] != null) {
                    playerBodies[i].position = playerCameras[i].transform.position - Vector3.up * 1.6f;
                    playerBodies[i].rotation = playerCameras[i].transform.rotation * Quaternion.Euler(0f, 180f, 0f);
                } else if (questRuntime && i != localPlayerId - 1 && questHeadPositions[i] != Vector3.zero) {
                    playerBodies[i].position = questHeadPositions[i] - Vector3.up * 1.6f;
                    playerBodies[i].rotation = questHeadRotations[i] * Quaternion.Euler(0f, 180f, 0f);
                } else {
                    playerBodies[i].position = playerRigs[i].position;
                    playerBodies[i].rotation = playerRigs[i].rotation * Quaternion.Euler(0f, 180f, 0f);
                }
            }
        }
    }

    void MoveQuestPlayer() {
        int index = localPlayerId - 1;
        var rig = playerRigs[index];
        var view = playerCameras[index];
        if (rig == null || view == null) return;
        Vector2 stick = Vector2.zero;
        InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(CommonUsages.primary2DAxis, out stick);
        Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
        Vector3 move = forward * stick.y + right * stick.x;
        if (move.sqrMagnitude > 1f) move.Normalize();
        rig.position += move * (5f * Time.deltaTime);
        rig.position = new Vector3(Mathf.Clamp(rig.position.x, -48f, 48f),
            rig.position.y, Mathf.Clamp(rig.position.z, -48f, 48f));
    }

    void SendQuestState() {
        if (questNetwork == null || !questNetwork.IsConnected || Time.unscaledTime < nextNetworkStateAt) return;
        nextNetworkStateAt = Time.unscaledTime + .05f;
        int index = localPlayerId - 1;
        var rig = playerRigs[index];
        var view = playerCameras[index];
        if (rig == null || view == null) return;
        questNetwork.SendState(new QuestDuelNetwork.Snapshot {
            rigPosition = rig.position,
            rigRotation = rig.rotation,
            headPosition = view.transform.position,
            headRotation = view.transform.rotation,
            playerOneHealth = health[0],
            playerTwoHealth = health[1]
        });
    }

    void MovePlayer(int index, KeyCode forward, KeyCode backward, KeyCode left, KeyCode right) {
        var rig = playerRigs[index];
        if (rig == null) return;
        float forwardAxis = (Input.GetKey(forward) ? 1f : 0f) - (Input.GetKey(backward) ? 1f : 0f);
        float sideAxis = (Input.GetKey(right) ? 1f : 0f) - (Input.GetKey(left) ? 1f : 0f);
        Vector3 flatForward = Vector3.ProjectOnPlane(rig.forward, Vector3.up).normalized;
        Vector3 flatRight = Vector3.ProjectOnPlane(rig.right, Vector3.up).normalized;
        Vector3 move = (flatForward * forwardAxis + flatRight * sideAxis);
        if (move.sqrMagnitude > 1f) move.Normalize();
        Vector3 next = rig.position + move * (4.2f * Time.deltaTime);
        next.x = Mathf.Clamp(next.x, -48f, 48f);
        next.z = Mathf.Clamp(next.z, -48f, 48f);
        rig.position = next;
    }

    void CreateArena() {
        MakePrimitive("Duel platform", PrimitiveType.Cube, new Vector3(0f, -.35f, 0f),
            new Vector3(15f, .5f, 7f), new Color(.055f, .075f, .13f));
        MakePrimitive("Raised rune dais", PrimitiveType.Cylinder, new Vector3(0f, -.08f, 0f),
            new Vector3(5.4f, .12f, 3.4f), new Color(.12f, .16f, .25f));
        centerRune = MakePrimitive("Glowing arena rune", PrimitiveType.Cylinder,
            new Vector3(0f, .015f, 0f), new Vector3(2.2f, .04f, 2.2f),
            new Color(.08f, .42f, .72f), true).transform;
        MakePrimitive("Rune inner disc", PrimitiveType.Cylinder, new Vector3(0f, .045f, 0f),
            new Vector3(1.25f, .025f, 1.25f), new Color(.08f, .12f, .19f));
        MakePrimitive("Back wall", PrimitiveType.Cube, new Vector3(0f, 2.6f, 3.3f),
            new Vector3(15f, 6f, .3f), new Color(.035f, .05f, .09f));
        CreatePillar(-6.2f, 2.35f, new Color(.12f, .60f, 1f), 0);
        CreatePillar(6.2f, 2.35f, new Color(1f, .24f, .12f), 1);
        CreatePillar(-6.2f, -2.4f, new Color(.70f, .24f, 1f), 2);
        CreatePillar(6.2f, -2.4f, new Color(1f, .66f, .12f), 3);
        var lightObject = new GameObject("PvP arena light");
        lightObject.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.45f;
        RenderSettings.ambientLight = new Color(.28f, .31f, .42f);
    }

    void CreatePillar(float x, float z, Color glow, int index) {
        MakePrimitive("Rune pillar " + index, PrimitiveType.Cylinder,
            new Vector3(x, 1.25f, z), new Vector3(.36f, 1.25f, .36f),
            new Color(.10f, .13f, .20f));
        string[] crystalNames = { "orb03_blue", "orb03_red", "orb03_green", "orb03_yellow" };
        var crystalObject = MakeAssetVisual(crystalNames[index], "Floating arena crystal " + index,
            new Vector3(x, 2.9f, z), Vector3.one * .72f,
            PrimitiveType.Cube, new Vector3(.58f, .88f, .58f), glow);
        crystalObject.transform.rotation *= Quaternion.Euler(0f, 45f, 0f);
        var crystal = crystalObject.transform;
        floatingCrystals.Add(crystal);
        floatingCrystalHomes.Add(crystal.position);
        var lampObject = new GameObject("Crystal light " + index);
        lampObject.transform.position = crystal.position;
        var lamp = lampObject.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = glow;
        lamp.range = 5.5f;
        lamp.intensity = 1.6f;
        arenaLights.Add(lamp);
    }

    void CreatePlayer(int index, Vector3 position, Color color) {
        var body = MakePrimitive("Player " + (index + 1) + " wizard", PrimitiveType.Capsule,
            position + Vector3.up * 1.05f, new Vector3(1.1f, 1.05f, .8f), color);
        MakePrimitive("Player " + (index + 1) + " robe", PrimitiveType.Cylinder,
            position + Vector3.up * .68f, new Vector3(1.05f, .55f, .82f),
            Color.Lerp(color, Color.black, .36f));
        var head = MakePrimitive("Player " + (index + 1) + " head", PrimitiveType.Sphere,
            position + Vector3.up * 2.25f, new Vector3(.72f, .72f, .72f), new Color(.85f, .74f, .62f));
        var hood = MakePrimitive("Player " + (index + 1) + " hood", PrimitiveType.Sphere,
            position + new Vector3(0f, 2.43f, .08f), new Vector3(.82f, .66f, .80f),
            Color.Lerp(color, Color.black, .22f));
        head.transform.SetParent(body.transform, true);
        hood.transform.SetParent(body.transform, true);
        var face = MakePrimitive("Player " + (index + 1) + " face", PrimitiveType.Sphere,
            position + new Vector3(0f, 2.23f, -.31f), new Vector3(.43f, .44f, .24f),
            new Color(.91f, .78f, .63f));
        face.transform.SetParent(body.transform, true);

        var direction = index == 0 ? Vector3.right : Vector3.left;
        string wandAsset = index == 0 ? "wand01_blue" : "wand01_red";
        var wand = MakeAssetVisual(wandAsset, "Player " + (index + 1) + " wand",
            position + new Vector3(direction.x * .7f, 1.65f, -.2f), Vector3.one,
            PrimitiveType.Cylinder, new Vector3(.11f, .78f, .11f), new Color(.38f, .23f, .10f));
        AlignAssetLength(wand, direction, 1.65f);
        wands[index] = wand.transform;
        homeRotation[index] = wand.transform.rotation;

        var labelX = index == 0 ? -2.6f : 2.6f;
        string playerName = botOpponent ? (index == 0 ? "YOU" : "WAND BOT") :
            "PLAYER " + (index + 1);
        healthLabels[index] = MakeText(playerName + "   100 HP",
            new Vector3(labelX, 3.3f, 0f), 1.3f, color);
        var bar = MakePrimitive("Player " + (index + 1) + " health bar", PrimitiveType.Cube,
            new Vector3(labelX, 3.0f, 0f), new Vector3(3.2f, .20f, .12f), color);
        healthFill[index] = bar.transform;
    }

    void CreateVictoryPanel() {
        victoryPanel = MakePrimitive("Victory sign panel", PrimitiveType.Cube,
            new Vector3(0f, 4.15f, 0f), new Vector3(9.2f, 1.0f, .16f),
            new Color(.10f, .075f, .025f), true);
        victoryPanel.transform.rotation = Quaternion.LookRotation(
            gameCamera.transform.forward, gameCamera.transform.up);
        victoryText = MakeText("YOU WIN!", new Vector3(0f, 4.15f, -.13f),
            2.8f, new Color(1f, .82f, .18f));
        victoryPanel.SetActive(false);
        victoryText.gameObject.SetActive(false);
    }

    GameObject MakeAssetVisual(string assetName, string objectName, Vector3 position,
                               Vector3 assetScale, PrimitiveType fallbackShape,
                               Vector3 fallbackScale, Color fallbackColor) {
        var prefab = Resources.Load<GameObject>("PvPAssets/" + assetName);
        if (prefab != null) {
            var instance = Instantiate(prefab);
            instance.name = objectName;
            instance.transform.position = position;
            instance.transform.localScale = assetScale;
            return instance;
        }
        return MakePrimitive(objectName, fallbackShape, position, fallbackScale, fallbackColor, true);
    }

    void AlignAssetLength(GameObject asset, Vector3 direction, float desiredLength) {
        var mesh = asset.GetComponentInChildren<MeshFilter>();
        if (mesh == null || mesh.sharedMesh == null) {
            asset.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
            return;
        }
        Vector3 size = mesh.sharedMesh.bounds.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        Vector3 currentAxis = mesh.transform.TransformDirection(localAxis).normalized;
        asset.transform.rotation = Quaternion.FromToRotation(currentAxis, direction.normalized) * asset.transform.rotation;
        float meshLength = axis == 0 ? size.x : axis == 1 ? size.y : size.z;
        if (meshLength > .001f) asset.transform.localScale = Vector3.one * (desiredLength / meshLength);
    }

    GameObject MakePrimitive(string name, PrimitiveType shape, Vector3 position,
                             Vector3 scale, Color color, bool emissive = false) {
        var item = GameObject.CreatePrimitive(shape);
        item.name = name;
        item.transform.position = position;
        item.transform.localScale = scale;
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", .35f);
        if (emissive) {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.5f);
        }
        item.GetComponent<Renderer>().material = material;
        var collider = item.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        return item;
    }

    TextMesh MakeText(string value, Vector3 position, float size, Color color) {
        var item = new GameObject("PvP text - " + value);
        item.transform.position = position;
        // TextMesh's visible face is local -Z. Align its axes to the camera so
        // text faces the viewer and progresses left-to-right on screen.
        item.transform.rotation = Quaternion.LookRotation(
            gameCamera.transform.forward, gameCamera.transform.up);
        var text = item.AddComponent<TextMesh>();
        text.text = value;
        text.fontSize = 72;
        text.characterSize = size / 72f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = color;
        return text;
    }

    void OnPose(int player, Quaternion pose) {
        if (player < 1 || player > 2) return;
        if (!started) return; // Wait for the launcher mode packet so solo doesn't briefly start as PvP.
        var index = player - 1;
        if (!poseCalibrated[index]) {
            poseNeutral[index] = pose;
            poseCalibrated[index] = true;
        }
        var relativePose = Quaternion.Inverse(poseNeutral[index]) * pose;
        // IMU roll made the desktop view tilt. Keep the horizon level and let
        // the mouse cursor set the shot direction; retain pose on the wand prop.
        if (wands[index] != null)
            wands[index].rotation = cameraHomeRotation[index] * relativePose;
    }

    void OnCast(int player, string spell, float confidence) {
        if (player < 1 || player > 2 || confidence < .55f) return;
        if (botOpponent && player != 1) return;
        if (!started) return;
        if (questRuntime && questNetwork != null && questNetwork.IsActive && !questNetwork.IsHost) return;
        if (roundOver || Time.time - lastCast[player - 1] < Cooldown) return;
        int spellIndex = SpellIndex(spell);
        if (spellIndex < 0) return;
        SetFeed("PLAYER " + player + " CASTS " + SpellName(spellIndex), SpellColor(spellIndex));
        lastCast[player - 1] = Time.time;
        if (botOpponent && player == 1) {
            lastHumanSpell = spellIndex;
            float recentPace = Mathf.Clamp01((2.5f - (Time.time - lastHumanCastAt)) / 2f);
            float reaction = Mathf.Lerp(1.9f, .65f, Mathf.Max(BotPressure(), recentPace));
            nextBotCastAt = Mathf.Min(nextBotCastAt, Time.time + reaction);
            lastHumanCastAt = Time.time;
        }
        int attacker = player - 1;
        var aimCamera = playerCameras[attacker];
        bool landed = false;
        Vector3 impact = playerBodies[1 - attacker] != null
            ? playerBodies[1 - attacker].position : Vector3.zero;
        Ray ray = GetAimRay(attacker);
        RaycastHit hit;
        bool canAim = aimCamera != null || (questRuntime && questNetwork != null &&
            questNetwork.IsHost && questNetwork.IsConnected);
        if (canAim && Physics.Raycast(ray, out hit, 120f)) {
            impact = hit.point;
            landed = hit.transform == playerBodies[1 - attacker] ||
                (playerBodies[1 - attacker] != null && hit.transform.IsChildOf(playerBodies[1 - attacker]));
        }
        if (questRuntime && questNetwork != null && questNetwork.IsHost)
            questNetwork.SendSpell(new QuestDuelNetwork.SpellEvent {
                player = player, spell = spellIndex, origin = ray.origin,
                target = landed ? impact : ray.GetPoint(18f), landed = landed
            });
        StartCoroutine(SpellFlight(attacker, spellIndex, landed, impact));
    }

    void BotAttack() {
        float pressure = BotPressure();
        nextBotCastAt = Time.time + Random.Range(Mathf.Lerp(3.8f, 2.2f, pressure),
            Mathf.Lerp(4.8f, 2.9f, pressure));
        int spellIndex = ChooseBotSpell(pressure);
        float hitChance = Mathf.Lerp(.68f, .94f, pressure);
        bool lands = Random.value < hitChance;
        if (!lands) {
            SetFeed("THE BOT MISSED!", new Color(.72f, .84f, 1f));
            if (banner != null) banner.text = "WAND BOT MISSED!";
            StartCoroutine(RestoreBannerAfter(.9f));
            return;
        }
        lastBotSpell = spellIndex;
        SetFeed("THE BOT CASTS " + SpellName(spellIndex), SpellColor(spellIndex));
        if (banner != null) banner.text = "WAND BOT CASTS " + SpellName(spellIndex);
        StartCoroutine(SpellFlight(1, spellIndex, lands,
            playerBodies[0] != null ? playerBodies[0].position + Vector3.up : Vector3.zero));
    }

    float BotPressure() {
        // The bot speeds up when the human is ahead and eases off when behind.
        return Mathf.Clamp01((health[0] - health[1]) / MaxHealth);
    }

    int ChooseBotSpell(float pressure) {
        if (health[0] <= 35f && Random.value < .45f + .4f * pressure) return 3;
        int choice = Random.Range(0, 4);
        if (choice == lastBotSpell) choice = (choice + Random.Range(1, 4)) % 4;
        // Occasionally answer the player's latest element with a different one
        // so the bot's casts feel reactive instead of repeating a fixed loop.
        if (lastHumanSpell >= 0 && Random.value < .35f)
            choice = (lastHumanSpell + Random.Range(1, 4)) % 4;
        return choice;
    }

    IEnumerator SpellFlight(int attacker, int spellIndex, bool lands = true, Vector3? impactPoint = null) {
        int defender = 1 - attacker;
        var color = SpellColor(spellIndex);
        var aimCamera = playerCameras[attacker];
        Ray aimRay = GetAimRay(attacker);
        bool remoteQuestCaster = questRuntime && questNetwork != null && questNetwork.IsConnected &&
            attacker != localPlayerId - 1;
        Vector3 origin = aimCamera != null || remoteQuestCaster
            ? aimRay.origin + aimRay.direction * .45f
            : (wands[attacker] != null ? wands[attacker].position : Vector3.up);
        Vector3 defenderPosition = playerBodies[defender] != null
            ? playerBodies[defender].position : Vector3.zero;
        Vector3 target = impactPoint ?? defenderPosition;
        if (!lands && (aimCamera != null || remoteQuestCaster) && target == defenderPosition)
            target = aimRay.origin + aimRay.direction * 18f;
        string[] orbNames = { "orb03_red", "orb03_blue", "orb03_green", "orb03_yellow" };
        var orb = MakeAssetVisual(orbNames[spellIndex], "PvP " + SpellName(spellIndex) + " spell",
            origin, Vector3.one * .58f, PrimitiveType.Sphere, Vector3.one * .45f, color);
        for (int i = 0; i < 3; i++) {
            float angle = i * Mathf.PI * 2f / 3f;
            var spark = MakePrimitive("Orbiting spell mote", PrimitiveType.Sphere,
                origin, Vector3.one * .16f, color, true);
            spark.transform.SetParent(orb.transform, false);
            spark.transform.localPosition = new Vector3(Mathf.Cos(angle) * .52f,
                Mathf.Sin(angle * 2f) * .22f, Mathf.Sin(angle) * .52f);
        }
        var halo = MakeArcaneRing("Spell halo", orb.transform, .48f, .53f, color);
        halo.localRotation = Quaternion.Euler(75f, 0f, 0f);
        var trail = orb.AddComponent<TrailRenderer>();
        trail.time = .46f;
        trail.startWidth = .42f;
        trail.endWidth = .015f;
        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
        trail.material = new Material(Shader.Find("Sprites/Default"));
        var glow = orb.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = 3.8f;
        glow.intensity = 2.8f;
        float duration = .52f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime) {
            orb.transform.position = Vector3.Lerp(origin, target, elapsed / duration);
            orb.transform.Rotate(Vector3.up, Time.deltaTime * 260f, Space.Self);
            yield return null;
        }
        orb.transform.position = target;
        Destroy(orb);
        ImpactBurst(target, color);
        if (lands) ApplyDamage(defender, spellIndex);
    }

    IEnumerator ShowNetworkSpell(QuestDuelNetwork.SpellEvent shot) {
        if (shot.spell < 0 || shot.spell > 3) yield break;
        var color = SpellColor(shot.spell);
        string[] orbNames = { "orb03_red", "orb03_blue", "orb03_green", "orb03_yellow" };
        var orb = MakeAssetVisual(orbNames[shot.spell], "Remote Quest spell",
            shot.origin, Vector3.one * .58f, PrimitiveType.Sphere, Vector3.one * .45f, color);
        var trail = orb.AddComponent<TrailRenderer>();
        trail.time = .46f;
        trail.startWidth = .42f;
        trail.endWidth = .015f;
        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
        trail.material = new Material(Shader.Find("Sprites/Default"));
        var glow = orb.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = 3.8f;
        glow.intensity = 2.8f;
        float elapsed = 0f;
        while (elapsed < .52f && orb != null) {
            elapsed += Time.deltaTime;
            orb.transform.position = Vector3.Lerp(shot.origin, shot.target, elapsed / .52f);
            orb.transform.Rotate(Vector3.up, Time.deltaTime * 260f, Space.Self);
            yield return null;
        }
        if (orb != null) Destroy(orb);
        ImpactBurst(shot.target, color);
        if (shot.landed) SetFeed("PLAYER " + shot.player + " HITS!", color);
    }

    void ApplyDamage(int defender, int spellIndex) {
        float[] damage = { 16f, 11f, 13f, 22f };
        health[defender] = Mathf.Max(0f, health[defender] - damage[spellIndex]);
        UpdateHealthUi();
        if (!roundOver && banner != null) {
            banner.text = botOpponent
                ? (defender == 1 ? "YOU HIT THE WAND BOT!" : "WAND BOT HITS YOU!")
                : "PLAYER " + (2 - defender) + " HIT!";
            StartCoroutine(RestoreBannerAfter(.9f));
        }
        SetFeed(botOpponent
            ? (defender == 1 ? "DIRECT HIT!" : "YOU WERE HIT!")
            : "PLAYER " + (2 - defender) + " TAKES A HIT", SpellColor(spellIndex));
        if (health[defender] <= 0f) {
            roundOver = true;
            string result = botOpponent
                ? (defender == 1 ? "YOU WIN!" : "WAND BOT WINS!")
                : "PLAYER " + (2 - defender) + " WINS!";
            if (banner != null) banner.text = result;
            if (victoryText != null) victoryText.text = result;
            if (victoryPanel != null) victoryPanel.SetActive(true);
            if (victoryText != null) victoryText.gameObject.SetActive(true);
            Vector3 winnerPosition = playerRigs[1 - defender] != null
                ? playerRigs[1 - defender].position + Vector3.up * 1.5f
                : Vector3.up * 1.5f;
            ImpactBurst(winnerPosition, new Color(1f, .78f, .18f));
            StartCoroutine(ResetAfterRound());
        }
    }

    IEnumerator ResetAfterRound() {
        yield return new WaitForSeconds(4.5f);
        ResetRound();
    }

    IEnumerator RestoreBannerAfter(float delay) {
        yield return new WaitForSeconds(delay);
        if (!roundOver && banner != null)
            banner.text = botOpponent ? "YOU VS THE WAND BOT" : "CAST YOUR SPELLS";
    }

    void ResetRound() {
        health[0] = MaxHealth;
        health[1] = MaxHealth;
        lastCast[0] = -10f;
        lastCast[1] = -10f;
        roundOver = false;
        lastHumanSpell = -1;
        lastBotSpell = -1;
        lastHumanCastAt = -10f;
        nextBotCastAt = Time.time + Random.Range(2f, 3.5f);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (victoryText != null) victoryText.gameObject.SetActive(false);
        if (banner != null)
            banner.text = botOpponent ? "YOU VS THE WAND BOT" : "CAST YOUR SPELLS";
        UpdateHealthUi();
    }

    void ImpactBurst(Vector3 position, Color color) {
        var shockwave = MakeArcaneRing("Spell impact shockwave", null, .24f, .32f, color);
        shockwave.position = position;
        StartCoroutine(ExpandImpactRing(shockwave));
        var flashObject = new GameObject("Spell impact flash");
        flashObject.transform.position = position;
        var flash = flashObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = color;
        flash.range = 5.5f;
        flash.intensity = 5f;
        StartCoroutine(FadeImpactLight(flashObject, flash));
        for (int i = 0; i < 20; i++) {
            Vector3 offset = Random.insideUnitSphere * .42f;
            var spark = MakePrimitive("Spell impact spark", PrimitiveType.Sphere,
                position + offset, Vector3.one * Random.Range(.09f, .20f), color, true);
            StartCoroutine(ShrinkSpark(spark.transform,
                offset.normalized * Random.Range(.7f, 1.45f)));
        }
    }

    IEnumerator ExpandImpactRing(Transform ring) {
        float elapsed = 0f;
        while (elapsed < .55f && ring != null) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / .55f);
            ring.localScale = Vector3.one * Mathf.Lerp(.3f, 4.6f, t);
            yield return null;
        }
        if (ring != null) Destroy(ring.gameObject);
    }

    IEnumerator FadeImpactLight(GameObject lightObject, Light light) {
        float elapsed = 0f;
        while (elapsed < .32f && light != null) {
            elapsed += Time.deltaTime;
            light.intensity = Mathf.Lerp(5f, 0f, elapsed / .32f);
            yield return null;
        }
        if (lightObject != null) Destroy(lightObject);
    }

    void SetFeed(string message, Color color) {
        feedText = message;
        feedUntil = Time.time + 1.15f;
        feedColor = color;
    }

    IEnumerator ShrinkSpark(Transform spark, Vector3 travel) {
        Vector3 start = spark.position;
        Vector3 initialScale = spark.localScale;
        float elapsed = 0f;
        while (elapsed < .48f && spark != null) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / .48f);
            spark.position = start + travel * t + Vector3.up * (.2f * t);
            spark.localScale = initialScale * (1f - t);
            yield return null;
        }
        if (spark != null) Destroy(spark.gameObject);
    }

    void UpdateHealthUi() {
        for (int i = 0; i < 2; i++) {
            if (healthLabels[i] != null) {
                string playerName = botOpponent ? (i == 0 ? "YOU" : "WAND BOT") :
                    "PLAYER " + (i + 1);
                healthLabels[i].text = playerName + "   " + Mathf.CeilToInt(health[i]) + " HP";
            }
            if (healthFill[i] != null) {
                float ratio = health[i] / MaxHealth;
                healthFill[i].localScale = new Vector3(3.2f * ratio, .20f, .12f);
                healthFill[i].localPosition = new Vector3(i == 0 ? -2.6f - 1.6f * (1f - ratio) :
                    2.6f + 1.6f * (1f - ratio), 3.0f, 0f);
            }
        }
    }

    void OnGUI() {
        if (questRuntime && !started) {
            DrawHudRect(new Rect(Screen.width * .18f, Screen.height * .35f,
                Screen.width * .64f, Screen.height * .30f), new Color(.018f, .024f, .065f, .94f));
            var warningStyle = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Screen.height / 36, 18, 32),
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            warningStyle.normal.textColor = new Color(.88f, .94f, 1f);
            string pairingStatus = questNetwork == null ? "Starting Quest link..." :
                questNetwork.IsHost && !questNetwork.IsConnected ? "HOST READY  •  WAITING FOR PLAYER 2 TO JOIN" :
                !questNetwork.IsHost && questNetwork.IsActive ? "SEARCHING FOR PLAYER 1 ON THIS WI-FI..." :
                "PLAYER 1: PRESS A ON RIGHT CONTROLLER TO HOST\nPLAYER 2: PRESS X ON LEFT CONTROLLER TO JOIN";
            GUI.Label(new Rect(Screen.width * .20f, Screen.height * .37f,
                Screen.width * .60f, Screen.height * .26f),
                "QUEST WAND DUEL\n" + pairingStatus, warningStyle);
            return;
        }
        if (!started) return;
        float half = Screen.width * .5f;
        if (botOpponent) {
            DrawHealthPanel(0, 18f, 202f, "YOU");
            DrawHealthPanel(1, Screen.width - 220f, 202f, "WAND BOT");
        } else if (questRuntime) {
            int localIndex = localPlayerId - 1;
            DrawHealthPanel(localIndex, 18f, half - 36f, "YOU");
            DrawHealthPanel(1 - localIndex, half + 18f, half - 36f, "OPPONENT");
        } else {
            DrawHealthPanel(0, 18f, half - 36f, "PLAYER 1");
            DrawHealthPanel(1, half + 18f, half - 36f, "PLAYER 2");
        }

        var titleStyle = new GUIStyle(GUI.skin.label) {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Clamp(Screen.height / 35, 16, 26),
            fontStyle = FontStyle.Bold,
            richText = true
        };
        titleStyle.normal.textColor = new Color(.9f, .96f, 1f);
        DrawHudRect(new Rect(Screen.width * .34f, 14f, Screen.width * .32f, 40f),
            new Color(.025f, .035f, .09f, .88f));
        DrawHudRect(new Rect(Screen.width * .34f, 14f, 3f, 40f), new Color(.2f, .75f, 1f));
        GUI.Label(new Rect(Screen.width * .34f, 14f, Screen.width * .32f, 40f),
            botOpponent ? "ARCANE DUEL  <size=13>•  SOLO TRIAL</size>" :
                questRuntime ? "ARCANE DUEL  <size=13>•  QUEST ONLINE</size>" :
                "ARCANE DUEL  <size=13>•  LOCAL RIVALS</size>", titleStyle);

        var hintStyle = new GUIStyle(GUI.skin.label) {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Clamp(Screen.height / 68, 11, 16),
            fontStyle = FontStyle.Bold
        };
        hintStyle.normal.textColor = new Color(.78f, .88f, 1f);
        DrawHudRect(new Rect(Screen.width * .15f, Screen.height - 43f,
            Screen.width * .70f, 30f), new Color(.025f, .035f, .09f, .82f));
        GUI.Label(new Rect(Screen.width * .15f, Screen.height - 43f,
            Screen.width * .70f, 30f), questRuntime
                ? "LOOK TO AIM  •  WAND GESTURE TO CAST"
                : botOpponent
                    ? "WASD MOVE  •  MOUSE AIM  •  SPACE LOCK / UNLOCK  •  WAND GESTURE CAST"
                    : "P1 WASD  •  P2 ARROWS  •  MOVE MOUSE INTO YOUR HALF TO AIM  •  SPACE LOCKS AIM", hintStyle);
        if (!questRuntime && !botOpponent) {
            DrawHudRect(new Rect(half - 1f, 0f, 2f, Screen.height), new Color(.48f, .72f, 1f, .75f));
            DrawReticle(aimScreenPositions[0], aimLocked && lockedAimPlayer == 0
                ? new Color(1f, .82f, .25f) : new Color(.2f, .82f, 1f));
            DrawReticle(aimScreenPositions[1], aimLocked && lockedAimPlayer == 1
                ? new Color(1f, .82f, .25f) : new Color(1f, .38f, .23f));
        } else if (!questRuntime)
            DrawReticle(aimScreenPositions[0], aimLocked
                ? new Color(1f, .82f, .25f) : new Color(.58f, .86f, 1f));

        if (Time.time < feedUntil && !string.IsNullOrEmpty(feedText)) {
            var feedStyle = new GUIStyle(titleStyle) { fontSize = Mathf.Clamp(Screen.height / 25, 20, 34) };
            feedStyle.normal.textColor = feedColor;
            GUI.Label(new Rect(Screen.width * .2f, Screen.height * .20f,
                Screen.width * .6f, 45f), feedText, feedStyle);
        }

        if (roundOver) {
            DrawHudRect(new Rect(Screen.width * .22f, Screen.height * .34f,
                Screen.width * .56f, Screen.height * .27f), new Color(.018f, .024f, .065f, .94f));
            DrawHudRect(new Rect(Screen.width * .22f, Screen.height * .34f, 5f,
                Screen.height * .27f), new Color(1f, .68f, .18f));
            var resultStyle = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Screen.height / 12, 30, 64),
                fontStyle = FontStyle.Bold,
                richText = true
            };
            resultStyle.normal.textColor = new Color(1f, .83f, .2f);
            GUI.Label(new Rect(Screen.width * .22f, Screen.height * .36f,
                Screen.width * .56f, Screen.height * .23f),
                botOpponent ? (health[0] > 0 ? "YOU WIN!" : "BOT WINS") :
                    questRuntime
                        ? (health[localPlayerId - 1] > 0 ? "YOU WIN!" : "YOU LOSE")
                        : (health[0] > 0 ? "PLAYER 1 WINS!" : "PLAYER 2 WINS!"), resultStyle);
        }
    }

    void DrawHealthPanel(int index, float x, float width, string label) {
        float y = 68f;
        Color accent = index == 0 ? new Color(.12f, .72f, 1f) : new Color(1f, .28f, .16f);
        DrawHudRect(new Rect(x, y, width, 55f), new Color(.018f, .026f, .065f, .88f));
        DrawHudRect(new Rect(x, y, 3f, 55f), accent);
        var nameStyle = new GUIStyle(GUI.skin.label) {
            alignment = TextAnchor.MiddleLeft,
            fontSize = Mathf.Clamp(Screen.height / 62, 12, 18),
            fontStyle = FontStyle.Bold
        };
        nameStyle.normal.textColor = new Color(.85f, .91f, 1f);
        GUI.Label(new Rect(x + 11f, y + 3f, width * .65f, 22f), label, nameStyle);
        var hpStyle = new GUIStyle(nameStyle) {
            alignment = TextAnchor.MiddleRight,
            fontSize = Mathf.Clamp(Screen.height / 70, 11, 16)
        };
        hpStyle.normal.textColor = accent;
        GUI.Label(new Rect(x + width * .65f, y + 3f, width * .30f, 22f),
            Mathf.CeilToInt(health[index]) + "  HP", hpStyle);
        DrawHudRect(new Rect(x + 11f, y + 34f, width - 22f, 9f), new Color(.08f, .10f, .17f));
        DrawHudRect(new Rect(x + 11f, y + 34f, (width - 22f) * (health[index] / MaxHealth), 9f), accent);
    }

    void DrawHudRect(Rect rect, Color color) {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    void DrawReticle(Vector2 screenPosition, Color color) {
        float centerX = screenPosition.x;
        float midY = Screen.height - screenPosition.y;
        DrawHudRect(new Rect(centerX - 8f, midY - 1f, 6f, 2f), color);
        DrawHudRect(new Rect(centerX + 2f, midY - 1f, 6f, 2f), color);
        DrawHudRect(new Rect(centerX - 1f, midY - 8f, 2f, 6f), color);
        DrawHudRect(new Rect(centerX - 1f, midY + 2f, 2f, 6f), color);
        DrawHudRect(new Rect(centerX - 1f, midY - 1f, 2f, 2f), Color.white);
    }

    static int SpellIndex(string spell) {
        switch ((spell ?? "").ToLowerInvariant()) {
            case "fire": return 0;
            case "water": return 1;
            case "wind": return 2;
            case "lightning": return 3;
            default: return -1;
        }
    }

    static string SpellName(int index) {
        return new[] { "FIRE", "WATER", "WIND", "LIGHTNING" }[index];
    }

    static Color SpellColor(int index) {
        return new[] {
            new Color(1f, .08f, .025f), new Color(.08f, .48f, 1f),
            new Color(.10f, .9f, .18f), new Color(1f, .88f, .08f)
        }[index];
    }
}
