using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;

/// <summary>
/// Editor side of the shuttle route: arming the recorder, turning a recorded file
/// into real waypoints, and building the objective HUD.
/// </summary>
public static class RouteSetup
{
    const string CarName = "PlayerCar";
    const string RouteName = "Route";
    const string CanvasName = "HUDCanvas";
    const string PanelName = "RouteHUD";
    const string RoutesFolder = "Routes";

    // --------------------------------------------------------------- recording

    [MenuItem("NCAT/Route/1. Arm Route Recording", false, 70)]
    public static void ArmRecording()
    {
        GameObject car = GameObject.Find(CarName);
        if (car == null) { Debug.LogError("No " + CarName + " in the scene."); return; }

        RouteRecorder recorder = car.GetComponent<RouteRecorder>();
        if (recorder == null) recorder = Undo.AddComponent<RouteRecorder>(car);

        Undo.RecordObject(recorder, "Arm Route Recording");
        recorder.recording = true;
        recorder.statusLabel = GetOrCreateRecordLabel();

        EditorUtility.SetDirty(recorder);
        EditorSceneManager.MarkSceneDirty(car.scene);

        Debug.Log(
            "Route recording armed.\n" +
            "  Press Play and drive the route you want.\n" +
            "  B          drop a waypoint here\n" +
            "  N          change what the next one will be\n" +
            "  Backspace  remove the last one\n" +
            "Finish with a FINISH waypoint, then press Stop: it saves automatically.\n" +
            "Then run NCAT → Route → 2. Import Recorded Route.");
    }

    [MenuItem("NCAT/Route/Disarm Route Recording", false, 71)]
    public static void DisarmRecording()
    {
        GameObject car = GameObject.Find(CarName);
        RouteRecorder recorder = car != null ? car.GetComponent<RouteRecorder>() : null;

        if (recorder == null) { Debug.Log("Recording was not armed."); return; }

        Undo.RecordObject(recorder, "Disarm Route Recording");
        recorder.recording = false;
        EditorUtility.SetDirty(recorder);

        Debug.Log("Route recording disarmed. Play normally.");
    }

    // ------------------------------------------------------------------ import

    [System.Serializable] class WaypointData
    {
        public string kind;
        public float x, y, z;
        public float radius;
        public float speedLimitMph;
    }

    [System.Serializable] class RouteData { public WaypointData[] waypoints; }

    [MenuItem("NCAT/Route/2. Import Recorded Route", false, 72)]
    public static void ImportRoute()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string folder = Path.Combine(root, RoutesFolder);

        if (!Directory.Exists(folder))
        {
            Debug.LogError("No " + RoutesFolder + " folder yet. Record a route first: " +
                           "NCAT → Route → 1. Arm Route Recording.");
            return;
        }

        string[] files = Directory.GetFiles(folder, "route_*.json").OrderBy(f => f).ToArray();

        if (files.Length == 0)
        {
            Debug.LogError("No route files in " + folder + ".");
            return;
        }

        string newest = files[files.Length - 1];
        RouteData data = JsonUtility.FromJson<RouteData>(File.ReadAllText(newest));

        if (data == null || data.waypoints == null || data.waypoints.Length == 0)
        {
            Debug.LogError("Could not read any waypoints from " + Path.GetFileName(newest) + ".");
            return;
        }

        GameObject existing = GameObject.Find(RouteName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        GameObject route = new GameObject(RouteName);
        Undo.RegisterCreatedObjectUndo(route, "Import Route");

        int finishCount = 0;

        for (int i = 0; i < data.waypoints.Length; i++)
        {
            WaypointData d = data.waypoints[i];

            RouteWaypoint.Kind kind = RouteWaypoint.Kind.Checkpoint;
            try { kind = (RouteWaypoint.Kind)System.Enum.Parse(typeof(RouteWaypoint.Kind), d.kind); }
            catch { Debug.LogWarning("Unknown waypoint kind \"" + d.kind + "\": treated as Checkpoint."); }

            GameObject go = new GameObject(
                string.Format("{0:00}_{1}", i + 1, kind));

            go.transform.SetParent(route.transform, false);
            go.transform.position = new Vector3(d.x, d.y, d.z);

            RouteWaypoint waypoint = go.AddComponent<RouteWaypoint>();
            waypoint.kind = kind;
            waypoint.radius = d.radius > 0f ? d.radius : 12f;
            waypoint.speedLimitMph = d.speedLimitMph > 0f ? d.speedLimitMph : 20f;

            // A sensible default so bus stops are worth stopping at without any
            // further editing.
            if (kind == RouteWaypoint.Kind.BusStop) waypoint.passengersWaiting = Random.Range(2, 7);
            if (kind == RouteWaypoint.Kind.Finish) finishCount++;
        }

        EditorSceneManager.MarkSceneDirty(route.scene);
        Selection.activeGameObject = route;

        Debug.Log(
            "Imported " + data.waypoints.Length + " waypoint(s) from " +
            Path.GetFileName(newest) + ".\n" +
            "  Reorder by dragging in the Hierarchy: order is the route order\n" +
            "  Select a waypoint to edit its kind, radius and speed limit\n" +
            (finishCount == 0
                ? "  WARNING: no Finish waypoint. The run will end when the last point is reached.\n"
                : "") +
            "Now run NCAT → Route → 3. Set Up Route HUD, then save (Ctrl+S).");
    }

    [MenuItem("NCAT/Route/Snap Route To Ground", false, 74)]
    public static void SnapToGround()
    {
        GameObject route = GameObject.Find(RouteName);
        if (route == null) { Debug.LogError("No Route object in the scene."); return; }

        int moved = 0;

        foreach (RouteWaypoint waypoint in route.GetComponentsInChildren<RouteWaypoint>())
        {
            // Start well above and cast down, so a waypoint recorded slightly inside
            // the terrain still finds the surface rather than missing it.
            Vector3 from = waypoint.transform.position + Vector3.up * 50f;

            RaycastHit hit;
            if (!Physics.Raycast(from, Vector3.down, out hit, 200f, ~0,
                                 QueryTriggerInteraction.Ignore)) continue;

            Undo.RecordObject(waypoint.transform, "Snap Route To Ground");
            waypoint.transform.position = hit.point;
            moved++;
        }

        Debug.Log("Snapped " + moved + " waypoint(s) to the terrain. " +
                  (moved == 0
                      ? "Nothing hit: the Cesium tiles near the route may not be loaded, " +
                        "or physics meshes are off (NCAT → Enable Cesium Physics Meshes)."
                      : ""));

        EditorSceneManager.MarkSceneDirty(route.scene);
    }

    // --------------------------------------------------------------------- HUD

    [MenuItem("NCAT/Route/3. Set Up Route HUD", false, 73)]
    public static void SetUpHud()
    {
        GameObject canvasGO = GetOrCreateCanvas();

        Transform existing = canvasGO.transform.Find(PanelName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject panel = new GameObject(PanelName, typeof(RectTransform));
        panel.transform.SetParent(canvasGO.transform, false);
        Undo.RegisterCreatedObjectUndo(panel, "Create Route HUD");

        // Top centre, clear of the minimap (bottom left) and the speedometer
        // (bottom right).
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -24f);
        panelRect.sizeDelta = new Vector2(560f, 118f);

        GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(panel.transform, false);
        RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = backdropRect.offsetMax = Vector2.zero;

        Image backdropImage = backdrop.GetComponent<Image>();
        backdropImage.color = new Color(0.05f, 0.06f, 0.08f, 0.62f);
        backdropImage.raycastTarget = false;

        Text progress = AddText(panel, "Progress", 14, FontStyle.Bold,
                                new Vector2(0f, -18f), new Vector2(540f, 20f));
        progress.text = "STOP 1 OF 1";
        progress.color = new Color(0.58f, 0.63f, 0.70f);

        Text objective = AddText(panel, "Objective", 26, FontStyle.Bold,
                                 new Vector2(0f, -46f), new Vector2(540f, 34f));
        objective.text = "-";
        objective.color = new Color(1f, 0.82f, 0.25f);

        Text distance = AddText(panel, "Distance", 20, FontStyle.Bold,
                                new Vector2(0f, -76f), new Vector2(540f, 26f));
        distance.text = "";

        // Alerts sit below the panel so a long message never widens it.
        Text alert = AddText(panel, "Alert", 20, FontStyle.Bold,
                             new Vector2(0f, -136f), new Vector2(760f, 28f));
        alert.text = "";
        alert.color = new Color(1f, 0.45f, 0.35f);

        // ---- off-screen direction arrow -----------------------------------------
        // Lives on the canvas rather than inside the panel, because it travels
        // around the whole screen edge.
        Transform staleArrow = canvasGO.transform.Find("RouteArrow");
        if (staleArrow != null) Undo.DestroyObjectImmediate(staleArrow.gameObject);

        GameObject arrowGO = new GameObject("RouteArrow", typeof(RectTransform), typeof(Image));
        arrowGO.transform.SetParent(canvasGO.transform, false);

        RectTransform arrowRect = arrowGO.GetComponent<RectTransform>();
        arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(0f, 0f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.sizeDelta = new Vector2(54f, 54f);

        Image arrowImage = arrowGO.GetComponent<Image>();
        arrowImage.sprite = GetOrCreateArrowSprite();
        arrowImage.raycastTarget = false;
        arrowGO.SetActive(false);

        // ---- runner -------------------------------------------------------------
        GameObject car = GameObject.Find(CarName);

        RouteRunner runner = Object.FindObjectOfType<RouteRunner>();
        if (runner == null)
        {
            GameObject holder = new GameObject("RouteRunner");
            Undo.RegisterCreatedObjectUndo(holder, "Create Route Runner");
            runner = holder.AddComponent<RouteRunner>();
        }

        Undo.RecordObject(runner, "Wire Route HUD");

        GameObject route = GameObject.Find(RouteName);
        runner.route = route != null ? route.transform : null;
        runner.vehicle = car != null ? car.transform : null;
        runner.scorer = car != null ? car.GetComponent<DriveScorer>() : null;
        runner.debrief = Object.FindObjectOfType<DebriefScreen>();
        runner.speedometer = Object.FindObjectOfType<Speedometer>();

        runner.objectiveLabel = objective;
        runner.distanceLabel = distance;
        runner.progressLabel = progress;
        runner.alertLabel = alert;

        // ---- in-world beacons ---------------------------------------------------
        // Without these the route is invisible while driving: waypoints draw with
        // Gizmos, and Gizmos never render in the Game view or in a build.
        RouteMarkers markers = runner.GetComponent<RouteMarkers>();
        if (markers == null) markers = Undo.AddComponent<RouteMarkers>(runner.gameObject);

        Undo.RecordObject(markers, "Wire Route Markers");
        markers.runner = runner;
        markers.offScreenArrow = arrowRect;
        EditorUtility.SetDirty(markers);

        EditorUtility.SetDirty(runner);
        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = runner.gameObject;

        int count = route != null ? route.GetComponentsInChildren<RouteWaypoint>().Length : 0;

        Debug.Log(
            "Route HUD set up.\n" +
            "  " + count + " waypoint(s) on the route\n" +
            "  Objective, distance and progress at the top of the screen\n" +
            (runner.debrief == null
                ? "  WARNING: no DebriefScreen: reaching the finish will not open a report.\n" +
                  "  Run NCAT → Set Up Drive Debrief.\n"
                : "  Reaching the Finish waypoint opens the debrief automatically\n") +
            (count == 0 ? "  WARNING: no waypoints. Import a recorded route first.\n" : "") +
            "Remember to disarm recording before a real run, and save the scene (Ctrl+S).");
    }

    // ------------------------------------------------------------------ helpers

    const string ArrowPath = "Assets/UI/RouteArrow.png";

    /// <summary>
    /// A chunky upward-pointing triangle, generated once so there is no art to
    /// download. It points up at zero rotation, which is what RouteMarkers assumes
    /// when it works out the angle.
    /// </summary>
    static Sprite GetOrCreateArrowSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");

        const int size = 128;
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float t = y / (float)(size - 1);

            // Wide at the base, tapering to a point at the top, with the bottom
            // fifth cut off so it reads as an arrowhead rather than a triangle.
            float halfWidth = Mathf.Lerp(size * 0.46f, 0f, t);
            float centre = (size - 1) * 0.5f;

            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Abs(x - centre);
                float alpha = t > 0.18f ? Mathf.Clamp01(halfWidth - distance) : 0f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();

        File.WriteAllBytes(ArrowPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(ArrowPath);

        TextureImporter importer = AssetImporter.GetAtPath(ArrowPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(ArrowPath);
    }

    static Text GetOrCreateRecordLabel()
    {
        GameObject canvasGO = GetOrCreateCanvas();

        Transform found = canvasGO.transform.Find("RouteRecordStatus");
        if (found != null) return found.GetComponent<Text>();

        GameObject go = new GameObject("RouteRecordStatus", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(canvasGO.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);      // bottom centre
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 22f);
        rect.sizeDelta = new Vector2(900f, 26f);

        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 15;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = new Color(1f, 0.35f, 0.30f);
        text.text = "REC";

        return text;
    }

    static Text AddText(GameObject parent, string name, int size, FontStyle style,
                        Vector2 position, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = new Color(0.92f, 0.94f, 0.96f);

        return text;
    }

    static GameObject GetOrCreateCanvas()
    {
        GameObject canvasGO = GameObject.Find(CanvasName);
        if (canvasGO != null) return canvasGO;

        canvasGO = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create HUD Canvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGO.AddComponent<GraphicRaycaster>();
        return canvasGO;
    }
}
