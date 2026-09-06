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

    [Header("Variety")]
    [Tooltip("Random height multiplier, so a crowd is not all the same person's size.")]
    public Vector2 scaleRange = new Vector2(0.94f, 1.06f);

    [Tooltip("Random walk speed multiplier. Small differences read as individual people.")]
    public Vector2 speedRange = new Vector2(0.85f, 1.15f);

    CharacterController controller;
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

        if (!idling && (IsBlockedAhead() || IsEdgeAhead()))
        {
            // Turn away rather than stop, so they flow around obstacles.
            targetYaw += Random.Range(0f, 1f) > 0.5f ? 90f : -90f;
            idleUntil = 0f;
            ScheduleNextDecision();
        }

        float yaw = Mathf.MoveTowardsAngle(
            transform.eulerAngles.y, targetYaw, turnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        bool fleeing = Time.time < fleeUntil;
        float speed = idling && !fleeing ? 0f : (fleeing ? fleeSpeed : walkSpeed);
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
        else
        {
            targetYaw += Random.Range(-headingChangeRange, headingChangeRange);
        }

        ScheduleNextDecision();
    }

    void ScheduleNextDecision()
    {
        nextDecisionTime = Time.time +
            Random.Range(headingChangeInterval.x, headingChangeInterval.y);
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
