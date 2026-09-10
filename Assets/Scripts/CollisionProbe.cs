using UnityEngine;

/// <summary>
/// Shows you what you just hit.
///
/// "It stopped me and there was nothing there" is not something anyone can debug from
/// a description, because the thing being hit is usually real and simply not visible:
/// it is below the road surface, above the roof, or off to the side of the bus where
/// the collider is wider than the body.
///
/// So this marks it. Every impact leaves a red sphere floating at the exact contact
/// point and prints one line saying how far that point was from the road, from the
/// centre of the bus, and how far up. Drive the stretch that keeps stopping you, then
/// look at where the markers ended up.
///
///   markers under the road       the collider hangs below the wheels
///   markers out to the side      the collider is wider than the bus
///   markers up in the air        the collider is taller than the bus, or it is
///                                catching scanned tree canopy
///   markers exactly on the road  genuine scanned obstacles, and step assist is the
///                                answer rather than the collider
///
/// Take it off the car when you are done. It is a diagnostic, not part of the game.
/// </summary>
public class CollisionProbe : MonoBehaviour
{
    [Header("Markers")]
    [Tooltip("How big the marker spheres are, in metres.")]
    public float markerSize = 0.4f;

    [Tooltip("Seconds a marker stays. Zero leaves them there for the whole run, which " +
             "is what you want when hunting a repeatable spot.")]
    public float markerSeconds = 0f;

    [Tooltip("Stop making markers after this many, so a long scrape along a wall does " +
             "not fill the scene.")]
    public int maximumMarkers = 60;

    [Header("Filtering")]
    [Tooltip("Ignore contacts gentler than this, in m/s. Resting on the road is a " +
             "constant stream of contacts and none of them are what stopped you.")]
    public float minimumImpact = 0.6f;

    [Tooltip("Seconds before the same spot can be reported again.")]
    public float cooldown = 0.4f;

    int markers;
    float lastReport = -999f;
    Collider hull;
    Transform markerParent;

    void Start()
    {
        foreach (Collider candidate in GetComponentsInChildren<Collider>())
        {
            if (candidate.isTrigger) continue;
            if (hull == null || candidate.bounds.size.sqrMagnitude > hull.bounds.size.sqrMagnitude)
                hull = candidate;
        }

        markerParent = new GameObject("CollisionMarkers").transform;

        Debug.Log("[CollisionProbe] Watching. Every impact leaves a red marker and one " +
                  "line in the Console. Remove this component when you are done.");
    }

    void OnCollisionEnter(Collision collision) { Report(collision); }
    void OnCollisionStay(Collision collision) { Report(collision); }

    void Report(Collision collision)
    {
        if (collision.relativeVelocity.magnitude < minimumImpact) return;
        if (Time.time - lastReport < cooldown) return;
        if (markers >= maximumMarkers) return;

        if (collision.contactCount == 0) return;

        lastReport = Time.time;

        ContactPoint contact = collision.GetContact(0);
        Vector3 point = contact.point;

        // Where the road actually is under that contact, found independently of what
        // was hit, so the height comparison means something.
        float roadY = point.y;
        RaycastHit hit;

        if (Physics.Raycast(point + Vector3.up * 30f, Vector3.down, out hit, 200f))
            roadY = hit.point.y;

        float aboveRoad = point.y - roadY;
        float belowHull = hull != null ? point.y - hull.bounds.min.y : 0f;

        // Sideways distance from the centreline of the bus, so a contact out past the
        // bodywork is obvious.
        Vector3 offset = point - transform.position;
        float sideways = Vector3.Dot(offset, transform.right);
        float ahead = Vector3.Dot(offset, transform.forward);

        Marker(point);
        markers++;

        Debug.Log(string.Format(
            "[CollisionProbe] Hit \"{0}\"  at {1:F2} m {2} the road,  {3:F2} m " +
            "{4} of centre,  {5:F2} m {6},  impact {7:F1} m/s{8}",
            collision.gameObject.name,
            Mathf.Abs(aboveRoad), aboveRoad < -0.05f ? "BELOW" : "above",
            Mathf.Abs(sideways), sideways < 0f ? "left" : "right",
            Mathf.Abs(ahead), ahead < 0f ? "behind" : "ahead",
            collision.relativeVelocity.magnitude,
            belowHull < -0.05f
                ? "   <- BELOW the bottom of the collider, so the collider is reaching " +
                  "under the road"
                : ""));
    }

    void Marker(Vector3 point)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Hit_" + markers;
        marker.transform.SetParent(markerParent, false);
        marker.transform.position = point;
        marker.transform.localScale = Vector3.one * markerSize;

        // No collider, or the markers become obstacles of their own.
        Collider collider = marker.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        Renderer renderer = marker.GetComponent<Renderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader);
        material.color = new Color(1f, 0.15f, 0.12f);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(1f, 0.15f, 0.12f));

        renderer.sharedMaterial = material;

        if (markerSeconds > 0f) Destroy(marker, markerSeconds);
    }
}
