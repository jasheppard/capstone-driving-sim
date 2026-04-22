using System;
using System.Collections.Generic;

[Serializable]
public class ScenarioResult
{
    public string scenarioName;
    public int score;
    public bool passed;
    public int infractions;
    public float timeTaken;
    public string dateCompleted;
}

[Serializable]
public class PlayerProfile
{
    public string playerName;
    public int totalScore;
    public int sessionsCompleted;
    public string lastSessionDate;
    public List<ScenarioResult> scenarioResults = new List<ScenarioResult>();
}