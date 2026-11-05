using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MultiBoxSceneBuilder
{
    const string SourceScene = "Assets/Scenes/PickAndPlaceAutomatic.unity";
    const string OutputScene = "Assets/Scenes/MultiBoxSorting.unity";
    const string MaterialFolder = "Assets/Materials/Sorting";

    [MenuItem("Tools/Robotics/Build Multi-Box Sorting Scene")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Sorting");

        var deckMaterial = MaterialAsset("Deck", new Color(0.17f, 0.20f, 0.25f));
        var edgeMaterial = MaterialAsset("Edge", new Color(0.08f, 0.65f, 0.75f));
        var binAMaterial = MaterialAsset("BinA", new Color(0.05f, 0.55f, 0.78f));
        var binBMaterial = MaterialAsset("BinB", new Color(0.96f, 0.44f, 0.10f));
        var boxAMaterial = MaterialAsset("BoxA", new Color(0.15f, 0.80f, 0.95f));
        var boxBMaterial = MaterialAsset("BoxB", new Color(1.00f, 0.56f, 0.16f));
        var boxCMaterial = MaterialAsset("BoxC", new Color(0.38f, 0.91f, 0.54f));

        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        DestroySceneObject("Table");
        DestroySceneObject("Target");
        DestroySceneObject("TargetPlacement");

        var launcher = UnityEngine.Object.FindAnyObjectByType<PickAndPlaceLauncher>();
        if (launcher != null)
            UnityEngine.Object.DestroyImmediate(launcher);

        var workspace = new GameObject("Sorting Workspace - edit boxes and bins here").transform;
        Cube("Low work platform", workspace, new Vector3(0f, 0.605f, -0.12f),
            new Vector3(0.72f, 0.05f, 0.58f), deckMaterial);
        Cube("Front edge", workspace, new Vector3(0f, 0.632f, -0.413f),
            new Vector3(0.72f, 0.006f, 0.006f), edgeMaterial);

        var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Target.prefab");
        if (boxPrefab == null)
            throw new InvalidOperationException("The original Target prefab is missing.");

        var boxes = new[]
        {
            Box(boxPrefab, "Box 1 - blue", new Vector3(0.216f, 0.646f, -0.157f), boxAMaterial),
            Box(boxPrefab, "Box 2 - orange", new Vector3(0.174f, 0.646f, -0.207f), boxBMaterial),
            Box(boxPrefab, "Box 3 - green", new Vector3(0.174f, 0.646f, -0.105f), boxCMaterial)
        };

        var binA = CreateBin("Bin A - blue", workspace, new Vector3(-0.198f, 0.63f, -0.18f), binAMaterial);
        var binB = CreateBin("Bin B - orange", workspace, new Vector3(-0.216f, 0.63f, -0.26f), binBMaterial);
        var drop1 = DropTarget(binA.transform, "Drop target for Box 1", new Vector3(-0.018f, 0.01f, -0.007f));
        var drop2 = DropTarget(binB.transform, "Drop target for Box 2", new Vector3(0f, 0.01f, 0f));
        var drop3 = DropTarget(binA.transform, "Drop target for Box 3", new Vector3(0.018f, 0.01f, -0.007f));

        var publisher = GameObject.Find("Publisher");
        if (publisher == null)
            throw new InvalidOperationException("The Part 3 Publisher object is missing.");

        var planner = publisher.GetComponent<TrajectoryPlanner>();
        if (planner == null)
            throw new InvalidOperationException("The Part 3 TrajectoryPlanner is missing.");
        planner.Target = boxes[0];
        planner.TargetPlacement = drop1;

        var sorter = publisher.AddComponent<MultiBoxSortingController>();
        var sorterSerialized = new SerializedObject(sorter);
        sorterSerialized.FindProperty("m_Planner").objectReferenceValue = planner;
        var jobs = sorterSerialized.FindProperty("m_Jobs");
        jobs.arraySize = 3;
        SetJob(jobs.GetArrayElementAtIndex(0), "Box 1 to Bin A", boxes[0], drop1, binA);
        SetJob(jobs.GetArrayElementAtIndex(1), "Box 2 to Bin B", boxes[1], drop2, binB);
        SetJob(jobs.GetArrayElementAtIndex(2), "Box 3 to Bin A", boxes[2], drop3, binA);
        sorterSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, OutputScene))
            throw new InvalidOperationException("Could not save MultiBoxSorting.unity.");

        var buildScenes = EditorBuildSettings.scenes.ToList();
        if (buildScenes.All(item => item.path != OutputScene))
        {
            buildScenes.Add(new EditorBuildSettingsScene(OutputScene, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Created " + OutputScene + " with three pickable boxes, two physical bins, and ROS sorting buttons.");
    }

    static void SetJob(SerializedProperty job, string label, GameObject box, GameObject target, SortingBin bin)
    {
        job.FindPropertyRelative("Label").stringValue = label;
        job.FindPropertyRelative("Box").objectReferenceValue = box;
        job.FindPropertyRelative("DropTarget").objectReferenceValue = target;
        job.FindPropertyRelative("Bin").objectReferenceValue = bin;
    }

    static Material MaterialAsset(string name, Color color)
    {
        var path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        material = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void DestroySceneObject(string name)
    {
        var item = GameObject.Find(name);
        if (item == null)
            throw new InvalidOperationException("The Part 3 scene is missing " + name + ".");
        UnityEngine.Object.DestroyImmediate(item);
    }

    static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = position;
        cube.transform.localScale = scale;
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        return cube;
    }

    static GameObject Box(GameObject prefab, string name, Vector3 position, Material material)
    {
        var box = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        box.name = name;
        box.transform.position = position;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        var body = box.GetComponent<Rigidbody>();
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return box;
    }

    static SortingBin CreateBin(string name, Transform parent, Vector3 position, Material material)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = position;

        var interior = root.AddComponent<BoxCollider>();
        interior.isTrigger = true;
        interior.center = new Vector3(0f, 0.045f, 0f);
        interior.size = new Vector3(0.112f, 0.09f, 0.058f);
        var bin = root.AddComponent<SortingBin>();

        Cube("Solid bottom", root.transform, position + new Vector3(0f, 0.004f, 0f),
            new Vector3(0.125f, 0.008f, 0.07f), material);
        // The original MoveIt scene has no bin-wall collision geometry. Its planned
        // gripper path can push a held box through a wall, so these walls are visual.
        // The solid bottom supports the box and the interior trigger checks placement.
        VisualWall("Left wall", root.transform, position + new Vector3(-0.060f, 0.03f, 0f),
            new Vector3(0.006f, 0.052f, 0.07f), material);
        VisualWall("Right wall", root.transform, position + new Vector3(0.060f, 0.03f, 0f),
            new Vector3(0.006f, 0.052f, 0.07f), material);
        VisualWall("Front wall", root.transform, position + new Vector3(0f, 0.03f, -0.032f),
            new Vector3(0.125f, 0.052f, 0.006f), material);
        VisualWall("Back wall", root.transform, position + new Vector3(0f, 0.03f, 0.032f),
            new Vector3(0.125f, 0.052f, 0.006f), material);
        return bin;
    }

    static void VisualWall(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var wall = Cube(name, parent, position, scale, material);
        UnityEngine.Object.DestroyImmediate(wall.GetComponent<BoxCollider>());
    }

    static GameObject DropTarget(Transform bin, string name, Vector3 localPosition)
    {
        var target = new GameObject(name);
        target.transform.SetParent(bin, false);
        target.transform.localPosition = localPosition;
        return target;
    }
}
