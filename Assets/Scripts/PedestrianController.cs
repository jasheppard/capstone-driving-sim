using UnityEngine;

/// <summary>
/// A wandering pedestrian.
///
/// Deliberately not a NavMeshAgent: the campus ground is Cesium photogrammetry that
/// streams in at runtime, so there is no surface to bake a NavMesh against in the
/// editor. Instead this walks forward, feels ahead with a raycast, and turns away
/// from walls and from edges where the ground drops out.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PedestrianController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Set from the walk clip's own stride speed so the feet do not slide.")]
    public float walkSpeed = 1.45f;
    public float turnSpeed = 160f;
    public float gravity = -20f;

    [Header("Wandering")]
    [Tooltip("Seconds between picking a new heading.")]
    public Vector2 headingChangeInterval = new Vector2(4f, 10f);

    [Tooltip("How far the new heading can differ from the current one, in degrees.")]
    public float headingChangeRange = 70f;

    [Tooltip("Chance per heading change of standing still for a moment instead.")]
    [Range(0f, 1f)] public float idleChance = 0.2f;
    public Vector2 idleDuration = new Vector2(2f, 6f);

    [Header("Sensing")]
    [Tooltip("How far ahead to check for walls.")]
    public float obstacleDistance = 1.5f;

    [Tooltip("How far ahead to check that the ground continues.")]
    public float edgeCheckDistance = 1.2f;

    [Tooltip("Drop bigger than this counts as an edge to avoid.")]
    public float maxStepDown = 1.5f;

    [Header("Animation")]
    public Animator animator;
    public string speedParameter = "Speed";

    [Header("Reaction")]
    [Tooltip("Speed when running away after being hit. Match the run clip so the " +
             "legs keep up.")]
    public float fleeSpeed = 4.4f;

    [Tooltip("How long they keep running before settling back to a walk.")]
    public float fleeDuration = 5f;

    [Header("Sidewalks")]
    [Tooltip("Set at runtime by the spawner. With a path assigned the pedestrian " +
             "walks it point to point instead of wandering, which is the only way " +
             "to keep people off a road the geometry cannot identify.")]
    public SidewalkPath path;

    [Tooltip("How close counts as having reached a point, in metres. Too small " +
             "and they overshoot and circle back; too large and they cut corners.")]
    public float arriveDistance = 1.1f;

    [Header("Variety")]
    [Tooltip("Random height multiplier, so a crowd is not all the same person's size.")]
    public Vector2 scaleRange = new Vector2(0.94f, 1.06f);

    [Tooltip("Random walk speed multiplier. Small differences read as individual people.")]
    public Vector2 speedRange = new Vector2(0.85f, 1.15f);

    CharacterController controller;

    // Path following state. laneOffset is picked once and kept, so a pedestrian
    // walks a consistent line down the width of the pavement rather than weaving
    // across it every time it picks a new point.
    int nodeIndex;
    int step = 1;
    float laneOffset;
    Vector3 goal;
    bool haveGoal;
    bool crossing;
    float waitUntil;
    float avoidUntil;

    float targetYaw;
    float verticalVelocity;
    float nextDecisionTime;
    float idleUntil;
    float fleeUntil;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        targetYaw = transform.eulerAngles.y;
        ScheduleNextDecision();

        // Without this every pedestrian walks in perfect lockstep, which is the
        // single most obvious tell that a crowd is fake.
        walkSpeed *= Random.Range(speedRange.x, speedRange.y);
        transform.localScale *= Random.Range(scaleRange.x, scaleRange.y);
    }

    void Start()
    {
        if (animator == null) return;

        // Start each one at a different point in the walk cycle.
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        animator.Play(state.fullPathHash, 0, Random.value);
    }

    /// <summary>Called by the spawner when a pedestrian is recycled to a new spot.</summary>
    public void ResetState(float yaw)
    {
        targetYaw = yaw;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        verticalVelocity = 0f;
        idleUntil = 0f;
        fleeUntil = 0f;
        ScheduleNextDecision();
    }

    /// <summary>Turn away from whatever just hit us and run for it.</summary>
    public void Flee(Vector3 threatPosition)
    {
        Vector3 away = transform.position - threatPosition;
        away.y = 0f;

        if (away.sqrMagnitude > 0.0001f)
            targetYaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;

        fleeUntil = Time.time + fleeDuration;
        idleUntil = 0f;

        // Push the next wander decision past the flee, so they do not casually
        // turn back toward you mid-panic.
        nextDecisionTime = fleeUntil;
    }

    public bool IsFleeing()
    {
        return Time.time < fleeUntil;
    }

    void Update()
    {
        if (Time.time >= nextDecisionTime) MakeDecision();

        bool idling = Time.time < idleUntil && Time.time >= fleeUntil;

        bool onPath = path != null && path.Count >= 2 && Time.time >= fleeUntil;

        if (!idling && (IsBlockedAhead() || IsEdgeAhead()))
        {
            // Turn away rather than stop, so they flow around obstacles. On a path
            // the turn is smaller and temporary: a full 90 degrees would send them
            // off the pavement, which is the thing this is all for. They sidestep,
            // then aim back at the point they were heading for.
            targetYaw += Random.Range(0f, 1f) > 0.5f
                ? (onPath ? 55f : 90f)
                : (onPath ? -55f : -90f);

            idleUntil = 0f;
            avoidUntil = Time.time + 0.9f;
            ScheduleNextDecision();
        }

        if (onPath) SteerAlongPath();

        float yaw = Mathf.MoveTowardsAngle(
            transform.eulerAngles.y, targetYaw, turnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        bool fleeing = Time.time < fleeUntil;
        bool waiting = Time.time < waitUntil && !fleeing;
        float speed = (idling || waiting) && !fleeing ? 0f : (fleeing ? fleeSpeed : walkSpeed);
        Vector3 motion = transform.forward * speed;

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        else verticalVelocity += gravity * Time.deltaTime;

        motion.y = verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        if (animator != null)
            animator.SetFloat(speedParameter, speed, 0.12f, Time.deltaTime);
    }

    void MakeDecision()
    {
        if (Random.value < idleChance)
        {
            idleUntil = Time.time + Random.Range(idleDuration.x, idleDuration.y);
        }
        else if (path == null)
        {
            // Only wander when there is no path. With one, the heading belongs to
            // the path and a random turn here would undo it.
            targetYaw += Random.Range(-headingChangeRange, headingChangeRange);
        }

        ScheduleNextDecision();
    }

    void ScheduleNextDecision()
    {
        nextDecisionTime = Time.time +
            Random.Range(headingChangeInterval.x, headingChangeInterval.y);
    }

    // ---------------------------------------------------------------- walkways

    /// <summary>Assigns a walkway and drops the pedestrian into it at a point.</summary>
    public void FollowPath(SidewalkPath newPath, int startIndex)
    {
        path = newPath;
        nodeIndex = startIndex;
        step = Random.value < 0.5f ? 1 : -1;

        float halfWidth = newPath != null ? newPath.width * 0.5f : 0f;
        laneOffset = Random.Range(-halfWidth, halfWidth);

        haveGoal = false;
        crossing = false;
        waitUntil = 0f;
        avoidUntil = 0f;
    }

    /// <summary>Points the pedestrian at the next place on the walkway.</summary>
    void SteerAlongPath()
    {
        if (Time.time < waitUntil) return;
        if (!haveGoal) PickGoal();

        Vector3 toGoal = goal - transform.position;
        toGoal.y = 0f;

        if (toGoal.magnitude <= arriveDistance)
        {
            Arrive();
            return;
        }

        // While sidestepping an obstacle the avoidance heading wins, or the two
        // fight each other and the pedestrian grinds along the thing it hit.
        if (Time.time >= avoidUntil && toGoal.sqrMagnitude > 0.0001f)
            targetYaw = Mathf.Atan2(toGoal.x, toGoal.z) * Mathf.Rad2Deg;
    }

    /// <summary>Works out where on the pavement the next point actually is.</summary>
    void PickGoal()
    {
        Vector3 basePoint = path.Position(nodeIndex);

        // Offset across the walkway rather than along it, so the lane spreads the
        // crowd out sideways instead of making them early or late.
        Vector3 across = Vector3.Cross(Vector3.up, path.DirectionAt(nodeIndex));

        goal = basePoint + across.normalized * laneOffset;
        haveGoal = true;
    }

    /// <summary>Reached a point: cross here, or carry on along the walkway.</summary>
    void Arrive()
    {
        if (crossing)
        {
            // Just finished stepping across the road. Pick up the far pavement.
            crossing = false;
            haveGoal = false;
            return;
        }

        Transform node = path.Node(nodeIndex);
        SidewalkCrossing link = node != null ? node.GetComponent<SidewalkCrossing>() : null;

        if (link != null && link.otherSide != null && Random.value < link.crossChance)
        {
            SidewalkPath farPath = link.OtherPath;
            int farIndex = link.OtherIndex;

            if (farPath != null && farIndex >= 0)
            {
                // Hesitate at the kerb first. Without this a group arriving together
                // steps into the road in one block, which looks staged and gives the
                // driver no warning that anyone was waiting.
                waitUntil = Time.time + Random.Range(link.waitSeconds.x, link.waitSeconds.y);

                path = farPath;
                nodeIndex = farIndex;
                crossing = true;
                haveGoal = false;

                goal = link.otherSide.position;
                haveGoal = true;
                return;
            }
        }

        AdvanceNode();
        haveGoal = false;
    }

    /// <summary>
    /// Walks on to the next point, turning around at the ends of an open path and
    /// wrapping around on a looped one.
    /// </summary>
    void AdvanceNode()
    {
        nodeIndex += step;

        if (nodeIndex >= path.Count)
        {
            if (path.loop) nodeIndex = 0;
            else { step = -1; nodeIndex = Mathf.Max(0, path.Count - 2); }
        }
        else if (nodeIndex < 0)
        {
            if (path.loop) nodeIndex = path.Count - 1;
            else { step = 1; nodeIndex = Mathf.Min(1, path.Count - 1); }
        }
    }

    bool IsBlockedAhead()
    {
        Vector3 origin = transform.position + Vector3.up * 0.4f;

        RaycastHit[] hits = Physics.RaycastAll(
            origin, transform.forward, obstacleDistance, ~0, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider.transform.IsChildOf(transform)) return true;
        }

        return false;
    }

    /// <summary>True when the ground ahead falls away — a kerb edge, steps, or a gap in the tiles.</summary>
    bool IsEdgeAhead()
    {
        Vector3 ahead = transform.position + transform.forward * edgeCheckDistance + Vector3.up * 0.5f;

        RaycastHit[] hits = Physics.RaycastAll(
            ahead, Vector3.down, maxStepDown + 1f, ~0, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            return false;      // found ground within reach, so no edge
        }

        return true;
    }
}
