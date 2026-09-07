using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Wires up the campus boundary: the tile excluder that stops the world streaming
/// past a radius, and the soft wall that stops you driving past it.
/// </summary>
public static class CampusBoundarySetup
{
    const string CarName = "PlayerCar";
    const string CentreName = "CampusCentre";
    const string CanvasName = "HUDCanvas";
    const string LabelName = "BoundaryWarning";

    // Main campus is roughly 800 m across. The wall sits inside the streamed edge
    // so you are turned back while there is still ground to turn back onto.
    const float TileRadius = 850f;
    const float WallRadius = 780f;

    [MenuItem("NCAT/Set Up Campus Boundary", false, 60)]
    public static void SetUp()
    {
        GameObject tileset = FindTileset();

        if (tileset == null)
        {
            Debug.LogError("No Cesium3DTileset in the scene: nothing to limit.");
            return;
        }

        Transform centre = GetOrCreateCentre();

        // ---- limit what streams ------------------------------------------------
        CampusTileExcluder excluder = tileset.GetComponent<CampusTileExcluder>();
        if (excluder == null) excluder = Undo.AddComponent<CampusTileExcluder>(tileset);

        Undo.RecordObject(excluder, "Configure Campus Boundary");
        excluder.centre = centre;
        excluder.radius = TileRadius;
        EditorUtility.SetDirty(excluder);

        // ---- keep the car inside it -------------------------------------------
        GameObject car = GameObject.Find(CarName);

        if (car == null)
        {
            Debug.LogWarning("No " + CarName + " found. The map is limited, but " +
                             "nothing stops you driving off the edge of it yet.");
        }
        else
        {
            CampusBoundary boundary = car.GetComponent<CampusBoundary>();
            if (boundary == null) boundary = Undo.AddComponent<CampusBoundary>(car);

            Undo.RecordObject(boundary, "Configure Campus Boundary");
            boundary.centre = centre;
            boundary.radius = WallRadius;
            boundary.warningLabel = GetOrCreateLabel();
            EditorUtility.SetDirty(boundary);
        }

        EditorSceneManager.MarkSceneDirty(tileset.scene);
        Selection.activeGameObject = centre.gameObject;

        Debug.Log(
            "Campus boundary set up.\n" +
            "  Tiles stream out to " + TileRadius + " m from " + CentreName + "\n" +
            "  The car is turned back at " + WallRadius + " m\n" +
            "Move " + CentreName + " to recentre the campus, and tune both radii in " +
            "the Inspector: the circles draw in the Scene view when selected.\n" +
            "Save the scene (Ctrl+S) or none of this survives.");
    }

    /// <summary>
    /// Found by component name rather than by type, so this file does not need to
    /// reference the Cesium assembly directly.
    /// </summary>
    static GameObject FindTileset()
    {
        foreach (MonoBehaviour mb in Object.FindObjectsOfType<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name == "Cesium3DTileset") return mb.gameObject;
        }

        return null;
    }

    static Transform GetOrCreateCentre()
    {
        GameObject existing = GameObject.Find(CentreName);
        if (existing != null) return existing.transform;

        GameObject centre = new GameObject(CentreName);
        Undo.RegisterCreatedObjectUndo(centre, "Create Campus Centre");

        // The car's spawn is the best guess at the middle of the playable area.
        GameObject car = GameObject.Find(CarName);
        centre.transform.position = car != null ? car.transform.position : Vector3.zero;

        return centre.transform;
    }

    static Text GetOrCreateLabel()
    {
        GameObject canvasGO = GameObject.Find(CanvasName);

        if (canvasGO == null)
        {
            canvasGO = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create HUD Canvas");

            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvasGO.AddComponent<GraphicRaycaster>();
        }

        Transform found = canvasGO.transform.Find(LabelName);
        if (found != null) return found.GetComponent<Text>();

        GameObject labelGO = new GameObject(LabelName, typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(canvasGO.transform, false);
        Undo.RegisterCreatedObjectUndo(labelGO, "Create Boundary Warning");

        RectTransform rect = labelGO.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);      // top centre
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -40f);
        rect.sizeDelta = new Vector2(700f, 50f);

        Text text = labelGO.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 30;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.text = "Approaching campus boundary";
        text.enabled = false;

        return text;
    }
}
