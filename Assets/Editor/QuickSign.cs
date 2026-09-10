using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Puts one sign down, right now, with no route and no driving.
///
/// The recorder exists because a route has to be proved drivable, and that is worth
/// the ceremony when you are laying out a route. It is not worth it when you just
/// want a stop sign on a corner. This is the short way: point the Scene view at a
/// junction, click the menu, and a sign appears there.
///
/// What it creates is a real waypoint, not scenery, so the sign is scored exactly
/// like one dropped while driving. The difference is only in how it got there.
///
/// The one thing it cannot know is which way traffic approaches, so the sign is
/// turned to face the Scene camera. Look at the junction from the direction a driver
/// would arrive and it comes out right. If it does not, rotate it in the Inspector.
/// </summary>
public static class QuickSign
{
    const string RouteName = "Route";

    [MenuItem("NCAT/Place Stop Sign Here", false, 20)]
    public static void StopSign()
    {
        Place(RouteWaypoint.Kind.StopSign, "StopSign");
    }

    [MenuItem("NCAT/Place Traffic Light Here", false, 21)]
    public static void TrafficLight()
    {
        Place(RouteWaypoint.Kind.TrafficSignal, "TrafficLight");
    }

    /// <summary>
    /// An unsignalled junction. Nothing to obey, but arriving above the limit fails
    /// "approaching intersections", which is the element that asks whether you came
    /// in under control.
    /// </summary>
    [MenuItem("NCAT/Place Intersection Here", false, 22)]
    public static void Intersection()
    {
        Place(RouteWaypoint.Kind.Intersection, null);
    }

    /// <summary>
    /// A crossing. Only tests the driver when somebody is actually in it, so put
    /// these where the pedestrian spawner puts people.
    /// </summary>
    [MenuItem("NCAT/Place Crosswalk Here", false, 23)]
    public static void Crosswalk()
    {
        Place(RouteWaypoint.Kind.Crosswalk, null);
    }

    static void Place(RouteWaypoint.Kind kind, string prefabName)
    {
        SceneView view = SceneView.lastActiveSceneView;

        if (view == null)
        {
            Debug.LogError("[QuickSign] No Scene view open. Click in the Scene tab, " +
                           "point it at the spot you want, then run this again.");
            return;
        }

        // "Here" means whatever is in the middle of the Scene view.
        //
        // A ray straight out through the centre of the viewport, onto the first thing
        // it hits. That is the only definition that matches what someone is actually
        // doing when they line a camera up on a corner, and it needs no explanation:
        // put the crosshair on the spot, click the menu, the sign is on the spot.
        //
        // The camera pivot was the obvious alternative and it is worse, because the
        // pivot only moves when you orbit or frame something. Fly across campus with
        // the right mouse button and the pivot trails behind you, so the sign lands
        // somewhere you left a minute ago.
        Vector3 spot;
        bool grounded = false;

        RaycastHit centre;
        Ray ray = view.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out centre, 5000f))
        {
            spot = centre.point;
            grounded = true;
        }
        else
        {
            // Nothing under the crosshair, usually unstreamed tiles. Fall back to the
            // pivot and drop from there.
            spot = DropToGround(view.pivot, out grounded);
        }

        // ---- the waypoint ------------------------------------------------------
        GameObject routeGO = GameObject.Find(RouteName);

        if (routeGO == null)
        {
            routeGO = new GameObject(RouteName);
            Undo.RegisterCreatedObjectUndo(routeGO, "Create Route");
        }

        GameObject waypointGO = new GameObject(kind.ToString() + "_" + routeGO.transform.childCount);
        Undo.RegisterCreatedObjectUndo(waypointGO, "Place Sign");

        waypointGO.transform.SetParent(routeGO.transform, false);
        waypointGO.transform.position = spot;

        RouteWaypoint waypoint = waypointGO.AddComponent<RouteWaypoint>();
        waypoint.kind = kind;

        // ---- the model ---------------------------------------------------------
        // Intersections and crosswalks have no prefab: they are places on the road,
        // not objects standing beside it, so there is nothing to put there.
        GameObject prefab = string.IsNullOrEmpty(prefabName) ? null : FindPrefab(prefabName);
        string modelNote;

        if (string.IsNullOrEmpty(prefabName))
        {
            modelNote = "  No model: this kind is a zone on the road, not an object\n";
        }
        else if (prefab != null)
        {
            GameObject sign = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(sign, "Place Sign Model");

            sign.transform.SetParent(waypointGO.transform, false);
            sign.transform.localPosition = Vector3.zero;

            // Facing the Scene camera, flattened, so it stands upright rather than
            // leaning back at whatever angle you were looking down from.
            Vector3 toCamera = view.camera.transform.position - spot;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude > 0.01f)
                sign.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);

            if (kind == RouteWaypoint.Kind.TrafficSignal)
            {
                TrafficLightVisual visual = sign.GetComponent<TrafficLightVisual>();
                if (visual == null) visual = sign.AddComponent<TrafficLightVisual>();
                visual.waypoint = waypoint;
            }

            modelNote = "  Model attached, facing the Scene camera\n";
        }
        else
        {
            modelNote =
                "  NO MODEL. The waypoint is there and will be scored, but nothing is\n" +
                "  visible. Make a prefab called \"" + prefabName + "\" and run\n" +
                "  Route > 4. Place Signs to fill it in\n";
        }

        Selection.activeGameObject = waypointGO;
        EditorSceneManager.MarkSceneDirty(routeGO.scene);

        // Parenthesised, because a conditional inside a concatenation binds to the
        // false branch alone and would silently drop the rest of the message.
        Debug.Log(
            waypoint.DisplayName + " placed.\n" +
            modelNote +
            (grounded ? "" : "  Nothing under the crosshair, so this landed at the " +
                             "camera pivot. The tiles below have not streamed in: " +
                             "let them load and run it again.\n") +
            "  It is waypoint " + (routeGO.transform.childCount - 1) + " under Route\n" +
            "Save the scene (Ctrl+S).");
    }

    static Vector3 DropToGround(Vector3 point, out bool grounded)
    {
        RaycastHit hit;
        Vector3 start = point + Vector3.up * 200f;

        grounded = Physics.Raycast(start, Vector3.down, out hit, 600f);
        return grounded ? hit.point : point;
    }

    static GameObject FindPrefab(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab " + name))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        return null;
    }
}
