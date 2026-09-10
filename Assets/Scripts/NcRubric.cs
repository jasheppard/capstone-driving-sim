using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// The nine elements of the North Carolina road skills test that this simulator
/// measures.
///
/// WHY NINE AND NOT SIXTEEN
///
/// The full NCDOT skills list has more on it than this. Lane keeping and lane changes
/// need a hand-authored lane graph, because Google photogrammetry carries no road
/// network. Following another vehicle, passing, and yielding to vehicles need traffic
/// that does not exist. Backing and the three point turnabout need manoeuvres the
/// vehicle cannot perform.
///
/// Those seven were carried for a while as rows that always read "not assessed",
/// which was honest but was also seven sixteenths of the report saying nothing. They
/// are out of scope instead. The scope belongs in the writeup, not on the screen
/// every run.
///
/// Nothing about the standard changed with them: these nine are scored exactly as
/// they were, and a result is still a result against the published NCDOT elements
/// rather than against a scale we invented.
/// </summary>
public enum NcElement
{
    StopSigns = 0,
    YieldToPedestrians = 1,
    SafeSpeed = 2,
    Stopping = 3,
    Starting = 4,
    IntersectionApproach = 5,
    TrafficSignals = 6,
    Turning = 7,
    SignalsAndHorn = 8,
}

/// <summary>
/// One element of the test, and how the driver did on it.
///
/// Everything is a ratio of failures to chances. That is the only form in which a
/// result is comparable between two drives, because a route with two stop signs
/// and a route with twenty are not the same test.
/// </summary>
public class ElementResult
{
    public NcElement Element;
    public int Opportunities;
    public int Violations;

    /// <summary>
    /// Set when something happened that fails the element outright, whatever the
    /// ratio would otherwise have said. Striking a pedestrian is the clearest case:
    /// yielding correctly at nine crossings out of ten does not make the tenth
    /// acceptable.
    /// </summary>
    public bool CriticalFailure;

    /// <summary>True once the driver has had at least one chance at this element.</summary>
    public bool Assessed { get { return Opportunities > 0; } }

    /// <summary>Fraction done correctly, 0 to 1. Unassessed elements report 0.</summary>
    public float Proficiency
    {
        get
        {
            if (CriticalFailure) return 0f;
            if (Opportunities <= 0) return 0f;
            return Mathf.Clamp01(1f - (float)Violations / Opportunities);
        }
    }

    /// <summary>
    /// How many errors this element tolerates before it is failed.
    ///
    /// A flat 80 percent looks fair and is not. One error out of eighteen chances is
    /// 94 percent and passes; one error out of four is 75 percent and fails, for the
    /// same single mistake. A human examiner does not work that way, and simulated
    /// driver profiles showed the effect clearly: careful drivers were failing
    /// elements they had made one small slip on, while sloppy drivers passed
    /// elements with many chances.
    ///
    /// So the tolerance is 20 percent of the chances, with a floor of one error once
    /// there have been at least three. Below three chances there is no tolerance,
    /// because a single error out of one or two is not a slip, it is most of the
    /// evidence there is.
    /// </summary>
    public int AllowedErrors
    {
        get { return Opportunities >= 3 ? Mathf.Max(1, Opportunities / 5) : 0; }
    }

    public bool Passed
    {
        get { return Assessed && !CriticalFailure && Violations <= AllowedErrors; }
    }

    /// <summary>
    /// Failed every single chance at this element.
    ///
    /// Kept apart from an ordinary failure because it means something different. One
    /// or two mistakes at a manoeuvre is a lapse; getting it wrong every time you
    /// were asked is not knowing how to do it, and no examiner licences that. Three
    /// chances are required before this can trigger, so one unlucky attempt cannot
    /// end a run.
    /// </summary>
    public bool TotalFailure
    {
        get { return Opportunities >= 3 && Violations >= Opportunities; }
    }
}

/// <summary>
/// Scores a drive against the North Carolina road skills test instead of against an
/// invented hundred point scale.
///
/// WHY THIS REPLACED THE 0 TO 100 SCORE
///
/// The old score was a rubric we made up. It behaved sensibly, but it could not be
/// compared to anything outside this project, which made it useless for the part of
/// the coursework that argues about real pass rates. A per element pass and fail
/// against the published NCDOT skills list can be compared, because that is the
/// same instrument a real examiner uses.
///
/// HOW A RESULT IS DECIDED
///
/// Each element is scored as a ratio of violations to opportunities. An element is
/// passed at 80 percent or better. The drive as a whole is passed when the mean
/// proficiency across the assessed elements reaches 80 percent AND no more than
/// five elements were failed.
///
/// WHAT NOT ASSESSED MEANS
///
/// An element the route never tested is reported as not assessed rather than as a
/// pass. Counting an untested element as a pass would let a driver clear the test
/// by driving a route with nothing on it, which is the single easiest way to make a
/// figure like this dishonest. The debrief states how many of the nine were
/// actually assessed, so any pass rate quoted from this data carries its own
/// caveat.
///
/// A route with no stop signs or signals on it will leave those elements untested,
/// so expect fewer than nine assessed until the campus has enough placed on it.
///
/// </summary>
public class NcRubric : MonoBehaviour
{
    /// <summary>
    /// 80 percent, matching the North Carolina cutoff. Used both for a single
    /// element and for the drive overall.
    /// </summary>
    public const float PassThreshold = 0.80f;

    [Header("Pass conditions")]
    [Tooltip("The most elements that may ever be failed while still passing, however " +
             "many were assessed. North Carolina allows five errors before failing.")]
    public int maximumFailedElements = 5;

    /// <summary>
    /// Elements that fail the whole test on their own.
    ///
    /// Every road test has acts that end it regardless of how the rest went, and
    /// these are the campus equivalents: rolling stop signs, running reds, and not
    /// yielding to people on foot. Without this, averaging quietly forgives them. A
    /// simulated driver who rolled three of four stop signs still came out at 88
    /// percent overall and passed, which is not a result anyone would defend.
    /// </summary>
    static readonly NcElement[] CriticalElements =
    {
        NcElement.StopSigns,
        NcElement.TrafficSignals,
        NcElement.YieldToPedestrians,
    };

    /// <summary>
    /// The failure allowance for this run, scaled to how much was actually tested.
    ///
    /// North Carolina's written cutoff is five errors out of twenty-five, which is
    /// 20 percent, not a flat five. Applying a flat five to a run that assessed nine
    /// elements would let a driver fail more than half of them and still pass.
    /// </summary>
    public int MaximumFailedForThisRun
    {
        get
        {
            return Mathf.Min(maximumFailedElements,
                             Mathf.Max(1, Mathf.RoundToInt(0.2f * AssessedCount)));
        }
    }

    [Tooltip("How many of the nine must have been tested for the result to count " +
             "as a real attempt rather than an incomplete one. Below this the stamp " +
             "reads INCOMPLETE, whatever the proficiency was.")]
    public int minimumAssessedElements = 5;

    [Header("Output")]
    public bool saveReport = true;
    public string outputFolder = "TelemetryData";

    [Tooltip("Append one row per drive to a shared file, so pass rates can be " +
             "computed across every driver who has used the simulator.")]
    public bool appendToCohortFile = true;

    // ------------------------------------------------------------------- results

    readonly Dictionary<NcElement, ElementResult> results =
        new Dictionary<NcElement, ElementResult>();

    public bool Finished { get; private set; }

    /// <summary>Mean proficiency across the assessed elements, 0 to 100.</summary>
    public float OverallProficiency { get; private set; }

    public int AssessedCount { get; private set; }
    public int PassedCount { get; private set; }
    public int FailedCount { get; private set; }

    /// <summary>PASS, FAIL or INCOMPLETE.</summary>
    public string Stamp { get; private set; }
    public string Verdict { get; private set; }

    /// <summary>
    /// Running total of violations, for the live penalty ticker on the HUD. The
    /// driver sees this climb as they drive rather than only learning at the end.
    /// </summary>
    public int TotalViolations { get; private set; }

    /// <summary>Raised whenever a violation is recorded, so the HUD can flash it.</summary>
    public System.Action<NcElement, string> OnViolation;

    /// <summary>
    /// Acts that end the test whatever else went well.
    ///
    /// A real examiner terminates the road test for a collision or any dangerous
    /// act, and no amount of correct driving before it earns the licence back. This
    /// is that rule. It is kept separate from the elements because it is not a
    /// question of proficiency at a maneuver, it is a disqualification.
    /// </summary>
    public readonly List<string> AutomaticFailures = new List<string>();

    public void RegisterAutomaticFail(string reason)
    {
        if (Finished || AutomaticFailures.Contains(reason)) return;

        AutomaticFailures.Add(reason);
        if (OnViolation != null) OnViolation(NcElement.SafeSpeed, "TEST ENDED: " + reason);
    }

    void Awake()
    {
        foreach (NcElement element in System.Enum.GetValues(typeof(NcElement)))
            results[element] = new ElementResult { Element = element };
    }

    // -------------------------------------------------------------- recording

    /// <summary>
    /// One chance at an element, and whether it was taken correctly.
    ///
    /// Called by RouteRunner as each waypoint is left behind, and by DriveScorer for
    /// the continuous elements. Opportunities are counted on leaving rather than on
    /// arriving, because a rolling stop only becomes a rolling stop once the vehicle
    /// is past the line.
    /// </summary>
    public void Register(NcElement element, bool violated, string detail = null)
    {
        if (Finished) return;

        ElementResult result = results[element];
        result.Opportunities++;

        if (!violated) return;

        result.Violations++;
        TotalViolations++;

        if (OnViolation != null) OnViolation(element, detail ?? Label(element));
    }

    /// <summary>
    /// Something that fails the element outright. Records an opportunity too, so an
    /// element can be failed critically the first time it is tested.
    /// </summary>
    public void RegisterCritical(NcElement element, string detail = null)
    {
        if (Finished) return;

        ElementResult result = results[element];
        result.Opportunities++;
        result.Violations++;
        result.CriticalFailure = true;
        TotalViolations++;

        if (OnViolation != null)
            OnViolation(element, detail ?? ("CRITICAL: " + Label(element)));
    }

    /// <summary>
    /// A continuous element judged once at the end rather than event by event.
    /// Safe speed is the example: there is no discrete moment to score, so the
    /// fraction of the drive spent within the limit becomes the proficiency
    /// directly.
    /// </summary>
    public void RegisterContinuous(NcElement element, float fractionCorrect, int samples)
    {
        if (Finished || samples <= 0) return;

        ElementResult result = results[element];
        result.Opportunities = samples;
        result.Violations = Mathf.RoundToInt(samples * (1f - Mathf.Clamp01(fractionCorrect)));
    }

    public ElementResult Result(NcElement element) { return results[element]; }

    // ----------------------------------------------------------------- finishing

    /// <summary>
    /// Freezes the result and decides pass or fail. Called by the debrief screen
    /// when the drive ends.
    /// </summary>
    public void Finish()
    {
        if (Finished) return;
        Finished = true;

        float total = 0f;
        AssessedCount = PassedCount = FailedCount = 0;

        string criticalFailed = null;
        string totallyFailed = null;

        foreach (ElementResult result in results.Values)
        {
            if (!result.Assessed) continue;

            AssessedCount++;
            total += result.Proficiency;

            if (result.Passed)
            {
                PassedCount++;
                continue;
            }

            FailedCount++;

            if (criticalFailed == null &&
                System.Array.IndexOf(CriticalElements, result.Element) >= 0)
                criticalFailed = Label(result.Element);

            if (totallyFailed == null && result.TotalFailure)
                totallyFailed = Label(result.Element);
        }

        OverallProficiency = AssessedCount > 0 ? 100f * total / AssessedCount : 0f;

        // The gates, in the order an examiner would apply them. A disqualifying act
        // outranks everything; a failed critical element outranks the average; and
        // the average is the last thing asked, not the first.
        if (AutomaticFailures.Count > 0)
        {
            Stamp = "FAIL";
            Verdict = "Automatic failure: " +
                      string.Join("; ", AutomaticFailures.ToArray()) + ".";
        }
        else if (AssessedCount < minimumAssessedElements)
        {
            Stamp = "INCOMPLETE";
            Verdict = "Only " + AssessedCount + " of 9 elements were tested. " +
                      "Drive a route that covers more of them.";
        }
        else if (criticalFailed != null)
        {
            Stamp = "FAIL";
            Verdict = "Failed a critical element: " + criticalFailed.ToLowerInvariant() + ".";
        }
        else if (totallyFailed != null)
        {
            Stamp = "FAIL";
            Verdict = "Got " + totallyFailed.ToLowerInvariant() +
                      " wrong on every attempt.";
        }
        else if (FailedCount > MaximumFailedForThisRun)
        {
            Stamp = "FAIL";
            Verdict = FailedCount + " elements failed. More than " +
                      MaximumFailedForThisRun + " out of " + AssessedCount +
                      " tested is an automatic fail.";
        }
        else if (OverallProficiency < PassThreshold * 100f)
        {
            Stamp = "FAIL";
            Verdict = "Overall proficiency " + OverallProficiency.ToString("F0") +
                      " percent, below the 80 percent standard.";
        }
        else
        {
            Stamp = "PASS";
            Verdict = "Met the standard on " + PassedCount + " of " +
                      AssessedCount + " elements tested.";
        }

        if (saveReport) WriteReport();
        if (appendToCohortFile) AppendCohortRow();
    }

    // -------------------------------------------------------------------- output

    string Folder()
    {
        // Per driver, so one person's runs are not mixed in with another's. The
        // coursework needs results grouped by who produced them.
        string root = Path.GetDirectoryName(Application.dataPath);
        string folder = Path.Combine(Path.Combine(root, outputFolder),
                                     DriverIdentity.FileSafeName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    void WriteReport()
    {
        try
        {
            string stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(Folder(),
                                       "rubric_" + DriverIdentity.FileSafeName + "_" + stamp + ".csv");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("driver,element,opportunities,violations,proficiency_pct,assessed,passed,critical");

            foreach (NcElement element in System.Enum.GetValues(typeof(NcElement)))
            {
                ElementResult r = results[element];
                sb.AppendLine(string.Join(",", new string[]
                {
                    DriverIdentity.FileSafeName,
                    Label(element).Replace(",", ""),
                    r.Opportunities.ToString(),
                    r.Violations.ToString(),
                    r.Assessed ? (r.Proficiency * 100f).ToString("F1") : "",
                    r.Assessed ? "1" : "0",
                    r.Assessed ? (r.Passed ? "1" : "0") : "",
                    r.CriticalFailure ? "1" : "0",
                }));
            }

            File.WriteAllText(path, sb.ToString());
            Debug.Log("[NcRubric] " + Stamp + " at " + OverallProficiency.ToString("F0") +
                      " percent. Saved to " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[NcRubric] Could not save the rubric sheet: " + e.Message);
        }
    }

    /// <summary>
    /// One line per drive in a single shared file. This is what the pass rate is
    /// computed from, and it is the only file that has to be read to compare a
    /// cohort against the published North Carolina figures.
    /// </summary>
    void AppendCohortRow()
    {
        try
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string folder = Path.Combine(root, outputFolder);
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, "cohort_results.csv");
            bool fresh = !File.Exists(path);

            StringBuilder sb = new StringBuilder();
            if (fresh)
                sb.AppendLine("driver,timestamp,result,overall_pct,assessed,passed,failed,violations");

            sb.AppendLine(string.Join(",", new string[]
            {
                DriverIdentity.FileSafeName,
                System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Stamp,
                OverallProficiency.ToString("F1"),
                AssessedCount.ToString(),
                PassedCount.ToString(),
                FailedCount.ToString(),
                TotalViolations.ToString(),
            }));

            File.AppendAllText(path, sb.ToString());
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[NcRubric] Could not append to the cohort file: " + e.Message);
        }
    }

    // -------------------------------------------------------------------- labels

    /// <summary>
    /// The wording NCDOT uses, so the briefing, the debrief and the report all name
    /// an element the same way the real test does.
    /// </summary>
    public static string Label(NcElement element)
    {
        switch (element)
        {
            case NcElement.StopSigns: return "Stop signs";
            case NcElement.YieldToPedestrians: return "Yielding to pedestrians";
            case NcElement.SafeSpeed: return "Safe speed for conditions";
            case NcElement.Stopping: return "Stopping";
            case NcElement.Starting: return "Starting";
            case NcElement.IntersectionApproach: return "Approaching intersections";
            case NcElement.TrafficSignals: return "Traffic signals";
            case NcElement.Turning: return "Turning";
            case NcElement.SignalsAndHorn: return "Turn signals and horn";
        }
        return element.ToString();
    }
}
