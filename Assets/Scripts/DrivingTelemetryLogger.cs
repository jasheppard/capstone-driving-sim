using UnityEngine;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Records real driving data from the campus simulator to a CSV file.
///
/// This is the data source for the CST499 analytics work. Every play session
/// writes one file, so a session is a self-contained run you can analyse on its
/// own or concatenate with others.
///
/// The variables were chosen so that the correlations in them mean something for
/// driving safety, rather than being arbitrary numbers that happen to move
/// together:
///
///   speed_kmh              how fast the vehicle is travelling
///   throttle / brake       what the driver is asking for
///   steer_input            raw steering command, -1 (left) to +1 (right)
///   yaw_rate_dps           how fast the car is actually rotating
///   lateral_accel_ms2      sideways load through the corner — the physical
///                          quantity that decides whether a real car holds on
///   speed_change_ms2       longitudinal acceleration and braking
///   nearest_ped_m          distance to the closest pedestrian
///   peds_within_15m        how crowded the immediate surroundings are
///   collisions_cum         running total of impacts this session
///
/// The interesting pairs are physical rather than tautological: lateral
/// acceleration is speed times yaw rate, so it rises with both — which is the
/// textbook reason cornering fast is what loses control, and it shows up in the
/// correlation matrix as a real, explainable relationship.
///
/// Attach to PlayerCar, or just run NCAT → Set Up Telemetry Logging.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DrivingTelemetryLogger : MonoBehaviour
{
    [Header("Sampling")]
    [Tooltip("Samples per second. 10 Hz gives a few thousand rows in a short drive: " +
             "plenty for correlation work without producing an unwieldy file.")]
    public float sampleRate = 10f;

    [Tooltip("Ignore the first moments of the session, while the car is still being " +
             "seated on the ground by CarGroundSpawner. Those frames are not driving.")]
    public float warmupSeconds = 3f;

    [Header("Pedestrian proximity")]
    [Tooltip("Radius counted as 'near' for the crowding column, in metres.")]
    public float nearRadius = 15f;

    [Tooltip("Reported when no pedestrian is in range: a ceiling, not a real distance.")]
    public float maxReportedDistance = 100f;

    [Header("Output")]
    [Tooltip("Folder for the CSV files, relative to the project folder. " +
             "Kept outside Assets so Unity does not try to import them.")]
    public string outputFolder = "TelemetryData";

    [Tooltip("Print the file path and row count to the Console when the session ends.")]
    public bool logSummary = true;

    // ---------------------------------------------------------------- internals

    Rigidbody body;
    StreamWriter writer;
    string filePath;

    // Read once at the start of the session rather than on every row.
    string driverName = DriverIdentity.Unknown;

    float sessionTime;
    float nextSampleTime;
    int rowCount;

    float previousSpeed;            // m/s, for longitudinal acceleration
    float previousHeading;          // degrees, for yaw rate
    float distanceTravelled;
    Vector3 previousPosition;

    int collisions;

    // Pedestrians are spawned and recycled at runtime, so the list is refreshed
    // periodically rather than cached once at Start.
    readonly List<Transform> pedestrians = new List<Transform>();
    float nextPedestrianRefresh;

    void Start()
    {
        body = GetComponent<Rigidbody>();

        previousPosition = transform.position;
        previousHeading = transform.eulerAngles.y;
        previousSpeed = 0f;

        OpenFile();
    }

    void OpenFile()
    {
        // Application.dataPath is <project>/Assets, so its parent is the project root.
        string root = Path.GetDirectoryName(Application.dataPath);

        // One folder per driver, with the name in the filename as well. The folder
        // is what makes a cohort of runs sortable by person without opening
        // anything; the name in the filename is what survives a file being moved out
        // of its folder, which is how a CSV usually reaches the analysis script.
        string driver = DriverIdentity.FileSafeName;
        driverName = driver;

        string folder = Path.Combine(Path.Combine(root, outputFolder), driver);
        Directory.CreateDirectory(folder);

        string stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        filePath = Path.Combine(folder, "drive_" + driver + "_" + stamp + ".csv");

        writer = new StreamWriter(filePath, false, Encoding.UTF8);

        writer.WriteLine(
            "driver,time_s,speed_kmh,throttle,brake,steer_input,yaw_rate_dps," +
            "lateral_accel_ms2,speed_change_ms2,nearest_ped_m,peds_within_15m," +
            "collisions_cum,distance_m,pos_x,pos_z");

        Debug.Log("[Telemetry] Recording this session to:\n" + filePath);
    }

    void FixedUpdate()
    {
        sessionTime += Time.fixedDeltaTime;

        if (sessionTime < warmupSeconds) return;
        if (writer == null) return;
        if (sessionTime < nextSampleTime) return;

        nextSampleTime = sessionTime + 1f / Mathf.Max(1f, sampleRate);

        WriteSample();
    }

    void WriteSample()
    {
        float dt = 1f / Mathf.Max(1f, sampleRate);

        // ---- motion -----------------------------------------------------------
        Vector3 velocity = body.velocity;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

        float speedMs = flat.magnitude;
        float speedKmh = speedMs * 3.6f;

        float heading = transform.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(previousHeading, heading) / dt;   // deg/s

        // Lateral acceleration is v * omega. This is the sideways force the tyres
        // have to generate, and it is what actually breaks traction — a tight turn
        // at low speed and a gentle turn at high speed can load the car equally.
        float lateralAccel = speedMs * (yawRate * Mathf.Deg2Rad);

        float speedChange = (speedMs - previousSpeed) / dt;                // m/s^2

        distanceTravelled += Vector3.Distance(
            new Vector3(previousPosition.x, 0f, previousPosition.z),
            new Vector3(transform.position.x, 0f, transform.position.z));

        previousSpeed = speedMs;
        previousHeading = heading;
        previousPosition = transform.position;

        // ---- driver input -----------------------------------------------------
        float throttle, brake, steer;
        ReadInput(out throttle, out brake, out steer);

        // ---- surroundings -----------------------------------------------------
        float nearest;
        int nearby;
        MeasurePedestrians(out nearest, out nearby);

        // InvariantCulture matters: on a machine set to a comma decimal separator,
        // the default formatting would produce "12,5" and silently corrupt the CSV.
        writer.WriteLine(string.Join(",", new string[]
        {
            driverName,
            F(sessionTime - warmupSeconds),
            F(speedKmh),
            F(throttle),
            F(brake),
            F(steer),
            F(yawRate),
            F(lateralAccel),
            F(speedChange),
            F(nearest),
            nearby.ToString(CultureInfo.InvariantCulture),
            collisions.ToString(CultureInfo.InvariantCulture),
            F(distanceTravelled),
            F(transform.position.x),
            F(transform.position.z)
        }));

        rowCount++;
    }

    static string F(float value)
    {
        return value.ToString("F4", CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------- input

    /// <summary>
    /// Reads the same controls the vehicle does, without depending on
    /// VehicleController's internals — so this keeps compiling if that file changes.
    /// </summary>
    void ReadInput(out float throttle, out float brake, out float steer)
    {
        throttle = 0f;
        brake = 0f;
        steer = 0f;

        // Keyboard / legacy axes.
        float vertical = Input.GetAxis("Vertical");
        steer = Input.GetAxis("Horizontal");

        if (vertical > 0f) throttle = vertical;
        else brake = -vertical;

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            float rt = pad.rightTrigger.ReadValue();
            float lt = pad.leftTrigger.ReadValue();
            float stick = pad.leftStick.x.ReadValue();

            // Whichever input is being used more strongly wins, so plugging a
            // controller in mid-session does not zero out the keyboard columns.
            if (rt > throttle) throttle = rt;
            if (lt > brake) brake = lt;
            if (Mathf.Abs(stick) > Mathf.Abs(steer)) steer = stick;
        }
#endif

        throttle = Mathf.Clamp01(throttle);
        brake = Mathf.Clamp01(brake);
        steer = Mathf.Clamp(steer, -1f, 1f);
    }

    // ------------------------------------------------------------- pedestrians

    void MeasurePedestrians(out float nearest, out int nearby)
    {
        if (Time.time >= nextPedestrianRefresh)
        {
            RefreshPedestrians();
            nextPedestrianRefresh = Time.time + 2f;
        }

        nearest = maxReportedDistance;
        nearby = 0;

        Vector3 here = transform.position;

        for (int i = 0; i < pedestrians.Count; i++)
        {
            Transform ped = pedestrians[i];
            if (ped == null) continue;

            float distance = Vector3.Distance(here, ped.position);

            if (distance < nearest) nearest = distance;
            if (distance <= nearRadius) nearby++;
        }

        if (nearest > maxReportedDistance) nearest = maxReportedDistance;
    }

    /// <summary>
    /// Finds pedestrians by component name rather than by type, so this file does
    /// not hard-depend on PedestrianController existing. A project without
    /// pedestrians still logs everything else and simply reports the ceiling
    /// distance in that column.
    /// </summary>
    void RefreshPedestrians()
    {
        pedestrians.Clear();

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            if (mb == null) continue;
            if (mb.GetType().Name != "PedestrianController") continue;

            pedestrians.Add(mb.transform);
        }
    }

    // -------------------------------------------------------------- collisions

    [Header("Collision detection")]
    [Tooltip("Minimum impact speed counted as a collision, in m/s. 4 is about 9 mph. " +
             "Photogrammetry ground is not flat, so a driving vehicle generates a " +
             "constant stream of low-speed contacts against mesh seams and kerbs.")]
    public float collisionSpeedThreshold = 4f;

    [Tooltip("Minimum seconds between counted collisions. One crash produces many " +
             "contact points over several frames; without this, one impact is " +
             "recorded as a dozen.")]
    public float collisionCooldown = 1.5f;

    float lastCollisionTime = -999f;

    /// <summary>
    /// Counts real impacts and ignores terrain scrape.
    ///
    /// The first version of this used a 2 m/s threshold with no cooldown, and
    /// recorded 108 "collisions" in a 196-second drive — one every 1.8 seconds.
    /// None of them were crashes. Two things were wrong: the threshold was below
    /// the speed at which a vehicle bumps over seams in scanned terrain, and a
    /// single impact fires OnCollisionEnter repeatedly as contact points resolve.
    /// </summary>
    void OnCollisionEnter(Collision collision)
    {
        if (sessionTime < warmupSeconds) return;      // the initial landing is not a crash

        if (collision.relativeVelocity.magnitude < collisionSpeedThreshold) return;
        if (sessionTime - lastCollisionTime < collisionCooldown) return;

        lastCollisionTime = sessionTime;
        collisions++;
    }

    // ------------------------------------------------------------------ closing

    void OnDisable()
    {
        CloseFile();
    }

    void OnApplicationQuit()
    {
        CloseFile();
    }

    void CloseFile()
    {
        if (writer == null) return;

        writer.Flush();
        writer.Close();
        writer = null;

        if (logSummary)
        {
            Debug.Log(
                "[Telemetry] Session finished.\n" +
                "  Rows: " + rowCount + "\n" +
                "  Duration: " + (sessionTime - warmupSeconds).ToString("F1") + " s\n" +
                "  Distance: " + distanceTravelled.ToString("F0") + " m\n" +
                "  Collisions: " + collisions + "\n" +
                "  File: " + filePath);
        }
    }
}
