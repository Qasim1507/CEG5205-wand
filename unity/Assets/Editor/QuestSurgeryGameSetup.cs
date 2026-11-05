using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Creates a Quest heart-replacement game from the tested hand-control scene.</summary>
public static class QuestSurgeryGameSetup
{
    const string SourceScene = "Assets/Scenes/QuestHandRobotControl.unity";
    const string GameScene = "Assets/Scenes/QuestSurgeryGame.unity";
    const string Output = "Builds/QuestSurgeryGame.mac.apk";
    const string MaterialFolder = "Assets/Materials/SurgeryGame";
    const float ChestX = 0.29f;
    const float ChestZ = -0.16f;

    [MenuItem("Tools/Robotics/Quest/Build Surgery Game Scene")]
    public static void BuildScene()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play mode before creating the surgery game scene.");

        EnsureMaterialFolder();
        var drape = MaterialAsset("SterileDrape", new Color(0.025f, 0.31f, 0.36f));
        var cavityMaterial = MaterialAsset("OpenChestInterior", new Color(0.19f, 0.027f, 0.043f));
        var incisionMaterial = MaterialAsset("IncisionEdge", new Color(0.57f, 0.23f, 0.25f));
        var ribMaterial = MaterialAsset("RibBone", new Color(0.88f, 0.77f, 0.65f));
        var oldHeartMaterial = MaterialAsset("NativeHeart", new Color(0.45f, 0.065f, 0.09f));
        var donorHeartMaterial = MaterialAsset("DonorHeart", new Color(0.70f, 0.12f, 0.15f));
        var arteryMaterial = MaterialAsset("Artery", new Color(0.78f, 0.23f, 0.23f));
        var veinMaterial = MaterialAsset("Vein", new Color(0.20f, 0.33f, 0.54f));
        var steelMaterial = MaterialAsset("InstrumentSteel", new Color(0.70f, 0.78f, 0.81f));
        var trayMaterial = MaterialAsset("SpecimenTray", new Color(0.08f, 0.56f, 0.68f));
        var wallMaterial = MaterialAsset("OperatingRoomWall", new Color(0.78f, 0.88f, 0.89f));
        var floorMaterial = MaterialAsset("OperatingRoomFloor", new Color(0.28f, 0.43f, 0.46f));
        var bedMaterial = MaterialAsset("OperatingBed", new Color(0.16f, 0.24f, 0.30f));
        var sheetMaterial = MaterialAsset("BedSheet", new Color(0.73f, 0.91f, 0.94f));
        var patientMaterial = MaterialAsset("PracticePatient", new Color(0.67f, 0.48f, 0.36f));
        var patientSkin = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Surgery/Models/PracticePatientSkin.png");
        if (patientSkin == null)
            throw new InvalidOperationException("The MakeHuman patient skin texture has not imported.");
        patientMaterial.mainTexture = patientSkin;
        patientMaterial.color = Color.white;
        patientMaterial.SetFloat("_Glossiness", 0.12f);
        EditorUtility.SetDirty(patientMaterial);
        var forcepsMaterial = MaterialAsset("SurgicalSteel", new Color(0.72f, 0.78f, 0.83f));
        var monitorMaterial = MaterialAsset("VitalsScreen", new Color(0.02f, 0.12f, 0.15f));

        var scene = EditorSceneManager.OpenScene(SourceScene);
        var robotControl = UnityEngine.Object.FindAnyObjectByType<QuestHandRobotController>();
        var rig = GameObject.Find("XR Origin - Quest viewing");
        var camera = rig != null ? rig.GetComponentInChildren<Camera>(true) : null;
        if (robotControl == null || camera == null)
            throw new InvalidOperationException("The hand-control scene is missing its arm controller or XR camera.");

        // ROS tutorial controls are not part of the self-contained Quest game.
        var publisher = GameObject.Find("Publisher");
        if (publisher != null)
            publisher.SetActive(false);

        var oldWorkspace = GameObject.Find("Sorting Workspace - edit boxes and bins here");
        if (oldWorkspace != null)
            UnityEngine.Object.DestroyImmediate(oldWorkspace);

        // Bedside position keeps the base off the torso while all three task
        // locations stay within the short Niryo arm's approximate workspace.
        var robot = GameObject.Find("niryo_one");
        if (robot == null)
            throw new InvalidOperationException("The Niryo arm is missing from the hand-control scene.");
        robot.transform.position = new Vector3(0.25f, 0f, -0.52f);
        foreach (var component in robot.GetComponents<MonoBehaviour>())
        {
            if (component == null || component.GetType().Name != "UrdfRobot") continue;
            var robotSettings = new SerializedObject(component);
            var exceptions = robotSettings.FindProperty("collisionExceptions");
            if (exceptions == null) continue;
            exceptions.ClearArray();
            robotSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        ConfigureSurgicalForceps(robotControl, forcepsMaterial);

        var world = new GameObject("Surgery training field").transform;
        Cube("Operating room floor", world, new Vector3(0f, -0.04f, -0.1f),
            new Vector3(5f, 0.06f, 5f), floorMaterial, false);
        Cube("Back wall", world, new Vector3(0f, 1.5f, -2.0f),
            new Vector3(5f, 3f, 0.05f), wallMaterial, false);
        Cube("Side wall", world, new Vector3(-2.3f, 1.5f, -0.1f),
            new Vector3(0.05f, 3f, 4f), wallMaterial, false);
        Cube("Ceiling light panel", world, new Vector3(0.1f, 2.7f, -0.1f),
            new Vector3(0.9f, 0.02f, 0.45f), sheetMaterial, false);

        // CC0 MakeHuman body mesh gives the practice cadaver continuous human
        // anatomy. It is decorative so it cannot destabilize the robot physics.
        Cube("Operating bed frame", world, new Vector3(0.05f, 0.53f, -0.16f),
            new Vector3(1.85f, 0.13f, 1.08f), bedMaterial, false);
        Cube("Operating bed mattress", world, new Vector3(0.05f, 0.608f, -0.16f),
            new Vector3(1.81f, 0.028f, 1.04f), sheetMaterial, false);
        Cube("Far bed rail", world, new Vector3(0.05f, 0.68f, 0.39f),
            new Vector3(1.73f, 0.025f, 0.025f), bedMaterial, false);
        Cube("Pillow", world, new Vector3(0.77f, 0.64f, -0.16f),
            new Vector3(0.22f, 0.035f, 0.28f), sheetMaterial, false);
        var patientPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Surgery/Models/PracticePatientBody.obj");
        if (patientPrefab == null)
            throw new InvalidOperationException("The CC0 practice patient mesh has not imported yet. Refresh Assets.");
        var patientMesh = patientPrefab.GetComponentInChildren<MeshFilter>().sharedMesh;
        var patient = new GameObject("Practice cadaver - MakeHuman body mesh");
        patient.transform.SetParent(world, false);
        patient.transform.position = new Vector3(0.05f, 0.64f, -0.16f);
        patient.transform.rotation = Quaternion.AngleAxis(-120f, Vector3.one.normalized);
        patient.transform.localScale = new Vector3(0.09f, 0.09f, 0.05f);
        patient.AddComponent<MeshFilter>().sharedMesh = OpenChestMesh(patientMesh, patient.transform);
        var patientRenderer = patient.AddComponent<MeshRenderer>();
        patientRenderer.sharedMaterial = patientMaterial;
        patientRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        patientRenderer.receiveShadows = false;
        // Trace the original skin before placing the incision. The MakeHuman
        // torso is curved, so a flat disk at a fixed height visibly floats.
        var skin = new TorsoSurfaceSampler(patientMesh, patient.transform);
        var chestSurfaceY = skin.SurfaceY(ChestX, ChestZ);
        var chest = new GameObject("Open chest surgical field").transform;
        chest.SetParent(world, false);
        chest.position = new Vector3(ChestX, chestSurfaceY - 0.085f, ChestZ);
        Ellipsoid("Recessed chest cavity", chest,
            new Vector3(ChestX, chestSurfaceY - 0.068f, ChestZ),
            new Vector3(0.206f, 0.010f, 0.140f), cavityMaterial);
        var rim = new GameObject("Skin fitted incision rim");
        rim.transform.SetParent(chest, false);
        rim.AddComponent<MeshFilter>().sharedMesh = IncisionRimMesh(skin, chest);
        rim.AddComponent<MeshRenderer>().sharedMaterial = incisionMaterial;
        var upperDrape = new GameObject("Fenestrated upper-body surgical drape");
        upperDrape.transform.SetParent(world, false);
        upperDrape.AddComponent<MeshFilter>().sharedMesh = FenestratedDrapeMesh(skin);
        upperDrape.AddComponent<MeshRenderer>().sharedMaterial = drape;
        for (var i = 0; i < 4; ++i)
        {
            var ribX = chest.position.x - 0.062f + i * 0.042f;
            SurfaceSegment("Left retracted rib " + i, chest, skin,
                new Vector2(ribX, -0.222f), new Vector2(ribX - 0.01f, -0.192f),
                0.004f, ribMaterial);
            SurfaceSegment("Right retracted rib " + i, chest, skin,
                new Vector2(ribX, -0.098f), new Vector2(ribX - 0.01f, -0.128f),
                0.004f, ribMaterial);
        }
        SurfaceSegment("Left steel retractor", chest, skin,
            new Vector2(ChestX - 0.126f, -0.16f), new Vector2(ChestX - 0.126f, -0.10f),
            0.003f, steelMaterial);
        SurfaceSegment("Right steel retractor", chest, skin,
            new Vector2(ChestX + 0.126f, -0.22f), new Vector2(ChestX + 0.126f, -0.16f),
            0.003f, steelMaterial);
        Cube("Vitals monitor", world, new Vector3(0.73f, 1.27f, 0.3f),
            new Vector3(0.30f, 0.22f, 0.035f), bedMaterial, false);
        Cube("Vitals display", world, new Vector3(0.73f, 1.27f, 0.277f),
            new Vector3(0.27f, 0.18f, 0.006f), monitorMaterial, false);
        Cube("Monitor stand", world, new Vector3(0.73f, 0.73f, 0.32f),
            new Vector3(0.028f, 0.9f, 0.028f), bedMaterial, false);
        Cube("Sterile instrument surface", world, new Vector3(0.25f, 0.735f, -0.76f),
            new Vector3(0.95f, 0.035f, 0.32f), forcepsMaterial, false);
        // Existing graspable rigidbodies retain the Box prefix required by
        // QuestHandRobotController, but their cube renderers become heart models.
        var oldHeart = MakeHeart("Box 1 - native heart", "Box 1 - blue",
            new Vector3(ChestX, chestSurfaceY + 0.010f, ChestZ), oldHeartMaterial,
            arteryMaterial, veinMaterial);
        var donorHeart = MakeHeart("Box 2 - donor heart", "Box 2 - orange",
            new Vector3(0.53f, 0.81f, -0.76f), donorHeartMaterial,
            arteryMaterial, veinMaterial);
        var unused = GameObject.Find("Box 3 - green");
        if (unused != null)
            UnityEngine.Object.DestroyImmediate(unused);

        var transplantTarget = new GameObject("Empty heart socket");
        transplantTarget.transform.SetParent(chest, false);
        transplantTarget.transform.position = new Vector3(ChestX, chestSurfaceY + 0.010f, ChestZ);
        var socket = transplantTarget.AddComponent<SphereCollider>();
        socket.isTrigger = true;
        socket.radius = 0.058f;

        var discard = Tray("Native heart disposal tray", world,
            new Vector3(-0.04f, 0.76f, -0.76f),
            trayMaterial, bedMaterial);
        var donor = Tray("Donor heart sterile tray", world,
            new Vector3(0.53f, 0.76f, -0.76f),
            trayMaterial, bedMaterial);
        // The donor sits above its tray; the scene shows a clear source and goal.
        donorHeart.position = new Vector3(0.53f, 0.81f, -0.76f);
        oldHeart.isKinematic = true;
        oldHeart.useGravity = false;
        donorHeart.isKinematic = true;
        donorHeart.useGravity = false;

        var instruction = camera.transform.Find("Hand tracking instructions");
        if (instruction != null)
            instruction.gameObject.SetActive(false);
        var banner = Text("Procedure banner", camera.transform,
            new Vector3(0f, 0.39f, 1.2f), 48, 0.008f,
            new Color(0.90f, 1f, 1f), TextAnchor.UpperCenter);
        var hud = Text("Procedure score and status", camera.transform,
            new Vector3(-0.46f, 0.26f, 1.2f), 46, 0.007f,
            Color.white, TextAnchor.UpperLeft);

        var gameObject = new GameObject("Heart replacement game rules");
        var game = gameObject.AddComponent<SurgeryGameController>();
        var fields = new SerializedObject(game);
        fields.FindProperty("robotControl").objectReferenceValue = robotControl;
        fields.FindProperty("nativeHeart").objectReferenceValue = oldHeart;
        fields.FindProperty("donorHeart").objectReferenceValue = donorHeart;
        fields.FindProperty("discardTray").objectReferenceValue = discard;
        fields.FindProperty("heartSocket").objectReferenceValue = socket;
        fields.FindProperty("hud").objectReferenceValue = hud;
        fields.FindProperty("banner").objectReferenceValue = banner;
        fields.ApplyModifiedPropertiesWithoutUndo();

        SurgerySceneRefinement.Apply();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, GameScene))
            throw new InvalidOperationException("Could not save " + GameScene);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.All(item => item.path != GameScene))
        {
            scenes.Add(new EditorBuildSettingsScene(GameScene, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Surgery game scene saved: " + GameScene);
    }

    [MenuItem("Tools/Robotics/Quest/Build Surgery Game APK")]
    public static void BuildAndroid()
    {
        QuestViewSetup.ConfigureProject();
        if (!System.IO.File.Exists(GameScene))
            BuildScene();
        var previousId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var previousName = PlayerSettings.productName;
        var output = System.IO.Path.GetFullPath(Output);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        try
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                "com.ceg5205.questsurgerygame");
            PlayerSettings.productName = "Robot Surgery Trainer";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GameScene },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Surgery game APK build failed: " + report.summary.result);
            Debug.Log("Surgery game APK: " + output);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousId);
            PlayerSettings.productName = previousName;
        }
    }

    static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "SurgeryGame");
    }

    static Material MaterialAsset(string name, Color color)
    {
        var path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;
        material = new Material(Shader.Find("Standard")) { color = color };
        material.SetFloat("_Metallic", name.StartsWith("Fragment") ? 0.75f : 0.05f);
        material.SetFloat("_Glossiness", name.StartsWith("Fragment") ? 0.68f : 0.22f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void ConfigureSurgicalForceps(QuestHandRobotController controller, Material steel)
    {
        var robot = GameObject.Find("niryo_one");
        const string hand = "world/base_link/shoulder_link/arm_link/elbow_link/forearm_link/wrist_link/hand_link";
        var mount = robot != null ? robot.transform.Find(hand + "/tool_link") : null;
        var claw = mount != null ? mount.Find("gripper_base") : null;
        var left = claw != null ? claw.Find("servo_head/control_rod_left/left_gripper") : null;
        var right = claw != null ? claw.Find("servo_head/control_rod_right/right_gripper") : null;
        if (mount == null || claw == null || left == null || right == null)
            throw new InvalidOperationException("Could not locate Niryo tool and gripper joints.");
        var leftRenderer = left.GetComponentInChildren<Renderer>();
        var rightRenderer = right.GetComponentInChildren<Renderer>();
        if (leftRenderer == null || rightRenderer == null)
            throw new InvalidOperationException("The original gripper meshes are missing.");

        // Calibrate the forceps tip once from the tested cube scene. The old
        // articulated gripper is removed below so its physics cannot shake the
        // wrist or the visible instrument in the surgery scene.
        var leftCenter = mount.InverseTransformPoint(leftRenderer.bounds.center);
        var rightCenter = mount.InverseTransformPoint(rightRenderer.bounds.center);
        var jawSeparation = rightCenter - leftCenter;

        var settings = new SerializedObject(controller);
        settings.FindProperty("graspRadius").floatValue = 0.05f;
        settings.FindProperty("graspJawAxisTolerance").floatValue = 0.025f;
        settings.FindProperty("surgeryPrecisionMode").boolValue = false;
        settings.FindProperty("carryWithoutParenting").boolValue = true;
        settings.FindProperty("useVirtualSurgicalJaws").boolValue = true;
        settings.FindProperty("surgicalToolMount").objectReferenceValue = mount;
        settings.FindProperty("surgicalJawMidLocal").vector3Value = (leftCenter + rightCenter) * 0.5f;
        settings.FindProperty("surgicalJawAxisLocal").vector3Value = jawSeparation.normalized;
        settings.FindProperty("surgicalOpenJawWidth").floatValue = jawSeparation.magnitude;
        settings.FindProperty("surgicalClosedJawWidth").floatValue = 0.006f;
        // Restore every tested hand-to-motor gain from the cube version.
        settings.FindProperty("baseDegreesPerMeter").floatValue = 480f;
        settings.FindProperty("shoulderDegreesPerMeter").floatValue = 360f;
        settings.FindProperty("elbowDegreesPerMeter").floatValue = 360f;
        settings.FindProperty("wristDegreesPerDegree").floatValue = 1f;
        settings.FindProperty("maximumJointSpeedDegreesPerSecond").floatValue = 180f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        // The turquoise fork is rendered by tool_link itself on this URDF, not
        // just by gripper_base. Hide every original tool mesh after calibration.
        foreach (var renderer in mount.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
        foreach (var collider in mount.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        UnityEngine.Object.DestroyImmediate(claw.gameObject);

        var visualObject = new GameObject("Slim surgical forceps");
        visualObject.transform.SetParent(mount, false);
        var visual = visualObject.AddComponent<SurgicalForcepsVisual>();
        var fields = new SerializedObject(visual);
        fields.FindProperty("controller").objectReferenceValue = controller;
        fields.FindProperty("mount").objectReferenceValue = mount;
        fields.FindProperty("shaft").objectReferenceValue = ForcepsPart("Steel shaft", PrimitiveType.Cylinder, steel, visualObject.transform);
        fields.FindProperty("leftTine").objectReferenceValue = ForcepsPart("Left forceps tine", PrimitiveType.Cylinder, steel, visualObject.transform);
        fields.FindProperty("rightTine").objectReferenceValue = ForcepsPart("Right forceps tine", PrimitiveType.Cylinder, steel, visualObject.transform);
        fields.FindProperty("leftTip").objectReferenceValue = ForcepsPart("Left fine tip", PrimitiveType.Sphere, steel, visualObject.transform);
        fields.FindProperty("rightTip").objectReferenceValue = ForcepsPart("Right fine tip", PrimitiveType.Sphere, steel, visualObject.transform);
        fields.ApplyModifiedPropertiesWithoutUndo();
        foreach (Transform part in visualObject.transform)
        {
            part.position = mount.position;
            part.localScale = Vector3.one * 0.004f;
        }
        visual.SetPreview(mount.TransformPoint(leftCenter), mount.TransformPoint(rightCenter));
    }

    static Transform ForcepsPart(string name, PrimitiveType type, Material material, Transform parent)
    {
        var item = GameObject.CreatePrimitive(type);
        item.name = name;
        item.transform.SetParent(parent, false);
        item.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
        return item.transform;
    }

    static Mesh OpenChestMesh(Mesh original, Transform patient)
    {
        const string path = "Assets/Surgery/Models/PracticePatientOpenChestV2.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var mesh = UnityEngine.Object.Instantiate(original);
        mesh.name = "Practice patient with open chest";
        var vertices = original.vertices;
        var source = original.triangles;
        var kept = new System.Collections.Generic.List<int>(source.Length);
        for (var i = 0; i < source.Length; i += 3)
        {
            var a = patient.TransformPoint(vertices[source[i]]);
            var b = patient.TransformPoint(vertices[source[i + 1]]);
            var c = patient.TransformPoint(vertices[source[i + 2]]);
            var center = (a + b + c) / 3f;
            // Foot details sit entirely under the sterile drape. Omit only
            // that covered end of the skin mesh so toes cannot show through
            // the cloth at oblique Quest viewing angles.
            if (a.x < -0.54f && b.x < -0.54f && c.x < -0.54f &&
                center.z > -0.48f && center.z < 0.16f)
                continue;
            var x = (center.x - ChestX) / 0.106f;
            var z = (center.z - ChestZ) / 0.074f;
            // Keep the patient's back. Remove only front-facing skin within
            // the retracted incision, leaving a real opening in the torso.
            if (center.y > 0.64f && x * x + z * z < 1f)
                continue;
            kept.Add(source[i]);
            kept.Add(source[i + 1]);
            kept.Add(source[i + 2]);
        }
        mesh.triangles = kept.ToArray();
        mesh.RecalculateBounds();
        Debug.Log("Patient chest opening removed " +
                  (source.Length - kept.Count) / 3 + " skin triangles.");
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    sealed class TorsoSurfaceSampler
    {
        readonly Vector3[] vertices;
        readonly int[] triangles;

        public TorsoSurfaceSampler(Mesh mesh, Transform patient)
        {
            var local = mesh.vertices;
            vertices = new Vector3[local.Length];
            for (var i = 0; i < local.Length; ++i)
                vertices[i] = patient.TransformPoint(local[i]);
            triangles = mesh.triangles;
        }

        public float SurfaceY(float x, float z)
        {
            var highest = float.NegativeInfinity;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                var denominator = (b.z - c.z) * (a.x - c.x) +
                    (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < 0.00000001f)
                    continue;
                var u = ((b.z - c.z) * (x - c.x) +
                    (c.x - b.x) * (z - c.z)) / denominator;
                var v = ((c.z - a.z) * (x - c.x) +
                    (a.x - c.x) * (z - c.z)) / denominator;
                var w = 1f - u - v;
                if (u < -0.0001f || v < -0.0001f || w < -0.0001f)
                    continue;
                highest = Mathf.Max(highest, u * a.y + v * b.y + w * c.y);
            }
            if (float.IsNegativeInfinity(highest))
                throw new InvalidOperationException("Could not trace torso at " + x + ", " + z);
            return highest;
        }
    }

    static Mesh IncisionRimMesh(TorsoSurfaceSampler skin, Transform chest)
    {
        const int count = 48;
        const string path = "Assets/Surgery/Models/SkinFittedIncisionRim.asset";
        var vertices = new Vector3[count * 3];
        var triangles = new int[count * 12];
        for (var i = 0; i < count; ++i)
        {
            var angle = i * Mathf.PI * 2f / count;
            var cosine = Mathf.Cos(angle);
            var sine = Mathf.Sin(angle);
            var outerX = chest.position.x + cosine * 0.125f;
            var outerZ = chest.position.z + sine * 0.090f;
            var innerX = chest.position.x + cosine * 0.102f;
            var innerZ = chest.position.z + sine * 0.070f;
            var skinY = skin.SurfaceY(outerX, outerZ);
            vertices[i * 3] = new Vector3(outerX - chest.position.x,
                skinY + 0.001f - chest.position.y, outerZ - chest.position.z);
            vertices[i * 3 + 1] = new Vector3(innerX - chest.position.x,
                skinY - 0.010f - chest.position.y, innerZ - chest.position.z);
            vertices[i * 3 + 2] = new Vector3(innerX - chest.position.x,
                0.003f, innerZ - chest.position.z);
            var next = (i + 1) % count;
            var t = i * 12;
            triangles[t] = i * 3;
            triangles[t + 1] = i * 3 + 1;
            triangles[t + 2] = next * 3;
            triangles[t + 3] = i * 3 + 1;
            triangles[t + 4] = next * 3 + 1;
            triangles[t + 5] = next * 3;
            triangles[t + 6] = i * 3 + 1;
            triangles[t + 7] = i * 3 + 2;
            triangles[t + 8] = next * 3 + 1;
            triangles[t + 9] = i * 3 + 2;
            triangles[t + 10] = next * 3 + 2;
            triangles[t + 11] = next * 3 + 1;
        }
        var mesh = new Mesh { name = "Skin-fitted incision rim" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Mesh FenestratedDrapeMesh(TorsoSurfaceSampler skin)
    {
        const int angles = 64, rings = 4;
        const string path = "Assets/Surgery/Models/UpperChestSurgicalDrape.asset";
        var vertices = new Vector3[angles * rings];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[angles * (rings - 1) * 6];
        for (var i = 0; i < angles; ++i)
        {
            var theta = i * Mathf.PI * 2f / angles;
            var dx = Mathf.Cos(theta);
            var dz = Mathf.Sin(theta);
            var xDistance = dx > 0f ? (0.52f - ChestX) / dx :
                dx < 0f ? (0.085f - ChestX) / dx : float.PositiveInfinity;
            var zDistance = dz > 0f ? (-0.025f - ChestZ) / dz :
                dz < 0f ? (-0.295f - ChestZ) / dz : float.PositiveInfinity;
            var edge = Mathf.Min(xDistance, zDistance);
            for (var ring = 0; ring < rings; ++ring)
            {
                var t = ring / (float)(rings - 1);
                var x = Mathf.Lerp(ChestX + dx * 0.14f, ChestX + dx * edge, t);
                var z = Mathf.Lerp(ChestZ + dz * 0.102f, ChestZ + dz * edge, t);
                var fold = t * 0.003f * Mathf.Sin(theta * 11f);
                var index = ring * angles + i;
                vertices[index] = new Vector3(x, DrapeSurfaceY(skin, x, z) + 0.012f + fold, z);
                uv[index] = new Vector2((x + 0.3f) * 2f, (z + 0.5f) * 2f);
            }
        }
        var cursor = 0;
        for (var ring = 0; ring < rings - 1; ++ring)
            for (var i = 0; i < angles; ++i)
            {
                var next = (i + 1) % angles;
                var a = ring * angles + i;
                var b = ring * angles + next;
                var c = (ring + 1) * angles + i;
                var d = (ring + 1) * angles + next;
                triangles[cursor++] = a;
                triangles[cursor++] = b;
                triangles[cursor++] = c;
                triangles[cursor++] = b;
                triangles[cursor++] = d;
                triangles[cursor++] = c;
            }
        var mesh = new Mesh { name = "Upper-body surgical drape with chest opening" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static float DrapeSurfaceY(TorsoSurfaceSampler skin, float x, float z)
    {
        // A drape spans past the torso edge near the shoulder. Sample the
        // nearest supported skin point so the cloth hangs over that gap.
        for (var fraction = 1f; fraction > 0.2f; fraction *= 0.7f)
        {
            try
            {
                return skin.SurfaceY(Mathf.Lerp(ChestX, x, fraction),
                    Mathf.Lerp(ChestZ, z, fraction));
            }
            catch (InvalidOperationException) { }
        }
        return skin.SurfaceY(ChestX, ChestZ);
    }

    static void SurfaceSegment(string name, Transform parent, TorsoSurfaceSampler skin,
        Vector2 start, Vector2 end, float radius, Material material)
    {
        Segment(name, parent,
            new Vector3(start.x, skin.SurfaceY(start.x, start.y) + 0.006f, start.y),
            new Vector3(end.x, skin.SurfaceY(end.x, end.y) + 0.006f, end.y),
            radius, material);
    }

    static Rigidbody MakeHeart(string newName, string oldName, Vector3 position,
        Material heart, Material artery, Material vein)
    {
        var item = GameObject.Find(oldName);
        if (item == null)
            throw new InvalidOperationException("Missing pickable " + oldName);
        item.name = newName;
        item.transform.position = position;
        item.GetComponent<MeshRenderer>().enabled = false;
        var collider = item.GetComponent<BoxCollider>();
        var worldScale = item.transform.lossyScale;
        collider.size = new Vector3(0.082f / Mathf.Abs(worldScale.x),
            0.105f / Mathf.Abs(worldScale.y), 0.082f / Mathf.Abs(worldScale.z));
        var body = item.GetComponent<Rigidbody>();
        body.mass = 0.08f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        var model = new GameObject("Anatomical heart model").transform;
        model.SetParent(item.transform, false);
        var scale = item.transform.lossyScale;
        model.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        Ellipsoid("Left ventricle", model, position + new Vector3(-0.008f, 0f, 0f),
            new Vector3(0.035f, 0.047f, 0.034f), heart);
        Ellipsoid("Right ventricle", model, position + new Vector3(0.012f, 0.004f, 0.004f),
            new Vector3(0.027f, 0.037f, 0.029f), heart);
        Ellipsoid("Left atrium", model, position + new Vector3(-0.016f, 0.019f, -0.008f),
            new Vector3(0.022f, 0.022f, 0.024f), heart);
        Ellipsoid("Right atrium", model, position + new Vector3(0.016f, 0.021f, -0.006f),
            new Vector3(0.019f, 0.021f, 0.021f), heart);
        Segment("Aorta", model, position + new Vector3(-0.009f, 0.025f, 0.006f),
            position + new Vector3(-0.011f, 0.052f, 0.009f), 0.006f, artery);
        Segment("Pulmonary vessel", model, position + new Vector3(0.011f, 0.025f, -0.005f),
            position + new Vector3(0.020f, 0.045f, -0.009f), 0.005f, vein);
        return body;
    }

    static BoxCollider Tray(string name, Transform parent, Vector3 position,
        Material interior, Material metal)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        Cube(name + " bottom", root.transform, position,
            new Vector3(0.13f, 0.007f, 0.11f), metal, false);
        Cube(name + " liner", root.transform, position + new Vector3(0f, 0.005f, 0f),
            new Vector3(0.12f, 0.002f, 0.10f), interior, false);
        foreach (var x in new[] { -0.065f, 0.065f })
            Cube("Tray side", root.transform, position + new Vector3(x, 0.018f, 0f),
                new Vector3(0.004f, 0.03f, 0.11f), metal, false);
        foreach (var z in new[] { -0.055f, 0.055f })
            Cube("Tray end", root.transform, position + new Vector3(0f, 0.018f, z),
                new Vector3(0.13f, 0.03f, 0.004f), metal, false);
        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 0.035f, 0f);
        trigger.size = new Vector3(0.115f, 0.07f, 0.095f);
        return trigger;
    }

    static void Segment(string name, Transform parent, Vector3 start,
        Vector3 end, float radius, Material material)
    {
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = name;
        cylinder.transform.SetParent(parent, false);
        cylinder.transform.position = (start + end) * 0.5f;
        cylinder.transform.up = (end - start).normalized;
        cylinder.transform.localScale = new Vector3(radius * 2f,
            Vector3.Distance(start, end) * 0.5f, radius * 2f);
        cylinder.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(cylinder.GetComponent<Collider>());
    }

    static GameObject Cube(string name, Transform parent, Vector3 worldPosition,
        Vector3 scale, Material material, bool physical)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = name;
        item.transform.SetParent(parent, false);
        item.transform.position = worldPosition;
        item.transform.localScale = scale;
        item.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (!physical)
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
        return item;
    }

    static GameObject Ellipsoid(string name, Transform parent, Vector3 worldPosition,
        Vector3 scale, Material material)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        item.name = name;
        item.transform.SetParent(parent, false);
        item.transform.position = worldPosition;
        item.transform.localScale = scale;
        item.GetComponent<MeshRenderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
        return item;
    }

    static TextMesh Text(string name, Transform parent, Vector3 localPosition,
        int fontSize, float characterSize, Color color, TextAnchor anchor)
    {
        var item = new GameObject(name);
        item.transform.SetParent(parent, false);
        item.transform.localPosition = localPosition;
        var label = item.AddComponent<TextMesh>();
        label.fontSize = fontSize;
        label.characterSize = characterSize;
        label.anchor = anchor;
        label.alignment = TextAlignment.Center;
        label.color = color;
        return label;
    }
}
