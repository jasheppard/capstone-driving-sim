using UnityEngine;

/// <summary>
/// A pedestrian who waits at a crossing and steps out when the bus comes.
///
/// WHY THIS EXISTS ALONGSIDE THE SPAWNER
///
/// PedestrianSpawner scatters people in a ring around the bus and lets them wander.
/// That is the right tool for making a campus feel populated, and the wrong one for
/// testing a driver: on photogrammetry terrain with no walkable surface authored, they
/// end up standing in hedges and on tree canopy, and whether anyone ever steps in
/// front of you is luck. A road test cannot be built on luck.
///
/// This is the opposite. One person, at a crossing you chose, who steps out when you
/// are close enough that you have to do something about it, and who is in the crossing
/// when you arrive. Every drive, the same place, the same decision. That is what makes
/// "yielding to pedestrians" an element that can actually be scored rather than one
/// that happens to come up.
///
/// IT IS DELIBERATELY NOT CLEVER
///
/// No avoidance, no pathfinding, no reacting to being nearly hit. A pedestrian who
/// gets out of your way is a pedestrian you were never tested against. This one walks
/// the crossing at a steady pace and it is the driver's job to not be there.
/// </summary>
public class CrossingWalker : MonoBehaviour
{
    [Header("Where")]
    [Tooltip("The crossing this person uses. Set by the editor tool.")]
    public RouteWaypoint crossing;

    [Tooltip("Direction across the road, set from the route when this was placed. " +
             "The walk runs along this line, through the crossing.")]
    public Vector3 across = Vector3.right;

    [Tooltip("How far the walk goes either side of the crossing, in metres. Needs to " +
             "clear the road, or they finish their walk in the middle of it.")]
    public float halfWidth = 7f;

    [Header("When")]
    [Tooltip("The bus. Left empty, it is found at startup.")]
    public Transform bus;

    [Tooltip("How close the bus must be before they step out, in metres. Far enough " +
             "that a driver at the speed limit has to react; near enough that they are " +
             "still in the road when it arrives.")]
    public float triggerDistance = 32f;

    [Tooltip("They only step out for a bus that is coming TOWARDS the crossing. " +
             "Without this they walk out behind you as you leave.")]
    public bool onlyWhenApproaching = true;

    [Tooltip("Seconds on the far kerb before they are willing to cross back.")]
    public float restSeconds = 8f;

    [Header("How")]
    [Tooltip("Walking pace in metres per second. 1.3 is an ordinary adult walk; " +
             "slower makes the crossing take longer and the driver wait.")]
    public float walkSpeed = 1.3f;

    [Tooltip("How far above the ground their feet sit, in metres.")]
    public float footHeight = 0.05f;

    enum State { Waiting, Crossing, Resting }

    State state = State.Waiting;
    Vector3 kerbA, kerbB;
    Vector3 target;
    float restRemaining;
    float lastGround;

    void Start()
    {
        if (bus == null)
        {
            VehicleController vehicle = Object.FindObjectOfType<VehicleController>();
            if (vehicle != null) bus = vehicle.transform;
        }

        Vector3 centre = crossing != null ? crossing.transform.position : transform.position;

        Vector3 line = across;
        line.y = 0f;

        if (line.sqrMagnitude < 0.01f) line = Vector3.right;
        line.Normalize();

        kerbA = centre - line * halfWidth;
        kerbB = centre + line * halfWidth;

        lastGround = centre.y;

        transform.position = Ground(kerbA);
        target = kerbB;

        Face(kerbB - kerbA);
    }

    void Update()
    {
        switch (state)
        {
            case State.Waiting: Wait(); break;
            case State.Crossing: Cross(); break;
            case State.Resting: Rest(); break;
        }
    }

    void Wait()
    {
        if (bus == null) return;

        Vector3 centre = crossing != null ? crossing.transform.position : transform.position;

        Vector2 toBus = Flat(bus.position - centre);
        if (toBus.magnitude > triggerDistance) return;

        // Coming towards the crossing, not away from it. Measured on the bus's own
        // facing rather than its velocity, so it still works when it is crawling.
        if (onlyWhenApproaching)
        {
            Vector2 facing = Flat(bus.forward);
            if (Vector2.Dot(facing.normalized, -toBus.normalized) < 0.3f) return;
        }

        state = State.Crossing;
        Face(target - transform.position);
    }

    void Cross()
    {
        Vector3 here = transform.position;
        Vector3 flatTarget = new Vector3(target.x, here.y, target.z);

        float step = walkSpeed * Time.deltaTime;

        if (Vector3.Distance(here, flatTarget) <= step)
        {
            transform.position = Ground(target);

            // Swap the kerbs so the next crossing goes back the other way.
            target = Vector3.Distance(target, kerbB) < 0.5f ? kerbA : kerbB;

            restRemaining = restSeconds;
            state = State.Resting;
            return;
        }

        Vector3 moved = Vector3.MoveTowards(here, flatTarget, step);
        transform.position = Ground(moved);

        Face(flatTarget - here);
    }

    void Rest()
    {
        restRemaining -= Time.deltaTime;
        if (restRemaining > 0f) return;

        state = State.Waiting;
        Face(target - transform.position);
    }

    /// <summary>
    /// Puts them on the ground, using the same rule the painted path uses: the highest
    /// surface near the height of the last step, which is what keeps them under trees
    /// rather than standing on the canopy.
    /// </summary>
    Vector3 Ground(Vector3 point)
    {
        bool found;
        float y = RoutePathVisual.GroundY(point.x, point.z, lastGround, 4f, out found);

        if (found) lastGround = y;

        return new Vector3(point.x, y + footHeight, point.z);
    }

    void Face(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;

        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    static Vector2 Flat(Vector3 v)
    {
        return new Vector2(v.x, v.z);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 centre = crossing != null ? crossing.transform.position : transform.position;

        Vector3 line = across;
        line.y = 0f;

        if (line.sqrMagnitude < 0.01f) line = Vector3.right;
        line.Normalize();

        Gizmos.color = new Color(1f, 0.82f, 0.25f, 0.9f);
        Gizmos.DrawLine(centre - line * halfWidth, centre + line * halfWidth);

        Gizmos.color = new Color(1f, 0.82f, 0.25f, 0.25f);
        Gizmos.DrawWireSphere(centre, triggerDistance);
    }
}
