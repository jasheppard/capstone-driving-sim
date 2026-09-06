using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// One-click setup for the driving vehicle in the NC A&amp;T campus scene.
///
/// Safe to run repeatedly. If PlayerCar and CarCamera already exist it repairs
/// them in place rather than creating duplicates, so you never have to delete
/// anything between runs.
///
/// Lives in an Editor folder, so none of this ships in a build.
/// </summary>
public static class CampusCarSetup
{
    // Matches the PlayerCar Rigidbody from the original capstone-driving-sim project.
    const float CarMass = 1000f;
    const float CarDrag = 1f;
    const float CarAngularDrag = 5f;

    const string CarName = "PlayerCar";
    const string CamName = "CarCamera";

    // Roughly a real sedan in metres — the campus is real-world scale.
    static readonly Vector3 CarScale = new Vector3(2f, 1.5f, 4.5f);
    static readonly Vector3 CameraOffset = new Vector3(0f, 2.5f, -2.5f);

    [MenuItem("NCAT/Set Up Player Car", false, 0)]
    public static void SetUpPlayerCar()
    {
        GameObject car = GameObject.Find(CarName);
        bool repairing = car != null;

        if (!repairing)
        {
            car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            car.name = CarName;
            car.transform.localScale = CarScale;
            car.transform.position = GuessSpawnPoint();
            Undo.RegisterCreatedObjectUndo(car, "Create Player Car");
        }
        else
        {
            Undo.RegisterFullObjectHierarchyUndo(car, "Repair Player Car");
        }

        ConfigureCar(car);

        GameObject camGO = GameObject.Find(CamName);
        if (camGO == null)
        {
            camGO = new GameObject(CamName);
            camGO.transform.position = car.transform.position + CameraOffset;
            Undo.RegisterCreatedObjectUndo(camGO, "Create Car Camera");
        }
        else
        {
            Undo.RegisterFullObjectHierarchyUndo(camGO, "Repair Car Camera");
        }

        ConfigureCamera(camGO, car);
        DisableOtherCameras(camGO);

        int fixedTilesets = EnableCesiumPhysicsMeshes();

        EditorSceneManager.MarkSceneDirty(car.scene);
        Selection.activeGameObject = car;
        EditorGUIUtility.PingObject(car);

        SceneView sv = SceneView.lastActiveSceneView;
        if (sv != null) sv.FrameSelected();

        Debug.Log(
            (repairing ? "PlayerCar repaired" : "PlayerCar created") + " at " + car.transform.position + ".\n" +
            "Press Play. The car stays frozen until terrain streams in, then drops onto the ground.\n" +
            "W/S drive, A/D steer, Space brake, R respawn, C toggles first/third person.\n" +
            (fixedTilesets > 0
                ? "Enabled physics meshes on " + fixedTilesets + " Cesium tileset(s)."
                : "Cesium tilesets already had physics meshes enabled."));
    }

    static void ConfigureCar(GameObject car)
    {
        car.tag = "Player";                       // built-in tag; the zone triggers rely on it

        Collider col = car.GetComponent<Collider>();
        if (col == null) col = car.AddComponent<BoxCollider>();

        // Frictionless. The controller supplies grip in script, so real friction here
        // only makes the flat-bottomed box catch on every bump in the scanned terrain.
        col.material = GetOrCreateCarPhysicsMaterial();

        Rigidbody rb = car.GetComponent<Rigidbody>();
        if (rb == null) rb = car.AddComponent<Rigidbody>();

        rb.mass = CarMass;
        rb.drag = CarDrag;
        rb.angularDrag = CarAngularDrag;
        rb.useGravity = true;
        // Streamed terrain colliders arrive late; these stop the car tunnelling
        // through geometry at speed and keep motion smooth at 50 Hz physics.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (car.GetComponent<VehicleController>() == null)
            car.AddComponent<VehicleController>();

        // The important one: holds the car still until there is ground to land on.
        if (car.GetComponent<CarGroundSpawner>() == null)
            car.AddComponent<CarGroundSpawner>();
    }

    static void ConfigureCamera(GameObject camGO, GameObject car)
    {
        // Critical for Cesium: in the editor it streams tiles for the Scene view camera,
        // but in Play mode it looks for the camera tagged MainCamera. Without this tag
        // the tileset has nothing to stream for and the whole world unloads on Play.
        camGO.tag = "MainCamera";

        Camera cam = camGO.GetComponent<Camera>();
        if (cam == null) cam = camGO.AddComponent<Camera>();

        // Unity's default 1000 m far plane clips the far side of campus.
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 20000f;

        CarFollowCamera follow = camGO.GetComponent<CarFollowCamera>();
        if (follow == null) follow = camGO.AddComponent<CarFollowCamera>();

        follow.target = car.transform;
        follow.mode = CarFollowCamera.ViewMode.FirstPerson;
        ApplyCarFraming(follow);

        if (UnityEngine.Object.FindObjectOfType<AudioListener>() == null)
            camGO.AddComponent<AudioListener>();
    }

    /// <summary>
    /// A zero-friction physics material for the car's collider, created once as a
    /// project asset so it survives play sessions.
    /// </summary>
    static PhysicMaterial GetOrCreateCarPhysicsMaterial()
    {
        const string folder = "Assets/Settings";
        const string path = folder + "/CarPhysics.physicMaterial";

        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "Settings");

        PhysicMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
        if (mat == null)
        {
            mat = new PhysicMaterial("CarPhysics");
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.dynamicFriction = 0f;
        mat.staticFriction = 0f;
        mat.bounciness = 0f;
        mat.frictionCombine = PhysicMaterialCombine.Minimum;
        mat.bounceCombine = PhysicMaterialCombine.Minimum;

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>
    /// Puts MainMenu first and the campus scene second in Build Settings.
    /// Scene order matters: index 0 is what launches when you build the game,
    /// and SceneManager.LoadScene by name only works for scenes listed here.
    /// </summary>
    /// <summary>
    /// Pushes the recommended camera framing onto both cameras.
    /// Needed because Unity keeps values already serialised on a component in
    /// preference to new defaults in the script — so editing defaults alone
    /// changes nothing on objects that already exist in your scene.
    /// </summary>
    [MenuItem("NCAT/Apply Camera Framing", false, 10)]
    public static void ApplyCameraFraming()
    {
        int touched = 0;

        GameObject camGO = GameObject.Find(CamName);
        CarFollowCamera follow = camGO != null ? camGO.GetComponent<CarFollowCamera>() : null;
        if (follow != null)
        {
            Undo.RecordObject(follow, "Apply Camera Framing");
            ApplyCarFraming(follow);
            touched++;
        }

        if (touched > 0 && camGO != null) EditorSceneManager.MarkSceneDirty(camGO.scene);

        Debug.Log(touched > 0
            ? "Camera framing applied. Right stick orbits the car in third person, " +
              "full 360, and stays where you leave it."
            : "No CarCamera in this scene — run NCAT → Set Up Player Car first.");
    }

    static void ApplyCarFraming(CarFollowCamera follow)
    {
        follow.orbitDistance = 6f;
        follow.pivotHeight = 1.1f;
        follow.restPitch = 6f;
        follow.orbitMinPitch = -12f;
        follow.orbitMaxPitch = 55f;
        follow.firstPersonLookOnly = false;    // orbit in third person too
        follow.autoRecentre = false;   // stay where you leave it
        follow.orbitSmoothing = 12f;
    }

    /// <summary>
    /// Makes Play always begin at the main menu, whatever scene is open.
    ///
    /// Unity plays the currently open scene, not build index 0 — that ordering only
    /// applies to real builds. This overrides it for the editor, so you can work in
    /// the campus scene and still test the full flow from the menu.
    /// </summary>
    [MenuItem("NCAT/Always Start Play at Main Menu", false, 41)]
    public static void StartPlayAtMainMenu()
    {
        const string menuScene = "Assets/Scenes/MainMenu.unity";

        SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(menuScene);
        if (asset == null)
        {
            Debug.LogError("Could not find " + menuScene + ".");
            return;
        }

        EditorSceneManager.playModeStartScene = asset;
        Debug.Log("Play will now always start at MainMenu, whichever scene is open.\n" +
                  "Undo this with NCAT → Start Play at Current Scene.");
    }

    [MenuItem("NCAT/Start Play at Current Scene", false, 42)]
    public static void StartPlayAtCurrentScene()
    {
        EditorSceneManager.playModeStartScene = null;
        Debug.Log("Play will now start at whichever scene is open — Unity's default.");
    }

    [MenuItem("NCAT/Configure Build Scenes", false, 40)]
    public static void ConfigureBuildScenes()
    {
        string menuScene = "Assets/Scenes/MainMenu.unity";
        string gameScene = "Assets/Scenes/SampleScene.unity";

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(menuScene) == null)
        {
            Debug.LogError("Could not find " + menuScene + ". Copy MainMenu.unity into Assets/Scenes first.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(gameScene) == null)
        {
            Debug.LogError("Could not find " + gameScene + ".");
            return;
        }

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(menuScene, true),
            new EditorBuildSettingsScene(gameScene, true),
        };

        Debug.Log("Build scenes set:\n  0 — MainMenu (launches first)\n  1 — SampleScene (the campus)");
    }

    [MenuItem("NCAT/Enable Cesium Physics Meshes", false, 20)]
    public static void EnableCesiumPhysicsMeshesMenu()
    {
        int count = EnableCesiumPhysicsMeshes();
        Debug.Log(count > 0
            ? "Enabled physics meshes on " + count + " Cesium tileset(s). Reload the tileset to rebuild colliders."
            : "Found no Cesium tilesets that needed changing.");
    }

    /// <summary>
    /// Turns on collider generation for any Cesium3DTileset in the scene.
    /// Done by type name and SerializedProperty so this file never references
    /// the Cesium assembly — it still compiles if Cesium is absent or renamed.
    /// </summary>
    static int EnableCesiumPhysicsMeshes()
    {
        int changed = 0;
        string[] candidateFields = { "_createPhysicsMeshes", "createPhysicsMeshes" };

        foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "Cesium3DTileset") continue;

            SerializedObject so = new SerializedObject(mb);
            foreach (string field in candidateFields)
            {
                SerializedProperty prop = so.FindProperty(field);
                if (prop == null || prop.propertyType != SerializedPropertyType.Boolean) continue;

                if (!prop.boolValue)
                {
                    Undo.RecordObject(mb, "Enable Cesium Physics Meshes");
                    prop.boolValue = true;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(mb);
                    changed++;
                }
                break;
            }
        }

        return changed;
    }

    /// <summary>
    /// Turns off other cameras so the chase cam is the one you see.
    /// Disables the whole GameObject rather than just the Camera component, because
    /// Cesium's DynamicCamera also carries CesiumOriginShift — which keeps re-centering
    /// the world origin even when its camera is switched off.
    /// </summary>
    static void DisableOtherCameras(GameObject keep)
    {
        foreach (Camera cam in UnityEngine.Object.FindObjectsOfType<Camera>())
        {
            GameObject go = cam.gameObject;
            if (go == keep || !go.activeSelf) continue;

            bool shiftsOrigin = false;
            foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "CesiumOriginShift") shiftsOrigin = true;
            }

            Undo.RecordObject(go, "Disable Camera Object");
            go.SetActive(false);

            Debug.Log("Disabled \"" + go.name + "\" so " + CamName + " is the active view." +
                      (shiftsOrigin
                          ? " It had a CesiumOriginShift, which would have kept moving the world under the car."
                          : ""));
        }
    }

    /// <summary>Spawns where you are looking in the Scene view, so the car lands on visible ground.</summary>
    static Vector3 GuessSpawnPoint()
    {
        SceneView sv = SceneView.lastActiveSceneView;
        Vector3 origin = sv != null ? sv.pivot : Vector3.zero;

        if (Physics.Raycast(origin + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 1000f))
            return hit.point + Vector3.up * 1.5f;

        return origin + Vector3.up * 2f;
    }
}
