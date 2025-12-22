using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("Scene to load when Play is pressed.")]
    [SerializeField] string gameScene = "GameScene";

    [Tooltip("If you keep Settings in a separate scene, put its name here. Leave blank if using a panel.")]
    [SerializeField] string settingsScene = "";

    [Header("Optional In-Scene Panels")]
    [Tooltip("Assign if your Settings is a panel in this scene.")]
    [SerializeField] GameObject settingsPanel;

    [Tooltip("Assign if you want a Quit confirm popup.")]
    [SerializeField] GameObject quitConfirmPanel;

    // ----- Buttons -----
    public void OnPlay()
    {
        if (!string.IsNullOrEmpty(gameScene))
            SceneManager.LoadScene(gameScene);
    }

    public void OnSettings()
    {
        if (settingsPanel) settingsPanel.SetActive(true);
        else if (!string.IsNullOrEmpty(settingsScene)) SceneManager.LoadScene(settingsScene);
    }

    public void OnCalibrateGyroButton()
    {
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

        // Let the sensor warm up a touch (especially on device)
        yield return null; // 1 frame
        yield return null; // 2 frames

        var q = sensor.attitude.ReadValue();
        GyroCalibrationService.SetBaseline(q);
        Debug.Log("[Settings] Gyro calibrated in menu.");
    }

    public void OnClearGyroCalibration()
    {
        GyroCalibrationService.ClearBaseline();
        Debug.Log("[Settings] Cleared stored baseline.");
    }

    public void OnCloseSettings()
    {
        if (settingsPanel) settingsPanel.SetActive(false);
        else
            // If Settings is a separate scene, go back to main menu (adjust as needed)
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnQuit()
    {
        // Works in builds; exits Play Mode in the Editor
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Optional confirm flow
    public void OnQuitAsk() { if (quitConfirmPanel) quitConfirmPanel.SetActive(true); }
    public void OnQuitCancel() { if (quitConfirmPanel) quitConfirmPanel.SetActive(false); }
}
