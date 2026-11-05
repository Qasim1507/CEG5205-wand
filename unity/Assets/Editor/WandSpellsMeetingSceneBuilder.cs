using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

/// <summary>Builds an isolated, high-fidelity visual slice for the wand pivot.</summary>
public static class WandSpellsMeetingSceneBuilder
{
    const string ScenePath = "Assets/Scenes/WandSpellsMeetingDemo.unity";
    const string Root = "Assets/WandSpells";

    [MenuItem("Tools/Wand Spells/Build Meeting Scene")]
    public static void BuildScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        EnsureFolder(Root + "/Materials");
        EnsureFolder(Root + "/Models");
        EnsureFolder(Root + "/Shaders");
        var basaltTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/BasaltArcaneFloor.png");
        if (basaltTexture == null) throw new InvalidOperationException("Arcane basalt texture has not imported.");
        var blockTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/MedievalBlocks03_Diffuse.jpg");
        var normalPath = Root + "/Textures/MedievalBlocks03_Normal.jpg";
        var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }
        var blockNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (blockTexture == null || blockNormal == null)
            throw new InvalidOperationException("CC0 stone wall textures have not imported.");

        var stone = Mat("Basalt stone", new Color(0.24f, 0.28f, 0.32f), null, basaltTexture);
        stone.mainTextureScale = new Vector2(3f, 3f);
        var wallStone = Mat("Chiseled temple stone", new Color(0.53f, 0.59f, 0.66f), null, blockTexture);
        wallStone.mainTextureScale = new Vector2(2f, 2f);
        wallStone.SetTexture("_BumpMap", blockNormal);
        wallStone.EnableKeyword("_NORMALMAP");
        var trim = Mat("Antique gold", new Color(0.65f, 0.48f, 0.23f), null);
        trim.SetFloat("_Metallic", 0.75f); trim.SetFloat("_Glossiness", 0.55f);
        var dark = Mat("Obsidian", new Color(0.028f, 0.042f, 0.063f), null);
        dark.SetFloat("_Metallic", 0.4f); dark.SetFloat("_Glossiness", 0.42f);
        var heartwood = Mat("Heartwood wand", new Color(.31f, .105f, .054f), null);
        heartwood.SetFloat("_Glossiness", .18f);
        var cyan = Mat("Arcane cyan", new Color(0.08f, 0.72f, 0.95f), new Color(0.04f, 0.52f, 0.95f) * 1.5f);
        var violet = Mat("Arcane violet", new Color(0.62f, 0.28f, 0.92f), new Color(0.40f, 0.14f, 0.75f) * 1.5f);
        var ember = Mat("Arcane ember", new Color(1f, 0.34f, 0.07f), new Color(1f, 0.15f, 0.02f) * 1.6f);
        var parchment = Mat("Warm parchment", new Color(0.91f, 0.80f, 0.55f), null);
        var lava = Mat("Moving lava", new Color(.22f, .09f, .055f),
            new Color(.36f, .055f, .012f), basaltTexture);
        var lavaShader = Shader.Find("Wand/MoltenLava");
        if (lavaShader != null)
        {
            lava.shader = lavaShader;
            lava.SetColor("_Color", new Color(.11f, .035f, .045f));
            lava.SetColor("_FlowColor", new Color(1f, .16f, .02f));
        }
        lava.mainTextureScale = new Vector2(2.5f, 2.5f);
        var rose = Mat("Rose crystal", new Color(.95f, .16f, .42f), new Color(.65f, .045f, .2f));
        var goldCrystal = Mat("Solar crystal", new Color(1f, .72f, .12f), new Color(.65f, .36f, .025f));
        foreach (var gem in new[] { ember, cyan, violet, rose, goldCrystal })
        {
            gem.SetFloat("_Metallic", .12f);
            gem.SetFloat("_Glossiness", .82f);
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.25f, 0.30f, 0.41f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.038f, 0.055f, 0.085f);
        RenderSettings.fogDensity = 0.016f;

        var world = new GameObject("ARCANE SANCTUM - environment").transform;
        var lavaFloor = Cube("Living lava lake - entire arena floor", world,
            new Vector3(0f, -0.24f, 1.7f), new Vector3(13.6f, .10f, 12.6f), lava);
        // The whole arena is a lava lake; narrow basalt stepping stones replace the old circle.
        for (int i = 0; i < 8; i++)
        {
            float z = -3.0f + i * .84f;
            Cube("Floating basalt walkway " + i, world, new Vector3(0f, -.065f, z),
                new Vector3(1.65f, .18f, .78f), stone);
            Cube("Walkway gilded joint " + i, world, new Vector3(0f, .033f, z),
                new Vector3(1.68f, .012f, .035f), trim);
        }
        Motes("Rolling heat and ash", world, new Vector3(0f, .08f, 1.7f),
            new Color(.98f, .28f, .055f, .18f), 4.8f, 3.3f, 25f);
        Cube("Rear wall", world, new Vector3(0f, 3.5f, 7.0f), new Vector3(14f, 7f, 0.35f), wallStone);
        Cube("Left wall", world, new Vector3(-6.7f, 3.5f, 2.3f), new Vector3(0.35f, 7f, 10f), wallStone);
        Cube("Right wall", world, new Vector3(6.7f, 3.5f, 2.3f), new Vector3(0.35f, 7f, 10f), wallStone);
        Cube("Upper lintel", world, new Vector3(0f, 5.0f, 4.7f), new Vector3(11.5f, 0.4f, 0.45f), trim);

        // Entrance hall. Choosing a wand opens the split gate and moves the viewer into the arena.
        var hall = new GameObject("WAND HALL - opening sequence").transform;
        Cube("Hall cobbled floor", hall, new Vector3(0f, -.16f, -9.8f),
            new Vector3(7f, .26f, 8.2f), stone);
        Cube("Hall left wall", hall, new Vector3(-3.55f, 2.45f, -9.8f),
            new Vector3(.32f, 4.9f, 8.2f), wallStone);
        Cube("Hall right wall", hall, new Vector3(3.55f, 2.45f, -9.8f),
            new Vector3(.32f, 4.9f, 8.2f), wallStone);
        Cube("Hall vaulted ceiling", hall, new Vector3(0f, 4.92f, -9.8f),
            new Vector3(7.4f, .34f, 8.2f), wallStone);
        for (int i = 0; i < 4; i++)
        {
            float z = -12.6f + i * 1.95f;
            Cube("Hall rib " + i, hall, new Vector3(0f, 4.55f, z),
                new Vector3(7f, .26f, .32f), trim);
            foreach (float x in new[] { -3.16f, 3.16f })
            {
                Cylinder("Hall column " + i, hall, new Vector3(x, 2.1f, z),
                    new Vector3(.33f, 2.1f, .33f), wallStone);
                Sphere("Hall sconce", hall, new Vector3(x * .91f, 2.35f, z),
                    new Vector3(.21f, .34f, .21f), ember);
                PointLight("Hall torchlight", new Vector3(x * .91f, 2.35f, z),
                    new Color(1f, .35f, .12f), 1.8f, 3.6f);
            }
        }
        Cube("Gate arch lintel", hall, new Vector3(0f, 4.12f, -6.05f),
            new Vector3(7.1f, .6f, .8f), trim);
        var leftGate = Cube("Opening gate - left", hall, new Vector3(-.84f, 1.88f, -6.04f),
            new Vector3(1.68f, 3.9f, .18f), dark);
        var rightGate = Cube("Opening gate - right", hall, new Vector3(.84f, 1.88f, -6.04f),
            new Vector3(1.68f, 3.9f, .18f), dark);
        for (int i = 0; i < 5; i++)
        {
            Cube("Left gate gilding", leftGate, new Vector3(0f, -.35f + i*.18f, -.55f),
                new Vector3(.75f, .012f, .08f), trim);
            Cube("Right gate gilding", rightGate, new Vector3(0f, -.35f + i*.18f, -.55f),
                new Vector3(.75f, .012f, .08f), trim);
        }
        Text("Hall title", hall, new Vector3(0f, 3.68f, -6.22f), "THE ARCANE SANCTUM",
            .04f, parchment);
        var wandDisplays = new Transform[3];
        var displayMesh = CrystalMesh();
        var displayNames = new[] { "EMBERWOOD", "FROSTSPIRE", "SUNFORGED" };
        var displayColors = new[] { ember, cyan, goldCrystal };
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 1.8f;
            Cylinder("Wand selection plinth", hall, new Vector3(x, .47f, -10.15f),
                new Vector3(.68f, .47f, .68f), dark);
            Cylinder("Selection plinth rim", hall, new Vector3(x, .96f, -10.15f),
                new Vector3(.76f, .05f, .76f), trim);
            var display = new GameObject("Pick " + displayNames[i] + " - click or press " + (i+1)).transform;
            display.SetParent(hall, false);
            display.localPosition = new Vector3(x, 1.12f, -10.15f);
            display.localRotation = Quaternion.Euler(-66f, 0f, 0f);
            display.localScale = Vector3.one * 1.45f;
            var box = display.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0f, .3f);
            box.size = new Vector3(.85f, .8f, 1.2f);
            BuildWand(display, displayNames[i] + " display", i == 0 ? heartwood : dark, trim,
                displayColors[i], displayMesh, i);
            Text("Wand name " + i, hall, new Vector3(x, 1.0f, -10.75f),
                (i+1) + "  " + displayNames[i], .012f, parchment);
            PointLight("Wand plinth glow", new Vector3(x, 1.35f, -10.15f),
                displayColors[i].color, 1.15f, 1.8f);
            wandDisplays[i] = display;
        }

        foreach (var x in new[] { -4.8f, -3.45f, 3.45f, 4.8f })
        {
            Cylinder("Fluted stone column", world, new Vector3(x, 2.2f, 4.5f),
                new Vector3(0.55f, 2.15f, 0.55f), wallStone);
            Cylinder("Column foot", world, new Vector3(x, 0.18f, 4.5f),
                new Vector3(0.85f, 0.16f, 0.85f), dark);
            Cylinder("Column capital", world, new Vector3(x, 4.35f, 4.5f),
                new Vector3(0.8f, 0.15f, 0.8f), trim);
            for (var band = 1; band <= 3; ++band)
                Cylinder("Column gilded band", world, new Vector3(x, band * 1.06f, 4.5f),
                    new Vector3(0.60f, 0.025f, 0.60f), trim);
        }

        // A tall runic alcove replaces the old circular portal.
        Cube("Runic alcove shadow", world, new Vector3(0f, 2.25f, 6.68f),
            new Vector3(2.55f, 3.9f, .055f), dark);
        foreach (float x in new[] { -1.45f, 1.45f })
            Cube("Alcove stone jamb", world, new Vector3(x, 2.4f, 6.46f),
                new Vector3(.40f, 4.6f, .38f), wallStone);
        for (int i = 0; i < 11; i++)
        {
            float a = Mathf.PI * i / 10f;
            var block = Cube("Alcove arch stone " + i, world,
                new Vector3(Mathf.Cos(a) * 1.45f, 4.53f + Mathf.Sin(a) * .72f, 6.45f),
                new Vector3(.42f, .24f, .42f), wallStone);
            block.rotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
        }
        var alcoveCrystal = new GameObject("Floating sanctuary keystone");
        alcoveCrystal.transform.SetParent(world, false);
        alcoveCrystal.transform.localPosition = new Vector3(0f, 2.66f, 6.32f);
        alcoveCrystal.transform.localScale = new Vector3(.82f, 1.15f, .46f);
        alcoveCrystal.AddComponent<MeshFilter>().sharedMesh = CrystalMesh();
        alcoveCrystal.AddComponent<MeshRenderer>().sharedMaterial = cyan;
        Motes("Alcove drifting sparks", world, new Vector3(0f, 2.45f, 6.27f),
            cyan.color, 1.4f, 1.5f, 15f);

        foreach (var x in new[] { -2.8f, 2.8f })
        {
            var pedestal = Cylinder("Arcane brazier plinth", world, new Vector3(x, 0.65f, 2.65f),
                new Vector3(0.66f, 0.62f, 0.66f), dark);
            Cylinder("Brazier gilded lip", pedestal, new Vector3(0f, 0.68f, 0f),
                new Vector3(0.85f, 0.04f, 0.85f), trim);
            Sphere("Brazier contained flame", world, new Vector3(x, 1.43f, 2.65f),
                new Vector3(0.35f, 0.58f, 0.35f), ember);
            Motes("Brazier flame sparks", world, new Vector3(x, 1.46f, 2.65f),
                ember.color, 0.26f, 0.55f, 11f);
            PointLight("Brazier warm light", new Vector3(x, 1.5f, 2.65f), new Color(1f, 0.35f, 0.13f), 3.2f, 5f);
        }

        var crystalMesh = CrystalMesh();
        var targets = new Transform[5];
        var renderers = new Renderer[5];
        var colors = new[] { ember, cyan, violet, rose, goldCrystal };
        var names = new[] { "EMBER", "FROST", "STORM", "ARCANE", "SHATTER" };
        for (var i = 0; i < 5; ++i)
        {
            var x = (i - 2) * 1.04f;
            var altar = new GameObject("Spell target " + (i + 1) + " - " + names[i]).transform;
            altar.SetParent(world, false);
            altar.position = new Vector3(x, 0f, 3.55f);
            Cylinder("Altar base", altar, new Vector3(0f, 0.37f, 0f), new Vector3(0.57f, 0.36f, 0.57f), dark);
            Cylinder("Altar rim", altar, new Vector3(0f, 0.74f, 0f), new Vector3(0.68f, 0.045f, 0.68f), trim);
            var crystal = new GameObject("Faceted spell crystal");
            crystal.transform.SetParent(altar, false);
            crystal.transform.localPosition = new Vector3(0f, 1.19f, 0f);
            crystal.transform.localScale = Vector3.one * 0.58f;
            crystal.AddComponent<MeshFilter>().sharedMesh = crystalMesh;
            renderers[i] = crystal.AddComponent<MeshRenderer>();
            renderers[i].sharedMaterial = colors[i];
            var targetCollider = crystal.AddComponent<CapsuleCollider>();
            targetCollider.direction = 1;
            targetCollider.radius = .72f;
            targetCollider.height = 2.2f;
            targets[i] = crystal.transform;
            var core = new GameObject("Refracted luminous crystal core");
            core.transform.SetParent(crystal.transform, false);
            core.transform.localScale = new Vector3(.55f,.84f,.55f);
            core.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);
            core.AddComponent<MeshFilter>().sharedMesh = crystalMesh;
            core.AddComponent<MeshRenderer>().sharedMaterial = colors[i];
            for (int shard = 0; shard < 4; shard++)
            {
                float angle = shard * Mathf.PI * .5f;
                var fragment = new GameObject("Orbiting crystal splinter " + shard).transform;
                fragment.SetParent(crystal.transform, false);
                fragment.localPosition = new Vector3(Mathf.Cos(angle) * .63f, .1f * (shard % 2),
                    Mathf.Sin(angle) * .63f);
                fragment.localRotation = Quaternion.Euler(12f, shard * 90f, 20f);
                fragment.localScale = Vector3.one * .24f;
                fragment.gameObject.AddComponent<MeshFilter>().sharedMesh = crystalMesh;
                fragment.gameObject.AddComponent<MeshRenderer>().sharedMaterial = colors[i];
            }
            PointLight("Crystal glow", new Vector3(x, 1.36f, 3.55f), colors[i].color, 1.25f, 1.7f);
        }

        var aimMarker = new GameObject("Target lock - appears on aimed crystal").transform;
        aimMarker.SetParent(world, false);
        Ring(aimMarker, "Target lock outer ring", Vector3.zero, .48f, .012f, 32, cyan);
        Ring(aimMarker, "Target lock inner ring", Vector3.zero, .39f, .006f, 32, trim);
        aimMarker.gameObject.SetActive(false);

        // The XR origin is independent of the old robot/surgery scenes.
        var rig = new GameObject("XR Origin - Wand Spells");
        rig.transform.position = new Vector3(0f, 0f, -13f);
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(rig.transform, false);
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(offset.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.028f, 0.044f, 0.075f);
        camera.nearClipPlane = 0.04f;
        camera.farClipPlane = 40f;
        camera.fieldOfView = 72f;
        camera.stereoTargetEye = StereoTargetEyeMask.Both;
        cameraObject.AddComponent<AudioListener>();
        var tracked = cameraObject.AddComponent<TrackedPoseDriver>();
        tracked.positionInput = new InputActionProperty(new InputAction("Head position",
            binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        tracked.rotationInput = new InputActionProperty(new InputAction("Head rotation",
            binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        tracked.trackingStateInput = new InputActionProperty(new InputAction("Head state",
            binding: "<XRHMD>/trackingState", expectedControlType: "Integer"));
        var xr = rig.AddComponent<XROrigin>();
        xr.Camera = camera;
        xr.CameraFloorOffsetObject = offset;
        xr.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        var wand = new GameObject("Wand mount - choose one of three styles").transform;
        wand.SetParent(cameraObject.transform, false);
        wand.localPosition = new Vector3(0.27f, -0.28f, 0.48f);
        wand.localRotation = Quaternion.Euler(-11f, -15f, 0f);
        var wands = new Transform[3];
        wands[0] = BuildWand(wand, "EMBERWOOD - gnarled staff", heartwood, trim, ember, crystalMesh, 0);
        wands[1] = BuildWand(wand, "FROSTSPIRE - crystal scepter", dark, cyan, cyan, crystalMesh, 1);
        wands[2] = BuildWand(wand, "SUNFORGED - gilded wand", trim, dark, goldCrystal, crystalMesh, 2);
        wands[1].gameObject.SetActive(false);
        wands[2].gameObject.SetActive(false);
        var tip = new GameObject("Spell emission point").transform;
        tip.SetParent(wand, false);
        tip.localPosition = new Vector3(0f, 0f, 0.57f);
        PointLight("Wand tip glow", wand.TransformPoint(new Vector3(0f, 0f, 0.48f)),
            cyan.color, 1.5f, 1.4f, wand);

        var status = Text("Top left spell HUD", cameraObject.transform,
            new Vector3(-.82f, .48f, 1.35f), "CHOOSE A WAND", .008f, cyan);
        status.anchor = TextAnchor.UpperLeft; status.alignment = TextAlignment.Left;
        var score = Text("Top right progress HUD", cameraObject.transform,
            new Vector3(.82f, .48f, 1.35f), "0 / 5 CRYSTALS", .008f, parchment);
        score.anchor = TextAnchor.UpperRight; score.alignment = TextAlignment.Right;
        var choice = Text("Wand choice prompt", cameraObject.transform,
            new Vector3(0f, .29f, 1.45f),
            "CLICK A WAND  OR  PRESS 1 / 2 / 3",
            .0065f, parchment);
        var crosshair = new GameObject("Mouse aim crosshair - centered on actual shot ray").transform;
        crosshair.SetParent(cameraObject.transform, false);
        crosshair.localPosition = new Vector3(0f, 0f, 1.2f);
        Ring(crosshair, "Crosshair ring", Vector3.zero, .025f, .0016f, 24, cyan);
        Cube("Crosshair center", crosshair, Vector3.zero,
            new Vector3(.005f, .005f, .003f), parchment);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * .5f;
            var tick = Cube("Crosshair tick " + i, crosshair,
                new Vector3(Mathf.Cos(angle)*.042f, Mathf.Sin(angle)*.042f, 0f),
                i % 2 == 0 ? new Vector3(.013f,.0025f,.002f) :
                    new Vector3(.0025f,.013f,.002f), trim);
        }
        crosshair.gameObject.SetActive(false);
        var controller = new GameObject("Spell director - keyboard mock and UDP model input")
            .AddComponent<WandSpellDemoController>();
        controller.wand = wand;
        controller.wandTip = tip;
        controller.wandStyles = wands;
        controller.wandDisplays = wandDisplays;
        controller.targets = targets;
        controller.targetCrystals = renderers;
        controller.statusText = status;
        controller.scoreText = score;
        controller.choiceText = choice;
        controller.lavaRenderer = lavaFloor.GetComponent<Renderer>();
        controller.sceneRig = rig.transform;
        controller.entranceDoors = new[] { leftGate, rightGate };
        controller.aimMarker = aimMarker;
        controller.crosshair = crosshair;
        PointLight("Portal cyan bounce", new Vector3(0f, 2.1f, 6.0f),
            new Color(0.14f, 0.56f, 1f), 2.6f, 6f);
        var key = new GameObject("Warm key light").AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1f, 0.81f, 0.60f);
        key.intensity = 1.1f;
        key.transform.rotation = Quaternion.Euler(48f, -24f, 0f);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Could not save wand meeting scene.");
        AssetDatabase.SaveAssets();
        Debug.Log("WAND_MEETING_SCENE_SAVED " + ScenePath);
    }

    [MenuItem("Tools/Wand Spells/Build Quest APK")]
    public static void BuildAndroid()
    {
        QuestViewSetup.ConfigureProject();
        if (!System.IO.File.Exists(ScenePath)) BuildScene();
        var previousId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var previousName = PlayerSettings.productName;
        var output = System.IO.Path.GetFullPath("Builds/WandSpellsMeetingDemo.apk");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        try
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                "com.ceg5205.wandspellsdemo");
            PlayerSettings.productName = "Wand Spells MR";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Wand Quest APK failed: " + report.summary.result);
            Debug.Log("WAND_QUEST_APK_BUILT " + output);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousId);
            PlayerSettings.productName = previousName;
        }
    }

    static void EnsureFolder(string path)
    {
        var pieces = path.Split('/');
        var current = pieces[0];
        for (var i = 1; i < pieces.Length; ++i)
        {
            var next = current + "/" + pieces[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, pieces[i]);
            current = next;
        }
    }

    static Transform BuildWand(Transform parent, string name, Material shaft, Material accent,
        Material focus, Mesh crystalMesh, int style)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        if (style == 0)
        {
            Vector3 previous = Vector3.zero;
            for (int i = 1; i <= 7; i++)
            {
                float z = i * .073f;
                var next = new Vector3(Mathf.Sin(i * .9f) * .018f, Mathf.Cos(i * .8f) * .018f, z);
                Rod("Twisted emberwood", root, previous, next, .039f - i * .002f, shaft);
                previous = next;
            }
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                Rod("Claw around ember", root, new Vector3(Mathf.Cos(a)*.06f, Mathf.Sin(a)*.06f,.40f),
                    new Vector3(Mathf.Cos(a)*.11f, Mathf.Sin(a)*.11f,.53f), .012f, accent);
            }
            Sphere("Captured ember", root, new Vector3(0f,0f,.52f), new Vector3(.10f,.10f,.12f), focus);
        }
        else if (style == 1)
        {
            Rod("Cobalt staff",root,Vector3.zero,new Vector3(0f,0f,.47f),.032f,shaft);
            for (int i=0;i<6;i++)
            {
                float z=.09f+i*.07f;
                var ring= Cylinder("Frost spiral inlay",root,new Vector3(0f,0f,z),
                    new Vector3(.044f,.012f,.044f),accent,Quaternion.Euler(90f,0f,0f));
                ring.rotation*=Quaternion.Euler(0f,0f,i*26f);
            }
            var jewel=new GameObject("Tall ice crystal"); jewel.transform.SetParent(root,false);
            jewel.transform.localPosition=new Vector3(0f,0f,.55f);
            jewel.transform.localRotation=Quaternion.Euler(90f,0f,0f);
            jewel.transform.localScale=new Vector3(.17f,.32f,.17f);
            jewel.AddComponent<MeshFilter>().sharedMesh=crystalMesh;
            jewel.AddComponent<MeshRenderer>().sharedMaterial=focus;
        }
        else
        {
            Rod("Sunforged shaft",root,Vector3.zero,new Vector3(0f,0f,.49f),.028f,shaft);
            for(int i=0;i<5;i++)
                Cylinder("Dark jeweled band",root,new Vector3(0f,0f,.08f+i*.085f),
                    new Vector3(.048f,.010f,.048f),accent,Quaternion.Euler(90f,0f,0f));
            Sphere("Golden sun center",root,new Vector3(0f,0f,.52f),Vector3.one*.10f,focus);
            for(int i=0;i<10;i++)
            {
                float a=i*Mathf.PI*2f/10f;
                Rod("Sun ray",root,
                    new Vector3(Mathf.Cos(a)*.06f,Mathf.Sin(a)*.06f,.52f),
                    new Vector3(Mathf.Cos(a)*.17f,Mathf.Sin(a)*.17f,.52f),.012f,shaft);
            }
        }
        return root;
    }

    static Transform Rod(string name, Transform parent, Vector3 from, Vector3 to,
        float radius, Material material)
    {
        var rod=Cylinder(name,parent,(from+to)*.5f,
            new Vector3(radius,Vector3.Distance(from,to)*.5f,radius),material);
        rod.up=(to-from).normalized;
        return rod;
    }

    static Material Mat(string name, Color color, Color? emission, Texture texture = null)
    {
        var path = Root + "/Materials/" + name.Replace(' ', '_') + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.name = name;
        material.color = color;
        material.mainTexture = texture;
        material.SetFloat("_Glossiness", 0.35f);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Transform Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        => Shape(PrimitiveType.Cube, name, parent, position, scale, material);
    static Transform Cylinder(string name, Transform parent, Vector3 position, Vector3 scale,
        Material material, Quaternion? rotation = null)
        => Shape(PrimitiveType.Cylinder, name, parent, position, scale, material, rotation);
    static Transform Sphere(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        => Shape(PrimitiveType.Sphere, name, parent, position, scale, material);

    static Transform Shape(PrimitiveType type, string name, Transform parent, Vector3 position,
        Vector3 scale, Material material, Quaternion? rotation = null)
    {
        var shape = GameObject.CreatePrimitive(type);
        shape.name = name;
        shape.transform.SetParent(parent, false);
        shape.transform.localPosition = position;
        shape.transform.localRotation = rotation ?? Quaternion.identity;
        shape.transform.localScale = scale;
        shape.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(shape.GetComponent<Collider>());
        return shape.transform;
    }

    static TextMesh Text(string name, Transform parent, Vector3 position, string content,
        float size, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.identity;
        var text = go.AddComponent<TextMesh>();
        var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Cinzel.ttf");
        if (font != null)
        {
            text.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        text.text = content;
        text.characterSize = size;
        text.fontSize = 64;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = material.color;
        return text;
    }

    static Light PointLight(string name, Vector3 position, Color color, float intensity,
        float range, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        return light;
    }

    static void Motes(string name, Transform parent, Vector3 position, Color color,
        float radius, float lifetime, float rate)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var particles = go.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = true;
        main.startLifetime = lifetime;
        main.startSpeed = 0.25f;
        main.startSize = 0.035f;
        main.startColor = color;
        main.maxParticles = 120;
        var emission = particles.emission;
        emission.rateOverTime = rate;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.y = 0.3f;
    }

    static void Ring(Transform parent, string name, Vector3 center, float radius,
        float thickness, int segments, Material material)
    {
        var ring = new GameObject(name).transform;
        ring.SetParent(parent, false);
        for (var i = 0; i < segments; ++i)
        {
            var a = i * Mathf.PI * 2f / segments;
            var b = (i + 1) * Mathf.PI * 2f / segments;
            var p = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            var q = center + new Vector3(Mathf.Cos(b) * radius, Mathf.Sin(b) * radius, 0f);
            var segment = Cylinder("Gilded arc", ring, (p + q) * 0.5f,
                new Vector3(thickness, Vector3.Distance(p, q) * 0.53f, thickness), material);
            segment.up = (q - p).normalized;
        }
    }

    static Mesh CrystalMesh()
    {
        const string path = Root + "/Models/FacetedSpellCrystalV2.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        const int sides = 7;
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        void Face(Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }
        var rings = new Vector3[4, sides];
        float[] radii = { .27f, .43f, .35f, .20f };
        float[] heights = { .54f, .14f, -.36f, -.64f };
        for (int r = 0; r < 4; r++)
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                float irregularity = 1f + .10f * Mathf.Sin(i * 3.3f + r * 1.7f);
                rings[r, i] = new Vector3(Mathf.Cos(a) * radii[r] * irregularity,
                    heights[r] + .045f * Mathf.Sin(i * 2.7f + r),
                    Mathf.Sin(a) * radii[r] * irregularity);
            }
        Vector3 top = new Vector3(.035f, 1.02f, -.015f);
        Vector3 bottom = new Vector3(-.025f, -.98f, .02f);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Face(top, rings[0,j], rings[0,i]);
            for (int r = 0; r < 3; r++)
            {
                Face(rings[r,i], rings[r,j], rings[r+1,i]);
                Face(rings[r,j], rings[r+1,j], rings[r+1,i]);
            }
            Face(bottom, rings[3,i], rings[3,j]);
        }
        var mesh = new Mesh { name = "Irregular sharp faceted crystal" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
