using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A walkway, described as an ordered chain of points you place by hand.
///
/// WHY THIS HAS TO BE HAND-PLACED
///
/// The campus is Google photogrammetry: one continuous mesh with no idea what any
/// part of it is. A raycast onto a pavement and a raycast onto the middle of the
/// road return the same thing, because to the geometry they are the same thing.
/// There is no material to test, no layer to filter, no lane data to read.
///
/// A NavMesh does not rescue this either. Cesium streams tiles in at runtime, so
/// there is no static geometry in the editor to bake against, and a mesh baked at
/// runtime would still mark the road walkable because nothing in it says otherwise.
///
/// So "sidewalk" is information the project has to supply. This is where it lives:
/// a parent object whose children are points along a walkway, in order. Pedestrians
/// walk from point to point instead of wandering, which is the only way to keep
/// them off the road on a surface that cannot tell you where the road is.
///
/// CROSSINGS
///
/// A path alone would leave every pedestrian permanently on one side of the street,
/// and the road test needs them to step out in front of the driver sometimes, or
/// the "yield to pedestrians" element can never be assessed. So a point can carry a
/// SidewalkCrossing that links it to a point on the far side, and a pedestrian
/// reaching it may cross rather than continue.
/// </summary>
public class SidewalkPath : MonoBehaviour
{
    [Tooltip("Join the last point back to the first, so pedestrians circulate " +
             "instead of turning around at the ends. Good for a quad or a loop " +
             "around a building.")]
    public bool loop = false;

    [Tooltip("How wide the walkway is, in metres. Pedestrians pick a lane offset " +
             "inside this width and keep it, so a group on one path does not walk " +
             "in single file down an invisible line.")]
    public float width = 2.2f;

    /// <summary>
    /// Every path in the scene.
    ///
    /// Maintained here rather than found on demand, because the spawner needs it
    /// every time a pedestrian is placed or recycled and FindObjectsOfType walks
    /// the entire scene each call.
    /// </summary>
    public static readonly List<SidewalkPath> All = new List<SidewalkPath>();

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

    public Transform Node(int index)
    {
        if (index < 0 || index >= transform.childCount) return null;
        return transform.GetChild(index);
    }

    public Vector3 Position(int index)
    {
        Transform node = Node(index);
        return node != null ? node.position : transform.position;
    }

    /// <summary>
    /// Which way the walkway runs at a point, flattened.
    ///
    /// Used to offset pedestrians sideways across the width of the path rather
    /// than along it, so the offset spreads them out instead of making them early
    /// or late.
    /// </summary>
    public Vector3 DirectionAt(int index)
    {
        if (Count < 2) return transform.forward;

        int from = Mathf.Clamp(index, 0, Count - 2);
        Vector3 direction = Position(from + 1) - Position(from);

        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
    }

    /// <summary>The point on this path closest to a position, on the flat.</summary>
    public int NearestNode(Vector3 position, out float distance)
    {
        int best = -1;
        distance = float.PositiveInfinity;

        for (int i = 0; i < Count; i++)
        {
            Vector3 offset = Position(i) - position;
            offset.y = 0f;

            float candidate = offset.magnitude;
            if (candidate >= distance) continue;

            distance = candidate;
            best = i;
        }

        return best;
    }

    // ------------------------------------------------------------------ gizmos

    void OnDrawGizmos()
    {
        Draw(new Color(0.35f, 0.75f, 1f, 0.7f));
    }

    void OnDrawGizmosSelected()
    {
        Draw(new Color(0.4f, 1f, 0.6f, 1f));
    }

    void Draw(Color colour)
    {
        if (Count == 0) return;

        Gizmos.color = colour;

        for (int i = 0; i < Count; i++)
        {
            Vector3 here = Position(i);
            Gizmos.DrawSphere(here, 0.35f);

            if (i + 1 < Count) Gizmos.DrawLine(here, Position(i + 1));
            else if (loop && Count > 2) Gizmos.DrawLine(here, Position(0));

            // A crossing is drawn in a different colour and struck across to the
            // far side, because it is the one place a pedestrian leaves the kerb
            // and it should be obvious where those are while laying paths out.
            SidewalkCrossing crossing = Node(i).GetComponent<SidewalkCrossing>();

            if (crossing == null || crossing.otherSide == null) continue;

            Gizmos.color = new Color(1f, 0.75f, 0.25f, 0.9f);
            Gizmos.DrawLine(here, crossing.otherSide.position);
            Gizmos.DrawWireSphere(here, 0.8f);
            Gizmos.color = colour;
        }
    }
}
