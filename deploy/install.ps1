$ErrorActionPreference = 'Stop'
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$admin) { throw '请以管理员身份运行 PowerShell，然后执行 install.ps1。' }
if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw '此安装包仅支持 Windows x64。' }
$os = Get-CimInstance Win32_OperatingSystem
if (($os.ProductType -eq 1 -and [int]$os.BuildNumber -lt 22000) -or ($os.ProductType -ne 1 -and [int]$os.BuildNumber -lt 17763)) { throw '需要 Windows 11 或 Windows Server 2019 以上版本。' }
$app = Join-Path $PSScriptRoot 'app'
if (!(Test-Path "$app\MintAPI.exe")) { throw '请使用 Release 预编译包。' }
$state = Join-Path $env:ProgramData 'MintAPI'
New-Item -ItemType Directory $state -Force | Out-Null
& icacls.exe $state /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' | Out-Null
if ($LASTEXITCODE) { throw 'Failed to protect configuration directory.' }
$runtimePath = Join-Path $app 'deployment\vc_redist.x64.exe'
if (Test-Path $runtimePath) {
    $runtime = Start-Process $runtimePath -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
    if ($runtime.ExitCode -notin @(0,1638,3010)) { throw 'VC 运行库安装失败。' }
}
& "$app\MintAPI.exe" --self-test
if ($LASTEXITCODE) { throw '发布包自检失败。' }
$config = Join-Path $state 'appsettings.Production.json'
& "$app\MintAPI.exe" --setup --config $config
if ($LASTEXITCODE) { throw '配置未完成。' }
$service = Get-Service MintAPI -ErrorAction SilentlyContinue
if ($service) { Stop-Service MintAPI }
$release = Join-Path $env:ProgramFiles ('MintAPI\releases\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmss') + '-' + $PID)
New-Item -ItemType Directory $release -Force | Out-Null
Copy-Item "$app\*" $release -Recurse -Force
# Per-service environment, without changing global environment variables.
$bin = '"' + (Join-Path $release 'MintAPI.exe') + '" --contentRoot "' + $release + '"'
if ($service) {
    & sc.exe config MintAPI binPath= $bin start= auto obj= 'NT AUTHORITY\LocalService'
} else {
    New-Service -Name MintAPI -DisplayName MintAPI -BinaryPathName $bin -StartupType Automatic | Out-Null
    & sc.exe config MintAPI obj= 'NT AUTHORITY\LocalService'
}
if ($LASTEXITCODE) { throw '服务注册失败。' }
$serviceKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\MintAPI'
New-ItemProperty -Path $serviceKey -Name Environment -PropertyType MultiString -Value @('ASPNETCORE_ENVIRONMENT=Production',"MINTAPI_CONFIG_FILE=$config") -Force | Out-Null
& sc.exe failure MintAPI reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
Start-Service MintAPI
& "$release\MintAPI.exe" --wait-ready --config $config
if ($LASTEXITCODE) { throw '服务未就绪；配置和旧版本已保留，请检查应用日志。' }
Write-Host "安装完成。配置/API Key：$config；服务：Get-Service MintAPI"
