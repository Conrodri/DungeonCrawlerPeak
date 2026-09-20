using UnityEngine;

// Plays the pre-generated AI voice line narrating a boss's intro (2026-09-21 request) - same
// offline-pre-generation approach as AchievementVoice and for the same reason (a fixed, known-in-
// advance line per biome/tier, so there's no reason to pay a network call/API cost/latency at the
// exact moment the player steps into a boss room). See Tools/generate_boss_intro_voices.ps1.
// Looked up from Resources/BossIntroVoices/<key>.mp3, where key is "<biome>_<tier>" (see
// DungeonGenerator.BossFamily.introKey / BossRoomController.introVoiceKey). A missing clip just
// means no voice plays - the cutscene's pause/pan/title card still happen regardless.
[RequireComponent(typeof(AudioSource))]
public class BossIntroVoice : MonoBehaviour
{
    AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
    }

    // Returns the clip's length in seconds (0 if none found) so the caller can hold the title
    // card up for at least that long instead of cutting the voice off early.
    public float Play(string key)
    {
        AudioClip clip = Resources.Load<AudioClip>("BossIntroVoices/" + key);
        if (clip == null) return 0f;
        source.PlayOneShot(clip);
        return clip.length;
    }
}
