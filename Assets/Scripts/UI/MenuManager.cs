using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("Scene to load when Play is pressed.")]
    [SerializeField] string gameScene = "GameScene";

    [Tooltip("Assign the list of levels.")]
    [SerializeField] LevelDatabase levelDatabase;

    [Header("In-Scene Canvas/Panels")]
    [Tooltip("Assign the Main Menu canvas.")]
    [SerializeField] GameObject mainMenuCanvas;

    [Tooltip("Assign Settings panel.")]
    [SerializeField] GameObject settingsPanel;

    [Tooltip("Assign a Quit confirm popup.")]
    [SerializeField] GameObject quitConfirmPanel;

    [Tooltip("Assign the Level Select canvas.")]
    [SerializeField] GameObject levelSelectCanvas;

    [Tooltip("Assign the How To Play canvas.")]
    [SerializeField] GameObject HelpCanvas;

    // ======================
    // Audio hooks
    // ======================
    [Header("Audio")]
    [Tooltip("Main menu music. Played on Awake if enabled.")]
    [SerializeField] AudioClip musicMainMenu;

    [Tooltip("Play menu music automatically on Awake.")]
    [SerializeField] bool playMusicOnAwake = true;

    [Space(6)]
    [Tooltip("Generic UI click.")]
    [SerializeField] AudioClip sfxClick;

    [Tooltip("Back/cancel action.")]
    [SerializeField] AudioClip sfxBack;

    [Tooltip("Confirm/primary action.")]
    [SerializeField] AudioClip sfxConfirm;

    void Awake()
    {
        if (playMusicOnAwake && musicMainMenu != null)
        {
            // Uses AudioManager from earlier; harmless if missing.
            AudioManager.I?.PlayMusic(musicMainMenu);
        }
    }

    // ----- Buttons -----
    public void ShowMainMenu()
    {
        PlayBack();
        if (mainMenuCanvas) mainMenuCanvas.SetActive(true);
        if (levelSelectCanvas) levelSelectCanvas.SetActive(false);
        if (HelpCanvas) HelpCanvas.SetActive(false);
    }

    public void ShowHowToPlay()
    {
        PlayClick();
        if (mainMenuCanvas) mainMenuCanvas.SetActive(false);
        if (HelpCanvas) HelpCanvas.SetActive(true);
    }

    public void ShowLevelSelect()
    {
        PlayClick();
        if (mainMenuCanvas) mainMenuCanvas.SetActive(false);
        if (levelSelectCanvas) levelSelectCanvas.SetActive(true);
    }

    public void LoadLevelByIndex(int index)
    {
        if (levelDatabase == null)
        {
            Debug.LogWarning("MainMenuLevelSelect: No LevelDatabase assigned.");
            return;
        }

        if (index < 0 || index >= levelDatabase.levels.Length)
        {
            Debug.LogWarning($"MainMenuLevelSelect: Invalid level index {index}.");
            return;
        }

        LevelSelectionService.SelectedLevel = levelDatabase.levels[index];
        SceneManager.LoadScene(gameScene);
    }

    public void OnSettings()
    {
        PlayClick();
        if (settingsPanel) settingsPanel.SetActive(true);
        else Debug.LogWarning("MenuManager: No settings panel assigned.");
    }

    public void OnCalibrateGyroButton()
    {
        PlayConfirm();
        StartCoroutine(CalibrateNowInMenu());
    }

    IEnumerator CalibrateNowInMenu()
    {
        var sensor = AttitudeSensor.current;
        if (sensor == null)
        {
            Debug.LogWarning("[Settings] No AttitudeSensor available.");
            yield break;
        }

        if (!sensor.enabled) InputSystem.EnableDevice(sensor);

        // Let the sensor warm for a couple frames (esp. on device)
        yield return null;
        yield return null;

        var q = sensor.attitude.ReadValue();
        GyroCalibrationService.SetBaseline(q);
        Debug.Log("[Settings] Gyro calibrated in menu.");
    }

    public void OnClearGyroCalibration()
    {
        PlayBack();
        GyroCalibrationService.ClearBaseline();
        Debug.Log("[Settings] Cleared stored baseline.");
    }

    public void OnCloseSettings()
    {
        PlayBack();
        if (settingsPanel) settingsPanel.SetActive(false);
        else
            // If Settings is a separate scene, go back to main menu (adjust as needed)
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnQuit()
    {
        PlayConfirm();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Optional confirm flow
    public void OnQuitAsk() { PlayClick(); if (quitConfirmPanel) quitConfirmPanel.SetActive(true); }
    public void OnQuitCancel() { PlayBack(); if (quitConfirmPanel) quitConfirmPanel.SetActive(false); }

    // ======================
    // Audio helpers
    // ======================
    void PlayClick() { if (sfxClick) AudioManager.I?.PlayUISnap(sfxClick); }
    void PlayBack() { if (sfxBack) AudioManager.I?.PlayUISnap(sfxBack); }
    void PlayConfirm() { if (sfxConfirm) AudioManager.I?.PlayUISnap(sfxConfirm); }
}
