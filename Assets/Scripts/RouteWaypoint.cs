using UnityEngine;

/// <summary>
/// One point on the shuttle route.
///
/// Order is the sibling order under the Route parent, so reordering the route is
/// dragging in the Hierarchy rather than editing an index on every waypoint. That
/// also means inserting a point in the middle cannot leave the numbering wrong.
///
/// The speed limit belongs to the segment LEADING TO this waypoint, not the one
/// leaving it. That way the limit changes as you arrive at the sign that sets it,
/// which is how road signage actually works.
/// </summary>
public class RouteWaypoint : MonoBehaviour
{
    public enum Kind
    {
        /// <summary>Just drive through it. Keeps the route on the road.</summary>
        Checkpoint,

        /// <summary>Come to a stop and wait while passengers board.</summary>
        BusStop,

        /// <summary>Full stop required. Rolling through is a violation.</summary>
        StopSign,

        /// <summary>Yield to anyone in the crossing.</summary>
        Crosswalk,

        /// <summary>
        /// A signalled junction. The light cycles on its own clock, and entering on
        /// red is a violation. Stopping on amber is not required: the rule is the
        /// real one, which is to stop if you safely can.
        /// </summary>
        TrafficSignal,

        /// <summary>
        /// An unsignalled junction. Nothing has to be obeyed here beyond arriving
        /// slowly enough to be able to stop, which is what the road test means by
        /// approaching an intersection under control.
        /// </summary>
        Intersection,

        /// <summary>End of the route. Reaching it ends the run.</summary>
        Finish,
    }

    [Header("What this point is")]
    public Kind kind = Kind.Checkpoint;

    [Tooltip("Shown on the objective HUD. Left blank, the kind is used.")]
    public string label = "";

    [Header("Geometry")]
    [Tooltip("How close the bus must get for this to count as reached, in metres. " +
             "Generous is kinder: photogrammetry roads are not perfectly flat and " +
             "a tight radius is easy to miss.")]
    public float radius = 12f;

    [Header("Rules")]
    [Tooltip("Speed limit in mph for the stretch of road LEADING TO this waypoint.")]
    public float speedLimitMph = 45f;

    [Tooltip("Bus stops only: seconds the bus must remain stopped while passengers board.")]
    public float dwellSeconds = 3f;

    [Tooltip("Bus stops only: how many passengers are waiting here.")]
    public int passengersWaiting = 0;

    [Tooltip("Crosswalks only: how far from this point a pedestrian counts as being " +
             "in the crossing, in metres.")]
    public float crossingRadius = 8f;

    [Header("Traffic signal")]
    [Tooltip("Seconds of green in the cycle.")]
    public float greenSeconds = 12f;

    [Tooltip("Seconds of amber. Kept near the real value: North Carolina timings " +
             "run roughly three to six seconds depending on the approach speed.")]
    public float amberSeconds = 4f;

    [Tooltip("Seconds of red.")]
    public float redSeconds = 12f;

    [Tooltip("Seconds to shift this signal's cycle by, so a route with several " +
             "signals does not have all of them changing together.")]
    public float cycleOffset = 0f;

    [Tooltip("Allow a right turn on red here, after a complete stop.\n\n" +
             "North Carolina permits this at every signal unless a NO TURN ON RED sign " +
             "says otherwise, so it is on by default. Turn it off for a junction that " +
             "is signed against it: a one way street on the left, a blind approach, a " +
             "crossing with its own phase.")]
    public bool rightTurnOnRedAllowed = true;

    /// <summary>Where this signal is in its cycle right now.</summary>
    public enum Light { Green, Amber, Red }

    /// <summary>
    /// The signal state, derived from the clock rather than stored.
    ///
    /// Deriving it means every observer agrees without anything having to be kept in
    /// step, and it survives a scene reload with no state to reset. Time.time is used
    /// rather than unscaled time so the lights hold still while the game is paused
    /// behind the briefing or the debrief.
    /// </summary>
    public Light CurrentLight
    {
        get
        {
            float cycle = Mathf.Max(greenSeconds + amberSeconds + redSeconds, 0.1f);
            float t = Mathf.Repeat(Time.time + cycleOffset, cycle);

            if (t < greenSeconds) return Light.Green;
            if (t < greenSeconds + amberSeconds) return Light.Amber;
            return Light.Red;
        }
    }

    /// <summary>Name for the HUD.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(label)) return label;

            switch (kind)
            {
                case Kind.BusStop: return "Bus stop";
                case Kind.StopSign: return "Stop sign";
                case Kind.Crosswalk: return "Crosswalk";
                case Kind.TrafficSignal: return "Traffic signal";
                case Kind.Intersection: return "Intersection";
                case Kind.Finish: return "Depot";
                default: return "Checkpoint";
            }
        }
    }

    public Color GizmoColour
    {
        get
        {
            switch (kind)
            {
                case Kind.BusStop: return new Color(0.30f, 0.70f, 1f);
                case Kind.StopSign: return new Color(1f, 0.30f, 0.25f);
                case Kind.Crosswalk: return new Color(1f, 0.82f, 0.25f);
                case Kind.Intersection: return new Color(0.72f, 0.55f, 1f);

                // Whatever the light is showing, so the gizmo in the Scene view is
                // the same information the driver gets in the world.
                case Kind.TrafficSignal:
                    switch (CurrentLight)
                    {
                        case Light.Green: return new Color(0.30f, 0.90f, 0.42f);
                        case Light.Amber: return new Color(1f, 0.72f, 0.15f);
                        default: return new Color(1f, 0.25f, 0.20f);
                    }

                case Kind.Finish: return new Color(0.44f, 0.85f, 0.50f);
                default: return new Color(0.65f, 0.70f, 0.78f);
            }
        }
    }

    void OnDrawGizmos()
    {
        Color colour = GizmoColour;

        // Filled disc on the ground plus a vertical stalk, so a waypoint is visible
        // both from overhead and from a low camera angle.
        Gizmos.color = new Color(colour.r, colour.g, colour.b, 0.85f);
        DrawRing(transform.position, radius);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 6f);
        Gizmos.DrawSphere(transform.position + Vector3.up * 6f, 0.8f);

        if (kind == Kind.Crosswalk)
        {
            Gizmos.color = new Color(colour.r, colour.g, colour.b, 0.35f);
            DrawRing(transform.position, crossingRadius);
        }

        // Line to the next waypoint, so the route reads as a path rather than a
        // scatter of markers.
        Transform parent = transform.parent;
        if (parent == null) return;

        int index = transform.GetSiblingIndex();
        if (index + 1 >= parent.childCount) return;

        Transform next = parent.GetChild(index + 1);
        if (next == null) return;

        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Gizmos.DrawLine(transform.position + Vector3.up * 1.5f,
                        next.position + Vector3.up * 1.5f);
    }

    static void DrawRing(Vector3 centre, float radius)
    {
        const int segments = 36;
        Vector3 previous = centre + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = centre + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
