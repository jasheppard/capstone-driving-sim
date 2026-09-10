using UnityEngine;

/// <summary>
/// Drives through the campus instead of into it.
///
/// Google's photogrammetry is a single scanned surface, not a model of a place. Every
/// tree, bollard, parked car and scanning artefact is baked into that surface as
/// solid geometry, so a kerb-height ripple that is not really there will still stop
/// thirteen tonnes of bus dead. When the job is laying out a route, that is nothing
/// but an obstacle course between you and the corner you were trying to reach.
///
/// Ghost mode turns collision off and floats the bus a fixed height above whatever
/// the ground is below it. You keep steering and the speed still reads correctly,
/// but nothing can block you.
///
/// THIS IS AN AUTHORING TOOL, NOT A CHEAT MODE
///
/// A run driven through solid objects is not a road test, and the numbers from it
/// would be worthless: no collisions, no braking for anything, and a route walked
/// through walls that a real bus could never take. So the moment ghost mode is
/// switched on, this switches the telemetry logger off and stops the drive report
/// being written. The run stops producing data rather than producing data that
/// quietly lies. Press Play again for a real run.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GhostDrive : MonoBehaviour
{
    [Header("Controls")]
    public KeyCode toggleKey = KeyCode.G;

    [Header("Riding the surface")]
    [Tooltip("Clearance between the bottom of the bus and the road, in metres. Near " +
             "zero, because the point is to drive on the road rather than fly over " +
             "it: the wheels should look planted. Raise it only if the bus visibly " +
             "sinks into the scan.")]
    public float rideHeight = 0.05f;

    [Tooltip("Turn this off only if you know why you are doing it. Left on, using " +
             "ghost mode stops the run writing telemetry or a report, because a drive " +
             "through solid objects is not a road test and a CSV that says otherwise " +
             "is worse than no CSV.")]
    public bool invalidateRun = true;

    [Tooltip("How far above the bus the ground ray starts. It has to begin above " +
             "anything the bus might currently be inside, or it starts underground " +
             "and finds nothing.")]
    public float rayStartAbove = 40f;

    public float rayLength = 200f;

    [Tooltip("How quickly the bus settles to its ride height. Instant snapping makes " +
             "the camera judder over every scanned bump.")]
    public float settleSpeed = 6f;

    public bool Ghosting { get; private set; }

    Rigidbody body;
    Collider hull;
    bool everUsed;

    /// <summary>
    /// Distance from the object's origin down to the bottom of its collider.
    ///
    /// Without this the bus is placed with its ORIGIN on the road, which buries the
    /// bottom half of it in the tarmac. The origin of a vehicle prefab is rarely at
    /// the wheels, so the offset has to be measured rather than assumed.
    /// </summary>
    float groundOffset = 1f;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void Start()
    {
        foreach (Collider candidate in GetComponentsInChildren<Collider>())
        {
            if (candidate.isTrigger) continue;
            if (hull == null || candidate.bounds.size.sqrMagnitude > hull.bounds.size.sqrMagnitude)
                hull = candidate;
        }

        if (hull != null)
            groundOffset = transform.position.y - hull.bounds.min.y;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) Toggle();
    }

    public void Toggle()
    {
        Ghosting = !Ghosting;

        // detectCollisions rather than moving to a different layer, because raycasts
        // against the terrain must keep working: the ground ray below is what holds
        // the bus up once nothing is holding it up physically.
        body.detectCollisions = !Ghosting;
        body.useGravity = !Ghosting;

        if (Ghosting && !everUsed && invalidateRun)
        {
            everUsed = true;
            Poison();
        }

        Debug.Log(Ghosting
            ? "[GhostDrive] Ghost mode ON. Driving through scenery. This run will not " +
              "be recorded or scored. Press " + toggleKey + " again to land."
            : "[GhostDrive] Ghost mode OFF. Solid again.");
    }

    /// <summary>
    /// Stops this run producing data, permanently, the first time ghost mode is used.
    ///
    /// Not reversible within the run on purpose. Ghosting for two seconds past a tree
    /// and then turning it off still means the drive contains a stretch no real
    /// vehicle could have made, and a CSV that says otherwise is worse than no CSV.
    /// </summary>
    void Poison()
    {
        DrivingTelemetryLogger logger = GetComponent<DrivingTelemetryLogger>();
        if (logger != null) logger.enabled = false;

        DriveScorer scorer = GetComponent<DriveScorer>();
        if (scorer != null) scorer.saveReport = false;

        NcRubric rubric = GetComponent<NcRubric>();
        if (rubric != null)
        {
            rubric.saveReport = false;
            rubric.appendToCohortFile = false;
        }

        Debug.LogWarning("[GhostDrive] Telemetry and the drive report are off for the " +
                         "rest of this run, because a drive through solid objects is " +
                         "not a road test.");
    }

    void FixedUpdate()
    {
        if (!Ghosting) return;

        RaycastHit hit;
        Vector3 start = transform.position + Vector3.up * rayStartAbove;

        if (!Physics.Raycast(start, Vector3.down, out hit, rayStartAbove + rayLength))
        {
            // Nothing below. Usually unstreamed tiles. Hold the current height rather
            // than falling into the void while the driver waits for them.
            Vector3 held = body.velocity;
            held.y = 0f;
            body.velocity = held;
            return;
        }

        // Ground, plus the distance from the origin down to the bottom of the bus, so
        // the wheels sit on the road instead of the origin sitting on it.
        float target = hit.point.y + groundOffset + rideHeight;

        Vector3 position = transform.position;
        position.y = Mathf.Lerp(position.y, target, settleSpeed * Time.fixedDeltaTime);

        body.MovePosition(new Vector3(position.x, position.y, position.z));

        // Vertical speed is now controlled by the lerp above, so any left over from
        // before the toggle has to go, or it fights it.
        Vector3 velocity = body.velocity;
        velocity.y = 0f;
        body.velocity = velocity;
    }

    void OnDisable()
    {
        // Never leave the bus intangible for the next scene or the next run.
        if (body == null) return;

        body.detectCollisions = true;
        body.useGravity = true;
    }
}
