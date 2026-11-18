[CmdletBinding()]
param(
    [ValidateSet('api','worker','migrate','check')][string]$Service = 'api',
    [ValidatePattern('^TMusicV2[A-Za-z0-9_]*$')][string]$Database = 'TMusicV2Dev',
    [switch]$RespectLegacyTikTokDisable
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'Load-Environment.ps1')
$connection = $env:ConnectionStrings__TMusic
if ([string]::IsNullOrWhiteSpace($connection)) { $connection = $env:ConnectionStrings__DefaultConnection }
if ([string]::IsNullOrWhiteSpace($connection)) { throw 'Set ConnectionStrings__TMusic in .env.' }
$rewritten = $connection -replace '(?i)(^|;)\s*(Database|Initial\s+Catalog)\s*=\s*[^;]*', '$1'
$rewritten = ($rewritten.Trim(';') + ";Initial Catalog=$Database").Trim(';')
if ($rewritten -match '(?i)(^|;)\s*(Database|Initial\s+Catalog)\s*=\s*(?!' + [Regex]::Escape($Database) + '\s*(;|$))') {
    throw "Failed to isolate target database $Database."
}
$env:ConnectionStrings__TMusic = $rewritten
$env:ConnectionStrings__DefaultConnection = $rewritten
if (-not $RespectLegacyTikTokDisable) {
    $env:Features__EnableTikTokVideoImport = 'true'
}
$env:Features__EnableSoundCloudImport = 'true'
$env:Features__EnableCrossProviderMixedCollections = 'true'
$env:Features__EnableAudioDownloads = 'true'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5078'
# The legacy database and the original .env are never changed by this script.
Push-Location $projectRoot
try {
    switch ($Service) {
        'api' { dotnet run --project backend/src/TMusic.Api --no-launch-profile }
        'worker' { dotnet run --project backend/src/TMusic.Worker --no-launch-profile }
        'migrate' {
            dotnet tool restore
            if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
            dotnet ef database update --project backend/src/TMusic.Infrastructure --startup-project backend/src/TMusic.Api
        }
        'check' {
            dotnet build backend/TMusic.slnx -c Release
            if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
            dotnet test backend/TMusic.slnx -c Release --no-build
        }
    }
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE." }
} finally { Pop-Location }
