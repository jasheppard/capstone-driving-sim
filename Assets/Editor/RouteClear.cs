using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>
/// Throws away a route and starts again.
///
/// Laying out a route is trial and error, and the first few attempts are usually
/// wrong: waypoints somewhere the bus cannot reach, points dropped in the wrong
/// order, a Finish put down by accident halfway round. Without a way to wipe it, the
/// mistakes accumulate in the scene and in the Routes folder, and the next import
/// quietly picks up an old file.
///
/// So this clears both halves: the objects in the scene, and the recorded files on
/// disk that an import would read back.
/// </summary>
public static class RouteClear
{
    const string RouteName = "Route";
    const string OutputFolder = "Routes";

    [MenuItem("NCAT/Route/Clear Route", false, 122)]
    public static void Clear()
    {
        GameObject route = GameObject.Find(RouteName);
        int waypoints = 0;

        if (route != null)
        {
            for (int i = 0; i < route.transform.childCount; i++)
            {
                if (route.transform.GetChild(i).GetComponent<RouteWaypoint>() != null)
                    waypoints++;
            }
        }

        string[] files = RecordedFiles();

        if (route == null && files.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Nothing to clear",
                "There is no Route object in the scene and no recorded route files.",
                "OK");
            return;
        }

        // Asked rather than assumed. Deleting a route someone spent half an hour
        // driving, with no warning and no undo for the files, is not something to do
        // on a mis-click.
        bool confirmed = EditorUtility.DisplayDialog(
            "Clear the route?",
            "This removes:\n\n" +
            "  " + waypoints + " waypoints from the scene\n" +
            "  " + files.Length + " recorded route " +
            (files.Length == 1 ? "file" : "files") + " from the " + OutputFolder +
            " folder\n\n" +
            "Any signs placed on those waypoints go with them. This cannot be undone " +
            "for the files.",
            "Clear it", "Cancel");

        if (!confirmed) return;

        if (route != null)
        {
            // Undo-able, so the scene half can be brought back with Ctrl+Z even
            // though the files cannot.
            Undo.DestroyObjectImmediate(route);
            EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }

        int deleted = 0;

        foreach (string file in files)
        {
            try { File.Delete(file); deleted++; }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RouteClear] Could not delete " + file + ": " + e.Message);
            }
        }

        Debug.Log(
            "Route cleared.\n" +
            "  " + waypoints + " waypoints removed from the scene\n" +
            "  " + deleted + " recorded " + (deleted == 1 ? "file" : "files") + " deleted\n" +
            "Save the scene (Ctrl+S), then Route > 1. Arm Route Recording to start again.");
    }

    /// <summary>
    /// Removes the placed sign models but leaves the waypoints alone.
    ///
    /// Useful when the signs came out facing the wrong way or floating above
    /// unloaded terrain: there is no need to re-drive anything, only to place them
    /// again once the tiles are in.
    /// </summary>
    [MenuItem("NCAT/Route/Clear Placed Signs", false, 121)]
    public static void ClearSigns()
    {
        GameObject route = GameObject.Find(RouteName);
        Transform signs = route != null ? route.transform.Find("Signs") : null;

        if (signs == null)
        {
            Debug.Log("[RouteClear] No placed signs to clear.");
            return;
        }

        int count = signs.childCount;
        Undo.DestroyObjectImmediate(signs.gameObject);

        EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[RouteClear] Removed " + count + " placed signs. The waypoints are " +
                  "untouched, so Route > 4. Place Signs will put them back.");
    }

    /// <summary>
    /// Puts ghost mode on the bus, so scanned trees and kerbs stop blocking the
    /// route you are trying to lay out.
    ///
    /// Left as a separate step rather than added by the car setup, because a bus that
    /// can drive through walls is not something to have on by accident. Adding it is
    /// a decision; the component still has to be toggled with G in play mode.
    /// </summary>
    [MenuItem("NCAT/Route/Add Ghost Drive", false, 111)]
    public static void AddGhostDrive()
    {
        GameObject car = GameObject.Find("PlayerCar");

        if (car == null)
        {
            Debug.LogError("[RouteClear] No PlayerCar in the scene. Run " +
                           "NCAT > Set Up Player Car first.");
            return;
        }

        if (car.GetComponent<GhostDrive>() != null)
        {
            Debug.Log("[RouteClear] PlayerCar already has Ghost Drive. Press G in play " +
                      "mode to toggle it.");
            return;
        }

        Undo.AddComponent<GhostDrive>(car);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Selection.activeGameObject = car;

        Debug.Log(
            "Ghost Drive added to PlayerCar.\n" +
            "  Press G in play mode to float through scenery, G again to land\n" +
            "  Using it switches telemetry and the drive report off for that run,\n" +
            "  because a drive through solid objects is not a road test\n" +
            "Save the scene (Ctrl+S).");
    }

    static string[] RecordedFiles()
    {
        // Application.dataPath is <project>/Assets, so its parent is the project root.
        string root = Path.GetDirectoryName(Application.dataPath);
        string folder = Path.Combine(root, OutputFolder);

        if (!Directory.Exists(folder)) return new string[0];

        return Directory.GetFiles(folder, "route_*.json");
    }
}
