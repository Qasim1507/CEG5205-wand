using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Meeting vertical slice. Keyboard is a simulated classifier; the same
/// CastSpell entry point accepts predictions from the Python IMU model.
/// </summary>
public sealed class WandSpellDemoController : MonoBehaviour
{
    [Serializable]
    sealed class SensorPacket
    {
        public string spell;
        public float conf;
        public float[] q;
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

    readonly Queue<string> incoming = new Queue<string>();
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
    static readonly string[] SpellNames = { "fire", "frost", "storm", "arcane", "shatter" };
    static readonly Color[] SpellColors = {
        new Color(1f,.26f,.04f), new Color(.2f,.8f,1f), new Color(.7f,.3f,1f),
        new Color(.1f,1f,.7f), new Color(1f,.72f,.2f)
    };
    float lastSpellTime = -100f;
    Quaternion recenter = Quaternion.identity;
    Vector3[] crystalHome;
    Vector2 aimViewport = new Vector2(.5f, .5f);
    bool skipMouseDelta;

    void Start()
    {
        crystalHome = new Vector3[targets.Length];
        broken = new bool[targets.Length];
        for (var i = 0; i < targets.Length; ++i) crystalHome[i] = targets[i].localPosition;
        SetWandStyle(-1);
        viewCamera = Camera.main;
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
                    if (incoming.Count < 32) incoming.Enqueue(json);
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { break; }
        }
    }

    void Update()
    {
        for (var i = 0; i < targets.Length; ++i)
        {
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
        if (keyboard != null)
        {
            if (wandStyle < 0)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) SetWandStyle(0);
                if (keyboard.digit2Key.wasPressedThisFrame) SetWandStyle(1);
                if (keyboard.digit3Key.wasPressedThisFrame) SetWandStyle(2);
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                recenter = Quaternion.Inverse(wand.localRotation);
                aimViewport = new Vector2(.5f, .5f);
            }
        }
        var mouse = Mouse.current;
        if (wandStyle < 0 && mouse != null && mouse.leftButton.wasPressedThisFrame && viewCamera != null)
        {
            RaycastHit displayHit;
            if (Physics.Raycast(viewCamera.ScreenPointToRay(mouse.position.ReadValue()), out displayHit, 20f))
                for (int i = 0; wandDisplays != null && i < wandDisplays.Length; i++)
                    if (displayHit.collider.transform == wandDisplays[i]) { SetWandStyle(i); break; }
        }
        if (wandStyle >= 0 && arenaReady && mouse != null && viewCamera != null)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                if (skipMouseDelta) skipMouseDelta = false;
                else aimViewport = AdvanceAim(aimViewport, mouse.delta.ReadValue());
            }
            else
                Cursor.visible = true;
            var ray = viewCamera.ViewportPointToRay(new Vector3(aimViewport.x, aimViewport.y, 0f));
            if (crosshair != null)
            {
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
            bool hitCrystal = TryGetAimedCrystal(ray, out hitPoint);
            if (hitCrystal) aimPoint = hitPoint;
            var localDirection = wand.parent.InverseTransformDirection((aimPoint - wand.position).normalized);
            var desiredRotation = Quaternion.LookRotation(localDirection, Vector3.up);
            wand.localRotation = Quaternion.Slerp(wand.localRotation, desiredRotation, Time.deltaTime * 16f);
            if (aimMarker != null)
            {
                aimMarker.gameObject.SetActive(hitCrystal);
                if (hitCrystal) aimMarker.position = targets[selected].position;
            }
            if (mouse.leftButton.wasPressedThisFrame && !busy)
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    skipMouseDelta = true;
                }
                if (hitCrystal) CastSpell(SpellNames[selected % SpellNames.Length], 1f, "MOUSE AIM");
                else UpdateLabels("CRYSTAL " + (selected + 1) + " IS ACTIVE", "AIM AT THE GLOWING CRYSTAL");
            }
        }

        while (true)
        {
            string json;
            lock (incoming)
            {
                if (incoming.Count == 0) break;
                json = incoming.Dequeue();
            }
            try
            {
                var packet = JsonUtility.FromJson<SensorPacket>(json);
                if (packet == null) continue;
                if (packet.q != null && packet.q.Length == 4)
                {
                    var rotation = new Quaternion(packet.q[1], packet.q[2], packet.q[3], packet.q[0]);
                    var lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y +
                        rotation.z * rotation.z + rotation.w * rotation.w;
                    if (lengthSquared > 0.1f) wand.localRotation = recenter * rotation.normalized;
                }
                if (!string.IsNullOrEmpty(packet.spell)) CastSpell(packet.spell, packet.conf, "IMU / UDP");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Invalid wand packet: " + exception.Message);
            }
        }
    }

    static Vector2 AdvanceAim(Vector2 current, Vector2 mouseDelta)
    {
        const float sensitivity = .0015f;
        return new Vector2(Mathf.Clamp(current.x + mouseDelta.x * sensitivity, .07f, .93f),
            Mathf.Clamp(current.y + mouseDelta.y * sensitivity, .12f, .88f));
    }

    bool TryGetAimedCrystal(Ray ray, out Vector3 point)
    {
        point = Vector3.zero;
        RaycastHit hit;
        Physics.SyncTransforms();
        if (!Physics.Raycast(ray, out hit, 30f) || selected >= targets.Length ||
            hit.collider.transform != targets[selected]) return false;
        point = hit.point;
        return true;
    }

    void SetWandStyle(int style)
    {
        wandStyle = style;
        if (choiceText != null) choiceText.gameObject.SetActive(style < 0);
        if (wandStyles != null)
            for (int i = 0; i < wandStyles.Length; i++)
                if (wandStyles[i] != null) wandStyles[i].gameObject.SetActive(i == style);
        if (style >= 0)
        {
            UpdateLabels("THE GATE AWAKENS", "ENTERING THE ARENA");
            if (sceneRig != null) StartCoroutine(EnterArena());
            else arenaReady = true;
        }
    }

    IEnumerator EnterArena()
    {
        arenaReady = false;
        var from = sceneRig.position;
        var to = new Vector3(0f, 0f, -3.25f);
        var doorLeft = entranceDoors != null && entranceDoors.Length > 0 ? entranceDoors[0] : null;
        var doorRight = entranceDoors != null && entranceDoors.Length > 1 ? entranceDoors[1] : null;
        var leftHome = doorLeft != null ? doorLeft.localPosition : Vector3.zero;
        var rightHome = doorRight != null ? doorRight.localPosition : Vector3.zero;
        float elapsed = 0f;
        while (elapsed < 2.7f)
        {
            elapsed += Time.deltaTime;
            float gate = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .85f));
            if (doorLeft != null) doorLeft.localPosition = leftHome + Vector3.left * gate * 1.7f;
            if (doorRight != null) doorRight.localPosition = rightHome + Vector3.right * gate * 1.7f;
            float travel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - .45f) / 2.2f));
            sceneRig.position = Vector3.Lerp(from, to, travel);
            yield return null;
        }
        sceneRig.position = to;
        arenaReady = true;
        aimViewport = new Vector2(.5f, .5f);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        skipMouseDelta = true;
        if (crosshair != null) crosshair.gameObject.SetActive(true);
        if (scoreText != null) scoreText.gameObject.SetActive(true);
        UpdateLabels("CRYSTAL 1 IS ACTIVE", "MOVE MOUSE TO AIM  /  LEFT CLICK TO FIRE");
    }

    public void CastSpell(string spell, float confidence, string source)
    {
        if (wandStyle < 0 || busy || Time.time - lastSpellTime < 0.25f ||
            targets == null || selected >= targets.Length) return;
        spell = spell.Trim().ToLowerInvariant();
        if (spell == "fireball") spell = "fire";
        if (spell == "ice") spell = "frost";
        if (spell == "lightning") spell = "storm";
        int spellIndex = Array.IndexOf(SpellNames, spell);
        if (spellIndex < 0) return;
        lastSpellTime = Time.time;
        StartCoroutine(CastAndShatter(spellIndex, confidence, source));
    }

    IEnumerator CastAndShatter(int spellIndex, float confidence, string source)
    {
        busy = true;
        int index = selected;
        var color = SpellColors[spellIndex];
        var destination = targets[index].position;
        UpdateLabels(SpellNames[spellIndex].ToUpperInvariant() + "  /  CRYSTAL " + (index + 1),
            source + "  " + Mathf.Clamp01(confidence).ToString("P0"));
        yield return StartCoroutine(SpellEffect(SpellNames[spellIndex], color, destination));
        yield return StartCoroutine(Shatter(index, color));
        score++; selected++;
        if (scoreText != null) scoreText.text = score + " / " + targets.Length + " CRYSTALS";
        if (selected >= targets.Length)
        {
            UpdateLabels("SANCTUM CLEARED", "THE CRYSTALS WILL REFORM");
            yield return new WaitForSeconds(2.5f);
            for (int i = 0; i < targets.Length; i++)
            {
                broken[i] = false;
                targets[i].gameObject.SetActive(true);
                targets[i].localPosition = crystalHome[i];
            }
            selected = score = 0;
            if (scoreText != null) scoreText.text = "0 / " + targets.Length + " CRYSTALS";
        }
        UpdateLabels("CRYSTAL " + (selected + 1) + " IS ACTIVE", "MOVE MOUSE TO AIM  /  LEFT CLICK TO FIRE");
        busy = false;
    }

    IEnumerator SpellEffect(string spell, Color color, Vector3 destination)
    {
        var start = wandTip.position;
        if (spell == "storm" || spell == "shatter")
        {
            var bolt = new GameObject("Storm spell - temporary arc");
            var line = bolt.AddComponent<LineRenderer>();
            line.positionCount = 9;
            line.startWidth = 0.035f;
            line.endWidth = 0.008f;
            line.material = Glow(color);
            for (var i = 0; i < 9; ++i)
            {
                var t = i / 8f;
                line.SetPosition(i, Vector3.Lerp(start, destination, t) +
                    (i == 0 || i == 8 ? Vector3.zero : UnityEngine.Random.insideUnitSphere * 0.08f));
            }
            Destroy(bolt, 0.28f);
            yield return new WaitForSeconds(.24f);
            yield break;
        }

        var projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectile.name = spell + " spell projectile";
        Destroy(projectile.GetComponent<Collider>());
        projectile.transform.localScale = Vector3.one * (spell == "fire" ? 0.13f : 0.10f);
        projectile.GetComponent<Renderer>().material = Glow(color);
        var light = projectile.AddComponent<Light>();
        light.color = color;
        light.range = 1.5f;
        light.intensity = 2f;
        var trail = projectile.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = 0.09f;
        trail.endWidth = 0f;
        trail.material = Glow(color);
        var elapsed = 0f;
        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            projectile.transform.position = Vector3.Lerp(start, destination, Mathf.Clamp01(elapsed / 0.4f));
            yield return null;
        }
        Destroy(projectile, 0.3f);
    }

    IEnumerator Shatter(int index, Color color)
    {
        var crystal = targets[index];
        var impactPoint = crystal.position;
        var mesh = targetCrystals[index].GetComponent<MeshFilter>().sharedMesh;
        var pieces = new List<Transform>();
        var directions = new List<Vector3>();
        for (int i = 0; i < 20; i++)
        {
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
        while (elapsed < .8f)
        {
            var dt = Time.deltaTime; elapsed += dt;
            float radius = .18f + elapsed * 1.35f;
            var right = viewCamera != null ? viewCamera.transform.right : Vector3.right;
            var up = viewCamera != null ? viewCamera.transform.up : Vector3.up;
            for (int j = 0; j < 48; j++)
            {
                float angle = j * Mathf.PI * 2f / 48f;
                halo.SetPosition(j, impactPoint +
                    (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
            }
            halo.startWidth = halo.endWidth = Mathf.Max(0f, .05f * (1f - elapsed / .8f));
            impactLight.intensity = 5f * (1f - elapsed / .8f);
            for (int i = 0; i < pieces.Count; i++)
            {
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

    static Material Glow(Color color)
    {
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 2f);
        return material;
    }

    void UpdateLabels(string headline, string subline)
    {
        if (statusText != null) statusText.text = headline + "\n" + subline;
    }
}
