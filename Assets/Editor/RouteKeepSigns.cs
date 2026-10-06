using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Rebuilding the driven route without throwing away the signs standing on it.
///
/// THE PROBLEM THESE SOLVE
///
/// Everything placed along the route lives under one Route object: the checkpoints
/// that describe where the road goes, and the stop signs, signals and crossings that
/// are scored while driving it. Clear Route and Import Recorded Route both delete
/// that object whole, which is correct for the checkpoints and wrong for the signs.
/// Signs are placed by hand, one at a time, looking at the campus; checkpoints are
/// re-recorded in a single drive. Losing the cheap thing to replace the expensive
/// one is the wrong way round.
///
/// These two rebuild the checkpoints and leave the signs where they are. The sign
/// objects themselves are moved aside and moved back rather than recreated, so their
/// models, rotations and any tuning survive exactly.
/// </summary>
public static class RouteKeepSigns
{
    const string RouteName = "Route";
    const string StashName = "__SignStash";
    const string RoutesFolder = "Routes";

    [System.Serializable] class WaypointData
    {
        public string kind;
        public float x, y, z;
        public float radius;
        public float speedLimitMph;
    }

    [System.Serializable] class RouteData { public WaypointData[] waypoints; }

    // ------------------------------------------------------------------ clearing

    [MenuItem("NCAT/Route/Clear Checkpoints Only", false, 120)]
    public static void ClearCheckpoints()
    {
        GameObject route = GameObject.Find(RouteName);

        if (route == null)
        {
            Debug.Log("[Route] No Route object, nothing to clear.");
            return;
        }

        int removed = 0;
        int kept = 0;

        // Backwards, because destroying a child shifts the index of everything
        // after it and a forward loop silently skips every other one.
        for (int i = route.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = route.transform.GetChild(i);
            RouteWaypoint waypoint = child.GetComponent<RouteWaypoint>();

            if (waypoint != null && waypoint.kind != RouteWaypoint.Kind.Checkpoint)
            {
                kept++;
                continue;
            }

            Undo.DestroyObjectImmediate(child.gameObject);
            removed++;
        }

        EditorSceneManager.MarkSceneDirty(route.scene);

        Debug.Log("Cleared " + removed + " checkpoint(s). " + kept + " sign(s) and " +
                  "crossing(s) left where they were.\nSave the scene (Ctrl+S).");
    }

    // ------------------------------------------------------------------ importing

    [MenuItem("NCAT/Route/Import Recorded Route (Keep Signs)", false, 104)]
    public static void ImportKeepingSigns()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string folder = Path.Combine(root, RoutesFolder);

        if (!Directory.Exists(folder))
        {
            Debug.LogError("[Route] No " + RoutesFolder + " folder. Record a route first.");
            return;
        }

        string[] files = Directory.GetFiles(folder, "route_*.json").OrderBy(f => f).ToArray();

        if (files.Length == 0)
        {
            Debug.LogError("[Route] No route_*.json files in " + folder + ".");
            return;
        }

        string newest = files[files.Length - 1];
        RouteData data = JsonUtility.FromJson<RouteData>(File.ReadAllText(newest));

        if (data == null || data.waypoints == null || data.waypoints.Length == 0)
        {
            Debug.LogError("[Route] No waypoints readable from " + Path.GetFileName(newest) + ".");
            return;
        }

        GameObject route = GameObject.Find(RouteName);

        // Park the signs outside the Route before anything is removed from it.
        //
        // Moved rather than copied: a sign carries a prefab instance, a rotation
        // chosen by eye, and possibly edited timings. Recreating it from its
        // position alone would quietly lose all of that.
        var stashed = new List<Transform>();

        if (route != null)
        {
            for (int i = route.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = route.transform.GetChild(i);
                RouteWaypoint waypoint = child.GetComponent<RouteWaypoint>();

                if (waypoint == null || waypoint.kind == RouteWaypoint.Kind.Checkpoint) continue;

                Undo.SetTransformParent(child, null, "Stash Sign");
                stashed.Add(child);
            }

            Undo.DestroyObjectImmediate(route);
        }

        route = new GameObject(RouteName);
        Undo.RegisterCreatedObjectUndo(route, "Import Route");

        // The checkpoints from the file, in file order, which is drive order.
        int checkpoints = 0;

        for (int i = 0; i < data.waypoints.Length; i++)
        {
            WaypointData d = data.waypoints[i];

            RouteWaypoint.Kind kind = RouteWaypoint.Kind.Checkpoint;
            try { kind = (RouteWaypoint.Kind)System.Enum.Parse(typeof(RouteWaypoint.Kind), d.kind); }
            catch { }

            // Signs in the file are skipped: the ones standing in the scene are the
            // ones the driver placed and they win. Importing both would leave two
            // stop signs at slightly different spots on the same corner.
            if (kind != RouteWaypoint.Kind.Checkpoint && stashed.Count > 0) continue;

            GameObject go = new GameObject(string.Format("{0:00}_{1}", checkpoints + 1, kind));
            Undo.RegisterCreatedObjectUndo(go, "Import Waypoint");

            go.transform.SetParent(route.transform, false);
            go.transform.position = new Vector3(d.x, d.y, d.z);

            RouteWaypoint waypoint = go.AddComponent<RouteWaypoint>();
            waypoint.kind = kind;
            waypoint.radius = d.radius > 0f ? d.radius : 12f;
            waypoint.speedLimitMph = d.speedLimitMph > 0f ? d.speedLimitMph : 20f;

            checkpoints++;
        }

        // And the signs go back.
        foreach (Transform sign in stashed)
            Undo.SetTransformParent(sign, route.transform, "Restore Sign");

        // Then everything is put into drive order, which is the point.
        //
        // Route order is sibling order, and the restored signs land at the end of
        // the list. Left there, the guidance arrow sends the driver around the whole
        // loop and then back to a stop sign they passed four minutes ago. Sorting by
        // position along the centreline puts each sign between the checkpoints it
        // actually stands between.
        SortAlongRoute(route.transform);

        EditorSceneManager.MarkSceneDirty(route.scene);
        Selection.activeGameObject = route;

        Debug.Log(
            "Imported " + checkpoints + " checkpoint(s) from " + Path.GetFileName(newest) + ".\n" +
            "  " + stashed.Count + " sign(s) kept exactly where they were, with their\n" +
            "  models and rotations, and sorted into drive order along the route\n" +
            (checkpoints < 40
                ? "  NOTE: " + checkpoints + " checkpoints is sparse. Corners will be cut by\n" +
                  "  any corridor built from this. Drive slower for more points.\n"
                : "") +
            "Save the scene (Ctrl+S).");

        GameObject stale = GameObject.Find(StashName);
        if (stale != null) Undo.DestroyObjectImmediate(stale);
    }

    // ------------------------------------------------------------------ ordering

    [MenuItem("NCAT/Route/Sort Waypoints Into Drive Order", false, 105)]
    public static void SortMenu()
    {
        GameObject route = GameObject.Find(RouteName);

        if (route == null || route.transform.childCount < 2)
        {
            Debug.LogError("[Route] Need a Route with at least two waypoints.");
            return;
        }

        int moved = SortAlongRoute(route.transform);

        EditorSceneManager.MarkSceneDirty(route.scene);

        Debug.Log("Sorted the route into drive order: " + moved + " waypoint(s) moved.\n" +
                  "  The guidance arrow now points at them in the order you drive past\n" +
                  "Save the scene (Ctrl+S).");
    }

    /// <summary>
    /// Puts every waypoint into the order a driver meets it.
    ///
    /// The checkpoints define the path, so each waypoint is placed by how far along
    /// that path its nearest point lies. Checkpoints keep their own order, because
    /// their distances increase by construction; signs land between the two
    /// checkpoints they physically stand between.
    ///
    /// Flat distances throughout: a sign at the top of a hill belongs where it is on
    /// the map, not further along for being higher up.
    /// </summary>
    static int SortAlongRoute(Transform route)
    {
        var line = new List<Vector3>();

        for (int i = 0; i < route.childCount; i++)
        {
            RouteWaypoint w = route.GetChild(i).GetComponent<RouteWaypoint>();
            if (w != null && w.kind == RouteWaypoint.Kind.Checkpoint)
                line.Add(route.GetChild(i).position);
        }

        // Without a path there is nothing to measure along, so leave it alone
        // rather than shuffling the list on a meaningless key.
        if (line.Count < 2) return 0;

        var order = new List<KeyValuePair<float, Transform>>();

        // A waypoint far from the centreline has no honest place in the order: the
        // nearest point on the route could be anywhere, and it usually comes out at
        // the start. Worth saying out loud rather than silently sorting it wrong.
        int strays = 0;
        float worstStray = 0f;

        for (int i = 0; i < route.childCount; i++)
        {
            Transform child = route.GetChild(i);

            float offRoute;
            float along = DistanceAlong(line, child.position, out offRoute);

            if (offRoute > 30f)
            {
                strays++;
                worstStray = Mathf.Max(worstStray, offRoute);
            }

            order.Add(new KeyValuePair<float, Transform>(along, child));
        }

        if (strays > 0)
            Debug.LogWarning(
                "[Route] " + strays + " waypoint(s) sit more than 30 m from the driven " +
                "route, the furthest by " + worstStray.ToString("F0") + " m.\n" +
                "  Their place in the order is a guess. Either they belong to an older\n" +
                "  route, or the drive did not pass them. Move them onto the route or\n" +
                "  delete them.");

        order.Sort((a, b) => a.Key.CompareTo(b.Key));

        int moved = 0;

        for (int i = 0; i < order.Count; i++)
        {
            if (order[i].Value.GetSiblingIndex() != i) moved++;
            order[i].Value.SetSiblingIndex(i);
        }

        return moved;
    }

    /// <summary>
    /// How far along the centreline a position sits, in metres, and how far off
    /// the centreline it is.
    /// </summary>
    static float DistanceAlong(List<Vector3> line, Vector3 position, out float offRoute)
    {
        Vector3 flat = new Vector3(position.x, 0f, position.z);

        float travelled = 0f;
        float bestDistance = float.PositiveInfinity;
        float bestTravelled = 0f;

        for (int i = 0; i < line.Count - 1; i++)
        {
            Vector3 a = new Vector3(line[i].x, 0f, line[i].z);
            Vector3 b = new Vector3(line[i + 1].x, 0f, line[i + 1].z);

            Vector3 along = b - a;
            float lengthSquared = along.sqrMagnitude;

            if (lengthSquared > 0.0001f)
            {
                float t = Mathf.Clamp01(Vector3.Dot(flat - a, along) / lengthSquared);
                Vector3 point = a + along * t;

                float distance = Vector3.Distance(flat, point);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTravelled = travelled + Vector3.Distance(a, point);
                }
            }

            travelled += Vector3.Distance(a, b);
        }

        offRoute = bestDistance;
        return bestTravelled;
    }
}
