using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Lays out a route by driving it.
///
/// You cannot reliably author a route on photogrammetry terrain from the editor
/// view. What looks like a road may be a scanned kerb, a planter, or a surface the
/// bus physically cannot climb; the only way to know a waypoint is reachable is to
/// have reached it. So the route is recorded from the driver's seat.
///
/// Waypoints dropped during Play are ordinary runtime data and vanish when Play
/// stops, so they are written to a JSON file instead. NCAT → Import Recorded Route
/// then builds them as real GameObjects in the scene.
///
/// CONTROLS WHILE RECORDING
///
///   B  /  D-pad Right    drop a waypoint here
///   N  /  D-pad Left     change what the next one will be
///   Backspace / D-pad Down   remove the last one
///   K                    save now (it also saves automatically when Play stops)
/// </summary>
public class RouteRecorder : MonoBehaviour
{
    [Header("Recording")]
    [Tooltip("Turn this off for normal play. NCAT → Arm Route Recording turns it on.")]
    public bool recording = true;

    [Tooltip("Folder for the route files, relative to the project folder.")]
    public string outputFolder = "Routes";

    [Header("Defaults for new waypoints")]
    public float defaultRadius = 12f;
    public float defaultSpeedLimitMph = 20f;

    [Header("Keys")]
    public KeyCode dropKey = KeyCode.B;
    public KeyCode cycleKey = KeyCode.N;
    public KeyCode undoKey = KeyCode.Backspace;
    public KeyCode saveKey = KeyCode.K;

    [Header("UI")]
    public Text statusLabel;

    struct Recorded
    {
        public Vector3 position;
        public RouteWaypoint.Kind kind;
        public float radius;
        public float speedLimitMph;
    }

    readonly List<Recorded> points = new List<Recorded>();
    RouteWaypoint.Kind nextKind = RouteWaypoint.Kind.Checkpoint;
    string savedPath;

    void Start()
    {
        if (!recording)
        {
            if (statusLabel != null) statusLabel.enabled = false;
            enabled = false;
            return;
        }

        Debug.Log("[RouteRecorder] Recording armed.\n" +
                  "  B drops a waypoint, N changes its kind, Backspace undoes.\n" +
                  "  Saves automatically when you stop playing.");

        UpdateStatus();
    }

    void Update()
    {
        if (!recording) return;

        bool drop = Input.GetKeyDown(dropKey);
        bool cycle = Input.GetKeyDown(cycleKey);
        bool undo = Input.GetKeyDown(undoKey);
        bool save = Input.GetKeyDown(saveKey);

#if ENABLE_INPUT_SYSTEM
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            // D-pad Up is already the camera toggle, so the other three are used.
            if (pad.dpad.right.wasPressedThisFrame) drop = true;
            if (pad.dpad.left.wasPressedThisFrame) cycle = true;
            if (pad.dpad.down.wasPressedThisFrame) undo = true;
        }
#endif

        if (drop) Drop();
        if (cycle) Cycle();
        if (undo) Undo();
        if (save) Save();
    }

    void Drop()
    {
        Recorded point = new Recorded
        {
            position = transform.position,
            kind = nextKind,
            radius = defaultRadius,
            speedLimitMph = defaultSpeedLimitMph,
        };

        points.Add(point);

        Debug.Log("[RouteRecorder] " + points.Count + ": " + nextKind + " at " +
                  point.position.ToString("F1"));

        // A finish point ends the route, so go back to plain checkpoints rather
        // than dropping a second finish by accident.
        if (nextKind == RouteWaypoint.Kind.Finish) nextKind = RouteWaypoint.Kind.Checkpoint;

        UpdateStatus();
    }

    void Cycle()
    {
        int count = System.Enum.GetValues(typeof(RouteWaypoint.Kind)).Length;
        nextKind = (RouteWaypoint.Kind)(((int)nextKind + 1) % count);
        UpdateStatus();
    }

    void Undo()
    {
        if (points.Count == 0) return;

        points.RemoveAt(points.Count - 1);
        Debug.Log("[RouteRecorder] Removed the last waypoint. " + points.Count + " left.");
        UpdateStatus();
    }

    void UpdateStatus()
    {
        if (statusLabel == null) return;

        statusLabel.enabled = true;
        statusLabel.text =
            "REC  " + points.Count + " waypoint" + (points.Count == 1 ? "" : "s") +
            "     next: " + nextKind.ToString().ToUpperInvariant() +
            "     [B] drop   [N] kind   [Backspace] undo";
    }

    // -------------------------------------------------------------------- save

    void OnDisable()
    {
        // Stopping Play is the normal way a recording session ends, so this is the
        // save that actually matters. Without it, everything just dropped is lost.
        Save();
    }

    public void Save()
    {
        if (points.Count == 0) return;
        if (!string.IsNullOrEmpty(savedPath) && !Application.isPlaying) return;

        try
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string folder = Path.Combine(root, outputFolder);
            Directory.CreateDirectory(folder);

            string stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            savedPath = Path.Combine(folder, "route_" + stamp + ".json");

            File.WriteAllText(savedPath, ToJson());

            Debug.Log("[RouteRecorder] Saved " + points.Count + " waypoint(s) to:\n" +
                      savedPath + "\n" +
                      "Now run NCAT → Import Recorded Route to build them in the scene.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[RouteRecorder] Could not save the route: " + e.Message);
        }
    }

    /// <summary>
    /// Hand-written rather than via JsonUtility, because a plain array at the top
    /// level is easier to read and to edit by hand than the wrapper object
    /// JsonUtility insists on.
    /// </summary>
    string ToJson()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"waypoints\": [");

        for (int i = 0; i < points.Count; i++)
        {
            Recorded p = points[i];

            sb.Append("    { ");
            sb.Append("\"kind\": \"" + p.kind + "\", ");
            sb.Append("\"x\": " + F(p.position.x) + ", ");
            sb.Append("\"y\": " + F(p.position.y) + ", ");
            sb.Append("\"z\": " + F(p.position.z) + ", ");
            sb.Append("\"radius\": " + F(p.radius) + ", ");
            sb.Append("\"speedLimitMph\": " + F(p.speedLimitMph));
            sb.Append(" }");
            sb.AppendLine(i < points.Count - 1 ? "," : "");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string F(float value)
    {
        // InvariantCulture, or a machine set to comma decimals writes invalid JSON.
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }
}
