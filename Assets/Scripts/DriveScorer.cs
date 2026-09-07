using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Watches a drive and scores it on safety.
///
/// The telemetry logger writes every sample to disk for later analysis. This does
/// the other half of the job: it keeps running totals in memory so the player can
/// be told, the moment they finish, how they actually drove. In a training
/// simulator the learning happens in the debrief, not during the drive.
///
/// WHERE THE THRESHOLDS COME FROM
///
/// The event definitions follow the conventions used by insurance telematics,
/// which score drivers on harsh braking, harsh acceleration, harsh cornering and
/// speeding. Those systems commonly treat about 0.3 g as the boundary between
/// normal and harsh for a car.
///
/// This vehicle is a bus, so the cornering threshold is set lower: it can only
/// hold about 0.35 g before lifting a wheel, so 0.25 g is genuinely aggressive
/// here even though it would be unremarkable in a saloon.
///
/// EVERYTHING IS SCORED AS A RATE OR A RATIO
///
/// Driving events are counted per minute and route rules per opportunity, so
/// neither a longer drive nor a longer route costs you points for its length
/// alone. An earlier version used flat per-event penalties and, on real recorded
/// data, gave a 196-second drive a 0 and an 11-second one a 90.
///
/// THE WEIGHTS ARE A DESIGN CHOICE, NOT A MEASUREMENT
///
/// Losing 30 points for running every stop sign, and 25 for never yielding,
/// expresses a judgement about their relative seriousness. It is not derived from
/// crash data and should not be presented as though it were. State it as a rubric
/// you defined and the numbers are defensible; imply it is empirical and they are
/// not.
///
/// The defaults were checked against modelled driver profiles: a clean run scores
/// 98, a careful driver who rolls one stop sign 89, an ordinary driver 71, a
/// hurried one 49, and a reckless one 0.
/// </summary>
public class DriveScorer : MonoBehaviour
{
    [Header("What counts as unsafe")]
    [Tooltip("Campus speed limit, mph. Time spent above this is penalised.")]
    public float speedLimitMph = 20f;

    [Tooltip("Deceleration counted as harsh braking, in g. Telematics systems " +
             "typically use around 0.30 for cars.")]
    public float hardBrakingG = 0.30f;

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

    [Header("Penalties")]
    [Tooltip("Points lost at 100% of the drive spent over the limit. Scales with " +
             "the fraction of time speeding, so it does not depend on drive length.")]
    public float speedingWeight = 25f;

    [Tooltip("Points lost per harsh-braking event per minute, capped below.")]
    public float brakingWeight = 2.5f;
    public float brakingCap = 10f;

    [Tooltip("Points lost per harsh-cornering event per minute, capped below.")]
    public float corneringWeight = 2f;
    public float corneringCap = 10f;

    [Tooltip("Points lost per pedestrian near miss per minute, capped below.")]
    public float nearMissWeight = 10f;
    public float nearMissCap = 20f;

    [Tooltip("Points lost per collision per minute, capped below.")]
    public float collisionWeight = 20f;
    public float collisionCap = 30f;

    [Header("Route rules")]
    [Tooltip("Points lost if EVERY stop sign on the route was run. Scored per " +
             "opportunity: run two of five and you lose two fifths of this.")]
    public float stopSignWeight = 30f;

    [Tooltip("Points lost if you failed to yield at every crossing, scored the " +
             "same way.")]
    public float yieldWeight = 25f;

    [Tooltip("Any collision at all costs at least this much, however long the drive. " +
             "Hitting someone should never be diluted by driving for another hour.")]
    public float minimumCollisionPenalty = 12f;

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

    public int Score { get; private set; }
    public string Grade { get; private set; }
    public string Verdict { get; private set; }

    // Kept separately so the debrief can show where the points went, rather than
    // just handing back a number the player has to take on faith.
    public float SpeedingDeduction { get; private set; }
    public float BrakingDeduction { get; private set; }
    public float CorneringDeduction { get; private set; }
    public float NearMissDeduction { get; private set; }
    public float CollisionDeduction { get; private set; }
    public float StopSignDeduction { get; private set; }
    public float YieldDeduction { get; private set; }

    // ------------------------------------------------------- route rule results

    public int StopSignsEncountered { get; private set; }
    public int StopSignViolations { get; private set; }
    public int CrosswalksEncountered { get; private set; }
    public int YieldViolations { get; private set; }

    /// <summary>
    /// Reported by RouteRunner as each stop sign is left behind.
    ///
    /// Counting opportunities as well as failures is what makes these scoreable.
    /// "Ran two stop signs" means something very different on a route with two
    /// than on a route with twenty, and only the ratio is comparable between runs.
    /// </summary>
    public void RegisterStopSign(bool violated)
    {
        StopSignsEncountered++;
        if (violated) StopSignViolations++;
    }

    public void RegisterCrosswalk(bool violated)
    {
        CrosswalksEncountered++;
        if (violated) YieldViolations++;
    }

    /// <summary>Harsh-braking events per minute — the rate the score is built from.</summary>
    public float BrakesPerMinute { get { return HardBrakes / Mathf.Max(DurationSeconds / 60f, 1f / 6f); } }
    public float CornersPerMinute { get { return HardCorners / Mathf.Max(DurationSeconds / 60f, 1f / 6f); } }
    public float PercentOverLimit { get { return DurationSeconds > 0f ? 100f * SecondsOverLimit / DurationSeconds : 0f; } }

    // ---------------------------------------------------------------- internals

    Rigidbody body;
    VehicleController vehicle;

    float previousSpeed;
    float previousHeading;
    Vector3 previousPosition;

    float movingSampleTotal;
    int movingSampleCount;

    float lastBrakeEvent = -999f;
    float lastCornerEvent = -999f;
    float lastNearMiss = -999f;
    float lastCollision = -999f;

    readonly List<Transform> pedestrians = new List<Transform>();
    float nextPedestrianRefresh;

    bool running = true;

    void Start()
    {
        body = GetComponent<Rigidbody>();
        vehicle = GetComponent<VehicleController>();

        previousPosition = transform.position;
        previousHeading = transform.eulerAngles.y;

        ClosestPedestrianM = 999f;
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

        if (speedMph > speedLimitMph) SecondsOverLimit += dt;

        // ---- braking -----------------------------------------------------------
        float longitudinalAccel = (speedMs - previousSpeed) / dt;
        float brakingG = -longitudinalAccel / 9.81f;             // positive when slowing

        // The same guard the cornering test uses. A sudden stop against a wall is a
        // collision, not braking, and it shows up here as an impossible figure —
        // real recorded data produced a "hardest braking" reading of 13.97 g.
        // No road vehicle exceeds about 1.2 g, so anything past 2 is an impact.
        if (brakingG < 2f)
        {
            if (brakingG > HardestBrakingG) HardestBrakingG = brakingG;

            if (brakingG > hardBrakingG && DurationSeconds - lastBrakeEvent > eventCooldown)
            {
                lastBrakeEvent = DurationSeconds;
                HardBrakes++;
            }
        }

        // ---- cornering ---------------------------------------------------------
        float heading = transform.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(previousHeading, heading) / dt;
        float lateralG = Mathf.Abs(speedMs * yawRate * Mathf.Deg2Rad) / 9.81f;

        // A physically impossible value is a collision spinning the body, not
        // cornering. Counting it would punish the player for being hit.
        if (lateralG < 1.5f)
        {
            if (lateralG > PeakLateralG) PeakLateralG = lateralG;

            if (lateralG > hardCorneringG && DurationSeconds - lastCornerEvent > eventCooldown)
            {
                lastCornerEvent = DurationSeconds;
                HardCorners++;
            }
        }

        // ---- pedestrians -------------------------------------------------------
        CheckPedestrians(speedMph);

        previousSpeed = speedMs;
        previousHeading = heading;
        previousPosition = transform.position;
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
            }
        }
    }

    /// <summary>Found by component name so this does not hard-depend on the pedestrian script.</summary>
    void RefreshPedestrians()
    {
        pedestrians.Clear();

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "PedestrianController") continue;
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
    }

    // -------------------------------------------------------------------- score

    /// <summary>
    /// Freezes the statistics, computes the score, and returns it.
    /// Called by the debrief screen when the player ends the drive.
    /// </summary>
    public int FinishDrive()
    {
        running = false;

        MeanSpeedMph = movingSampleCount > 0 ? movingSampleTotal / movingSampleCount : 0f;
        if (ClosestPedestrianM > 900f) ClosestPedestrianM = -1f;      // none encountered

        // Everything is scored as a RATE, not a total.
        //
        // The first version of this deducted a flat penalty per event, and it was
        // wrong in a way that only showed up on real data: a 196-second drive
        // scored 0 while an 11-second one scored 90, because penalties accumulate
        // with time and nothing offsets them. That punishes driving for longer,
        // which is precisely backwards. Insurance telematics avoid this by scoring
        // events per unit of driving, and so does this.
        float minutes = Mathf.Max(DurationSeconds / 60f, 1f / 6f);      // 10 s floor
        float fractionOverLimit = DurationSeconds > 0f
            ? Mathf.Clamp01(SecondsOverLimit / DurationSeconds)
            : 0f;

        SpeedingDeduction = Mathf.Min(fractionOverLimit * speedingWeight, speedingWeight);
        BrakingDeduction = Mathf.Min(HardBrakes / minutes * brakingWeight, brakingCap);
        CorneringDeduction = Mathf.Min(HardCorners / minutes * corneringWeight, corneringCap);
        NearMissDeduction = Mathf.Min(NearMisses / minutes * nearMissWeight, nearMissCap);

        CollisionDeduction = Mathf.Min(Collisions / minutes * collisionWeight, collisionCap);
        if (Collisions > 0)
            CollisionDeduction = Mathf.Max(CollisionDeduction, minimumCollisionPenalty);

        // Route rules are scored per opportunity rather than per minute. A stop
        // sign is not something you encounter at a rate — it is there once, and you
        // either stopped or you did not.
        StopSignDeduction = StopSignsEncountered > 0
            ? stopSignWeight * StopSignViolations / StopSignsEncountered
            : 0f;

        YieldDeduction = CrosswalksEncountered > 0
            ? yieldWeight * YieldViolations / CrosswalksEncountered
            : 0f;

        float deductions = SpeedingDeduction + BrakingDeduction + CorneringDeduction +
                           NearMissDeduction + CollisionDeduction +
                           StopSignDeduction + YieldDeduction;

        Score = Mathf.Clamp(Mathf.RoundToInt(100f - deductions), 0, 100);

        if (Score >= 90) { Grade = "A"; Verdict = "Excellent. Safe, controlled driving throughout."; }
        else if (Score >= 80) { Grade = "B"; Verdict = "Good. A few lapses worth reviewing below."; }
        else if (Score >= 70) { Grade = "C"; Verdict = "Acceptable, but several unsafe moments."; }
        else if (Score >= 60) { Grade = "D"; Verdict = "Unsafe driving. Review the events below."; }
        else { Grade = "F"; Verdict = "Would not be permitted to carry passengers."; }

        // Stopping the logger here flushes its CSV, so the written data and this
        // report describe exactly the same drive.
        DrivingTelemetryLogger logger = GetComponent<DrivingTelemetryLogger>();
        if (logger != null) logger.enabled = false;

        if (saveReport) WriteReport();

        return Score;
    }

    void WriteReport()
    {
        try
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string folder = Path.Combine(root, outputFolder);
            Directory.CreateDirectory(folder);

            string stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(folder, "drive_report_" + stamp + ".txt");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NC A&T Campus Driving Safety Simulator: Drive Report");
            sb.AppendLine("Generated " + System.DateTime.Now.ToString("f"));
            sb.AppendLine(new string('-', 58));
            sb.AppendLine();
            sb.AppendLine("SAFETY SCORE   " + Score + " / 100   (" + Grade + ")");
            sb.AppendLine(Verdict);
            sb.AppendLine();
            sb.AppendLine("Drive");
            sb.AppendLine("  Duration              " + FormatTime(DurationSeconds));
            sb.AppendLine("  Distance              " + DistanceMetres.ToString("F0") + " m");
            sb.AppendLine("  Average speed         " + MeanSpeedMph.ToString("F1") + " mph");
            sb.AppendLine("  Top speed             " + TopSpeedMph.ToString("F1") + " mph");
            sb.AppendLine();
            sb.AppendLine("Safety events   (scored as rates, so drive length does not skew the result)");
            sb.AppendLine("  Time over " + speedLimitMph + " mph      " + SecondsOverLimit.ToString("F0") + " s"
                          + "  (" + PercentOverLimit.ToString("F0") + "% of the drive)"
                          + "   -" + SpeedingDeduction.ToString("F0"));
            sb.AppendLine("  Harsh braking         " + HardBrakes
                          + "  (" + BrakesPerMinute.ToString("F1") + "/min)   -" + BrakingDeduction.ToString("F0"));
            sb.AppendLine("  Harsh cornering       " + HardCorners
                          + "  (" + CornersPerMinute.ToString("F1") + "/min)   -" + CorneringDeduction.ToString("F0"));
            sb.AppendLine("  Pedestrian near miss  " + NearMisses + "   -" + NearMissDeduction.ToString("F0"));
            sb.AppendLine("  Collisions            " + Collisions + "   -" + CollisionDeduction.ToString("F0"));

            if (StopSignsEncountered > 0 || CrosswalksEncountered > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Route rules   (scored per opportunity)");
                sb.AppendLine("  Stop signs obeyed     " +
                              (StopSignsEncountered - StopSignViolations) + " / " +
                              StopSignsEncountered + "   -" + StopSignDeduction.ToString("F0"));
                sb.AppendLine("  Crossings yielded     " +
                              (CrosswalksEncountered - YieldViolations) + " / " +
                              CrosswalksEncountered + "   -" + YieldDeduction.ToString("F0"));
            }
            sb.AppendLine();
            sb.AppendLine("Peaks");
            sb.AppendLine("  Hardest braking       " + HardestBrakingG.ToString("F2") + " g");
            sb.AppendLine("  Peak cornering load   " + PeakLateralG.ToString("F2") + " g");
            sb.AppendLine("  Closest pedestrian    " +
                          (ClosestPedestrianM < 0f ? "none encountered"
                                                   : ClosestPedestrianM.ToString("F1") + " m"));
            sb.AppendLine();
            sb.AppendLine("Thresholds used: " + hardBrakingG + " g braking, " +
                          hardCorneringG + " g cornering, " + nearMissDistance + " m near miss.");
            sb.AppendLine("Penalty weights are a defined rubric, not values derived from crash data.");

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
