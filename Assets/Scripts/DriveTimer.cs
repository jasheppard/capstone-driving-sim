using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The drive clock on the HUD. Holds at 0:00 until the bus pulls away, then counts up.
///
/// WHY IT DOES NOT START WITH THE SCENE
///
/// Pressing Play is not the start of a drive. The briefing is on screen, the Cesium
/// tiles are still streaming, and CarGroundSpawner is still seating the bus on the
/// terrain. A clock started on the first frame charges the driver for all of that,
/// and the wait depends on the machine, so two runs of the same route would not be
/// comparable.
///
/// WHY FORWARD MOTION, AND WHY IT HAS TO LAST
///
/// Seating the bus on photogrammetry terrain produces real velocity: it drops a few
/// centimetres, slides on a mesh seam, settles. A plain "is it moving" test starts on
/// that. So two things are required, and settling fails both: the motion is FORWARD
/// along the nose, and it lasts longer than a frame. The confirmation time is then
/// credited back, so the clock reads from when the bus began moving rather than from
/// when this became sure of it.
///
/// Reversing off the stand does not start it. The drive starts when the route does.
/// </summary>
[DefaultExecutionOrder(400)]
public class DriveTimer : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The bus. Left empty, the one in the scene is found at startup.")]
    public VehicleController vehicle;

    [Tooltip("The scorer, so the clock stops when the drive ends. Left empty, the " +
             "one on the bus is used.")]
    public DriveScorer scorer;

    [Tooltip("The label this writes into.")]
    public Text display;

    [Header("When it starts")]
    [Tooltip("Forward speed that counts as pulling away, in mph. Low enough for a " +
             "gentle start, high enough to ignore the bus settling.")]
    public float startSpeedMph = 1.5f;

    [Tooltip("Seconds of continuous forward motion before the clock commits. This is " +
             "what rejects a one-frame spike while the bus is being seated. It is " +
             "credited back, so no time is lost.")]
    public float confirmSeconds = 0.25f;

    [Header("While it runs")]
    [Tooltip("Keep counting while stopped. On, which is what a timed drive means: " +
             "waiting at a red light is part of the journey. Off, it measures only " +
             "time spent moving.")]
    public bool countWhileStopped = true;

    [Tooltip("Speed below which the bus counts as stopped, in mph. Only used when " +
             "Count While Stopped is off.")]
    public float stoppedSpeedMph = 0.5f;

    [Header("Look")]
    [Tooltip("Show tenths. Useful for comparing two runs of the same route; busier " +
             "to look at while driving.")]
    public bool showTenths = false;

    public Color waitingColour = new Color(0.55f, 0.60f, 0.66f);
    public Color runningColour = new Color(0.94f, 0.96f, 0.98f);
    public Color finishedColour = new Color(0.44f, 0.85f, 0.50f);

    /// <summary>Seconds on the clock.</summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>True once the bus has pulled away.</summary>
    public bool Running { get; private set; }

    /// <summary>True once the drive has ended and the clock is frozen.</summary>
    public bool Finished { get; private set; }

    float forwardFor;

    void Start()
    {
        if (vehicle == null) vehicle = Object.FindObjectOfType<VehicleController>();
        if (scorer == null && vehicle != null) scorer = vehicle.GetComponent<DriveScorer>();

        if (display == null)
            Debug.LogWarning("[DriveTimer] No display label: the clock runs but nothing " +
                             "shows it. Run NCAT -> Set Up Drive Timer.");

        Show();
    }

    /// <summary>
    /// Update rather than FixedUpdate, deliberately: Time.deltaTime is zero while the
    /// game is paused, which holds the clock still behind the briefing and the debrief
    /// without either of them needing to know this exists.
    /// </summary>
    void Update()
    {
        if (Finished) return;

        // Checked first, so the frame the debrief appears on is not counted.
        if (scorer != null && !scorer.DriveRunning) { Finish(); return; }

        if (vehicle == null) return;

        if (!Running) { WaitForTheStart(); return; }

        if (countWhileStopped || Mathf.Abs(vehicle.GetSpeedMPH()) > stoppedSpeedMph)
            ElapsedSeconds += Time.deltaTime;

        Show();
    }

    void WaitForTheStart()
    {
        // Signed speed, so backing off the stand does not start the drive.
        if (vehicle.GetForwardSpeedMPH() < startSpeedMph) { forwardFor = 0f; return; }

        forwardFor += Time.deltaTime;
        if (forwardFor < confirmSeconds) return;

        Running = true;

        // It has been rolling for the whole confirmation window, so that belongs on
        // the clock. Without this the timer reads 0:00 while the bus is already moving.
        ElapsedSeconds = forwardFor;
        Show();
    }

    /// <summary>Freezes the clock. Called when the drive ends.</summary>
    public void Finish()
    {
        if (Finished) return;

        Finished = true;
        Show();

        Debug.Log("[DriveTimer] Drive time " + Format(ElapsedSeconds) +
                  (Running ? "" : "   (the bus never pulled away)"));
    }

    /// <summary>Back to 0:00 and waiting, for restarting a run without reloading.</summary>
    public void ResetClock()
    {
        ElapsedSeconds = 0f;
        forwardFor = 0f;
        Running = false;
        Finished = false;
        Show();
    }

    void Show()
    {
        if (display == null) return;

        display.text = Format(ElapsedSeconds);
        display.color = Finished ? finishedColour : (Running ? runningColour : waitingColour);
    }

    /// <summary>
    /// Minutes and seconds, counting up from zero. Truncated rather than rounded: a
    /// clock that shows 0:01 at six tenths never reads 0:00 for a full second, which
    /// is wrong in the direction people notice.
    /// </summary>
    public string Format(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float remainder = seconds - minutes * 60f;

        if (!showTenths)
            return minutes + ":" + Mathf.FloorToInt(remainder).ToString("00");

        int tenths = Mathf.FloorToInt(remainder * 10f);
        return minutes + ":" + (tenths / 10).ToString("00") + "." + (tenths % 10);
    }
}
