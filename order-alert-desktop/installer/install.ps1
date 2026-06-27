param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-p]{32}$')]
    [string]$ExtensionId,
    [switch]$EnableAutoStart
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$InstallRoot = Join-Path $env:LOCALAPPDATA 'Programs\OrderAlert'
$AppRoot = Join-Path $InstallRoot 'app'
$HostRoot = Join-Path $InstallRoot 'host'
$ExtensionRoot = Join-Path $AppRoot 'extension'

New-Item -ItemType Directory -Force $AppRoot, $HostRoot | Out-Null
dotnet publish (Join-Path $RepoRoot 'order-alert-desktop\src\OrderAlert.App\OrderAlert.App.csproj') `
    -c Release -r win-x64 --self-contained false -o $AppRoot
dotnet publish (Join-Path $RepoRoot 'order-alert-desktop\src\OrderAlert.NativeHost\OrderAlert.NativeHost.csproj') `
    -c Release -r win-x64 --self-contained false -o $HostRoot

if (Test-Path $ExtensionRoot) {
    Remove-Item -LiteralPath $ExtensionRoot -Recurse -Force
}
New-Item -ItemType Directory -Force $ExtensionRoot | Out-Null
Copy-Item (Join-Path $RepoRoot 'browser-plugin\*') $ExtensionRoot -Recurse -Force `
    -Exclude 'node_modules', 'tests', 'package-lock.json', 'package.json'

$HostPath = (Join-Path $HostRoot 'OrderAlert.NativeHost.exe')
$ManifestPath = Join-Path $InstallRoot 'com.orderalert.native-host.json'
$Template = Get-Content (Join-Path $PSScriptRoot 'com.orderalert.native-host.json') -Raw
$RenderedManifest = $Template.Replace(
    'ORDER_ALERT_NATIVE_HOST_PATH',
    $HostPath.Replace('\', '\\')).Replace(
    'ORDER_ALERT_EXTENSION_ID',
    $ExtensionId)
$RenderedManifest | Set-Content -LiteralPath $ManifestPath -Encoding utf8

$NativeKey = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.orderalert.native_host'
New-Item -Path $NativeKey -Force | Out-Null
Set-Item -Path $NativeKey -Value $ManifestPath

$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($EnableAutoStart) {
    New-Item -Path $RunKey -Force | Out-Null
    Set-ItemProperty -Path $RunKey -Name 'OrderAlert' `
        -Value ('"{0}"' -f (Join-Path $AppRoot 'OrderAlert.App.exe'))
}

Write-Host "安装完成：$InstallRoot"
Write-Host '请在 Chrome 扩展管理页加载 app\extension，并确认扩展 ID 与安装参数一致。'
