using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Takes the wobble out of a recorded route without rounding off its corners.
///
/// THE PROBLEM
///
/// A recorded route is a record of someone driving, and nobody drives a clean line.
/// A late correction, a twitch of the stick, a swerve round a pedestrian — each one
/// leaves a kink in the checkpoints, and everything built on them inherits it: the
/// painted path jerks, the guidance arrow snaps sideways, and the corridor pinches.
///
/// Smoothing the DRAWING cannot fix this. The path already runs a Catmull-Rom spline
/// through the checkpoints, and Catmull-Rom is interpolating: it passes exactly
/// through every control point, so it reproduces the wobble faithfully. The control
/// points themselves have to move.
///
/// WHY NOT JUST AVERAGE EVERYTHING
///
/// The obvious fix is to pull every checkpoint toward the average of its neighbours.
/// Do that and the ninety degree junctions go with it: at a right angle the average
/// of the two neighbours is about ten metres off the corner, so a few passes cut it
/// into a wide sweep across the far lane. The route stops describing the road.
///
/// A wobble and a corner have to be told apart, and they can be, because they look
/// different. A corner turns one way and keeps turning. A wobble turns one way and
/// immediately turns back — it is a zigzag, and a zigzag is a reversal of sign in the
/// turn angle. So:
///
///   a median filter first, which removes an isolated point that sits off the line
///   while leaving a genuine step alone — that is exactly what medians are for, and
///
///   then averaging applied ONLY where the turn reverses, which is where a driver
///   corrected rather than where a road bends.
///
/// Signs, signals and bus stops are never moved. They were placed by hand against the
/// campus and they are not the thing that is wrong.
/// </summary>
public static class RouteSmoother
{
    /// <summary>Turn angle, in degrees, above which a reversal counts as a wobble.</summary>
    const float WobbleDegrees = 18f;

    /// <summary>How hard to pull a wobbling point toward its neighbours, per pass.</summary>
    const float Strength = 0.45f;

    const int Passes = 4;

    /// <summary>Furthest any checkpoint may be moved in total, in metres.</summary>
    const float MaximumShift = 4f;

    [MenuItem("NCAT/Route/Smooth Out Wobbles", false, 112)]
    public static void Smooth()
    {
        GameObject routeGO = GameObject.Find("Route");

        if (routeGO == null)
        {
            Debug.LogError("[RouteSmoother] No Route object in the scene.");
            return;
        }

        // Checkpoints only, in route order, and remembered with their transforms so
        // the smoothed positions can be written back to the right objects.
        var moving = new List<Transform>();

        for (int i = 0; i < routeGO.transform.childCount; i++)
        {
            Transform child = routeGO.transform.GetChild(i);
            RouteWaypoint waypoint = child.GetComponent<RouteWaypoint>();

            if (waypoint != null && waypoint.kind == RouteWaypoint.Kind.Checkpoint)
                moving.Add(child);
        }

        if (moving.Count < 5)
        {
            Debug.LogError("[RouteSmoother] Only " + moving.Count + " checkpoints. " +
                           "Nothing worth smoothing.");
            return;
        }

        var original = new List<Vector3>();
        foreach (Transform t in moving) original.Add(t.position);

        float worstBefore = WorstTurn(original);

        var points = new List<Vector3>(original);

        Median(points);

        for (int pass = 0; pass < Passes; pass++) Relax(points);

        // Nothing may wander far from where it was driven, whatever the maths wants.
        // A cap is the difference between tidying a line and inventing one.
        int clamped = 0;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 shift = points[i] - original[i];
            shift.y = 0f;

            if (shift.magnitude <= MaximumShift) continue;

            points[i] = original[i] + shift.normalized * MaximumShift;
            clamped++;
        }

        // ---- write back ----------------------------------------------------------
        float moved = 0f;
        float worstMove = 0f;
        int touched = 0;

        for (int i = 0; i < moving.Count; i++)
        {
            Vector3 to = new Vector3(points[i].x, original[i].y, points[i].z);
            float d = Vector3.Distance(original[i], to);

            if (d < 0.05f) continue;

            Undo.RecordObject(moving[i], "Smooth Route");
            moving[i].position = to;

            moved += d;
            worstMove = Mathf.Max(worstMove, d);
            touched++;
        }

        float worstAfter = WorstTurn(points);

        EditorSceneManager.MarkSceneDirty(routeGO.scene);

        Debug.Log(
            "Route smoothed.\n" +
            "  " + touched + " of " + moving.Count + " checkpoints moved, by " +
            (touched > 0 ? (moved / touched).ToString("F2") : "0") + " m on average, " +
            worstMove.ToString("F2") + " m at most\n" +
            "  sharpest turn " + worstBefore.ToString("F0") + " deg -> " +
            worstAfter.ToString("F0") + " deg\n" +
            (clamped > 0
                ? "  " + clamped + " were held back by the " + MaximumShift +
                  " m limit, which is the guard against inventing a line nobody drove\n"
                : "") +
            "  Signs, signals and bus stops were not touched\n" +
            "  Ctrl+Z puts it all back\n\n" +
            "Then rebuild what is made from the route:\n" +
            "  NCAT -> Road -> 1. Build Corridor From Route\n" +
            "  NCAT -> Route -> Draw Path On Ground\n" +
            "Save the scene (Ctrl+S).");
    }

    // ------------------------------------------------------------------ filtering

    /// <summary>
    /// A five point median on each axis.
    ///
    /// Removes a checkpoint that sits off the line on its own, which is what a single
    /// twitch leaves behind, while leaving a genuine step alone: the median of a run
    /// that steps once is still on one side or the other, never in between. That edge
    /// preserving behaviour is the whole reason to use a median rather than a mean.
    /// </summary>
    static void Median(List<Vector3> points)
    {
        var source = new List<Vector3>(points);
        var window = new List<float>();

        for (int i = 2; i < points.Count - 2; i++)
        {
            points[i] = new Vector3(
                MedianOf(window, source, i, true),
                source[i].y,
                MedianOf(window, source, i, false));
        }
    }

    static float MedianOf(List<float> window, List<Vector3> source, int centre, bool xAxis)
    {
        window.Clear();

        for (int k = centre - 2; k <= centre + 2; k++)
            window.Add(xAxis ? source[k].x : source[k].z);

        window.Sort();
        return window[2];
    }

    /// <summary>
    /// One pass of averaging, applied only where the route reverses direction.
    ///
    /// The weight is how much of a zigzag this point sits in: full where the turn
    /// reverses hard, nothing where the route is turning steadily or running straight.
    /// A ninety degree junction has the same sign on both sides of it and is left
    /// exactly where it is.
    /// </summary>
    static void Relax(List<Vector3> points)
    {
        var source = new List<Vector3>(points);

        for (int i = 1; i < points.Count - 1; i++)
        {
            float before = Turn(source, i - 1);
            float here = Turn(source, i);
            float after = Turn(source, i + 1);

            bool reverses =
                (Opposed(here, before) && Mathf.Min(Mathf.Abs(here), Mathf.Abs(before)) > WobbleDegrees) ||
                (Opposed(here, after) && Mathf.Min(Mathf.Abs(here), Mathf.Abs(after)) > WobbleDegrees);

            if (!reverses) continue;

            Vector3 average = (source[i - 1] + source[i + 1]) * 0.5f;

            points[i] = Vector3.Lerp(source[i], average, Strength);
        }
    }

    static bool Opposed(float a, float b)
    {
        return a * b < 0f;
    }

    /// <summary>
    /// The signed turn at a point, in degrees. Positive is one way, negative the
    /// other; which is which does not matter, only that they differ.
    /// </summary>
    static float Turn(List<Vector3> points, int i)
    {
        if (i <= 0 || i >= points.Count - 1) return 0f;

        Vector2 into = Flat(points[i] - points[i - 1]);
        Vector2 outOf = Flat(points[i + 1] - points[i]);

        if (into.sqrMagnitude < 0.01f || outOf.sqrMagnitude < 0.01f) return 0f;

        return Vector2.SignedAngle(into, outOf);
    }

    static Vector2 Flat(Vector3 v)
    {
        return new Vector2(v.x, v.z);
    }

    static float WorstTurn(List<Vector3> points)
    {
        float worst = 0f;

        for (int i = 1; i < points.Count - 1; i++)
            worst = Mathf.Max(worst, Mathf.Abs(Turn(points, i)));

        return worst;
    }
}
