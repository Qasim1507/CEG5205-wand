using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Imports CC0 torso organ meshes prepared from the original GLB.</summary>
public static class SurgeryAnatomyAssets
{
    const string Source = "ThirdPartySources/Anatomy/Prepared";
    const string Meshes = "Assets/Surgery/Models/AnatomyMeshes";
    const string Materials = "Assets/Materials/Anatomy";
    const string Textures = "Assets/Surgery/Models/AnatomyTextures";

    public static string[] ManifestLines => File.ReadAllLines(Source + "/manifest.tsv");

    [MenuItem("Tools/Robotics/Quest/Import Surgery Anatomy")]
    public static void Ensure()
    {
        if (!File.Exists(Source + "/manifest.tsv"))
            throw new FileNotFoundException("Prepared anatomy data is missing.", Source + "/manifest.tsv");
        Directory.CreateDirectory(Meshes);
        Directory.CreateDirectory(Materials);
        AssetDatabase.Refresh();
        foreach (var line in ManifestLines)
        {
            var fields = line.Split('\t');
            if (fields.Length != 3) throw new InvalidDataException("Bad anatomy manifest: " + line);
            var part = fields[0];
            var name = fields[1];
            var materialId = fields[2];
            var materialPath = Materials + "/Material_" + materialId + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Textures + "/Material_" + materialId + ".png");
                if (texture == null) throw new FileNotFoundException("Missing anatomy texture " + materialId);
                var material = new Material(Shader.Find("Standard"))
                {
                    name = "Anatomy " + materialId,
                    mainTexture = texture,
                    color = Color.white,
                    enableInstancing = true
                };
                material.SetFloat("_Glossiness", 0.22f);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var meshPath = Meshes + "/Part_" + part + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) continue;
            using (var reader = new BinaryReader(File.OpenRead(Source + "/Part_" + part + ".meshbin")))
            {
                var vertexCount = reader.ReadInt32();
                var indexCount = reader.ReadInt32();
                var vertices = new Vector3[vertexCount];
                var normals = new Vector3[vertexCount];
                var uv = new Vector2[vertexCount];
                for (var i = 0; i < vertexCount; ++i)
                {
                    vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                var indices = new int[indexCount];
                for (var i = 0; i < indexCount; ++i) indices[i] = reader.ReadInt32();
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uv;
                mesh.triangles = indices;
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Surgery anatomy imported: " + ManifestLines.Length + " meshes.");
    }

    public static Mesh Mesh(string part) => AssetDatabase.LoadAssetAtPath<Mesh>(
        Meshes + "/Part_" + part + ".asset");
    public static Material Material(string materialId) => AssetDatabase.LoadAssetAtPath<Material>(
        Materials + "/Material_" + materialId + ".mat");
}
