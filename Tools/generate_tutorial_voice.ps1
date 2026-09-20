# Generates the Guide's dungeon-explanation voice line (2026-09-21 request: "pour la toute
# premiere game, lors du tuto... un speech explicatif du donjon par l'ia") via the ElevenLabs API -
# run this once by hand (or again if DungeonGenerator.SpawnTutorialNpc's bodyText changes), never
# at runtime: it's fixed content, no reason to pay a network call/API cost/latency the moment a
# first-time player talks to the Guide (see TutorialVoice.cs).
#
# Usage:
#   $env:ELEVENLABS_API_KEY = "..."      # required
#   $env:ELEVENLABS_VOICE_ID = "..."     # optional, defaults to the premade voice below
#   .\Tools\generate_tutorial_voice.ps1
#
# Writes Assets/Resources/TutorialVoices/guide_intro.mp3 - TutorialVoice.cs loads this exact path.

param(
    [string]$VoiceId = "21m00Tcm4TlvDq8ikWAM"  # ElevenLabs premade voice "Rachel" - override with -VoiceId or $env:ELEVENLABS_VOICE_ID
)

if (-not $env:ELEVENLABS_API_KEY) {
    Write-Error "ELEVENLABS_API_KEY n'est pas definie. Lance d'abord: `$env:ELEVENLABS_API_KEY = 'ta_cle'"
    exit 1
}
if ($env:ELEVENLABS_VOICE_ID) { $VoiceId = $env:ELEVENLABS_VOICE_ID }

$outDir = Join-Path $PSScriptRoot "..\Assets\Resources\TutorialVoices"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Mirrors DungeonGenerator.SpawnTutorialNpc's bodyText verbatim (line breaks flattened to periods
# for a natural spoken cadence, "10" spelled out) - keep this in sync by hand if that text changes.
$text = "Bienvenue dans la Fosse, crawler. Chaque etage est chronometre : dix minutes pour trouver l'escalier et descendre, sans quoi le sol s'effondre et vous tue. Certains escaliers se trouvent juste en explorant, d'autres exigent de battre un boss, d'actionner un levier ou d'attendre. Combattez, pillez, equipez-vous, et descendez aussi loin que possible. Le portail derriere moi vous mene au premier etage."

$outPath = Join-Path $outDir "guide_intro.mp3"
Write-Host "Generation de guide_intro..."
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
    Write-Host "Termine -> $outPath"
} catch {
    Write-Error "Echec : $_"
    exit 1
}
