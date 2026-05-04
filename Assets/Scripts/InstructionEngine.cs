using UnityEngine;
using System.Collections.Generic;

public class InstructionEngine : MonoBehaviour
{
    [Header("References")]
    public HUDController hud;
    public Rigidbody carRigidbody;

    [Header("Speed Settings")]
    public float speedLimitMPH = 35f;
    public float speedWarningCooldown = 10f;

    private float speedWarningTimer = 0f;
    private float infractionCount = 0f;
    private bool signalOn = false;
    private bool turningWithoutSignal = false;

    void Update()
    {
        CheckSpeed();
        CheckTurnSignal();
    }

    void CheckSpeed()
    {
        float speed = carRigidbody.velocity.magnitude * 2.237f;

        if (speedWarningTimer > 0)
        {
            speedWarningTimer -= Time.deltaTime;
        }

        if (speed > speedLimitMPH && speedWarningTimer <= 0)
        {
            TriggerInfraction(
                "You are exceeding the speed limit! Slow down.",
                "SPEEDING",
                "WARNING"
            );
            speedWarningTimer = speedWarningCooldown;
        }
    }

    void CheckTurnSignal()
    {
    float steerInput = Input.GetAxis("Horizontal");
    bool isTurning = Mathf.Abs(steerInput) > 0.5f;

    if (Input.GetKeyDown(KeyCode.E))
    {
        signalOn = true;
    }

    if (Input.GetKeyUp(KeyCode.E))
    {
        signalOn = false;
    }

    if (isTurning && !signalOn && !turningWithoutSignal)
    {
        turningWithoutSignal = true;
        TriggerInfraction(
            "Always use your turn signal before turning!",
            "TURN_SIGNAL_OMITTED",
            "WARNING"
        );
    }

    if (!isTurning)
    {
        turningWithoutSignal = false;
    }
    }

    public void TriggerInfraction(string message, string type, string severity)
    {
        infractionCount++;

        // Log it
        Debug.Log("[INFRACTION] Type: " + type + " | Severity: " + severity);

        // Send to HUD
        hud.ShowInstruction(message, severity);
    }

    public float GetInfractionCount()
    {
        return infractionCount;
    }

    public void ResetInfractions()
    {
        infractionCount = 0;
        speedWarningTimer = 0;
    }
    public void SetSpeedLimit(float newLimit)
{
    speedLimitMPH = newLimit;
    hud.UpdateSpeedLimit(newLimit);
}
}