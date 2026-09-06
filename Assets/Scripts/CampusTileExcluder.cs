using UnityEngine;
using CesiumForUnity;

/// <summary>
/// Restricts the streamed world to a circle around campus.
///
/// Cesium decides what to load by walking a tree of tiles, each with a bounding
/// box, and refining the ones near the camera. CesiumTileExcluder lets you veto
/// tiles during that walk: return true and the tile is never loaded — and neither
/// is anything below it in the tree, which is what makes this cheap. The root
/// tile's bounds cover the whole planet, so it always survives the test and the
/// walk proceeds normally; only the branches that wander off campus get cut.
///
/// Three things this buys you:
///
///   Scope       the simulation is about NC A&T, so the world ending at the
///               campus edge is a design decision, not a limitation.
///   Performance far fewer meshes and colliders, which matters a lot on a
///               mechanical hard drive.
///   Quota       Google Photorealistic 3D Tiles are billed per request. Not
///               streaming half of Greensboro keeps that bounded.
///
/// IMPORTANT: this must live on the same GameObject as the Cesium3DTileset, or on
/// one of its parents (the CesiumGeoreference is the usual choice). Cesium only
/// looks for excluders there.
/// </summary>
[ExecuteInEditMode]
public class CampusTileExcluder : CesiumTileExcluder
{
    [Header("Boundary")]
    [Tooltip("What the circle is centred on. Leave empty to use this object's own " +
             "origin, which for the CesiumGeoreference is the latitude and longitude " +
             "you set for campus.")]
    public Transform centre;

    [Tooltip("Radius in metres. NC A&T's main campus is roughly 800 m across, so " +
             "600-900 covers it with room to spare. Raise it if you find the world " +
             "ending somewhere you wanted to drive.")]
    public float radius = 800f;

    [Header("Editor")]
    [Tooltip("Draw the boundary circle in the Scene view when this object is selected.")]
    public bool showGizmo = true;

    // Tile bounds arrive in this GameObject's local space, so the centre has to be
    // converted into the same space before they can be compared.
    Vector3 localCentre;

    protected override void OnEnable()
    {
        RecalculateCentre();
        base.OnEnable();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
    }

    void Update()
    {
        // Cheap, and it means dragging the centre object or editing the radius in
        // the Inspector updates the world without needing a restart.
        RecalculateCentre();
    }

    void RecalculateCentre()
    {
        localCentre = centre != null
            ? transform.InverseTransformPoint(centre.position)
            : Vector3.zero;
    }

    /// <summary>
    /// True means "do not load this tile".
    ///
    /// Bounds.SqrDistance measures from a point to the nearest part of the box and
    /// returns zero when the point is inside it. So a tile is kept if any part of
    /// it comes within the radius, and dropped only when the whole box is outside —
    /// which avoids slicing through a tile that is half on campus.
    ///
    /// Squared distances throughout, to skip a square root on every tile in the
    /// tree on every frame.
    /// </summary>
    public override bool ShouldExclude(Cesium3DTile tile)
    {
        if (!enabled) return false;

        return tile.bounds.SqrDistance(localCentre) > radius * radius;
    }

    void OnDrawGizmosSelected()
    {
        if (!showGizmo) return;

        Vector3 worldCentre = centre != null ? centre.position : transform.position;

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);

        // A ring on the ground plane reads better than a wire sphere for something
        // that is really a horizontal limit.
        const int segments = 72;
        Vector3 previous = worldCentre + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = worldCentre + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
