using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Adds step assist to the bus, and tells you what it will and will not do.
/// </summary>
public static class StepAssistSetup
{
    [MenuItem("NCAT/Set Up Step Assist", false, 54)]
    public static void SetUp()
    {
        GameObject car = GameObject.Find("PlayerCar");

        if (car == null)
        {
            Debug.LogError("[StepAssistSetup] No PlayerCar in the scene. Run " +
                           "NCAT > Set Up Player Car first.");
            return;
        }

        StepAssist assist = car.GetComponent<StepAssist>();
        bool existing = assist != null;

        if (!existing) assist = Undo.AddComponent<StepAssist>(car);

        EditorUtility.SetDirty(assist);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Selection.activeGameObject = car;

        Debug.Log(
            (existing ? "Step assist already on PlayerCar." : "Step assist added to PlayerCar.") + "\n" +
            "  The bus now climbs anything under " + assist.maxStepHeight + " m: kerbs, " +
            "scan ripples, low bushes\n" +
            "  Buildings, walls and parked cars still stop it, because a ray at knee " +
            "height still hits them\n" +
            "  Pedestrians are excluded, so striking one is still an automatic failure\n" +
            "  Raise maxStepHeight if the route is still blocked, lower it if the bus " +
            "starts climbing things it should not\n" +
            "Save the scene (Ctrl+S).");
    }
}
