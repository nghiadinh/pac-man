<#
.SYNOPSIS
    Starts the Pac-Man 1v1 backend and frontend together.

.DESCRIPTION
    Launches the ASP.NET Core match server and the Vite dev server, waits until both are actually
    answering, then opens two browser windows - one per player, since the game needs two clients
    to start a match at all.

    Press Ctrl+C to stop both.

.PARAMETER NoBrowser
    Start the servers without opening browser windows.

.PARAMETER BackendUrl
    Where the match server listens. Must match the CORS origin allowed in Program.cs if changed.

.PARAMETER FrontendUrl
    Where the Vite dev server listens.

.PARAMETER Windowed
    Run each server in its own visible console window instead of streaming both into this one.

.PARAMETER Stop
    Kill whatever is holding the backend and frontend ports, then exit. Use this if a previous run
    was force-killed and left servers behind - Ctrl+C cleans up, but a hard kill cannot.

.EXAMPLE
    .\run.ps1
    Start everything and open two browser windows.

.EXAMPLE
    .\run.ps1 -NoBrowser -Windowed
    Start both servers in separate console windows, no browsers.

.EXAMPLE
    .\run.ps1 -Stop
    Free the ports after an unclean shutdown.
#>
[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$Windowed,
    [switch]$Stop,
    [string]$BackendUrl = 'http://localhost:5080',
    [string]$FrontendUrl = 'http://localhost:5173'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$processes = @()

function Write-Step($message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Write-Ok($message) {
    Write-Host "    $message" -ForegroundColor Green
}

function Write-Warn($message) {
    Write-Host "    $message" -ForegroundColor Yellow
}

function Test-Command($name) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    return $null -ne $cmd
}

# Waits for a URL to answer. The servers take a few seconds to warm up, and opening a browser
# before they do just shows the user an error page.
function Wait-ForUrl {
    param(
        [string]$Url,
        [string]$Name,
        [int]$TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Ok "$Name is up at $Url"
                return $true
            }
        }
        catch {
            # Not listening yet - that is the normal case while it boots.
        }

        Start-Sleep -Milliseconds 500
    }

    Write-Warn "$Name did not respond at $Url within $TimeoutSeconds seconds"
    return $false
}

function Start-Server {
    param(
        [string]$Name,
        [string]$FilePath,
        [string]$Arguments,
        [string]$WorkingDirectory
    )

    Write-Step "Starting $Name"

    $startArgs = @{
        FilePath         = $FilePath
        ArgumentList     = $Arguments
        WorkingDirectory = $WorkingDirectory
        PassThru         = $true
    }

    if ($Windowed) {
        # Each server gets its own console, which makes their logs easy to read separately.
        $startArgs.WindowStyle = 'Normal'
    }
    else {
        $startArgs.NoNewWindow = $true
    }

    return Start-Process @startArgs
}

function Stop-Servers {
    Write-Host ''
    Write-Step 'Shutting down'

    foreach ($proc in $script:processes) {
        if ($null -eq $proc) { continue }

        try {
            if (-not $proc.HasExited) {
                # Kill the whole tree: `dotnet run` and `npm run dev` both spawn children that
                # keep the ports bound if only the parent is stopped.
                & taskkill /PID $proc.Id /T /F 2>&1 | Out-Null
                Write-Ok "stopped PID $($proc.Id)"
            }
        }
        catch {
            Write-Warn "could not stop PID $($proc.Id): $($_.Exception.Message)"
        }
    }
}

function Get-PortFromUrl([string]$Url) {
    return ([uri]$Url).Port
}

# Finds the PIDs listening on a port. Used both to clean up after an unclean shutdown and to fail
# fast with a useful message rather than letting a server bind-fail somewhere in its own logs.
function Get-ListenerPids([int]$Port) {
    $pids = @()

    try {
        $connections = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop
        $pids = @($connections | Select-Object -ExpandProperty OwningProcess -Unique)
    }
    catch {
        # Get-NetTCPConnection is missing on some systems; netstat is the portable fallback.
        $lines = netstat -ano -p TCP | Select-String ":$Port\s+.*LISTENING"
        foreach ($line in $lines) {
            $fields = ($line.ToString() -split '\s+') | Where-Object { $_ }
            if ($fields.Count -gt 0) { $pids += $fields[-1] }
        }
        $pids = @($pids | Sort-Object -Unique)
    }

    return $pids
}

function Stop-Port([int]$Port, [string]$Label) {
    $pids = Get-ListenerPids -Port $Port

    if ($pids.Count -eq 0) {
        Write-Ok "$Label port $Port is already free"
        return
    }

    foreach ($processId in $pids) {
        # A PID can disappear between listing and killing - a child that exited when its parent
        # did, for instance. Gone is the outcome we wanted, so do not report it as a failure.
        if (-not (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
            continue
        }

        $output = & taskkill /PID $processId /T /F 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "$Label port ${Port}: killed PID $processId"
        }
        else {
            Write-Warn "$Label port ${Port}: could not kill PID ${processId} - $output"
        }
    }
}

# ---- -Stop -------------------------------------------------------------------------

if ($Stop) {
    Write-Step 'Freeing ports'
    Stop-Port -Port (Get-PortFromUrl $BackendUrl) -Label 'backend'
    Stop-Port -Port (Get-PortFromUrl $FrontendUrl) -Label 'frontend'
    Write-Host ''
    Write-Host '  Ports released.' -ForegroundColor Green
    return
}

# ---- prerequisites -----------------------------------------------------------------

Write-Step 'Checking prerequisites'

if (-not (Test-Command 'dotnet')) {
    throw 'dotnet was not found on PATH. Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
}
if (-not (Test-Command 'node')) {
    throw 'node was not found on PATH. Install Node.js 20 or newer: https://nodejs.org'
}
if (-not (Test-Command 'npm')) {
    throw 'npm was not found on PATH. It ships with Node.js.'
}

Write-Ok ".NET  $(dotnet --version)"
Write-Ok "Node  $(node --version)"

# A previous run that was force-killed leaves its servers bound. Say so plainly instead of letting
# the new ones fail to bind and bury the reason in their own output.
$busy = @()
foreach ($pair in @(
        @{ Port = (Get-PortFromUrl $BackendUrl); Name = 'backend' },
        @{ Port = (Get-PortFromUrl $FrontendUrl); Name = 'frontend' })) {

    if ((Get-ListenerPids -Port $pair.Port).Count -gt 0) {
        $busy += "$($pair.Name) port $($pair.Port)"
    }
}

if ($busy.Count -gt 0) {
    # Built in one piece: with `+` the -f operator would bind to the trailing fragment only and
    # leave the placeholder unsubstituted.
    $message = "Already in use: $($busy -join ', '). Something is still running - probably a " +
        "previous run that was force-killed. Run '.\run.ps1 -Stop' to free the ports, then retry."
    throw $message
}

if (-not (Test-Path (Join-Path $root 'frontend/node_modules'))) {
    Write-Step 'Installing frontend dependencies (first run only)'
    Push-Location (Join-Path $root 'frontend')
    try {
        npm install --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
    }
    finally {
        Pop-Location
    }
}

# ---- run ---------------------------------------------------------------------------

try {
    # Ctrl+C should take the servers down with it rather than orphaning them on their ports.
    # Registration can fail when the host has no console (redirected output, some CI agents);
    # the finally block below still cleans up, so this is a nicety rather than a requirement.
    try {
        Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action { Stop-Servers } | Out-Null
    }
    catch {
        Write-Warn 'could not hook the exit event; Ctrl+C cleanup may be less tidy'
    }

    $processes += Start-Server `
        -Name 'backend (ASP.NET Core + SignalR)' `
        -FilePath 'dotnet' `
        -Arguments "run --project `"$root\backend\src\MatchServer`" --urls $BackendUrl" `
        -WorkingDirectory $root

    $processes += Start-Server `
        -Name 'frontend (Vite)' `
        -FilePath 'npm.cmd' `
        -Arguments 'run dev' `
        -WorkingDirectory (Join-Path $root 'frontend')

    Write-Step 'Waiting for servers'
    $backendUp = Wait-ForUrl -Url "$BackendUrl/health" -Name 'Backend'
    $frontendUp = Wait-ForUrl -Url $FrontendUrl -Name 'Frontend'

    if (-not ($backendUp -and $frontendUp)) {
        throw 'One or both servers failed to start. Check the output above.'
    }

    if (-not $NoBrowser) {
        # Two windows, because a match cannot start until both roles are filled. The pause lets
        # the first client register as Runner before the second arrives - role assignment is
        # first-come-first-served, so simultaneous joins can race.
        Write-Step 'Opening two browser windows (Pac-Man, then Ghost)'
        Start-Process $FrontendUrl
        Start-Sleep -Seconds 2
        Start-Process $FrontendUrl
    }

    Write-Host ''
    Write-Host '  Pac-Man 1v1 is running' -ForegroundColor Green
    Write-Host "    frontend  $FrontendUrl"
    Write-Host "    backend   $BackendUrl"
    Write-Host ''
    Write-Host '  Click "Join match" in the FIRST window, wait for "Waiting for opponent",'
    Write-Host '  then click it in the second. Arrow keys or WASD to move.'
    Write-Host ''
    Write-Host '  Press Ctrl+C to stop.' -ForegroundColor DarkGray
    Write-Host ''

    # Hold the script open, and bail out early if either server dies on its own.
    while ($true) {
        Start-Sleep -Seconds 1

        foreach ($proc in $processes) {
            if ($proc.HasExited) {
                Write-Warn "a server exited unexpectedly (PID $($proc.Id), code $($proc.ExitCode))"
                throw 'Server stopped.'
            }
        }
    }
}
finally {
    Stop-Servers
}
