[CmdletBinding()]
param(
    [string]$ApiUrl = 'https://tmusic-api-gateway.trongkim3008.workers.dev',
    [ValidateSet('debug', 'release')][string]$Mode = 'debug',
    [ValidateRange(1, 2147483647)][int]$BuildNumber = 2
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'Load-Environment.ps1')

if ($Mode -eq 'release') {
    foreach ($name in @('TMUSIC_KEYSTORE', 'TMUSIC_KEYSTORE_PASSWORD', 'TMUSIC_KEY_ALIAS', 'TMUSIC_KEY_PASSWORD')) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, 'Process'))) {
            throw 'Release builds require the existing signing keystore and all TMUSIC_KEYSTORE/TMUSIC_KEY_* settings.'
        }
    }
    $releaseKeystorePath = if ([IO.Path]::IsPathRooted($env:TMUSIC_KEYSTORE)) {
        [IO.Path]::GetFullPath($env:TMUSIC_KEYSTORE)
    } else {
        [IO.Path]::GetFullPath((Join-Path $projectRoot $env:TMUSIC_KEYSTORE))
    }
    if (-not (Test-Path -LiteralPath $releaseKeystorePath -PathType Leaf)) {
        throw 'The configured release keystore does not exist.'
    }
}

$artifactDirectory = Join-Path $projectRoot '.local/artifacts'
New-Item -Path $artifactDirectory -ItemType Directory -Force | Out-Null
$definesPath = Join-Path $artifactDirectory 'android-public-defines.json'
$previousApiUrl = [Environment]::GetEnvironmentVariable('TMUSIC_BUILD_API_URL', 'Process')
$previousKeystore = [Environment]::GetEnvironmentVariable('TMUSIC_KEYSTORE', 'Process')
$env:TMUSIC_BUILD_API_URL = $ApiUrl
try {
    if ($Mode -eq 'release') { $env:TMUSIC_KEYSTORE = $releaseKeystorePath }
    python (Join-Path $PSScriptRoot 'prepare_mobile_oauth.py') --platform android --output $definesPath
    if ($LASTEXITCODE -ne 0) { throw 'Public Google OAuth build configuration is invalid.' }
    Push-Location (Join-Path $projectRoot 'mobile')
    try {
        flutter pub get --enforce-lockfile
        if ($LASTEXITCODE -ne 0) { throw 'Flutter dependency resolution failed.' }
        flutter build apk "--$Mode" --no-pub "--build-number=$BuildNumber" "--dart-define-from-file=$definesPath"
        if ($LASTEXITCODE -ne 0) { throw 'Android APK build failed.' }
        $apk = Join-Path $projectRoot "mobile/build/app/outputs/flutter-apk/app-$Mode.apk"
        $artifact = Join-Path $artifactDirectory "TMusic-android-$Mode-google.apk"
        Copy-Item -LiteralPath $apk -Destination $artifact
        Write-Output "APK: $artifact"
        Write-Output ('APK SHA-256: ' + (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash)
    } finally {
        Pop-Location
    }
} finally {
    [Environment]::SetEnvironmentVariable('TMUSIC_BUILD_API_URL', $previousApiUrl, 'Process')
    [Environment]::SetEnvironmentVariable('TMUSIC_KEYSTORE', $previousKeystore, 'Process')
}
