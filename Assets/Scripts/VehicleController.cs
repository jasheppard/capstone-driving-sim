using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VehicleController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("Car Settings")]
    public float motorTorque = 400f;
    public float brakeTorque = 3000f;
    public float maxSteerAngle = 35f;
    public float maxSpeed = 60f;

    private Rigidbody rb;
    private float accelerateInput;
    private float steerInput;
    private bool brakingInput;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -1.0f, 0);
        rb.drag = 0.1f;
        rb.angularDrag = 0.5f;
    }

    void Update()
    {
        accelerateInput = Input.GetAxis("Vertical");
        steerInput = Input.GetAxis("Horizontal");
        brakingInput = Input.GetKey(KeyCode.Space);
    }

    void FixedUpdate()
    {
        float speed = rb.velocity.magnitude * 3.6f;

        // Steering
        wheelFL.steerAngle = maxSteerAngle * steerInput;
        wheelFR.steerAngle = maxSteerAngle * steerInput;

        // Motor
        if (speed < maxSpeed && !brakingInput)
        {
            wheelRL.motorTorque = motorTorque * accelerateInput;
            wheelRR.motorTorque = motorTorque * accelerateInput;
        }
        else
        {
            wheelRL.motorTorque = 0;
            wheelRR.motorTorque = 0;
        }

        // Braking
        if (brakingInput)
        {
            wheelFL.brakeTorque = brakeTorque;
            wheelFR.brakeTorque = brakeTorque;
            wheelRL.brakeTorque = brakeTorque;
            wheelRR.brakeTorque = brakeTorque;
        }
        else
        {
            wheelFL.brakeTorque = 0;
            wheelFR.brakeTorque = 0;
            wheelRL.brakeTorque = 0;
            wheelRR.brakeTorque = 0;
        }
    }
}