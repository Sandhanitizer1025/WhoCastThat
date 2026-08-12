using UnityEngine;

/// <summary>
/// The player's audio preferences, in one place so every scene agrees on them.
///
/// Music and SFX reuse the exact PlayerPrefs keys and defaults that BootAudioManager already
/// reads, so a change made at the mirror is picked up by the boot music without any extra wiring.
/// BootAudioManager only exists in BootScene though, so those two sliders have nothing to move in
/// the lobby — Master is the one that is audible everywhere, because AudioListener.volume scales
/// all audio in whatever scene is loaded.
/// </summary>
public static class GameAudioSettings
{
    public const string MasterVolumeKey = "MasterVolume";
    public const string MusicVolumeKey = "MusicVolume";   // shared with BootAudioManager
    public const string UISfxVolumeKey = "UISfxVolume";   // shared with BootAudioManager

    public const float MasterDefault = 1f;
    public const float MusicDefault = 0.6f;               // BootAudioManager's defaults
    public const float SfxDefault = 0.8f;

    public static float Master { get { return PlayerPrefs.GetFloat(MasterVolumeKey, MasterDefault); } }
    public static float Music { get { return PlayerPrefs.GetFloat(MusicVolumeKey, MusicDefault); } }
    public static float Sfx { get { return PlayerPrefs.GetFloat(UISfxVolumeKey, SfxDefault); } }

    /// <summary>Re-applies the saved master volume on every launch, before the first scene loads.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void ApplySaved()
    {
        AudioListener.volume = Master;
    }

    public static void SetMaster(float volume)
    {
        AudioListener.volume = volume;
        Save(MasterVolumeKey, volume);
    }

    public static void SetMusic(float volume)
    {
        foreach (var audio in Object.FindObjectsByType<BootAudioManager>(FindObjectsSortMode.None))
            audio.SetMusicVolume(volume);
        Save(MusicVolumeKey, volume);
    }

    public static void SetSfx(float volume)
    {
        foreach (var audio in Object.FindObjectsByType<BootAudioManager>(FindObjectsSortMode.None))
            audio.SetUISfxVolume(volume);
        Save(UISfxVolumeKey, volume);
    }

    static void Save(string key, float volume)
    {
        PlayerPrefs.SetFloat(key, volume);
        PlayerPrefs.Save();
    }
}
