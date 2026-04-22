using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Buttons")]
    public Button newGameButton;
    public Button continueButton;
    public Button quitButton;
    public TMP_InputField playerNameInput;

    [Header("UI")]
    public TextMeshProUGUI titleText;
    public GameObject noSaveWarning;

    void Start()
    {
        Time.timeScale = 1f;

        newGameButton.onClick.AddListener(StartNewGame);
        continueButton.onClick.AddListener(ContinueGame);
        quitButton.onClick.AddListener(QuitGame);

        CheckForSaveData();
    }

    void CheckForSaveData()
    {
        bool saveExists = PlayerPrefs.HasKey("PlayerName");
        if (noSaveWarning != null)
        {
            noSaveWarning.SetActive(!saveExists);
        }
        continueButton.interactable = saveExists;
    }

    void StartNewGame()
{
    PlayerPrefs.DeleteAll();
    SaveSystem.DeleteSave();

    if (GameManager.Instance != null && playerNameInput != null)
    {
        if (playerNameInput.text != "")
        {
            GameManager.Instance.currentProfile.playerName = playerNameInput.text;
            SaveSystem.SaveProfile(GameManager.Instance.currentProfile);
        }
    }

    SceneManager.LoadScene("DrivingScene");
}

    void ContinueGame()
    {
        if (PlayerPrefs.HasKey("PlayerName"))
        {
            SceneManager.LoadScene("DrivingScene");
        }
    }

    void QuitGame()
    {
        Debug.Log("Quitting game...");
        Application.Quit();
    }
}