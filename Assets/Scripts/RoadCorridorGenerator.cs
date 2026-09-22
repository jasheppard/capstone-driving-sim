using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a chain of trigger BoxColliders along the route defined by RouteWaypoint objects,
/// creating a drivable "road corridor" the bus must stay inside.
///
/// Usage:
///  1. Attach this to an empty GameObject called "RoadNetwork" in your scene.
///  2. Assign the same waypoints list you use for RouteWaypoint scoring (in order).
///  3. Set roadWidth to match your visual road (in meters).
///  4. Press the "Generate Road Corridor" context menu item (or call GenerateCorridor() at
///     runtime/edit time). It will create one child BoxCollider per segment.
///  5. Create a "Road" layer in Unity (Edit > Project Settings > Tags and Layers) and set
///     roadLayer accordingly. Generated segments are auto-assigned to that layer.
///
/// NOTE: Adjust the `GetWaypointPosition` helper below if your RouteWaypoint class exposes
/// position differently (e.g. via transform.position vs a public Vector3 field).
/// </summary>
[ExecuteAlways]
public class RoadCorridorGenerator : MonoBehaviour
{
    [Header("Source waypoints (same order as route)")]
    public List<RouteWaypoint> waypoints = new List<RouteWaypoint>();

    [Header("Corridor settings")]
    [Tooltip("Width of the drivable corridor, in meters.")]
    public float roadWidth = 7f;

    [Tooltip("Height of each trigger box (should comfortably cover bus height + a margin).")]
    public float roadHeight = 4f;

    [Tooltip("Layer the generated segments will be placed on.")]
    public string roadLayerName = "Road";

    [Tooltip("Clears and regenerates automatically whenever waypoints change (editor only).")]
    public bool autoRegenerate = false;

    private const string SegmentParentName = "GeneratedSegments";

    [ContextMenu("Generate Road Corridor")]
    public void GenerateCorridor()
    {
        ClearCorridor();

        if (waypoints == null || waypoints.Count < 2)
        {
            Debug.LogWarning("[RoadCorridorGenerator] Need at least 2 waypoints to generate a corridor.");
            return;
        }

        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        if (roadLayer < 0)
        {
            Debug.LogWarning($"[RoadCorridorGenerator] Layer '{roadLayerName}' doesn't exist. " +
                              "Create it in Project Settings > Tags and Layers, or segments will stay on Default.");
        }

        Transform parent = new GameObject(SegmentParentName).transform;
        parent.SetParent(transform, false);

        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            Vector3 a = GetWaypointPosition(waypoints[i]);
            Vector3 b = GetWaypointPosition(waypoints[i + 1]);

            CreateSegment(a, b, i, parent, roadLayer);
        }

        Debug.Log($"[RoadCorridorGenerator] Generated {waypoints.Count - 1} road segments.");
    }

    [ContextMenu("Clear Road Corridor")]
    public void ClearCorridor()
    {
        Transform existing = transform.Find(SegmentParentName);
        if (existing != null)
        {
            if (Application.isPlaying)
                Destroy(existing.gameObject);
            else
                DestroyImmediate(existing.gameObject);
        }
    }

    private void CreateSegment(Vector3 a, Vector3 b, int index, Transform parent, int roadLayer)
    {
        Vector3 mid = (a + b) * 0.5f;
        Vector3 dir = b - a;
        float length = dir.magnitude;
        if (length < 0.01f) return;

        GameObject seg = new GameObject($"RoadSegment_{index}");
        seg.transform.SetParent(parent, false);
        seg.transform.position = mid;
        seg.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        if (roadLayer >= 0) seg.layer = roadLayer;

        BoxCollider box = seg.AddComponent<BoxCollider>();
        box.isTrigger = true;
        // Local Z runs along the segment direction (forward), X is width, Y is height.
        box.size = new Vector3(roadWidth, roadHeight, length);
        box.center = Vector3.zero;
    }

    /// <summary>
    /// Adjust this to match your RouteWaypoint.cs. Common cases:
    ///   return wp.transform.position;   // if RouteWaypoint is a MonoBehaviour
    ///   return wp.position;              // if it exposes a public Vector3 field
    /// </summary>
    private Vector3 GetWaypointPosition(RouteWaypoint wp)
    {
        if (wp == null)
        {
            Debug.LogError("[RoadCorridorGenerator] Null waypoint in list.");
            return Vector3.zero;
        }
        return wp.transform.position;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (waypoints == null || waypoints.Count < 2) return;
        Gizmos.color = Color.yellow;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;
            Gizmos.DrawLine(GetWaypointPosition(waypoints[i]), GetWaypointPosition(waypoints[i + 1]));
        }
    }
#endif
}
