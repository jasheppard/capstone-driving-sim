using UnityEngine;

public class VehicleController : MonoBehaviour
{
    [Header("Car Settings")]
    public float accelerationForce = 15000f;
    public float reverseForce = 8000f;
    public float turnSpeed = 60f;
    public float maxSpeed = 30f;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.drag = 1f;
        rb.angularDrag = 5f;
        rb.centerOfMass = new Vector3(0, -0.5f, 0);
    }

    void FixedUpdate()
    {
        float currentSpeed = rb.velocity.magnitude * 2.237f;

        // Forward
        if (Input.GetKey(KeyCode.W) && currentSpeed < maxSpeed)
        {
            rb.AddForce(transform.forward * accelerationForce, ForceMode.Force);
        }

        // Reverse
        if (Input.GetKey(KeyCode.S))
        {
            rb.AddForce(-transform.forward * reverseForce, ForceMode.Force);
        }

        // Steering
        if (currentSpeed > 1f)
        {
            if (Input.GetKey(KeyCode.A))
            {
                transform.Rotate(0, -turnSpeed * Time.fixedDeltaTime, 0);
            }
            if (Input.GetKey(KeyCode.D))
            {
                transform.Rotate(0, turnSpeed * Time.fixedDeltaTime, 0);
            }
        }

        // Brake
        if (Input.GetKey(KeyCode.Space))
        {
            rb.velocity = Vector3.Lerp(rb.velocity, Vector3.zero, 0.2f);
        }

        // Keep grounded
        rb.AddForce(Vector3.down * 20f, ForceMode.Acceleration);
    }

    public float GetSpeedMPH()
    {
        return rb.velocity.magnitude * 2.237f;
    }
}