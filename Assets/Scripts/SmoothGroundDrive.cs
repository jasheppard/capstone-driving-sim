using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Drives the bus over scanned terrain without letting the terrain fight it.
///
/// WHY THE COLLISION APPROACH KEPT FAILING
///
/// Everything before this tried to make a rigid box slide along Google's
/// photogrammetry: bigger step tolerance, better collider, more clearance. None of it
/// could work, because a scanned surface is not a road. It is a noisy height field
/// full of centimetre ripples, false kerbs and seams between tiles, and a box resting
/// on it catches on every one of them. Catching feels like an invisible wall; being
/// pushed out of one feels like a jump.
///
/// So the box stops resting on it. Height comes from a downward ray instead, and the
/// bus is placed at the height the ground actually is, smoothed. Ripples cannot stop
/// it because nothing is touching them, and it cannot be launched off a bump because
/// nothing is pushing it up.
///
/// WHAT STILL STOPS THE BUS
///
/// Physical collision is off, so blocking is done deliberately rather than by
/// accident. A ray is cast forward at knee height: anything solid up there is a wall,
/// a building or a parked car, and forward motion is cancelled. Anything below knee
/// height is ground, and the bus drives over it.
///
/// Pedestrians are checked separately, by proximity, because with collision off they
/// would otherwise never register, and striking one is the most serious failure on
/// the test.
///
/// THE HONEST CAVEAT
///
/// This is a simplification, and the report should say so. Contact with the ground is
/// no longer simulated; it is asserted. Everything that makes the vehicle model worth
/// defending is untouched, because acceleration, braking, cornering limits and the
/// stopping distances all live in VehicleController and none of them depend on how
/// the wheels meet the road. What is lost is suspension behaviour over rough ground,
/// which this project was never modelling in the first place.
/// </summary>
[DefaultExecutionOrder(200)]   // after VehicleController has moved the bus
[RequireComponent(typeof(Rigidbody))]
public class SmoothGroundDrive : MonoBehaviour
{
    [Header("Riding the ground")]
    [Tooltip("Clearance between the bottom of the bus and the ground, in metres. " +
             "Small: the wheels should look planted, not hovering.")]
    public float rideHeight = 0.02f;

    [Tooltip("How quickly the bus follows the ground. Higher sticks to every " +
             "contour, lower glides over dips and reads as suspension. Around 8 is " +
             "a bus, 20 is a go-kart.")]
    public float followSpeed = 8f;

    [Tooltip("Biggest single height change the bus will follow in one step, in " +
             "metres. Anything sharper than this is a scanning artefact rather than " +
             "a road, and is smoothed out instead of followed.")]
    public float maximumStep = 0.6f;

    [Header("What still blocks you")]
    [Tooltip("Height above the ground at which something counts as an obstacle " +
             "rather than as ground, in metres. Below this you drive over it.")]
    public float kneeHeight = 0.55f;

    [Tooltip("How far ahead obstacles are detected, in metres.")]
    public float lookAhead = 3f;

    [Tooltip("Half the width of the bus, used to check both front corners as well " +
             "as the centre. Without the corners you can drive a wall through the " +
             "side of the bus.")]
    public float halfWidth = 1.1f;

    [Tooltip("How hard the vehicle is stopped when it meets something solid, in " +
             "m/s^2. Left at zero it matches the vehicle's own braking rate, which " +
             "is what you want: a block should never produce a deceleration the " +
             "brakes could not, or the telemetry records something the vehicle " +
             "cannot physically do. Set it above zero only to override that.")]
    public float blockDeceleration = 0f;

    /// <summary>
    /// The rate a block actually sheds speed at.
    ///
    /// Matched to the vehicle's brakes by default. An earlier version deleted the
    /// forward velocity outright, which the logger recorded as 0.88 g on a vehicle
    /// whose brakes make 0.59 g; the fix after that shed speed at a fixed 8 m/s2,
    /// which was better but still harder than the vehicle can brake. Anything
    /// firmer than the brakes puts impossible rows in the data, and impossible rows
    /// have to be explained away in every analysis that uses the file.
    /// </summary>
    float BlockRate
    {
        get
        {
            if (blockDeceleration > 0f) return blockDeceleration;
            return vehicle != null ? vehicle.brakeDeceleration : 5.8f;
        }
    }

    [Header("Pedestrians")]
    [Tooltip("With collision off, people are found by proximity instead. This is how " +
             "close counts as striking one, in metres.")]
    public float strikeDistance = 1.6f;

    [Tooltip("Below this speed, being close to someone is not a strike.")]
    public float strikeSpeedMph = 3f;

    // ---------------------------------------------------------------- internals

    Rigidbody body;
    Collider hull;
    NcRubric rubric;
    VehicleController vehicle;

    float groundOffset = 1f;      // origin to the bottom of the collider
    float lastGroundY;
    bool haveGround;

    readonly List<Transform> pedestrians = new List<Transform>();
    float nextPedestrianScan;
    bool struckSomeone;

    void Start()
    {
        body = GetComponent<Rigidbody>();
        rubric = GetComponent<NcRubric>();
        if (rubric == null) rubric = FindObjectOfType<NcRubric>();

        vehicle = GetComponent<VehicleController>();

        foreach (Collider candidate in GetComponentsInChildren<Collider>())
        {
            if (candidate.isTrigger) continue;
            if (hull == null || candidate.bounds.size.sqrMagnitude > hull.bounds.size.sqrMagnitude)
                hull = candidate;
        }

        if (hull != null) groundOffset = transform.position.y - hull.bounds.min.y;

        // The terrain stops pushing the bus around. Gravity goes with it, because the
        // ground ray is what holds the bus up now and the two would fight.
        body.detectCollisions = false;
        body.useGravity = false;

        // These two exist only to work around collision with the terrain, which is no
        // longer happening. Left on they would do nothing at best.
        StepAssist assist = GetComponent<StepAssist>();
        if (assist != null) assist.enabled = false;

        Debug.Log("[SmoothGroundDrive] Riding the ground by raycast. Terrain collision " +
                  "is off, so ripples and seams cannot block or launch the bus. " +
                  "Anything above " + kneeHeight + " m still stops it.");
    }

    void OnDisable()
    {
        // Never leave the bus intangible and weightless for whatever comes next.
        if (body == null) return;

        body.detectCollisions = true;
        body.useGravity = true;
    }

    void FixedUpdate()
    {
        Block();
        Follow();
        CheckPedestrians();
    }

    // -------------------------------------------------------------------- ground

    /// <summary>
    /// Puts the bus at the height of the ground beneath it.
    ///
    /// The ray starts well above the bus so it cannot begin inside geometry the bus
    /// is currently overlapping, which is the usual reason a ground ray finds
    /// nothing and a vehicle drops through the world.
    /// </summary>
    void Follow()
    {
        RaycastHit hit;
        Vector3 start = transform.position + Vector3.up * 50f;

        if (!Physics.Raycast(start, Vector3.down, out hit, 300f))
        {
            // Unstreamed tiles. Hold the last known height rather than falling.
            Hold();
            return;
        }

        float groundY = hit.point.y;

        // A sudden jump in the reading is a tile seam or a scanned object passing
        // under the ray, not a real change in the road. Clamped so the bus does not
        // hop when the ray briefly lands on a bush.
        if (haveGround)
            groundY = Mathf.Clamp(groundY, lastGroundY - maximumStep, lastGroundY + maximumStep);

        lastGroundY = groundY;
        haveGround = true;

        float target = groundY + groundOffset + rideHeight;

        Vector3 position = body.position;
        position.y = Mathf.Lerp(position.y, target, followSpeed * Time.fixedDeltaTime);
        body.MovePosition(position);

        Hold();
    }

    /// <summary>Vertical speed is owned by the follow above, so anything else goes.</summary>
    void Hold()
    {
        Vector3 velocity = body.velocity;
        velocity.y = 0f;
        body.velocity = velocity;
    }

    // ------------------------------------------------------------------ blocking

    /// <summary>
    /// Cancels forward motion when something tall is ahead.
    ///
    /// Three rays rather than one: centre and both front corners. A single centre ray
    /// misses a wall approached at an angle, which lets the corner of the bus pass
    /// through a building while the middle of it is still in clear air.
    /// </summary>
    void Block()
    {
        Vector3 flat = new Vector3(body.velocity.x, 0f, body.velocity.z);
        if (flat.sqrMagnitude < 0.04f) return;

        Vector3 heading = flat.normalized;

        float y = (hull != null ? hull.bounds.min.y : transform.position.y) + kneeHeight;
        Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;

        for (int i = -1; i <= 1; i++)
        {
            Vector3 from = new Vector3(transform.position.x, y, transform.position.z)
                           + right * (halfWidth * i);

            if (!Physics.Raycast(from, heading, lookAhead)) continue;

            // Something solid at knee height ahead. Bleed the forward component off
            // at a rate a vehicle could actually manage, rather than deleting it.
            //
            // Deleting it was the bug: a real recorded drive showed a hardest-braking
            // figure of 1.69 g on a vehicle whose brakes do 0.59 g, because an
            // instant stop looks to the scorer exactly like an enormous one, and the
            // driver was being marked down for hitting a wall the terrain put in
            // their way. Bleeding it off keeps the block firm and keeps every
            // deceleration in the telemetry physically possible.
            //
            // Only the forward component goes, so the bus can still be steered away
            // rather than welded to the wall.
            Vector3 velocity = body.velocity;
            Vector3 forward = Vector3.Project(velocity, heading);

            float shed = BlockRate * Time.fixedDeltaTime;
            Vector3 slowed = Vector3.MoveTowards(forward, Vector3.zero, shed);

            body.velocity = velocity - forward + slowed;
            return;
        }
    }

    // --------------------------------------------------------------- pedestrians

    void CheckPedestrians()
    {
        if (rubric == null || struckSomeone) return;

        if (Time.time >= nextPedestrianScan)
        {
            pedestrians.Clear();

            foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "PedestrianController") continue;
                pedestrians.Add(mb.transform);
            }

            nextPedestrianScan = Time.time + 2f;
        }

        float speedMph = body.velocity.magnitude * 2.23694f;
        if (speedMph < strikeSpeedMph) return;

        for (int i = 0; i < pedestrians.Count; i++)
        {
            if (pedestrians[i] == null) continue;
            if (Vector3.Distance(transform.position, pedestrians[i].position) > strikeDistance)
                continue;

            struckSomeone = true;
            rubric.RegisterCritical(NcElement.YieldToPedestrians, "Struck a pedestrian");
            rubric.RegisterAutomaticFail("struck a pedestrian");
            return;
        }
    }
}
