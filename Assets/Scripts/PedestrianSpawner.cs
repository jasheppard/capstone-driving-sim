using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Keeps a population of pedestrians around wherever you are.
///
/// It spawns them in a ring — far enough that you do not see them appear, close
/// enough to be worth simulating — and recycles anyone who falls too far behind.
/// Populating the whole campus instead would cost far more and you would never see
/// most of it.
///
/// Spawning waits for a downward raycast to find ground, which also solves the
/// Cesium streaming problem: no tile loaded means no hit, so nobody spawns into
/// thin air and falls out of the world.
/// </summary>
public class PedestrianSpawner : MonoBehaviour
{
    [Header("What to spawn")]
    [Tooltip("One entry per character. A random one is chosen each spawn, so the more " +
             "you add the less the crowd repeats.")]
    public GameObject[] pedestrianPrefabs;

    [Tooltip("How many to keep alive at once. Each is a skinned character: raise this " +
             "carefully and watch your frame rate.")]
    public int population = 8;

    [Header("Where")]
    [Tooltip("Followed by the spawner. Left empty, it uses the main camera, which " +
             "works whether you are driving or on foot.")]
    public Transform target;

    [Tooltip("Closest a new pedestrian may appear.")]
    public float spawnRadiusMin = 25f;

    [Tooltip("Furthest a new pedestrian may appear.")]
    public float spawnRadiusMax = 60f;

    [Tooltip("Beyond this from the target, a pedestrian is recycled to a new spot.")]
    public float despawnRadius = 90f;

    [Header("Ground")]
    public float groundProbeHeight = 60f;
    public float groundProbeDistance = 200f;

    [Tooltip("Reject spawn points this much higher than the target: they are rooftops.")]
    public float maxHeightAboveTarget = 6f;

    [Header("Pacing")]
    [Tooltip("Seconds between spawn attempts, so a burst does not stall a frame.")]
    public float spawnInterval = 0.4f;

    readonly List<PedestrianController> active = new List<PedestrianController>();
    float nextSpawnTime;

    void Start()
    {
        if (target == null && Camera.main != null) target = Camera.main.transform;
    }

    void Update()
    {
        if (pedestrianPrefabs == null || pedestrianPrefabs.Length == 0) return;

        if (target == null)
        {
            if (Camera.main == null) return;
            target = Camera.main.transform;
        }

        RecycleDistant();

        if (active.Count < population && Time.time >= nextSpawnTime)
        {
            TrySpawn();
            nextSpawnTime = Time.time + spawnInterval;
        }
    }

    void RecycleDistant()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            PedestrianController pedestrian = active[i];

            if (pedestrian == null)
            {
                active.RemoveAt(i);
                continue;
            }

            float distance = Vector3.Distance(pedestrian.transform.position, target.position);
            bool fellThrough = pedestrian.transform.position.y < target.position.y - 100f;

            if (distance > despawnRadius || fellThrough)
            {
                if (TryFindGroundPoint(out Vector3 point))
                {
                    MoveTo(pedestrian, point);
                }
                else
                {
                    Destroy(pedestrian.gameObject);
                    active.RemoveAt(i);
                }
            }
        }
    }

    void TrySpawn()
    {
        if (!TryFindGroundPoint(out Vector3 point)) return;

        GameObject prefab = pedestrianPrefabs[Random.Range(0, pedestrianPrefabs.Length)];
        if (prefab == null) return;

        GameObject instance = Instantiate(prefab, point,
                                          Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        instance.name = "Pedestrian";

        PedestrianController pedestrian = instance.GetComponent<PedestrianController>();
        if (pedestrian == null) pedestrian = instance.AddComponent<PedestrianController>();

        active.Add(pedestrian);
    }

    /// <summary>Picks a random point in the ring and drops it onto whatever ground is there.</summary>
    bool TryFindGroundPoint(out Vector3 point)
    {
        point = Vector3.zero;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(spawnRadiusMin, spawnRadiusMax);

            Vector3 candidate = target.position +
                new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            Vector3 origin = candidate + Vector3.up * groundProbeHeight;

            RaycastHit[] hits = Physics.RaycastAll(
                origin, Vector3.down, groundProbeDistance, ~0, QueryTriggerInteraction.Ignore);

            float bestY = float.NegativeInfinity;
            bool found = false;

            foreach (RaycastHit hit in hits)
            {
                if (hit.point.y > bestY)
                {
                    bestY = hit.point.y;
                    found = true;
                }
            }

            if (!found) continue;                                   // tile not streamed in yet

            // A hit well above you is a roof, not a pavement.
            if (bestY > target.position.y + maxHeightAboveTarget) continue;

            point = new Vector3(candidate.x, bestY + 1f, candidate.z);
            return true;
        }

        return false;
    }

    void MoveTo(PedestrianController pedestrian, Vector3 point)
    {
        CharacterController cc = pedestrian.GetComponent<CharacterController>();

        // The CharacterController must be off to teleport, or it fights the move.
        if (cc != null) cc.enabled = false;
        pedestrian.transform.position = point;
        if (cc != null) cc.enabled = true;

        pedestrian.ResetState(Random.Range(0f, 360f));
    }

    void OnDrawGizmosSelected()
    {
        Transform pivot = target != null ? target : transform;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(pivot.position, spawnRadiusMin);
        Gizmos.DrawWireSphere(pivot.position, spawnRadiusMax);

        Gizmos.color = new Color(1f, 0.5f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(pivot.position, despawnRadius);
    }
}
