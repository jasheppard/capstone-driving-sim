using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Main menu for the campus driving sim.
///
/// Ported from the capstone-driving-sim project with three changes:
///   * The target scene is a field instead of a hard-coded "DrivingScene",
///     so it points at the NC A&amp;T campus scene.
///   * New Game actually writes the PlayerPrefs key that Continue checks —
///     in the original it never did, so Continue could never light up.
///   * Quit stops play mode in the editor, where Application.Quit does nothing.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Scene")]
    [Tooltip("Scene loaded by New Game and Continue. Must also be listed in Build Settings.")]
    public string gameSceneName = "SampleScene";

    [Header("Buttons")]
    public Button newGameButton;
    public Button continueButton;
    public Button quitButton;
    public TMP_InputField playerNameInput;

    [Header("UI")]
    public TextMeshProUGUI titleText;
    public GameObject noSaveWarning;

    const string PlayerNameKey = "PlayerName";

    void Start()
    {
        Time.timeScale = 1f;

        if (newGameButton != null) newGameButton.onClick.AddListener(StartNewGame);
        if (continueButton != null) continueButton.onClick.AddListener(ContinueGame);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);

        CheckForSaveData();
    }

    void CheckForSaveData()
    {
        bool saveExists = PlayerPrefs.HasKey(PlayerNameKey) || SaveSystem.SaveExists();

        if (noSaveWarning != null) noSaveWarning.SetActive(!saveExists);
        if (continueButton != null) continueButton.interactable = saveExists;
    }

    void StartNewGame()
    {
        PlayerPrefs.DeleteAll();
        SaveSystem.DeleteSave();

        string playerName = "Player";
        if (playerNameInput != null && !string.IsNullOrWhiteSpace(playerNameInput.text))
        {
            playerName = playerNameInput.text.Trim();
        }

        // Continue reads this key, so it has to be written here or Continue
        // stays greyed out forever.
        PlayerPrefs.SetString(PlayerNameKey, playerName);
        PlayerPrefs.Save();

        if (GameManager.Instance != null && GameManager.Instance.currentProfile != null)
        {
            GameManager.Instance.currentProfile.playerName = playerName;
            SaveSystem.SaveProfile(GameManager.Instance.currentProfile);
        }

        LoadGameScene();
    }

    void ContinueGame()
    {
        if (PlayerPrefs.HasKey(PlayerNameKey) || SaveSystem.SaveExists())
        {
            LoadGameScene();
        }
    }

    void LoadGameScene()
    {
        if (string.IsNullOrEmpty(gameSceneName))
        {
            Debug.LogError("[MainMenuController] No game scene name set.");
            return;
        }

        if (Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
        }
        else
        {
            Debug.LogError(
                "[MainMenuController] Scene \"" + gameSceneName + "\" is not in Build Settings.\n" +
                "Run NCAT → Configure Build Scenes, or add it via File → Build Settings.");
        }
    }

    void QuitGame()
    {
        Debug.Log("Quitting game...");

#if UNITY_EDITOR
        // Application.Quit does nothing in the editor, which makes the button
        // look broken while you are testing.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
