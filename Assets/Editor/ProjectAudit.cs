using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Reports what is actually running in this scene, and what is only pretending to.
///
/// WHY THIS EXISTS
///
/// A Unity project does not tell you when a system is installed but idle. A component
/// sitting on an object looks identical in the Hierarchy whether it is doing its job
/// or quietly doing nothing because the data it depends on was never authored. This
/// project has several of those, and they are the single biggest reason it feels
/// disorganised: you cannot tell a working feature from a stranded one.
///
/// The clearest example: RoadBoundary is on the bus, and there are no RoadCorridor
/// objects anywhere in the scene. The component runs every physics step, finds nothing
/// to measure against, and keeps the bus inside nothing at all. Nothing anywhere says
/// so. The same is true of the pedestrian system, which has a spawner and no pavements
/// for anyone to walk on.
///
/// So this walks the scene and asks each system two questions: are you here, and do
/// you have what you need. Anything that answers yes and no is the real problem.
///
/// It also reports which of the nine NCDOT elements this route is capable of testing,
/// because that is what decides whether a drive can be passed at all. An element with
/// nothing in the scene to trigger it is never assessed, and a run that assesses five
/// of nine is scored on a fifth of the evidence the standard assumes.
///
/// Nothing here changes anything. It only looks.
/// </summary>
public class ProjectAudit : EditorWindow
{
    enum State { Live, Inert, Absent, Note }

    class Row
    {
        public string system;
        public string detail;
        public string remedy;
        public State state;
    }

    [MenuItem("NCAT/Project Audit", false, 0)]
    public static void Open()
    {
        GetWindow<ProjectAudit>("NCAT Audit").minSize = new Vector2(620f, 400f);
    }

    Vector2 scroll;
    List<Row> rows;
    string summary = "";

    void OnEnable() { Scan(); }

    void OnGUI()
    {
        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("What is actually running in this scene", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Rescan", GUILayout.Width(80f))) Scan();
        }

        if (!string.IsNullOrEmpty(summary))
            EditorGUILayout.HelpBox(summary, MessageType.Info);

        EditorGUILayout.Space(4f);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (rows != null)
        {
            string section = null;

            foreach (Row row in rows)
            {
                // A null detail marks a section heading rather than a system.
                if (row.detail == null)
                {
                    section = row.system;
                    EditorGUILayout.Space(8f);
                    GUILayout.Label(section, EditorStyles.boldLabel);
                    continue;
                }

                DrawRow(row);
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(4f);
    }

    void DrawRow(Row row)
    {
        Color tint = GUI.color;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.color = Colour(row.state);
                GUILayout.Label(Badge(row.state), GUILayout.Width(74f));
                GUI.color = tint;

                GUILayout.Label(row.system, EditorStyles.boldLabel, GUILayout.Width(190f));
                GUILayout.Label(row.detail, EditorStyles.wordWrappedLabel);
            }

            if (!string.IsNullOrEmpty(row.remedy))
            {
                GUI.color = new Color(0.78f, 0.82f, 0.88f);
                EditorGUILayout.LabelField("        " + row.remedy, EditorStyles.wordWrappedMiniLabel);
                GUI.color = tint;
            }
        }
    }

    static string Badge(State state)
    {
        switch (state)
        {
            case State.Live:   return "LIVE";
            case State.Inert:  return "INERT";
            case State.Absent: return "ABSENT";
            default:           return "NOTE";
        }
    }

    static Color Colour(State state)
    {
        switch (state)
        {
            case State.Live:   return new Color(0.45f, 0.90f, 0.55f);
            case State.Inert:  return new Color(1f, 0.45f, 0.40f);
            case State.Absent: return new Color(0.70f, 0.74f, 0.80f);
            default:           return new Color(1f, 0.80f, 0.30f);
        }
    }

    // ------------------------------------------------------------------ scanning

    void Head(string title)
    {
        rows.Add(new Row { system = title, detail = null });
    }

    void Add(string system, State state, string detail, string remedy = null)
    {
        rows.Add(new Row { system = system, state = state, detail = detail, remedy = remedy });
    }

    /// <summary>Everything of a type currently in the scene, inactive objects included.</summary>
    static T[] InScene<T>() where T : Object
    {
        return Object.FindObjectsOfType<T>(true);
    }

    void Scan()
    {
        rows = new List<Row>();

        int inert = 0;

        // ---------------------------------------------------------------- vehicle
        Head("Vehicle");

        VehicleController[] cars = InScene<VehicleController>();
        Add("VehicleController", cars.Length > 0 ? State.Live : State.Absent,
            cars.Length == 0 ? "No vehicle in the scene."
                             : cars.Length + " in the scene, governed to " +
                               cars[0].governedTopSpeedMph.ToString("F0") + " mph, " +
                               cars[0].maxLateralG.ToString("F2") + " g cornering limit.",
            cars.Length == 0 ? "NCAT -> Set Up Player Car" : null);

        Add("SmoothGroundDrive", InScene<SmoothGroundDrive>().Length > 0 ? State.Live : State.Absent,
            InScene<SmoothGroundDrive>().Length > 0
                ? "Keeping the bus on the terrain. Note this sets detectCollisions false, " +
                  "so physical colliders do not stop the bus; only geometric systems do."
                : "Absent. The bus will fall through or bounce on scanned terrain.");

        DriveTimer[] timers = InScene<DriveTimer>();
        Add("DriveTimer", timers.Length > 0 ? State.Live : State.Absent,
            timers.Length > 0 ? "Clock on the HUD, starts when the bus pulls away."
                              : "No drive clock.",
            timers.Length == 0 ? "NCAT -> Set Up Drive Timer" : null);

        // ------------------------------------------------------------------ route
        Head("Route");

        RouteWaypoint[] waypoints = InScene<RouteWaypoint>();
        var byKind = new Dictionary<RouteWaypoint.Kind, int>();

        foreach (RouteWaypoint w in waypoints)
        {
            if (!byKind.ContainsKey(w.kind)) byKind[w.kind] = 0;
            byKind[w.kind]++;
        }

        int checkpoints = Count(byKind, RouteWaypoint.Kind.Checkpoint);

        Add("Route waypoints", waypoints.Length > 0 ? State.Live : State.Absent,
            Describe(byKind, waypoints.Length),
            checkpoints < 20 && waypoints.Length > 0
                ? "Only " + checkpoints + " checkpoints. A route needs enough to describe " +
                  "where the road goes: NCAT -> Route -> Build Route From A Drive."
                : null);

        RouteRunner[] runners = InScene<RouteRunner>();
        Add("RouteRunner", runners.Length > 0 && checkpoints >= 2 ? State.Live
            : runners.Length > 0 ? State.Inert : State.Absent,
            runners.Length == 0 ? "No route runner, so nothing sequences the drive or ends it."
            : checkpoints < 2 ? "Present, but the route has fewer than two checkpoints, so it " +
                                "has nothing to send the driver to and the run can never complete."
            : "Sequencing " + waypoints.Length + " waypoints. Completing them all ends the run " +
              "and opens the debrief.");

        if (runners.Length > 0 && checkpoints < 2) inert++;

        RoutePathVisual[] paths = InScene<RoutePathVisual>();
        int drawn = 0;

        foreach (RoutePathVisual p in paths)
        {
            LineRenderer line = p.GetComponent<LineRenderer>();
            if (line != null) drawn += line.positionCount;
        }

        Add("RoutePathVisual", paths.Length == 0 ? State.Absent
            : drawn > 0 ? State.Live : State.Inert,
            paths.Length == 0 ? "No painted path on the ground."
            : drawn > 0 ? "Painting " + drawn + " points along the ground."
                        : "Present, but the line has no points, so nothing is drawn.",
            paths.Length > 0 && drawn == 0 ? "NCAT -> Route -> Draw Path On Ground" : null);

        if (paths.Length > 0 && drawn == 0) inert++;

        // ------------------------------------------------------------------- road
        Head("Road and boundaries");

        int corridors = InScene<RoadCorridor>().Length;
        RoadBoundary[] boundaries = InScene<RoadBoundary>();

        Add("RoadCorridor", corridors > 0 ? State.Live : State.Absent,
            corridors > 0 ? corridors + " corridor(s) describing where the road is."
                          : "None in the scene. Nothing describes where the road is.",
            corridors == 0 ? "NCAT -> Road -> 1. Build Corridor From Route" : null);

        bool boundaryInert = boundaries.Length > 0 && corridors == 0;

        Add("RoadBoundary", boundaries.Length == 0 ? State.Absent
            : boundaryInert ? State.Inert : State.Live,
            boundaries.Length == 0 ? "The bus is not kept on the road by anything."
            : boundaryInert
                ? "ON THE BUS BUT DOING NOTHING. It measures against RoadCorridor objects " +
                  "and there are none, so it runs every physics step and keeps the bus " +
                  "inside nothing."
                : "Keeping the bus inside " + corridors + " corridor(s).",
            boundaryInert ? "Build a corridor first: NCAT -> Road -> 1. Build Corridor From Route" : null);

        if (boundaryInert) inert++;

        int constraints = InScene<BusRoadConstraint>().Length;
        int generators = InScene<RoadCorridorGenerator>().Length;
        int overlapping = (boundaries.Length > 0 ? 1 : 0) + (constraints > 0 ? 1 : 0) +
                          (generators > 0 ? 1 : 0);

        if (overlapping > 1)
        {
            Add("Overlapping road systems", State.Note,
                overlapping + " different systems in this scene all try to answer \"where is " +
                "the road\": " +
                (boundaries.Length > 0 ? "RoadBoundary " : "") +
                (constraints > 0 ? "BusRoadConstraint " : "") +
                (generators > 0 ? "RoadCorridorGenerator " : "") +
                ". They were written at different times and do not know about each other.",
                "Pick one and remove the others. Two of them fighting is why the boundary " +
                "has behaved unpredictably.");
        }

        Add("CampusBoundary", InScene<CampusBoundary>().Length > 0 ? State.Live : State.Absent,
            InScene<CampusBoundary>().Length > 0
                ? "Outer limit on how far the bus may drive from campus."
                : "No outer limit: the bus can drive until the tiles run out.");

        // ------------------------------------------------------------ pedestrians
        Head("Pedestrians");

        PedestrianSpawner[] spawners = InScene<PedestrianSpawner>();
        int sidewalks = InScene<SidewalkPath>().Length;
        int crossings = InScene<SidewalkCrossing>().Length;

        bool pedestriansOn = spawners.Length > 0 && spawners[0].enabled &&
                             spawners[0].population > 0;
        bool wantsSidewalks = spawners.Length > 0 && spawners[0].useSidewalks;

        Add("PedestrianSpawner", spawners.Length == 0 ? State.Absent
            : pedestriansOn ? State.Live : State.Inert,
            spawners.Length == 0 ? "No pedestrians at all."
            : !pedestriansOn ? "Present but switched off (population " +
                               spawners[0].population + ", enabled " + spawners[0].enabled + ")."
            : "Spawning up to " + spawners[0].population + " pedestrians.");

        Add("SidewalkPath", sidewalks > 0 ? State.Live : State.Absent,
            sidewalks > 0 ? sidewalks + " pavement path(s), " + crossings + " crossing(s)."
                          : "No pavements authored. With none, pedestrians have nowhere to " +
                            "walk but the road, which is why they wander into traffic.",
            sidewalks == 0 && wantsSidewalks
                ? "Draw some: NCAT -> Sidewalk -> 1. Start New Path, then Alt+P to add points."
                : null);

        if (spawners.Length > 0 && !pedestriansOn) inert++;

        // --------------------------------------------------------------- scoring
        Head("Scoring");

        Add("DriveScorer", InScene<DriveScorer>().Length > 0 ? State.Live : State.Absent,
            InScene<DriveScorer>().Length > 0 ? "Measuring the drive." : "Nothing measures the drive.");

        Add("NcRubric", InScene<NcRubric>().Length > 0 ? State.Live : State.Absent,
            InScene<NcRubric>().Length > 0 ? "Judging against the nine NCDOT elements."
                                           : "Nothing judges the drive.");

        Add("DebriefScreen", InScene<DebriefScreen>().Length > 0 ? State.Live : State.Absent,
            InScene<DebriefScreen>().Length > 0
                ? "Shows the result when the route completes."
                : "No results screen, so finishing the route shows nothing.",
            InScene<DebriefScreen>().Length == 0 ? "NCAT -> Set Up Drive Debrief" : null);

        Add("SignJudge", InScene<SignJudge>().Length > 0 ? State.Live : State.Absent,
            InScene<SignJudge>().Length > 0
                ? "Scores signs out of order. Stands down while RouteRunner is active, so " +
                  "nothing is counted twice."
                : "Signs are only scored in route order.");

        // -------------------------------------------------- what can be assessed
        Head("Can this route be passed?");

        AssessElements(byKind, spawners.Length > 0 && pedestriansOn);

        // ---------------------------------------------------------------- summary
        summary = inert == 0
            ? "Nothing stranded. Every system in this scene has what it needs."
            : inert + " system(s) are installed but doing nothing. They are marked INERT below. " +
              "Each one looks identical to a working feature in the Hierarchy, which is why " +
              "the project feels disorganised.";

        Repaint();
    }

    static int Count(Dictionary<RouteWaypoint.Kind, int> byKind, RouteWaypoint.Kind kind)
    {
        int n;
        return byKind.TryGetValue(kind, out n) ? n : 0;
    }

    static string Describe(Dictionary<RouteWaypoint.Kind, int> byKind, int total)
    {
        if (total == 0) return "None in the scene.";

        StringBuilder sb = new StringBuilder();
        sb.Append(total).Append(" total: ");

        bool first = true;

        foreach (var pair in byKind)
        {
            if (!first) sb.Append(", ");
            sb.Append(pair.Value).Append(" ").Append(pair.Key);
            first = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Which of the nine elements this scene can actually test.
    ///
    /// This is the part that decides whether a drive is passable. An element with
    /// nothing in the scene to trigger it records no opportunities, so it is reported
    /// as not assessed rather than as a pass, and the overall result is averaged over
    /// whatever is left. Five of nine assessed is a result drawn from a fifth of the
    /// evidence the real standard assumes.
    /// </summary>
    void AssessElements(Dictionary<RouteWaypoint.Kind, int> byKind, bool pedestrians)
    {
        int stopSigns = Count(byKind, RouteWaypoint.Kind.StopSign);
        int signals = Count(byKind, RouteWaypoint.Kind.TrafficSignal);
        int crosswalks = Count(byKind, RouteWaypoint.Kind.Crosswalk);
        int busStops = Count(byKind, RouteWaypoint.Kind.BusStop);
        int junctions = Count(byKind, RouteWaypoint.Kind.Intersection);

        DriveScorer scorer = Object.FindObjectOfType<DriveScorer>();
        bool smoothness = scorer != null && scorer.scoreStopSmoothness;

        int can = 0;

        can += Element("Stop signs", stopSigns > 0,
                       stopSigns + " stop sign(s) on the route.",
                       "Place some: NCAT -> Place Stop Sign Here.");

        can += Element("Traffic signals", signals > 0,
                       signals + " signal(s) on the route.",
                       "Place one: NCAT -> Place Traffic Light Here.");

        can += Element("Yielding to pedestrians", pedestrians || crosswalks > 0,
                       "Pedestrians or crossings are present.",
                       "CRITICAL element, currently never tested. Turn pedestrians on, or " +
                       "place crossings: NCAT -> Place Crosswalk Here.");

        can += Element("Safe speed", true, "Measured continuously throughout every drive.", null);

        can += Element("Stopping", busStops > 0 || smoothness,
                       busStops > 0 ? busStops + " bus stop(s) on the route."
                                    : "Stop smoothness is being scored.",
                       "Nothing tests it. Either place bus stops, or switch Score Stop " +
                       "Smoothness back on in DriveScorer.");

        can += Element("Starting", true, "Measured on every pull-away.", null);

        can += Element("Approaching intersections", junctions > 0,
                       junctions + " intersection waypoint(s).",
                       "Place some: NCAT -> Place Intersection Here.");

        can += Element("Turning", true, "Measured on every turn.", null);

        can += Element("Turn signals and horn", true, "Measured on every turn.", null);

        Add("Elements assessable", can >= 9 ? State.Live : can >= 7 ? State.Note : State.Inert,
            can + " of 9 can be tested by this scene.",
            can >= 9 ? null
                     : "An element nothing can trigger is never assessed, and the result is " +
                       "averaged over the rest. Below " + 5 + " assessed the run is stamped " +
                       "INCOMPLETE whatever was driven. Every recorded run so far has failed, " +
                       "and this is the likeliest reason.");
    }

    int Element(string name, bool possible, string yes, string no)
    {
        Add(name, possible ? State.Live : State.Inert,
            possible ? yes : "Nothing in this scene can trigger it, so it is never assessed.",
            possible ? null : no);

        return possible ? 1 : 0;
    }
}
