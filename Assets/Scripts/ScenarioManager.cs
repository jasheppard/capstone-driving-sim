using UnityEngine;
using System.Collections.Generic;

public class ScenarioManager : MonoBehaviour
{
    [Header("Scenario Info")]
    public string scenarioName = "Basic Lane Driving";
    public float timeLimit = 120f;

    [Header("References")]
    public InstructionEngine instructionEngine;
    public HUDController hud;
    public ResultsScreen resultsScreen;

    [Header("Objectives")]
    public int objectivesTotal = 3;
    private int objectivesCompleted = 0;

    private float timeRemaining;
    private bool scenarioActive = false;
    private bool scenarioEnded = false;

    void Start()
    {
        timeRemaining = timeLimit;
        StartScenario();
    }

    void Update()
{
    if (!scenarioActive || scenarioEnded) return;

    timeRemaining -= Time.deltaTime;

    if (timeRemaining <= 0)
    {
        timeRemaining = 0;
        UpdateTimerDisplay();
        EndScenario(false, "Time ran out!");
        return;
    }

    UpdateTimerDisplay();
}

    public void StartScenario()
    {
        scenarioActive = true;
        Time.timeScale = 1f;
        scenarioEnded = false;
        objectivesCompleted = 0;
        timeRemaining = timeLimit;
        instructionEngine.ResetInfractions();
        hud.ShowInstruction("Scenario started! Follow the rules.", "INFO");
        Debug.Log("Scenario started: " + scenarioName);
    }

    public void CompleteObjective()
    {
        objectivesCompleted++;
        Debug.Log("Objective completed: " + objectivesCompleted + "/" + objectivesTotal);
        hud.ShowInstruction("Objective complete! " + objectivesCompleted + "/" + objectivesTotal, "INFO");

        if (objectivesCompleted >= objectivesTotal)
        {
            EndScenario(true, "All objectives complete!");
        }
    }

    public void EndScenario(bool passed, string reason)
{
    if (scenarioEnded) return;
    scenarioEnded = true;
    scenarioActive = false;
    Time.timeScale = 0f;

    int finalScore = CalculateScore(passed);
    float timeTaken = timeLimit - timeRemaining;

    if (GameManager.Instance != null)
    {
        GameManager.Instance.SaveScenarioResult(
            scenarioName,
            finalScore,
            passed,
            (int)instructionEngine.GetInfractionCount(),
            timeTaken
        );
    }

    hud.ShowInstruction(
        passed ? "Scenario Complete! Score: " + finalScore : "Scenario Failed: " + reason,
        passed ? "INFO" : "CRITICAL"
    );

    resultsScreen.ShowResults(
        passed,
        finalScore,
        timeRemaining,
        timeLimit,
        objectivesCompleted,
        objectivesTotal,
        (int)instructionEngine.GetInfractionCount()
    );
}

    int CalculateScore(bool passed)
    {
        if (!passed) return 0;

        int baseScore = 100;
        int infractionPenalty = (int)instructionEngine.GetInfractionCount() * 10;
        int timePenalty = (int)(timeLimit - timeRemaining) / 10;

        int finalScore = Mathf.Max(0, baseScore - infractionPenalty - timePenalty);
        return finalScore;
    }

    void UpdateTimerDisplay()
    {
        int minutes = Mathf.FloorToInt(timeRemaining / 60);
        int seconds = Mathf.FloorToInt(timeRemaining % 60);
        Debug.Log("Time: " + minutes + ":" + seconds.ToString("00"));
    }

    public float GetTimeRemaining()
    {
        return timeRemaining;
    }

    public int GetObjectivesCompleted()
    {
        return objectivesCompleted;
    }
}