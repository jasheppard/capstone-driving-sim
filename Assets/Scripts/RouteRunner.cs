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
/// <summary>How the next waypoint is chosen.</summary>
public enum Guidance
{
    /// <summary>
    /// In the order they sit under the Route object, which is the order they were
    /// driven when the route was recorded.
    ///
    /// This is what a road test needs. Every driver meets the same places in the
    /// same sequence, so their runs are comparable, and "approaching intersections"
    /// means something because the approach is the one the route intended.
    /// </summary>
    Ordered,

    /// <summary>
    /// Whichever unvisited waypoint is closest, recomputed each time one is reached.
    ///
    /// Forgiving while building a route: the order under the Route object stops
    /// mattering, so a sign added later or a checkpoint out of sequence still gets
    /// pointed at sensibly. The cost is that the route stops being a route. Two
    /// drivers can take opposite ways around campus and both finish, which is fine
    /// for practice and wrong for a graded run.
    /// </summary>
    NearestUnvisited,
}

public class RouteRunner : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Parent object whose children are the waypoints, in order. Left empty, " +
             "an object named \"Route\" is used.")]
    public Transform route;

    [Header("Vehicle")]
    public Transform vehicle;

    [Header("Guidance")]
    [Tooltip("Ordered follows the waypoints in the order they appear under the Route " +
             "object, which is the order they were driven. NearestUnvisited always " +
             "points at the closest waypoint not yet visited, whatever the order.")]
    public Guidance guidance = Guidance.Ordered;

    [Header("Rules")]
    [Tooltip("At or below this speed the bus counts as stopped, in mph. Not zero: " +
             "a Rigidbody on scanned terrain never sits at exactly zero.")]
    public float stoppedSpeedMph = 1.5f;

    [Tooltip("How long the bus must hold that speed for it to count as a real stop, " +
             "in seconds. This is what separates a stop from a rolling stop.")]
    public float stopHoldSeconds = 0.7f;

    [Tooltip("Speed at or below which the bus counts as yielding at a crossing, mph.")]
    public float yieldSpeedMph = 3f;

    [Tooltip("Seconds the bus must be stopped at a red before a right turn on red is " +
             "allowed. Longer than an ordinary stop, because the rule is not just " +
             "\"stop\" but \"stop, then look, then go if it is clear\".")]
    public float rightOnRedHoldSeconds = 2f;

    [Tooltip("Degrees of turn that count as having turned right, measured from the " +
             "heading the bus arrived on to the heading it left on. Well under 90, " +
             "because the turn is rarely finished by the time the zone is behind you.")]
    public float rightTurnDegrees = 40f;

    [Header("Finishing")]
    [Tooltip("End the run the moment the bus gets back to the stop it set off from.\n\n" +
             "This is what a shuttle lap actually is, and unlike finishing on the last " +
             "waypoint it does not depend on every single one having been ticked off. " +
             "Miss one checkpoint out of two hundred and thirty, and the ordered route " +
             "waits for it forever: everything after it is never offered, the run never " +
             "completes, and from the driver's seat you simply arrive back at the start " +
             "and nothing happens. Coming home is something the driver can always do.")]
    public bool endOnReturnToStart = true;

    [Tooltip("How far the bus must get from the start before the lap can end, in " +
             "metres. Without this the run would finish the instant it began, because " +
             "it begins at the start.")]
    public float leaveDistance = 40f;

    [Tooltip("How close counts as back, in metres.")]
    public float returnDistance = 15f;

    [Tooltip("Seconds of driving before a lap can be completed. A second guard against " +
             "a very short loop ending itself.")]
    public float minimumLapSeconds = 30f;

    [Tooltip("End the run back where it started, rather than at the last waypoint.\n\n" +
             "A campus shuttle route is a loop: the first stop is also the last one, " +
             "and a driver has not finished until they are back at it. Off, the run " +
             "ends wherever the final waypoint happens to be, which on a loop is a few " +
             "metres short of the start and reads as stopping early.")]
    public bool finishAtStart = true;

    [Header("Recovery")]
    [Tooltip("Let the route move on when the bus has clearly driven past a waypoint " +
             "without entering it. Leave this on.\n\n" +
             "Waypoints are satisfied by driving inside their radius. Miss one — by " +
             "going round the wrong side of an island, by being pushed wide, by a " +
             "checkpoint that ended up a few metres into a kerb — and the route waits " +
             "for it forever. The run can then never complete, so the debrief never " +
             "opens and the drive is unscored. On a sixty-three point loop that is a " +
             "near certainty over enough laps, and it is invisible when it happens: " +
             "the bus simply keeps driving with the arrow pointing backwards.")]
    public bool skipWhenPassed = true;

    [Tooltip("How many waypoints ahead to look when deciding the current one has " +
             "been passed.")]
    public int skipLookahead = 6;

    [Tooltip("How much closer a later waypoint must be, in metres, before the current " +
             "one counts as passed. Comfortably more than the spacing between " +
             "waypoints, or a normal approach would trip it.")]
    public float skipMargin = 20f;

    [Tooltip("Seconds that must hold true before giving up on the current waypoint. " +
             "Stops a momentary swing past a corner from skipping anything.")]
    public float skipAfterSeconds = 2.5f;

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

    /// <summary>
    /// Why the route did or did not finish, in one line.
    ///
    /// A run that ends without completing looks identical to one that completes, from
    /// the driver's seat: you get back to the start and nothing happens. This says
    /// which waypoint it was still waiting for, which is the only thing that explains it.
    /// </summary>
    public string Progress()
    {
        int left = 0;
        int firstLeft = -1;

        for (int i = 0; i < visited.Count; i++)
        {
            if (visited[i]) continue;

            left++;
            if (firstLeft < 0) firstLeft = i;
        }

        if (RouteComplete)
            return "Route completed: all " + waypoints.Count + " waypoints.";

        if (left == 0)
            return "Every waypoint visited; was on the way back to the start when the " +
                   "drive ended.";

        RouteWaypoint next = firstLeft >= 0 && firstLeft < waypoints.Count
            ? waypoints[firstLeft] : null;

        return left + " of " + waypoints.Count + " waypoints never reached. The first " +
               "was number " + firstLeft +
               (next != null
                   ? " (" + next.kind + " at " + next.transform.position.ToString("F0") + ")"
                   : "") +
               ". The route waits for each one in turn, so everything after it was " +
               "never offered.";
    }

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
    readonly List<bool> visited = new List<bool>();
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

    /// <summary>Heading on arriving at a waypoint, so the turn out of it can be measured.</summary>
    float entryHeading;

    /// <summary>Set when the recovery gave up on a waypoint, so it cannot be forgiven.</summary>
    bool missedWaypoint;

    /// <summary>Whether the driver has already been told the right turn is available.</summary>
    bool rightOnRedAnnounced;

    // Approach speed at the moment the zone was entered. The road test asks whether
    // you arrived under control, which is a question about the approach, not about
    // how slowly you were going once you were already in the junction.
    float entrySpeedMph;

    float alertRemaining;
    float nextPedestrianRefresh;

    void Start()
    {
        // Recording a route and being scored on one are the same drive seen two ways,
        // and they cannot both happen at once.
        //
        // While recording, the Route object holds whatever has been placed so far:
        // signs, bus stops, nothing. The runner would sequence those, declare the
        // route complete on reaching the last one, and open the debrief — which
        // pauses the game, in the middle of the drive being recorded. So it stands
        // down, the same way SignJudge stands down when the runner is active.
        RouteRecorder recorder = FindObjectOfType<RouteRecorder>();

        if (recorder != null && recorder.recording)
        {
            Debug.Log("[RouteRunner] Route recording is armed, so scoring is off for " +
                      "this drive. Drive the route you want; nothing is being judged " +
                      "and the run cannot end early.\n" +
                      "  Run NCAT -> Route -> Disarm Route Recording to score again.");
            enabled = false;
            return;
        }

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

        for (int i = 0; i < waypoints.Count; i++) visited.Add(false);

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

        home = FindHome();

        body = vehicle.GetComponent<Rigidbody>();
        if (scorer == null) scorer = vehicle.GetComponent<DriveScorer>();
        if (rubric == null) rubric = vehicle.GetComponent<NcRubric>();
        if (rubric == null) rubric = FindObjectOfType<NcRubric>();
        if (debrief == null) debrief = FindObjectOfType<DebriefScreen>();
        if (speedometer == null) speedometer = FindObjectOfType<Speedometer>();

        EnterWaypoint();

        // The first target is chosen the same way as every later one, so Nearest
        // starts at the nearest rather than always at waypoint zero.
        if (guidance == Guidance.NearestUnvisited)
        {
            index = -1;
            index = NextWaypoint();
        }

        Debug.Log("[RouteRunner] Route loaded: " + waypoints.Count + " waypoints, " +
                  "guidance " + guidance + ".");
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
        if (RouteComplete) return;

        // Before the waypoint logic, and not part of it: the lap ends on getting home
        // whether or not the sequence was finished.
        CheckLap(Time.fixedDeltaTime);

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
        else CheckPassed(distance, dt);

        UpdateHud(target, distance);
    }

    // ------------------------------------------------------------------- zones

    bool enteredThisWaypoint;
    float passedFor;

    /// <summary>True once every other waypoint is done and the bus is heading home.</summary>
    bool returningToStart;

    /// <summary>Where the lap starts and finishes: the first bus stop on the route.</summary>
    Transform home;
    bool leftHome;
    float lapSeconds;

    void OnEnterZone(RouteWaypoint target, float speedMph)
    {
        enteredThisWaypoint = true;
        stoppedFor = 0f;
        dwellFor = 0f;
        stopSatisfied = false;
        yieldFailed = false;
        signalViolated = false;
        missedWaypoint = false;
        rightOnRedAnnounced = false;
        entrySpeedMph = speedMph;
        entryHeading = vehicle != null ? vehicle.eulerAngles.y : 0f;

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

                    Alert(target.rightTurnOnRedAllowed
                        ? "Stopped. Go on green, or turn right once clear"
                        : "Stopped at the light: go on green. NO TURN ON RED");
                }
                else if (stopSatisfied && !rightOnRedAnnounced &&
                         target.rightTurnOnRedAllowed &&
                         stoppedFor >= rightOnRedHoldSeconds)
                {
                    // Said once, when the right turn becomes available, so the driver
                    // knows the wait is over rather than guessing at it.
                    rightOnRedAnnounced = true;
                    Alert("RIGHT TURN ON RED NOW ALLOWED");
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

    /// <summary>
    /// Notices that the bus has driven past the waypoint it was heading for.
    ///
    /// The test is comparative rather than geometric: no projection onto a route
    /// direction, which is unreliable at corners, but simply whether a waypoint
    /// further along has been meaningfully closer than the current one for a couple
    /// of seconds. Driving towards a waypoint never looks like that. Driving away
    /// from one always does.
    /// </summary>
    void CheckPassed(float distanceToTarget, float dt)
    {
        if (!skipWhenPassed) { passedFor = 0f; return; }

        float aheadDistance;
        int ahead = NearestAheadOf(index, out aheadDistance);

        if (ahead < 0 || aheadDistance > distanceToTarget - skipMargin)
        {
            passedFor = 0f;
            return;
        }

        passedFor += dt;
        if (passedFor < skipAfterSeconds) return;

        passedFor = 0f;
        MissCurrent();
    }

    /// <summary>The closest waypoint still to be visited, within the look-ahead.</summary>
    int NearestAheadOf(int from, out float best)
    {
        best = float.PositiveInfinity;
        int found = -1;

        Vector2 here = new Vector2(vehicle.position.x, vehicle.position.z);
        int last = Mathf.Min(waypoints.Count - 1, from + Mathf.Max(1, skipLookahead));

        for (int i = from + 1; i <= last; i++)
        {
            if (waypoints[i] == null || visited[i]) continue;

            Vector3 p = waypoints[i].transform.position;
            float d = Vector2.Distance(here, new Vector2(p.x, p.z));

            if (d >= best) continue;

            best = d;
            found = i;
        }

        return found;
    }

    /// <summary>
    /// Gives up on the current waypoint and moves on, scoring it as failed.
    ///
    /// Scored, not silently dropped: a stop sign the bus drove around is a stop sign
    /// it did not stop at, and skipping it quietly would turn this recovery into a
    /// way of dodging the rubric by driving badly enough to miss things.
    /// </summary>
    void MissCurrent()
    {
        RouteWaypoint target = waypoints[index];

        // The judgement flags say what actually happened: nothing here was satisfied,
        // because the bus never came close enough to satisfy it.
        stopSatisfied = false;
        yieldFailed = true;
        enteredOnRed = true;
        signalViolated = true;
        missedWaypoint = true;
        entrySpeedMph = body != null ? body.velocity.magnitude * 2.23694f : 0f;

        if (target != null && target.kind != RouteWaypoint.Kind.Checkpoint)
            Alert("MISSED: " + target.DisplayName);

        Debug.Log("[RouteRunner] Passed waypoint " + index + " (" +
                  (target != null ? target.kind.ToString() : "null") +
                  ") without entering it. Moving on so the route can still finish.");

        Advance(true, true);
    }

    /// <summary>
    /// The start and finish of the lap: the first bus stop, or failing that the first
    /// waypoint of any kind.
    ///
    /// A bus stop rather than a checkpoint because that is what a driver recognises as
    /// the start of a shuttle route. Checkpoints are invisible marks on the road; the
    /// stop is a thing standing there with a sign on it.
    /// </summary>
    Transform FindHome()
    {
        for (int i = 0; i < waypoints.Count; i++)
            if (waypoints[i] != null && waypoints[i].kind == RouteWaypoint.Kind.BusStop)
                return waypoints[i].transform;

        return waypoints.Count > 0 && waypoints[0] != null ? waypoints[0].transform : null;
    }

    /// <summary>
    /// Ends the run when the bus comes back to where it set off from.
    ///
    /// Two guards, because a lap that starts and finishes in the same place would
    /// otherwise finish the moment it started: the bus has to get clear of the start
    /// first, and a minimum time has to have passed.
    /// </summary>
    void CheckLap(float dt)
    {
        if (!endOnReturnToStart || home == null || vehicle == null) return;

        lapSeconds += dt;

        Vector2 here = new Vector2(vehicle.position.x, vehicle.position.z);
        Vector2 start = new Vector2(home.position.x, home.position.z);
        float distance = Vector2.Distance(here, start);

        if (!leftHome)
        {
            if (distance > leaveDistance)
            {
                leftHome = true;
                Debug.Log("[RouteRunner] Left the start. The lap ends on getting back " +
                          "to within " + returnDistance.ToString("F0") + " m of it.");
            }

            return;
        }

        if (lapSeconds < minimumLapSeconds) return;
        if (distance > returnDistance) return;

        Alert("BACK AT THE START");
        Complete();
    }

    void Advance(bool reached)
    {
        Advance(reached, false);
    }

    void Advance(bool reached, bool missed)
    {
        RouteWaypoint target = waypoints[index];

        if (reached && target != null)
        {
            // A waypoint that was driven past was not reached, so it does not count
            // towards the tally, but it is still judged below.
            if (!missed) WaypointsReached++;

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
                    //
                    // Judged on the way OUT, on what the light was doing as the bus
                    // left and which way it went. That is the only moment at which
                    // both facts are known, and it is what separates the three cases
                    // that look identical while sitting at the line: waiting for
                    // green, turning right on red, and simply going anyway.
                    if (enteredOnRed)
                    {
                        bool stillRed = target.CurrentLight == RouteWaypoint.Light.Red;

                        // Positive is clockwise about Y, which is a right turn.
                        float turned = Mathf.DeltaAngle(entryHeading,
                            vehicle != null ? vehicle.eulerAngles.y : entryHeading);

                        bool rightOnRed = stillRed
                                          && !missedWaypoint
                                          && target.rightTurnOnRedAllowed
                                          && stoppedFor >= rightOnRedHoldSeconds
                                          && turned >= rightTurnDegrees;

                        bool ranIt = (stillRed && !rightOnRed) || missedWaypoint;

                        Report(NcElement.TrafficSignals, ranIt, "Ran a red light");

                        if (rightOnRed) Alert("Right on red, after a full stop");
                        else if (ranIt) Alert("RAN A RED LIGHT");
                    }
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

        if (index >= 0 && index < visited.Count) visited[index] = true;

        // Reaching the start again on the return leg is the end of the lap. Checked
        // before asking for the next waypoint, because on the way home there isn't one.
        if (returningToStart && index == 0)
        {
            Complete();
            return;
        }


        index = NextWaypoint();
        enteredThisWaypoint = false;
        inZone = false;
        passedFor = 0f;

        if (index >= waypoints.Count) Complete();
        else EnterWaypoint();
    }

    /// <summary>
    /// Which waypoint to head for now.
    ///
    /// Returns waypoints.Count when there is nothing left, because every caller
    /// already treats that as the end of the route.
    /// </summary>
    int NextWaypoint()
    {
        if (guidance == Guidance.Ordered)
        {
            // Forward only. Scanning from the current position rather than simply
            // adding one skips any waypoint already reached out of sequence, which
            // is possible when two zones overlap.
            for (int i = index + 1; i < waypoints.Count; i++)
                if (waypoints[i] != null && !visited[i]) return i;

            return Home();
        }

        // Nearest unvisited, measured flat: a waypoint up a hill is not further away
        // for being higher, and height differences on campus are big enough to
        // reorder two waypoints that are side by side on the map.
        Vector3 here = vehicle != null ? vehicle.position : transform.position;

        int best = waypoints.Count;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null || visited[i]) continue;

            Vector3 offset = waypoints[i].transform.position - here;
            offset.y = 0f;

            if (offset.sqrMagnitude >= bestDistance) continue;

            bestDistance = offset.sqrMagnitude;
            best = i;
        }

        return best < waypoints.Count ? best : Home();
    }

    /// <summary>
    /// What to do when there is nothing left to visit.
    ///
    /// On a loop that is not the end of the drive: the bus is parked at the far side
    /// of campus from where it set off, and a shuttle driver who stopped there would
    /// not have finished the route. So the first waypoint is re-targeted once, and
    /// reaching it ends the run.
    ///
    /// Once only, which is what returningToStart guards. Without it the first
    /// waypoint would be handed out forever and the drive could never end.
    /// </summary>
    int Home()
    {
        if (!finishAtStart || returningToStart || waypoints.Count < 2)
            return waypoints.Count;

        returningToStart = true;

        Alert("LAST LEG: return to the start");

        Debug.Log("[RouteRunner] Every waypoint visited. Heading back to the start to " +
                  "finish the lap.");

        return 0;
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
                  waypoints.Count + " waypoints, " + PassengersCarried + " passengers.\n" +
                  "  " + Progress());

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

    /// <summary>Anything that counts as a person on foot.</summary>
    public static bool IsPedestrian(MonoBehaviour mb)
    {
        string name = mb.GetType().Name;
        return name == "PedestrianController" || name == "CrossingWalker";
    }

    void RefreshPedestrians()
    {
        pedestrians.Clear();

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            // Two kinds of person count: the wandering ones from the spawner, and the
            // scripted ones standing at crossings. Matched by type name because
            // neither shares a base class with the other, and a driver who runs one
            // down has run a person down either way.
            if (mb == null || !IsPedestrian(mb)) continue;
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
                {
                    if (!stopSatisfied) return "RED";

                    return target.rightTurnOnRedAllowed &&
                           stoppedFor >= rightOnRedHoldSeconds
                        ? "waiting - right turn allowed"
                        : "waiting";
                }
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
