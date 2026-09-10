using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Scores every sign in the world, in whatever order you happen to meet them.
///
/// WHY THIS EXISTS ALONGSIDE RouteRunner
///
/// RouteRunner judges a route: it knows which waypoint is next and watches only that
/// one, which is right when the driver is being sent somewhere in a fixed order. It
/// is useless for a campus with signs dotted around it, because a sign that is not
/// the next waypoint is invisible to it. Drive past four stop signs in a different
/// order than they were placed and three of them score nothing.
///
/// This watches all of them at once and judges each on its own. There is no order and
/// no route: a sign is scored the moment you leave it, whichever one it was.
///
/// The two do not run together. If a RouteRunner is active, it owns the judging and
/// this steps aside, because both reporting the same stop sign would count one
/// mistake twice.
///
/// HOW A STOP IS JUDGED
///
/// On leaving the zone, not on entering it. The question is not whether the bus ever
/// went slowly near the sign, it is whether it completed a stop before proceeding,
/// and those two only look different once the bus is past the line. Judging on exit
/// is what makes a rolling stop detectable.
/// </summary>
public class SignJudge : MonoBehaviour
{
    [Header("What counts as stopped")]
    [Tooltip("At or below this speed the bus counts as stopped, in mph. Not zero: a " +
             "Rigidbody on scanned terrain never sits at exactly zero.")]
    public float stoppedSpeedMph = 1.5f;

    [Tooltip("Seconds the bus must hold that speed for it to count as a real stop. " +
             "This is what separates a stop from a rolling stop.")]
    public float stopHoldSeconds = 0.7f;

    [Tooltip("Speed at or below which the bus counts as yielding, in mph.")]
    public float yieldSpeedMph = 3f;

    [Header("Feedback")]
    [Tooltip("How far out the driver is warned that a sign is coming, in metres. " +
             "Zero switches the warning off.")]
    public float warnDistance = 35f;

    // ---------------------------------------------------------------- internals

    /// <summary>What is being tracked about one sign the bus is currently inside.</summary>
    class Visit
    {
        public RouteWaypoint Waypoint;
        public float StoppedFor;
        public bool Satisfied;
        public bool Failed;
        public float EntrySpeedMph;
    }

    readonly List<RouteWaypoint> signs = new List<RouteWaypoint>();
    readonly Dictionary<RouteWaypoint, Visit> inside = new Dictionary<RouteWaypoint, Visit>();

    NcRubric rubric;
    Rigidbody body;
    RouteRunner runner;

    float nextRescan;

    void Start()
    {
        body = GetComponent<Rigidbody>();
        rubric = GetComponent<NcRubric>();
        if (rubric == null) rubric = FindObjectOfType<NcRubric>();

        runner = FindObjectOfType<RouteRunner>();

        if (rubric == null)
        {
            Debug.LogError("[SignJudge] No NcRubric anywhere, so nothing can be scored. " +
                           "Run NCAT > Set Up Road Test.");
            enabled = false;
            return;
        }

        if (runner != null && runner.enabled && runner.WaypointCount > 0)
        {
            Debug.Log("[SignJudge] A route is loaded, so RouteRunner is doing the " +
                      "judging and this is standing down. Both would count the same " +
                      "stop sign twice.");
            enabled = false;
            return;
        }

        Rescan();

        Debug.Log("[SignJudge] Watching " + signs.Count + " signs. No route needed: " +
                  "each one is scored on its own when you leave it.");
    }

    /// <summary>
    /// Collects every sign in the scene.
    ///
    /// Repeated occasionally rather than done once, because signs get added and moved
    /// in the editor while play mode is running, and a list built at Start would go
    /// stale the first time that happens.
    /// </summary>
    void Rescan()
    {
        signs.Clear();

        foreach (RouteWaypoint waypoint in FindObjectsOfType<RouteWaypoint>())
        {
            if (Judged(waypoint.kind)) signs.Add(waypoint);
        }
    }

    static bool Judged(RouteWaypoint.Kind kind)
    {
        return kind == RouteWaypoint.Kind.StopSign
            || kind == RouteWaypoint.Kind.TrafficSignal
            || kind == RouteWaypoint.Kind.Crosswalk
            || kind == RouteWaypoint.Kind.Intersection;
    }

    void FixedUpdate()
    {
        if (rubric == null || rubric.Finished) return;

        if (Time.time >= nextRescan)
        {
            Rescan();
            nextRescan = Time.time + 5f;
        }

        float dt = Time.fixedDeltaTime;
        float speedMph = body != null ? body.velocity.magnitude * 2.23694f : 0f;
        bool stopped = speedMph <= stoppedSpeedMph;

        Vector3 here = transform.position;

        for (int i = 0; i < signs.Count; i++)
        {
            RouteWaypoint sign = signs[i];
            if (sign == null) continue;

            Vector3 there = sign.transform.position;

            // Horizontal only. A sign on a slope should not be harder to reach than
            // one on the flat.
            float distance = Vector2.Distance(new Vector2(here.x, here.z),
                                              new Vector2(there.x, there.z));

            bool within = distance <= sign.radius;
            bool tracked = inside.ContainsKey(sign);

            if (within && !tracked) Enter(sign, speedMph);
            else if (within) Judge(inside[sign], speedMph, stopped, dt);

            // Hysteresis, so sitting on the boundary does not flicker between
            // arriving and leaving and report the same sign over and over.
            else if (tracked && distance > sign.radius * 1.15f) Leave(sign);
        }
    }

    void Enter(RouteWaypoint sign, float speedMph)
    {
        inside[sign] = new Visit
        {
            Waypoint = sign,
            EntrySpeedMph = speedMph,
        };

        // A signal is only a test of the driver if it was red when they arrived.
        if (sign.kind == RouteWaypoint.Kind.TrafficSignal)
            inside[sign].Failed = sign.CurrentLight != RouteWaypoint.Light.Red;
    }

    void Judge(Visit visit, float speedMph, bool stopped, float dt)
    {
        switch (visit.Waypoint.kind)
        {
            case RouteWaypoint.Kind.StopSign:
                if (stopped) visit.StoppedFor += dt;
                if (visit.StoppedFor >= stopHoldSeconds) visit.Satisfied = true;
                break;

            case RouteWaypoint.Kind.TrafficSignal:
                // Failed is being used here to mean "arrived on green", in which case
                // there is nothing to satisfy and nothing to report.
                if (visit.Failed) break;

                if (stopped) visit.StoppedFor += dt;
                if (visit.StoppedFor >= stopHoldSeconds) visit.Satisfied = true;
                break;

            case RouteWaypoint.Kind.Crosswalk:
                if (speedMph <= yieldSpeedMph) visit.Satisfied = true;
                break;
        }
    }

    void Leave(RouteWaypoint sign)
    {
        Visit visit = inside[sign];
        inside.Remove(sign);

        switch (sign.kind)
        {
            case RouteWaypoint.Kind.StopSign:
                rubric.Register(NcElement.StopSigns, !visit.Satisfied,
                                "Rolled the stop sign");
                break;

            case RouteWaypoint.Kind.TrafficSignal:
                // Arrived on green, so the driver was never asked anything. Not
                // counted as an opportunity either way, or a route of green lights
                // would inflate the element with free passes.
                if (visit.Failed) break;

                rubric.Register(NcElement.TrafficSignals, !visit.Satisfied,
                                "Ran a red light");
                break;

            case RouteWaypoint.Kind.Crosswalk:
                rubric.Register(NcElement.YieldToPedestrians, !visit.Satisfied,
                                "Failed to yield at a crossing");
                break;

            case RouteWaypoint.Kind.Intersection:
                bool tooFast = visit.EntrySpeedMph > sign.speedLimitMph;
                rubric.Register(NcElement.IntersectionApproach, tooFast,
                                "Entered the intersection at " +
                                visit.EntrySpeedMph.ToString("F0") + " mph");
                break;
        }
    }
}
