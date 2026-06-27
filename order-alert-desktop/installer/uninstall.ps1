param([switch]$RemoveUserData)

$ErrorActionPreference = 'Stop'
$InstallRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'Programs\OrderAlert'))
$ProgramsRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'Programs'))
if (-not $InstallRoot.StartsWith($ProgramsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝删除预期目录之外的路径：$InstallRoot"
}

Get-Process 'OrderAlert.App', 'OrderAlert.NativeHost' -ErrorAction SilentlyContinue |
    Stop-Process -Force
Remove-Item 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.orderalert.native_host' `
    -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'OrderAlert' -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}

if ($RemoveUserData) {
    $DataRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'OrderAlert'))
    $ExpectedRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
    if (-not $DataRoot.StartsWith($ExpectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝删除预期目录之外的路径：$DataRoot"
    }
    if (Test-Path -LiteralPath $DataRoot) {
        Remove-Item -LiteralPath $DataRoot -Recurse -Force
    }
}

Write-Host '订单临期预警已卸载。'
