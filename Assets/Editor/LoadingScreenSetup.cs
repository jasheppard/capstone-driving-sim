using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Reflection;

/// <summary>
/// Builds the loading screen, and tunes the tileset's streaming settings.
/// </summary>
public static class LoadingScreenSetup
{
    const string CanvasName = "LoadingCanvas";
    const string ScreenName = "LoadingScreen";

    [MenuItem("NCAT/Set Up Loading Screen", false, 37)]
    public static void SetUp()
    {
        GameObject existing = GameObject.Find(CanvasName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        // ---- canvas ------------------------------------------------------------
        GameObject canvasGO = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Loading Canvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;                 // above the HUD

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGO.AddComponent<GraphicRaycaster>();

        // ---- full-screen backdrop ----------------------------------------------
        GameObject screen = new GameObject(ScreenName,
            typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        screen.transform.SetParent(canvasGO.transform, false);

        RectTransform screenRect = screen.GetComponent<RectTransform>();
        Stretch(screenRect);

        Image backdrop = screen.GetComponent<Image>();
        backdrop.color = new Color(0.05f, 0.06f, 0.08f, 1f);

        // ---- text --------------------------------------------------------------
        Text title = AddText(screen, "Title", 52, FontStyle.Bold,
                             new Vector2(0f, 90f), new Vector2(1200f, 70f));
        title.text = "NORTH CAROLINA A&T";
        title.color = new Color(0.94f, 0.96f, 0.98f);

        Text subtitle = AddText(screen, "Subtitle", 22, FontStyle.Normal,
                                new Vector2(0f, 40f), new Vector2(1200f, 34f));
        subtitle.text = "Campus Driving Safety Simulator";
        subtitle.color = new Color(1f, 0.82f, 0.25f);

        // ---- progress bar ------------------------------------------------------
        GameObject track = new GameObject("BarTrack", typeof(RectTransform), typeof(Image));
        track.transform.SetParent(screen.transform, false);

        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 0.5f);
        trackRect.anchoredPosition = new Vector2(0f, -40f);
        trackRect.sizeDelta = new Vector2(620f, 10f);

        track.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.22f);

        GameObject fill = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(track.transform, false);

        RectTransform fillRect = fill.GetComponent<RectTransform>();
        Stretch(fillRect);

        Image fillImage = fill.GetComponent<Image>();
        fillImage.color = new Color(1f, 0.82f, 0.25f);

        // A plain white sprite is needed for fillAmount to work at all — with no
        // sprite, Image draws the whole rect and ignores the fill.
        fillImage.sprite = WhiteSprite();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 0f;

        Text percent = AddText(screen, "Percent", 20, FontStyle.Bold,
                               new Vector2(0f, -76f), new Vector2(300f, 30f));
        percent.text = "0%";
        percent.color = new Color(0.85f, 0.88f, 0.92f);

        Text status = AddText(screen, "Status", 16, FontStyle.Normal,
                              new Vector2(0f, -104f), new Vector2(700f, 26f));
        status.text = "Connecting to Cesium ion";
        status.color = new Color(0.55f, 0.60f, 0.66f);

        Text hint = AddText(screen, "Hint", 14, FontStyle.Italic,
                            new Vector2(0f, -190f), new Vector2(1000f, 26f));
        hint.text = "Terrain streams from Google Photorealistic 3D Tiles: " +
                    "first load depends on your connection";
        hint.color = new Color(0.40f, 0.44f, 0.50f);

        // ---- wire it up --------------------------------------------------------
        CesiumLoadingScreen loading = screen.AddComponent<CesiumLoadingScreen>();
        loading.group = screen.GetComponent<CanvasGroup>();
        loading.progressBar = fillImage;
        loading.percentLabel = percent;
        loading.statusLabel = status;

        EditorUtility.SetDirty(loading);

        int tuned = TuneTileset();

        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = screen;

        Debug.Log(
            "Loading screen set up.\n" +
            "  Waits until 92% of the view has streamed, then fades out\n" +
            "  Driving is disabled until then, so you cannot drive into empty space\n" +
            "  Gives up after 45s so a bad connection cannot lock you out\n" +
            "  Tuned " + tuned + " streaming setting(s) on the tileset\n" +
            "Save the scene (Ctrl+S).");
    }

    /// <summary>
    /// Nudges the tileset's streaming settings toward loading the near view faster.
    ///
    /// Set through the public properties by reflection, so no reference to the
    /// Cesium assembly is needed and the file still compiles without it.
    /// </summary>
    static int TuneTileset()
    {
        MonoBehaviour tileset = null;

        foreach (MonoBehaviour mb in Object.FindObjectsOfType<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name == "Cesium3DTileset") { tileset = mb; break; }
        }

        if (tileset == null)
        {
            Debug.LogWarning("No Cesium3DTileset in the scene: skipped streaming tuning.");
            return 0;
        }

        Undo.RecordObject(tileset, "Tune Tileset Streaming");

        int applied = 0;

        // More requests in flight fills the view sooner. Cesium warns that pushing
        // this far above the default trades frame time for load time, so this is a
        // moderate step up rather than a large one.
        applied += TrySet(tileset, "maximumSimultaneousTileLoads", 32u);

        // Keep more of what has already been fetched in memory. Since there is no
        // disk cache, anything evicted has to be downloaded again.
        applied += TrySet(tileset, "maximumCachedBytes", 1536L * 1024L * 1024L);

        // Load the tiles just outside the view too, so turning round does not
        // trigger a fresh wait. This is the same problem the minimap had.
        applied += TrySet(tileset, "preloadAncestors", true);
        applied += TrySet(tileset, "preloadSiblings", true);

        // Draw a coarse parent rather than a hole while children are still coming.
        applied += TrySet(tileset, "forbidHoles", false);

        EditorUtility.SetDirty(tileset);
        return applied;
    }

    static int TrySet(object target, string propertyName, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName, BindingFlags.Public | BindingFlags.Instance);

        if (property == null || !property.CanWrite)
        {
            Debug.LogWarning("Could not set " + propertyName +
                             ": not present in this Cesium version. Set it by hand " +
                             "in the Inspector if you want it.");
            return 0;
        }

        try
        {
            property.SetValue(target, System.Convert.ChangeType(value, property.PropertyType));
            return 1;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Could not set " + propertyName + ": " + e.Message);
            return 0;
        }
    }

    // ------------------------------------------------------------------ helpers

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static Text AddText(GameObject parent, string name, int size, FontStyle style,
                        Vector2 position, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;

        return text;
    }

    /// <summary>Unity's built-in white sprite, needed for a filled Image to work.</summary>
    static Sprite WhiteSprite()
    {
        Sprite builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (builtin != null) return builtin;

        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
    }
}
