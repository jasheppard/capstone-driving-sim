using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Physically-based driving controller, tuned for a transit shuttle bus.
///
/// The previous version applied a constant 15,000 N until a hard speed cap. Real
/// vehicles do not behave that way, and the difference is the whole point of a
/// driving-safety simulator: how long it takes to get going, and how far it takes
/// to stop.
///
/// WHAT ACTUALLY LIMITS ACCELERATION
///
/// Two ceilings, whichever is lower at the current speed:
///
///   Traction    F = mu * (weight on the driven axle). You cannot push harder
///               than the tyres can grip, no matter the engine.
///   Power       F = P / v. An engine makes roughly constant power, so the force
///               available falls as speed rises. This is why every vehicle
///               accelerates hardest from a standstill and tails off.
///
/// A bus crosses from traction-limited to power-limited at walking pace, so it is
/// power-limited almost the whole way — which is exactly why buses feel slow.
///
/// WHAT LIMITS TOP SPEED
///
///   Aerodynamic drag   F = 0.5 * rho * Cd*A * v^2, growing with the square of
///                      speed. A bus is a brick: roughly 5.6 m^2 of Cd*A against
///                      a sedan's 0.7.
///   Rolling resistance F = Crr * m * g, near-constant.
///
/// Left alone the model tops out near 78 mph, so a governor is applied — as on
/// real transit buses.
///
/// BRAKING
///
/// APTA's in-service brake standard asks for 0.6 g peak and 0.528 g average from
/// 20 mph; the federal floor in FMCSR 393.52 is 0.435 g. The default here is
/// 5.0 m/s^2, about 0.51 g, which is a well-maintained bus. That produces a real
/// stopping distance — around 60 ft from 25 mph before reaction time — and is the
/// number a safety study should be built on.
///
/// STEERING
///
/// A bicycle model: yaw rate = v * tan(steer) / wheelbase, capped so lateral
/// acceleration never exceeds what the vehicle can hold. A high-floor bus starts
/// lifting a wheel around 0.35 g, so that is the cap. The cap is what makes the
/// steering go heavy at speed without any artificial "reduce steering" fudge.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleController : MonoBehaviour
{
    [Header("Vehicle — 35 ft transit bus")]
    [Tooltip("Kerb weight plus a light passenger load, in kg. A 35-40 ft transit " +
             "bus is 12,000-15,000 kg; a sedan is about 1,500.")]
    public float massKg = 13000f;

    [Tooltip("Power at the wheels in kW, after drivetrain losses. A Cummins B6.7 " +
             "at 280 hp is 209 kW at the crank, roughly 180 at the wheels.")]
    public float enginePowerKW = 180f;

    [Tooltip("Drag coefficient times frontal area, in m^2. A bus is about 5.6; " +
             "a saloon car about 0.7. This is what sets top speed.")]
    public float dragArea = 5.6f;

    [Tooltip("Rolling resistance coefficient. 0.008 for truck tyres on tarmac.")]
    public float rollingResistance = 0.008f;

    [Tooltip("Tyre-road friction. 0.8 is dry tarmac; drop toward 0.4 for wet.")]
    public float tyreFriction = 0.8f;

    [Tooltip("Fraction of the vehicle's weight over the driven axle. A rear-drive " +
             "transit bus carries roughly 60% at the back.")]
    [Range(0.2f, 1f)] public float driveAxleLoad = 0.6f;

    [Tooltip("Governed top speed in mph. Transit buses are limited well below what " +
             "the engine could manage — without this it would reach about 78.")]
    public float governedTopSpeedMph = 65f;

    [Tooltip("Top speed in reverse, mph.")]
    public float maxReverseSpeedMph = 12f;

    [Header("Braking")]
    [Tooltip("Service brake deceleration in m/s^2. 5.0 is about 0.51 g, matching " +
             "APTA's 0.528 g in-service standard for transit buses. A car manages " +
             "roughly 9.")]
    public float brakeDeceleration = 5f;

    [Tooltip("Deceleration when you lift off the throttle entirely, in m/s^2. " +
             "Diesel engine braking is significant on a heavy vehicle.")]
    public float engineBraking = 0.6f;

    [Header("Steering")]
    [Tooltip("Axle-to-axle distance in metres. About 7 m on a 35 ft bus.")]
    public float wheelbase = 7f;

    [Tooltip("Maximum angle of the front wheels, degrees. 35 gives roughly a 10 m " +
             "turning radius, which is what a bus of this size actually manages.")]
    public float maxSteerAngle = 35f;

    [Tooltip("How fast the wheels can be turned lock to lock, degrees per second. " +
             "Stops the bus darting on a flick of the stick.")]
    public float steerRate = 90f;

    [Tooltip("Cornering limit in g. A high-floor bus starts lifting an inside " +
             "wheel around 0.35; a sports car holds about 1.0.")]
    public float maxLateralG = 0.35f;

    [Tooltip("Below this speed (mph) the wheels turn but the vehicle does not, " +
             "as when stationary.")]
    public float steerCutoffSpeed = 0.5f;

    [Header("Orientation")]
    [Tooltip("Tick this if W drives the vehicle backwards — flips which way the " +
             "body counts as forward.")]
    public bool invertForward = false;

    [Header("Grip")]
    [Tooltip("How strongly sideways sliding is cancelled. 1 = glued to its heading, 0 = ice.")]
    [Range(0f, 1f)] public float lateralGrip = 0.9f;

    [Tooltip("Cancels leftover spin when you are not steering, so it holds a straight line.")]
    [Range(0f, 1f)] public float yawDamping = 0.85f;

    [Header("Stability")]
    [Tooltip("Locks the vehicle upright. Strongly recommended on photogrammetry " +
             "terrain — without it a kerb or a mesh seam will flip you.")]
    public bool keepUpright = true;

    [Tooltip("Extra downward acceleration to keep it planted. Keep this low — " +
             "pressing a box hard into scanned terrain makes it snag on every bump.")]
    public float groundStick = 5f;

    [Tooltip("Height of the centre of mass in local space. Lower is more stable.")]
    public float centreOfMassHeight = -0.3f;

    [Tooltip("Set the Rigidbody's mass from Mass Kg on start, so the physics and " +
             "the numbers above agree. Untick if you are setting mass by hand.")]
    public bool overrideRigidbodyMass = true;

    [Header("Ground Check")]
    public float groundCheckDistance = 1.2f;

    [Header("Gamepad")]
    [Tooltip("Ignore trigger travel below this.")]
    public float triggerDeadzone = 0.05f;

    [Tooltip("Ignore stick movement below this.")]
    public float stickDeadzone = 0.15f;

    const float Gravity = 9.81f;
    const float AirDensity = 1.225f;
    const float MsToMph = 2.23694f;

    Rigidbody rb;
    bool isGrounded;
    float currentSteerAngle;        // degrees, eased toward the input

    /// <summary>Which way the vehicle counts as pointing, honouring the invert toggle.</summary>
    Vector3 Nose { get { return invertForward ? -transform.forward : transform.forward; } }
    Vector3 Side { get { return invertForward ? -transform.right : transform.right; } }

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (overrideRigidbodyMass) rb.mass = massKg;

        // Drag and rolling resistance are modelled explicitly below, so Unity's
        // own damping has to be off or it would be counted twice.
        rb.drag = 0f;
        rb.angularDrag = 5f;
        rb.centerOfMass = new Vector3(0f, centreOfMassHeight, 0f);

        ApplyUprightConstraint();
    }

    void OnValidate()
    {
        if (Application.isPlaying && rb != null) ApplyUprightConstraint();
    }

    void ApplyUprightConstraint()
    {
        if (rb == null) return;

        if (keepUpright)
            rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        else
            rb.constraints &= ~(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        float throttle, steerInput;
        bool braking;
        ReadInput(out throttle, out steerInput, out braking);

        isGrounded = CheckGrounded();

        // Signed speed along the vehicle's own axis. Negative means reversing.
        float forwardSpeed = Vector3.Dot(rb.velocity, Nose);

        if (isGrounded)
        {
            forwardSpeed = UpdateLongitudinal(forwardSpeed, throttle, braking, dt);
            UpdateSteering(forwardSpeed, steerInput, dt);
            ApplyVelocity(forwardSpeed);

            rb.AddForce(Vector3.down * groundStick, ForceMode.Acceleration);
        }

        if (Mathf.Abs(steerInput) <= 0.01f)
        {
            // Bleed off spin picked up from bumps, or it slowly rotates on its own.
            Vector3 av = rb.angularVelocity;
            av.y = Mathf.Lerp(av.y, 0f, yawDamping);
            rb.angularVelocity = av;
        }
    }

    // ------------------------------------------------------------------ input

    void ReadInput(out float throttle, out float steer, out bool braking)
    {
        throttle = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) throttle += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) throttle -= 1f;

        steer = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) steer += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) steer -= 1f;

        braking = Input.GetKey(KeyCode.Space);

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            float padThrottle = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            if (Mathf.Abs(padThrottle) > triggerDeadzone) throttle = padThrottle;

            float padSteer = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(padSteer) > stickDeadzone) steer = padSteer;

            if (pad.buttonSouth.isPressed) braking = true;      // Cross
        }
#endif
    }

    // ----------------------------------------------------------- longitudinal

    /// <summary>
    /// Returns the new forward speed in m/s.
    ///
    /// Drive and resistance are kept separate on purpose. Drive may reverse the
    /// direction of travel; resistance never may — friction brings a vehicle to a
    /// stop, it does not then push it backwards. Applying them as one number is
    /// how simulations end up with cars that creep backwards at a standstill.
    /// </summary>
    float UpdateLongitudinal(float v, float throttle, bool braking, float dt)
    {
        float speed = Mathf.Abs(v);
        float mph = v * MsToMph;

        float driveAccel = 0f;      // signed
        float resistAccel = 0f;     // magnitude, always opposes motion

        // ---- always present ---------------------------------------------------
        float dragForce = 0.5f * AirDensity * dragArea * speed * speed;
        float rollForce = speed > 0.05f ? rollingResistance * massKg * Gravity : 0f;
        resistAccel += (dragForce + rollForce) / massKg;

        // ---- engine -----------------------------------------------------------
        if (throttle > 0.01f)
        {
            if (mph < governedTopSpeedMph)
                driveAccel += MaxDriveAcceleration(speed) * throttle;
        }
        else if (throttle < -0.01f)
        {
            if (v > 0.5f)
            {
                // Pulling back while still rolling forward is a brake request,
                // not a demand to slam into reverse.
                resistAccel += brakeDeceleration * -throttle;
            }
            else if (mph > -maxReverseSpeedMph)
            {
                // Reverse gear is short: plenty of force, very little speed.
                driveAccel -= MaxDriveAcceleration(speed) * -throttle;
            }
        }
        else
        {
            resistAccel += engineBraking;
        }

        if (braking) resistAccel += brakeDeceleration;

        // ---- integrate --------------------------------------------------------
        v += driveAccel * dt;

        float resistDv = resistAccel * dt;
        if (Mathf.Abs(v) <= resistDv) v = 0f;                    // stop, never reverse
        else v -= Mathf.Sign(v) * resistDv;

        return v;
    }

    /// <summary>
    /// The lower of the traction ceiling and the power ceiling, as acceleration.
    ///
    /// At a standstill the power ceiling is infinite (F = P/v), so traction wins;
    /// the two cross over at v = P / F_traction, which for these bus numbers is
    /// under 7 mph. Above that the engine, not the tyres, is the limit.
    /// </summary>
    float MaxDriveAcceleration(float speed)
    {
        float tractionForce = tyreFriction * driveAxleLoad * massKg * Gravity;

        float powerForce = speed > 0.5f
            ? enginePowerKW * 1000f / speed
            : float.MaxValue;

        return Mathf.Min(tractionForce, powerForce) / massKg;
    }

    // --------------------------------------------------------------- steering

    /// <summary>
    /// Bicycle model with a cornering-force limit.
    ///
    /// yaw rate = v * tan(steer) / wheelbase is the geometry of a vehicle rolling
    /// through a turn without sliding. Left alone it lets you pull absurd lateral
    /// loads at speed, so it is capped at whatever keeps v * omega under the
    /// vehicle's cornering limit. That cap is why this needs no artificial
    /// "reduce steering at speed" factor: the physics does it.
    /// </summary>
    void UpdateSteering(float forwardSpeed, float steerInput, float dt)
    {
        // The wheels take time to turn. Instant lock-to-lock is what made the old
        // version dart sideways on a stick flick.
        float targetAngle = steerInput * maxSteerAngle;
        currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, targetAngle, steerRate * dt);

        float speed = Mathf.Abs(forwardSpeed);
        if (speed * MsToMph < steerCutoffSpeed) return;

        float yawRate = speed * Mathf.Tan(currentSteerAngle * Mathf.Deg2Rad) / Mathf.Max(0.5f, wheelbase);

        float limit = (maxLateralG * Gravity) / Mathf.Max(speed, 0.1f);
        yawRate = Mathf.Clamp(yawRate, -limit, limit);

        // Reversing steers the opposite way, as it does in a real vehicle.
        if (forwardSpeed < 0f) yawRate = -yawRate;

        float yawDegrees = yawRate * Mathf.Rad2Deg * dt;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yawDegrees, 0f));
    }

    // ----------------------------------------------------------------- tyres

    /// <summary>
    /// Rebuilds the velocity from the longitudinal speed we just computed plus
    /// whatever sideways slip survives the grip setting.
    ///
    /// Splitting velocity into "along" and "across" and cancelling most of the
    /// across component is what stops a rotated Rigidbody sliding sideways like a
    /// hockey puck. Vertical speed is left untouched so gravity still works.
    /// </summary>
    void ApplyVelocity(float forwardSpeed)
    {
        Vector3 velocity = rb.velocity;
        float verticalSpeed = velocity.y;

        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

        Vector3 forward = Nose;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return;
        forward.Normalize();

        Vector3 right = Side;
        right.y = 0f;
        right.Normalize();

        float across = Vector3.Dot(flat, right) * (1f - lateralGrip);

        rb.velocity = forward * forwardSpeed + right * across + Vector3.up * verticalSpeed;
    }

    // ---------------------------------------------------------------- ground

    bool CheckGrounded()
    {
        Collider col = GetComponent<Collider>();
        float halfHeight = col != null ? col.bounds.extents.y : 0.75f;
        float distance = halfHeight + groundCheckDistance;

        RaycastHit[] hits = Physics.RaycastAll(
            transform.position, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform)) continue;
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------- API

    /// <summary>Absolute speed in mph. Read by the speedometer and the telemetry logger.</summary>
    public float GetSpeedMPH()
    {
        return rb != null ? rb.velocity.magnitude * MsToMph : 0f;
    }

    /// <summary>Signed speed in mph — negative while reversing.</summary>
    public float GetForwardSpeedMPH()
    {
        return rb != null ? Vector3.Dot(rb.velocity, Nose) * MsToMph : 0f;
    }

    public bool IsGrounded()
    {
        return isGrounded;
    }

    /// <summary>
    /// How far it would take to stop from the current speed, in metres, ignoring
    /// reaction time. Useful for a HUD readout or a safety-margin metric.
    /// </summary>
    public float StoppingDistance()
    {
        if (rb == null) return 0f;

        float v = rb.velocity.magnitude;
        return (v * v) / (2f * Mathf.Max(0.1f, brakeDeceleration));
    }
}
