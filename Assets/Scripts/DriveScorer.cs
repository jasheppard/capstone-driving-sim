using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Watches a drive and turns it into evidence about the driver.
///
/// This used to produce a 0 to 100 safety score of our own invention. It behaved
/// sensibly, but it could not be compared to anything outside this project, which
/// made it useless for the part of the coursework that argues about real pass rates.
/// The score is gone. What is left is the detection work: this class finds the
/// moments worth judging, and NcRubric decides what they mean against the nine
/// elements of the North Carolina road skills test.
///
/// WHERE THE THRESHOLDS COME FROM
///
/// The event definitions follow the conventions used by insurance telematics, which
/// score drivers on harsh braking, harsh acceleration and harsh cornering. Those
/// systems commonly treat about 0.3 g as the boundary between normal and harsh for
/// a car.
///
/// This vehicle is a bus, so the cornering threshold is set lower: it can only hold
/// about 0.35 g before lifting a wheel, so 0.25 g is genuinely aggressive here even
/// though it would be unremarkable in a saloon.
///
/// WHAT COUNTS AS AN OPPORTUNITY
///
/// Every element is scored as failures over chances, and the chances have to be real
/// ones. A brake application is a chance at stopping smoothly. A pull away from rest
/// is a chance at starting smoothly. A turn is a chance at both turning and at
/// signalling. Ten seconds of driving is a chance at holding a safe speed. Counting
/// them this way means a long drive and a short one produce comparable results,
/// which flat event totals never did: on real recorded data the old flat model gave
/// a 196 second drive a 0 and an 11 second one a 90.
/// </summary>
public class DriveScorer : MonoBehaviour
{
    [Header("What counts as unsafe")]
    [Tooltip("Campus speed limit, mph. Overwritten per segment by the route.")]
    public float speedLimitMph = 45f;

    [Tooltip("How far over the limit is tolerated before it counts against you, mph. " +
             "Real examiners do not mark a driver down for drifting a couple over; " +
             "they mark down speed that is unsafe for the conditions. This is where " +
             "that line sits.")]
    public float speedToleranceMph = 5f;

    /// <summary>
    /// The speed at which you actually start losing the element.
    ///
    /// Kept as one place both the scorer and the briefing read, so the screen can
    /// never promise a tolerance the scoring does not give.
    /// </summary>
    public float ToleratedSpeedMph { get { return speedLimitMph + speedToleranceMph; } }

    [Tooltip("Deceleration counted as harsh braking, in g. Telematics systems " +
             "typically use around 0.30 for cars.")]
    public float hardBrakingG = 0.30f;

    [Tooltip("Acceleration counted as a harsh start, in g. A 13 tonne bus cannot " +
             "reach much more than this, so it sits deliberately near the limit of " +
             "what the vehicle can actually do.")]
    public float harshAccelG = 0.22f;

    [Tooltip("Lateral load counted as harsh cornering, in g. Lower than a car's " +
             "because a bus rolls at about 0.35.")]
    public float hardCorneringG = 0.25f;

    [Tooltip("How close you can pass a pedestrian before it counts as a near miss, " +
             "in metres.")]
    public float nearMissDistance = 2.5f;

    [Tooltip("Below this speed, passing close to someone is not a near miss: it " +
             "is just parking near them.")]
    public float nearMissMinSpeedMph = 8f;

    [Tooltip("Impact speed counted as a collision, m/s. Scanned terrain produces " +
             "constant low-speed contact that is not a crash.")]
    public float collisionSpeedThreshold = 4f;

    [Header("Turns")]
    [Tooltip("Total heading change that makes a manoeuvre count as a turn rather " +
             "than a curve in the road, in degrees.")]
    public float turnDegrees = 35f;

    [Tooltip("Seconds of going straight that closes a turn.")]
    public float turnEndSeconds = 1f;

    [Header("Safe speed")]
    [Tooltip("Length of the window used to score safe speed, in seconds. Each " +
             "window is one chance to hold the limit.")]
    public float speedWindowSeconds = 10f;

    [Header("Calibration")]
    [Tooltip("Derive the harsh-event thresholds from what this vehicle can actually " +
             "do, instead of using the numbers typed above. Leave this on: fixed " +
             "thresholds silently stop meaning anything the moment the vehicle is " +
             "retuned.")]
    public bool calibrateToVehicle = true;

    [Tooltip("Fraction of the vehicle's own limit that counts as aggressive. At 0.7, " +
             "a vehicle that can hold 0.45 g through a corner is flagged above " +
             "0.32 g: firm driving, not a slide.")]
    [Range(0.4f, 0.95f)] public float aggressiveFraction = 0.7f;

    [Header("What to score")]
    [Tooltip("Score how gently each stop was made, as part of the NCDOT Stopping " +
             "element. Off, a hard brake is still measured, still written to the " +
             "telemetry CSV, and still counted in the drive report; it just is not " +
             "a violation against the rubric and does not flash on the HUD.\n\n" +
             "Off by default. Smoothness is the one thing in here a driver cannot " +
             "practise honestly on photogrammetry terrain: the scan has kerbs, " +
             "speed humps and mesh seams that are not really there, the ground ray " +
             "steps between tiles, and braking firmly to avoid a pedestrian who " +
             "walked out is correct driving that this would mark down. Whether the " +
             "bus stopped at all is still scored, by stop signs, signals and bus " +
             "stops, and those are the parts the road test actually fails you on.")]
    public bool scoreStopSmoothness = false;

    [Header("Event debouncing")]
    [Tooltip("Seconds before the same kind of event can be counted again. One long " +
             "brake application is one event, not fifty.")]
    public float eventCooldown = 1.5f;

    [Header("Output")]
    [Tooltip("Write a plain-text report beside the telemetry CSV when the drive ends.")]
    public bool saveReport = true;

    public string outputFolder = "TelemetryData";

    // ------------------------------------------------------------------ results

    public float DurationSeconds { get; private set; }
    public float DistanceMetres { get; private set; }
    public float TopSpeedMph { get; private set; }
    public float MeanSpeedMph { get; private set; }
    public float SecondsOverLimit { get; private set; }
    public float PeakLateralG { get; private set; }
    public float HardestBrakingG { get; private set; }
    public float ClosestPedestrianM { get; private set; }

    public int Collisions { get; private set; }
    public int NearMisses { get; private set; }
    public int HardBrakes { get; private set; }
    public int HardCorners { get; private set; }

    /// <summary>Chances at each element, kept for the report's own bookkeeping.</summary>
    public int BrakeApplications { get; private set; }
    public int Launches { get; private set; }
    public int Turns { get; private set; }
    public int UnsignalledTurns { get; private set; }

    /// <summary>
    /// True while the drive is still being scored, false once it has ended.
    /// Read by the on-screen timer so the clock stops with the drive rather than
    /// running on behind the debrief.
    /// </summary>
    public bool DriveRunning { get { return running; } }

    public float BrakesPerMinute { get { return HardBrakes / Mathf.Max(DurationSeconds / 60f, 1f / 6f); } }
    public float CornersPerMinute { get { return HardCorners / Mathf.Max(DurationSeconds / 60f, 1f / 6f); } }
    public float PercentOverLimit { get { return DurationSeconds > 0f ? 100f * SecondsOverLimit / DurationSeconds : 0f; } }

    // ---------------------------------------------------------------- internals

    Rigidbody body;
    VehicleController vehicle;
    NcRubric rubric;

    float previousSpeed;
    float previousHeading;
    Vector3 previousPosition;

    float movingSampleTotal;
    int movingSampleCount;

    float lastBrakeEvent = -999f;
    float lastCornerEvent = -999f;
    float lastNearMiss = -999f;
    float lastCollision = -999f;

    // One brake application: from the first touch of the brake until the bus either
    // stops or comes off the brake again.
    bool braking;
    bool brakingWasHarsh;

    // One pull away: from stationary until properly moving.
    bool stationary = true;
    bool launchWasHarsh;

    // One turn: heading accumulating in one direction, closed by going straight.
    float turnAccumulated;
    float straightFor;
    bool turning;
    bool turnWasHarsh;
    bool turnWasSignalled;

    float speedWindowElapsed;
    bool exceededThisWindow;

    readonly List<Transform> pedestrians = new List<Transform>();
    float nextPedestrianRefresh;

    bool running = true;

    void Start()
    {
        body = GetComponent<Rigidbody>();
        vehicle = GetComponent<VehicleController>();
        rubric = GetComponent<NcRubric>();

        if (rubric == null)
            Debug.LogWarning("[DriveScorer] No NcRubric on this object, so nothing " +
                             "will be graded. Run NCAT > Set Up Road Test.");

        Calibrate();

        previousPosition = transform.position;
        previousHeading = transform.eulerAngles.y;

        ClosestPedestrianM = 999f;
    }

    /// <summary>
    /// Scales the harsh-event thresholds to what this vehicle can actually do.
    ///
    /// WHY FIXED THRESHOLDS BROKE
    ///
    /// They were set for a 13 tonne transit bus: 0.25 g cornering, because that bus
    /// lifts a wheel at 0.35. When the vehicle was retuned to a lighter shuttle that
    /// holds 0.45 g, the threshold stayed at 0.25 and the vehicle's ordinary
    /// cornering limit ended up nearly double the figure that flags a fault. A real
    /// recorded drive produced 23 harsh-cornering events in 87 seconds, about
    /// 15 per minute, where a real driver produces one to three. None of that was
    /// bad driving. It was a threshold left behind by a vehicle change.
    ///
    /// So the thresholds are derived rather than typed. "Aggressive" means using most
    /// of what the vehicle has, which is what the phrase means to a person and is the
    /// only definition that survives the vehicle being changed again.
    ///
    /// THE FLOOR
    ///
    /// Never more lenient than the insurance-telematics convention of about 0.3 g
    /// braking and 0.25 g cornering. Those figures describe what is uncomfortable for
    /// the people inside, which does not get easier just because the vehicle is
    /// capable, so they stand as a minimum.
    /// </summary>
    void Calibrate()
    {
        if (!calibrateToVehicle || vehicle == null) return;

        float corneringLimit = vehicle.maxLateralG;
        float brakingLimit = vehicle.brakeDeceleration / 9.81f;

        // What the tyres can put down before they slip, in g. The same figure the
        // vehicle uses to cap its own acceleration.
        float tractionLimit = vehicle.tyreFriction * vehicle.driveAxleLoad;

        float corner = Mathf.Max(0.25f, corneringLimit * aggressiveFraction);
        float brake = Mathf.Max(0.30f, brakingLimit * aggressiveFraction);
        float accel = Mathf.Max(0.15f, tractionLimit * aggressiveFraction);

        Debug.Log(string.Format(
            "[DriveScorer] Thresholds calibrated to this vehicle:\n" +
            "  cornering {0:F2} g -> {1:F2} g   (vehicle holds {2:F2} g)\n" +
            "  braking   {3:F2} g -> {4:F2} g   (vehicle stops at {5:F2} g)\n" +
            "  starting  {6:F2} g -> {7:F2} g   (tyres grip to {8:F2} g)",
            hardCorneringG, corner, corneringLimit,
            hardBrakingG, brake, brakingLimit,
            harshAccelG, accel, tractionLimit));

        hardCorneringG = corner;
        hardBrakingG = brake;
        harshAccelG = accel;
    }

    void FixedUpdate()
    {
        if (!running) return;

        float dt = Time.fixedDeltaTime;
        DurationSeconds += dt;

        // Let the vehicle settle before judging anything. CarGroundSpawner is still
        // seating it during the first moments, and that is not driving.
        if (DurationSeconds < 3f)
        {
            previousPosition = transform.position;
            previousHeading = transform.eulerAngles.y;
            previousSpeed = 0f;
            return;
        }

        Vector3 velocity = body.velocity;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

        float speedMs = flat.magnitude;
        float speedMph = speedMs * 2.23694f;

        // ---- distance and speed ------------------------------------------------
        DistanceMetres += Vector3.Distance(
            new Vector3(previousPosition.x, 0f, previousPosition.z),
            new Vector3(transform.position.x, 0f, transform.position.z));

        if (speedMph > TopSpeedMph) TopSpeedMph = speedMph;

        if (speedMph > 1f)
        {
            movingSampleTotal += speedMph;
            movingSampleCount++;
        }

        // Measured against the tolerated speed, not the sign, so "percent over the
        // limit" in the report means percent of the drive spent genuinely too fast
        // rather than percent spent a mile or two over.
        if (speedMph > ToleratedSpeedMph)
        {
            SecondsOverLimit += dt;
            exceededThisWindow = true;
        }

        float longitudinalAccel = (speedMs - previousSpeed) / dt;
        float brakingG = -longitudinalAccel / 9.81f;             // positive when slowing
        float accelG = longitudinalAccel / 9.81f;

        JudgeSafeSpeed(dt);
        JudgeBraking(brakingG, speedMph);
        JudgeStarting(accelG, speedMph);

        // ---- cornering, and the turn it may belong to ---------------------------
        float heading = transform.eulerAngles.y;
        float headingChange = Mathf.DeltaAngle(previousHeading, heading);
        float yawRate = headingChange / dt;
        float lateralG = Mathf.Abs(speedMs * yawRate * Mathf.Deg2Rad) / 9.81f;

        bool harshCorner = false;

        // A physically impossible value is a collision spinning the body, not
        // cornering. Counting it would punish the player for being hit.
        if (lateralG < 1.5f)
        {
            if (lateralG > PeakLateralG) PeakLateralG = lateralG;

            if (lateralG > hardCorneringG && DurationSeconds - lastCornerEvent > eventCooldown)
            {
                lastCornerEvent = DurationSeconds;
                HardCorners++;
                harshCorner = true;
            }
        }

        JudgeTurn(headingChange, speedMph, harshCorner, dt);

        CheckPedestrians(speedMph);

        previousSpeed = speedMs;
        previousHeading = heading;
        previousPosition = transform.position;
    }

    // -------------------------------------------------------------- the elements

    /// <summary>
    /// Safe speed for conditions, scored in fixed windows.
    ///
    /// A continuous quantity has no natural moment to judge, so the drive is cut
    /// into equal windows and each one asks the same yes or no question: was the
    /// limit exceeded at any point during it. That gives the element real
    /// opportunities to divide by, and it makes a driver who speeds once for a
    /// second score very differently from one who speeds throughout.
    /// </summary>
    void JudgeSafeSpeed(float dt)
    {
        speedWindowElapsed += dt;
        if (speedWindowElapsed < speedWindowSeconds) return;

        Report(NcElement.SafeSpeed, exceededThisWindow,
               "Over " + ToleratedSpeedMph.ToString("F0") + " mph");

        speedWindowElapsed = 0f;
        exceededThisWindow = false;
    }

    /// <summary>
    /// Stopping, scored per brake application rather than per frame.
    ///
    /// The application opens on the first meaningful deceleration and closes when the
    /// bus is at rest or has come off the brake. Anything harsh at any point inside
    /// it fails the whole application, which is right: one violent grab in the middle
    /// of an otherwise gentle stop is still a bad stop.
    /// </summary>
    void JudgeBraking(float brakingG, float speedMph)
    {
        // A sudden stop against a wall is a collision, not braking, and it shows up
        // here as an impossible figure. Real recorded data produced a reading of
        // 13.97 g. No road vehicle exceeds about 1.2 g.
        if (brakingG >= 2f) return;

        if (brakingG > HardestBrakingG) HardestBrakingG = brakingG;

        bool harsh = brakingG > hardBrakingG;

        if (harsh && DurationSeconds - lastBrakeEvent > eventCooldown)
        {
            lastBrakeEvent = DurationSeconds;
            HardBrakes++;
        }

        if (brakingG > 0.05f && speedMph > 2f)
        {
            if (!braking) { braking = true; brakingWasHarsh = false; }
            if (harsh) brakingWasHarsh = true;
            return;
        }

        // Off the brake, or stopped. Close the application if one was open.
        if (!braking) return;

        braking = false;
        BrakeApplications++;

        // The application is counted either way, so the drive report and the
        // telemetry still describe how it was braked. Only the grade is skipped.
        if (scoreStopSmoothness)
            Report(NcElement.Stopping, brakingWasHarsh, "Harsh stop");
    }

    /// <summary>
    /// Starting, scored per pull away from rest.
    ///
    /// Only a genuine standing start counts. Easing back onto the throttle after
    /// lifting off is not a start, and counting it would flood this element with
    /// opportunities that ask nothing of the driver.
    /// </summary>
    void JudgeStarting(float accelG, float speedMph)
    {
        if (stationary)
        {
            if (accelG > harshAccelG) launchWasHarsh = true;

            if (speedMph > 5f)
            {
                stationary = false;
                Launches++;
                Report(NcElement.Starting, launchWasHarsh, "Pulled away harshly");
            }

            return;
        }

        if (speedMph < 1f)
        {
            stationary = true;
            launchWasHarsh = false;
        }
    }

    /// <summary>
    /// Turning, and the signal that should have preceded it.
    ///
    /// Heading change is accumulated while the bus is moving and reset by a spell of
    /// going straight, which is what separates a junction turn from following a bend
    /// in the road. Whether the indicator was on is sampled throughout rather than at
    /// the end, because a driver who signals and then cancels mid-turn has signalled.
    /// </summary>
    void JudgeTurn(float headingChange, float speedMph, bool harshCorner, float dt)
    {
        if (speedMph < 3f) return;

        if (Mathf.Abs(headingChange) > 0.05f)
        {
            turnAccumulated += headingChange;
            straightFor = 0f;

            if (harshCorner) turnWasHarsh = true;
            if (speedMph > ToleratedSpeedMph) turnWasHarsh = true;
            if (vehicle != null && vehicle.IndicatorOn) turnWasSignalled = true;

            if (Mathf.Abs(turnAccumulated) >= turnDegrees) turning = true;
            return;
        }

        straightFor += dt;
        if (straightFor < turnEndSeconds) return;

        if (turning)
        {
            Turns++;
            Report(NcElement.Turning, turnWasHarsh, "Turned too fast");
            Report(NcElement.SignalsAndHorn, !turnWasSignalled, "Turned without signalling");
            if (!turnWasSignalled) UnsignalledTurns++;
        }

        turning = false;
        turnAccumulated = 0f;
        turnWasHarsh = false;
        turnWasSignalled = false;
    }

    void CheckPedestrians(float speedMph)
    {
        if (Time.time >= nextPedestrianRefresh)
        {
            RefreshPedestrians();
            nextPedestrianRefresh = Time.time + 2f;
        }

        Vector3 here = transform.position;

        for (int i = 0; i < pedestrians.Count; i++)
        {
            Transform ped = pedestrians[i];
            if (ped == null) continue;

            float distance = Vector3.Distance(here, ped.position);

            if (distance < ClosestPedestrianM) ClosestPedestrianM = distance;

            bool tooClose = distance < nearMissDistance;
            bool movingFast = speedMph > nearMissMinSpeedMph;
            bool offCooldown = DurationSeconds - lastNearMiss > eventCooldown;

            if (tooClose && movingFast && offCooldown)
            {
                lastNearMiss = DurationSeconds;
                NearMisses++;

                // A near miss is a failure to yield with nothing to offset it, so it
                // is recorded as an opportunity that was failed rather than folded
                // into the crossing count. Passing that close to someone at speed is
                // not made acceptable by having yielded properly elsewhere.
                Report(NcElement.YieldToPedestrians, true, "Near miss with a pedestrian");
            }
        }
    }

    /// <summary>Found by component name so this does not hard-depend on the pedestrian script.</summary>
    void RefreshPedestrians()
    {
        pedestrians.Clear();

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            if (mb == null || !RouteRunner.IsPedestrian(mb)) continue;
            pedestrians.Add(mb.transform);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!running || DurationSeconds < 3f) return;
        if (collision.relativeVelocity.magnitude < collisionSpeedThreshold) return;
        if (DurationSeconds - lastCollision < eventCooldown) return;

        lastCollision = DurationSeconds;
        Collisions++;

        if (rubric == null) return;

        // A real examiner ends the road test for a collision, and no amount of
        // correct driving beforehand earns it back. Found by component name so this
        // keeps working if the pedestrian script is ever renamed or replaced.
        bool hitSomeone = false;

        foreach (MonoBehaviour mb in collision.gameObject.GetComponentsInParent<MonoBehaviour>())
        {
            if (mb != null && RouteRunner.IsPedestrian(mb)) hitSomeone = true;
        }

        if (hitSomeone)
        {
            rubric.RegisterCritical(NcElement.YieldToPedestrians, "Struck a pedestrian");
            rubric.RegisterAutomaticFail("struck a pedestrian");
        }
        else
        {
            rubric.RegisterAutomaticFail("collision");
        }
    }

    void Report(NcElement element, bool violated, string detail)
    {
        if (rubric != null) rubric.Register(element, violated, detail);
    }

    // ----------------------------------------------------------------- finishing

    /// <summary>
    /// Freezes the statistics and hands the last readings to the rubric.
    /// Called by the debrief screen when the player ends the drive.
    /// </summary>
    public void FinishDrive()
    {
        if (!running) return;
        running = false;

        MeanSpeedMph = movingSampleCount > 0 ? movingSampleTotal / movingSampleCount : 0f;
        if (ClosestPedestrianM > 900f) ClosestPedestrianM = -1f;      // none encountered

        // A part-finished window still says something about the drive, so it is
        // counted rather than thrown away. Without this, a drive shorter than one
        // window would leave safe speed unassessed entirely.
        if (speedWindowElapsed > speedWindowSeconds * 0.5f)
            Report(NcElement.SafeSpeed, exceededThisWindow, "Over the limit");

        // An open brake application at the moment the drive ends was still a stop.
        if (braking && scoreStopSmoothness)
            Report(NcElement.Stopping, brakingWasHarsh, "Harsh stop");

        // Stopping the logger here flushes its CSV, so the written data and this
        // report describe exactly the same drive.
        DrivingTelemetryLogger logger = GetComponent<DrivingTelemetryLogger>();
        if (logger != null) logger.enabled = false;

        if (rubric != null) rubric.Finish();
        if (saveReport) WriteReport();
    }

    void WriteReport()
    {
        try
        {
            // Per driver, so one person's runs are not mixed in with another's.
            string root = Path.GetDirectoryName(Application.dataPath);
            string folder = Path.Combine(Path.Combine(root, outputFolder),
                                         DriverIdentity.FileSafeName);
            Directory.CreateDirectory(folder);

            string stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(folder, "drive_report_" +
                                       DriverIdentity.FileSafeName + "_" + stamp + ".txt");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NC A&T Campus Driving Safety Simulator: Road Skills Test");
            sb.AppendLine("Driver: " + DriverIdentity.Name);
            sb.AppendLine("Generated " + System.DateTime.Now.ToString("f"));
            sb.AppendLine(new string('-', 62));
            sb.AppendLine();

            if (rubric != null)
            {
                sb.AppendLine("RESULT   " + rubric.Stamp + "   " +
                              rubric.OverallProficiency.ToString("F0") + "%   (" +
                              rubric.PassedCount + " of " + rubric.AssessedCount +
                              " elements passed, " + rubric.AssessedCount + " of 9 assessed)");
                sb.AppendLine(rubric.Verdict);
                sb.AppendLine();
                sb.AppendLine("The nine elements");

                foreach (NcElement element in System.Enum.GetValues(typeof(NcElement)))
                {
                    ElementResult r = rubric.Result(element);

                    string state = !r.Assessed ? "not assessed"
                                 : r.CriticalFailure ? "FAIL (critical)"
                                 : (r.Passed ? "pass" : "FAIL") + "  " +
                                   (r.Proficiency * 100f).ToString("F0").PadLeft(3) + "%  " +
                                   (r.Opportunities - r.Violations) + " of " + r.Opportunities;

                    sb.AppendLine("  " + NcRubric.Label(element).PadRight(28) + state);
                }

                sb.AppendLine();
            }

            sb.AppendLine("Drive");
            sb.AppendLine("  Duration              " + FormatTime(DurationSeconds));
            sb.AppendLine("  Distance              " + DistanceMetres.ToString("F0") + " m");
            sb.AppendLine("  Average speed         " + MeanSpeedMph.ToString("F1") + " mph");
            sb.AppendLine("  Top speed             " + TopSpeedMph.ToString("F1") + " mph");
            sb.AppendLine();
            sb.AppendLine("Events");
            sb.AppendLine("  Time over " + ToleratedSpeedMph + " mph      " +
                          SecondsOverLimit.ToString("F0") + " s  (" +
                          PercentOverLimit.ToString("F0") + "% of the drive)");
            sb.AppendLine("  Brake applications    " + BrakeApplications +
                          ", of which harsh " + HardBrakes +
                          "  (" + BrakesPerMinute.ToString("F1") + "/min)");
            sb.AppendLine("  Standing starts       " + Launches);
            sb.AppendLine("  Turns                 " + Turns +
                          ", unsignalled " + UnsignalledTurns);
            sb.AppendLine("  Harsh cornering       " + HardCorners +
                          "  (" + CornersPerMinute.ToString("F1") + "/min)");
            sb.AppendLine("  Pedestrian near miss  " + NearMisses);
            sb.AppendLine("  Collisions            " + Collisions);
            sb.AppendLine();
            sb.AppendLine("Peaks");
            sb.AppendLine("  Hardest braking       " + HardestBrakingG.ToString("F2") + " g");
            sb.AppendLine("  Peak cornering load   " + PeakLateralG.ToString("F2") + " g");
            sb.AppendLine("  Closest pedestrian    " +
                          (ClosestPedestrianM < 0f ? "none encountered"
                                                   : ClosestPedestrianM.ToString("F1") + " m"));
            sb.AppendLine();
            sb.AppendLine("Element thresholds: " + hardBrakingG + " g braking, " +
                          harshAccelG + " g starting, " + hardCorneringG + " g cornering, " +
                          nearMissDistance + " m near miss, " + speedWindowSeconds +
                          " s safe-speed window.");
            sb.AppendLine("The elements follow the NCDOT road skills list. Which driving " +
                          "behaviour counts as failing each one is a rubric we defined.");

            File.WriteAllText(path, sb.ToString());
            Debug.Log("[DriveScorer] Report saved to " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DriveScorer] Could not save the report: " + e.Message);
        }
    }

    public static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int remainder = Mathf.FloorToInt(seconds % 60f);
        return minutes + "m " + remainder.ToString("00") + "s";
    }
}
