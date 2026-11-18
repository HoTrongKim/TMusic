[CmdletBinding()]
param([string]$Path = (Join-Path $PSScriptRoot '..\.env'), [switch]$Override)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Environment file not found. Create .env from .env.example and set local values.' }
foreach ($line in [IO.File]::ReadAllLines([IO.Path]::GetFullPath($Path))) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
    if ($line -notmatch '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') { throw 'Invalid environment entry. Only KEY=value syntax is supported.' }
    $entryName = $Matches[1]
    $entryValue = $Matches[2].Trim()
    if ($entryValue.Length -ge 2 -and (($entryValue.StartsWith('"') -and $entryValue.EndsWith('"')) -or ($entryValue.StartsWith("'") -and $entryValue.EndsWith("'")))) {
        $entryValue = $entryValue.Substring(1, $entryValue.Length - 2)
    }
    if ($Override -or $null -eq [Environment]::GetEnvironmentVariable($entryName, 'Process')) {
        [Environment]::SetEnvironmentVariable($entryName, $entryValue, 'Process')
    }
}
# Deliberately no eval, shell interpolation, or value output.
