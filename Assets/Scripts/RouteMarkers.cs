using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Makes the route visible from the driver's seat.
///
/// RouteWaypoint draws itself with Gizmos, and Gizmos are an editor-only feature:
/// they appear in the Scene view and never in the Game view or a build. So the
/// route was visible to whoever laid it out and invisible to whoever drove it.
///
/// This puts real geometry in the world instead — a column of light at each
/// waypoint with a ring on the ground beneath it, coloured by what kind of point
/// it is. The current one is bright and breathing; the next few are faint so you
/// can read the shape of the road ahead; the ones behind you switch off.
///
/// A screen-edge arrow handles the rest. A beam is no help when the waypoint is
/// behind a building or off to your left, which on a campus is most of the time.
///
/// The markers carry no colliders. A solid pillar in the middle of the road would
/// stop the bus dead.
/// </summary>
public class RouteMarkers : MonoBehaviour
{
    [Header("References")]
    public RouteRunner runner;

    [Tooltip("Left empty, the main camera is used.")]
    public Camera viewCamera;

    [Tooltip("Optional. Points toward the next waypoint when it is off screen.")]
    public RectTransform offScreenArrow;

    [Header("Beam")]
    [Tooltip("How tall the column of light is, in metres. Tall enough to clear the " +
             "buildings between you and it.")]
    public float beamHeight = 45f;

    public float beamRadius = 0.8f;

    [Tooltip("Opacity of the active waypoint's beam.")]
    [Range(0f, 1f)] public float activeAlpha = 0.55f;

    [Tooltip("Opacity of the upcoming ones, so the route reads as a path without " +
             "cluttering the view.")]
    [Range(0f, 1f)] public float upcomingAlpha = 0.16f;

    [Tooltip("How many waypoints ahead of the current one stay visible.")]
    public int lookAhead = 3;

    [Header("Ground ring")]
    [Tooltip("Draw a disc on the ground at each waypoint, sized to its radius. " +
             "This is what shows you where the zone actually starts.")]
    public bool showGroundRings = true;

    [Range(0f, 1f)] public float ringAlpha = 0.22f;

    [Header("Orbs")]
    [Tooltip("A large floating ball at each waypoint, the thing the driver actually " +
             "aims at and drives through.")]
    public bool showOrbs = true;

    [Tooltip("Radius in metres. Big enough to be unmissable from down the road.")]
    public float orbRadius = 1.8f;

    [Tooltip("Height above the ground, in metres. Around windscreen height so it " +
             "sits in the driver's eyeline rather than under the bonnet.")]
    public float orbHeight = 2.6f;

    [Range(0f, 1f)] public float orbAlpha = 0.9f;

    [Tooltip("Metres the orb rises and falls as it bobs.")]
    public float bobHeight = 0.45f;

    [Tooltip("Degrees per second the orb spins. Motion is what makes it read as " +
             "something to collect rather than scenery.")]
    public float spinSpeed = 60f;

    [Header("Motion")]
    [Tooltip("Breathing speed of the active beam, in cycles per second. Zero is static.")]
    public float pulseSpeed = 1.1f;

    [Range(0f, 0.5f)] public float pulseDepth = 0.22f;

    // ---------------------------------------------------------------- internals

    class Marker
    {
        public GameObject root;
        public Renderer beam;
        public Renderer ring;
        public Renderer orb;
        public Material beamMaterial;
        public Material ringMaterial;
        public Material orbMaterial;
        public Color colour;
    }

    readonly List<Marker> markers = new List<Marker>();
    int lastIndex = -1;

    void Start()
    {
        if (runner == null) runner = FindObjectOfType<RouteRunner>();

        if (runner == null)
        {
            Debug.LogWarning("[RouteMarkers] No RouteRunner in the scene: nothing to mark.");
            enabled = false;
            return;
        }

        if (viewCamera == null) viewCamera = Camera.main;

        Build();

        if (offScreenArrow != null) offScreenArrow.gameObject.SetActive(false);

        Debug.Log("[RouteMarkers] Built " + markers.Count + " waypoint beacon(s).");
    }

    void Build()
    {
        for (int i = 0; i < runner.WaypointCount; i++)
        {
            RouteWaypoint waypoint = runner.WaypointAt(i);
            if (waypoint == null) { markers.Add(null); continue; }

            markers.Add(CreateMarker(waypoint));
        }
    }

    Marker CreateMarker(RouteWaypoint waypoint)
    {
        Marker marker = new Marker { colour = waypoint.GizmoColour };

        marker.root = new GameObject("Beacon_" + waypoint.name);
        marker.root.transform.SetParent(transform, false);
        marker.root.transform.position = waypoint.transform.position;

        // ---- beam --------------------------------------------------------------
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "Beam";
        beam.transform.SetParent(marker.root.transform, false);

        // A Unity cylinder is 2 units tall about its centre, so half the height
        // puts its base on the ground.
        beam.transform.localPosition = new Vector3(0f, beamHeight * 0.5f, 0f);
        beam.transform.localScale = new Vector3(beamRadius * 2f, beamHeight * 0.5f, beamRadius * 2f);

        StripCollider(beam);

        marker.beam = beam.GetComponent<Renderer>();
        marker.beamMaterial = CreateTransparentMaterial(marker.colour, activeAlpha);
        marker.beam.sharedMaterial = marker.beamMaterial;

        // ---- ground ring --------------------------------------------------------
        if (showGroundRings)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(marker.root.transform, false);

            // Flattened to a disc, lifted just clear of the terrain so it does not
            // fight the ground mesh for depth.
            ring.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            ring.transform.localScale = new Vector3(waypoint.radius * 2f, 0.02f, waypoint.radius * 2f);

            StripCollider(ring);

            marker.ring = ring.GetComponent<Renderer>();
            marker.ringMaterial = CreateTransparentMaterial(marker.colour, ringAlpha);
            marker.ring.sharedMaterial = marker.ringMaterial;
        }

        // ---- the orb --------------------------------------------------------------
        //
        // The thing you actually aim at. A beam tells you where a waypoint is from
        // half a mile away, and a ring tells you where its edge is, but neither reads
        // as something to go and get. A big solid ball floating at windscreen height
        // does, and it turns following a route into collecting things, which is a far
        // clearer instruction than a line on a map.
        //
        // Nearly opaque on purpose. The beam and ring are deliberately faint so they
        // do not hide the road; the orb is the opposite, and is meant to be the most
        // obvious object on screen.
        if (showOrbs)
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Orb";
            orb.transform.SetParent(marker.root.transform, false);
            orb.transform.localPosition = new Vector3(0f, orbHeight, 0f);
            orb.transform.localScale = Vector3.one * orbRadius * 2f;

            // No collider. Driving through it must not be a collision, and a
            // collision is an automatic failure on the road test.
            StripCollider(orb);

            marker.orb = orb.GetComponent<Renderer>();
            marker.orbMaterial = CreateTransparentMaterial(marker.colour, orbAlpha);
            marker.orb.sharedMaterial = marker.orbMaterial;
        }

        return marker;
    }

    static void StripCollider(GameObject go)
    {
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
    }

    /// <summary>
    /// An unlit transparent material, built at runtime so there is no asset to ship.
    ///
    /// URP's Lit and Unlit shaders default to opaque, and switching them to
    /// transparent takes more than setting an alpha: the surface type, the blend
    /// mode, depth writing and the render queue all have to move together, and the
    /// keyword has to be enabled or the shader compiles the opaque path anyway.
    /// </summary>
    static Material CreateTransparentMaterial(Color colour, float alpha)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);

        Color rgba = new Color(colour.r, colour.g, colour.b, alpha);

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", rgba);
        if (material.HasProperty("_Color")) material.SetColor("_Color", rgba);

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);   // transparent
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);       // alpha
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        return material;
    }

    // ------------------------------------------------------------------ update

    void Update()
    {
        if (runner == null) return;

        int current = runner.CurrentIndex;

        if (current != lastIndex)
        {
            RefreshVisibility(current);
            lastIndex = current;
        }

        Pulse(current);
        Animate();
        UpdateArrow(current);
    }

    /// <summary>
    /// Bobs and spins every visible orb.
    ///
    /// Both together, because either alone is not enough: a sphere spinning on its
    /// own axis is invisible without a texture to see turning, and one that only bobs
    /// reads as floating debris. Rotating while rising and falling is what makes it
    /// look like something placed there to be collected.
    /// </summary>
    void Animate()
    {
        if (!showOrbs) return;

        float spin = Time.time * spinSpeed;

        for (int i = 0; i < markers.Count; i++)
        {
            Marker marker = markers[i];
            if (marker == null || marker.orb == null) continue;
            if (marker.root == null || !marker.root.activeSelf) continue;

            Transform orb = marker.orb.transform;

            // Offset by index so a row of orbs does not bob in unison like a wave.
            float phase = Mathf.Sin((Time.time * 1.6f) + i * 0.7f) * bobHeight;

            orb.localPosition = new Vector3(0f, orbHeight + phase, 0f);
            orb.localRotation = Quaternion.Euler(0f, spin, spin * 0.4f);
        }
    }

    void RefreshVisibility(int current)
    {
        for (int i = 0; i < markers.Count; i++)
        {
            Marker marker = markers[i];
            if (marker == null || marker.root == null) continue;

            bool passed = i < current;
            bool visible = !passed && i <= current + lookAhead;

            marker.root.SetActive(visible);

            if (!visible) continue;

            float alpha = i == current ? activeAlpha : upcomingAlpha;
            SetAlpha(marker.beamMaterial, marker.colour, alpha);

            if (marker.ringMaterial != null)
                SetAlpha(marker.ringMaterial, marker.colour,
                         i == current ? ringAlpha : ringAlpha * 0.4f);

            // The one you are going for is solid. The ones behind it are ghosts, so
            // there is never any doubt which ball is next.
            if (marker.orbMaterial != null)
                SetAlpha(marker.orbMaterial, marker.colour,
                         i == current ? orbAlpha : orbAlpha * 0.25f);
        }
    }

    void Pulse(int current)
    {
        if (pulseSpeed <= 0f || pulseDepth <= 0f) return;
        if (current < 0 || current >= markers.Count) return;

        Marker marker = markers[current];
        if (marker == null || marker.beamMaterial == null) return;

        // A slow sine, not a flash. The beam should read as alive without dragging
        // the eye away from the road.
        float wave = (Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        float alpha = activeAlpha * (1f - pulseDepth + pulseDepth * wave);

        SetAlpha(marker.beamMaterial, marker.colour, alpha);
    }

    static void SetAlpha(Material material, Color colour, float alpha)
    {
        if (material == null) return;

        Color rgba = new Color(colour.r, colour.g, colour.b, alpha);

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", rgba);
        if (material.HasProperty("_Color")) material.SetColor("_Color", rgba);
    }

    // ------------------------------------------------------------------- arrow

    /// <summary>
    /// Pins an arrow to the edge of the screen pointing at the next waypoint
    /// whenever it is not in view.
    ///
    /// The awkward case is a target behind the camera: WorldToScreenPoint still
    /// returns a coordinate for it, but a mirrored one, so the arrow points the
    /// wrong way. Negative z is the tell, and flipping the vector through the
    /// screen centre corrects it.
    /// </summary>
    void UpdateArrow(int current)
    {
        if (offScreenArrow == null) return;

        RouteWaypoint target = runner.CurrentWaypoint;

        if (target == null || runner.RouteComplete)
        {
            offScreenArrow.gameObject.SetActive(false);
            return;
        }

        if (viewCamera == null || !viewCamera.isActiveAndEnabled)
        {
            viewCamera = Camera.main;
            if (viewCamera == null) return;
        }

        Vector3 screen = viewCamera.WorldToScreenPoint(target.transform.position + Vector3.up * 2f);
        Vector2 centre = new Vector2(Screen.width, Screen.height) * 0.5f;

        bool behind = screen.z < 0f;

        Vector2 point = new Vector2(screen.x, screen.y);
        if (behind) point = centre - (point - centre);

        const float margin = 90f;

        bool onScreen = !behind
                        && point.x > margin && point.x < Screen.width - margin
                        && point.y > margin && point.y < Screen.height - margin;

        if (onScreen)
        {
            offScreenArrow.gameObject.SetActive(false);
            return;
        }

        offScreenArrow.gameObject.SetActive(true);

        // Push the arrow out to an ellipse just inside the screen edge, along the
        // direction of the target.
        Vector2 direction = (point - centre).normalized;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.up;

        Vector2 radius = new Vector2(Screen.width * 0.5f - margin, Screen.height * 0.5f - margin);
        offScreenArrow.position = centre + new Vector2(direction.x * radius.x, direction.y * radius.y);

        // The arrow sprite points up at zero rotation, so subtract the 90 degrees.
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        offScreenArrow.localRotation = Quaternion.Euler(0f, 0f, angle);

        Image image = offScreenArrow.GetComponent<Image>();
        if (image != null) image.color = target.GizmoColour;
    }

    void OnDestroy()
    {
        // Materials created with `new` are not owned by the scene, so they leak
        // unless they are cleaned up explicitly.
        foreach (Marker marker in markers)
        {
            if (marker == null) continue;
            if (marker.beamMaterial != null) Destroy(marker.beamMaterial);
            if (marker.ringMaterial != null) Destroy(marker.ringMaterial);
        }
    }
}
