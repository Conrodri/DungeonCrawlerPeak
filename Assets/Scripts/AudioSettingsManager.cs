using UnityEngine;

// Persisted audio settings (2026-09-21 request: "un module son... choisir sa sortie audio et
// gerer le son"). Master volume drives AudioListener.volume (the game's existing volume slider,
// now actually persisted instead of resetting every session); Voice volume is a separate
// multiplier applied by AchievementVoice/BossIntroVoice's PlayOneShot calls - the only voiced
// audio in the game today. No AudioMixer asset for this: building a full mixer graph for exactly
// 2 AudioSources would be the abstraction the game doesn't need yet (see
// feedback_code_reuse_library) - add Music/SFX groups here if/when those actually exist.
//
// Output DEVICE selection is deliberately NOT reimplemented in-engine: Unity's AudioSettings API
// has no device enumeration/selection on Standalone (confirmed live against this project's Editor
// - see MainMenuController.BuildSettingsPanel's button, which instead opens Windows' own Sound
// settings page). A fake dropdown that can't actually switch anything would be worse than being
// upfront about the limitation.
public static class AudioSettingsManager
{
    const string MasterKey = "audio_master_volume";
    const string VoiceKey = "audio_voice_volume";

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(MasterKey, 1f);
        set
        {
            PlayerPrefs.SetFloat(MasterKey, value);
            AudioListener.volume = value;
        }
    }

    public static float VoiceVolume
    {
        get => PlayerPrefs.GetFloat(VoiceKey, 1f);
        set => PlayerPrefs.SetFloat(VoiceKey, value);
    }

    // Runs once before the first scene loads, regardless of which entry point the game actually
    // starts from - a value saved last session must already be applied before anything plays, not
    // just whenever the player happens to open the settings panel.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        AudioListener.volume = MasterVolume;
    }
}
