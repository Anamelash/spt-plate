using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch build of the PLATE blood bag bundle (SPT/EFT 0.16.9, Unity 2022.3.43f1).
/// Run: Unity.exe -batchmode -nographics -quit -projectPath &lt;unity&gt;
///      -executeMethod PlateBundleBuilder.Build -logFile build.log
/// Does: FBX import scaled to real-world size, glTF metallic-roughness conversion
/// to a Standard-shader map, material, prefab with a collider, AssetBundle build
/// under the key plate/blood_bag.bundle (= the item's Prefab.path).
/// </summary>
public static class PlateBundleBuilder
{
    private const string FbxPath = "Assets/PLATE/Models/blood_bag.fbx";
    // Retopologized variant (build/blender-retopo.py): clean UVs + baked maps.
    // If the files are present it is used, and UnityMeshSimplifier decimation is not needed.
    private const string RetopoFbxPath = "Assets/PLATE/Models/blood_bag_retopo.fbx";
    private const string BakedAlbedoPath = "Assets/PLATE/Textures/blood_bag_baked_albedo.png";
    private const string BakedNormalPath = "Assets/PLATE/Textures/blood_bag_baked_normal.png";
    private const string AlbedoPath = "Assets/PLATE/Textures/blood_bag_albedo.png";
    private const string NormalPath = "Assets/PLATE/Textures/blood_bag_normal.png";
    private const string MetalRoughPath = "Assets/PLATE/Textures/blood_bag_metalrough.png";
    private const string MetallicStdPath = "Assets/PLATE/Textures/blood_bag_metallic_std.png";
    private const string MaterialPath = "Assets/PLATE/blood_bag.mat";
    private const string SimplifiedMeshPath = "Assets/PLATE/blood_bag_lowpoly.asset";
    // IMPORTANT: the prefab file name must match the bundle file name — EFT fetches
    // the asset via IEasyBundle.SameNameAsset (bundle name without the extension)
    private const string PrefabPath = "Assets/PLATE/blood_bag.prefab";
    private const string BundleName = "plate/blood_bag.bundle";
    private const string OutputDir = "BundleOutput";

    /// <summary>Maximum item dimension in meters (a blood bag is ~23 cm).</summary>
    private const float TargetMaxDimM = 0.23f;

    /// <summary>Target triangle count; the source is ~50k. Below ~24k the
    /// photogrammetry texture degrades noticeably even with UV seam protection.</summary>
    private const int TargetTriangles = 24000;

    public static void Build()
    {
        try
        {
            BuildInternal();
            Debug.Log("[PLATE] Bundle build OK");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PLATE] Bundle build FAILED: {ex}");
            EditorApplication.Exit(1);
        }
    }

    private static void BuildInternal()
    {
        Material material;
        Mesh mesh;
        if (File.Exists(RetopoFbxPath))
        {
            Debug.Log("[PLATE] Using RETOPO model (baked maps, clean UVs)");
            ConfigureModel(RetopoFbxPath);
            material = BuildBakedMaterial();
            mesh = BuildRetopoMesh();
        }
        else
        {
            ConfigureModel(FbxPath);
            ConfigureTextures();
            var metallicStd = BuildMetallicSmoothnessMap();
            material = BuildMaterial(metallicStd);
            mesh = BuildSimplifiedMesh();
        }

        BuildPrefab(material, mesh);
        BuildBundle();
    }

    /// <summary>FBX: no materials from the file, scaled to the real size of the bag.</summary>
    private static void ConfigureModel(string fbxPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter
                       ?? throw new Exception($"FBX not found at {fbxPath}");
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.isReadable = true; // vertex access for decimation
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.SaveAndReimport();

        // scale: measure the actual bounds and bring them to TargetMaxDimM
        var size = MeasureWorldBounds(fbxPath).size;
        var maxDim = Mathf.Max(size.x, size.y, size.z);
        if (maxDim <= 0.0001f)
        {
            throw new Exception("Model bounds are zero");
        }

        if (Mathf.Abs(maxDim - TargetMaxDimM) > 0.005f)
        {
            importer.globalScale *= TargetMaxDimM / maxDim;
            importer.SaveAndReimport();
            Debug.Log($"[PLATE] Rescaled: maxDim {maxDim:F3} m -> {TargetMaxDimM} m " +
                      $"(globalScale={importer.globalScale:F4})");
        }
    }

    private static Bounds MeasureWorldBounds(string fbxPath)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var instance = UnityEngine.Object.Instantiate(model);
        try
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                throw new Exception("No renderers in FBX");
            }

            var b = renderers[0].bounds;
            foreach (var r in renderers)
            {
                b.Encapsulate(r.bounds);
            }

            return b;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void ConfigureTextures()
    {
        var albedo = (TextureImporter)AssetImporter.GetAtPath(AlbedoPath);
        albedo.maxTextureSize = 2048;
        albedo.SaveAndReimport();

        var normal = (TextureImporter)AssetImporter.GetAtPath(NormalPath);
        normal.textureType = TextureImporterType.NormalMap;
        normal.maxTextureSize = 2048;
        normal.SaveAndReimport();

        // conversion source — needs pixel access, does not go into the bundle
        var mr = (TextureImporter)AssetImporter.GetAtPath(MetalRoughPath);
        mr.isReadable = true;
        mr.sRGBTexture = false;
        mr.textureCompression = TextureImporterCompression.Uncompressed;
        mr.SaveAndReimport();
    }

    /// <summary>
    /// glTF metallic-roughness (G=roughness, B=metallic) -> Standard-shader map
    /// (_MetallicGlossMap: RGB=metallic, A=smoothness=1-roughness).
    /// </summary>
    private static Texture2D BuildMetallicSmoothnessMap()
    {
        var src = AssetDatabase.LoadAssetAtPath<Texture2D>(MetalRoughPath)
                  ?? throw new Exception($"Texture not found: {MetalRoughPath}");
        var pixels = src.GetPixels();
        for (var i = 0; i < pixels.Length; i++)
        {
            var metallic = pixels[i].b;
            var smoothness = 1f - pixels[i].g;
            pixels[i] = new Color(metallic, metallic, metallic, smoothness);
        }

        var outTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
        outTex.SetPixels(pixels);
        outTex.Apply();
        File.WriteAllBytes(MetallicStdPath, outTex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(outTex);
        AssetDatabase.ImportAsset(MetallicStdPath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(MetallicStdPath);
        importer.sRGBTexture = false; // linear data, not color
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicStdPath);
    }

    private static Material BuildMaterial(Texture2D metallicStd)
    {
        var shader = Shader.Find("Standard")
                     ?? throw new Exception("Standard shader not found");
        var mat = new Material(shader)
        {
            mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath),
        };
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", metallicStd);
        mat.EnableKeyword("_METALLICGLOSSMAP");

        AssetDatabase.DeleteAsset(MaterialPath);
        AssetDatabase.CreateAsset(mat, MaterialPath);
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    }

    /// <summary>Material from the retopo baked maps: albedo + normal on clean UVs.
    /// There is no metallic map (it lives in the old UVs) — the bag's plastic is
    /// set by constants.</summary>
    private static Material BuildBakedMaterial()
    {
        var albedo = (TextureImporter)AssetImporter.GetAtPath(BakedAlbedoPath);
        albedo.maxTextureSize = 2048;
        albedo.SaveAndReimport();

        var normal = (TextureImporter)AssetImporter.GetAtPath(BakedNormalPath);
        normal.textureType = TextureImporterType.NormalMap;
        normal.maxTextureSize = 2048;
        normal.SaveAndReimport();

        var shader = Shader.Find("Standard")
                     ?? throw new Exception("Standard shader not found");
        var mat = new Material(shader)
        {
            mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BakedAlbedoPath),
        };
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BakedNormalPath));
        mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0.45f); // PVC bag: moderate specular

        AssetDatabase.DeleteAsset(MaterialPath);
        AssetDatabase.CreateAsset(mat, MaterialPath);
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    }

    /// <summary>The retopo mesh is already low-poly: only bake the node transform.</summary>
    private static Mesh BuildRetopoMesh()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RetopoFbxPath);
        var mesh = BakeNodeTransform(model);
        mesh.name = "blood_bag_lowpoly";
        mesh.RecalculateTangents();
        Debug.Log($"[PLATE] Retopo mesh: {mesh.triangles.Length / 3} tris, " +
                  $"bounds={mesh.bounds.size}");

        AssetDatabase.DeleteAsset(SimplifiedMeshPath);
        AssetDatabase.CreateAsset(mesh, SimplifiedMeshPath);
        return AssetDatabase.LoadAssetAtPath<Mesh>(SimplifiedMeshPath);
    }

    /// <summary>
    /// Decimation down to TargetTriangles (quadric error metrics, UnityMeshSimplifier).
    /// Only the reduced mesh goes into the bundle — the source FBX does not.
    /// </summary>
    private static Mesh BuildSimplifiedMesh()
    {
        // Take vertices NOT from the raw mesh asset but with the FBX node transform
        // baked in: Blender hangs the unit conversion on the node scale, and the raw
        // mesh can be 100x smaller/larger than what the scene shows (symptom: an
        // item a couple of pixels big).
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var srcMesh = BakeNodeTransform(model);
        var srcTris = srcMesh.triangles.Length / 3;

        var simplifier = new UnityMeshSimplifier.MeshSimplifier();
        var options = UnityMeshSimplifier.SimplificationOptions.Default;
        options.PreserveBorderEdges = true;
        // UV seam protection is MANDATORY: without it the photogrammetry atlas
        // texture "swims" across the triangles (visible smearing). The price —
        // decimation stops at ~14k instead of the 4k target: a hard limit without
        // losing the unwrap.
        options.PreserveUVSeamEdges = true;
        simplifier.SimplificationOptions = options;
        simplifier.Initialize(srcMesh);
        simplifier.SimplifyMesh(Mathf.Clamp01((float)TargetTriangles / srcTris));

        var mesh = simplifier.ToMesh();
        mesh.name = "blood_bag_lowpoly";
        mesh.RecalculateBounds();
        if (mesh.normals == null || mesh.normals.Length == 0)
        {
            mesh.RecalculateNormals();
        }

        mesh.RecalculateTangents(); // the normal map needs valid tangents after simplification
        Debug.Log($"[PLATE] Mesh simplified: {srcTris} -> {mesh.triangles.Length / 3} tris, " +
                  $"bounds={mesh.bounds.size}");

        AssetDatabase.DeleteAsset(SimplifiedMeshPath);
        AssetDatabase.CreateAsset(mesh, SimplifiedMeshPath);
        return AssetDatabase.LoadAssetAtPath<Mesh>(SimplifiedMeshPath);
    }

    /// <summary>
    /// A copy of the FBX mesh with the node transform (unit-conversion scale/rotation)
    /// baked into the vertices — dimensions match what is visible in the scene.
    /// </summary>
    private static Mesh BakeNodeTransform(GameObject model)
    {
        var instance = UnityEngine.Object.Instantiate(model);
        try
        {
            var mf = instance.GetComponentInChildren<MeshFilter>()
                     ?? throw new Exception("No MeshFilter in FBX");
            var matrix = mf.transform.localToWorldMatrix; // instance root is at identity
            var baked = UnityEngine.Object.Instantiate(mf.sharedMesh);
            var verts = baked.vertices;
            for (var i = 0; i < verts.Length; i++)
            {
                verts[i] = matrix.MultiplyPoint3x4(verts[i]);
            }

            baked.vertices = verts;
            var normals = baked.normals;
            if (normals != null && normals.Length == verts.Length)
            {
                var rot = matrix.rotation;
                for (var i = 0; i < normals.Length; i++)
                {
                    normals[i] = (rot * normals[i]).normalized;
                }

                baked.normals = normals;
            }

            baked.RecalculateBounds();
            return baked;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    /// <summary>Prefab: a root with a BoxCollider, the mesh in its authored orientation, pivot at the center.</summary>
    private static void BuildPrefab(Material material, Mesh lowpolyMesh)
    {
        var root = new GameObject("item_plate_blood_bag");
        try
        {
            var mesh = new GameObject("mesh");
            mesh.transform.SetParent(root.transform, false);
            // authored orientation: the bag stands facing forward — the preview and
            // the icon look down the frontal axis (laying it flat gave a top-down view)
            mesh.AddComponent<MeshFilter>().sharedMesh = lowpolyMesh;
            mesh.AddComponent<MeshRenderer>().sharedMaterial = material;

            // pivot at the geometric center
            var bounds = mesh.GetComponent<MeshRenderer>().bounds;
            mesh.transform.localPosition = -bounds.center;

            var collider = root.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = bounds.size;

            // EFT preview/icon component (stub in Assets/PLATE/Stubs): without it the
            // icon generator crashes with an NRE (PreviewPivot.Icon.overrideIcon is
            // read without a null check) — an endless spinner instead of the icon
            var pivot = root.AddComponent<PreviewPivot>();
            pivot.pivotRotation = Quaternion.identity;
            pivot.scale = Vector3.one;
            pivot.Icon = new PreviewPivot.IconSettings
            {
                rotation = Quaternion.identity,
                boundsScale = 1f,
                orthographic = true,
            };

            AssetDatabase.DeleteAsset(PrefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void BuildBundle()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath)
                       ?? throw new Exception($"Prefab not found: {PrefabPath}");
        importer.SetAssetBundleNameAndVariant(BundleName, "");
        AssetDatabase.RemoveUnusedAssetBundleNames();

        Directory.CreateDirectory(OutputDir);
        var manifest = BuildPipeline.BuildAssetBundles(OutputDir,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        if (manifest == null)
        {
            throw new Exception("BuildAssetBundles returned null");
        }

        var bundleFile = Path.Combine(OutputDir, BundleName.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(bundleFile))
        {
            throw new Exception($"Bundle file missing: {bundleFile}");
        }

        Debug.Log($"[PLATE] Bundle written: {bundleFile} " +
                  $"({new FileInfo(bundleFile).Length / 1024} KB)");
    }
}
