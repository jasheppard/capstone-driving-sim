using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;

/// <summary>
/// Runs every setup step, in the right order, and saves the scene.
///
/// WHY THIS EXISTS
///
/// The setup was a dozen menu items that had to be run in a particular order, each
/// followed by Ctrl+S, and every one of them was a chance to forget. Skipping the
/// save was the expensive one: the menu items change the scene in memory only, so
/// pressing Play or reopening the project quietly threw the work away, and the
/// symptom was not an error, it was a game with no scoring in it.
///
/// So this does all of them and saves at the end. It is safe to run repeatedly:
/// every step it calls either reuses what is already there or rebuilds it.
///
/// Each step is wrapped on its own. One failing does not stop the rest, because a
/// missing bus model should not prevent the debrief being built, and the summary at
/// the end says exactly which ones worked.
/// </summary>
public static class SetUpEverything
{
    [MenuItem("NCAT/SET UP EVERYTHING", false, 1)]
    public static void Run()
    {
        StringBuilder report = new StringBuilder();
        int worked = 0, failed = 0;

        // Order matters. Prefabs are assets and have to exist before anything places
        // them; the collider has to be right before the car is measured for anything
        // else; the rubric has to exist before the debrief looks for it.
        Step(report, ref worked, ref failed, "Build sign prefabs",
             SignPrefabBuilder.Build);

        Step(report, ref worked, ref failed, "Fit the vehicle collider",
             VehicleColliderFix.Fix);

        Step(report, ref worked, ref failed, "Tune the vehicle to a campus shuttle",
             VehicleTuning.Shuttle);

        Step(report, ref worked, ref failed, "Road test, ground handling and fault HUD",
             RoadTestSetup.SetUp);

        Step(report, ref worked, ref failed, "Drive debrief",
             DebriefSetup.SetUp);

        Step(report, ref worked, ref failed, "Driver briefing",
             BriefingSetup.SetUp);

        // The step that was always being forgotten.
        bool saved = EditorSceneManager.SaveOpenScenes();

        report.AppendLine(saved
            ? "  OK    Scene saved"
            : "  FAIL  Scene could not be saved. Save it yourself with Ctrl+S.");

        AssetDatabase.SaveAssets();

        Debug.Log(
            "NCAT setup: " + worked + " of " + (worked + failed) + " steps done.\n" +
            report +
            (failed == 0
                ? "\nReady. Press Play, drive for at least 20 seconds, then press Esc " +
                  "for your result."
                : "\nSome steps failed. The message above each one says why."));
    }

    /// <summary>
    /// Runs one step and records what happened.
    ///
    /// Caught individually rather than around the whole run, so a step that throws
    /// takes only itself down. Half a setup with a clear list of what is missing is
    /// far more use than a stack trace and no idea how far it got.
    /// </summary>
    static void Step(StringBuilder report, ref int worked, ref int failed,
                     string name, System.Action action)
    {
        try
        {
            action();
            report.AppendLine("  OK    " + name);
            worked++;
        }
        catch (System.Exception e)
        {
            report.AppendLine("  FAIL  " + name + ": " + e.Message);
            failed++;
        }
    }
}
