using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the vehicle on the road.
///
/// Runs after SmoothGroundDrive, which owns height, and corrects only the flat
/// position. The two never fight because they work on different axes: the ground
/// script decides how high the vehicle is, this one decides how far sideways it is
/// allowed to be.
///
/// Three zones, which is what makes it feel like a road rather than a cage:
///
///   inside the half-width      nothing happens at all
///   in the shoulder            a warning, and a gentle nudge back
///   past the shoulder          held at the edge, outward speed removed
///
/// Nothing teleports. The correction is a velocity and a bounded move per physics
/// step, so at the limit the vehicle slides along the boundary the way a car scrubs
/// a kerb, and the driver keeps steering out of it under their own power.
/// </summary>
[DefaultExecutionOrder(300)]           // after SmoothGroundDrive, which is 200
[RequireComponent(typeof(Rigidbody))]
public class RoadBoundary : MonoBehaviour
{
    [Header("Feel")]
    [Tooltip("How briskly the vehicle is returned to the road once past the " +
             "shoulder, as a return speed per metre out. Low feels like soft verge, " +
             "high feels like a wall. Around 3 reads as a kerb, 1 as grass.")]
    public float pushSpeed = 3f;

    [Tooltip("How quickly outward motion is bled off, per second. This is the knob " +
             "that decides whether the boundary feels like a wall or a wide verge. " +
             "8 lets you drift a metre or two before it takes hold; 40 stops you " +
             "almost on the line; 100 is effectively solid.")]
    public float firmness = 8f;

    [Tooltip("Ceiling on the correction applied in one physics step, in metres. A " +
             "cap keeps a badly placed corridor from flinging the vehicle.")]
    public float maximumCorrection = 0.5f;

    [Header("Warning")]
    [Tooltip("Optional. Shows a message while the vehicle is on the shoulder or " +
             "past it. Leave empty and the boundary works silently.")]
    public Text warningLabel;

    [Tooltip("What the warning says on the shoulder.")]
    public string shoulderMessage = "KEEP RIGHT";

    [Tooltip("What it says once the boundary is holding the vehicle.")]
    public string edgeMessage = "OFF ROAD";

    [Header("Behaviour")]
    [Tooltip("Leave on. Off, the boundary reports and warns but never moves the " +
             "vehicle, which is useful for checking a corridor's width without " +
             "being shoved around while you drive it.")]
    public bool enforce = true;

    /// <summary>How far outside the road the vehicle is, in metres. Zero when inside.</summary>
    public float Excursion { get; private set; }

    /// <summary>True while the boundary is actively holding the vehicle in.</summary>
    public bool OutOfBounds { get; private set; }

    Rigidbody body;
    RoadCorridor corridor;
    float nextSearch;

    void Start()
    {
        body = GetComponent<Rigidbody>();

        if (RoadCorridor.All.Count == 0)
            Debug.Log("[RoadBoundary] No road corridor in the scene, so nothing is " +
                      "bounded. Run NCAT > Road > 1. Build Corridor From Route.");
    }

    void FixedUpdate()
    {
        RoadCorridor road = Find();

        if (road == null)
        {
            Clear();
            return;
        }

        Vector3 closest;
        float distance;

        if (!road.Nearest(transform.position, out closest, out distance))
        {
            Clear();
            return;
        }

        Excursion = Mathf.Max(0f, distance - road.halfWidth);
        OutOfBounds = distance > road.halfWidth + road.shoulder;

        Warn(road, distance);

        if (!enforce || !OutOfBounds) return;

        // The direction back toward the middle of the road, flat.
        Vector3 inward = closest - new Vector3(transform.position.x, 0f, transform.position.z);
        inward.y = 0f;

        if (inward.sqrMagnitude < 0.0001f) return;
        inward.Normalize();

        // The whole correction is done in velocity, and deliberately so.
        //
        // The obvious implementation moves the vehicle with MovePosition. It is
        // wrong here. SmoothGroundDrive has already called MovePosition this step to
        // set the height, a second call replaces that target rather than adding to
        // it, and the height correction is silently lost for every frame the
        // boundary is active. The symptom is a vehicle that sinks or floats away
        // from the terrain while it scrapes along the edge of the road.
        //
        // Working in velocity leaves position entirely to the ground script, so the
        // two never touch the same value.
        Vector3 velocity = body.velocity;

        // Bleed off the part of the velocity heading further out.
        //
        // Exponentially, at a rate per second, rather than removing it outright.
        // Removing it outright is a wall: the vehicle stops crossing the line in a
        // single physics step and the driver feels a hard edge they cannot see. Bled
        // off, the vehicle carries a little way past the line and is drawn back,
        // which is what running onto a verge actually feels like.
        //
        // Framed per second, so the feel does not change if the physics rate does.
        float outward = Vector3.Dot(velocity, -inward);

        if (outward > 0f)
        {
            float bleed = 1f - Mathf.Exp(-firmness * Time.fixedDeltaTime);
            velocity += inward * (outward * bleed);
        }

        // Then a return speed proportional to how far out it is, so a small
        // excursion is nudged and a large one is brought back briskly. Capped, so a
        // badly placed corridor cannot fling the vehicle.
        float overshoot = distance - (road.halfWidth + road.shoulder);
        float returnSpeed = Mathf.Min(overshoot * pushSpeed, maximumCorrection / Time.fixedDeltaTime);

        velocity += inward * returnSpeed;

        // Vertical speed is owned by SmoothGroundDrive. Leaving it untouched is what
        // keeps this correction confined to the flat.
        velocity.y = body.velocity.y;
        body.velocity = velocity;
    }

    /// <summary>
    /// The corridor the vehicle is on, re-checked a few times a second.
    ///
    /// Cached rather than searched every step: with several corridors in a scene
    /// this would otherwise walk every point of every one of them fifty times a
    /// second, and the answer changes only when the vehicle drives somewhere else.
    /// </summary>
    RoadCorridor Find()
    {
        if (Time.time < nextSearch && corridor != null) return corridor;
        nextSearch = Time.time + 0.4f;

        RoadCorridor best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < RoadCorridor.All.Count; i++)
        {
            RoadCorridor candidate = RoadCorridor.All[i];
            if (candidate == null || candidate.Count < 2) continue;

            Vector3 point;
            float distance;

            if (!candidate.Nearest(transform.position, out point, out distance)) continue;
            if (distance >= bestDistance) continue;

            best = candidate;
            bestDistance = distance;
        }

        corridor = best;
        return corridor;
    }

    void Warn(RoadCorridor road, float distance)
    {
        if (warningLabel == null) return;

        if (distance > road.halfWidth + road.shoulder)
        {
            warningLabel.text = edgeMessage;
            warningLabel.color = new Color(1f, 0.42f, 0.34f);
        }
        else if (distance > road.halfWidth)
        {
            warningLabel.text = shoulderMessage;
            warningLabel.color = new Color(1f, 0.78f, 0.35f);
        }
        else
        {
            warningLabel.text = "";
        }
    }

    void Clear()
    {
        Excursion = 0f;
        OutOfBounds = false;

        if (warningLabel != null) warningLabel.text = "";
    }
}
