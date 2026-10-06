using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// Attaches a car model to the PlayerCar cube.
///
/// The cube keeps the Rigidbody, collider, VehicleController and CarGroundSpawner;
/// the model is cosmetic. Nothing about the driving changes.
///
/// One thing this has to fix that the character setup did not: PlayerCar's transform
/// is scaled non-uniformly (2 x 1.5 x 4.5) to shape the box. Any child inherits that
/// and comes out stretched. So the cube's scale is normalised to 1 and the BoxCollider
/// is given those dimensions directly instead — identical physics, undistorted children.
/// </summary>
public static class VehicleModelSetup
{
    const string CarName = "PlayerCar";
    const string ModelChildName = "Model";

    // The chassis box, in metres. Roughly a sedan.
    static readonly Vector3 ChassisSize = new Vector3(2f, 1.5f, 4.5f);

    [MenuItem("NCAT/Attach Vehicle Model", false, 21)]
    public static void AttachSelectedModel()
    {
        GameObject modelAsset = Selection.activeObject as GameObject;
        string assetPath = modelAsset != null ? AssetDatabase.GetAssetPath(modelAsset) : null;

        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog(
                "Select the car model first",
                "Click the imported car (the .fbx, .obj or prefab) in the Project panel, then run this again.",
                "OK");
            return;
        }

        GameObject car = GameObject.Find(CarName);
        if (car == null)
        {
            Debug.LogError("No " + CarName + " in the scene. Run NCAT → Set Up Player Car first.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(car, "Attach Vehicle Model");

        NormaliseChassis(car);
        PrepareModelAsset(assetPath);

        modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

        Transform existing = car.transform.Find(ModelChildName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        instance.name = ModelChildName;
        Undo.RegisterCreatedObjectUndo(instance, "Attach Vehicle Model");

        instance.transform.SetParent(car.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        // ---- Orientation ---------------------------------------------------
        // A car is longer than it is wide. If the bounds say otherwise, the model
        // was authored facing sideways, so turn it a quarter turn.
        Bounds bounds = LocalBounds(instance);
        if (bounds.size.x > bounds.size.z)
        {
            instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            bounds = LocalBounds(instance);
            Debug.Log("Model was wider than it was long: rotated 90 degrees so it points down +Z.");
        }

        // ---- Fit model and chassis together ---------------------------------
        // A car-sized model gets scaled to the standard chassis. Anything much
        // bigger — a bus, a truck — keeps its own size and the chassis grows to
        // match instead. Shrinking a 12m bus down to a 4.5m sedan would be the
        // wrong way round.
        Vector3 chassis = ChassisSize;
        float modelLength = bounds.size.z;
        bool oversized = modelLength > ChassisSize.z * 1.6f || modelLength < ChassisSize.z * 0.6f;

        if (!oversized && modelLength > 0.01f)
        {
            float factor = ChassisSize.z / modelLength;
            instance.transform.localScale = Vector3.one * factor;
            bounds = LocalBounds(instance);

            Debug.Log("Model measured " + modelLength.ToString("F2") +
                      "m long: scaled by " + factor.ToString("F3") +
                      " to match the " + ChassisSize.z + "m chassis.");
        }
        else if (modelLength > 0.01f)
        {
            chassis = new Vector3(
                Mathf.Max(bounds.size.x, 1f),
                Mathf.Max(bounds.size.y, 1f),
                modelLength);

            ResizeChassis(car, chassis);

            Debug.Log("Model is " + modelLength.ToString("F2") + "m long: kept at its own " +
                      "scale and the chassis grew to " + chassis + " to fit it.");
        }

        // ---- Sit the wheels on the bottom of the box ------------------------
        Reseat(car, instance.transform);

        // ---- Hide the placeholder cube --------------------------------------
        MeshRenderer cubeRenderer = car.GetComponent<MeshRenderer>();
        if (cubeRenderer != null && cubeRenderer.enabled)
        {
            Undo.RecordObject(cubeRenderer, "Hide Cube");
            cubeRenderer.enabled = false;
        }

        EditorSceneManager.MarkSceneDirty(car.scene);
        Selection.activeGameObject = car;

        Debug.Log("Vehicle model attached to " + CarName + ".\n" +
                  "If it drives backwards, run NCAT → Flip Vehicle Model.");
    }

    /// <summary>For models authored facing -Z. Bounds cannot detect this, only your eyes can.</summary>
    [MenuItem("NCAT/Flip Vehicle Model", false, 22)]
    public static void FlipVehicleModel() { Rotate(0f, 180f, 0f, "Flipped 180 degrees: it now faces the other way."); }

    // Models authored Z-up — common out of Blender and 3ds Max — arrive standing on
    // end or upside down. Unity's importer usually corrects this, but not always.

    [MenuItem("NCAT/Rotate Vehicle - Pitch -90", false, 24)]
    public static void PitchDown() { Rotate(-90f, 0f, 0f, "Pitched -90 degrees. Typical fix for a Z-up export."); }

    [MenuItem("NCAT/Rotate Vehicle - Pitch +90", false, 23)]
    public static void PitchUp() { Rotate(90f, 0f, 0f, "Pitched +90 degrees."); }

    [MenuItem("NCAT/Rotate Vehicle - Roll 180", false, 27)]
    public static void Roll() { Rotate(0f, 0f, 180f, "Rolled 180 degrees: that is the upside-down case."); }

    [MenuItem("NCAT/Rotate Vehicle - Yaw +90", false, 25)]
    public static void YawRight() { Rotate(0f, 90f, 0f, "Turned 90 degrees right."); }

    [MenuItem("NCAT/Rotate Vehicle - Yaw -90", false, 26)]
    public static void YawLeft() { Rotate(0f, -90f, 0f, "Turned 90 degrees left."); }

    /// <summary>Rotates the model and re-seats it, since turning it moves where its wheels are.</summary>
    static void Rotate(float pitch, float yaw, float roll, string message)
    {
        GameObject car = GameObject.Find(CarName);
        Transform model = car != null ? car.transform.Find(ModelChildName) : null;

        if (model == null)
        {
            Debug.LogError("No " + ModelChildName + " under " + CarName + ".");
            return;
        }

        Undo.RecordObject(model, "Rotate Vehicle Model");

        // Pre-multiply, so the rotation happens around the CAR's axes rather than
        // the model's own. Post-multiplying rotates in the model's local frame —
        // and once a model has been pitched upright, its local Y is lying on its
        // side, so a "yaw" tips the vehicle onto its doors instead of turning it.
        model.localRotation = Quaternion.Euler(pitch, yaw, roll) * model.localRotation;

        Reseat(car, model);

        EditorSceneManager.MarkSceneDirty(car.scene);
        Debug.Log(message + " Rotation is now " + model.localEulerAngles + ".");
    }

    /// <summary>
    /// Drops the model so its lowest point rests on the bottom of the chassis box,
    /// and centres it horizontally.
    ///
    /// Measured in the car's own space and applied as a relative shift, so it stays
    /// correct whatever rotation the model is currently under — the earlier version
    /// mixed the model's rotated frame with the parent's and only happened to work
    /// for yaw.
    /// </summary>
    static void Reseat(GameObject car, Transform model)
    {
        BoxCollider box = car.GetComponent<BoxCollider>();
        Vector3 chassis = box != null ? box.size : ChassisSize;

        Bounds bounds = BoundsInParentSpace(model.gameObject, car.transform);

        Vector3 position = model.localPosition;
        position.x += -bounds.center.x;
        position.y += (-chassis.y * 0.5f) - bounds.min.y;
        position.z += -bounds.center.z;

        model.localPosition = position;
    }

    /// <summary>Renderer bounds expressed in the parent's local space.</summary>
    static Bounds BoundsInParentSpace(GameObject instance, Transform parent)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

        Matrix4x4 toParent = parent.worldToLocalMatrix;
        Bounds result = new Bounds();
        bool started = false;

        foreach (Renderer renderer in renderers)
        {
            Vector3 min = renderer.bounds.min;
            Vector3 max = renderer.bounds.max;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);

                Vector3 local = toParent.MultiplyPoint3x4(corner);

                if (!started) { result = new Bounds(local, Vector3.zero); started = true; }
                else result.Encapsulate(local);
            }
        }

        return result;
    }

    /// <summary>
    /// Grows the collider to fit a larger vehicle, and scales mass with it so a bus
    /// does not handle like a hatchback.
    /// </summary>
    static void ResizeChassis(GameObject car, Vector3 size)
    {
        BoxCollider box = car.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.size = size;
            box.center = Vector3.zero;
        }

        Rigidbody rb = car.GetComponent<Rigidbody>();
        if (rb == null) return;

        float baseVolume = ChassisSize.x * ChassisSize.y * ChassisSize.z;
        float newVolume = size.x * size.y * size.z;
        if (baseVolume <= 0.01f) return;

        float ratio = Mathf.Clamp(newVolume / baseVolume, 1f, 8f);
        rb.mass = 1000f * ratio;

        Debug.Log("Chassis mass scaled to " + rb.mass.ToString("F0") + " kg. " +
                  "Raise Acceleration Force on the VehicleController to suit: " +
                  "the default 15000 was tuned for a 1000 kg car.");
    }

    /// <summary>
    /// Moves the box dimensions off the transform and onto the collider, so child
    /// models are not stretched by a non-uniform parent scale.
    /// </summary>
    static void NormaliseChassis(GameObject car)
    {
        if (car.transform.localScale == Vector3.one) return;

        car.transform.localScale = Vector3.one;

        BoxCollider box = car.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.size = ChassisSize;
            box.center = Vector3.zero;
        }

        Debug.Log("Normalised " + CarName + " scale to 1 and moved the " + ChassisSize +
                  " chassis onto the BoxCollider. Physics is unchanged; children no longer stretch.");
    }

    /// <summary>Textures out of the file, materials as real assets, no animation rig.</summary>
    static void PrepareModelAsset(string assetPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null) return;      // already a prefab; nothing to configure

        string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        string name = Path.GetFileNameWithoutExtension(assetPath);
        string textureFolder = dir + "/" + name + "_Textures";

        importer.animationType = ModelImporterAnimationType.None;   // it is a car
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.SaveAndReimport();

        if (!AssetDatabase.IsValidFolder(textureFolder))
            AssetDatabase.CreateFolder(dir, name + "_Textures");

        if (importer.ExtractTextures(textureFolder))
        {
            AssetDatabase.Refresh();
            importer.SaveAndReimport();
            Debug.Log("Extracted embedded textures to " + textureFolder + ".");
        }
    }

    /// <summary>Renderer bounds expressed in the instance's own local space.</summary>
    static Bounds LocalBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

        Matrix4x4 toLocal = instance.transform.worldToLocalMatrix;
        Bounds result = new Bounds(toLocal.MultiplyPoint3x4(renderers[0].bounds.center), Vector3.zero);

        foreach (Renderer renderer in renderers)
        {
            Bounds b = renderer.bounds;
            Vector3 min = b.min;
            Vector3 max = b.max;

            // Transform all eight corners so rotation is accounted for.
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);

                result.Encapsulate(toLocal.MultiplyPoint3x4(corner));
            }
        }

        return result;
    }
}
