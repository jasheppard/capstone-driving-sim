using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the HUD speedometer: an analogue needle plus a digital mph readout.
///
/// Both together on purpose. The needle is what you read peripherally while
/// watching the road — position on an arc is far faster to judge than reading
/// digits — and the number is there when you want the exact figure, which is what
/// the telemetry writeup needs.
/// </summary>
public class Speedometer : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Left empty, it finds the VehicleController in the scene on start.")]
    public VehicleController vehicle;

    [Header("References")]
    public RectTransform needle;
    public Text digital;
    public Text stoppingLabel;

    [Header("Dial")]
    [Tooltip("Speed at the far end of the arc. Must match what the setup drew on " +
             "the face, or the needle will point at the wrong numbers.")]
    public float maxDisplaySpeed = 80f;

    [Tooltip("Needle rotation at zero, in degrees. Positive is anticlockwise, so " +
             "130 points down and to the left.")]
    public float startAngle = 130f;

    [Tooltip("Total travel from zero to full scale, in degrees.")]
    public float sweepAngle = 260f;

    [Tooltip("How quickly the needle chases the real speed. Real instruments lag " +
             "slightly; instant response reads as jittery.")]
    public float needleSmoothing = 8f;

    [Header("Speed limit")]
    [Tooltip("Campus limit in mph. The readout changes colour above it.")]
    public float speedLimitMph = 45f;

    [Tooltip("How far over the limit before the readout goes red rather than amber.")]
    public float overLimitMargin = 5f;

    [Header("Stopping distance")]
    [Tooltip("Show how far it would take to stop from the current speed. On a " +
             "vehicle this heavy that number is the whole safety lesson.")]
    public bool showStoppingDistance = true;

    static readonly Color Normal = new Color(0.92f, 0.94f, 0.96f);
    static readonly Color Warning = new Color(1f, 0.75f, 0.15f);
    static readonly Color Over = new Color(1f, 0.35f, 0.28f);

    float displayedSpeed;

    void Start()
    {
        if (vehicle == null) vehicle = FindObjectOfType<VehicleController>();

        if (vehicle == null)
        {
            Debug.LogError("[Speedometer] No VehicleController in the scene: " +
                           "nothing to read a speed from.");
            enabled = false;
            return;
        }

        if (needle == null)
            Debug.LogWarning("[Speedometer] No needle assigned. Re-run NCAT → Set Up Speedometer.");
    }

    void Update()
    {
        if (vehicle == null) return;

        float mph = vehicle.GetSpeedMPH();

        // Exponential smoothing, framerate-independent.
        displayedSpeed = Mathf.Lerp(displayedSpeed, mph,
                                    1f - Mathf.Exp(-needleSmoothing * Time.deltaTime));

        UpdateNeedle();
        UpdateReadout();
        UpdateStoppingDistance();
    }

    void UpdateNeedle()
    {
        if (needle == null) return;

        float t = Mathf.Clamp01(displayedSpeed / Mathf.Max(1f, maxDisplaySpeed));
        needle.localRotation = Quaternion.Euler(0f, 0f, startAngle - t * sweepAngle);
    }

    void UpdateReadout()
    {
        if (digital == null) return;

        digital.text = Mathf.RoundToInt(displayedSpeed).ToString();

        if (displayedSpeed > speedLimitMph + overLimitMargin) digital.color = Over;
        else if (displayedSpeed > speedLimitMph) digital.color = Warning;
        else digital.color = Normal;
    }

    void UpdateStoppingDistance()
    {
        if (stoppingLabel == null) return;

        if (!showStoppingDistance || displayedSpeed < 1f)
        {
            stoppingLabel.text = "";
            return;
        }

        // Reported in feet, since the speed is in mph and mixing units on one
        // instrument is how people misread them.
        float feet = vehicle.StoppingDistance() * 3.28084f;
        stoppingLabel.text = "STOP " + Mathf.RoundToInt(feet) + " ft";
        stoppingLabel.color = new Color(0.62f, 0.66f, 0.71f);
    }
}
