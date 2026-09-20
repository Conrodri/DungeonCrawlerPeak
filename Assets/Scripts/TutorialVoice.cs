using UnityEngine;

// Plays the pre-generated AI voice line narrating the dungeon to a first-time player (2026-09-21
// request: "pour la toute premiere game, lors du tuto... un speech explicatif du donjon par
// l'ia"). Same offline-pre-generation approach as AchievementVoice/BossIntroVoice and for the same
// reason (the Guide's explanation - see DungeonGenerator.SpawnTutorialNpc's bodyText - is fixed
// content, no live TTS call needed at the moment the player talks to him). See
// Tools/generate_tutorial_voice.ps1. A single fixed clip (Resources/TutorialVoices/guide_intro),
// unlike the other two voice components' per-id/per-key catalogs, so there's no lookup key
// parameter - if a 4th voiced system shows up after this one, these three are similar enough to
// be worth collapsing into one shared player at that point.
[RequireComponent(typeof(AudioSource))]
public class TutorialVoice : MonoBehaviour
{
    AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
    }

    public void Play()
    {
        AudioClip clip = Resources.Load<AudioClip>("TutorialVoices/guide_intro");
        if (clip == null) return;
        source.PlayOneShot(clip, AudioSettingsManager.VoiceVolume);
    }
}
