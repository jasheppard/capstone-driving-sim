using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Puts the physical stop signs and traffic lights on the campus, one at every
/// waypoint that calls for one.
///
/// WHY THE WAYPOINTS DECIDE, NOT YOU
///
/// It is tempting to drag signs around the campus by hand until they look right.
/// Do not. The waypoint is what the scoring reads: a sign placed anywhere else is
/// scenery that costs nothing to ignore, and a waypoint with no sign is a rule the
/// driver is punished for breaking without ever being shown. Generating the props
/// from the waypoints makes those two things the same thing by construction, so
/// they cannot drift apart later.
///
/// If a sign is in the wrong place, move the waypoint and run this again. It rebuilds
/// from scratch every time, so running it twice never leaves two signs on one corner.
///
/// WHERE EACH ONE GOES
///
/// A sign faces the driver approaching it, so its rotation comes from the direction
/// of travel into the waypoint, taken from the previous waypoint on the route. It is
/// then pushed to the right-hand kerb, because that is the side American signage
/// lives on, and dropped onto whatever the raycast finds below.
///
/// The raycast is the part that fails quietly. Cesium terrain only has colliders on
/// tiles that have actually streamed in, so a sign placed over unloaded ground ends
/// up hanging at the waypoint's own height. Fly the Scene view along the route first,
/// let the tiles load, then run this.
/// </summary>
public static class SignPlacer
{
    const string ContainerName = "Signs";
    const string StopSignPrefab = "StopSign";
    const string TrafficLightPrefab = "TrafficLight";

    /// <summary>How far to the right of the waypoint a sign stands, in metres.</summary>
    const float KerbOffset = 4.5f;
    const float RaycastUp = 30f;
    const float RaycastDown = 80f;

    [MenuItem("NCAT/Route/4. Place Signs", false, 24)]
    public static void Place()
    {
        GameObject routeGO = GameObject.Find("Route");

        if (routeGO == null)
        {
            Debug.LogError("[SignPlacer] No Route object in the scene. Record a route " +
                           "first, then run Route > 2. Import Recorded Route.");
            return;
        }

        GameObject stopPrefab = FindPrefab(StopSignPrefab);
        GameObject lightPrefab = FindPrefab(TrafficLightPrefab);

        if (stopPrefab == null && lightPrefab == null)
        {
            Debug.LogError(
                "[SignPlacer] No sign prefabs found.\n" +
                "Make them once, then run this again:\n" +
                "  1. Drag Assets/Models/Signs/StopSign/StopSign.fbx into the scene\n" +
                "  2. Drag it from the Hierarchy into Assets/Prefabs/Signs, name it " +
                "\"StopSign\", then delete it from the scene\n" +
                "  3. Drag Assets/Models/Signs/TrafficLight/StreetProps.fbx into the " +
                "scene. It contains ten props: find the traffic light among them\n" +
                "  4. Drag JUST that child out to the root, make it a prefab called " +
                "\"TrafficLight\", then delete the rest");
            return;
        }

        // Rebuilt from scratch, so running this twice does not stack two signs on
        // one corner.
        Transform old = routeGO.transform.Find(ContainerName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        GameObject container = new GameObject(ContainerName);
        Undo.RegisterCreatedObjectUndo(container, "Place Signs");
        container.transform.SetParent(routeGO.transform, false);

        List<RouteWaypoint> waypoints = new List<RouteWaypoint>();
        for (int i = 0; i < routeGO.transform.childCount; i++)
        {
            RouteWaypoint waypoint =
                routeGO.transform.GetChild(i).GetComponent<RouteWaypoint>();
            if (waypoint != null) waypoints.Add(waypoint);
        }

        int stops = 0, lights = 0, floating = 0;

        for (int i = 0; i < waypoints.Count; i++)
        {
            RouteWaypoint waypoint = waypoints[i];

            bool isStop = waypoint.kind == RouteWaypoint.Kind.StopSign;
            bool isLight = waypoint.kind == RouteWaypoint.Kind.TrafficSignal;
            if (!isStop && !isLight) continue;

            GameObject prefab = isStop ? stopPrefab : lightPrefab;
            if (prefab == null) continue;

            // The direction the driver arrives from. The first waypoint has no
            // previous one, so it borrows the direction to the next instead.
            Vector3 approach = Approach(waypoints, i);

            Vector3 right = Vector3.Cross(Vector3.up, approach).normalized;
            Vector3 spot = waypoint.transform.position + right * KerbOffset;

            bool grounded;
            spot = DropToGround(spot, out grounded);
            if (!grounded) floating++;

            GameObject sign = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(sign, "Place Sign");

            sign.transform.SetParent(container.transform, false);
            sign.transform.position = spot;

            // Facing back down the road at whoever is coming.
            sign.transform.rotation = Quaternion.LookRotation(-approach, Vector3.up);
            sign.name = (isStop ? "StopSign_" : "TrafficLight_") + i;

            if (isLight)
            {
                TrafficLightVisual visual = sign.GetComponent<TrafficLightVisual>();
                if (visual == null) visual = sign.AddComponent<TrafficLightVisual>();

                visual.waypoint = waypoint;
                lights++;
            }
            else
            {
                stops++;
            }
        }

        EditorSceneManager.MarkSceneDirty(routeGO.scene);
        Selection.activeGameObject = container;

        string warning = floating > 0
            ? "\n  " + floating + " could not find ground and are floating. Fly the " +
              "Scene view along the route so the tiles load, then run this again."
            : "";

        Debug.Log(
            "Signs placed.\n" +
            "  " + stops + " stop signs, " + lights + " traffic lights\n" +
            "  Each faces the driver and sits " + KerbOffset + " m to the right" +
            warning + "\n" +
            "Save the scene (Ctrl+S).");
    }

    /// <summary>The direction of travel into this waypoint, normalised.</summary>
    static Vector3 Approach(List<RouteWaypoint> waypoints, int index)
    {
        Vector3 here = waypoints[index].transform.position;

        Vector3 from = index > 0
            ? waypoints[index - 1].transform.position
            : (index + 1 < waypoints.Count
                   ? here - (waypoints[index + 1].transform.position - here)
                   : here + Vector3.back);

        Vector3 direction = here - from;
        direction.y = 0f;

        return direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.forward;
    }

    /// <summary>
    /// Drops a point onto the ground below it.
    ///
    /// Started from well above the waypoint rather than at it, because a waypoint
    /// snapped to the road sits fractionally inside the collider and a ray cast from
    /// there starts underground and hits nothing.
    /// </summary>
    static Vector3 DropToGround(Vector3 point, out bool grounded)
    {
        RaycastHit hit;
        Vector3 start = point + Vector3.up * RaycastUp;

        grounded = Physics.Raycast(start, Vector3.down, out hit,
                                   RaycastUp + RaycastDown);

        return grounded ? hit.point : point;
    }

    static GameObject FindPrefab(string name)
    {
        // Searched by name across the whole project, so it does not matter which
        // folder the prefabs were saved into.
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab " + name))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        return null;
    }
}
