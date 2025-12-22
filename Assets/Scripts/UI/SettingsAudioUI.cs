using UnityEngine;
using UnityEngine.UI;

public class SettingsAudioUI : MonoBehaviour
{
    [SerializeField] Slider master;
    [SerializeField] Slider music;
    [SerializeField] Slider sfx;

    void Start()
    {
        // Initialize from saved prefs (same defaults as AudioManager)
        master.SetValueWithoutNotify(PlayerPrefs.GetFloat("vol_master", 0.8f));
        music.SetValueWithoutNotify(PlayerPrefs.GetFloat("vol_music", 0.8f));
        sfx.SetValueWithoutNotify(PlayerPrefs.GetFloat("vol_sfx", 0.8f));

        // Wire change events ? AudioManager
        master.onValueChanged.AddListener(v => AudioManager.I?.SetMasterLinear(v));
        music.onValueChanged.AddListener(v => AudioManager.I?.SetMusicLinear(v));
        sfx.onValueChanged.AddListener(v => AudioManager.I?.SetSFXLinear(v));
    }
}
