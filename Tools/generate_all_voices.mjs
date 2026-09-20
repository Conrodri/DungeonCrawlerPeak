// Generates every pre-generated AI voice clip the game uses (2026-09-21 request, updated after
// ElevenLabs turned out to be blocked - free plan can't call TTS with library voices via the API
// - AND Windows' own Natural voice pack download failed to install (0 MB, likely a broken
// Windows Update component on this machine)). Uses the Microsoft Edge "Read Aloud" neural voices
// instead, via the community msedge-tts package: same Azure neural engine, genuinely free, no
// account/API key, works today. Unofficial/reverse-engineered (not a documented Microsoft API) -
// if it ever breaks, ElevenLabs (Tools/generate_*_voices.ps1, now dead code kept for that day) or
// a fixed Windows Natural-voice install are the fallbacks.
//
// Run once by hand (or again whenever the source text changes) - never at runtime, see
// AchievementVoice/BossIntroVoice/TutorialVoice's own comments for why.
//
// Usage:
//   cd Tools
//   npm install
//   node generate_all_voices.mjs [voiceName]
// voiceName defaults to fr-FR-DeniseNeural (female) - try fr-FR-HenriNeural for a male voice.

import { MsEdgeTTS, OUTPUT_FORMAT } from "msedge-tts";
import { mkdir, rename } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const resourcesRoot = path.join(here, "..", "Assets", "Resources");
const voiceName = process.argv[2] || "fr-FR-DeniseNeural";

// --- Achievements (mirrors AchievementCatalog.cs) ---
const achievements = [
    { id: "first_blood", text: "Succes debloque : Premier Sang. Vous avez tue votre premier monstre." },
    { id: "hunter_50", text: "Succes debloque : Chasseur Aguerri. Vous avez tue cinquante monstres." },
    { id: "hunter_200", text: "Succes debloque : Fleau des Donjons. Vous avez tue deux cents monstres." },
    { id: "zone_slayer", text: "Succes debloque : Terreur de Quartier. Vous avez vaincu un boss de quartier." },
    { id: "ville_slayer", text: "Succes debloque : Terreur de Ville. Vous avez vaincu un boss de ville." },
    { id: "region_slayer", text: "Succes debloque : Terreur de Region. Vous avez vaincu un boss de region." },
    { id: "floor_5", text: "Succes debloque : Explorateur. Vous avez atteint l'etage cinq." },
    { id: "floor_10", text: "Succes debloque : Spelonque Profonde. Vous avez atteint l'etage dix." },
    { id: "level_10", text: "Succes debloque : Aguerri. Vous avez atteint le niveau dix." },
    { id: "quest_done", text: "Succes debloque : Contractant. Vous avez termine votre premier contrat." },
    { id: "rich", text: "Succes debloque : Petite Fortune. Vous avez amasse cent pieces d'or." },
    { id: "legendary_find", text: "Succes debloque : Collectionneur. Vous avez trouve un objet legendaire." },
];

// --- Boss intros (mirrors DungeonGenerator.BossFamilyFor/TierLabel) ---
const bossFamilies = [
    { key: "jungle", names: { zone: "Jeune Anaconda", ville: "Anaconda Royale", region: "Anaconda Primordiale" },
      desc: "Ce serpent gigantesque rode dans les frondaisons depuis des siecles, digerant lentement tout ce qui a eu le malheur de croiser sa route. Il vient de sentir une nouvelle proie approcher, et il en a apres VOUS." },
    { key: "forest", names: { zone: "Sapling Enrage", ville: "Ent Corrompu", region: "Ent Ancien, Coeur de la Foret" },
      desc: "Autrefois gardien paisible de cette foret, cet arbre anime a vu trop d'aventuriers pietiner ses racines sans jamais s'excuser. Sa patience est epuisee, et il en a apres VOUS." },
    { key: "city", names: { zone: "Automate Rouille", ville: "Golem d'Acier", region: "Golem d'Acier, Gardien de la Cite" },
      desc: "Assemble a partir des ruines d'une cite oubliee, ce golem de fer et de rouille ne connait qu'un seul ordre : proteger ce territoire de tout intrus. Ses capteurs viennent de vous reperer, et il en a apres VOUS." },
    { key: "beach", names: { zone: "Calmar Geant", ville: "Kraken Echoue", region: "Kraken des Abysses" },
      desc: "Echoue sur ce rivage il y a bien longtemps, ce monstre des profondeurs n'a jamais cesse de chercher un chemin vers l'ocean, brisant tout ce qui se trouve sur son passage. Il vient de decider que VOUS feriez un bon obstacle a eliminer." },
    { key: "cave", names: { zone: "Chiot du Cerbere", ville: "Cerbere", region: "Cerbere, Gardien des Enfers" },
      desc: "Ce chien des enfers s'est egare depuis que son maitre l'a laisse faire mumuse avec les ossements des enfers. Il semblerait bien qu'il soit ici maintenant, et qu'il en ait apres VOUS." },
    { key: "skycastle", names: { zone: "Aiglon Mecanique", ville: "Aigle Royal Mecanique", region: "Rex Aquila, Seigneur des Cieux" },
      desc: "Construit par une civilisation disparue pour veiller sur les cieux, cet aigle mecanique patrouille encore ces ruines flottantes des siecles plus tard. Ses circuits viennent de designer une nouvelle cible, et il en a apres VOUS." },
    { key: "backrooms", names: { zone: "Ombre Errante", ville: "L'Arpenteur", region: "L'Arpenteur, Ancien des Couloirs" },
      desc: "Personne ne sait depuis combien de temps cette silhouette erre dans ces couloirs identiques, ni si elle a jamais ete humaine. Elle vient de s'arreter de marcher pour la premiere fois depuis des annees, et elle en a apres VOUS." },
];
const tiers = [
    { suffix: "zone", label: "quartier" },
    { suffix: "ville", label: "ville" },
    { suffix: "region", label: "region" },
];

// --- Tutorial guide (mirrors DungeonGenerator.SpawnTutorialNpc's bodyText) ---
const tutorial = {
    id: "guide_intro",
    text: "Bienvenue dans la Fosse, crawler. Chaque etage est chronometre : dix minutes pour trouver l'escalier et descendre, sans quoi le sol s'effondre et vous tue. Certains escaliers se trouvent juste en explorant, d'autres exigent de battre un boss, d'actionner un levier ou d'attendre. Combattez, pillez, equipez-vous, et descendez aussi loin que possible. Le portail derriere moi vous mene au premier etage.",
};

async function generate(outDir, id, text) {
    await mkdir(outDir, { recursive: true });
    const tts = new MsEdgeTTS();
    await tts.setMetadata(voiceName, OUTPUT_FORMAT.AUDIO_24KHZ_96KBITRATE_MONO_MP3);
    // toFile always names its output "audio.mp3" (no filename parameter in this library) - rename
    // to the id Resources.Load expects (see AchievementVoice/BossIntroVoice/TutorialVoice)
    // immediately so the next clip's toFile call in the same dir can't clobber it first.
    const { audioFilePath } = await tts.toFile(outDir, text);
    const finalPath = path.join(outDir, id + ".mp3");
    await rename(audioFilePath, finalPath);
    console.log("  OK ->", finalPath);
}

async function main() {
    console.log("Voix : " + voiceName);

    console.log("\n--- Succes (" + achievements.length + ") ---");
    const achievementsDir = path.join(resourcesRoot, "AchievementVoices");
    for (const a of achievements) {
        console.log("Generation de " + a.id + "...");
        await generate(achievementsDir, a.id, a.text);
    }

    console.log("\n--- Intros de boss (" + bossFamilies.length * tiers.length + ") ---");
    const bossDir = path.join(resourcesRoot, "BossIntroVoices");
    for (const family of bossFamilies) {
        for (const tier of tiers) {
            const id = family.key + "_" + tier.suffix;
            const bossName = family.names[tier.suffix];
            const text = bossName + ". Boss de " + tier.label + ". " + family.desc;
            console.log("Generation de " + id + "...");
            await generate(bossDir, id, text);
        }
    }

    console.log("\n--- Tutoriel (1) ---");
    const tutorialDir = path.join(resourcesRoot, "TutorialVoices");
    console.log("Generation de " + tutorial.id + "...");
    await generate(tutorialDir, tutorial.id, tutorial.text);

    console.log("\nTermine - " + (achievements.length + bossFamilies.length * tiers.length + 1) + " clips generes.");
}

main().catch(err => {
    console.error("Echec :", err);
    process.exitCode = 1;
});
