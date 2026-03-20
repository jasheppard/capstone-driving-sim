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

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -1.5f, 0);
    }

    void Update()
    {
        float speed = rb.velocity.magnitude * 3.6f;

        float accelerate = Input.GetAxis("Vertical");
        float steer = Input.GetAxis("Horizontal");
        bool braking = Input.GetKey(KeyCode.Space);

        // Steering
        wheelFL.steerAngle = maxSteerAngle * steer;
        wheelFR.steerAngle = maxSteerAngle * steer;

        // Motor
        if (speed < maxSpeed && !braking)
        {
            wheelRL.motorTorque = motorTorque * accelerate;
            wheelRR.motorTorque = motorTorque * accelerate;
        }
        else
        {
            wheelRL.motorTorque = 0;
            wheelRR.motorTorque = 0;
        }

        // Braking
        if (braking)
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