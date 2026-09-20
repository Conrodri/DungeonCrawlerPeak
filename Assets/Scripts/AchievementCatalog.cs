// The fixed achievement list (2026-09-21 request) - static content, not procedurally generated,
// so a fixed AI voice line can be pre-generated once per id (see Tools/generate_achievement_voices.py
// and AchievementVoice). Every condition reuses a hook already wired elsewhere in the game (kills,
// boss tiers, floor depth, level, quests, loot) - see AchievementManager for where each fires.
public static class AchievementCatalog
{
    public static readonly AchievementDefinition[] All =
    {
        new AchievementDefinition { id = "first_blood", title = "Premier Sang", description = "Tuer votre premier monstre." },
        new AchievementDefinition { id = "hunter_50", title = "Chasseur Aguerri", description = "Tuer 50 monstres." },
        new AchievementDefinition { id = "hunter_200", title = "Fleau des Donjons", description = "Tuer 200 monstres." },
        new AchievementDefinition { id = "zone_slayer", title = "Terreur de Quartier", description = "Vaincre un boss de quartier." },
        new AchievementDefinition { id = "ville_slayer", title = "Terreur de Ville", description = "Vaincre un boss de ville." },
        new AchievementDefinition { id = "region_slayer", title = "Terreur de Region", description = "Vaincre un boss de region." },
        new AchievementDefinition { id = "floor_5", title = "Explorateur", description = "Atteindre l'etage 5." },
        new AchievementDefinition { id = "floor_10", title = "Spelonque Profonde", description = "Atteindre l'etage 10." },
        new AchievementDefinition { id = "level_10", title = "Aguerri", description = "Atteindre le niveau 10." },
        new AchievementDefinition { id = "quest_done", title = "Contractant", description = "Terminer un premier contrat." },
        new AchievementDefinition { id = "rich", title = "Petite Fortune", description = "Amasser 100 or au total." },
        new AchievementDefinition { id = "legendary_find", title = "Collectionneur", description = "Trouver un objet Legendaire ou Mythique." },
    };

    public static AchievementDefinition Get(string id)
    {
        foreach (AchievementDefinition def in All) if (def.id == id) return def;
        return null;
    }
}
