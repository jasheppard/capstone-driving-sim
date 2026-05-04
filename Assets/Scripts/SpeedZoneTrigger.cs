using UnityEngine;

public class SpeedZoneTrigger : MonoBehaviour
{
    public InstructionEngine instructionEngine;
    public float newSpeedLimit = 25f;
    public string zoneName = "School Zone";

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            instructionEngine.SetSpeedLimit(newSpeedLimit);
            instructionEngine.TriggerInfraction(
                "Speed limit change: " + newSpeedLimit + " mph — " + zoneName,
                "SPEED_ZONE",
                "INFO"
            );
        }
    }
}