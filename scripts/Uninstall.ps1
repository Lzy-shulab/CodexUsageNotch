[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$localAppDataRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
$installRoot = [IO.Path]::GetFullPath((Join-Path $localAppDataRoot 'CodexUsageNotch'))
$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupDirectory 'Codex Usage Notch.lnk'

if (-not $installRoot.StartsWith($localAppDataRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($installRoot) -ne 'CodexUsageNotch')
{
    throw '安装目录校验失败，未执行卸载。'
}

Get-Process CodexUsageNotch -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force

if (Test-Path -LiteralPath $shortcutPath)
{
    Remove-Item -LiteralPath $shortcutPath -Force
}

if (Test-Path -LiteralPath $installRoot)
{
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}

Write-Host 'Codex 额度浮窗已卸载。'
