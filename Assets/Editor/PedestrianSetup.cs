using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Sets up wandering pedestrians, start to finish.
///
/// Self-contained on purpose: it builds its own Animator controller rather than
/// borrowing a player's, so this project needs no player character, no punching
/// and no combat scripts — just the campus, some characters and three clips.
///
/// It also prepares the source assets itself, because Mixamo files never arrive
/// ready: characters import on the Generic rig with their textures sealed inside
/// the FBX, and animation clips import without looping.
/// </summary>
public static class PedestrianSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string AnimFolder = "Assets/Animations";
    const string ControllerPath = AnimFolder + "/PedestrianLocomotion.controller";
    const string SpawnerName = "PedestrianSpawner";

    const string IdleKeyword = "idle";
    const string WalkKeyword = "walk";
    const string RunKeyword = "run";

    // Used when a clip was exported "In Place" and carries no travel of its own.
    const float FallbackWalk = 1.45f;
    const float FallbackRun = 4.4f;

    [MenuItem("NCAT/Set Up Pedestrians", false, 20)]
    public static void SetUpPedestrians()
    {
        AnimationClip idle = FindClip(IdleKeyword);
        AnimationClip walk = FindClip(WalkKeyword);
        AnimationClip run = FindClip(RunKeyword);

        if (idle == null || walk == null || run == null)
        {
            Debug.LogError(
                "Missing animation clips.\n" +
                "  idle: " + (idle ? idle.name : "MISSING") + "\n" +
                "  walk: " + (walk ? walk.name : "MISSING") + "\n" +
                "  run:  " + (run ? run.name : "MISSING") + "\n" +
                "Copy Breathing Idle.fbx, Walking.fbx and Running.fbx into Assets, or " +
                "download them from Mixamo as FBX for Unity, Without Skin.");
            return;
        }

        List<GameObject> models = FindCharacterModels();
        if (models.Count == 0)
        {
            Debug.LogError("No character models with a mesh found in the project.");
            return;
        }

        float walkSpeed = MeasuredSpeed(walk, FallbackWalk);
        float runSpeed = MeasuredSpeed(run, FallbackRun);

        AnimatorController controller = BuildController(idle, walk, run, walkSpeed, runSpeed);

        // ---- One prefab per character ---------------------------------------
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        List<GameObject> prefabs = new List<GameObject>();
        string names = "";

        foreach (GameObject model in models)
        {
            GameObject built = BuildPrefab(model, controller, walkSpeed, runSpeed);
            if (built == null) continue;

            prefabs.Add(built);
            names += "\n  " + built.name;
        }

        if (prefabs.Count == 0)
        {
            Debug.LogError("Failed to build any pedestrian prefabs.");
            return;
        }

        // ---- Spawner ---------------------------------------------------------
        GameObject spawnerGO = GameObject.Find(SpawnerName);
        if (spawnerGO == null)
        {
            spawnerGO = new GameObject(SpawnerName);
            Undo.RegisterCreatedObjectUndo(spawnerGO, "Create Pedestrian Spawner");
        }
        else
        {
            Undo.RegisterFullObjectHierarchyUndo(spawnerGO, "Repair Pedestrian Spawner");
        }

        PedestrianSpawner spawner = spawnerGO.GetComponent<PedestrianSpawner>();
        if (spawner == null) spawner = spawnerGO.AddComponent<PedestrianSpawner>();

        spawner.pedestrianPrefabs = prefabs.ToArray();

        EditorSceneManager.MarkSceneDirty(spawnerGO.scene);
        Selection.activeGameObject = spawnerGO;

        Debug.Log(
            "Pedestrians set up with " + prefabs.Count + " character(s):" + names + "\n" +
            "  Walk " + walkSpeed.ToString("F2") + " m/s, run " + runSpeed.ToString("F2") +
            " m/s — taken from the clips, so the feet do not slide\n" +
            "  Population " + spawner.population + ", spawning " +
            spawner.spawnRadiusMin + "–" + spawner.spawnRadiusMax + "m from the camera\n" +
            "They follow the main camera, so they appear around the car as you drive.");
    }

    // ------------------------------------------------------------- controller

    static AnimatorController BuildController(AnimationClip idle, AnimationClip walk,
                                              AnimationClip run, float walkSpeed, float runSpeed)
    {
        if (!AssetDatabase.IsValidFolder(AnimFolder))
            AssetDatabase.CreateFolder("Assets", "Animations");

        if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        BlendTree tree;
        controller.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;

        // Thresholds are the clips' own travel speeds. Matching them to how fast the
        // pedestrian actually moves is the whole trick behind feet that do not skate.
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, walkSpeed);
        tree.AddChild(run, runSpeed);

        EditorUtility.SetDirty(tree);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        return controller;
    }

    static float MeasuredSpeed(AnimationClip clip, float fallback)
    {
        float measured = clip.averageSpeed.magnitude;
        return measured < 0.05f ? fallback : measured;
    }

    // ----------------------------------------------------------------- prefab

    static GameObject BuildPrefab(GameObject modelAsset, AnimatorController controller,
                                  float walkSpeed, float runSpeed)
    {
        string modelName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(modelAsset));
        GameObject root = new GameObject("Pedestrian_" + modelName);

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);

        float height = MeasureHeight(model);
        if (height > 0.01f)
        {
            float factor = 1.8f / height;
            if (factor < 0.5f || factor > 2f) model.transform.localScale = Vector3.one * factor;
        }

        CharacterController cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);   // feet at the object's origin
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.4f;
        cc.skinWidth = 0.03f;

        Animator animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullCompletely;   // off-screen ones cost nothing

        PedestrianController pedestrian = root.AddComponent<PedestrianController>();
        pedestrian.animator = animator;
        pedestrian.walkSpeed = walkSpeed;
        pedestrian.fleeSpeed = runSpeed;

        string path = PrefabFolder + "/Pedestrian_" + modelName + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        return prefab;
    }

    // ------------------------------------------------------------------ assets

    /// <summary>Every character model with a mesh, promoted to Humanoid and textured.</summary>
    static List<GameObject> FindCharacterModels()
    {
        List<GameObject> found = new List<GameObject>();

        foreach (string guid in AssetDatabase.FindAssets("t:Model"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;

            // Animation-only downloads carry no mesh, so this also filters them out.
            if (asset.GetComponentInChildren<SkinnedMeshRenderer>() == null) continue;

            if (PrepareCharacterAsset(importer, path))
                asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (asset != null) found.Add(asset);
        }

        return found;
    }

    /// <summary>Humanoid rig plus extracted textures. Returns true if it reimported.</summary>
    static bool PrepareCharacterAsset(ModelImporter importer, string path)
    {
        bool changed = false;
        string name = Path.GetFileNameWithoutExtension(path);

        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            changed = true;
            Debug.Log("Promoted \"" + name + "\" to a Humanoid rig.");
        }

        if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription ||
            importer.materialLocation != ModelImporterMaterialLocation.External)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            changed = true;
        }

        if (!changed) return false;

        importer.SaveAndReimport();

        // Textures stay locked inside the FBX until extracted, and any material built
        // against them comes out blank white.
        string dir = Path.GetDirectoryName(path).Replace('\\', '/');
        string textureFolder = dir + "/" + name + "_Textures";

        if (!AssetDatabase.IsValidFolder(textureFolder))
            AssetDatabase.CreateFolder(dir, name + "_Textures");

        if (importer.ExtractTextures(textureFolder))
        {
            AssetDatabase.Refresh();
            importer.SaveAndReimport();
            Debug.Log("Extracted textures for \"" + name + "\".");
        }

        return true;
    }

    /// <summary>Finds an animation clip by file name, forcing Humanoid rig and looping.</summary>
    static AnimationClip FindClip(string keyword)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = Path.GetFileNameWithoutExtension(path);

            if (file.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null && asset.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                continue;      // that is a character, not an animation file

            PrepareClipAsset(path);

            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                AnimationClip clip = sub as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__")) continue;
                return clip;
            }
        }

        return null;
    }

    /// <summary>Humanoid rig and Loop Time. Neither is the default, and both are required.</summary>
    static void PrepareClipAsset(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        bool dirty = false;

        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            dirty = true;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        for (int i = 0; i < clips.Length; i++)
        {
            if (!clips[i].loopTime)
            {
                clips[i].loopTime = true;
                dirty = true;
            }
        }

        if (dirty)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Debug.Log("Prepared " + Path.GetFileName(path) + " — Humanoid rig, looping on.");
        }
    }

    static float MeasureHeight(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        return bounds.size.y;
    }
}
