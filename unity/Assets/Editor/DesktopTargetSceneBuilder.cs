using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DesktopTargetSceneBuilder
{
    const string SourceScene = "Assets/Scenes/MultiBoxSorting.unity";
    const string OutputScene = "Assets/Scenes/DesktopHandTarget.unity";

    [MenuItem("Tools/Robotics/Build Desktop Hand Target Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        var publisher = GameObject.Find("Publisher");
        var planner = publisher == null ? null : publisher.GetComponent<TrajectoryPlanner>();
        if (planner == null || planner.NiryoOne == null)
            throw new InvalidOperationException("The sorting scene needs its Niryo robot and Publisher.");

        var sorting = publisher.GetComponent<MultiBoxSortingController>();
        if (sorting != null)
            sorting.enabled = false;

        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Desktop Hand Target - drag in Game view";
        marker.transform.position = new Vector3(0.216f, 0.746f, -0.157f);
        marker.transform.localScale = Vector3.one * 0.032f;
        marker.GetComponent<SphereCollider>().isTrigger = true;

        const string materialPath = "Assets/Materials/Sorting/HandTarget.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.88f, 0.05f) };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.8f, 0.54f, 0f));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        marker.GetComponent<MeshRenderer>().sharedMaterial = material;

        var controller = publisher.AddComponent<DesktopTargetController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("m_Robot").objectReferenceValue = planner.NiryoOne;
        serialized.FindProperty("m_Target").objectReferenceValue = marker.transform;
        serialized.FindProperty("m_Camera").objectReferenceValue = Camera.main;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, OutputScene))
            throw new InvalidOperationException("Could not save " + OutputScene);

        var buildScenes = EditorBuildSettings.scenes.ToList();
        if (buildScenes.All(item => item.path != OutputScene))
        {
            buildScenes.Add(new EditorBuildSettingsScene(OutputScene, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("DESKTOP_TARGET_SCENE_CREATED " + OutputScene);
    }
}
