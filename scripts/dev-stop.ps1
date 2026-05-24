[CmdletBinding()]
param(
    [switch]$CleanLogs
)

$ErrorActionPreference = "Stop"

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [Console]::OutputEncoding
} catch {
    # Some restricted hosts do not allow changing console encoding.
}

function Write-Step {
    param([string]$Message)
    Write-Host "[dev-stop] $Message"
}

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Stop-ProcessTree {
    param([int]$ProcessId)

    if ($ProcessId -le 0) {
        return
    }

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }

    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue
    foreach ($child in $children) {
        Stop-ProcessTree -ProcessId ([int]$child.ProcessId)
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

$repoRoot = Get-RepoRoot
$runtimeDir = Join-Path $repoRoot "artifacts\dev-runtime"
$logsDir = Join-Path $runtimeDir "logs"
$stateFile = Join-Path $runtimeDir "dev-services.json"

if (-not (Test-Path -LiteralPath $stateFile)) {
    Write-Step "No dev service state file found. Nothing to stop."
    return
}

$state = Get-Content -LiteralPath $stateFile -Encoding utf8 -Raw | ConvertFrom-Json
$services = @($state.services)

foreach ($service in $services) {
    $servicePid = [int]$service.pid
    $isExternal = $false
    if ($null -ne $service.PSObject.Properties["external"]) {
        $isExternal = [bool]$service.external
    }

    if ($servicePid -gt 0 -and (Get-Process -Id $servicePid -ErrorAction SilentlyContinue)) {
        Write-Step "Stopping $($service.name) (PID $servicePid)..."
        Stop-ProcessTree -ProcessId $servicePid
    } elseif ($servicePid -le 0 -and $isExternal) {
        Write-Step "$($service.name) is externally managed at $($service.url); skipping stop."
    } elseif ($servicePid -le 0) {
        Write-Step "$($service.name) has no tracked PID; skipping stop."
    } else {
        Write-Step "$($service.name) is already stopped (PID $servicePid)."
    }
}

Remove-Item -LiteralPath $stateFile -Force -ErrorAction SilentlyContinue

Get-ChildItem -LiteralPath $runtimeDir -Filter "*.run.ps1" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

if ($CleanLogs -and (Test-Path -LiteralPath $logsDir)) {
    $resolvedRuntime = (Resolve-Path -LiteralPath $runtimeDir).Path
    $resolvedLogs = (Resolve-Path -LiteralPath $logsDir).Path

    if (-not $resolvedLogs.StartsWith($resolvedRuntime, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean logs outside runtime directory: $resolvedLogs"
    }

    Get-ChildItem -LiteralPath $logsDir -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Step "Development services stopped."
