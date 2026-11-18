[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$credentialPath = Join-Path $root '.local/sql-docker-password.xml'
if (-not (Test-Path -LiteralPath $credentialPath -PathType Leaf)) {
    throw 'Không tìm thấy mật khẩu SQL Docker được mã hóa trong .local. Không tạo lại mật khẩu trên volume đã tồn tại.'
}
$securePassword = Import-Clixml -LiteralPath $credentialPath
$password = (New-Object System.Net.NetworkCredential('', $securePassword)).Password
$builder = New-Object System.Data.Common.DbConnectionStringBuilder
$builder['Server'] = 'db,1433'
$builder['Database'] = 'TMusicV2Dev'
$builder['User ID'] = 'sa'
$builder['Password'] = $password
$builder['Encrypt'] = 'True'
$builder['TrustServerCertificate'] = 'True'
$env:MSSQL_SA_PASSWORD = $password
$env:TMUSIC_DOCKER_CONNECTION = $builder.ConnectionString
Push-Location $root
try {
    docker compose -f docker-compose.yml -f docker-compose.autostart.yml up -d --build db api worker
    if ($LASTEXITCODE -ne 0) { throw 'Không thể khởi động Docker stack.' }
} finally {
    Pop-Location
    Remove-Item Env:MSSQL_SA_PASSWORD, Env:TMUSIC_DOCKER_CONNECTION -ErrorAction SilentlyContinue
}
