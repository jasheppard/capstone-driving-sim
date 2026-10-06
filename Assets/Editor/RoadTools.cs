using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Building the road corridor.
///
/// The first menu item is the one to use: the route you recorded by driving is
/// already a list of points down the middle of the road, so a corridor can be built
/// from it without placing anything by hand. The rest are for roads the route does
/// not cover.
/// </summary>
public static class RoadTools
{
    /// <summary>
    /// How much road the driver gets, in metres from the centreline.
    ///
    /// These two are the dial for "the boundary feels tight". Correction does not
    /// begin until the bus is past HalfWidth + Shoulder, so at 8 and 4 there are
    /// twelve metres of slack either side before anything happens, and a warning from
    /// eight. Change them here and run NCAT -> Road -> 1. Build Corridor From Route
    /// again.
    /// </summary>
    const float HalfWidth = 8f;
    const float Shoulder = 4f;

    const string ParentName = "Roads";
    const string CarName = "PlayerCar";

    static RoadCorridor lastCorridor;

    [MenuItem("NCAT/Road/1. Build Corridor From Route", false, 140)]
    public static void FromRoute()
    {
        GameObject route = GameObject.Find("Route");

        if (route == null || route.transform.childCount < 2)
        {
            Debug.LogError("[Road] No Route in the scene with at least two points. " +
                           "Either record or import a route first, or build a " +
                           "corridor by hand with 2. Start New Corridor.");
            return;
        }

        // Only the checkpoints. This filter is the whole point of the method.
        //
        // The Route object holds everything placed along the route, and most of it
        // is not the route: stop signs, signals and crosswalks stand beside the road
        // at scattered spots, and QuickSign appends them in the order they happened
        // to be placed. Copying every child produced a corridor that zigzagged
        // between signs on opposite sides of campus and blocked the road it was
        // supposed to describe.
        //
        // Checkpoint is the kind that means "drive through here", which is exactly
        // and only what a centreline is.
        int skipped = 0;
        var centreline = new System.Collections.Generic.List<Transform>();

        for (int i = 0; i < route.transform.childCount; i++)
        {
            Transform waypoint = route.transform.GetChild(i);
            if (waypoint == null) continue;

            RouteWaypoint marker = waypoint.GetComponent<RouteWaypoint>();

            if (marker == null || marker.kind != RouteWaypoint.Kind.Checkpoint)
            {
                skipped++;
                continue;
            }

            centreline.Add(waypoint);
        }

        if (centreline.Count < 2)
        {
            Debug.LogError(
                "[Road] The route has " + centreline.Count + " checkpoint(s) and " +
                skipped + " other waypoint(s), which is not enough for a centreline.\n" +
                "  Checkpoints are the drive-through points recorded by driving the\n" +
                "  route. Signs and crossings are not, and are ignored here.\n" +
                "  Either import the recorded route, or build the corridor by hand\n" +
                "  with 2. Start New Corridor and Alt+R.");
            return;
        }

        RoadCorridor corridor = Create("Corridor_FromRoute");

        for (int i = 0; i < centreline.Count; i++)
        {
            GameObject point = new GameObject("Point_" + i);
            Undo.RegisterCreatedObjectUndo(point, "Corridor Point");

            point.transform.SetParent(corridor.transform, false);
            point.transform.position = centreline[i].position;
        }

        Selection.activeGameObject = corridor.gameObject;
        EditorSceneManager.MarkSceneDirty(corridor.gameObject.scene);

        Debug.Log(
            "Corridor built from the route: " + centreline.Count + " checkpoints.\n" +
            (skipped > 0
                ? "  " + skipped + " sign/crossing waypoint(s) ignored, which is correct:\n" +
                  "  they stand beside the road, not down the middle of it\n"
                : "") +
            "  The orange lines in the Scene view are the road edges\n" +
            "  Widen or narrow it with Half Width on the RoadCorridor component\n" +
            "  Run 4. Add Boundary To Car, then drive it and adjust\n" +
            "Save the scene (Ctrl+S).");
    }

    [MenuItem("NCAT/Road/0. Delete All Corridors", false, 150)]
    public static void Clear()
    {
        RoadCorridor[] corridors = Object.FindObjectsOfType<RoadCorridor>();

        if (corridors.Length == 0)
        {
            Debug.Log("[Road] No corridors to delete.");
            return;
        }

        foreach (RoadCorridor corridor in corridors)
            Undo.DestroyObjectImmediate(corridor.gameObject);

        GameObject parent = GameObject.Find(ParentName);
        if (parent != null && parent.transform.childCount == 0)
            Undo.DestroyObjectImmediate(parent);

        lastCorridor = null;

        Debug.Log("Deleted " + corridors.Length + " corridor(s). The car is " +
                  "unbounded until you build another. Save the scene (Ctrl+S).");
    }

    [MenuItem("NCAT/Road/2. Start New Corridor", false, 141)]
    public static void StartCorridor()
    {
        RoadCorridor corridor = Create("Corridor");

        Selection.activeGameObject = corridor.gameObject;
        EditorSceneManager.MarkSceneDirty(corridor.gameObject.scene);

        Debug.Log("Started " + corridor.name + ". Point the Scene view down the " +
                  "middle of the road and press Alt+R for each point.");
    }

    [MenuItem("NCAT/Road/3. Add Point Here &r", false, 142)]
    public static void AddPoint()
    {
        RoadCorridor corridor = Current();

        if (corridor == null)
        {
            Debug.LogError("[Road] No corridor to add to. Run 2. Start New Corridor, " +
                           "or select an existing corridor in the Hierarchy.");
            return;
        }

        SceneView view = SceneView.lastActiveSceneView;

        if (view == null)
        {
            Debug.LogError("[Road] No Scene view open.");
            return;
        }

        Ray ray = view.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        if (!Physics.Raycast(ray, out hit, 5000f))
        {
            Debug.LogWarning("[Road] Nothing under the crosshair. Let the tiles " +
                             "stream in and try again.");
            return;
        }

        GameObject point = new GameObject("Point_" + corridor.Count);
        Undo.RegisterCreatedObjectUndo(point, "Corridor Point");

        point.transform.SetParent(corridor.transform, false);
        point.transform.position = hit.point;

        lastCorridor = corridor;
        Selection.activeGameObject = corridor.gameObject;

        EditorSceneManager.MarkSceneDirty(corridor.gameObject.scene);

        Debug.Log(corridor.name + ": point " + (corridor.Count - 1) + " placed.");
    }

    [MenuItem("NCAT/Road/4. Add Boundary To Car", false, 143)]
    public static void AddBoundary()
    {
        GameObject car = GameObject.Find(CarName);

        if (car == null)
        {
            Debug.LogError("[Road] No object called \"" + CarName + "\" in the scene.");
            return;
        }

        RoadBoundary boundary = car.GetComponent<RoadBoundary>();
        if (boundary == null) boundary = Undo.AddComponent<RoadBoundary>(car);

        // Reuse the fault HUD if it is there, so the warning appears where the
        // driver is already being told about faults rather than somewhere new.
        GameObject canvas = GameObject.Find("FaultCanvas");

        if (canvas != null && boundary.warningLabel == null)
        {
            Transform existing = canvas.transform.Find("RoadWarning");

            if (existing == null)
            {
                GameObject label = new GameObject("RoadWarning",
                                                  typeof(RectTransform), typeof(Text));
                Undo.RegisterCreatedObjectUndo(label, "Create Road Warning");

                label.transform.SetParent(canvas.transform, false);

                RectTransform rect = label.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 120f);
                rect.sizeDelta = new Vector2(800f, 60f);

                Text text = label.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 34;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.text = "";

                existing = label.transform;
            }

            boundary.warningLabel = existing.GetComponent<Text>();
        }

        EditorUtility.SetDirty(boundary);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Selection.activeGameObject = car;

        Debug.Log(
            "RoadBoundary added to " + CarName + ".\n" +
            "  Warning appears low centre on the fault canvas\n" +
            "  Turn Enforce off on the component to warn without being pushed,\n" +
            "  which is the way to check a corridor's width while driving it\n" +
            "Save the scene (Ctrl+S).");
    }

    [MenuItem("NCAT/Road/6. Smooth Corridor Through Curves", false, 145)]
    public static void Smooth()
    {
        RoadCorridor corridor = Current();

        if (corridor == null)
        {
            RoadCorridor[] all = Object.FindObjectsOfType<RoadCorridor>();
            if (all.Length == 1) corridor = all[0];
        }

        if (corridor == null || corridor.Count < 3)
        {
            Debug.LogError("[Road] Select a corridor with at least three points. " +
                           "Smoothing needs neighbours on both sides of a bend to " +
                           "know which way it curves.");
            return;
        }

        // Why this exists.
        //
        // The corridor is straight between points, and distance is measured to those
        // straight segments. On a bend that is a chord: the corridor cuts across the
        // inside of the curve, the real road lies outside it, and the boundary stops
        // the driver for being on the road. Widening the corridor hides it by making
        // the whole road loose.
        //
        // Instead the points are treated as a Catmull-Rom spline, which is the curve
        // that passes exactly through every one of them, and resampled along it. The
        // corridor still follows the recorded route precisely; it just stops taking
        // the shortcut between the samples.
        var source = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < corridor.Count; i++) source.Add(corridor.Position(i));

        var smoothed = new System.Collections.Generic.List<Vector3>();
        int last = source.Count - 1;

        for (int i = 0; i < last; i++)
        {
            // The two points either side of the segment shape its curvature. At the
            // ends there is no outer neighbour, so the end point stands in for it,
            // which makes the spline run straight into the end rather than flick.
            Vector3 p0 = source[Mathf.Max(i - 1, 0)];
            Vector3 p1 = source[i];
            Vector3 p2 = source[i + 1];
            Vector3 p3 = source[Mathf.Min(i + 2, last)];

            for (int step = 0; step < subdivisions; step++)
            {
                float t = step / (float)subdivisions;
                smoothed.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        smoothed.Add(source[last]);

        // Replace the children in one undoable go.
        for (int i = corridor.Count - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(corridor.transform.GetChild(i).gameObject);

        for (int i = 0; i < smoothed.Count; i++)
        {
            GameObject point = new GameObject("Point_" + i);
            Undo.RegisterCreatedObjectUndo(point, "Smooth Corridor");

            point.transform.SetParent(corridor.transform, false);
            point.transform.position = smoothed[i];
        }

        Selection.activeGameObject = corridor.gameObject;
        EditorSceneManager.MarkSceneDirty(corridor.gameObject.scene);

        Debug.Log(
            "Smoothed " + corridor.name + ": " + source.Count + " points became " +
            smoothed.Count + ".\n" +
            "  The corridor now curves through the bends instead of cutting across\n" +
            "  Every original point is still on the line: the curve passes through\n" +
            "  them rather than near them\n" +
            "  If a bend is still tight, raise Subdivisions and run it on a freshly\n" +
            "  built corridor\n" +
            "Save the scene (Ctrl+S).");
    }

    [Tooltip("Samples per original segment when smoothing.")]
    static int subdivisions = 6;

    /// <summary>
    /// The Catmull-Rom point at t between p1 and p2.
    ///
    /// Chosen over a Bezier because it interpolates: the curve passes through the
    /// control points rather than being pulled toward them. That matters here,
    /// because the control points are places the vehicle actually drove.
    /// </summary>
    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * ((2f * p1) +
                       (-p0 + p2) * t +
                       (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                       (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    [MenuItem("NCAT/Road/5. Check Road", false, 144)]
    public static void Check()
    {
        RoadCorridor[] corridors = Object.FindObjectsOfType<RoadCorridor>();

        if (corridors.Length == 0)
        {
            Debug.LogWarning("[Road] No corridors in the scene. Nothing bounds the car.");
            return;
        }

        int points = 0;
        int tooShort = 0;
        float shortest = float.PositiveInfinity;
        float longest = 0f;

        foreach (RoadCorridor corridor in corridors)
        {
            points += corridor.Count;
            if (corridor.Count < 2) { tooShort++; continue; }

            for (int i = 0; i < corridor.Count - 1; i++)
            {
                float gap = Vector3.Distance(corridor.Position(i), corridor.Position(i + 1));
                shortest = Mathf.Min(shortest, gap);
                longest = Mathf.Max(longest, gap);
            }
        }

        GameObject car = GameObject.Find(CarName);
        RoadBoundary boundary = car != null ? car.GetComponent<RoadBoundary>() : null;

        Debug.Log(
            "Road\n" +
            "  " + corridors.Length + " corridor(s), " + points + " points\n" +
            (tooShort > 0 ? "  " + tooShort + " with fewer than 2 points, ignored\n" : "") +
            (longest > 0f
                ? "  gaps between points: " + shortest.ToString("F1") + " to " +
                  longest.ToString("F1") + " m\n"
                : "") +
            (longest > 40f
                ? "  WARNING: a gap over 40 m cuts corners. The corridor is straight\n" +
                  "  between points, so a bend needs points through it\n"
                : "") +
            (boundary == null
                ? "  NO RoadBoundary on the car: run 4. Add Boundary To Car\n"
                : boundary.enforce
                    ? "  Boundary is enforcing\n"
                    : "  Boundary is in warn-only mode (Enforce is off)\n"));
    }

    // ------------------------------------------------------------------ helpers

    static RoadCorridor Create(string name)
    {
        GameObject parent = GameObject.Find(ParentName);

        if (parent == null)
        {
            parent = new GameObject(ParentName);
            Undo.RegisterCreatedObjectUndo(parent, "Create Roads");
        }

        GameObject go = new GameObject(name + "_" + parent.transform.childCount);
        Undo.RegisterCreatedObjectUndo(go, "Create Corridor");

        go.transform.SetParent(parent.transform, false);

        lastCorridor = go.AddComponent<RoadCorridor>();

        // Written out rather than left to the component's defaults.
        //
        // A corridor already in a scene keeps whatever was serialised for it, so
        // changing the default in RoadCorridor.cs does nothing to one that exists and
        // nothing to one built before the change. Setting it here means re-running the
        // build is how you apply a new width, which is what anybody would expect.
        lastCorridor.halfWidth = HalfWidth;
        lastCorridor.shoulder = Shoulder;

        return lastCorridor;
    }

    static RoadCorridor Current()
    {
        GameObject chosen = Selection.activeGameObject;

        if (chosen != null)
        {
            RoadCorridor corridor = chosen.GetComponentInParent<RoadCorridor>();
            if (corridor != null) return corridor;
        }

        return lastCorridor;
    }
}
