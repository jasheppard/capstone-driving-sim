using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// Drawing the route on the road, and clearing it away again.
///
/// WHY THERE IS A MATERIAL ASSET HERE
///
/// A LineRenderer with no material draws magenta, or nothing at all. The component
/// can build itself one when the game starts, but a material made at run time is not
/// a project asset, so the scene file stores a reference to nothing and the line is
/// invisible the moment you stop playing. So the material is created once as a real
/// asset on disk and assigned, which makes the line visible in the Scene view, in
/// Play mode, and in a build.
/// </summary>
public static class RoutePathTools
{
    const string PathName = "RoutePath";
    const string MaterialFolder = "Assets/Materials";
    const string MaterialPath = MaterialFolder + "/RoutePath.mat";

    [MenuItem("NCAT/Route/Draw Path On Ground", false, 109)]
    public static void Draw()
    {
        GameObject route = GameObject.Find("Route");

        if (route == null)
        {
            Debug.LogError("[RoutePath] No Route object in the scene.\n" +
                           "  Build one first: NCAT -> Route -> Build Route From A Drive.");
            return;
        }

        int checkpoints = RoutePathVisual.CountCheckpoints(route.transform);

        // The commonest reason the line does not appear, and it is invisible as a
        // symptom: a Route full of stop signs still looks like a route in the
        // Hierarchy, but the path only follows checkpoints.
        if (checkpoints < 2)
        {
            Debug.LogError(
                "[RoutePath] The Route has " + route.transform.childCount + " waypoint(s) " +
                "but only " + checkpoints + " of them are checkpoints.\n" +
                "  The path follows checkpoints. Signs and signals stand beside the\n" +
                "  road, so drawing a line through them would put it on the pavement.\n" +
                "  Build the checkpoints first:\n" +
                "    NCAT -> Route -> Build Route From A Drive (Keep Signs)\n" +
                "  Your signs are kept where they are.");
            return;
        }

        GameObject go = GameObject.Find(PathName);

        if (go == null)
        {
            go = new GameObject(PathName, typeof(LineRenderer), typeof(RoutePathVisual));
            Undo.RegisterCreatedObjectUndo(go, "Draw Route Path");
        }

        RoutePathVisual visual = go.GetComponent<RoutePathVisual>();
        if (visual == null) visual = Undo.AddComponent<RoutePathVisual>(go);

        visual.route = route.transform;
        visual.material = PathMaterial();

        // One-time nudge for a path drawn before the height was raised. Only when it
        // is still sitting on the old default, so a height chosen by hand is never
        // overwritten by re-running this.
        if (visual.height <= 0.13f) visual.height = 0.35f;

        int drawn = visual.Build();

        EditorUtility.SetDirty(visual);
        EditorUtility.SetDirty(go.GetComponent<LineRenderer>());
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;

        Debug.Log(
            "Route path drawn: " + drawn + " points along the ground, from " +
            checkpoints + " checkpoints.\n" +
            "  Colour, Width and Height are on the RoutePathVisual component.\n" +
            "  Raise Height if it still sinks anywhere; 0.35 m is the default\n" +
            "  It re-measures the ground near the bus while you drive, so stretches\n" +
            "  baked against a tile that has since changed detail level correct\n" +
            "  themselves within a second of driving up to them\n" +
            "  If it floats or sinks in the Scene view, fly the Scene camera along\n" +
            "  the route first so the tiles stream in, then draw it again\n" +
            "  Re-run this after changing the route\n" +
            "Save the scene (Ctrl+S).");
    }

    [MenuItem("NCAT/Route/Erase Path From Ground", false, 110)]
    public static void Erase()
    {
        GameObject go = GameObject.Find(PathName);

        if (go == null)
        {
            Debug.Log("[RoutePath] No path in the scene.");
            return;
        }

        Undo.DestroyObjectImmediate(go);
        Debug.Log("Route path erased. Save the scene (Ctrl+S).");
    }

    /// <summary>The saved material, created on first use.</summary>
    static Material PathMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (existing != null)
        {
            // A material saved before Assets/Shaders/RoutePath.shader existed is
            // pointed at the new shader, rather than leaving the old one in place and
            // leaving the line flickering with no way for the driver to know why.
            Shader ours = Shader.Find("NCAT/RoutePath");

            if (ours != null && existing.shader != ours)
            {
                existing.shader = ours;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();

                Debug.Log("[RoutePath] Moved " + MaterialPath + " onto the NCAT/RoutePath " +
                          "shader, which offsets the line towards the camera so it stops " +
                          "fighting with the road surface.");
            }

            return existing;
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        Material material = RoutePathVisual.MakeMaterial();
        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();

        Debug.Log("[RoutePath] Created " + MaterialPath + " using the " +
                  material.shader.name + " shader.");

        return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    }
}
