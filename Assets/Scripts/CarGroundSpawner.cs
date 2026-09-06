using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Holds the vehicle still until there is stable ground underneath it, then
/// seats it exactly on the surface — no drop.
///
/// Two problems this solves:
///   1. Cesium streams terrain in after Play starts. Without waiting, the car
///      spawns into empty space and falls out of the world.
///   2. Cesium loads coarse tiles first and refines them. Landing on the coarse
///      surface means the ground shifts underneath you a second later, which
///      reads as the car dropping from the sky.
///
/// So it waits for the measured ground height to stop changing before it
/// places the car, and it places the car's *underside* on the surface rather
/// than guessing an offset from the pivot.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarGroundSpawner : MonoBehaviour
{
    [Header("Ground probe")]
    [Tooltip("How far above the spawn point the probe ray starts.")]
    public float probeHeight = 500f;

    [Tooltip("How far down the probe ray reaches.")]
    public float probeDistance = 2000f;

    [Tooltip("Gap left between the car's underside and the ground when it seats.")]
    public float clearance = 0.05f;

    [Header("Waiting for tiles to settle")]
    [Tooltip("Ground height must stay within this many metres to count as settled.")]
    public float settleTolerance = 0.25f;

    [Tooltip("How long the ground height must hold steady before the car is seated.")]
    public float settleTime = 0.5f;

    [Tooltip("Seconds to wait for terrain before giving up. 0 waits forever.")]
    public float timeout = 45f;

    [Header("Recovery")]
    [Tooltip("Press this to return to the last solid ground.")]
    public KeyCode respawnKey = KeyCode.R;

    [Tooltip("Auto-respawn if the car ends up this far below where it seated.")]
    public float fallLimit = 300f;

    Rigidbody rb;
    Collider col;
    Vector3 anchor;              // world XZ we keep probing beneath
    Vector3 lastSafePoint;
    bool seated;
    float waited;

    float lastGroundY;
    float steadyFor;
    bool hasReading;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();

        anchor = transform.position;
        lastSafePoint = anchor;

        // Freeze completely until there is something solid and settled to sit on.
        rb.isKinematic = true;
        Debug.Log("[CarGroundSpawner] Waiting for terrain to stream in and settle...");
    }

    void Update()
    {
        if (!seated)
        {
            WaitForGround();
            return;
        }

        bool respawnPressed = Input.GetKeyDown(respawnKey);
#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonEast.wasPressedThisFrame) respawnPressed = true;  // Circle
#endif

        if (respawnPressed)
        {
            Respawn();
            return;
        }

        if (transform.position.y < lastSafePoint.y - fallLimit)
        {
            Debug.LogWarning("[CarGroundSpawner] Fell through the world — respawning.");
            Respawn();
        }
    }

    void WaitForGround()
    {
        Vector3 origin = new Vector3(anchor.x, anchor.y + probeHeight, anchor.z);

        if (TryHitGround(origin, out RaycastHit hit))
        {
            float y = hit.point.y;

            if (hasReading && Mathf.Abs(y - lastGroundY) <= settleTolerance)
            {
                steadyFor += Time.deltaTime;
            }
            else
            {
                // Ground moved — a finer tile arrived. Start the timer again.
                steadyFor = 0f;
            }

            lastGroundY = y;
            hasReading = true;

            if (steadyFor >= settleTime)
            {
                Seat(hit.point);
                return;
            }
        }
        else
        {
            steadyFor = 0f;
            hasReading = false;
        }

        if (timeout > 0f)
        {
            waited += Time.deltaTime;
            if (waited >= timeout)
            {
                seated = true;
                rb.isKinematic = false;
                Debug.LogWarning(
                    "[CarGroundSpawner] No stable ground after " + timeout + "s. Releasing anyway.\n" +
                    "Check that your Cesium3DTileset has Create Physics Meshes enabled, that tiles " +
                    "load at this location, and that your camera is tagged MainCamera.");
            }
        }
    }

    /// <summary>Raycast downward, ignoring the car's own colliders and any triggers.</summary>
    bool TryHitGround(Vector3 origin, out RaycastHit best)
    {
        best = default;
        RaycastHit[] hits = Physics.RaycastAll(
            origin, Vector3.down, probeDistance, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        float nearest = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform)) continue;

            if (hits[i].distance < nearest)
            {
                nearest = hits[i].distance;
                best = hits[i];
                found = true;
            }
        }

        return found;
    }

    /// <summary>Places the car's underside on the surface, level, then releases physics.</summary>
    void Seat(Vector3 groundPoint)
    {
        // Level the car first so the collider bounds are measured upright.
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        float halfHeight = col != null ? col.bounds.extents.y : 0.75f;
        float pivotOffset = col != null ? transform.position.y - col.bounds.center.y : 0f;

        Vector3 seatedPosition = new Vector3(
            groundPoint.x,
            groundPoint.y + halfHeight + pivotOffset + clearance,
            groundPoint.z);

        transform.position = seatedPosition;
        lastSafePoint = seatedPosition;

        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        seated = true;

        Debug.Log("[CarGroundSpawner] Seated on ground at " + groundPoint.y.ToString("F1") +
                  "m. Drive with W/S, steer with A/D, Space to brake, R to respawn.");
    }

    public void Respawn()
    {
        transform.position = lastSafePoint;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Re-probe from here in case the terrain has streamed differently.
        anchor = lastSafePoint;
        seated = false;
        waited = 0f;
        steadyFor = 0f;
        hasReading = false;
        rb.isKinematic = true;
    }
}
