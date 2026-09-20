# Generates one AI voice clip per boss family/tier combo (2026-09-21 request: "description orale
# du boss par l'IA") via the ElevenLabs API - run this once by hand (or again whenever
# DungeonGenerator.BossFamilyFor's names/lore change), never at runtime: each line is fixed
# content keyed only by biome+tier, so there's no reason to pay a network call/API cost/latency at
# the moment the player actually walks into a boss room (see BossIntroVoice.cs).
#
# Usage:
#   $env:ELEVENLABS_API_KEY = "..."      # required
#   $env:ELEVENLABS_VOICE_ID = "..."     # optional, defaults to the premade voice below
#   .\Tools\generate_boss_intro_voices.ps1
#
# Writes Assets/Resources/BossIntroVoices/<biome>_<tier>.mp3 - BossIntroVoice.cs loads these by
# key via Resources.Load at runtime, so filenames (without extension) MUST match
# DungeonGenerator.BossFamily.introKey + "_" + tier exactly (tier is "zone"/"ville"/"region").

param(
    [string]$VoiceId = "21m00Tcm4TlvDq8ikWAM"  # ElevenLabs premade voice "Rachel" - override with -VoiceId or $env:ELEVENLABS_VOICE_ID
)

if (-not $env:ELEVENLABS_API_KEY) {
    Write-Error "ELEVENLABS_API_KEY n'est pas definie. Lance d'abord: `$env:ELEVENLABS_API_KEY = 'ta_cle'"
    exit 1
}
if ($env:ELEVENLABS_VOICE_ID) { $VoiceId = $env:ELEVENLABS_VOICE_ID }

$outDir = Join-Path $PSScriptRoot "..\Assets\Resources\BossIntroVoices"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Mirrors DungeonGenerator.BossFamilyFor/TierLabel - one family per biome, 3 tiers each (zone =
# "quartier", ville, region). Kept as a small hand-synced list here rather than parsed out of the
# C# file; 7 families x 3 tiers = 21 entries, not worth a build step over.
$families = @(
    @{ key = "jungle";    names = @{ zone = "Jeune Anaconda"; ville = "Anaconda Royale"; region = "Anaconda Primordiale" };
       desc = "Ce serpent gigantesque rode dans les frondaisons depuis des siecles, digerant lentement tout ce qui a eu le malheur de croiser sa route. Il vient de sentir une nouvelle proie approcher, et il en a apres VOUS." }
    @{ key = "forest";    names = @{ zone = "Sapling Enrage"; ville = "Ent Corrompu"; region = "Ent Ancien, Coeur de la Foret" };
       desc = "Autrefois gardien paisible de cette foret, cet arbre anime a vu trop d'aventuriers pietiner ses racines sans jamais s'excuser. Sa patience est epuisee, et il en a apres VOUS." }
    @{ key = "city";      names = @{ zone = "Automate Rouille"; ville = "Golem d'Acier"; region = "Golem d'Acier, Gardien de la Cite" };
       desc = "Assemble a partir des ruines d'une cite oubliee, ce golem de fer et de rouille ne connait qu'un seul ordre : proteger ce territoire de tout intrus. Ses capteurs viennent de vous reperer, et il en a apres VOUS." }
    @{ key = "beach";     names = @{ zone = "Calmar Geant"; ville = "Kraken Echoue"; region = "Kraken des Abysses" };
       desc = "Echoue sur ce rivage il y a bien longtemps, ce monstre des profondeurs n'a jamais cesse de chercher un chemin vers l'ocean, brisant tout ce qui se trouve sur son passage. Il vient de decider que VOUS feriez un bon obstacle a eliminer." }
    @{ key = "cave";      names = @{ zone = "Chiot du Cerbere"; ville = "Cerbere"; region = "Cerbere, Gardien des Enfers" };
       desc = "Ce chien des enfers s'est egare depuis que son maitre l'a laisse faire mumuse avec les ossements des enfers. Il semblerait bien qu'il soit ici maintenant, et qu'il en ait apres VOUS." }
    @{ key = "skycastle"; names = @{ zone = "Aiglon Mecanique"; ville = "Aigle Royal Mecanique"; region = "Rex Aquila, Seigneur des Cieux" };
       desc = "Construit par une civilisation disparue pour veiller sur les cieux, cet aigle mecanique patrouille encore ces ruines flottantes des siecles plus tard. Ses circuits viennent de designer une nouvelle cible, et il en a apres VOUS." }
    @{ key = "backrooms"; names = @{ zone = "Ombre Errante"; ville = "L'Arpenteur"; region = "L'Arpenteur, Ancien des Couloirs" };
       desc = "Personne ne sait depuis combien de temps cette silhouette erre dans ces couloirs identiques, ni si elle a jamais ete humaine. Elle vient de s'arreter de marcher pour la premiere fois depuis des annees, et elle en a apres VOUS." }
)
$tiers = @( @{ suffix = "zone"; label = "quartier" }, @{ suffix = "ville"; label = "ville" }, @{ suffix = "region"; label = "region" } )

$failures = @()
foreach ($f in $families) {
    foreach ($t in $tiers) {
        $id = "$($f.key)_$($t.suffix)"
        $outPath = Join-Path $outDir "$id.mp3"
        $bossName = $f.names[$t.suffix]
        $text = "$bossName. Boss de $($t.label). $($f.desc)"

        Write-Host "Generation de $id..."
        $body = @{
            text = $text
            model_id = "eleven_multilingual_v2"
            voice_settings = @{ stability = 0.5; similarity_boost = 0.75 }
        } | ConvertTo-Json

        try {
            Invoke-WebRequest -Uri "https://api.elevenlabs.io/v1/text-to-speech/$VoiceId" `
                -Method Post `
                -Headers @{ "xi-api-key" = $env:ELEVENLABS_API_KEY; "Content-Type" = "application/json" } `
                -Body $body `
                -OutFile $outPath
            Write-Host "  OK -> $outPath"
        } catch {
            Write-Warning "  Echec pour $($id): $_"
            $failures += $id
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Warning "Echecs : $($failures -join ', ')"
    exit 1
}
Write-Host "Termine - $($families.Count * $tiers.Count) clips generes dans $outDir"
