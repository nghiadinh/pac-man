<#
.SYNOPSIS
    Runs the Playwright end-to-end suite.

.DESCRIPTION
    Playwright starts and stops the backend and frontend itself, so this does not need run.ps1.
    If those servers are already running (from run.ps1), it reuses them.

    Default is headed, because the usual reason to run this by hand rather than in CI is to watch
    two browsers actually play a match.

.PARAMETER Headless
    Run without visible browser windows. Faster, and what CI uses.

.PARAMETER Spec
    Run a single spec instead of the whole suite: core-match-loop, frightened-state,
    vision-and-camping, or edge-cases.

.PARAMETER Ui
    Open Playwright's interactive UI mode - step through a test, inspect the DOM, time-travel.

.PARAMETER Report
    Open the HTML report from the last run and exit without running anything.

.EXAMPLE
    .\run-e2e.ps1
    Watch the full suite play out in visible browsers (~12 minutes).

.EXAMPLE
    .\run-e2e.ps1 -Spec core-match-loop
    Watch just the core match loop (~3 minutes) - the most gameplay per minute.

.EXAMPLE
    .\run-e2e.ps1 -Headless
    Run everything quietly.
#>
[CmdletBinding()]
param(
    [switch]$Headless,
    [switch]$Ui,
    [switch]$Report,
    [ValidateSet('core-match-loop', 'frightened-state', 'vision-and-camping', 'edge-cases')]
    [string]$Spec
)

$ErrorActionPreference = 'Stop'
$e2e = Join-Path $PSScriptRoot 'e2e'

function Write-Step($message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

if (-not (Get-Command 'node' -ErrorAction SilentlyContinue)) {
    throw 'node was not found on PATH. Install Node.js 20 or newer: https://nodejs.org'
}
if (-not (Get-Command 'dotnet' -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found on PATH. Playwright needs it to start the backend.'
}

Push-Location $e2e
try {
    if ($Report) {
        Write-Step 'Opening the report from the last run'
        npx playwright show-report
        return
    }

    if (-not (Test-Path (Join-Path $e2e 'node_modules'))) {
        Write-Step 'Installing Playwright (first run only)'
        npm install --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }

        Write-Step 'Downloading the Chromium build Playwright uses'
        npx playwright install chromium
    }

    $arguments = @('playwright', 'test')

    if ($Spec) {
        $arguments += "tests/$Spec.spec.ts"
    }

    if ($Ui) {
        $arguments += '--ui'
    }
    elseif (-not $Headless) {
        $arguments += '--headed'
    }

    if (-not $Ui) {
        $arguments += @('--reporter=list')
    }

    Write-Step "Running: npx $($arguments -join ' ')"
    Write-Host ''

    if (-not ($Headless -or $Ui)) {
        # Worth saying up front: the suite drives real matches through real servers, so it is
        # slow by nature, and browser windows opening and closing is the expected behaviour.
        Write-Host '  Browser windows will open and close as tests run. Each test plays a real' -ForegroundColor DarkGray
        Write-Host '  match against the real backend, so the whole suite takes ~12 minutes.' -ForegroundColor DarkGray
        Write-Host '  Use -Spec core-match-loop for a shorter look, or -Headless to skip the show.' -ForegroundColor DarkGray
        Write-Host ''
    }

    npx @arguments
    $exitCode = $LASTEXITCODE

    Write-Host ''
    if ($exitCode -eq 0) {
        Write-Host '  All end-to-end tests passed.' -ForegroundColor Green
    }
    else {
        Write-Host '  Some tests failed. Screenshots, video, and traces are in e2e/test-results.' -ForegroundColor Yellow
        Write-Host '  Run .\run-e2e.ps1 -Report to browse them.' -ForegroundColor Yellow
    }

    exit $exitCode
}
finally {
    Pop-Location
}
