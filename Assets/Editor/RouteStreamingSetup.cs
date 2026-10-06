using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Puts the route-shaped tile excluder where Cesium will actually look for it.
///
/// That placement is the whole reason this is a menu item rather than an instruction.
/// Cesium only reads excluders from the GameObject holding the Cesium3DTileset or from
/// its parents; put one anywhere else and it sits there doing nothing, with no warning
/// and no error. The existing campus excluder is already in the right place, so this
/// follows it.
/// </summary>
public static class RouteStreamingSetup
{
    [MenuItem("NCAT/Load Only Around The Route", false, 62)]
    public static void Apply()
    {
        CampusTileExcluder campus = Object.FindObjectOfType<CampusTileExcluder>(true);

        GameObject host = campus != null ? campus.gameObject : FindTilesetHost();

        if (host == null)
        {
            Debug.LogError(
                "[Streaming] Could not find the Cesium tileset or the campus excluder, " +
                "so there is nowhere valid to put this.\n" +
                "  Add RouteTileExcluder by hand to the object holding your " +
                "Cesium3DTileset, or to the CesiumGeoreference above it.");
            return;
        }

        RouteTileExcluder excluder = host.GetComponent<RouteTileExcluder>();
        if (excluder == null) excluder = Undo.AddComponent<RouteTileExcluder>(host);

        GameObject routeGO = GameObject.Find("Route");
        if (routeGO != null) excluder.route = routeGO.transform;

        // Written explicitly: a component already in the scene keeps what was
        // serialised for it and ignores a change to the defaults.
        excluder.margin = 150f;
        excluder.sampleSpacing = 50f;
        excluder.coreRadius = 260f;
        excluder.loadEverythingWithoutARoute = true;

        // The campus circle already knows where campus is, so borrow its centre
        // rather than asking for it again.
        if (campus != null) excluder.coreCentre = campus.centre;

        EditorUtility.SetDirty(excluder);
        EditorSceneManager.MarkSceneDirty(host.scene);
        Selection.activeGameObject = host;

        int checkpoints = 0;

        if (routeGO != null)
        {
            for (int i = 0; i < routeGO.transform.childCount; i++)
            {
                RouteWaypoint w = routeGO.transform.GetChild(i).GetComponent<RouteWaypoint>();
                if (w != null && w.kind == RouteWaypoint.Kind.Checkpoint) checkpoints++;
            }
        }

        Debug.Log(
            "Streaming now follows the route.\n" +
            "  on " + host.name + ", where Cesium looks for excluders\n" +
            "  " + checkpoints + " checkpoints, kept to 150 m either side\n" +
            "  plus the campus core kept whole, 260 m around the georeference origin\n" +
            (campus != null
                ? "  The campus circle (" + campus.radius.ToString("F0") + " m) is still " +
                  "there and still applies: a tile is dropped if EITHER says so, so the\n" +
                  "  circle stays as the outer limit and this trims inside it\n"
                : "") +
            "  Select " + host.name + " to see the kept corridor drawn in the Scene view\n" +
            "  No route, or no checkpoints, and this loads everything rather than\n" +
            "  nothing: a scene that silently refuses to stream looks broken\n\n" +
            "  Margin is the dial. Too small and you drive down a tunnel; too large\n" +
            "  and you may as well have kept the circle\n" +
            "Save the scene (Ctrl+S).");
    }

    /// <summary>The object holding a Cesium3DTileset, found without referencing the type.</summary>
    static GameObject FindTilesetHost()
    {
        foreach (MonoBehaviour mb in Object.FindObjectsOfType<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name == "Cesium3DTileset") return mb.gameObject;

        return null;
    }
}
