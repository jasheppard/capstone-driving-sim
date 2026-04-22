using UnityEngine;

public class StopZoneTrigger : MonoBehaviour
{
    public InstructionEngine instructionEngine;
    public ScenarioManager scenarioManager;
    public float stopSpeedThreshold = 2f;
    private bool alreadyTriggered = false;

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("Something entered trigger: " + other.name);
        if (other.CompareTag("Player"))
        {
            Debug.Log("Player entered stop zone");
            alreadyTriggered = false;
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player") && !alreadyTriggered)
        {
            Rigidbody rb = other.GetComponent<Rigidbody>();
            if (rb != null)
            {
                float speed = rb.velocity.magnitude * 2.237f;
                if (speed > stopSpeedThreshold)
                {
                    instructionEngine.TriggerInfraction(
                        "You failed to stop! Always come to a complete stop.",
                        "MISSED_STOP_SIGN",
                        "CRITICAL"
                    );
                    alreadyTriggered = true;
                }
                else
                {
                    Debug.Log("Player stopped correctly!");
                    scenarioManager.CompleteObjective();
                    alreadyTriggered = true;
                }
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            alreadyTriggered = false;
        }
    }
}