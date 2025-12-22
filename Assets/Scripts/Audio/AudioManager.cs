using UnityEngine;
using UnityEngine.Audio;
using System.Collections;

public class AudioManager : MonoBehaviour
{
    public static AudioManager I { get; private set; }

    [Header("Mixer (assign)")]
    [SerializeField] AudioMixer mixer;
    [SerializeField] string masterParam = "MasterVol";
    [SerializeField] string musicParam = "MusicVol";
    [SerializeField] string sfxParam = "SFXVol";

    [Header("Groups (assign)")]
    [SerializeField] AudioMixerGroup musicGroup;
    [SerializeField] AudioMixerGroup sfxGroup;

    [Header("Sources (auto if empty)")]
    [SerializeField] AudioSource musicSource; // looped music
    [SerializeField] AudioSource sfxOneShot;  // UI ticks/clicks
    [SerializeField] int sfxVoices = 8;       // pooled SFX voices

    AudioSource[] sfxPool;
    int sfxIndex;

    const string PP_MASTER = "vol_master";
    const string PP_MUSIC = "vol_music";
    const string PP_SFX = "vol_sfx";

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        // Create sources if not wired
        if (!musicSource)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.outputAudioMixerGroup = musicGroup;
        }
        if (!sfxOneShot)
        {
            sfxOneShot = gameObject.AddComponent<AudioSource>();
            sfxOneShot.playOnAwake = false;
            sfxOneShot.outputAudioMixerGroup = sfxGroup;
        }

        // Simple SFX pool
        sfxPool = new AudioSource[sfxVoices];
        for (int i = 0; i < sfxPool.Length; i++)
        {
            var a = new GameObject("SFX_" + i).AddComponent<AudioSource>();
            a.transform.SetParent(transform);
            a.playOnAwake = false;
            a.outputAudioMixerGroup = sfxGroup;
            sfxPool[i] = a;
        }

        // Load saved volumes (defaults)
        SetMasterLinear(PlayerPrefs.GetFloat(PP_MASTER, 0.8f));
        SetMusicLinear(PlayerPrefs.GetFloat(PP_MUSIC, 0.8f));
        SetSFXLinear(PlayerPrefs.GetFloat(PP_SFX, 0.8f));
    }

    // === Volume API (sliders call these with [0..1]) ===
    public void SetMasterLinear(float v) { SetLinear(masterParam, v); PlayerPrefs.SetFloat(PP_MASTER, v); }
    public void SetMusicLinear(float v) { SetLinear(musicParam, v); PlayerPrefs.SetFloat(PP_MUSIC, v); }
    public void SetSFXLinear(float v) { SetLinear(sfxParam, v); PlayerPrefs.SetFloat(PP_SFX, v); }

    static float LinToDb(float v)
    {
        // map 0..1 -> -80..0 dB (avoid -inf at 0)
        if (v <= 0.0001f) return -80f;
        return Mathf.Log10(v) * 20f;
    }
    void SetLinear(string param, float v)
    {
        if (!mixer) return;
        mixer.SetFloat(param, LinToDb(Mathf.Clamp01(v)));
    }

    // === Music helpers ===
    public void PlayMusic(AudioClip clip, float fade = 0.35f, bool loop = true)
    {
        if (!clip) return;
        StartCoroutine(SwapMusic(clip, fade, loop));
    }
    IEnumerator SwapMusic(AudioClip clip, float fade, bool loop)
    {
        if (fade > 0f && musicSource.isPlaying)
        {
            for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
            {
                musicSource.volume = 1f - (t / fade);
                yield return null;
            }
        }
        musicSource.Stop();
        musicSource.clip = clip;
        musicSource.loop = loop;
        musicSource.volume = 1f;
        musicSource.Play();
        yield break;
    }

    // === SFX helpers ===
    public void PlaySFX(AudioClip clip, float pitch = 1f, float spatial = 0f)
    {
        if (!clip) return;
        var a = sfxPool[sfxIndex++ % sfxPool.Length];
        a.pitch = pitch;
        a.spatialBlend = spatial; // 0=2D, 1=3D
        a.PlayOneShot(clip);
    }
    public void PlayUISnap(AudioClip clip) => sfxOneShot.PlayOneShot(clip);
}
