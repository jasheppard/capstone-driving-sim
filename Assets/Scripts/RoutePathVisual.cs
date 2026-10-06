using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Paints the route on the ground as a line the driver can follow.
///
/// An arrow says which way to go. A line on the road says exactly where, which is
/// the difference between "somewhere over there" and "this lane, round this side of
/// the island". On a campus with no lane markings in the scan, it is the only thing
/// that tells a driver which way round a bend the route goes.
///
/// HOW IT FOLLOWS THE GROUND
///
/// The checkpoints are metres apart and the ground between them is not flat, so a
/// straight line between two of them sinks into every dip and floats over every
/// crest. Instead the route is smoothed into a curve, resampled every couple of
/// metres, and each sample is dropped onto the terrain by raycast, then lifted a
/// few centimetres so it draws on top of the road rather than inside it.
///
/// The raycast has the same problem the vehicle does: from above, the first thing it
/// meets under a tree is the canopy. So each sample only accepts a surface within a
/// few metres of where the previous sample landed. The height of the line therefore
/// walks along the road rather than being judged afresh at every point, which is what
/// keeps it under trees and out of first-floor windows.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class RoutePathVisual : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The Route object. Left empty, an object named \"Route\" is used.")]
    public Transform route;

    [Header("Shape")]
    [Tooltip("Distance between points along the drawn line, in metres. Smaller " +
             "hugs the ground more closely and costs more points.")]
    public float spacing = 2f;

    [Tooltip("Height above the ground, in metres. Too low and the road pokes " +
             "through the line in places; too high and it floats visibly. A third " +
             "of a metre is about the most that still reads as painted on.")]
    public float height = 0.35f;

    [Tooltip("Width of the line, in metres. About a third of a lane reads as a " +
             "guide rather than as paint.")]
    public float width = 1.2f;

    [Tooltip("How far above the previous sample a surface may be and still count as " +
             "the road, in metres. This is what rejects tree canopy. Raise it only if " +
             "the route climbs something genuinely steep.")]
    public float verticalTolerance = 4f;

    [Header("Look")]
    [Tooltip("Material for the line. The editor tool assigns a saved asset here. " +
             "Left empty, one is built at run time, which works in Play mode but " +
             "saves as nothing in the scene file, and an unsaved material draws as " +
             "magenta or not at all.")]
    public Material material;

    [Tooltip("Colour of the line. Alpha below 1 needs a material that blends, which " +
             "the saved asset does.")]
    public Color colour = new Color(1f, 0.85f, 0.2f, 0.9f);

    [Header("Keeping it on the surface")]
    [Tooltip("Re-measure the ground under the line as you drive. Leave this on.\n\n" +
             "Cesium swaps each tile between levels of detail depending on how close " +
             "the camera is, so the terrain you drive over is not the same mesh that " +
             "was measured when the line was baked. A coarser tile sits above the " +
             "finer one it replaced, which is why stretches of the line end up under " +
             "the road. This re-measures the part of the line near the bus, a slice " +
             "at a time, so it settles onto whatever the ground has become.")]
    public bool regroundWhileDriving = true;

    [Tooltip("Seconds between re-measurements.")]
    public float regroundInterval = 0.4f;

    [Tooltip("How far from the bus to re-measure, in metres. Beyond this the line is " +
             "too far away for a few centimetres to be visible.")]
    public float regroundRange = 150f;

    [Tooltip("Points re-measured per pass. The work is spread over several passes so " +
             "a long route never costs a frame all at once.")]
    public int regroundBatch = 48;

    [Tooltip("Rebuild when the game starts. Off, the line stays exactly as it was " +
             "baked in the editor, which is faster but goes stale when the route " +
             "changes.")]
    public bool rebuildOnStart = true;

    LineRenderer line;

    // The drawn points, kept so re-grounding can move a few of them without
    // rebuilding the whole curve.
    Vector3[] drawnPoints;

    Transform bus;
    float nextReground;
    int regroundCursor;

    void Start()
    {
        if (rebuildOnStart) Build();

        VehicleController vehicle = Object.FindObjectOfType<VehicleController>();
        if (vehicle != null) bus = vehicle.transform;
    }

    void Update()
    {
        if (!regroundWhileDriving || drawnPoints == null || bus == null) return;
        if (Time.time < nextReground) return;

        nextReground = Time.time + Mathf.Max(0.05f, regroundInterval);
        Reground();
    }

    /// <summary>
    /// Re-measures a slice of the line against the terrain as it is right now.
    ///
    /// A slice rather than all of it, because every point costs a RaycastAll and a
    /// route is several hundred points long. The cursor walks the whole line over
    /// successive passes, so everything gets revisited without any single frame
    /// paying for all of it.
    ///
    /// Each point is measured against its own previous height rather than against a
    /// guide walked from the start of the route: by now every point already has a
    /// height that was close to right, which is a better starting guess than one
    /// carried from half a kilometre away.
    /// </summary>
    void Reground()
    {
        int count = drawnPoints.Length;
        if (count == 0) return;

        Vector3 here = bus.position;
        float rangeSquared = regroundRange * regroundRange;

        int batch = Mathf.Clamp(regroundBatch, 1, count);
        bool moved = false;

        // Points OUT of range must not count against the batch.
        //
        // They used to. Each step advanced the cursor whether or not it did any work,
        // so on a long route the pass was spent walking past the far side of campus
        // and only a handful of the points near the bus were ever re-measured. A four
        // kilometre route is a couple of thousand points: at forty-eight steps every
        // 0.4 s the cursor took nearly twenty seconds to come round, by which time the
        // bus had driven through the stretch that needed fixing. The line looked like
        // it simply was not snapping.
        //
        // Now the cursor sweeps the whole array every pass and only the points it
        // actually re-measures count, so the near ones are refreshed in about a second
        // however long the route is.
        int scanned = 0;
        int processed = 0;

        while (processed < batch && scanned < count)
        {
            int i = regroundCursor;
            regroundCursor = (regroundCursor + 1) % count;
            scanned++;

            Vector3 point = drawnPoints[i];

            // Flat distance: a point directly below the bus on a slope is still near it.
            float dx = point.x - here.x;
            float dz = point.z - here.z;
            if (dx * dx + dz * dz > rangeSquared) continue;

            processed++;

            bool found;
            float ground = GroundY(point.x, point.z, point.y - height,
                                   verticalTolerance, out found);

            if (!found) continue;

            float wanted = ground + height;
            if (Mathf.Abs(wanted - point.y) < 0.01f) continue;

            drawnPoints[i] = new Vector3(point.x, wanted, point.z);
            moved = true;
        }

        if (moved) line.SetPositions(drawnPoints);
    }

    /// <summary>
    /// Rebuilds the line from the current route and returns how many points it drew.
    /// Zero means the route has fewer than two checkpoints. Safe to call in the editor.
    /// </summary>
    public int Build()
    {
        line = GetComponent<LineRenderer>();

        Transform source = route != null ? route : FindRoute();

        if (source == null)
        {
            line.positionCount = 0;
            return 0;
        }

        // Checkpoints only. Signs stand beside the road, and threading the line
        // through them would pull it onto the pavement.
        var points = new List<Vector3>();

        for (int i = 0; i < source.childCount; i++)
        {
            RouteWaypoint waypoint = source.GetChild(i).GetComponent<RouteWaypoint>();
            if (waypoint != null && waypoint.kind == RouteWaypoint.Kind.Checkpoint)
                points.Add(source.GetChild(i).position);
        }

        if (points.Count < 2)
        {
            line.positionCount = 0;
            return 0;
        }

        // Smooth first, snap second. Snapping the checkpoints and then interpolating
        // between them would cut through every crest in between.
        var curve = new List<Vector3>();
        int last = points.Count - 1;

        for (int i = 0; i < last; i++)
        {
            Vector3 p0 = points[Mathf.Max(i - 1, 0)];
            Vector3 p1 = points[i];
            Vector3 p2 = points[i + 1];
            Vector3 p3 = points[Mathf.Min(i + 2, last)];

            // Enough samples that the spacing holds even on a long leg.
            int steps = Mathf.Max(2, Mathf.CeilToInt(
                Vector3.Distance(p1, p2) / Mathf.Max(0.5f, spacing)));

            for (int step = 0; step < steps; step++)
                curve.Add(CatmullRom(p0, p1, p2, p3, step / (float)steps));
        }

        curve.Add(points[last]);

        // The height walks along the route from the first checkpoint, each sample
        // accepting only a surface near the one before it.
        var drawn = new List<Vector3>(curve.Count);
        float guide = points[0].y;

        for (int i = 0; i < curve.Count; i++)
        {
            bool found;
            float y = GroundY(curve[i].x, curve[i].z, guide, verticalTolerance, out found);

            if (found) guide = y;

            drawn.Add(new Vector3(curve[i].x, y + height, curve[i].z));
        }

        drawnPoints = drawn.ToArray();

        line.useWorldSpace = true;
        line.positionCount = drawnPoints.Length;
        line.SetPositions(drawnPoints);

        line.startWidth = line.endWidth = width;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.alignment = LineAlignment.TransformZ;   // flat on the ground, not billboarded
        line.textureMode = LineTextureMode.Tile;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        if (material != null) line.sharedMaterial = material;
        else if (line.sharedMaterial == null) line.sharedMaterial = MakeMaterial();

        line.startColor = line.endColor = colour;

        // Belt and braces on the colour. A LineRenderer tints itself through vertex
        // colours, which some unlit shaders ignore, so the material is told as well.
        // Only outside Play, because this edits the shared material asset.
        if (!Application.isPlaying) Tint(line.sharedMaterial, colour);

        // Flat on the ground needs the transform lying flat, because TransformZ
        // aligns the ribbon to this object's Z axis.
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        return drawn.Count;
    }

    /// <summary>How many checkpoints a route has. Used by the editor tool to explain itself.</summary>
    public static int CountCheckpoints(Transform route)
    {
        if (route == null) return 0;

        int count = 0;

        for (int i = 0; i < route.childCount; i++)
        {
            RouteWaypoint waypoint = route.GetChild(i).GetComponent<RouteWaypoint>();
            if (waypoint != null && waypoint.kind == RouteWaypoint.Kind.Checkpoint) count++;
        }

        return count;
    }

    /// <summary>
    /// The height of the road at a spot, given roughly where it was a moment ago.
    ///
    /// Shared with the route importer so the line and the checkpoints agree about
    /// where the ground is. Returns the guide unchanged, and found false, when there
    /// is nothing to stand on: that happens where the Cesium tiles have not streamed
    /// in, which in the editor is most of campus until the Scene view has looked at it.
    /// </summary>
    public static float GroundY(float x, float z, float guide, float tolerance, out bool found)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, guide + 60f, z), Vector3.down,
                                              400f, ~0, QueryTriggerInteraction.Ignore);

        float ceiling = guide + tolerance;
        float best = float.NegativeInfinity;

        found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            float y = hits[i].point.y;

            // Above the tolerance is canopy, a bridge, or a floor inside a building.
            // Below the best so far is the ground under the ground.
            if (y > ceiling || y <= best) continue;

            best = y;
            found = true;
        }

        return found ? best : guide;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * ((2f * p1) +
                       (-p0 + p2) * t +
                       (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                       (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    Transform FindRoute()
    {
        GameObject go = GameObject.Find("Route");
        return go != null ? go.transform : null;
    }

    static void Tint(Material material, Color colour)
    {
        if (material == null) return;

        // Named differently in URP and in the built-in shaders, and setting a
        // property a shader does not have throws.
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
        if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", colour);
    }

    /// <summary>
    /// An unlit, vertex-coloured, alpha-blended material, so the line is the same
    /// brightness at night and in shadow.
    ///
    /// The shader is looked up by name from a short list because the project is on
    /// URP, where the built-in unlit shaders are named differently, and a missing
    /// shader silently renders magenta. Sprites/Default is first because it is the
    /// one that honours a LineRenderer's own colours in both pipelines.
    /// </summary>
    public static Material MakeMaterial()
    {
        string[] candidates =
        {
            // Ours first: it is the only one of these that offsets the line towards
            // the camera, which is what stops it flickering against the road.
            "NCAT/RoutePath",
            "Sprites/Default",
            "Universal Render Pipeline/Particles/Unlit",
            "Legacy Shaders/Particles/Alpha Blended Premultiply",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        foreach (string name in candidates)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) continue;

            Material material = new Material(shader);
            material.name = "RoutePath";
            return material;
        }

        return new Material(Shader.Find("Standard"));
    }
}
