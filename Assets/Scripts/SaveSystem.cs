using UnityEngine;
using System.IO;

public static class SaveSystem
{
    private static string GetSavePath()
    {
        return Path.Combine(Application.persistentDataPath, "playerprofile.json");
    }

    public static void SaveProfile(PlayerProfile profile)
    {
        string path = GetSavePath();
        string json = JsonUtility.ToJson(profile, true);
        File.WriteAllText(path, json);
        Debug.Log("Profile saved to: " + path);
    }

    public static PlayerProfile LoadProfile()
    {
        string path = GetSavePath();
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            PlayerProfile profile = JsonUtility.FromJson<PlayerProfile>(json);
            Debug.Log("Profile loaded for: " + profile.playerName);
            return profile;
        }
        else
        {
            Debug.Log("No save file found at: " + path);
            return null;
        }
    }

    public static bool SaveExists()
    {
        return File.Exists(GetSavePath());
    }

    public static void DeleteSave()
    {
        string path = GetSavePath();
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log("Save file deleted");
        }
    }
}