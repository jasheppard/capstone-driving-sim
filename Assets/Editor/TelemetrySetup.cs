using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// Attaches the telemetry logger to the player vehicle, and gives you a quick way
/// to get at the CSV files it writes.
/// </summary>
public static class TelemetrySetup
{
    const string CarName = "PlayerCar";
    const string DataFolder = "TelemetryData";

    [MenuItem("NCAT/Set Up Telemetry Logging", false, 50)]
    public static void SetUpLogging()
    {
        GameObject car = GameObject.Find(CarName);

        if (car == null)
        {
            Debug.LogError("No " + CarName + " in the scene. Run NCAT → Set Up Player Car first.");
            return;
        }

        DrivingTelemetryLogger logger = car.GetComponent<DrivingTelemetryLogger>();

        if (logger == null)
        {
            logger = Undo.AddComponent<DrivingTelemetryLogger>(car);
            Debug.Log("Added DrivingTelemetryLogger to " + CarName + ".");
        }
        else
        {
            Debug.Log("DrivingTelemetryLogger was already on " + CarName + ".");
        }

        EditorUtility.SetDirty(logger);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Selection.activeGameObject = car;

        Debug.Log(
            "Telemetry ready.\n" +
            "  Press Play, drive for a few minutes, then press Stop.\n" +
            "  Stopping is what closes the file: the CSV is incomplete until you do.\n" +
            "  Find it with NCAT → Open Telemetry Folder.\n" +
            "Remember to save the scene (Ctrl+S), or the component will not be there next time.");
    }

    [MenuItem("NCAT/Open Telemetry Folder", false, 51)]
    public static void OpenFolder()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string folder = Path.Combine(root, DataFolder);

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            Debug.Log("Created " + folder + ". It is empty until you record a session.");
        }

        EditorUtility.RevealInFinder(folder + Path.DirectorySeparatorChar);
    }
}
