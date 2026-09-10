using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Runs the shuttle route: what to do next, whether the rules were obeyed, and
/// when the run is over.
///
/// Waypoints are taken in order from the children of the Route object. Each is
/// reached by driving inside its radius; what has to happen there depends on what
/// kind of point it is.
///
///   Checkpoint   drive through it
///   BusStop      stop, and stay stopped while passengers board
///   StopSign     come to a genuine halt before leaving the zone
///   Crosswalk    yield to anyone in the crossing
///   Finish       ends the run and opens the debrief
///
/// HOW THE RULES ARE JUDGED
///
/// Both the stop sign and the crosswalk are judged on leaving the zone, not on
/// entering it. That matters: the question is not "did the bus ever go slowly near
/// the sign" but "did it complete a stop before proceeding". Judging on exit is the
/// only way to tell those apart, and it is also what makes a rolling stop
/// detectable — the bus slows, never reaches a halt, and drives on.
///
/// Violations cost points but never end the run, so every attempt produces a
/// complete dataset rather than a truncated one.
/// </summary>
public class RouteRunner : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Parent object whose children are the waypoints, in order. Left empty, " +
             "an object named \"Route\" is used.")]
    public Transform route;

    [Header("Vehicle")]
    public Transform vehicle;

    [Header("Rules")]
    [Tooltip("At or below this speed the bus counts as stopped, in mph. Not zero: " +
             "a Rigidbody on scanned terrain never sits at exactly zero.")]
    public float stoppedSpeedMph = 1.5f;

    [Tooltip("How long the bus must hold that speed for it to count as a real stop, " +
             "in seconds. This is what separates a stop from a rolling stop.")]
    public float stopHoldSeconds = 0.7f;

    [Tooltip("Speed at or below which the bus counts as yielding at a crossing, mph.")]
    public float yieldSpeedMph = 3f;

    [Header("HUD")]
    public Text objectiveLabel;
    public Text distanceLabel;
    public Text progressLabel;
    public Text alertLabel;

    [Tooltip("Seconds an alert stays on screen.")]
    public float alertSeconds = 2.5f;

    [Header("References")]
    public DriveScorer scorer;
    public NcRubric rubric;
    public DebriefScreen debrief;
    public Speedometer speedometer;

    // ------------------------------------------------------------------ results

    public bool RouteComplete { get; private set; }
    public int WaypointsReached { get; private set; }
    public int WaypointCount { get { return waypoints.Count; } }
    public int PassengersCarried { get; private set; }

    /// <summary>Which waypoint is currently being driven to. Read by RouteMarkers.</summary>
    public int CurrentIndex { get { return index; } }

    public RouteWaypoint CurrentWaypoint
    {
        get { return index >= 0 && index < waypoints.Count ? waypoints[index] : null; }
    }

    public RouteWaypoint WaypointAt(int i)
    {
        return i >= 0 && i < waypoints.Count ? waypoints[i] : null;
    }

    // ---------------------------------------------------------------- internals

    readonly List<RouteWaypoint> waypoints = new List<RouteWaypoint>();
    readonly List<Transform> pedestrians = new List<Transform>();

    int index;
    Rigidbody body;

    bool inZone;
    float stoppedFor;
    float dwellFor;
    bool stopSatisfied;
    bool yieldFailed;

    // Traffic signals are judged on what the light was doing when the bus arrived,
    // not on what it is doing now. Entering on green and still being in the junction
    // when it turns red is not running a red light, and scoring it as one would
    // punish a driver for the cycle rather than for their driving.
    bool enteredOnRed;
    bool signalViolated;

    // Approach speed at the moment the zone was entered. The road test asks whether
    // you arrived under control, which is a question about the approach, not about
    // how slowly you were going once you were already in the junction.
    float entrySpeedMph;

    float alertRemaining;
    float nextPedestrianRefresh;

    void Start()
    {
        if (route == null)
        {
            GameObject found = GameObject.Find("Route");
            if (found != null) route = found.transform;
        }

        if (route == null)
        {
            Debug.LogError("[RouteRunner] No Route object in the scene. Record one, " +
                           "then run NCAT → Import Recorded Route.");
            enabled = false;
            return;
        }

        for (int i = 0; i < route.childCount; i++)
        {
            RouteWaypoint waypoint = route.GetChild(i).GetComponent<RouteWaypoint>();
            if (waypoint != null) waypoints.Add(waypoint);
        }

        if (waypoints.Count == 0)
        {
            Debug.LogError("[RouteRunner] The Route object has no waypoints under it.");
            enabled = false;
            return;
        }

        if (vehicle == null)
        {
            GameObject car = GameObject.Find("PlayerCar");
            if (car != null) vehicle = car.transform;
        }

        if (vehicle == null)
        {
            Debug.LogError("[RouteRunner] No PlayerCar to follow.");
            enabled = false;
            return;
        }

        body = vehicle.GetComponent<Rigidbody>();
        if (scorer == null) scorer = vehicle.GetComponent<DriveScorer>();
        if (rubric == null) rubric = vehicle.GetComponent<NcRubric>();
        if (rubric == null) rubric = FindObjectOfType<NcRubric>();
        if (debrief == null) debrief = FindObjectOfType<DebriefScreen>();
        if (speedometer == null) speedometer = FindObjectOfType<Speedometer>();

        EnterWaypoint();

        Debug.Log("[RouteRunner] Route loaded: " + waypoints.Count + " waypoints.");
    }

    void Update()
    {
        if (alertRemaining > 0f)
        {
            alertRemaining -= Time.deltaTime;
            if (alertRemaining <= 0f && alertLabel != null) alertLabel.text = "";
        }
    }

    void FixedUpdate()
    {
        if (RouteComplete || index >= waypoints.Count) return;

        RouteWaypoint target = waypoints[index];
        if (target == null) { Advance(false); return; }

        float dt = Time.fixedDeltaTime;

        Vector3 here = vehicle.position;
        Vector3 there = target.transform.position;

        // Horizontal distance only. A waypoint on a slope should not be harder to
        // reach than one on the flat.
        Vector2 flatHere = new Vector2(here.x, here.z);
        Vector2 flatThere = new Vector2(there.x, there.z);
        float distance = Vector2.Distance(flatHere, flatThere);

        float speedMph = body != null ? body.velocity.magnitude * 2.23694f : 0f;
        bool stopped = speedMph <= stoppedSpeedMph;

        bool nowInZone = distance <= target.radius;

        if (nowInZone && !inZone) OnEnterZone(target, speedMph);
        inZone = nowInZone;

        if (nowInZone) JudgeInZone(target, speedMph, stopped, dt);
        else if (WasInZoneAndLeft(target, distance)) Advance(true);

        UpdateHud(target, distance);
    }

    // ------------------------------------------------------------------- zones

    bool enteredThisWaypoint;

    void OnEnterZone(RouteWaypoint target, float speedMph)
    {
        enteredThisWaypoint = true;
        stoppedFor = 0f;
        dwellFor = 0f;
        stopSatisfied = false;
        yieldFailed = false;
        signalViolated = false;
        entrySpeedMph = speedMph;

        switch (target.kind)
        {
            case RouteWaypoint.Kind.StopSign:
                Alert("STOP SIGN: come to a full stop");
                break;
            case RouteWaypoint.Kind.Crosswalk:
                Alert("CROSSWALK: yield to pedestrians");
                break;
            case RouteWaypoint.Kind.BusStop:
                Alert("BUS STOP: stop for boarding");
                break;

            case RouteWaypoint.Kind.TrafficSignal:
                enteredOnRed = target.CurrentLight == RouteWaypoint.Light.Red;
                Alert(enteredOnRed ? "RED LIGHT: stop and wait"
                                   : "SIGNAL: " + target.CurrentLight.ToString().ToLowerInvariant());
                break;

            case RouteWaypoint.Kind.Intersection:
                Alert("INTERSECTION: slow and check");
                break;
        }
    }

    void JudgeInZone(RouteWaypoint target, float speedMph, bool stopped, float dt)
    {
        switch (target.kind)
        {
            case RouteWaypoint.Kind.Checkpoint:
                Advance(true);          // nothing to satisfy; passing through is enough
                break;

            case RouteWaypoint.Kind.Finish:
                Complete();
                break;

            case RouteWaypoint.Kind.StopSign:
                if (stopped) stoppedFor += dt;
                if (stoppedFor >= stopHoldSeconds && !stopSatisfied)
                {
                    stopSatisfied = true;
                    Alert("Stop complete: proceed when clear");
                }
                break;

            case RouteWaypoint.Kind.BusStop:
                if (stopped)
                {
                    dwellFor += dt;
                    if (dwellFor >= target.dwellSeconds && !stopSatisfied)
                    {
                        stopSatisfied = true;
                        PassengersCarried += target.passengersWaiting;

                        Alert(target.passengersWaiting > 0
                                  ? target.passengersWaiting + " passengers boarded"
                                  : "Boarding complete");
                    }
                }
                else if (!stopSatisfied)
                {
                    // Moving off mid-dwell restarts it. Passengers do not board a
                    // bus that is pulling away.
                    dwellFor = 0f;
                }
                break;

            case RouteWaypoint.Kind.Crosswalk:
                if (PedestrianInCrossing(target) && speedMph > yieldSpeedMph && !yieldFailed)
                {
                    yieldFailed = true;
                    Alert("FAILED TO YIELD");
                }
                break;

            case RouteWaypoint.Kind.TrafficSignal:
                // Arrived on red. Holding still clears it; carrying on through does
                // not. Waiting for green and then going is the correct behaviour and
                // records no violation.
                if (!enteredOnRed) break;

                if (stopped) stoppedFor += dt;

                if (stoppedFor >= stopHoldSeconds && !stopSatisfied)
                {
                    stopSatisfied = true;
                    Alert("Stopped at the light: go on green");
                }
                else if (!stopSatisfied && speedMph > yieldSpeedMph && !signalViolated)
                {
                    signalViolated = true;
                    Alert("RAN A RED LIGHT");
                }
                break;
        }
    }

    /// <summary>
    /// True once the bus has been inside the zone and has now driven back out of
    /// it. Judging happens here rather than on entry, so a rolling stop can be told
    /// apart from a real one.
    /// </summary>
    bool WasInZoneAndLeft(RouteWaypoint target, float distance)
    {
        if (!enteredThisWaypoint) return false;

        // A little hysteresis, so sitting exactly on the boundary does not flicker
        // between arriving and leaving.
        return distance > target.radius * 1.15f;
    }

    void Advance(bool reached)
    {
        RouteWaypoint target = waypoints[index];

        if (reached && target != null)
        {
            WaypointsReached++;

            // Every judgement is made here, on the way out of the zone, and reported
            // to the rubric as one opportunity at one element of the road test.
            switch (target.kind)
            {
                case RouteWaypoint.Kind.StopSign:
                    Report(NcElement.StopSigns, !stopSatisfied, "Rolled the stop sign");
                    if (!stopSatisfied) Alert("Rolled through the stop sign");
                    break;

                case RouteWaypoint.Kind.Crosswalk:
                    Report(NcElement.YieldToPedestrians, yieldFailed,
                           "Failed to yield at a crossing");
                    break;

                case RouteWaypoint.Kind.TrafficSignal:
                    // Only an arrival on red is a test of the driver. Arriving on
                    // green asks nothing of them, so it is not counted as an
                    // opportunity either way.
                    if (enteredOnRed)
                        Report(NcElement.TrafficSignals, signalViolated, "Ran a red light");
                    break;

                case RouteWaypoint.Kind.Intersection:
                    bool tooFast = entrySpeedMph > target.speedLimitMph;
                    Report(NcElement.IntersectionApproach, tooFast,
                           "Entered the intersection at " + entrySpeedMph.ToString("F0") + " mph");
                    if (tooFast) Alert("TOO FAST INTO THE INTERSECTION");
                    break;

                case RouteWaypoint.Kind.BusStop:
                    // Pulling in and away at a stop is the road test's "stopping,
                    // starting and parking", so the bus stop scores it.
                    Report(NcElement.Stopping, !stopSatisfied, "Missed the bus stop");
                    if (!stopSatisfied) Alert("Missed the stop: passengers left behind");
                    break;
            }
        }

        index++;
        enteredThisWaypoint = false;
        inZone = false;

        if (index >= waypoints.Count) Complete();
        else EnterWaypoint();
    }

    /// <summary>
    /// One opportunity at one element of the road test, passed or failed.
    ///
    /// Wrapped rather than called directly so a scene with no rubric on the car
    /// still drives normally instead of throwing on every waypoint.
    /// </summary>
    void Report(NcElement element, bool violated, string detail)
    {
        if (rubric != null) rubric.Register(element, violated, detail);
    }

    /// <summary>Applies the new segment's speed limit as the next waypoint becomes current.</summary>
    void EnterWaypoint()
    {
        if (index >= waypoints.Count) return;

        RouteWaypoint target = waypoints[index];
        if (target == null) return;

        if (scorer != null) scorer.speedLimitMph = target.speedLimitMph;
        if (speedometer != null) speedometer.speedLimitMph = target.speedLimitMph;
    }

    void Complete()
    {
        if (RouteComplete) return;

        RouteComplete = true;
        Alert("ROUTE COMPLETE");

        Debug.Log("[RouteRunner] Route complete: " + WaypointsReached + "/" +
                  waypoints.Count + " waypoints, " + PassengersCarried + " passengers.");

        if (debrief != null) debrief.EndDrive();
    }

    // ------------------------------------------------------------- pedestrians

    bool PedestrianInCrossing(RouteWaypoint target)
    {
        if (Time.time >= nextPedestrianRefresh)
        {
            RefreshPedestrians();
            nextPedestrianRefresh = Time.time + 1f;
        }

        Vector3 crossing = target.transform.position;

        for (int i = 0; i < pedestrians.Count; i++)
        {
            if (pedestrians[i] == null) continue;
            if (Vector3.Distance(crossing, pedestrians[i].position) <= target.crossingRadius)
                return true;
        }

        return false;
    }

    void RefreshPedestrians()
    {
        pedestrians.Clear();

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "PedestrianController") continue;
            pedestrians.Add(mb.transform);
        }
    }

    // --------------------------------------------------------------------- HUD

    void UpdateHud(RouteWaypoint target, float distance)
    {
        if (objectiveLabel != null)
            objectiveLabel.text = target.DisplayName.ToUpperInvariant();

        if (distanceLabel != null)
            distanceLabel.text = distance < target.radius
                ? InZoneText(target)
                : Mathf.RoundToInt(distance) + " m";

        if (progressLabel != null)
            progressLabel.text = "STOP " + Mathf.Min(index + 1, waypoints.Count) +
                                 " OF " + waypoints.Count;
    }

    string InZoneText(RouteWaypoint target)
    {
        switch (target.kind)
        {
            case RouteWaypoint.Kind.StopSign:
                return stopSatisfied ? "clear to go" : "STOP";

            case RouteWaypoint.Kind.BusStop:
                if (stopSatisfied) return "boarded";
                return dwellFor > 0f
                    ? "boarding " + Mathf.Max(0f, target.dwellSeconds - dwellFor).ToString("F1") + "s"
                    : "STOP TO BOARD";

            case RouteWaypoint.Kind.Crosswalk:
                return PedestrianInCrossing(target) ? "YIELD" : "clear";

            case RouteWaypoint.Kind.TrafficSignal:
                if (target.CurrentLight == RouteWaypoint.Light.Red)
                    return stopSatisfied ? "waiting" : "RED";
                return target.CurrentLight == RouteWaypoint.Light.Amber ? "AMBER" : "green";

            case RouteWaypoint.Kind.Intersection:
                return "check both ways";

            default:
                return "arrived";
        }
    }

    void Alert(string message)
    {
        if (alertLabel == null) return;

        alertLabel.text = message;
        alertRemaining = alertSeconds;
    }
}
