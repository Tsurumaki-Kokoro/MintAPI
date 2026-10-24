param([ValidateSet('database')][string]$Component)
$ErrorActionPreference = 'Stop'
$password = [Console]::ReadLine()
if ($password -notmatch '^[a-f0-9]{48}$') { throw 'Invalid generated credential.' }
if (Get-Service MintAPIMySQL -ErrorAction SilentlyContinue) { throw 'Managed MySQL already exists; enter its existing connection.' }
if (Get-NetTCPConnection -LocalPort 3306 -State Listen -ErrorAction SilentlyContinue) { throw 'Database port is occupied; use an existing connection.' }
$root = Join-Path $env:ProgramData 'MintAPI-MySQL'
if (Test-Path $root) { throw 'Database directory already exists; do not reinitialize it.' }
$source = Join-Path $PSScriptRoot 'mysql'
if (!(Test-Path "$source\bin\mysqld.exe")) { throw 'The release is missing its MySQL component.' }
New-Item -ItemType Directory $root | Out-Null
& icacls.exe $root /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE) { throw 'Failed to protect MySQL directory.' }
Copy-Item $source (Join-Path $root 'server') -Recurse
$server = Join-Path $root 'server\bin\mysqld.exe'
$data = Join-Path $root 'data'
$ini = Join-Path $root 'my.ini'
$init = Join-Path $root 'bootstrap.sql'
$rootPassword = -join ((1..48) | ForEach-Object { '0123456789abcdef'[(Get-Random -Maximum 16)] })
# Files inherit the restricted parent ACL; MySQL runs as SYSTEM.
$config = "[mysqld]`nbasedir=$($root.Replace('\','/'))/server`ndatadir=$($data.Replace('\','/'))`nport=3306`nbind-address=127.0.0.1`nmysqlx=0`n"
[IO.File]::WriteAllText($ini, $config, [Text.UTF8Encoding]::new($false))
& $server "--defaults-file=$ini" --initialize-insecure
if ($LASTEXITCODE) { throw 'MySQL initialization failed; directory retained for diagnosis.' }
$sql = "ALTER USER 'root'@'localhost' IDENTIFIED BY '$rootPassword';`nCREATE DATABASE mintapi CHARACTER SET utf8mb4;`nCREATE USER 'mintapi'@'localhost' IDENTIFIED BY '$password';`nGRANT ALL PRIVILEGES ON mintapi.* TO 'mintapi'@'localhost';`nCREATE USER 'mintapi'@'127.0.0.1' IDENTIFIED BY '$password';`nGRANT ALL PRIVILEGES ON mintapi.* TO 'mintapi'@'127.0.0.1';`n"
[IO.File]::WriteAllText($init, $sql, [Text.UTF8Encoding]::new($false))
[IO.File]::AppendAllText($ini, "init-file=$($init.Replace('\','/'))`n")
[IO.File]::WriteAllText((Join-Path $root 'root-password.txt'), $rootPassword)
& $server --install MintAPIMySQL "--defaults-file=$ini"
if ($LASTEXITCODE) { throw 'MySQL service registration failed.' }
Start-Service MintAPIMySQL
# The presence of a TCP listener alone does not prove bootstrap completed. Wait for a real authenticated query.
$client = Join-Path $root 'server\bin\mysql.exe'
$clientIni = Join-Path $root 'bootstrap-client.ini'
[IO.File]::WriteAllText($clientIni, "[client]`nhost=127.0.0.1`nuser=root`npassword=$rootPassword`n")
$ready = $false
try {
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        & $client "--defaults-extra-file=$clientIni" --execute='SELECT 1' 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready=$true; break }
        Start-Sleep -Seconds 2
    }
    if (!$ready) { throw 'MySQL did not become ready; protected bootstrap files retained.' }
    Stop-Service MintAPIMySQL
    [IO.File]::WriteAllText($ini, $config, [Text.UTF8Encoding]::new($false))
    Remove-Item $init
    Start-Service MintAPIMySQL
} finally { Remove-Item $clientIni -ErrorAction SilentlyContinue }
