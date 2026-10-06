using UnityEngine;
using System.Collections.Generic;
using CesiumForUnity;

/// <summary>
/// Streams only the world near the route, rather than a circle around campus.
///
/// WHY A CIRCLE IS THE WRONG SHAPE
///
/// CampusTileExcluder keeps everything within a radius of one point. That is the
/// right first cut — it stops half of Greensboro loading — but a route is a thin
/// ribbon and a circle is not. A route spanning 900 m of campus needs a 900 m radius
/// to reach its far end, and that circle is about two and a half square kilometres,
/// almost none of which is ever driven. Every tile in it is a mesh, a collider, a
/// download and a billed request.
///
/// This keeps a tile if any part of it comes within a margin of any part of the
/// route. The loaded world ends up corridor shaped: the road, the buildings either
/// side of it, and nothing beyond. For the same view out of the windscreen it is a
/// fraction of the tiles.
///
/// HOW IT STAYS CHEAP
///
/// Cesium calls this for every tile it considers, every frame, so the test has to be
/// quick. Two things make it so. The route is sampled down to a point every fifty
/// metres rather than every waypoint — the margin is much larger than the spacing, so
/// the circles around consecutive samples overlap heavily and nothing falls between
/// them. And the test returns the moment one sample is close enough, so a tile that is
/// obviously on the route costs one comparison rather than sixty.
///
/// Excluding a tile also excludes everything beneath it in the tree, so the branches
/// that wander off the route are cut near the top and never walked at all.
///
/// IMPORTANT: like any excluder, this has to live on the same GameObject as the
/// Cesium3DTileset or on one of its parents. Cesium only looks for them there.
/// </summary>
[ExecuteInEditMode]
public class RouteTileExcluder : CesiumTileExcluder
{
    [Header("Route")]
    [Tooltip("The Route object. Left empty, one named \"Route\" is used.")]
    public Transform route;

    [Tooltip("How far either side of the route to keep, in metres. This is the whole " +
             "dial: small enough and you drive down a tunnel with nothing beside it, " +
             "large enough and you may as well have used a circle. 150 shows the " +
             "buildings along the road and the far side of a quad.")]
    public float margin = 150f;

    [Tooltip("Distance between the points the route is sampled at, in metres. Must " +
             "stay well under the margin: the kept area is a circle around each " +
             "sample, and if they are further apart than the margin there are gaps " +
             "between them where the road goes missing.")]
    public float sampleSpacing = 50f;

    [Header("Always keep")]
    [Tooltip("A patch that loads in full regardless of where the route goes. Leave " +
             "empty to use this object's own origin, which on the CesiumGeoreference " +
             "is the campus latitude and longitude.")]
    public Transform coreCentre;

    [Tooltip("Radius of that patch, in metres. The route is a ribbon, so a corridor " +
             "around it shows the buildings lining the road and nothing behind them — " +
             "which is wrong for the middle of campus, where the quad and the halls " +
             "set off the road are most of what the place looks like. Keeping the core " +
             "whole costs little, because it is the part you drive through anyway.\n\n" +
             "Zero turns this off and leaves only the corridor.")]
    public float coreRadius = 260f;

    [Header("Safety")]
    [Tooltip("What to do when there is no route to follow. On, the whole world loads " +
             "as if this were not here, which is the safe way round: the alternative " +
             "is a scene that silently refuses to stream anything and looks broken.")]
    public bool loadEverythingWithoutARoute = true;

    [Header("Editor")]
    public bool showGizmo = true;

    /// <summary>Route samples in this object's local space, which is where tile bounds are.</summary>
    readonly List<Vector3> samples = new List<Vector3>();

    float marginSquared;
    int lastChildCount = -1;
    float nextRefresh;

    /// <summary>The always-keep centre, in the same local space as the tile bounds.</summary>
    Vector3 localCore;

    protected override void OnEnable()
    {
        Rebuild();
        base.OnEnable();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
    }

    void Update()
    {
        // Cheap enough to check often, so editing the route or the margin updates the
        // world without a restart, but not every frame on a long route.
        if (Time.unscaledTime < nextRefresh) return;

        nextRefresh = Time.unscaledTime + 1f;

        Transform source = Source();
        int count = source != null ? source.childCount : -1;

        if (count != lastChildCount || !Mathf.Approximately(marginSquared, margin * margin))
            Rebuild();
    }

    Transform Source()
    {
        if (route != null) return route;

        GameObject found = GameObject.Find("Route");
        return found != null ? found.transform : null;
    }

    void Rebuild()
    {
        samples.Clear();
        marginSquared = margin * margin;

        localCore = coreCentre != null
            ? transform.InverseTransformPoint(coreCentre.position)
            : Vector3.zero;

        Transform source = Source();
        lastChildCount = source != null ? source.childCount : -1;

        if (source == null) return;

        // Checkpoints describe where the road goes; signs stand beside it and would
        // only repeat places the checkpoints already cover.
        var line = new List<Vector3>();

        for (int i = 0; i < source.childCount; i++)
        {
            RouteWaypoint waypoint = source.GetChild(i).GetComponent<RouteWaypoint>();

            if (waypoint != null && waypoint.kind == RouteWaypoint.Kind.Checkpoint)
                line.Add(source.GetChild(i).position);
        }

        if (line.Count == 0) return;

        float spacing = Mathf.Max(5f, sampleSpacing);
        float since = spacing;      // so the first point is always taken

        for (int i = 0; i < line.Count; i++)
        {
            if (i > 0) since += Vector3.Distance(line[i - 1], line[i]);

            if (since < spacing && i != line.Count - 1) continue;

            since = 0f;
            samples.Add(transform.InverseTransformPoint(line[i]));
        }
    }

    /// <summary>
    /// True means "do not load this tile".
    ///
    /// SqrDistance measures from a point to the nearest part of the box and is zero
    /// inside it, so a tile survives if any part of it comes within the margin of any
    /// sample. Dropping only when the whole box is clear avoids slicing a tile that is
    /// half on the route.
    /// </summary>
    public override bool ShouldExclude(Cesium3DTile tile)
    {
        if (!enabled) return false;

        // The core first, and cheaply: it is one comparison, it covers the busiest
        // part of the world, and getting a hit here skips the route entirely.
        if (coreRadius > 0f && tile.bounds.SqrDistance(localCore) <= coreRadius * coreRadius)
            return false;

        if (samples.Count == 0) return !loadEverythingWithoutARoute;

        for (int i = 0; i < samples.Count; i++)
            if (tile.bounds.SqrDistance(samples[i]) <= marginSquared) return false;

        return true;
    }

    void OnDrawGizmosSelected()
    {
        if (!showGizmo || samples.Count == 0) return;

        if (coreRadius > 0f)
        {
            Gizmos.color = new Color(0.45f, 0.95f, 0.55f, 0.9f);
            Ring(transform.TransformPoint(localCore), coreRadius);
        }

        Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.5f);

        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 world = transform.TransformPoint(samples[i]);

            Ring(world, margin);
            if (i > 0) Gizmos.DrawLine(transform.TransformPoint(samples[i - 1]), world);
        }
    }

    static void Ring(Vector3 centre, float radius)
    {
        const int segments = 24;
        Vector3 previous = centre + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;

            Vector3 next = centre + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
