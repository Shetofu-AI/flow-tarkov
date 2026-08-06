#Requires -Version 7

[CmdletBinding()]
param(
    [string] $PluginsPath,
    [switch] $NoRestart
)

$ErrorActionPreference = 'Stop'

$PROJECT_PATH = Join-Path $PSScriptRoot 'Flow.Launcher.Plugin.Tarkov\Flow.Launcher.Plugin.Tarkov.csproj'
$PLUGIN_JSON = Join-Path $PSScriptRoot 'Flow.Launcher.Plugin.Tarkov\plugin.json'
$PUBLISH_PATH = Join-Path $PSScriptRoot 'out'
$PORTABLE_ROOT = 'D:\PortableApps\Portable FlowLauncher'
$FLOW_PROCESS = 'Flow.Launcher'

function Write-Log
{
    param([string] $Message)

    $stamp = (Get-Date).ToString('HH:mm:ss')
    Write-Host "[$stamp] $Message"
}

function Resolve-PluginsPath
{
    param([string] $Explicit)

    if ($Explicit)
    {
        return $Explicit
    }

    $running = Get-Process -Name $FLOW_PROCESS -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($running -and $running.Path)
    {
        return Join-Path (Split-Path $running.Path -Parent) 'UserData\Plugins'
    }

    $portable = Get-ChildItem $PORTABLE_ROOT -Directory -Filter 'app-*' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if ($portable)
    {
        return Join-Path $portable.FullName 'UserData\Plugins'
    }

    $roaming = Join-Path $env:APPDATA 'FlowLauncher\Plugins'
    if (Test-Path -LiteralPath $roaming)
    {
        return $roaming
    }

    throw 'Не нашёл папку плагинов Flow Launcher. Укажи её явно: -PluginsPath "...\UserData\Plugins"'
}

function Stop-Flow
{
    $running = Get-Process -Name $FLOW_PROCESS -ErrorAction SilentlyContinue
    if (-not $running)
    {
        return $null
    }

    $executable = $running[0].Path
    Write-Log 'Закрываю Flow Launcher'
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800

    return $executable
}

$version = (Get-Content $PLUGIN_JSON -Raw | ConvertFrom-Json).Version
$target = Join-Path (Resolve-PluginsPath -Explicit $PluginsPath) "Tarkov-$version"

Write-Log "Собираю версию $version"
dotnet publish $PROJECT_PATH -c Release -o $PUBLISH_PATH --nologo | Out-Null
if ($LASTEXITCODE -ne 0)
{
    throw 'Сборка не удалась'
}

$executable = Stop-Flow

if (Test-Path -LiteralPath $target)
{
    Remove-Item -LiteralPath $target -Recurse -Force
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item "$PUBLISH_PATH\*" $target -Recurse -Force
Write-Log "Плагин установлен: $target"

if ($executable -and -not $NoRestart)
{
    Start-Process -FilePath $executable
    Write-Log 'Flow Launcher запущен обратно'
}
