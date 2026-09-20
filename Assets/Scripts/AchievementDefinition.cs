// One entry in AchievementCatalog - id doubles as the achievements.json save key and the voice
// clip lookup key (see AchievementVoice/Resources/AchievementVoices).
[System.Serializable]
public class AchievementDefinition
{
    public string id;
    public string title;
    public string description;
}
