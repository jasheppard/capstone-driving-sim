using UnityEngine;
using TMPro;

public class HUDController : MonoBehaviour
{
    [Header("HUD Elements")]
    public TextMeshProUGUI speedometerText;
    public TextMeshProUGUI speedLimitText;
    public GameObject instructionCard;
    public TextMeshProUGUI instructionText;
    public TextMeshProUGUI timerText;

    [Header("References")]
    public Rigidbody carRigidbody;
    public ScenarioManager scenarioManager;

    private float instructionTimer = 0f;
    private float instructionDuration = 4f;

    void Update()
    {
        UpdateSpeedometer();
        UpdateInstructionCard();

        if (scenarioManager != null)
        {
            UpdateTimer(scenarioManager.GetTimeRemaining());
        }
    }

    void UpdateSpeedometer()
{
    VehicleController vc = carRigidbody.GetComponent<VehicleController>();
    if (vc != null)
    {
        speedometerText.text = Mathf.RoundToInt(vc.GetSpeedMPH()) + " mph";
    }
}

    void UpdateInstructionCard()
    {
        if (instructionTimer > 0)
        {
            instructionTimer -= Time.deltaTime;
            instructionCard.SetActive(true);
        }
        else
        {
            instructionCard.SetActive(false);
        }
    }

    public void ShowInstruction(string message, string severity)
    {
        instructionText.text = message;
        instructionTimer = instructionDuration;

        if (severity == "WARNING")
        {
            instructionCard.GetComponent<UnityEngine.UI.Image>().color =
                new Color(1f, 0.8f, 0f, 0.85f);
        }
        else if (severity == "CRITICAL")
        {
            instructionCard.GetComponent<UnityEngine.UI.Image>().color =
                new Color(1f, 0.2f, 0.2f, 0.85f);
        }
        else
        {
            instructionCard.GetComponent<UnityEngine.UI.Image>().color =
                new Color(0.2f, 0.5f, 1f, 0.85f);
        }
    }

    public void UpdateTimer(float timeRemaining)
    {
        int minutes = Mathf.FloorToInt(timeRemaining / 60);
        int seconds = Mathf.FloorToInt(timeRemaining % 60);
        timerText.text = minutes + ":" + seconds.ToString("00");
    }

    public void UpdateSpeedLimit(float newLimit)
{
    speedLimitText.text = "Speed Limit: " + newLimit + " mph";
}
}