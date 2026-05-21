[CmdletBinding()]
param(
    [string]$ServerUrl = "http://localhost:5261",
    [string]$WebUrl = "http://localhost:5241",
    [string]$ApiBaseUrl = $ServerUrl,
    [int]$ReadyTimeoutSeconds = 60,
    [switch]$SkipNpmInstall,
    [switch]$SkipClientBuild,
    [switch]$OpenBrowser
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
    Write-Host "[dev-start] $Message"
}

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Test-RunningProcess {
    param([int]$ProcessId)

    if ($ProcessId -le 0) {
        return $false
    }

    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Wait-HttpReady {
    param(
        [string]$Name,
        [string]$Url,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
            if ([int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 500) {
                Write-Step "$Name is ready at $Url."
                return
            }
        } catch {
            $statusCode = $null
            if ($_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }

            if ($null -ne $statusCode -and $statusCode -ge 200 -and $statusCode -lt 500) {
                Write-Step "$Name is ready at $Url (HTTP $statusCode)."
                return
            }

            $lastError = $_.Exception.Message
        }

        Start-Sleep -Milliseconds 500
    }

    $message = "$Name did not become ready at $Url within $TimeoutSeconds seconds."
    if ($lastError) {
        $message = "$message Last error: $lastError"
    }

    throw $message
}

function Read-State {
    param([string]$StateFile)

    if (-not (Test-Path -LiteralPath $StateFile)) {
        return @()
    }

    $state = Get-Content -LiteralPath $StateFile -Encoding utf8 -Raw | ConvertFrom-Json
    return @($state.services)
}

function Escape-SingleQuoted {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

function Start-DevService {
    param(
        [string]$Name,
        [string]$ProjectPath,
        [string]$Url,
        [hashtable]$ExtraEnvironment,
        [string]$RuntimeDir,
        [string]$LogsDir,
        [string]$RepoRoot
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $stdoutLog = Join-Path $LogsDir "$Name-$timestamp.out.log"
    $stderrLog = Join-Path $LogsDir "$Name-$timestamp.err.log"
    $runnerPath = Join-Path $RuntimeDir "$Name.run.ps1"

    $envLines = @(
        "`$env:ASPNETCORE_ENVIRONMENT = 'Development'",
        "`$env:ASPNETCORE_URLS = '$(Escape-SingleQuoted $Url)'"
    )

    foreach ($key in $ExtraEnvironment.Keys) {
        $envLines += "`$env:$key = '$(Escape-SingleQuoted ([string]$ExtraEnvironment[$key]))'"
    }

    $runner = @"
`$ErrorActionPreference = 'Stop'
Set-Location '$(Escape-SingleQuoted $RepoRoot)'
$($envLines -join [Environment]::NewLine)
& dotnet run --no-launch-profile --project '$(Escape-SingleQuoted $ProjectPath)'
"@

    Set-Content -LiteralPath $runnerPath -Value $runner -Encoding utf8

    $process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $runnerPath) `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -WindowStyle Hidden `
        -PassThru

    [pscustomobject]@{
        name = $Name
        pid = $process.Id
        url = $Url
        stdoutLog = $stdoutLog
        stderrLog = $stderrLog
        runner = $runnerPath
        startedAt = (Get-Date).ToString("o")
    }
}

$repoRoot = Get-RepoRoot
$clientApp = Join-Path $repoRoot "src\LightRAGNet.Web\ClientApp"
$runtimeDir = Join-Path $repoRoot "artifacts\dev-runtime"
$logsDir = Join-Path $runtimeDir "logs"
$stateFile = Join-Path $runtimeDir "dev-services.json"

New-Item -ItemType Directory -Path $runtimeDir, $logsDir -Force | Out-Null

Write-Step "Repo: $repoRoot"

Push-Location $clientApp
try {
    if (-not $SkipNpmInstall) {
        if (-not (Test-Path -LiteralPath (Join-Path $clientApp "node_modules"))) {
            Write-Step "Installing React workbench packages..."
            npm install
        } else {
            Write-Step "React workbench packages already installed. Use -SkipNpmInstall to skip this check explicitly."
        }
    }

    if (-not $SkipClientBuild) {
        Write-Step "Building React graph workbench..."
        npm run build
    }
} finally {
    Pop-Location
}

$existingServices = Read-State $stateFile
$services = @()

foreach ($service in $existingServices) {
    if (Test-RunningProcess ([int]$service.pid)) {
        Write-Step "$($service.name) is already running at $($service.url) (PID $($service.pid))."
        $services += $service
    }
}

if (-not ($services | Where-Object { $_.name -eq "server" })) {
    Write-Step "Starting LightRAGNet.Server on $ServerUrl..."
    $services += Start-DevService `
        -Name "server" `
        -ProjectPath (Join-Path $repoRoot "src\LightRAGNet.Server") `
        -Url $ServerUrl `
        -ExtraEnvironment @{} `
        -RuntimeDir $runtimeDir `
        -LogsDir $logsDir `
        -RepoRoot $repoRoot
}

if (-not ($services | Where-Object { $_.name -eq "web" })) {
    Write-Step "Starting LightRAGNet.Web on $WebUrl..."
    $services += Start-DevService `
        -Name "web" `
        -ProjectPath (Join-Path $repoRoot "src\LightRAGNet.Web") `
        -Url $WebUrl `
        -ExtraEnvironment @{ ApiBaseUrl = $ApiBaseUrl } `
        -RuntimeDir $runtimeDir `
        -LogsDir $logsDir `
        -RepoRoot $repoRoot
}

$state = [pscustomobject]@{
    repoRoot = $repoRoot
    stateFile = $stateFile
    services = $services
}

$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stateFile -Encoding utf8

Wait-HttpReady "Server" $ServerUrl $ReadyTimeoutSeconds
Wait-HttpReady "Web" "$WebUrl/graph-view" $ReadyTimeoutSeconds

Write-Host ""
Write-Step "Development services are ready."
Write-Host "  Server: $ServerUrl"
Write-Host "  Web:    $WebUrl"
Write-Host "  Graph:  $WebUrl/graph-view"
Write-Host "  Logs:   $logsDir"
Write-Host ""
Write-Host "Stop with:"
Write-Host "  .\scripts\dev-stop.ps1"

if ($OpenBrowser) {
    Start-Process $WebUrl
}
