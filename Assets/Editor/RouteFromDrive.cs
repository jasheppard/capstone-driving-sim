using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Builds a route out of a drive that was already logged.
///
/// WHY THIS EXISTS
///
/// RouteRecorder drops a waypoint when you press B. That is the right tool for
/// placing a stop sign on a particular corner, and the wrong one for describing where
/// a road goes: a kilometre of campus needs a waypoint every fifteen metres, which is
/// seventy presses while steering. In practice that means two or three get dropped,
/// the route is a straight line across three buildings, and everything built on top
/// of it is wrong: the corridor, the guidance arrow, the painted path.
///
/// Meanwhile the telemetry logger has been recording the position of the bus ten
/// times a second for every drive since the start of the project. That is the same
/// information, already measured, at a thousand times the density. This reads one of
/// those logs and lays the route along it.
///
/// WHAT IT HAS TO CLEAN UP
///
/// A log is a record of a session, not of a lap. Respawning, or restarting at the
/// depot, puts a hundred-metre jump in the middle of it. A route threaded through
/// that jump drives through whatever is in between. So the log is cut at every jump
/// and the longest continuous stretch is the one used: that is the drive, and the
/// rest is the fumbling either side of it.
///
/// Heights are not in the log, only X and Z, so each waypoint is dropped onto the
/// terrain by raycast, walking the height along the route the same way the painted
/// path does. Where the Cesium tiles have not streamed in there is nothing to land
/// on, and those points keep the height of the one before them.
/// </summary>
public static class RouteFromDrive
{
    const float Spacing = 15f;      // metres between checkpoints
    const float MaxJump = 25f;      // a step longer than this is a teleport, not driving
    const float Tolerance = 8f;     // how far the ground may rise between checkpoints
    const float Radius = 12f;
    const float SpeedLimitMph = 20f;

    [MenuItem("NCAT/Route/Build Route From A Drive (Keep Signs)", false, 102)]
    public static void Build()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string telemetry = Path.Combine(root, "TelemetryData");

        string file = EditorUtility.OpenFilePanel(
            "Pick the drive to build the route from", 
            Directory.Exists(telemetry) ? telemetry : root, "csv");

        if (string.IsNullOrEmpty(file)) return;

        var raw = ReadPositions(file);

        if (raw.Count < 20)
        {
            Debug.LogError("[RouteFromDrive] " + Path.GetFileName(file) + " has only " +
                           raw.Count + " position(s) in it.\n" +
                           "  Either it is not a telemetry log, or the drive was too\n" +
                           "  short. Pick a log from a drive that covered the route.");
            return;
        }

        var run = LongestContinuousRun(raw);
        var spaced = Resample(run, Spacing);

        if (spaced.Count < 2)
        {
            Debug.LogError("[RouteFromDrive] The longest unbroken stretch of that drive " +
                           "is under " + Spacing + " m. Pick a longer drive.");
            return;
        }

        int snapped;
        var placed = DropOntoGround(spaced, out snapped);

        string stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string folder = Path.Combine(root, "Routes");
        Directory.CreateDirectory(folder);
        string output = Path.Combine(folder, "route_" + stamp + ".json");

        File.WriteAllText(output, ToJson(placed));

        Debug.Log(
            "Read " + Path.GetFileName(file) + ":\n" +
            "  " + raw.Count + " logged positions, longest unbroken stretch " +
            run.Count + " of them (" + Length(run).ToString("F0") + " m)\n" +
            "  " + placed.Count + " checkpoints at " + Spacing + " m spacing\n" +
            "  " + snapped + " of them landed on loaded terrain; the other " +
            (placed.Count - snapped) + " kept the height of the one before\n" +
            "  written to Routes/" + Path.GetFileName(output));

        // Reuse the importer rather than repeating it: it is the piece that parks the
        // signs, rebuilds the Route, puts the signs back with their models and
        // rotations intact, and sorts everything into drive order. It reads the
        // newest route_*.json, which is the one just written.
        RouteKeepSigns.ImportKeepingSigns();

        if (placed.Count - snapped > placed.Count / 4)
            Debug.LogWarning(
                "[RouteFromDrive] More than a quarter of the checkpoints had no terrain " +
                "under them.\n" +
                "  The Cesium tiles stream in around wherever the Scene view is\n" +
                "  looking. Fly the Scene camera along the route once so the campus\n" +
                "  loads, then run this again and the heights will be right.");
    }

    // ------------------------------------------------------------------- reading

    /// <summary>The X and Z columns of a telemetry log, in the order they were logged.</summary>
    static List<Vector2> ReadPositions(string file)
    {
        var points = new List<Vector2>();
        string[] lines = File.ReadAllLines(file);

        if (lines.Length < 2) return points;

        // The header is written with a byte order mark, which otherwise ends up
        // glued to the first column name.
        string[] header = lines[0].TrimStart('﻿').Split(',');

        int xColumn = -1;
        int zColumn = -1;

        for (int i = 0; i < header.Length; i++)
        {
            string name = header[i].Trim();
            if (name == "pos_x") xColumn = i;
            if (name == "pos_z") zColumn = i;
        }

        if (xColumn < 0 || zColumn < 0)
        {
            Debug.LogError("[RouteFromDrive] " + Path.GetFileName(file) + " has no " +
                           "pos_x / pos_z columns, so there is no path in it.\n" +
                           "  Logs from before those columns were added cannot be used.");
            return points;
        }

        int widest = Mathf.Max(xColumn, zColumn);

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cells = lines[i].Split(',');
            if (cells.Length <= widest) continue;

            float x, z;

            // InvariantCulture: the logger writes dots, and a machine set to comma
            // decimals would otherwise read every coordinate as zero.
            if (!float.TryParse(cells[xColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) continue;
            if (!float.TryParse(cells[zColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) continue;

            points.Add(new Vector2(x, z));
        }

        return points;
    }

    // ------------------------------------------------------------------- cleaning

    /// <summary>
    /// The longest stretch of the log with no teleports in it.
    ///
    /// Longest by distance driven rather than by number of samples, because sitting
    /// still at the depot logs thousands of samples and covers no ground.
    /// </summary>
    static List<Vector2> LongestContinuousRun(List<Vector2> points)
    {
        var best = new List<Vector2>();
        float bestLength = -1f;

        int start = 0;

        for (int i = 1; i <= points.Count; i++)
        {
            bool broken = i == points.Count ||
                          Vector2.Distance(points[i - 1], points[i]) > MaxJump;

            if (!broken) continue;

            var run = points.GetRange(start, i - start);
            float length = Length(run);

            if (length > bestLength)
            {
                bestLength = length;
                best = run;
            }

            start = i;
        }

        return best;
    }

    static float Length(List<Vector2> points)
    {
        float total = 0f;

        for (int i = 0; i < points.Count - 1; i++)
            total += Vector2.Distance(points[i], points[i + 1]);

        return total;
    }

    /// <summary>One point every `spacing` metres along the driven line.</summary>
    static List<Vector2> Resample(List<Vector2> points, float spacing)
    {
        var output = new List<Vector2>();
        if (points.Count == 0) return output;

        output.Add(points[0]);

        float since = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            since += Vector2.Distance(points[i - 1], points[i]);

            if (since < spacing) continue;

            output.Add(points[i]);
            since = 0f;
        }

        // The end of the drive matters: it is where the route finishes, and dropping
        // it would cut up to a full spacing off the route.
        if (Vector2.Distance(output[output.Count - 1], points[points.Count - 1]) > spacing * 0.3f)
            output.Add(points[points.Count - 1]);

        return output;
    }

    // ------------------------------------------------------------------ grounding

    /// <summary>
    /// Gives every point a height, walking it along the route.
    ///
    /// Shares RoutePathVisual.GroundY so the checkpoints and the line painted through
    /// them agree about where the road is, including the rule that rejects tree canopy.
    /// </summary>
    static List<Vector3> DropOntoGround(List<Vector2> points, out int snapped)
    {
        var output = new List<Vector3>(points.Count);

        float guide = SeedHeight(points[0]);
        snapped = 0;

        for (int i = 0; i < points.Count; i++)
        {
            bool found;
            float y = RoutePathVisual.GroundY(points[i].x, points[i].y, guide, Tolerance, out found);

            if (found)
            {
                guide = y;
                snapped++;
            }

            output.Add(new Vector3(points[i].x, y, points[i].y));
        }

        return output;
    }

    /// <summary>
    /// A starting height for the walk.
    ///
    /// The waypoints already standing in the scene are the best evidence available:
    /// they were placed on the road by hand or by a previous drive. Failing that, the
    /// lowest surface under the start is used, because the lowest thing a ray hits
    /// from high above is the ground and the highest is a tree.
    /// </summary>
    static float SeedHeight(Vector2 start)
    {
        RouteWaypoint[] existing = Object.FindObjectsOfType<RouteWaypoint>();

        float nearest = float.PositiveInfinity;
        float height = 0f;
        bool found = false;

        for (int i = 0; i < existing.Length; i++)
        {
            Vector3 position = existing[i].transform.position;
            float distance = Vector2.Distance(start, new Vector2(position.x, position.z));

            if (distance >= nearest) continue;

            nearest = distance;
            height = position.y;
            found = true;
        }

        if (found) return height;

        RaycastHit[] hits = Physics.RaycastAll(new Vector3(start.x, 500f, start.y),
                                              Vector3.down, 1200f, ~0,
                                              QueryTriggerInteraction.Ignore);

        float lowest = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
            lowest = Mathf.Min(lowest, hits[i].point.y);

        return float.IsPositiveInfinity(lowest) ? 0f : lowest;
    }

    // ---------------------------------------------------------------------- output

    /// <summary>
    /// The same shape of file RouteRecorder writes, so the existing importer reads it
    /// without knowing where it came from.
    /// </summary>
    static string ToJson(List<Vector3> points)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"waypoints\": [");

        for (int i = 0; i < points.Count; i++)
        {
            sb.Append("    { \"kind\": \"Checkpoint\"");
            sb.Append(", \"x\": " + F(points[i].x));
            sb.Append(", \"y\": " + F(points[i].y));
            sb.Append(", \"z\": " + F(points[i].z));
            sb.Append(", \"radius\": " + F(Radius));
            sb.Append(", \"speedLimitMph\": " + F(SpeedLimitMph));
            sb.Append(" }");
            sb.AppendLine(i < points.Count - 1 ? "," : "");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string F(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }
}
