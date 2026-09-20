# Generates one AI voice clip per achievement (2026-09-21 request) via the ElevenLabs API - run
# this once by hand (or again whenever AchievementCatalog.cs's list changes), never at runtime:
# the achievement list is fixed content, so there's no reason to pay a network call/API
# cost/latency at the exact moment a player earns one (see AchievementVoice.cs).
#
# Usage:
#   $env:ELEVENLABS_API_KEY = "..."      # required
#   $env:ELEVENLABS_VOICE_ID = "..."     # optional, defaults to the premade voice below
#   .\Tools\generate_achievement_voices.ps1
#
# Writes Assets/Resources/AchievementVoices/<id>.mp3 - AchievementVoice.cs loads these by id via
# Resources.Load at runtime, so the filenames (without extension) MUST match AchievementCatalog.cs's
# ids exactly.

param(
    [string]$VoiceId = "21m00Tcm4TlvDq8ikWAM"  # ElevenLabs premade voice "Rachel" - override with -VoiceId or $env:ELEVENLABS_VOICE_ID
)

if (-not $env:ELEVENLABS_API_KEY) {
    Write-Error "ELEVENLABS_API_KEY n'est pas definie. Lance d'abord: `$env:ELEVENLABS_API_KEY = 'ta_cle'"
    exit 1
}
if ($env:ELEVENLABS_VOICE_ID) { $VoiceId = $env:ELEVENLABS_VOICE_ID }

$outDir = Join-Path $PSScriptRoot "..\Assets\Resources\AchievementVoices"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Mirrors AchievementCatalog.cs (id + the announcement line read aloud) - kept as a small hand-synced
# list here rather than parsed out of the C# file; only 12 entries, not worth a build step over.
$achievements = @(
    @{ id = "first_blood";    text = "Succes debloque : Premier Sang. Vous avez tue votre premier monstre." }
    @{ id = "hunter_50";      text = "Succes debloque : Chasseur Aguerri. Vous avez tue cinquante monstres." }
    @{ id = "hunter_200";     text = "Succes debloque : Fleau des Donjons. Vous avez tue deux cents monstres." }
    @{ id = "zone_slayer";    text = "Succes debloque : Terreur de Quartier. Vous avez vaincu un boss de quartier." }
    @{ id = "ville_slayer";   text = "Succes debloque : Terreur de Ville. Vous avez vaincu un boss de ville." }
    @{ id = "region_slayer";  text = "Succes debloque : Terreur de Region. Vous avez vaincu un boss de region." }
    @{ id = "floor_5";        text = "Succes debloque : Explorateur. Vous avez atteint l'etage cinq." }
    @{ id = "floor_10";       text = "Succes debloque : Spelonque Profonde. Vous avez atteint l'etage dix." }
    @{ id = "level_10";       text = "Succes debloque : Aguerri. Vous avez atteint le niveau dix." }
    @{ id = "quest_done";     text = "Succes debloque : Contractant. Vous avez termine votre premier contrat." }
    @{ id = "rich";           text = "Succes debloque : Petite Fortune. Vous avez amasse cent pieces d'or." }
    @{ id = "legendary_find"; text = "Succes debloque : Collectionneur. Vous avez trouve un objet legendaire." }
)

$failures = @()
foreach ($a in $achievements) {
    $outPath = Join-Path $outDir "$($a.id).mp3"
    Write-Host "Generation de $($a.id)..."
    $body = @{
        text = $a.text
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
        Write-Warning "  Echec pour $($a.id): $_"
        $failures += $a.id
    }
}

if ($failures.Count -gt 0) {
    Write-Warning "Echecs : $($failures -join ', ')"
    exit 1
}
Write-Host "Termine - $($achievements.Count) clips generes dans $outDir"
