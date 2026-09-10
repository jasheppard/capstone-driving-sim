using UnityEngine;

/// <summary>
/// Blinking indicator lamps on the four corners of the bus.
///
/// The road test scores whether you signalled, and until now the only evidence that
/// an indicator was on was a boolean nobody could see. That is a bad way to teach a
/// habit: the driver presses Q, nothing happens, and they stop bothering.
///
/// The lamps are generated rather than modelled, at the corners of the vehicle's own
/// collider, so they land in the right place on any bus without the artist having
/// modelled indicators or named anything in particular.
///
/// They flash at about 1.5 Hz, which is the middle of the range road vehicles are
/// required to use (most regulations allow 60 to 120 flashes per minute).
/// </summary>
public class BusIndicators : MonoBehaviour
{
    [Header("Flashing")]
    [Tooltip("Flashes per second. Real vehicles run between 1 and 2.")]
    public float flashesPerSecond = 1.5f;

    [Header("Lamps")]
    public float lampRadius = 0.16f;

    [Tooltip("How far the lit lamp glows, in metres. Zero means the sphere brightens " +
             "but casts no light.")]
    public float glowRange = 4f;

    [Tooltip("Nudges the lamps in from the corners of the collider, in metres, so " +
             "they sit on the bodywork rather than floating at its extremes.")]
    public float inset = 0.12f;

    static readonly Color On = new Color(1f, 0.62f, 0.05f);
    static readonly Color Off = new Color(0.20f, 0.11f, 0.02f);

    VehicleController vehicle;
    Collider hull;

    Renderer frontLeft, frontRight, rearLeft, rearRight;
    Light glowLeft, glowRight;

    void Start()
    {
        vehicle = GetComponent<VehicleController>();

        if (vehicle == null)
        {
            Debug.LogWarning("[BusIndicators] No VehicleController, so there is no " +
                             "indicator state to show.");
            enabled = false;
            return;
        }

        foreach (Collider candidate in GetComponentsInChildren<Collider>())
        {
            if (candidate.isTrigger) continue;
            if (hull == null || candidate.bounds.size.sqrMagnitude > hull.bounds.size.sqrMagnitude)
                hull = candidate;
        }

        Build();
    }

    void Build()
    {
        // Corners of the vehicle in its own local space, so the lamps stay put when
        // it turns.
        Vector3 size = hull != null
            ? transform.InverseTransformVector(hull.bounds.size)
            : new Vector3(2f, 2f, 4f);

        Vector3 centre = hull != null
            ? transform.InverseTransformPoint(hull.bounds.center)
            : Vector3.zero;

        float x = Mathf.Abs(size.x) * 0.5f - inset;
        float z = Mathf.Abs(size.z) * 0.5f - inset;
        float y = centre.y;

        frontLeft = Lamp("IndicatorFrontLeft", new Vector3(-x, y, z));
        frontRight = Lamp("IndicatorFrontRight", new Vector3(x, y, z));
        rearLeft = Lamp("IndicatorRearLeft", new Vector3(-x, y, -z));
        rearRight = Lamp("IndicatorRearRight", new Vector3(x, y, -z));

        glowLeft = Glow("IndicatorGlowLeft", new Vector3(-x, y, 0f));
        glowRight = Glow("IndicatorGlowRight", new Vector3(x, y, 0f));
    }

    Renderer Lamp(string name, Vector3 localPosition)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = Vector3.one * lampRadius * 2f;

        // No collider. A lamp the world can hit would register as a collision, and a
        // collision is an automatic failure.
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        Renderer renderer = go.GetComponent<Renderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        // A material each, or all four share one and flash together.
        renderer.sharedMaterial = new Material(shader);
        return renderer;
    }

    Light Glow(string name, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = On;
        light.range = glowRange;
        light.intensity = 0f;

        return light;
    }

    void Update()
    {
        if (frontLeft == null) return;

        // Both sides lit at once is a hazard flash, which is what you get by pressing
        // both, so left and right are evaluated independently.
        float period = 1f / Mathf.Max(0.1f, flashesPerSecond);
        bool bright = Mathf.Repeat(Time.time, period) < period * 0.5f;

        bool left = vehicle.IndicatorLeft && bright;
        bool right = vehicle.IndicatorRight && bright;

        Set(frontLeft, left);
        Set(rearLeft, left);
        Set(frontRight, right);
        Set(rearRight, right);

        if (glowLeft != null) glowLeft.intensity = left ? 2.5f : 0f;
        if (glowRight != null) glowRight.intensity = right ? 2.5f : 0f;
    }

    static void Set(Renderer renderer, bool lit)
    {
        if (renderer == null || renderer.sharedMaterial == null) return;

        Color colour = lit ? On : Off;
        renderer.sharedMaterial.color = colour;

        // _BaseColor is URP's name for it, _Color the built-in one. Setting both
        // means this works whichever shader was found.
        if (renderer.sharedMaterial.HasProperty("_BaseColor"))
            renderer.sharedMaterial.SetColor("_BaseColor", colour);
    }
}
