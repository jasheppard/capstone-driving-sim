using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    public PlayerProfile currentProfile;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadOrCreateProfile();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void LoadOrCreateProfile()
    {
        if (SaveSystem.SaveExists())
        {
            currentProfile = SaveSystem.LoadProfile();
        }
        else
        {
            currentProfile = new PlayerProfile();
            currentProfile.playerName = "Player";
            currentProfile.totalScore = 0;
            currentProfile.sessionsCompleted = 0;
            SaveSystem.SaveProfile(currentProfile);
        }
    }

    public void SaveScenarioResult(string scenarioName, int score, 
                                   bool passed, int infractions, float timeTaken)
    {
        ScenarioResult result = new ScenarioResult();
        result.scenarioName = scenarioName;
        result.score = score;
        result.passed = passed;
        result.infractions = infractions;
        result.timeTaken = timeTaken;
        result.dateCompleted = System.DateTime.Now.ToString("MM/dd/yyyy HH:mm");

        currentProfile.scenarioResults.Add(result);
        currentProfile.totalScore += score;
        currentProfile.sessionsCompleted++;
        currentProfile.lastSessionDate = result.dateCompleted;

        SaveSystem.SaveProfile(currentProfile);
        Debug.Log("Scenario result saved. Total score: " + currentProfile.totalScore);
    }

    public PlayerProfile GetProfile()
    {
        return currentProfile;
    }
}