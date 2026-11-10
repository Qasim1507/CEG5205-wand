using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    readonly Transform[] healthFill = new Transform[2];
    readonly List<Transform> floatingCrystals = new List<Transform>();
    readonly List<Vector3> floatingCrystalHomes = new List<Vector3>();
    readonly List<Light> arenaLights = new List<Light>();
    TextMesh[] healthLabels = new TextMesh[2];
    TextMesh banner;
    TextMesh victoryText;
    GameObject victoryPanel;
    Transform centerRune;
    Camera gameCamera;
    bool started;
    bool roundOver;
    bool botOpponent;
    float nextBotCastAt;
    int lastHumanSpell = -1;
    int lastBotSpell = -1;
    float lastHumanCastAt = -10f;

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
        if (started && Input.GetKeyDown(KeyCode.F2)) ResetRound();
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
    }

    void OnModeRequested(string mode) {
        bool firstModePacket = !started;
        botOpponent = mode == "single";
        if (firstModePacket) nextBotCastAt = Time.time + Random.Range(2f, 3.5f);
        BeginDuel();
    }

    void BeginDuel() {
        if (started) return;
        gameCamera = Camera.main;
        if (gameCamera == null) return;
        started = true;

        // Clear the single-player meeting scene from view, then make a simple
        // side-on arena that two people can share on one monitor.
        foreach (Renderer item in FindObjectsOfType<Renderer>()) item.enabled = false;
        gameCamera.transform.position = new Vector3(0f, 6f, -14f);
        gameCamera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.1f, 0f) - gameCamera.transform.position);
        gameCamera.fieldOfView = 48f;
        CreateArena();
        CreatePlayer(0, new Vector3(-4f, 0f, 0f), new Color(.18f, .70f, 1f));
        CreatePlayer(1, new Vector3(4f, 0f, 0f), new Color(1f, .32f, .20f));
        banner = MakeText(botOpponent ? "YOU VS THE WAND BOT" : "CAST YOUR SPELLS",
            new Vector3(0f, 4.75f, 0f), 1.8f, Color.white);
        CreateVictoryPanel();
        UpdateHealthUi();
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
        if (!started) BeginDuel();
        var index = player - 1;
        if (wands[index] != null)
            wands[index].rotation = Quaternion.Slerp(wands[index].rotation,
                homeRotation[index] * pose, Time.deltaTime * 10f);
    }

    void OnCast(int player, string spell, float confidence) {
        if (player < 1 || player > 2 || confidence < .55f) return;
        if (botOpponent && player != 1) return;
        if (!started) BeginDuel();
        if (roundOver || Time.time - lastCast[player - 1] < Cooldown) return;
        int spellIndex = SpellIndex(spell);
        if (spellIndex < 0) return;
        lastCast[player - 1] = Time.time;
        if (botOpponent && player == 1) {
            lastHumanSpell = spellIndex;
            float recentPace = Mathf.Clamp01((2.5f - (Time.time - lastHumanCastAt)) / 2f);
            float reaction = Mathf.Lerp(1.9f, .65f, Mathf.Max(BotPressure(), recentPace));
            nextBotCastAt = Mathf.Min(nextBotCastAt, Time.time + reaction);
            lastHumanCastAt = Time.time;
        }
        StartCoroutine(SpellFlight(player - 1, spellIndex));
    }

    void BotAttack() {
        float pressure = BotPressure();
        nextBotCastAt = Time.time + Random.Range(Mathf.Lerp(3.8f, 2.2f, pressure),
            Mathf.Lerp(4.8f, 2.9f, pressure));
        int spellIndex = ChooseBotSpell(pressure);
        float hitChance = Mathf.Lerp(.68f, .94f, pressure);
        bool lands = Random.value < hitChance;
        if (!lands) {
            if (banner != null) banner.text = "WAND BOT MISSED!";
            StartCoroutine(RestoreBannerAfter(.9f));
            return;
        }
        lastBotSpell = spellIndex;
        if (banner != null) banner.text = "WAND BOT CASTS " + SpellName(spellIndex);
        StartCoroutine(SpellFlight(1, spellIndex));
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

    IEnumerator SpellFlight(int attacker, int spellIndex, bool lands = true) {
        int defender = 1 - attacker;
        var color = SpellColor(spellIndex);
        Vector3 origin = wands[attacker] != null ? wands[attacker].position :
            new Vector3(attacker == 0 ? -4f : 4f, 1.7f, 0f);
        Vector3 target = new Vector3(defender == 0 ? -4f : 4f, 1.5f, 0f);
        string[] orbNames = { "orb03_red", "orb03_blue", "orb03_green", "orb03_yellow" };
        var orb = MakeAssetVisual(orbNames[spellIndex], "PvP " + SpellName(spellIndex) + " spell",
            origin, Vector3.one * .58f, PrimitiveType.Sphere, Vector3.one * .45f, color);
        var trail = orb.AddComponent<TrailRenderer>();
        trail.time = .32f;
        trail.startWidth = .30f;
        trail.endWidth = .015f;
        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
        trail.material = new Material(Shader.Find("Sprites/Default"));
        var glow = orb.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = 2.6f;
        glow.intensity = 1.8f;
        float duration = .45f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime) {
            orb.transform.position = Vector3.Lerp(origin, target, elapsed / duration);
            yield return null;
        }
        orb.transform.position = target;
        Destroy(orb);
        ImpactBurst(target, color);
        if (lands) ApplyDamage(defender, spellIndex);
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
        if (health[defender] <= 0f) {
            roundOver = true;
            string result = botOpponent
                ? (defender == 1 ? "YOU WIN!" : "WAND BOT WINS!")
                : "PLAYER " + (2 - defender) + " WINS!";
            if (banner != null) banner.text = result;
            if (victoryText != null) victoryText.text = result;
            if (victoryPanel != null) victoryPanel.SetActive(true);
            if (victoryText != null) victoryText.gameObject.SetActive(true);
            Vector3 winnerPosition = defender == 1 ? new Vector3(-4f, 1.6f, 0f) :
                new Vector3(4f, 1.6f, 0f);
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
        for (int i = 0; i < 10; i++) {
            Vector3 offset = Random.insideUnitSphere * .28f;
            var spark = MakePrimitive("Spell impact spark", PrimitiveType.Sphere,
                position + offset, Vector3.one * Random.Range(.08f, .16f), color, true);
            StartCoroutine(ShrinkSpark(spark.transform,
                offset.normalized * Random.Range(.4f, .95f)));
        }
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
