using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the vehicle inside the campus boundary.
///
/// The tile excluder stops the world being streamed past a radius; on its own that
/// leaves you able to drive off the edge into empty space, which looks like a bug
/// rather than a boundary. This is the other half: an invisible soft wall at the
/// same radius.
///
/// It resists rather than blocks. Hitting a hard collider at 30 mph flips an arcade
/// car; cancelling the outward part of the velocity and pushing gently inward reads
/// as "the road doesn't go that way" and leaves the player in control.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CampusBoundary : MonoBehaviour
{
    [Header("Boundary")]
    [Tooltip("Centre of the campus circle. Should match the CampusTileExcluder, or " +
             "the world will end before the wall does.")]
    public Transform centre;

    [Tooltip("Metres from the centre. Set slightly inside the excluder's radius so " +
             "you are turned back while there is still ground under you.")]
    public float radius = 750f;

    [Tooltip("How far before the edge the warning appears.")]
    public float warningDistance = 80f;

    [Header("Feel")]
    [Tooltip("Inward acceleration applied past the edge, in m/s^2. Higher turns you " +
             "back faster and more abruptly.")]
    public float pushBack = 12f;

    [Header("UI")]
    [Tooltip("Optional. Set by NCAT → Set Up Campus Boundary.")]
    public Text warningLabel;

    public string warningText = "Approaching campus boundary";
    public string blockedText = "Campus boundary";

    Rigidbody body;

    void Start()
    {
        body = GetComponent<Rigidbody>();

        if (centre == null)
        {
            // Where the car starts is a reasonable centre if nothing else is set.
            GameObject anchor = new GameObject("CampusCentre");
            anchor.transform.position = transform.position;
            centre = anchor.transform;

            Debug.LogWarning("[CampusBoundary] No centre assigned: using the car's " +
                             "start position. Set one for a stable boundary.");
        }

        if (warningLabel != null) warningLabel.enabled = false;
    }

    void FixedUpdate()
    {
        if (centre == null) return;

        // Horizontal only. Height differences across campus are irrelevant to a
        // boundary, and including them would shrink the circle on a hill.
        Vector3 offset = transform.position - centre.position;
        offset.y = 0f;

        float distance = offset.magnitude;

        if (distance > radius)
        {
            Vector3 inward = -offset.normalized;

            // Remove the part of the velocity heading further out, so the car stops
            // gaining ground rather than being yanked backwards.
            float outwardSpeed = Vector3.Dot(body.velocity, -inward);
            if (outwardSpeed > 0f) body.velocity += inward * outwardSpeed;

            body.AddForce(inward * pushBack, ForceMode.Acceleration);
        }

        UpdateWarning(distance);
    }

    void UpdateWarning(float distance)
    {
        if (warningLabel == null) return;

        if (distance > radius)
        {
            warningLabel.enabled = true;
            warningLabel.text = blockedText;
            warningLabel.color = new Color(0.95f, 0.3f, 0.2f);
        }
        else if (distance > radius - warningDistance)
        {
            warningLabel.enabled = true;
            warningLabel.text = warningText;

            // Fade up as you close on the edge, so it is a gradient rather than a
            // label that snaps on and startles you.
            float t = (distance - (radius - warningDistance)) / warningDistance;
            warningLabel.color = new Color(1f, 0.85f, 0.2f, Mathf.Clamp01(0.35f + t));
        }
        else
        {
            warningLabel.enabled = false;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (centre == null) return;

        Gizmos.color = new Color(0.95f, 0.3f, 0.2f, 0.9f);

        const int segments = 72;
        Vector3 previous = centre.position + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = centre.position + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
