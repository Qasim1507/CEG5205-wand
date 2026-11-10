using System.Collections;
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
    TextMesh[] healthLabels = new TextMesh[2];
    TextMesh banner;
    Camera gameCamera;
    bool started;
    bool roundOver;
    bool botOpponent;
    float nextBotCastAt;

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
            new Vector3(0f, 4.3f, 0f), 2.4f, Color.white);
        UpdateHealthUi();
    }

    void CreateArena() {
        MakePrimitive("Duel platform", PrimitiveType.Cube, new Vector3(0f, -.35f, 0f),
            new Vector3(15f, .5f, 7f), new Color(.09f, .11f, .17f));
        MakePrimitive("Arena center rune", PrimitiveType.Cylinder, new Vector3(0f, -.05f, 0f),
            new Vector3(2.2f, .04f, 2.2f), new Color(.16f, .25f, .38f));
        MakePrimitive("Back wall", PrimitiveType.Cube, new Vector3(0f, 2.6f, 3.3f),
            new Vector3(15f, 6f, .3f), new Color(.06f, .08f, .13f));
        var lightObject = new GameObject("PvP arena light");
        lightObject.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        RenderSettings.ambientLight = new Color(.45f, .48f, .58f);
    }

    void CreatePlayer(int index, Vector3 position, Color color) {
        var body = MakePrimitive("Player " + (index + 1) + " wizard", PrimitiveType.Capsule,
            position + Vector3.up * 1.05f, new Vector3(1.1f, 1.05f, .8f), color);
        var head = MakePrimitive("Player " + (index + 1) + " head", PrimitiveType.Sphere,
            position + Vector3.up * 2.25f, new Vector3(.72f, .72f, .72f), new Color(.85f, .74f, .62f));
        head.transform.SetParent(body.transform, true);

        var direction = index == 0 ? Vector3.right : Vector3.left;
        var wand = MakePrimitive("Player " + (index + 1) + " wand", PrimitiveType.Cylinder,
            position + new Vector3(direction.x * .7f, 1.65f, -.2f),
            new Vector3(.09f, .72f, .09f), new Color(.35f, .23f, .12f));
        wand.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
        wands[index] = wand.transform;
        homeRotation[index] = Quaternion.LookRotation(direction);

        var labelX = index == 0 ? -2.6f : 2.6f;
        string playerName = botOpponent ? (index == 0 ? "YOU" : "WAND BOT") :
            "PLAYER " + (index + 1);
        healthLabels[index] = MakeText(playerName + "   100 HP",
            new Vector3(labelX, 3.3f, 0f), 1.3f, color);
        var bar = MakePrimitive("Player " + (index + 1) + " health bar", PrimitiveType.Cube,
            new Vector3(labelX, 3.0f, 0f), new Vector3(3.2f, .20f, .12f), color);
        healthFill[index] = bar.transform;
    }

    GameObject MakePrimitive(string name, PrimitiveType shape, Vector3 position,
                             Vector3 scale, Color color) {
        var item = GameObject.CreatePrimitive(shape);
        item.name = name;
        item.transform.position = position;
        item.transform.localScale = scale;
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", .35f);
        item.GetComponent<Renderer>().material = material;
        return item;
    }

    TextMesh MakeText(string value, Vector3 position, float size, Color color) {
        var item = new GameObject("PvP text - " + value);
        item.transform.position = position;
        item.transform.rotation = Quaternion.LookRotation(gameCamera.transform.position - position);
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
        StartCoroutine(SpellFlight(player - 1, spellIndex));
    }

    void BotAttack() {
        nextBotCastAt = Time.time + Random.Range(2.4f, 4.1f);
        int spellIndex = Random.Range(0, 4);
        bool lands = Random.value >= .22f;
        if (!lands) {
            if (banner != null) banner.text = "WAND BOT MISSED!";
            StartCoroutine(RestoreBannerAfter(.9f));
            return;
        }
        if (banner != null) banner.text = "WAND BOT CASTS " + SpellName(spellIndex);
        StartCoroutine(SpellFlight(1, spellIndex));
    }

    IEnumerator SpellFlight(int attacker, int spellIndex, bool lands = true) {
        int defender = 1 - attacker;
        var color = SpellColor(spellIndex);
        Vector3 origin = wands[attacker] != null ? wands[attacker].position :
            new Vector3(attacker == 0 ? -4f : 4f, 1.7f, 0f);
        Vector3 target = new Vector3(defender == 0 ? -4f : 4f, 1.5f, 0f);
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "PvP " + SpellName(spellIndex) + " spell";
        orb.transform.position = origin;
        orb.transform.localScale = Vector3.one * .45f;
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 1.6f);
        orb.GetComponent<Renderer>().material = material;
        float duration = .45f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime) {
            orb.transform.position = Vector3.Lerp(origin, target, elapsed / duration);
            yield return null;
        }
        orb.transform.position = target;
        Destroy(orb);
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
            if (banner != null) banner.text = botOpponent
                ? (defender == 1 ? "YOU WIN!" : "WAND BOT WINS!")
                : "PLAYER " + (2 - defender) + " WINS!";
            StartCoroutine(ResetAfterRound());
        }
    }

    IEnumerator ResetAfterRound() {
        yield return new WaitForSeconds(3f);
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
        nextBotCastAt = Time.time + Random.Range(2f, 3.5f);
        if (banner != null)
            banner.text = botOpponent ? "YOU VS THE WAND BOT" : "CAST YOUR SPELLS";
        UpdateHealthUi();
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
