using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class BootAudioManager : MonoBehaviour
{
    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource uiSfxSource;

    [Header("Audio Clips")]
    [SerializeField] private AudioClip hoverClip;
    [SerializeField] private AudioClip clickClip;

    [Header("Volume UI")]
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider uiSfxVolumeSlider;

    private const string MusicVolumeKey = "MusicVolume";
    private const string UISfxVolumeKey = "UISfxVolume";

    private float uiSfxVolume = 0.8f;

    private void Awake()
    {
        float savedMusicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 0.6f);
        float savedUISfxVolume = PlayerPrefs.GetFloat(UISfxVolumeKey, 0.8f);

        SetMusicVolume(savedMusicVolume);
        SetUISfxVolume(savedUISfxVolume);

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = savedMusicVolume;
            musicVolumeSlider.onValueChanged.AddListener(SetMusicVolume);
        }

        if (uiSfxVolumeSlider != null)
        {
            uiSfxVolumeSlider.value = savedUISfxVolume;
            uiSfxVolumeSlider.onValueChanged.AddListener(SetUISfxVolume);
        }
    }

    private void Start()
    {
        if (musicSource != null && !musicSource.isPlaying)
        {
            musicSource.Play();
        }
    }

    public void SetMusicVolume(float volume)
    {
        if (musicSource != null)
        {
            musicSource.volume = volume;
        }

        PlayerPrefs.SetFloat(MusicVolumeKey, volume);
        PlayerPrefs.Save();
    }

    public void SetUISfxVolume(float volume)
    {
        uiSfxVolume = volume;

        if (uiSfxSource != null)
        {
            uiSfxSource.volume = volume;
        }

        PlayerPrefs.SetFloat(UISfxVolumeKey, volume);
        PlayerPrefs.Save();
    }

    // Fades the music volume to silence over the given duration for scene transitions.
    // Does NOT persist to PlayerPrefs, so the player's saved music volume is preserved.
    public void FadeOutMusic(float duration)
    {
        if (musicSource == null)
        {
            return;
        }

        StartCoroutine(FadeOutMusicRoutine(duration));
    }

    private IEnumerator FadeOutMusicRoutine(float duration)
    {
        float startVolume = musicSource.volume;
        float elapsed = 0f;
        float fadeTime = Mathf.Max(0.01f, duration);

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeTime);
            yield return null;
        }

        musicSource.volume = 0f;
    }

    public void PlayHoverSound()
    {
        if (uiSfxSource != null && hoverClip != null)
        {
            uiSfxSource.PlayOneShot(hoverClip, uiSfxVolume);
        }
    }

    public void PlayClickSound()
    {
        if (uiSfxSource != null && clickClip != null)
        {
            uiSfxSource.PlayOneShot(clickClip, uiSfxVolume);
        }
    }
}