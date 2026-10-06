using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Sets how fast the bus can go, and how fast it is allowed to go.
///
/// WHY THIS IS A TOOL AND NOT A FIELD
///
/// The speed limit is not stored in one place. It lives on DriveScorer, on the
/// Speedometer, on the route recorder's defaults, and on EVERY waypoint — and the
/// waypoint copy wins, because RouteRunner writes it into the scorer each time the bus
/// passes one. That is deliberate: the limit belongs to the stretch of road leading to
/// a waypoint, so a route can change speed along its length.
///
/// The cost is that changing "the speed limit" by hand means finding every copy. Miss
/// the waypoints and the scorer is silently reset to the old number a few seconds into
/// the drive, which looks exactly like the setting not working.
///
/// WHY TWO NUMBERS
///
/// The governed top speed is what the vehicle can physically do. The limit is what the
/// driver is scored against. They have to differ, or Safe Speed is untestable: a bus
/// that cannot exceed the limit can never be marked down for exceeding it, and the
/// element would report a perfect score that means nothing.
/// </summary>
public static class SpeedLimitSetup
{
    /// <summary>What the bus can reach, in mph. Headroom above the limit, on purpose.</summary>
    const float GovernedTopSpeedMph = 50f;

    /// <summary>What the driver is scored against, in mph.</summary>
    const float LimitMph = 45f;

    /// <summary>
    /// Grace above the limit before it counts as a fault, in mph.
    ///
    /// Zero means 45 is a hard line. Raise it to 2 or 3 if a momentary overshoot while
    /// accelerating is costing the element unfairly — the briefing reads this same
    /// field, so the screen can never promise a tolerance the scoring does not give.
    /// </summary>
    const float ToleranceMph = 0f;

    [MenuItem("NCAT/Set Speed Limit", false, 61)]
    public static void Apply()
    {
        int changed = 0;

        VehicleController vehicle = Object.FindObjectOfType<VehicleController>();

        if (vehicle != null)
        {
            Undo.RecordObject(vehicle, "Set Speed Limit");
            vehicle.governedTopSpeedMph = GovernedTopSpeedMph;
            EditorUtility.SetDirty(vehicle);
            changed++;
        }

        DriveScorer scorer = Object.FindObjectOfType<DriveScorer>();

        if (scorer != null)
        {
            Undo.RecordObject(scorer, "Set Speed Limit");
            scorer.speedLimitMph = LimitMph;
            scorer.speedToleranceMph = ToleranceMph;
            EditorUtility.SetDirty(scorer);
            changed++;
        }

        Speedometer speedometer = Object.FindObjectOfType<Speedometer>();

        if (speedometer != null)
        {
            Undo.RecordObject(speedometer, "Set Speed Limit");
            speedometer.speedLimitMph = LimitMph;
            EditorUtility.SetDirty(speedometer);
            changed++;
        }

        RouteRecorder recorder = Object.FindObjectOfType<RouteRecorder>();

        if (recorder != null)
        {
            Undo.RecordObject(recorder, "Set Speed Limit");
            recorder.defaultSpeedLimitMph = LimitMph;
            EditorUtility.SetDirty(recorder);
            changed++;
        }

        // The ones that actually decide it at run time.
        RouteWaypoint[] waypoints = Object.FindObjectsOfType<RouteWaypoint>(true);

        foreach (RouteWaypoint waypoint in waypoints)
        {
            Undo.RecordObject(waypoint, "Set Speed Limit");
            waypoint.speedLimitMph = LimitMph;
            EditorUtility.SetDirty(waypoint);
        }

        if (vehicle != null) EditorSceneManager.MarkSceneDirty(vehicle.gameObject.scene);

        Debug.Log(
            "Speed limit set.\n" +
            "  bus can reach   " + GovernedTopSpeedMph.ToString("F0") + " mph\n" +
            "  limit           " + LimitMph.ToString("F0") + " mph" +
            (ToleranceMph > 0f
                ? ", faulted above " + (LimitMph + ToleranceMph).ToString("F0")
                : ", faulted above " + LimitMph.ToString("F0") + " (no tolerance)") + "\n" +
            "  updated         " + changed + " component(s) and " + waypoints.Length +
            " waypoint(s)\n\n" +
            "  Every waypoint carries its own limit and overwrites the scorer as the\n" +
            "  bus passes it, so routes recorded BEFORE this still carry the old\n" +
            "  number: run this again after importing one\n" +
            "  Re-run NCAT -> Set Up Driver Briefing so the briefing screen quotes\n" +
            "  the new figure rather than the old one\n" +
            "  Tune Vehicle: Arcade / Campus Shuttle both write the governed top\n" +
            "  speed themselves, so running one of those after this will override it\n" +
            "Save the scene (Ctrl+S).");
    }
}
