using UnityEngine;

/// <summary>
/// Makes a traffic light actually show its colour.
///
/// The downloaded prop is a housing with no working lamps, and its lenses are part
/// of one shared texture atlas, so there is nothing in the model to switch on. Three
/// small emissive spheres are added in front of it instead. That sounds like a
/// bodge, and it is the right one: it works with any model, needs no knowledge of
/// how the artist named their sub-meshes, and reads clearly from a moving vehicle,
/// which is the only place anybody will ever look at it from.
///
/// The state is not stored here. It is read from the RouteWaypoint every frame, so
/// the light on the pole, the gizmo in the Scene view, and the rule that decides
/// whether you ran a red can never disagree with each other.
/// </summary>
[ExecuteAlways]
public class TrafficLightVisual : MonoBehaviour
{
    [Header("Which signal this belongs to")]
    [Tooltip("Set by the sign placer. Without it the lamps stay dark.")]
    public RouteWaypoint waypoint;

    [Header("Lamp placement")]
    [Tooltip("Where the bottom lamp sits, relative to this object. Nudge these to " +
             "line the lamps up with the lenses on your model.")]
    public Vector3 lampOrigin = new Vector3(0f, 3.0f, 0.22f);

    [Tooltip("Distance between lamps, upward.")]
    public float lampSpacing = 0.34f;

    public float lampRadius = 0.13f;

    [Tooltip("How far the lit lamp glows. Zero means no light is cast, only the " +
             "sphere brightens.")]
    public float glowRange = 6f;

    Renderer redLamp, amberLamp, greenLamp;
    Light glow;

    static readonly Color RedOn = new Color(1f, 0.15f, 0.10f);
    static readonly Color AmberOn = new Color(1f, 0.68f, 0.06f);
    static readonly Color GreenOn = new Color(0.15f, 1f, 0.35f);
    static readonly Color Off = new Color(0.06f, 0.06f, 0.07f);

    void OnEnable()
    {
        if (redLamp == null) Build();
    }

    void Build()
    {
        // Top to bottom is red, amber, green, so the lamps are created upward from
        // green at the origin.
        greenLamp = Lamp("LampGreen", 0);
        amberLamp = Lamp("LampAmber", 1);
        redLamp = Lamp("LampRed", 2);

        GameObject glowGO = new GameObject("Glow");
        glowGO.transform.SetParent(transform, false);
        glowGO.transform.localPosition = lampOrigin + Vector3.up * lampSpacing;

        glow = glowGO.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = glowRange;
        glow.intensity = 2f;
    }

    Renderer Lamp(string name, int step)
    {
        Transform existing = transform.Find(name);
        GameObject go = existing != null
            ? existing.gameObject
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = lampOrigin + Vector3.up * (lampSpacing * step);
        go.transform.localScale = Vector3.one * lampRadius * 2f;

        // No collider. A lamp the bus can hit would be a collision, and a collision
        // is an automatic fail on the road test.
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) DestroyImmediate(collider);

        Renderer renderer = go.GetComponent<Renderer>();

        // A material per lamp, created at runtime, so three lamps on one pole do not
        // share one material and all light up together.
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        renderer.sharedMaterial = new Material(shader);
        return renderer;
    }

    void Update()
    {
        if (waypoint == null || redLamp == null) return;

        RouteWaypoint.Light state = waypoint.CurrentLight;

        Set(redLamp, state == RouteWaypoint.Light.Red ? RedOn : Off);
        Set(amberLamp, state == RouteWaypoint.Light.Amber ? AmberOn : Off);
        Set(greenLamp, state == RouteWaypoint.Light.Green ? GreenOn : Off);

        if (glow == null) return;

        glow.color = state == RouteWaypoint.Light.Red ? RedOn
                   : state == RouteWaypoint.Light.Amber ? AmberOn : GreenOn;

        // Brightest on red, because red is the one that costs you the element.
        glow.intensity = state == RouteWaypoint.Light.Red ? 3f : 1.6f;
    }

    static void Set(Renderer renderer, Color colour)
    {
        if (renderer == null || renderer.sharedMaterial == null) return;

        renderer.sharedMaterial.color = colour;

        // _BaseColor is URP's name for it; _Color is the built-in one. Setting both
        // means this works whichever shader was found above.
        if (renderer.sharedMaterial.HasProperty("_BaseColor"))
            renderer.sharedMaterial.SetColor("_BaseColor", colour);
    }
}
