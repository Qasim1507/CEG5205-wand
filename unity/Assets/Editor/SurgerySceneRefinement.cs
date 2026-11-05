using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Quest-friendly anatomy, draping and bedside instrument trolley.</summary>
public static class SurgerySceneRefinement
{
    const string DrapePath = "Assets/Surgery/Models/LowerBodyDrape.asset";
    const string DynamicHeart = "Heart ventricular and atrial myocardium";

    public static void Apply()
    {
        SurgeryAnatomyAssets.Ensure();
        SurgeryHeartAssets.Ensure();
        var field = GameObject.Find("Surgery training field");
        var native = GameObject.Find("Box 1 - native heart");
        var donor = GameObject.Find("Box 2 - donor heart");
        if (field == null || native == null || donor == null)
            throw new InvalidOperationException("Surgery scene is missing the field or hearts.");

        var priorSheet = GameObject.Find("Lower body sheet");
        if (priorSheet != null) UnityEngine.Object.DestroyImmediate(priorSheet);
        var priorAnatomy = GameObject.Find("Anatomy internals - CC0 torso organs");
        if (priorAnatomy != null) UnityEngine.Object.DestroyImmediate(priorAnatomy);
        var priorDrape = GameObject.Find("Sterile lower-body drape");
        if (priorDrape != null) UnityEngine.Object.DestroyImmediate(priorDrape);
        var priorTrolley = GameObject.Find("Bedside instrument trolley");
        if (priorTrolley != null) UnityEngine.Object.DestroyImmediate(priorTrolley);

        var heartPart = FindPart(DynamicHeart);
        var heartMesh = SurgeryAnatomyAssets.Mesh(heartPart.id);
        if (heartMesh == null) throw new InvalidOperationException("Anatomical heart mesh is missing.");
        var heartLocalCenter = heartMesh.bounds.center;
        var origin = native.transform.position - heartLocalCenter;
        // The cavity shows the organs below the skin, while the removable
        // heart sits slightly proud of the opening for a clear grasp target.
        origin.y -= 0.115f;
        var anatomy = new GameObject("Anatomy internals - CC0 torso organs");
        anatomy.transform.SetParent(field.transform, false);
        anatomy.transform.position = origin;

        foreach (var line in SurgeryAnatomyAssets.ManifestLines)
        {
            var values = line.Split('\t');
            if (values.Length != 3) continue;
            var id = values[0];
            var name = values[1];
            var materialId = values[2];
            if (name.Contains("torso shell")) continue; // existing full-body exterior
            if (IsMovingHeartPart(name))
            {
                continue;
            }
            var section = VisibleSectionMesh(id, SurgeryAnatomyAssets.Mesh(id), origin);
            if (section != null)
                CreateMeshObject(name, anatomy.transform, section,
                    SurgeryAnatomyAssets.Material(materialId), Vector3.zero);
        }
        SetHeartVisual(native.transform);
        SetHeartVisual(donor.transform);

        var drape = new GameObject("Sterile lower-body drape");
        drape.transform.SetParent(field.transform, false);
        drape.AddComponent<MeshFilter>().sharedMesh = CreateDrapeMesh();
        drape.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/SurgeryGame/SterileDrape.mat");

        var trolley = new GameObject("Bedside instrument trolley");
        trolley.transform.SetParent(field.transform, false);
        var steel = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/SurgeryGame/InstrumentSteel.mat");
        var bed = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/SurgeryGame/OperatingBed.mat");
        foreach (var x in new[] { -0.17f, 0.67f })
            foreach (var z in new[] { -0.86f, -0.66f })
            {
                Cube("Trolley leg", trolley.transform, new Vector3(x, 0.36f, z),
                    new Vector3(0.018f, 0.72f, 0.018f), steel);
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                wheel.name = "Trolley caster";
                wheel.transform.SetParent(trolley.transform, false);
                wheel.transform.position = new Vector3(x, 0.027f, z);
                wheel.transform.localScale = Vector3.one * 0.045f;
                wheel.GetComponent<MeshRenderer>().sharedMaterial = bed;
                UnityEngine.Object.DestroyImmediate(wheel.GetComponent<Collider>());
            }
        Cube("Lower trolley shelf", trolley.transform, new Vector3(0.25f, 0.24f, -0.76f),
            new Vector3(0.88f, 0.015f, 0.25f), bed);

        var rig = GameObject.Find("XR Origin - Quest viewing");
        if (rig != null)
        {
            rig.transform.position = new Vector3(1.16f, 0.45f, -2.73f);
            rig.transform.rotation = Quaternion.Euler(45f, -20f, 0f);
        }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.54f, 0.59f, 0.62f);
        var sun = GameObject.Find("Directional Light");
        if (sun != null)
        {
            var light = sun.GetComponent<Light>();
            if (light != null)
            {
                light.intensity = 0.95f;
                light.shadowStrength = 0.25f;
            }
        }
    }

    static (string id, string material) FindPart(string name)
    {
        foreach (var line in SurgeryAnatomyAssets.ManifestLines)
        {
            var values = line.Split('\t');
            if (values.Length == 3 && values[1] == name) return (values[0], values[2]);
        }
        throw new InvalidOperationException("Anatomy part not found: " + name);
    }

    static bool IsMovingHeartPart(string name) =>
        name == DynamicHeart || name == "Anterior interventricular artery" ||
        name == "Diagonal coronary branch" || name == "Great cardiac vein" ||
        name == "Right marginal coronary artery" ||
        name == "Ascending aorta and aortic arch" || name == "Pulmonary trunk" ||
        name == "Superior vena cava";

    static Mesh VisibleSectionMesh(string id, Mesh original, Vector3 origin)
    {
        if (original == null) throw new InvalidOperationException("Missing anatomy mesh " + id);
        var vertices = original.vertices;
        var source = original.triangles;
        var kept = new List<int>();
        for (var i = 0; i < source.Length; i += 3)
        {
            var center = origin + (vertices[source[i]] + vertices[source[i + 1]] +
                vertices[source[i + 2]]) / 3f;
            var x = (center.x - 0.29f) / 0.102f;
            var z = (center.z + 0.16f) / 0.070f;
            if (x * x + z * z > 0.94f * 0.94f) continue;
            kept.Add(source[i]);
            kept.Add(source[i + 1]);
            kept.Add(source[i + 2]);
        }
        if (kept.Count == 0) return null;
        var visible = new Mesh
        {
            name = "Visible thoracic section " + id,
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            vertices = vertices,
            normals = original.normals,
            uv = original.uv,
            triangles = kept.ToArray()
        };
        visible.RecalculateBounds();
        var path = "Assets/Surgery/Models/AnatomyMeshes/VisiblePart_" + id + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(visible, existing);
            UnityEngine.Object.DestroyImmediate(visible);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(visible, path);
        return visible;
    }

    static void SetHeartVisual(Transform heart)
    {
        var model = heart.Find("Anatomical heart model");
        if (model == null) throw new InvalidOperationException("A heart model container is missing.");
        // Source heart is modeled at adult size; 42% gives a compact,
        // recognizable game target that fits the small Niryo forceps and tray.
        model.localScale *= 0.42f;
        while (model.childCount > 0)
            UnityEngine.Object.DestroyImmediate(model.GetChild(0).gameObject);
        foreach (var line in SurgeryHeartAssets.ManifestLines)
        {
            var fields = line.Split('\t');
            CreateMeshObject(fields[1], model, TwoSidedMesh(fields[0]),
                SurgeryHeartAssets.Material(fields[2]), Vector3.zero);
        }
    }

    static Mesh TwoSidedMesh(string id)
    {
        var path = "Assets/Surgery/Models/HeartMeshes/TwoSidedPart_" + id + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        var source = SurgeryHeartAssets.Mesh(id);
        if (source == null) throw new InvalidOperationException("Missing anatomy mesh " + id);
        var count = source.vertexCount;
        var vertices = new Vector3[count * 2];
        var normals = new Vector3[count * 2];
        var uv = new Vector2[count * 2];
        var sourceVertices = source.vertices;
        var sourceNormals = source.normals;
        var sourceUv = source.uv;
        for (var i = 0; i < count; ++i)
        {
            vertices[i] = vertices[count + i] = sourceVertices[i];
            normals[i] = sourceNormals[i];
            normals[count + i] = -sourceNormals[i];
            uv[i] = uv[count + i] = sourceUv[i];
        }
        var sourceTriangles = source.triangles;
        var triangles = new int[sourceTriangles.Length * 2];
        Array.Copy(sourceTriangles, triangles, sourceTriangles.Length);
        for (var i = 0; i < sourceTriangles.Length; i += 3)
        {
            var j = sourceTriangles.Length + i;
            triangles[j] = count + sourceTriangles[i];
            triangles[j + 1] = count + sourceTriangles[i + 2];
            triangles[j + 2] = count + sourceTriangles[i + 1];
        }
        var mesh = new Mesh
        {
            name = "Two-sided anatomical part " + id,
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            vertices = vertices,
            normals = normals,
            uv = uv,
            triangles = triangles
        };
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static void CreateMeshObject(string name, Transform parent, Mesh mesh, Material material,
        Vector3 localPosition)
    {
        if (mesh == null || material == null)
            throw new InvalidOperationException("Anatomy asset missing for " + name);
        var item = new GameObject(name);
        item.transform.SetParent(parent, false);
        item.transform.localPosition = localPosition;
        item.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = item.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = name;
        item.transform.SetParent(parent, false);
        item.transform.position = position;
        item.transform.localScale = scale;
        item.GetComponent<MeshRenderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
    }

    static Mesh CreateDrapeMesh()
    {
        // The cloth overlaps the lower abdomen and hangs to the mattress.
        const int nx = 28, nz = 22;
        const float x0 = -0.96f, x1 = 0.14f, z0 = -0.69f, z1 = 0.37f;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (var zi = 0; zi <= nz; ++zi)
            for (var xi = 0; xi <= nx; ++xi)
            {
                var u = (float)xi / nx;
                var v = (float)zi / nz;
                var x = Mathf.Lerp(x0, x1, u);
                var z = Mathf.Lerp(z0, z1, v);
                var center = Mathf.Sin(Mathf.PI * v);
                // A slight rise at the hips and shallow pleats suggest cloth
                // over the body without the steep profile of a rigid tent.
                var fold = center * (0.011f * Mathf.Sin(7f * Mathf.PI * u + 2f * v) +
                    0.004f * Mathf.Sin(15f * Mathf.PI * v));
                var height = 0.77f + 0.035f * u + 0.012f * center + fold;
                vertices.Add(new Vector3(x, height, z));
                uv.Add(new Vector2(u * 3f, v * 3f));
            }
        for (var zi = 0; zi < nz; ++zi)
            for (var xi = 0; xi < nx; ++xi)
            {
                var a = zi * (nx + 1) + xi;
                var b = a + 1;
                var c = a + nx + 1;
                var d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        AddSkirt(vertices, uv, triangles, nx, nz, true, 0);
        AddSkirt(vertices, uv, triangles, nx, nz, true, nz);
        AddSkirt(vertices, uv, triangles, nx, nz, false, 0);
        AddSkirt(vertices, uv, triangles, nx, nz, false, nx);
        // Both faces remain opaque from the head and the foot of the bed.
        var frontCount = triangles.Count;
        for (var i = 0; i < frontCount; i += 3)
            triangles.AddRange(new[] { triangles[i], triangles[i + 2], triangles[i + 1] });
        var mesh = new Mesh { name = "Wrinkled sterile patient drape", indexFormat =
            UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        // Calculate lighting from the outward faces only. Averaging both
        // windings would cancel the normals and make the cloth look flat.
        var normals = new Vector3[vertices.Count];
        for (var i = 0; i < frontCount; i += 3)
        {
            var a = triangles[i]; var b = triangles[i + 1]; var c = triangles[i + 2];
            var face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            normals[a] += face; normals[b] += face; normals[c] += face;
        }
        for (var i = 0; i < normals.Length; ++i)
            normals[i] = normals[i].sqrMagnitude > 0.000001f ? normals[i].normalized : Vector3.up;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(DrapePath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, DrapePath);
        return mesh;
    }

    static void AddSkirt(List<Vector3> vertices, List<Vector2> uv, List<int> triangles,
        int nx, int nz, bool alongX, int edge)
    {
        var count = alongX ? nx : nz;
        for (var i = 0; i < count; ++i)
        {
            var a = alongX ? edge * (nx + 1) + i : i * (nx + 1) + edge;
            var b = alongX ? a + 1 : a + nx + 1;
            var start = vertices.Count;
            var pa = vertices[a];
            var pb = vertices[b];
            vertices.Add(pa);
            vertices.Add(pb);
            vertices.Add(new Vector3(pa.x, 0.585f, pa.z));
            vertices.Add(new Vector3(pb.x, 0.585f, pb.z));
            uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one });
            triangles.AddRange(new[] { start, start + 2, start + 1,
                start + 1, start + 2, start + 3 });
        }
    }

}
