using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What an infraction actually costs you while you are still driving.
///
/// Up to now every violation was invisible until the debrief. That makes the
/// simulator a recording device rather than a training one: a driver who runs three
/// stop signs learns nothing until the run is over, by which point they cannot
/// connect the penalty to the moment that earned it. This closes that gap.
///
/// TWO CONSEQUENCES, BOTH WITH A REAL COUNTERPART
///
/// The graduated governor turns the bus down as violations accumulate, which is what
/// fleet telematics and school bus governors do to drivers who collect events. The
/// examiner stop brings the bus to a controlled halt after a disqualifying act,
/// which is what an examiner does when they end a road test.
///
/// WHAT IS DELIBERATELY NOT DONE
///
/// No grip loss, no brake fade, no steering lag. Those would make the vehicle
/// different for each driver depending on how they drove, and the whole point of
/// scoring against the North Carolina elements is that every test-taker sat the same
/// test in the same bus. A governor is recorded, documented and reversible; degraded
/// tyres are an invention that would quietly destroy the comparison.
///
/// For the runs that go in the coursework, turn this off. For someone learning the
/// route, leave it on.
/// </summary>
public class InfractionConsequences : MonoBehaviour
{
    [Header("Master switch")]
    [Tooltip("Off means violations still score but the bus never changes. Use this " +
             "setting when collecting runs for analysis, so every driver takes the " +
             "same test in the same vehicle.")]
    public bool physicsConsequences = true;

    [Header("Governor")]
    [Tooltip("Turn the bus down as faults accumulate. OFF by default: a vehicle that " +
             "gets slower because the driver made mistakes is not something that " +
             "happens on a road test, and it means two people driving identically " +
             "get different vehicles, which ruins any comparison between them. Turn " +
             "it on only for training, never for runs that go in the analysis.")]
    public bool speedGovernor = false;

    [Tooltip("Violations needed before the bus is turned down at all. Below this " +
             "nothing happens, so a single mistake does not cripple the run.")]
    public int governorStartsAt = 3;

    [Tooltip("Speed caps in mph, applied in order as violations mount. The first " +
             "applies at the threshold above, the next three violations later, and " +
             "so on. The campus limit is 20, so the first cap is already a real " +
             "restriction.")]
    public float[] speedCapsMph = { 15f, 12f, 10f };

    [Tooltip("Violations between one cap and the next.")]
    public int violationsPerStep = 2;

    [Header("Examiner stop")]
    [Tooltip("Seconds the bus is held stationary after a disqualifying act.")]
    public float examinerStopSeconds = 3f;

    [Tooltip("Seconds allowed for the bus to brake to a halt before the hold begins.")]
    public float examinerBrakeSeconds = 2f;

    [Header("HUD")]
    [Tooltip("Large centred text that names the element just failed.")]
    public Text flashLabel;

    [Tooltip("Black panel behind the fault text so the warning is easy to read.")]
    public Image flashBackground;

    [Tooltip("Small persistent text showing the running violation count.")]
    public Text tallyLabel;

    [Tooltip("How long the fault flash remains visible.")]
    public float flashSeconds = 2.5f;

    [Tooltip("Opacity of the black warning box behind the fault text.")]
    [Range(0f, 1f)] public float flashBackgroundAlpha = 0.75f;

    // ---------------------------------------------------------------- internals

    NcRubric rubric;
    VehicleController vehicle;

    float flashRemaining;
    int lastAppliedTier = -1;

    void Start()
    {
        rubric = GetComponent<NcRubric>();
        if (rubric == null) rubric = FindObjectOfType<NcRubric>();

        vehicle = GetComponent<VehicleController>();
        if (vehicle == null) vehicle = FindObjectOfType<VehicleController>();

        if (rubric == null)
        {
            Debug.LogWarning("[InfractionConsequences] No NcRubric found, so nothing " +
                             "will be reported. Run NCAT > Set Up Road Test.");
            enabled = false;
            return;
        }

        rubric.OnViolation += OnViolation;

        if (flashLabel != null) flashLabel.text = "";
        if (flashBackground != null)
        {
            flashBackground.gameObject.SetActive(false);
            flashBackground.color = new Color(0f, 0f, 0f, flashBackgroundAlpha);
        }
        UpdateTally();
    }

    void OnDestroy()
    {
        // Without this the rubric keeps a reference to a destroyed component and
        // throws on the next violation after a scene reload.
        if (rubric != null) rubric.OnViolation -= OnViolation;

        // Never leave the next run governed by the last one's mistakes.
        if (vehicle != null)
        {
            vehicle.imposedSpeedCapMph = 0f;
            vehicle.forcedStopSeconds = 0f;
        }
    }

    void Update()
    {
        if (flashRemaining <= 0f) return;

        // Unscaled, so a flash raised just before the debrief pauses the game does
        // not hang on screen behind the report.
        flashRemaining -= Time.unscaledDeltaTime;
        if (flashRemaining <= 0f)
        {
            if (flashLabel != null) flashLabel.text = "";
            if (flashBackground != null) flashBackground.gameObject.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ reacting

    void OnViolation(NcElement element, string detail)
    {
        Flash(detail, element);
        UpdateTally();

        if (!physicsConsequences || vehicle == null) return;

        // The examiner stop stays: ending the drive after a disqualifying act is what
        // an examiner actually does. The governor does not, so it is opt in.
        if (speedGovernor) ApplyGovernor();

        // A disqualifying act stops the bus. Everything else only turns it down.
        if (rubric.AutomaticFailures.Count > 0)
            vehicle.forcedStopSeconds = examinerBrakeSeconds + examinerStopSeconds;
    }

    /// <summary>
    /// Turns the bus down a step at a time.
    ///
    /// Applied by tier rather than recomputed from scratch so the cap only ever
    /// tightens during a run. A driver cannot earn their speed back by driving well
    /// for a while, which is the same way a fleet governor behaves: it is reset by
    /// the depot, not by the next clean mile.
    /// </summary>
    void ApplyGovernor()
    {
        int over = rubric.TotalViolations - governorStartsAt;
        if (over < 0) return;

        int tier = Mathf.Min(over / Mathf.Max(1, violationsPerStep), speedCapsMph.Length - 1);
        if (tier <= lastAppliedTier) return;

        lastAppliedTier = tier;
        vehicle.imposedSpeedCapMph = speedCapsMph[tier];

        Flash("SPEED LIMITED TO " + speedCapsMph[tier].ToString("F0") + " MPH", null);
    }

    // ---------------------------------------------------------------------- HUD

    void Flash(string message, NcElement? element)
    {
        if (flashLabel == null || string.IsNullOrEmpty(message)) return;

        string line = element.HasValue
            ? NcRubric.Label(element.Value).ToUpperInvariant() + "\n" + message
            : message;

        flashLabel.text = line;
        if (flashBackground != null)
        {
            flashBackground.color = new Color(0f, 0f, 0f, flashBackgroundAlpha);
            flashBackground.gameObject.SetActive(true);
        }
        flashRemaining = flashSeconds;
    }

    void UpdateTally()
    {
        if (tallyLabel == null || rubric == null) return;

        int count = rubric.TotalViolations;

        tallyLabel.text = count == 0 ? "NO FAULTS"
                                     : count + (count == 1 ? " FAULT" : " FAULTS");

        // Amber once the governor is within reach, red once it has bitten.
        tallyLabel.color = count == 0 ? new Color(0.44f, 0.81f, 0.55f)
                         : count < governorStartsAt ? new Color(1f, 0.78f, 0.27f)
                         : new Color(1f, 0.42f, 0.34f);
    }
}
