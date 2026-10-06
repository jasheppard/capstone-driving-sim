using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Laying out walkways by hand, which on photogrammetry is the only way to do it.
///
/// The workflow is the same one the signs use, because it is the one that works:
/// point the Scene view at the spot, click the menu item, a point lands there. Alt+P
/// is bound to adding a point, so laying a path is fly, Alt+P, fly, Alt+P rather
/// than a trip to the menu every few metres.
/// </summary>
public static class SidewalkTools
{
    const string ParentName = "Sidewalks";

    /// <summary>
    /// The path points get added to when nothing useful is selected.
    ///
    /// Selection is checked first, so clicking a path in the Hierarchy switches to
    /// extending that one. This is only the fallback for the common case of having
    /// just created a path and immediately wanting to add to it.
    /// </summary>
    static SidewalkPath lastPath;

    [MenuItem("NCAT/Sidewalk/1. Start New Path", false, 160)]
    public static void StartPath()
    {
        GameObject parent = GameObject.Find(ParentName);

        if (parent == null)
        {
            parent = new GameObject(ParentName);
            Undo.RegisterCreatedObjectUndo(parent, "Create Sidewalks");
        }

        GameObject go = new GameObject("Walkway_" + parent.transform.childCount);
        Undo.RegisterCreatedObjectUndo(go, "Start Walkway");

        go.transform.SetParent(parent.transform, false);

        lastPath = go.AddComponent<SidewalkPath>();
        Selection.activeGameObject = go;

        EditorSceneManager.MarkSceneDirty(go.scene);

        Debug.Log("Started " + go.name + ".\n" +
                  "  Point the Scene view along the pavement and press Alt+P for each point\n" +
                  "  Four or five points is usually enough for a straight run\n" +
                  "  Tick Loop on the component for a path that circles back");
    }

    [MenuItem("NCAT/Sidewalk/2. Add Point Here &p", false, 161)]
    public static void AddPoint()
    {
        SidewalkPath path = CurrentPath();

        if (path == null)
        {
            Debug.LogError("[Sidewalk] No path to add to. Run " +
                           "NCAT > Sidewalk > 1. Start New Path first, or select an " +
                           "existing walkway in the Hierarchy.");
            return;
        }

        SceneView view = SceneView.lastActiveSceneView;

        if (view == null)
        {
            Debug.LogError("[Sidewalk] No Scene view open. Click the Scene tab, point " +
                           "it at the pavement, then try again.");
            return;
        }

        // Straight out through the middle of the viewport onto the first thing it
        // hits, which is the only definition that matches what someone is doing
        // when they line the camera up on a spot.
        Vector3 spot;
        Ray ray = view.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 5000f))
        {
            spot = hit.point;
        }
        else
        {
            Debug.LogWarning("[Sidewalk] Nothing under the crosshair. The tiles below " +
                             "have not streamed in yet: let them load and try again.");
            return;
        }

        GameObject point = new GameObject("Point_" + path.Count);
        Undo.RegisterCreatedObjectUndo(point, "Add Sidewalk Point");

        point.transform.SetParent(path.transform, false);
        point.transform.position = spot;

        lastPath = path;
        Selection.activeGameObject = path.gameObject;

        EditorSceneManager.MarkSceneDirty(path.gameObject.scene);

        Debug.Log(path.name + ": point " + (path.Count - 1) + " placed. " +
                  (path.Count < 2 ? "Add at least one more." : "Alt+P for the next."));
    }

    [MenuItem("NCAT/Sidewalk/3. Link Two Selected Points As Crossing", false, 162)]
    public static void LinkCrossing()
    {
        GameObject[] chosen = Selection.gameObjects;

        if (chosen.Length != 2)
        {
            Debug.LogError("[Sidewalk] Select exactly two points, one on each side of " +
                           "the road: click the first in the Hierarchy, Ctrl+click the " +
                           "second. You selected " + chosen.Length + ".");
            return;
        }

        SidewalkPath a = chosen[0].GetComponentInParent<SidewalkPath>();
        SidewalkPath b = chosen[1].GetComponentInParent<SidewalkPath>();

        if (a == null || b == null)
        {
            Debug.LogError("[Sidewalk] Both objects have to be points inside a walkway.");
            return;
        }

        // Linked both ways, so people cross in both directions. One-way crossings
        // look wrong: everyone ends up on the same side within a minute.
        Link(chosen[0], chosen[1].transform);
        Link(chosen[1], chosen[0].transform);

        EditorSceneManager.MarkSceneDirty(chosen[0].scene);

        Debug.Log("Crossing linked: " + chosen[0].name + " and " + chosen[1].name + ".\n" +
                  "  Pedestrians reaching either side may cross to the other\n" +
                  "  Put a Crosswalk waypoint at the same spot (NCAT > Place Crosswalk\n" +
                  "  Here) so the scoring and the pedestrians agree where crossing is\n" +
                  "Save the scene (Ctrl+S).");
    }

    static void Link(GameObject node, Transform other)
    {
        SidewalkCrossing crossing = node.GetComponent<SidewalkCrossing>();
        if (crossing == null) crossing = Undo.AddComponent<SidewalkCrossing>(node);

        crossing.otherSide = other;
        EditorUtility.SetDirty(crossing);
    }

    [MenuItem("NCAT/Sidewalk/4. Check Sidewalks", false, 163)]
    public static void Check()
    {
        SidewalkPath[] paths = Object.FindObjectsOfType<SidewalkPath>();

        if (paths.Length == 0)
        {
            Debug.LogWarning("[Sidewalk] No walkways in the scene. Pedestrians will " +
                             "wander as before, which is what they do today.");
            return;
        }

        int points = 0;
        int crossings = 0;
        int tooShort = 0;
        int dangling = 0;

        foreach (SidewalkPath path in paths)
        {
            points += path.Count;
            if (path.Count < 2) tooShort++;

            for (int i = 0; i < path.Count; i++)
            {
                SidewalkCrossing crossing = path.Node(i).GetComponent<SidewalkCrossing>();
                if (crossing == null) continue;

                if (crossing.otherSide == null) dangling++;
                else crossings++;
            }
        }

        PedestrianSpawner spawner = Object.FindObjectOfType<PedestrianSpawner>();

        Debug.Log(
            "Sidewalks\n" +
            "  " + paths.Length + " walkways, " + points + " points, " +
            crossings + " crossing links\n" +
            (tooShort > 0
                ? "  " + tooShort + " walkway(s) have fewer than 2 points and will be ignored\n"
                : "") +
            (dangling > 0
                ? "  " + dangling + " crossing(s) have no far side set and will be ignored\n"
                : "") +
            (spawner == null
                ? "  NO SPAWNER in the scene, so nobody will walk them\n"
                : spawner.useSidewalks
                    ? "  Spawner will put people on them within " +
                      spawner.sidewalkSnapDistance + " m\n"
                    : "  Spawner has useSidewalks OFF: tick it on the PedestrianSpawner\n"));
    }

    /// <summary>The selected walkway, the walkway of the selected point, or the last one made.</summary>
    static SidewalkPath CurrentPath()
    {
        GameObject chosen = Selection.activeGameObject;

        if (chosen != null)
        {
            SidewalkPath path = chosen.GetComponentInParent<SidewalkPath>();
            if (path != null) return path;
        }

        return lastPath;
    }
}
