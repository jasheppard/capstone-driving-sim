using UnityEngine;

/// <summary>
/// Lets the bus climb over the small rubbish in the scan without letting it climb
/// buildings.
///
/// THE PROBLEM
///
/// Google's photogrammetry is one continuous surface. The road, the kerb, the bush
/// beside the kerb and the wall behind it are all the same mesh, so there is no
/// object to delete and no layer to filter: a five centimetre scanning ripple stops
/// the bus exactly as hard as a building does. Ghost mode gets past that by turning
/// collision off, but a route laid out while intangible is a route no real vehicle
/// could drive, which defeats the point of laying it out by driving.
///
/// THE DISCRIMINATOR
///
/// What separates a kerb from a wall is not what it is made of, it is how tall it is.
/// So when the bus is blocked, this asks one question: is there anything still in the
/// way at knee height? A ray is cast forward from just above the step limit. If it
/// hits something, the obstacle is tall and the bus stays blocked, as it should
/// against a building. If it is clear, the obstacle ends below the step limit and the
/// bus is lifted over it.
///
/// That is the same trick a character controller's step offset uses, applied to a
/// Rigidbody, and it is why this cannot be used to climb a wall: every frame of the
/// climb re-asks the question, and a wall answers yes every time.
///
/// WHAT IT DOES NOT CHANGE
///
/// Collision stays on. Pedestrians still register, so striking one is still the
/// automatic failure it should be, and buildings still stop the bus. None of the
/// acceleration, braking or cornering model is touched. The only thing altered is
/// what counts as ground.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class StepAssist : MonoBehaviour
{
    [Header("What counts as a step")]
    [Tooltip("The tallest thing the bus will climb over, in metres. A real kerb is " +
             "about 0.15 and scan artefacts run to roughly 0.4, so half a metre " +
             "clears the junk while leaving anything wall-shaped solid.")]
    public float maxStepHeight = 0.5f;

    [Tooltip("How far ahead to check for a tall obstacle. Roughly the length of the " +
             "bonnet: far enough to see a wall, near enough not to see the next one " +
             "down the street.")]
    public float lookAhead = 2f;

    [Tooltip("Extra height above the step limit that the blocking ray is cast from, " +
             "so the ray does not clip the very obstacle it is meant to ignore.")]
    public float rayMargin = 0.12f;

    [Header("The climb")]
    [Tooltip("How fast the bus is raised over an obstacle, in metres per second. Too " +
             "fast reads as teleporting, too slow and it grinds to a stop first.")]
    public float climbSpeed = 2.5f;

    [Tooltip("Below this forward speed nothing is attempted. Lifting a stationary bus " +
             "would let it climb out of a car park sideways.")]
    public float minimumSpeedMph = 1.5f;

    [Header("Diagnostics")]
    [Tooltip("Count of obstacles climbed this run, for the Console at the end.")]
    public bool logSummary = true;

    Rigidbody body;
    Collider hull;

    bool blockedLow;
    float climbRemaining;
    int climbs;

    void Start()
    {
        body = GetComponent<Rigidbody>();

        // The largest collider on the vehicle, which is the chassis rather than any
        // trigger volume that may have been added around it.
        foreach (Collider candidate in GetComponentsInChildren<Collider>())
        {
            if (candidate.isTrigger) continue;
            if (hull == null || candidate.bounds.size.sqrMagnitude > hull.bounds.size.sqrMagnitude)
                hull = candidate;
        }

        if (hull == null)
            Debug.LogWarning("[StepAssist] No solid collider found on the vehicle, so " +
                             "there is no bottom edge to measure a step from.");
    }

    void OnCollisionStay(Collision collision)
    {
        if (hull == null) return;

        // Pedestrians are not scenery. Climbing over one would turn the most serious
        // failure on the test into a bump in the road.
        foreach (MonoBehaviour mb in collision.gameObject.GetComponentsInParent<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name == "PedestrianController") return;
        }

        float bottom = hull.bounds.min.y;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);

            // A mostly upward normal is ground being stood on, not something being
            // driven into. Only near-vertical faces block forward progress.
            if (contact.normal.y > 0.4f) continue;

            if (contact.point.y - bottom > maxStepHeight) continue;

            blockedLow = true;
            return;
        }
    }

    void FixedUpdate()
    {
        if (hull == null || body == null) return;

        bool wasBlocked = blockedLow;
        blockedLow = false;

        // Finish a climb already under way before considering a new one, so a bus
        // halfway over a kerb is not dropped back onto it.
        if (climbRemaining > 0f)
        {
            float rise = Mathf.Min(climbRemaining, climbSpeed * Time.fixedDeltaTime);
            body.MovePosition(body.position + Vector3.up * rise);
            climbRemaining -= rise;
            return;
        }

        if (!wasBlocked) return;

        Vector3 flat = new Vector3(body.velocity.x, 0f, body.velocity.z);
        if (flat.magnitude * 2.23694f < minimumSpeedMph) return;

        Vector3 heading = flat.normalized;

        // The question that separates a kerb from a wall: is anything still there
        // above the step limit?
        Vector3 rayFrom = new Vector3(body.position.x,
                                      hull.bounds.min.y + maxStepHeight + rayMargin,
                                      body.position.z);

        if (Physics.Raycast(rayFrom, heading, lookAhead)) return;    // tall, stay blocked

        climbRemaining = maxStepHeight;
        climbs++;
    }

    void OnDisable()
    {
        if (logSummary && climbs > 0)
            Debug.Log("[StepAssist] Climbed " + climbs + " low obstacles this run. If " +
                      "that number is in the hundreds, maxStepHeight is too high and " +
                      "the bus is crawling over things it should be stopped by.");
    }
}
