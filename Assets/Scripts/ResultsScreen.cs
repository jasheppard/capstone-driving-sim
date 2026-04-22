using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class ResultsScreen : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI objectivesText;
    public TextMeshProUGUI infractionsText;
    public GameObject resultsPanel;

    [Header("Buttons")]
    public Button retryButton;
    public Button mainMenuButton;

    void Start()
    {
        resultsPanel.SetActive(false);
        retryButton.onClick.AddListener(RetryScenario);
        mainMenuButton.onClick.AddListener(GoToMainMenu);
    }

    public void ShowResults(bool passed, int score, float timeRemaining, 
                           float timeLimit, int objectives, int totalObjectives, 
                           int infractions)
    {
        resultsPanel.SetActive(true);

        // Title
        titleText.text = passed ? "Scenario Complete!" : "Scenario Failed";
        titleText.color = passed ? 
            new Color(0.2f, 1f, 0.2f) : new Color(1f, 0.2f, 0.2f);

        // Score
        scoreText.text = "Score: " + score;

        // Time
        float timeTaken = timeLimit - timeRemaining;
        int minutes = Mathf.FloorToInt(timeTaken / 60);
        int seconds = Mathf.FloorToInt(timeTaken % 60);
        timeText.text = "Time: " + minutes + ":" + seconds.ToString("00");

        // Objectives
        objectivesText.text = "Objectives: " + objectives + "/" + totalObjectives;

        // Infractions
        infractionsText.text = "Infractions: " + infractions;
    }

    void RetryScenario()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void GoToMainMenu(){
    Time.timeScale = 1f;
    SceneManager.LoadScene("MainMenu");
}
}