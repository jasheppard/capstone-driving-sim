using UnityEngine;
using System.IO;

public static class SaveSystem
{
private static string savePath;

static SaveSystem()
{
    savePath = Application.persistentDataPath + "/playerprofile.json";
}
    public static void SaveProfile(PlayerProfile profile)
    {
        string json = JsonUtility.ToJson(profile, true);
        File.WriteAllText(savePath, json);
        Debug.Log("Profile saved to: " + savePath);
    }

    public static PlayerProfile LoadProfile()
    {
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);
            PlayerProfile profile = JsonUtility.FromJson<PlayerProfile>(json);
            Debug.Log("Profile loaded for: " + profile.playerName);
            return profile;
        }
        else
        {
            Debug.Log("No save file found, creating new profile");
            return null;
        }
    }

    public static bool SaveExists()
    {
        return File.Exists(savePath);
    }

    public static void DeleteSave()
    {
        if (File.Exists(savePath))
        {
            File.Delete(savePath);
            Debug.Log("Save file deleted");
        }
    }
}