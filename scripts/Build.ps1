[CmdletBinding()]
param([switch]$Portable)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $workspaceRoot 'CodexUsageNotch.slnx'
$project = Join-Path $workspaceRoot 'CodexUsageNotch\CodexUsageNotch.csproj'
$checks = Join-Path $workspaceRoot 'CodexUsageNotch.Tests\CodexUsageNotch.Tests.csproj'
$publishRoot = Join-Path $workspaceRoot 'dist\CodexUsageNotch'
if ($Portable) {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $version = $projectXml.Project.PropertyGroup.Version
    $publishRoot = Join-Path $workspaceRoot "dist\CodexUsageNotch-$version-win-x64"
}

dotnet build $solution -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release 编译失败。' }

dotnet run --project $checks -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw '功能自检失败。' }

if ($Portable) {
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishRoot `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false
} else {
    dotnet publish $project -c Release -r win-x64 --self-contained false -o $publishRoot
}
if ($LASTEXITCODE -ne 0) { throw '发布失败。' }

if ($Portable) {
    foreach ($plan in @('Pro', 'Plus')) {
        foreach ($theme in @('Dark', 'Light')) {
            $demoArgument = if ($plan -eq 'Plus') { '--demo=plus' } else { '--demo' }
            $command = '@echo off' + "`r`n" +
                'start "" "%~dp0CodexUsageNotch.exe" --preview ' + $demoArgument +
                ' --theme=' + $theme.ToLowerInvariant() + "`r`n"
            Set-Content -LiteralPath (Join-Path $publishRoot "Preview-$plan-$theme.cmd") `
                -Value $command -Encoding ascii -NoNewline
            $imageName = "$($plan.ToLowerInvariant())-$($theme.ToLowerInvariant()).png"
            Copy-Item -LiteralPath (Join-Path $workspaceRoot "docs\images\$imageName") `
                -Destination $publishRoot -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs\使用说明.txt') -Destination $publishRoot -Force
    $archivePath = Join-Path $workspaceRoot "dist\CodexUsageNotch-$version-win-x64.zip"
    $packageFiles = @('CodexUsageNotch.exe', '使用说明.txt') +
        @(Get-ChildItem -LiteralPath $publishRoot -File | Where-Object {
            $_.Name -match '^Preview-(Pro|Plus)-(Dark|Light)\.cmd$|^(pro|plus)-(dark|light)\.png$'
        } | Select-Object -ExpandProperty Name)
    Compress-Archive -LiteralPath @($packageFiles | ForEach-Object { Join-Path $publishRoot $_ }) `
        -DestinationPath $archivePath -Force
    Write-Host "便携包：$archivePath"
}

Write-Host "发布完成：$publishRoot"
