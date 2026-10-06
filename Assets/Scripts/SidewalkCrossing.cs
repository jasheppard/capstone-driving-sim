using UnityEngine;

/// <summary>
/// A point where a pedestrian may leave the walkway and cross to the other side.
///
/// This exists for the rubric as much as for the look of the thing. Pedestrians
/// confined to a path never enter the road, which means "yield to pedestrians" can
/// never be tested, which means the element sits unassessed in every debrief. A
/// crossing is what puts a person in front of the driver in a place a real examiner
/// would expect one.
///
/// Put these where a crosswalk is, and put the matching Crosswalk waypoint at the
/// same spot so the scoring and the pedestrians agree about where crossing happens.
/// </summary>
public class SidewalkCrossing : MonoBehaviour
{
    [Tooltip("The point on the far side of the road. Both sides can link to each " +
             "other, which lets people cross in both directions.")]
    public Transform otherSide;

    [Tooltip("Chance that a pedestrian arriving here crosses rather than carrying " +
             "on along the walkway. Low keeps the crossing occasional, which is " +
             "what a quiet campus street looks like; high makes it a busy crossing.")]
    [Range(0f, 1f)] public float crossChance = 0.35f;

    [Tooltip("Seconds a pedestrian waits at the kerb before stepping out. Some " +
             "hesitation stops a crowd from crossing in one synchronised block, " +
             "and gives the driver a moment to see them standing there.")]
    public Vector2 waitSeconds = new Vector2(0.5f, 3f);

    /// <summary>
    /// The path the far side belongs to, so a pedestrian that crosses can pick up
    /// walking on the other pavement instead of stopping dead once it arrives.
    /// </summary>
    public SidewalkPath OtherPath
    {
        get
        {
            return otherSide != null ? otherSide.GetComponentInParent<SidewalkPath>() : null;
        }
    }

    /// <summary>Where the far point sits in its own path's ordering.</summary>
    public int OtherIndex
    {
        get
        {
            SidewalkPath path = OtherPath;
            if (path == null || otherSide == null) return -1;

            for (int i = 0; i < path.Count; i++)
                if (path.Node(i) == otherSide) return i;

            return -1;
        }
    }
}
