[CmdletBinding()]
param(
    [string]$PayloadRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\CodexUsageNotch'),
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$resolvedPayload = (Resolve-Path -LiteralPath $PayloadRoot).Path
$installRoot = Join-Path $env:LOCALAPPDATA 'CodexUsageNotch'
$executable = Join-Path $installRoot 'CodexUsageNotch.exe'
$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupDirectory 'Codex Usage Notch.lnk'

Get-Process CodexUsageNotch -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Copy-Item -Path (Join-Path $resolvedPayload '*') -Destination $installRoot -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $installRoot
$shortcut.Description = 'Codex 额度浮窗'
$shortcut.Save()

if (-not $NoStart)
{
    Start-Process -FilePath $executable -WorkingDirectory $installRoot -WindowStyle Hidden
}

Write-Host "已安装到：$installRoot"
Write-Host "已创建开机启动项：$shortcutPath"
