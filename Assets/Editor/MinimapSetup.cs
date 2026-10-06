using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// Builds the HUD minimap: a top-down camera rendering to a texture, and a circular
/// panel in the corner of the screen showing it.
///
/// The circle mask and the heading arrow are generated as PNGs the first time this
/// runs, so there are no art assets to download or lose.
/// </summary>
public static class MinimapSetup
{
    const string CanvasName = "HUDCanvas";
    const string MinimapName = "Minimap";
    const string CameraName = "MinimapCamera";
    const string UIFolder = "Assets/UI";
    const string CirclePath = UIFolder + "/MinimapCircle.png";
    const string ArrowPath = UIFolder + "/MinimapArrow.png";

    const float PanelSize = 220f;

    // Wider view, and enough resolution to keep it legible at that scale.
    const float ViewRadius = 130f;
    const float CameraHeight = 250f;
    const int TextureSize = 512;

    [MenuItem("NCAT/Set Up Minimap", false, 51)]
    public static void SetUpMinimap()
    {
        Sprite circle = GetOrCreateSprite(CirclePath, DrawCircle);
        Sprite arrowSprite = GetOrCreateSprite(ArrowPath, DrawArrow);

        // ---- Camera ----------------------------------------------------------
        GameObject camGO = GameObject.Find(CameraName);
        if (camGO == null)
        {
            camGO = new GameObject(CameraName);
            Undo.RegisterCreatedObjectUndo(camGO, "Create Minimap Camera");
        }

        Camera cam = camGO.GetComponent<Camera>();
        if (cam == null) cam = camGO.AddComponent<Camera>();
        cam.depth = -10;                        // renders before the main camera

        // An AudioListener here would fight the gameplay camera's.
        AudioListener stray = camGO.GetComponent<AudioListener>();
        if (stray != null) Undo.DestroyObjectImmediate(stray);

        MinimapCamera minimap = camGO.GetComponent<MinimapCamera>();
        if (minimap == null) minimap = camGO.AddComponent<MinimapCamera>();

        // Set explicitly rather than relying on script defaults, which Unity ignores
        // once a component has serialised values of its own.
        minimap.viewRadius = ViewRadius;
        minimap.height = CameraHeight;
        minimap.textureSize = TextureSize;

        // ---- UI ---------------------------------------------------------------
        GameObject canvasGO = GetOrCreateCanvas();

        Transform existing = canvasGO.transform.Find(MinimapName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject panel = new GameObject(MinimapName, typeof(RectTransform));
        panel.transform.SetParent(canvasGO.transform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 0f);   // bottom-left
        panelRect.pivot = new Vector2(0f, 0f);
        panelRect.anchoredPosition = new Vector2(28f, 28f);
        panelRect.sizeDelta = new Vector2(PanelSize, PanelSize);

        // Circle image doubles as the mask, so the square render texture reads round.
        Image maskImage = panel.AddComponent<Image>();
        maskImage.sprite = circle;
        maskImage.color = Color.white;
        maskImage.raycastTarget = false;

        Mask mask = panel.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject view = new GameObject("MapView", typeof(RectTransform), typeof(RawImage));
        view.transform.SetParent(panel.transform, false);

        RectTransform viewRect = view.GetComponent<RectTransform>();
        viewRect.anchorMin = Vector2.zero;
        viewRect.anchorMax = Vector2.one;
        viewRect.offsetMin = viewRect.offsetMax = Vector2.zero;

        RawImage display = view.GetComponent<RawImage>();
        display.raycastTarget = false;
        // Dark until the camera fills it at runtime, so an empty map reads as
        // "not rendering yet" rather than a white disc.
        display.color = new Color(0.08f, 0.09f, 0.11f);

        // Arrow sits outside the mask so it is never clipped at the edges.
        GameObject arrowGO = new GameObject("HeadingArrow", typeof(RectTransform), typeof(Image));
        arrowGO.transform.SetParent(panel.transform.parent, false);
        arrowGO.transform.SetSiblingIndex(panel.transform.GetSiblingIndex() + 1);

        RectTransform arrowRect = arrowGO.GetComponent<RectTransform>();
        arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(0f, 0f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.anchoredPosition = panelRect.anchoredPosition + new Vector2(PanelSize * 0.5f, PanelSize * 0.5f);
        arrowRect.sizeDelta = new Vector2(22f, 22f);

        Image arrowImage = arrowGO.GetComponent<Image>();
        arrowImage.sprite = arrowSprite;
        arrowImage.color = new Color(1f, 0.85f, 0.2f);
        arrowImage.raycastTarget = false;

        Undo.RegisterCreatedObjectUndo(panel, "Create Minimap");
        Undo.RegisterCreatedObjectUndo(arrowGO, "Create Minimap Arrow");

        // ---- Wire it together --------------------------------------------------
        Undo.RecordObject(minimap, "Wire Minimap");
        minimap.display = display;
        minimap.arrow = arrowRect;

        GameObject car = GameObject.Find("PlayerCar");
        if (car != null) minimap.headingSource = car.transform;

        RegisterWithCesium(cam);

        EditorUtility.SetDirty(minimap);
        EditorUtility.SetDirty(display);

        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = camGO;

        Debug.Log(
            "Minimap set up.\n" +
            "  Bottom-left of the screen, " + minimap.viewRadius + "m radius at " +
            minimap.textureSize + "px, north-up\n" +
            "  Arrow shows " + (car != null ? "PlayerCar's" : "the camera's") + " heading\n" +
            "Tune View Radius on the MinimapCamera to zoom; it updates live while playing.");
    }

    /// <summary>
    /// Adds the minimap camera to Cesium's camera manager.
    ///
    /// Cesium picks which tiles to stream based on the cameras it is told about —
    /// by default just the one tagged MainCamera. An unregistered camera therefore
    /// renders only whatever the main camera happened to need, which is why parts of
    /// the map go black as you drive into ground the gameplay view has not required.
    ///
    /// Done by type name and SerializedProperty so this file never references the
    /// Cesium assembly and still compiles without it.
    /// </summary>
    static void RegisterWithCesium(Camera cam)
    {
        MonoBehaviour manager = null;

        foreach (MonoBehaviour mb in Object.FindObjectsOfType<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name == "CesiumCameraManager")
            {
                manager = mb;
                break;
            }
        }

        if (manager == null)
        {
            System.Type type = null;
            foreach (System.Reflection.Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("CesiumForUnity.CesiumCameraManager");
                if (type != null) break;
            }

            if (type == null)
            {
                Debug.Log("No Cesium in this project: skipping camera registration.");
                return;
            }

            GameObject holder = new GameObject("CesiumCameraManager");
            manager = holder.AddComponent(type) as MonoBehaviour;
            Undo.RegisterCreatedObjectUndo(holder, "Create Cesium Camera Manager");
        }

        SerializedObject so = new SerializedObject(manager);

        SerializedProperty list = so.FindProperty("_additionalCameras");
        if (list == null) list = so.FindProperty("additionalCameras");

        if (list == null || !list.isArray)
        {
            Debug.LogWarning("Could not find the additional cameras list on CesiumCameraManager. " +
                             "Add the MinimapCamera to it by hand to stop the map going black.");
            return;
        }

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == cam) return;   // already there
        }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = cam;

        SerializedProperty useMain = so.FindProperty("_useMainCamera");
        if (useMain != null) useMain.boolValue = true;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        Debug.Log("Registered the minimap camera with Cesium, so it streams tiles for " +
                  "the map view as well as the gameplay view.");
    }

    // -------------------------------------------------------------------- canvas

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

    // ------------------------------------------------------------------- sprites

    delegate Color32[] Painter(int size);

    static Sprite GetOrCreateSprite(string path, Painter painter)
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(UIFolder)) AssetDatabase.CreateFolder("Assets", "UI");

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(painter(size));
        texture.Apply();

        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>A soft-edged white disc, used as the minimap's mask.</summary>
    static Color32[] DrawCircle(int size)
    {
        Color32[] pixels = new Color32[size * size];
        float centre = (size - 1) * 0.5f;
        float radius = centre - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - centre;
                float dy = y - centre;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                // One pixel of falloff, so the rim is not jagged.
                float alpha = Mathf.Clamp01(radius - distance);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        return pixels;
    }

    /// <summary>An upward-pointing triangle for the heading indicator.</summary>
    static Color32[] DrawArrow(int size)
    {
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            // Width tapers to nothing at the top.
            float t = y / (float)(size - 1);
            float halfWidth = Mathf.Lerp(size * 0.42f, 0f, t);
            float centre = (size - 1) * 0.5f;

            for (int x = 0; x < size; x++)
            {
                bool inside = Mathf.Abs(x - centre) <= halfWidth && t > 0.12f;
                pixels[y * size + x] = inside
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(255, 255, 255, 0);
            }
        }

        return pixels;
    }
}
