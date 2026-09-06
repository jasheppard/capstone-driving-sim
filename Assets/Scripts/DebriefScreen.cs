using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The post-drive report.
///
/// A training simulator teaches in the debrief, not during the run. Up to now
/// every safety event was recorded to a CSV that only the researcher ever read;
/// the person driving got no feedback at all. This closes that loop.
///
/// Press Escape, or Options on the controller, to end the drive and see it.
///
/// Ending is deliberate rather than resumable: FinishDrive() freezes the
/// statistics and flushes the telemetry file, so one drive maps to exactly one
/// report and one CSV. Resuming afterwards would produce a session whose data
/// stops halfway through.
/// </summary>
public class DebriefScreen : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("The report itself. Hidden until the drive ends.")]
    public GameObject panel;

    [Header("Score")]
    public Text scoreText;
    public Text gradeText;
    public Text verdictText;

    [Header("Columns")]
    [Tooltip("Rich text is used for these, so individual values can be coloured.")]
    public Text summaryLabels;
    public Text summaryValues;
    public Text eventLabels;
    public Text eventValues;

    [Header("Buttons")]
    public Button driveAgainButton;
    public Button menuButton;

    [Header("Settings")]
    public KeyCode endDriveKey = KeyCode.Escape;
    public string menuSceneName = "MainMenu";

    [Tooltip("Minimum seconds of driving before a report means anything. A five " +
             "second drive has no rate worth computing.")]
    public float minimumDriveSeconds = 20f;

    static readonly string Good = "#6FCF8B";
    static readonly string Warn = "#FFC845";
    static readonly string Bad = "#FF6B57";
    static readonly string Plain = "#E7EBF0";

    DriveScorer scorer;
    RouteRunner route;
    bool shown;

    void Start()
    {
        scorer = FindObjectOfType<DriveScorer>();
        route = FindObjectOfType<RouteRunner>();      // optional — free driving has none

        if (scorer == null)
            Debug.LogError("[DebriefScreen] No DriveScorer in the scene — " +
                           "run NCAT → Set Up Drive Debrief.");

        if (panel != null) panel.SetActive(false);

        if (driveAgainButton != null) driveAgainButton.onClick.AddListener(DriveAgain);
        if (menuButton != null) menuButton.onClick.AddListener(ReturnToMenu);
    }

    void Update()
    {
        if (shown) return;

        bool pressed = Input.GetKeyDown(endDriveKey);

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.startButton.wasPressedThisFrame) pressed = true;   // Options
#endif

        if (pressed) EndDrive();
    }

    public void EndDrive()
    {
        if (shown || scorer == null) return;

        if (scorer.DurationSeconds < minimumDriveSeconds)
        {
            Debug.Log("[DebriefScreen] Only " + scorer.DurationSeconds.ToString("F0") +
                      "s driven — drive for at least " + minimumDriveSeconds +
                      "s before ending, or the rates are meaningless.");
            return;
        }

        shown = true;
        scorer.FinishDrive();

        Populate();

        if (panel != null) panel.SetActive(true);

        // Paused, so the world behind the report is frozen rather than carrying on
        // without a driver. Everything below must use unscaled time.
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    void Populate()
    {
        if (scoreText != null) scoreText.text = scorer.Score.ToString();

        if (gradeText != null)
        {
            gradeText.text = scorer.Grade;
            gradeText.color = GradeColour(scorer.Score);
        }

        if (scoreText != null) scoreText.color = GradeColour(scorer.Score);
        if (verdictText != null) verdictText.text = scorer.Verdict;

        // ---- the drive ---------------------------------------------------------
        bool onRoute = route != null && route.WaypointCount > 0;

        if (summaryLabels != null)
            summaryLabels.text =
                "Route\n" +
                "Passengers carried\n" +
                "Duration\n" +
                "Distance\n" +
                "Average speed\n" +
                "Top speed";

        if (summaryValues != null)
            summaryValues.text =
                Colour(onRoute
                           ? (route.RouteComplete
                                  ? "Completed"
                                  : route.WaypointsReached + " of " + route.WaypointCount + " stops")
                           : "Free drive",
                       !onRoute ? Plain : route.RouteComplete ? Good : Warn) + "\n" +
                Colour(onRoute ? route.PassengersCarried.ToString() : "-", Plain) + "\n" +
                Colour(DriveScorer.FormatTime(scorer.DurationSeconds), Plain) + "\n" +
                Colour(scorer.DistanceMetres.ToString("F0") + " m", Plain) + "\n" +
                Colour(scorer.MeanSpeedMph.ToString("F1") + " mph", Plain) + "\n" +
                Colour(scorer.TopSpeedMph.ToString("F1") + " mph",
                       scorer.TopSpeedMph > scorer.speedLimitMph + 10f ? Bad :
                       scorer.TopSpeedMph > scorer.speedLimitMph ? Warn : Good);

        // ---- what it cost ------------------------------------------------------
        if (eventLabels != null)
            eventLabels.text =
                "Over the limit\n" +
                "Stop signs obeyed\n" +
                "Crossings yielded\n" +
                "Harsh braking\n" +
                "Harsh cornering\n" +
                "Pedestrian near miss\n" +
                "Collisions";

        if (eventValues != null)
            eventValues.text =
                Row(scorer.PercentOverLimit.ToString("F0") + "% of drive",
                    scorer.SpeedingDeduction) + "\n" +
                Ratio(scorer.StopSignsEncountered - scorer.StopSignViolations,
                      scorer.StopSignsEncountered, scorer.StopSignDeduction) + "\n" +
                Ratio(scorer.CrosswalksEncountered - scorer.YieldViolations,
                      scorer.CrosswalksEncountered, scorer.YieldDeduction) + "\n" +
                Row(scorer.HardBrakes + "  (" + scorer.BrakesPerMinute.ToString("F1") + "/min)",
                    scorer.BrakingDeduction) + "\n" +
                Row(scorer.HardCorners + "  (" + scorer.CornersPerMinute.ToString("F1") + "/min)",
                    scorer.CorneringDeduction) + "\n" +
                Row(scorer.NearMisses.ToString(), scorer.NearMissDeduction) + "\n" +
                Row(scorer.Collisions.ToString(), scorer.CollisionDeduction);
    }

    /// <summary>
    /// "4 / 5" with what it cost beside it, or a dash when the route had none of
    /// that kind. Showing 0 / 0 would read as a failure on a route with no stop
    /// signs on it.
    /// </summary>
    static string Ratio(int obeyed, int total, float deduction)
    {
        if (total == 0) return Colour("-", Plain);
        return Row(obeyed + " / " + total, deduction);
    }

    /// <summary>A value plus what it cost, coloured by how much that was.</summary>
    static string Row(string value, float deduction)
    {
        string colour = deduction >= 10f ? Bad : deduction >= 3f ? Warn : Good;
        string cost = deduction >= 0.5f ? "  <size=13>-" + deduction.ToString("F0") + "</size>" : "";
        return Colour(value + cost, colour);
    }

    static string Colour(string text, string hex)
    {
        return "<color=" + hex + ">" + text + "</color>";
    }

    static Color GradeColour(int score)
    {
        if (score >= 80) return new Color(0.44f, 0.81f, 0.55f);
        if (score >= 60) return new Color(1f, 0.78f, 0.27f);
        return new Color(1f, 0.42f, 0.34f);
    }

    // ------------------------------------------------------------------ buttons

    public void DriveAgain()
    {
        // Always restore the clock before loading, or the next scene starts frozen.
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMenu()
    {
        Time.timeScale = 1f;

        if (Application.CanStreamedLevelBeLoaded(menuSceneName))
        {
            SceneManager.LoadScene(menuSceneName);
        }
        else
        {
            Debug.LogError("[DebriefScreen] \"" + menuSceneName + "\" is not in the " +
                           "build settings. Run NCAT → Configure Build Scenes.");
        }
    }

    void OnDestroy()
    {
        // A scene reload with timeScale still at zero would leave the game frozen
        // with no way back.
        Time.timeScale = 1f;
    }
}
