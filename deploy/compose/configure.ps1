$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (!(Test-Path .env)) {
    $image = Read-Host 'Docker Hub 镜像（账号/mintapi）'
    $version = Read-Host '发布版本（例如 0.1.0）'
    $clientId = Read-Host 'osu! Client ID'
    $secure = Read-Host 'osu! Client Secret' -AsSecureString
    $secret = [Net.NetworkCredential]::new('', $secure).Password
    if ($image -notmatch '^[a-z0-9._/-]+$' -or $version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$' -or $clientId -notmatch '^[1-9][0-9]*$' -or !$secret -or $secret.Contains("`n") -or $secret.Contains("`r")) { throw '无效配置。' }
    function Random-Hex { $bytes = New-Object byte[] 24; $rng = [Security.Cryptography.RandomNumberGenerator]::Create(); $rng.GetBytes($bytes); $rng.Dispose(); return -join ($bytes | ForEach-Object { $_.ToString('x2') }) }
    $secret = $secret.Replace("'", "\'")
    $text = @"
MINTAPI_IMAGE=$image
MINTAPI_VERSION=$version
MINTAPI_BIND=127.0.0.1
MINTAPI_PORT=8080
MINTAPI_API_KEY=$(Random-Hex)
OSU_CLIENT_ID=$clientId
OSU_CLIENT_SECRET='$secret'
MYSQL_PASSWORD=$(Random-Hex)
MYSQL_ROOT_PASSWORD=$(Random-Hex)
REDIS_PASSWORD=$(Random-Hex)
"@
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot '.env'), $text, [Text.UTF8Encoding]::new($false))
    & icacls.exe .env /inheritance:r /grant:r "$($env:USERNAME):(F)" '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
    if ($LASTEXITCODE) { throw '配置文件权限设置失败。' }
} else { Write-Host '.env 已存在，保留原有凭据。' }
docker compose pull
if ($LASTEXITCODE) { throw '镜像下载失败。' }
docker compose up -d --wait --wait-timeout 180
if ($LASTEXITCODE) { throw '服务未就绪，请检查容器日志。' }
Write-Host '部署完成：http://127.0.0.1:8080。API Key 保存在 .env。'
