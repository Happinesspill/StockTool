# Kill and/or start StockTool Release build.
# Usage:
#   .\Restart-Release.ps1 -Action Kill
#   .\Restart-Release.ps1 -Action Start
#   .\Restart-Release.ps1 -Action Restart
#
# Note: Release builds call in-process MSBuild tasks instead of this script
# (avoids PowerShell cold-start). Keep this for manual use.
param(
    [ValidateSet('Kill', 'Start', 'Restart')]
    [string]$Action = 'Restart',

    [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $ExePath) {
    $ExePath = Join-Path $repoRoot 'bin\Release\StockTool.exe'
}

$exeFull = [System.IO.Path]::GetFullPath($ExePath)
$processName = [System.IO.Path]::GetFileNameWithoutExtension($exeFull)

function Get-ReleaseProcesses {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq $exeFull)
        } catch {
            $false
        }
    }
}

function Stop-ReleaseInstance {
    $procs = @(Get-ReleaseProcesses)
    if ($procs.Count -eq 0) {
        Write-Host '[Restart-Release] No running Release instance.'
        return
    }

    foreach ($p in $procs) {
        Write-Host "[Restart-Release] Stopping PID $($p.Id): $($p.Path)"
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }

    try {
        Wait-Process -Id ($procs.Id) -Timeout 10 -ErrorAction SilentlyContinue
    } catch {
    }

    if (@(Get-ReleaseProcesses).Count -gt 0) {
        Write-Warning '[Restart-Release] Timed out waiting for process exit; build may fail if the exe is still locked.'
        return
    }

    # Brief pause so file handles are fully released before MSBuild overwrites the exe.
    Start-Sleep -Milliseconds 200
    Write-Host '[Restart-Release] Release instance stopped.'
}

function Start-ReleaseInstance {
    if (-not (Test-Path -LiteralPath $exeFull)) {
        throw "Exe not found: $exeFull"
    }

    Write-Host "[Restart-Release] Starting $exeFull"
    Start-Process -FilePath $exeFull -WorkingDirectory (Split-Path $exeFull -Parent)
}

switch ($Action) {
    'Kill' { Stop-ReleaseInstance }
    'Start' { Start-ReleaseInstance }
    'Restart' {
        Stop-ReleaseInstance
        Start-ReleaseInstance
    }
}
