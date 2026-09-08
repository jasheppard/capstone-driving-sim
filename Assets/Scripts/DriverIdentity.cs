using UnityEngine;
using System.Text;

/// <summary>
/// Who is driving.
///
/// The main menu already writes the name typed into its box to PlayerPrefs under
/// "PlayerName", so nothing new has to be captured. This just reads it back and
/// makes it safe to put in a filename and in a CSV cell.
///
/// PlayerPrefs is the right home for it: it survives the scene load from the menu
/// into the campus without needing an object carried across, and it persists
/// between runs so a returning driver keeps their name.
///
/// Someone who presses Play straight into the campus scene never passes through
/// the menu, so there may be no name stored at all. That case returns "Player"
/// rather than an empty string, which would produce files called "drive__12-04-33"
/// and rows with a blank driver.
/// </summary>
public static class DriverIdentity
{
    /// <summary>The key MainMenuController writes. Changing it breaks Continue.</summary>
    public const string PlayerNameKey = "PlayerName";

    public const string Unknown = "Player";

    /// <summary>The name as the driver typed it, for display and for the report.</summary>
    public static string Name
    {
        get
        {
            string stored = PlayerPrefs.GetString(PlayerNameKey, "").Trim();
            return string.IsNullOrEmpty(stored) ? Unknown : stored;
        }
    }

    /// <summary>
    /// The same name reduced to something safe for a filename and for a CSV cell.
    ///
    /// Two separate hazards. Windows rejects \ / : * ? " &lt; &gt; | in a path, so a
    /// name containing one would throw when the file is created. And a comma
    /// anywhere in a CSV value silently shifts every column after it, which is far
    /// worse than an error because the data still loads and is simply wrong.
    /// Both are handled by keeping only letters, digits, dash and underscore.
    /// </summary>
    public static string FileSafeName
    {
        get
        {
            string source = Name;
            StringBuilder safe = new StringBuilder(source.Length);

            foreach (char c in source)
            {
                if (char.IsLetterOrDigit(c)) safe.Append(c);
                else if (c == '-' || c == '_') safe.Append(c);
                else if (c == ' ') safe.Append('_');
            }

            string result = safe.ToString().Trim('_');

            // A name of nothing but punctuation reduces to an empty string.
            if (result.Length == 0) return Unknown;

            // Long enough for any real name, short enough to keep the path sane.
            return result.Length > 32 ? result.Substring(0, 32) : result;
        }
    }
}
