using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// The drivable road, described as a centreline with a width.
///
/// WHY NOT INVISIBLE WALLS
///
/// The obvious answer is a row of box colliders down each kerb. It cannot work here.
/// SmoothGroundDrive switches physical collision off on the vehicle so that scanned
/// terrain cannot catch it, and that switch is indiscriminate: the vehicle ignores
/// hand-placed colliders exactly as thoroughly as it ignores tile seams. Walls built
/// that way are driven straight through.
///
/// So the boundary is geometry rather than physics. The road is a polyline down its
/// middle plus a half-width, and a vehicle is inside the road when it is closer to
/// that line than the half-width. Distance to a polyline is cheap, exact, and needs
/// no colliders, no mesh and no NavMesh.
///
/// It also costs nothing to author, because the centreline already exists: the route
/// recorded by driving it is a list of points down the middle of the road. Road >
/// 1. Build Corridor From Route turns that into a corridor in one click.
/// </summary>
public class RoadCorridor : MonoBehaviour
{
    [Tooltip("Half the drivable width, in metres, measured from the centreline.\n\n" +
             "Deliberately generous. The centreline is not the road: it is wherever " +
             "the bus happened to be driven while recording, which wanders within the " +
             "lane, cuts bends and sits off-centre at junctions. Holding a driver to " +
             "that line to the metre punishes them for someone else's wobble. 8 m is " +
             "about the full carriageway, so you can use the whole road and swing wide " +
             "round a corner without anything taking the wheel off you.")]
    public float halfWidth = 8f;

    [Tooltip("Extra metres beyond the half-width where you are warned but not yet " +
             "corrected. This is the rumble strip: it tells the driver they are " +
             "drifting before anything touches the controls.")]
    public float shoulder = 4f;

    [Tooltip("Join the last point back to the first. Tick this for a loop route, " +
             "leave it off for an out-and-back.")]
    public bool loop = false;

    public static readonly List<RoadCorridor> All = new List<RoadCorridor>();

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    public int Count
    {
        get { return transform.childCount; }
    }

    public Vector3 Position(int index)
    {
        if (index < 0 || index >= transform.childCount) return transform.position;
        return transform.GetChild(index).position;
    }

    /// <summary>
    /// The closest point on the centreline, and how far the position is from it.
    ///
    /// Measured against the segments rather than only the points. Testing points
    /// alone reports a vehicle halfway between two widely spaced waypoints as far
    /// off the road when it is driving straight down the middle of it.
    ///
    /// Flat: height is ignored throughout, because a road that climbs a hill is
    /// still the same road and the driver has not left it by going uphill.
    /// </summary>
    public bool Nearest(Vector3 position, out Vector3 closest, out float distance)
    {
        closest = position;
        distance = float.PositiveInfinity;

        if (Count < 2) return false;

        Vector3 flatPosition = new Vector3(position.x, 0f, position.z);
        int segments = loop ? Count : Count - 1;

        for (int i = 0; i < segments; i++)
        {
            Vector3 a = Position(i);
            Vector3 b = Position((i + 1) % Count);

            a.y = 0f;
            b.y = 0f;

            Vector3 along = b - a;
            float lengthSquared = along.sqrMagnitude;
            if (lengthSquared < 0.0001f) continue;

            // Where the vehicle projects onto this segment, clamped to its ends so
            // the nearest point never runs off past a corner.
            float t = Mathf.Clamp01(Vector3.Dot(flatPosition - a, along) / lengthSquared);
            Vector3 point = a + along * t;

            float candidate = Vector3.Distance(flatPosition, point);
            if (candidate >= distance) continue;

            distance = candidate;
            closest = point;
        }

        return distance < float.PositiveInfinity;
    }

    // ------------------------------------------------------------------ gizmos

    void OnDrawGizmos()
    {
        Draw(new Color(1f, 0.85f, 0.3f, 0.55f));
    }

    void OnDrawGizmosSelected()
    {
        Draw(new Color(1f, 0.6f, 0.2f, 1f));
    }

    /// <summary>
    /// Draws the centreline and both edges.
    ///
    /// The edges matter more than the line: half-width is a number until you can
    /// see where it actually falls on the road, and every corridor needs widening
    /// somewhere once you look at it.
    /// </summary>
    void Draw(Color colour)
    {
        if (Count < 2) return;

        int segments = loop ? Count : Count - 1;

        for (int i = 0; i < segments; i++)
        {
            Vector3 a = Position(i);
            Vector3 b = Position((i + 1) % Count);

            Gizmos.color = colour;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(a, 0.4f);

            Vector3 along = b - a;
            along.y = 0f;
            if (along.sqrMagnitude < 0.0001f) continue;

            Vector3 across = Vector3.Cross(Vector3.up, along.normalized) * halfWidth;

            Gizmos.color = new Color(colour.r, colour.g, colour.b, colour.a * 0.7f);
            Gizmos.DrawLine(a + across, b + across);
            Gizmos.DrawLine(a - across, b - across);

            Vector3 warn = across.normalized * (halfWidth + shoulder);

            Gizmos.color = new Color(1f, 0.35f, 0.3f, colour.a * 0.35f);
            Gizmos.DrawLine(a + warn, b + warn);
            Gizmos.DrawLine(a - warn, b - warn);
        }

        if (!loop) Gizmos.DrawSphere(Position(Count - 1), 0.4f);
    }
}
