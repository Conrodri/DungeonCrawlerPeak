using UnityEngine;

// Plays a pre-generated AI voice line announcing an achievement unlock (2026-09-21 request).
// Clips are generated OFFLINE by Tools/generate_achievement_voices.py (an ElevenLabs call per
// AchievementCatalog entry) rather than synthesized live in-game - the achievement list is fixed
// content, so there's no reason to pay a network round-trip/API cost/latency at the exact moment
// a player earns one. Looked up by achievement id from Resources/AchievementVoices/<id>.mp3
// (Resources.Load, not a hand-wired Inspector slot per achievement - the catalog is a plain data
// table, see AchievementCatalog). A missing clip (not generated yet, or the id is new) just means
// no voice plays - it never blocks the toast or the unlock itself.
[RequireComponent(typeof(AudioSource))]
public class AchievementVoice : MonoBehaviour
{
    AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
    }

    public void Play(string achievementId)
    {
        AudioClip clip = Resources.Load<AudioClip>("AchievementVoices/" + achievementId);
        if (clip == null) return;
        source.PlayOneShot(clip);
    }
}
