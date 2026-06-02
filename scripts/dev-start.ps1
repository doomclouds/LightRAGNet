[CmdletBinding()]
param(
    [ValidateSet("All", "Server", "Service", "React")]
    [string]$Target = "All",
    [string]$ServerUrl = "http://localhost:5261",
    [string]$ReactUrl = "http://127.0.0.1:5173",
    [string]$ApiBaseUrl = $ServerUrl,
    [int]$ReadyTimeoutSeconds = 60,
    [switch]$SkipNpmInstall,
    [switch]$SkipClientBuild,
    [switch]$OpenBrowser,
    [switch]$Foreground,
    [switch]$Worker
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

function Test-StandaloneReactDevServer {
    param([string]$Url)

    try {
        $navigationSource = Invoke-WebRequest -Uri "$Url/src/app/navigation.ts" -UseBasicParsing -TimeoutSec 3
        $sourceText = [string]$navigationSource.Content

        return $sourceText.Contains("RAG Chat") `
            -and $sourceText.Contains("/graph-view") `
            -and $sourceText.Contains("/system-status") `
            -and $sourceText.Contains("/cache-management") `
            -and $sourceText.Contains("/document-preview")
    } catch {
        return $false
    }
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

function Test-HttpReady {
    param([string]$Url)

    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        return [int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 500
    } catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }

        return $null -ne $statusCode -and $statusCode -ge 200 -and $statusCode -lt 500
    }
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

function Start-ReactService {
    param(
        [string]$Name,
        [string]$AppPath,
        [string]$Url,
        [string]$ApiBaseUrl,
        [string]$RuntimeDir,
        [string]$LogsDir
    )

    $uri = [Uri]$Url
    $hostName = $uri.Host
    $port = $uri.Port
    if ($port -le 0) {
        throw "ReactUrl must include an explicit port: $Url"
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $stdoutLog = Join-Path $LogsDir "$Name-$timestamp.out.log"
    $stderrLog = Join-Path $LogsDir "$Name-$timestamp.err.log"
    $runnerPath = Join-Path $RuntimeDir "$Name.run.ps1"
    $vitePath = Join-Path $AppPath "node_modules\.bin\vite.cmd"

    $runner = @"
`$ErrorActionPreference = 'Stop'
Set-Location '$(Escape-SingleQuoted $AppPath)'
`$env:VITE_LIGHTRAG_API_BASE = '$(Escape-SingleQuoted $ApiBaseUrl)'
& '$(Escape-SingleQuoted $vitePath)' --host '$(Escape-SingleQuoted $hostName)' --port $port
"@

    Set-Content -LiteralPath $runnerPath -Value $runner -Encoding utf8

    $process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $runnerPath) `
        -WorkingDirectory $AppPath `
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

function New-ExternalServiceState {
    param(
        [string]$Name,
        [string]$Url,
        [int]$ProcessId = 0,
        [bool]$External = $true
    )

    [pscustomobject]@{
        name = $Name
        pid = $ProcessId
        url = $Url
        stdoutLog = $null
        stderrLog = $null
        runner = $null
        external = $External
        startedAt = $null
    }
}

function Find-DevRunnerProcessId {
    param(
        [string]$Name,
        [string]$RuntimeDir
    )

    $runnerPath = Join-Path $RuntimeDir "$Name.run.ps1"
    $process = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*$runnerPath*" } |
        Sort-Object ProcessId |
        Select-Object -First 1

    if ($null -eq $process) {
        return 0
    }

    return [int]$process.ProcessId
}

function New-WorkerArgumentList {
    param([hashtable]$BoundParameters)

    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath, "-Worker")
    $parameterNames = @(
        "Target",
        "ServerUrl",
        "ReactUrl",
        "ApiBaseUrl",
        "ReadyTimeoutSeconds",
        "SkipNpmInstall",
        "SkipClientBuild",
        "OpenBrowser"
    )

    foreach ($parameterName in $parameterNames) {
        if (-not $BoundParameters.ContainsKey($parameterName)) {
            continue
        }

        $value = $BoundParameters[$parameterName]
        if ($value -is [System.Management.Automation.SwitchParameter]) {
            if ($value.IsPresent) {
                $arguments += "-$parameterName"
            }

            continue
        }

        $arguments += "-$parameterName"
        $arguments += [string]$value
    }

    return $arguments
}

$repoRoot = Get-RepoRoot
$reactApp = Join-Path $repoRoot "src\LightRAGNet.React"
$runtimeDir = Join-Path $repoRoot "artifacts\dev-runtime"
$logsDir = Join-Path $runtimeDir "logs"
$stateFile = Join-Path $runtimeDir "dev-services.json"
$workerStateFile = Join-Path $runtimeDir "dev-start-worker.json"

New-Item -ItemType Directory -Path $runtimeDir, $logsDir -Force | Out-Null

if (-not $Foreground -and -not $Worker) {
    if (Test-Path -LiteralPath $workerStateFile) {
        $workerState = Get-Content -LiteralPath $workerStateFile -Encoding utf8 -Raw | ConvertFrom-Json
        $workerPid = [int]$workerState.pid
        if (Test-RunningProcess $workerPid) {
            Write-Step "Development starter is already running in background (PID $workerPid)."
            Write-Host "  Logs: $($workerState.stdoutLog)"
            Write-Host "        $($workerState.stderrLog)"
            Write-Host "Stop with:"
            Write-Host "  .\scripts\dev-stop.ps1"
            return
        }

        Remove-Item -LiteralPath $workerStateFile -Force -ErrorAction SilentlyContinue
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $stdoutLog = Join-Path $logsDir "dev-start-$timestamp.out.log"
    $stderrLog = Join-Path $logsDir "dev-start-$timestamp.err.log"
    $workerArguments = New-WorkerArgumentList -BoundParameters $PSBoundParameters

    $workerProcess = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList $workerArguments `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -WindowStyle Hidden `
        -PassThru

    [pscustomobject]@{
        repoRoot = $repoRoot
        pid = $workerProcess.Id
        stdoutLog = $stdoutLog
        stderrLog = $stderrLog
        startedAt = (Get-Date).ToString("o")
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $workerStateFile -Encoding utf8

    Write-Step "Development starter is running in background (PID $($workerProcess.Id))."
    Write-Host "  Logs:   $stdoutLog"
    Write-Host "          $stderrLog"
    Write-Host "  State:  $stateFile"
    Write-Host ""
    Write-Host "Stop with:"
    Write-Host "  .\scripts\dev-stop.ps1"
    Write-Host ""
    Write-Host "Run in the current console for diagnostics:"
    Write-Host "  .\scripts\dev-start.ps1 -Foreground"
    return
}

Write-Step "Repo: $repoRoot"
if ($Target -eq "Service") {
    $Target = "Server"
}

$wantsServer = $Target -eq "All" -or $Target -eq "Server"
$wantsReact = $Target -eq "All" -or $Target -eq "React"

$existingServices = Read-State $stateFile
$services = @()
$knownServiceNames = @("server", "react")
$desiredServiceNames = @()
if ($wantsServer) {
    $desiredServiceNames += "server"
}
if ($wantsReact) {
    $desiredServiceNames += "react"
}

foreach ($service in $existingServices) {
    if ($desiredServiceNames -contains ([string]$service.name) -and (Test-RunningProcess ([int]$service.pid))) {
        Write-Step "$($service.name) is already running at $($service.url) (PID $($service.pid))."
        $services += $service
    } elseif ($knownServiceNames -contains ([string]$service.name) -and (Test-RunningProcess ([int]$service.pid))) {
        Write-Step "Preserving existing $($service.name) state at $($service.url) (PID $($service.pid))."
        $services += $service
    } elseif (Test-RunningProcess ([int]$service.pid)) {
        Write-Step "Ignoring stale dev service state for $($service.name) at $($service.url) (PID $($service.pid))."
    }
}

if ($wantsServer -and -not ($services | Where-Object { $_.name -eq "server" })) {
    if (Test-HttpReady $ServerUrl) {
        $serverPid = Find-DevRunnerProcessId -Name "server" -RuntimeDir $runtimeDir
        if ($serverPid -gt 0) {
            Write-Step "Server is already responding at $ServerUrl; reusing runner PID $serverPid."
            $services += New-ExternalServiceState -Name "server" -Url $ServerUrl -ProcessId $serverPid -External $false
        } else {
            Write-Step "Server is already responding at $ServerUrl; reusing it without starting a new process."
            $services += New-ExternalServiceState -Name "server" -Url $ServerUrl
        }
    }
}

if ($wantsReact -and -not ($services | Where-Object { $_.name -eq "react" })) {
    if (Test-HttpReady "$ReactUrl/documents") {
        if (-not (Test-StandaloneReactDevServer $ReactUrl)) {
            throw "React is already responding at $ReactUrl, but it does not match the standalone LightRAGNet.React app. Stop the existing dev server or pass a different -ReactUrl."
        }

        $reactPid = Find-DevRunnerProcessId -Name "react" -RuntimeDir $runtimeDir
        if ($reactPid -gt 0) {
            Write-Step "React is already responding at $ReactUrl; reusing runner PID $reactPid."
            $services += New-ExternalServiceState -Name "react" -Url $ReactUrl -ProcessId $reactPid -External $false
        } else {
            Write-Step "React is already responding at $ReactUrl; reusing it without starting a new process."
            $services += New-ExternalServiceState -Name "react" -Url $ReactUrl
        }
    }
}

if ($wantsReact -and -not ($services | Where-Object { $_.name -eq "react" })) {
    Push-Location $reactApp
    try {
        if (-not $SkipNpmInstall) {
            if (-not (Test-Path -LiteralPath (Join-Path $reactApp "node_modules"))) {
                Write-Step "Installing standalone React packages..."
                npm install
            } else {
                Write-Step "Standalone React packages already installed. Use -SkipNpmInstall to skip this check explicitly."
            }
        }

        if (-not $SkipClientBuild) {
            Write-Step "Building standalone React frontend..."
            npm run build
        }
    } finally {
        Pop-Location
    }
}

if ($wantsServer -and -not ($services | Where-Object { $_.name -eq "server" })) {
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

if ($wantsReact -and -not ($services | Where-Object { $_.name -eq "react" })) {
    Write-Step "Starting LightRAGNet.React on $ReactUrl..."
    $services += Start-ReactService `
        -Name "react" `
        -AppPath $reactApp `
        -Url $ReactUrl `
        -ApiBaseUrl $ApiBaseUrl `
        -RuntimeDir $runtimeDir `
        -LogsDir $logsDir
}

$state = [pscustomobject]@{
    repoRoot = $repoRoot
    stateFile = $stateFile
    services = $services
}

$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stateFile -Encoding utf8

if ($wantsServer) {
    Wait-HttpReady "Server" $ServerUrl $ReadyTimeoutSeconds
}
if ($wantsReact) {
    Wait-HttpReady "React" "$ReactUrl/documents" $ReadyTimeoutSeconds
}

Write-Host ""
Write-Step "Development services are ready."
if ($wantsServer) {
    Write-Host "  Server: $ServerUrl"
}
if ($wantsReact) {
    Write-Host "  React:"
    Write-Host "    $ReactUrl/"
    Write-Host "    $ReactUrl/rag-chat"
    Write-Host "    $ReactUrl/documents"
    Write-Host "    $ReactUrl/documents/upload"
    Write-Host "    $ReactUrl/document-preview"
    Write-Host "    $ReactUrl/graph-view"
    Write-Host "    $ReactUrl/system-status"
    Write-Host "    $ReactUrl/cache-management"
}
Write-Host "  Logs:   $logsDir"
Write-Host ""
Write-Host "Stop with:"
Write-Host "  .\scripts\dev-stop.ps1"

if ($OpenBrowser -and $wantsReact) {
    Start-Process "$ReactUrl/documents"
}

if ($Worker) {
    Remove-Item -LiteralPath $workerStateFile -Force -ErrorAction SilentlyContinue
}
