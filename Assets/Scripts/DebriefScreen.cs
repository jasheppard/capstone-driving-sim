using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The post-drive report, as a road test result rather than a score out of a
/// hundred.
///
/// A training simulator teaches in the debrief, not during the run. What it shows
/// now is what a real examiner's sheet shows: every element of the test, whether it
/// was passed, and a stamp at the bottom. That is deliberately not a game score. A
/// number we invented cannot be compared to anything outside this project, and the
/// whole reason for grading against the North Carolina elements is that the result
/// means the same thing here as it does at a licence office.
///
/// Press Escape, or Options on the controller, to end the drive and see it.
///
/// Ending is deliberate rather than resumable: FinishDrive freezes the statistics
/// and flushes the telemetry file, so one drive maps to exactly one result and one
/// CSV. Resuming afterwards would produce a session whose data stops halfway.
/// </summary>
public class DebriefScreen : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("The report itself. Hidden until the drive ends.")]
    public GameObject panel;

    [Header("Heading")]
    public Text driverLine;

    [Header("Result")]
    public Text stampText;
    public Text proficiencyText;
    public Text passedText;
    public Text assessedText;
    public Text verdictText;
    public Text governorNote;

    [Header("Drive summary")]
    public Text summaryLabels;
    public Text summaryValues;

    [Header("The nine elements")]
    [Tooltip("Three parallel columns, one line per element, in enum order.")]
    public Text elementNumbers;
    public Text elementNames;
    public Text elementResults;
    public Text elementScores;

    [Header("Buttons")]
    public Button driveAgainButton;
    public Button menuButton;

    [Header("Settings")]
    public KeyCode endDriveKey = KeyCode.Escape;
    public string menuSceneName = "MainMenu";

    [Tooltip("Minimum seconds of driving before a result means anything. A five " +
             "second drive has tested nothing.")]
    public float minimumDriveSeconds = 20f;

    static readonly string Good = "#6FCF8B";
    static readonly string Warn = "#FFC845";
    static readonly string Bad = "#FF6B57";
    static readonly string Plain = "#E0E7F0";
    static readonly string Faint = "#6E7A8A";

    DriveScorer scorer;
    NcRubric rubric;
    DriveTimer timer;
    RouteRunner route;
    VehicleController vehicle;
    bool shown;

    void Start()
    {
        scorer = FindObjectOfType<DriveScorer>();
        rubric = FindObjectOfType<NcRubric>();
        timer = FindObjectOfType<DriveTimer>();
        route = FindObjectOfType<RouteRunner>();      // optional, free driving has none
        vehicle = FindObjectOfType<VehicleController>();

        if (scorer == null)
            Debug.LogError("[DebriefScreen] No DriveScorer in the scene: " +
                           "run NCAT > Set Up Drive Debrief.");

        if (rubric == null)
            Debug.LogError("[DebriefScreen] No NcRubric in the scene: " +
                           "run NCAT > Set Up Road Test.");

        if (Overlay() != null) Overlay().SetActive(false);

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
                      "s driven: drive for at least " + minimumDriveSeconds +
                      "s before ending, or nothing has been tested.");
            return;
        }

        shown = true;
        scorer.FinishDrive();       // this also calls Finish on the rubric

        // Freeze the clock here rather than letting it notice next frame: the
        // debrief sets timeScale to zero, and the number on this screen should be
        // the one the driver last saw on the HUD.
        if (timer != null) timer.Finish();

        // Printed whether the route finished or not, because "I got back to the start
        // and nothing happened" is otherwise impossible to tell apart from "the run
        // ended normally".
        RouteRunner runner = FindObjectOfType<RouteRunner>();
        if (runner != null) Debug.Log("[Route] " + runner.Progress());

        Populate();

        if (Overlay() != null) Overlay().SetActive(true);

        // Paused, so the world behind the report is frozen rather than carrying on
        // without a driver. Everything below must use unscaled time.
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    /// <summary>
    /// How long the drive took.
    ///
    /// The drive clock if there is one, because that is time spent actually driving:
    /// it starts when the bus pulls away rather than when the scene loads, so it does
    /// not charge the driver for the briefing, for Cesium streaming, or for however
    /// long they sat deciding where to go. DriveScorer's own duration runs from the
    /// first frame and is still the right basis for the per-minute rates, which is
    /// why both exist.
    /// </summary>
    string DriveTime()
    {
        if (timer != null && timer.Running) return timer.Format(timer.ElapsedSeconds);
        return DriveScorer.FormatTime(scorer.DurationSeconds);
    }

    void Populate()
    {
        if (driverLine != null)
            driverLine.text = DriverIdentity.Name + "   ·   " +
                              System.DateTime.Now.ToString("d MMM yyyy") + "   ·   " +
                              DriveTime();

        PopulateResult();
        PopulateSummary();
        PopulateElements();
    }

    // ------------------------------------------------------------------- result

    void PopulateResult()
    {
        if (rubric == null) return;

        bool passed = rubric.Stamp == "PASS";
        Color colour = passed ? new Color(0.44f, 0.81f, 0.55f)
                     : rubric.Stamp == "INCOMPLETE" ? new Color(1f, 0.78f, 0.27f)
                     : new Color(1f, 0.42f, 0.34f);

        if (stampText != null)
        {
            stampText.text = rubric.Stamp;
            stampText.color = colour;
        }

        if (proficiencyText != null)
        {
            proficiencyText.text = rubric.OverallProficiency.ToString("F0") + "%";
            proficiencyText.color = colour;
        }

        if (passedText != null)
            passedText.text = rubric.PassedCount + " of " + rubric.AssessedCount +
                              " elements passed";

        // Stated plainly, because it is the caveat that makes the rest honest. A
        // result computed over six elements is not a result over nine, and
        // anyone quoting a pass rate from this data needs to see that.
        if (assessedText != null)
            assessedText.text = rubric.AssessedCount + " of 9 assessed this run";

        if (verdictText != null) verdictText.text = rubric.Verdict;

        if (governorNote != null)
        {
            bool governed = vehicle != null && vehicle.imposedSpeedCapMph > 0f;

            governorNote.text = governed
                ? "Speed limited to " + vehicle.imposedSpeedCapMph.ToString("F0") +
                  " mph after " + rubric.TotalViolations + " faults."
                : "";
        }
    }

    void PopulateSummary()
    {
        bool onRoute = route != null && route.WaypointCount > 0;

        if (summaryLabels != null)
            // No passenger count. It is left over from the shuttle-route idea and
            // says nothing about how the person drove, which is all this report is
            // for.
            summaryLabels.text =
                "Route\n" +
                "Distance\n" +
                "Average speed\n" +
                "Top speed\n" +
                "Faults";

        if (summaryValues == null) return;

        summaryValues.text =
            Colour(onRoute
                       ? (route.RouteComplete
                              ? "Completed"
                              : route.WaypointsReached + " of " + route.WaypointCount + " stops")
                       : "Free drive",
                   !onRoute ? Plain : route.RouteComplete ? Good : Warn) + "\n" +
            Colour(scorer.DistanceMetres.ToString("F0") + " m", Plain) + "\n" +
            Colour(scorer.MeanSpeedMph.ToString("F1") + " mph", Plain) + "\n" +
            // Amber once past the sign, red only once past what is tolerated, so the
            // colour agrees with what was actually scored.
            Colour(scorer.TopSpeedMph.ToString("F1") + " mph",
                   scorer.TopSpeedMph > scorer.ToleratedSpeedMph ? Bad :
                   scorer.TopSpeedMph > scorer.speedLimitMph ? Warn : Good) + "\n" +
            Colour(rubric != null ? rubric.TotalViolations.ToString() : "-",
                   rubric == null ? Plain :
                   rubric.TotalViolations == 0 ? Good :
                   rubric.TotalViolations < 5 ? Warn : Bad);
    }

    // ----------------------------------------------------------------- elements

    /// <summary>
    /// One line per element in four parallel columns.
    ///
    /// Four rects rather than one, because the number, the name, the verdict and the
    /// percentage each need their own alignment. Packing them into a single string
    /// is what made the double-digit rows step sideways and the "not assessed"
    /// markers collide with the longer names.
    /// </summary>
    void PopulateElements()
    {
        if (rubric == null) return;

        string numbers = "", names = "", results = "", scores = "";
        int line = 0;

        foreach (NcElement element in System.Enum.GetValues(typeof(NcElement)))
        {
            ElementResult r = rubric.Result(element);

            string rowColour = !r.Assessed ? Faint : r.Passed ? Plain : Plain;
            string verdictColour = !r.Assessed ? Faint : r.Passed ? Good : Bad;

            numbers += Colour((line + 1) + ".", !r.Assessed ? Faint : "#9EADBF");
            names += Colour(NcRubric.Label(element), !r.Assessed ? Faint : rowColour);

            if (!r.Assessed)
            {
                results += "";
                scores += Colour("not assessed", Faint);
            }
            else if (r.CriticalFailure)
            {
                results += Colour("FAIL", Bad);
                scores += Colour("critical", Bad);
            }
            else
            {
                results += Colour(r.Passed ? "PASS" : "FAIL", verdictColour);
                scores += Colour((r.Proficiency * 100f).ToString("F0") + "%", verdictColour);
            }

            line++;

            // Length, not Length - 1. Getting this wrong drops the newline before the
            // last row, so the ninth element is drawn on the same line as the eighth
            // in every column at once: "TurningTurn signals and horn", "FAILFAIL".
            if (line < System.Enum.GetValues(typeof(NcElement)).Length)
            {
                numbers += "\n"; names += "\n"; results += "\n"; scores += "\n";
            }
        }

        if (elementNumbers != null) elementNumbers.text = numbers;
        if (elementNames != null) elementNames.text = names;
        if (elementResults != null) elementResults.text = results;
        if (elementScores != null) elementScores.text = scores;
    }

    static string Colour(string text, string hex)
    {
        return "<color=" + hex + ">" + text + "</color>";
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
                           "build settings. Run NCAT > Configure Build Scenes.");
        }
    }

    /// <summary>
    /// What actually gets shown and hidden: the whole overlay, not just the panel.
    ///
    /// The setup used to wire `panel` to the inner panel, whose parent is a
    /// full-screen near-opaque Dim image. Hiding only the panel left that Dim
    /// sitting over the game, and two of them stacked passed under 2% of the light
    /// through, which looked like the whole game had lost brightness and contrast.
    ///
    /// Stepping up one level when the parent is not the Canvas itself repairs scenes
    /// that were already built, without anyone re-running the setup.
    /// </summary>
    GameObject Overlay()
    {
        if (panel == null) return null;

        Transform parent = panel.transform.parent;
        if (parent != null && parent.GetComponent<Canvas>() == null) return parent.gameObject;

        return panel;
    }

    void OnDestroy()
    {
        // A scene reload with timeScale still at zero would leave the game frozen
        // with no way back.
        Time.timeScale = 1f;
    }
}
