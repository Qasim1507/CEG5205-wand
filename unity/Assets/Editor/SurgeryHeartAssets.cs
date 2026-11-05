using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Imports the CC0 anatomical heart as separate Unity Mesh assets.</summary>
public static class SurgeryHeartAssets
{
    const string Source = "ThirdPartySources/Anatomy/HeartPrepared";
    const string Meshes = "Assets/Surgery/Models/HeartMeshes";
    const string Materials = "Assets/Materials/HeartAnatomy";
    const string Textures = "Assets/Surgery/Models/HeartTextures";

    public static string[] ManifestLines => File.ReadAllLines(Source + "/manifest.tsv");

    public static void Ensure()
    {
        if (!File.Exists(Source + "/manifest.tsv"))
            throw new FileNotFoundException("Prepared heart data is missing", Source + "/manifest.tsv");
        Directory.CreateDirectory(Meshes);
        Directory.CreateDirectory(Materials);
        AssetDatabase.Refresh();
        foreach (var line in ManifestLines)
        {
            var fields = line.Split('\t');
            if (fields.Length != 3) throw new InvalidDataException("Bad heart manifest: " + line);
            var id = fields[0];
            var materialId = fields[2];
            var materialPath = Materials + "/Material_" + materialId + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Textures + "/HeartMaterial_" + materialId + ".png");
                if (texture == null) throw new FileNotFoundException("Missing heart texture " + materialId);
                var material = new Material(Shader.Find("Standard"))
                {
                    name = "Anatomical heart " + materialId,
                    mainTexture = texture,
                    color = Color.white,
                    enableInstancing = true
                };
                material.SetFloat("_Glossiness", 0.30f);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var meshPath = Meshes + "/Part_" + id + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) continue;
            using (var reader = new BinaryReader(File.OpenRead(Source + "/Part_" + id + ".meshbin")))
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
                var triangles = new int[indexCount];
                for (var i = 0; i < indexCount; ++i) triangles[i] = reader.ReadInt32();
                var mesh = new Mesh { name = fields[1], indexFormat = IndexFormat.UInt32 };
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uv;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
        }
        AssetDatabase.SaveAssets();
    }

    public static Mesh Mesh(string id) => AssetDatabase.LoadAssetAtPath<Mesh>(Meshes + "/Part_" + id + ".asset");
    public static Material Material(string id) => AssetDatabase.LoadAssetAtPath<Material>(Materials + "/Material_" + id + ".mat");
}
