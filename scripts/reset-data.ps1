#!/usr/bin/env pwsh
# ---------------------------------------------------------------------------
# reset-data.ps1 — Clear pre-reset leaderboard history and legacy puzzle files
#                  from the Azure Files share used by the Container App.
#
# Prerequisites:
#   - Azure CLI installed and logged in (az login)
#   - Access to the resource group rg-svensktkorsord
#
# Usage:
#   ./scripts/reset-data.ps1                   # Dry-run (shows what would be deleted)
#   ./scripts/reset-data.ps1 -Confirm          # Actually delete files
#   ./scripts/reset-data.ps1 -Confirm -Verbose # Delete with detailed output
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [switch]$Confirm,

    [string]$ResourceGroup = 'rg-svensktkorsord',
    [string]$AppName       = 'svensktkorsord',

    # Data before this date is considered stale and will be removed.
    [string]$CutoffDate    = '2026-05-14'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 1. Resolve the storage account name from the Bicep deployment outputs
# ---------------------------------------------------------------------------
Write-Host "`n=== Resolving storage account ===" -ForegroundColor Cyan

$storageAccountName = az storage account list `
    --resource-group $ResourceGroup `
    --query "[?starts_with(name, '$($AppName.Replace('-',''))') || starts_with(name, 'svensktkorsord')].name | [0]" `
    --output tsv

if (-not $storageAccountName) {
    Write-Error "Could not find a storage account in resource group '$ResourceGroup'."
    exit 1
}
Write-Host "  Storage account: $storageAccountName"

$shareName = 'crossword-data'

# Get a storage account key for file share operations
$accountKey = az storage account keys list `
    --resource-group $ResourceGroup `
    --account-name $storageAccountName `
    --query '[0].value' --output tsv

$storageArgs = @(
    '--account-name', $storageAccountName
    '--account-key',  $accountKey
    '--share-name',   $shareName
)

# ---------------------------------------------------------------------------
# 2. Delete leaderboard history files before the cutoff date
# ---------------------------------------------------------------------------
Write-Host "`n=== Leaderboard history cleanup (cutoff: $CutoffDate) ===" -ForegroundColor Cyan

$historyFiles = az storage file list @storageArgs `
    --path 'leaderboard/history' `
    --query '[].name' --output tsv 2>$null

if ($historyFiles) {
    $toDelete = $historyFiles | Where-Object {
        # File names are like "2025-12-31.json" — extract the date part
        $_ -match '^(\d{4}-\d{2}-\d{2})\.json$' -and $Matches[1] -lt $CutoffDate
    }

    if ($toDelete) {
        Write-Host "  Found $($toDelete.Count) history file(s) to remove:"
        foreach ($file in $toDelete) {
            Write-Host "    - leaderboard/history/$file" -ForegroundColor Yellow
            if ($Confirm) {
                az storage file delete @storageArgs --path "leaderboard/history/$file" | Out-Null
                Write-Verbose "    Deleted leaderboard/history/$file"
            }
        }
    } else {
        Write-Host "  No history files older than $CutoffDate."
    }
} else {
    Write-Host "  No history directory found (or empty)."
}

# ---------------------------------------------------------------------------
# 3. Clear the current leaderboard (current.json) — reset to empty
# ---------------------------------------------------------------------------
Write-Host "`n=== Current leaderboard reset ===" -ForegroundColor Cyan

$currentExists = az storage file exists @storageArgs `
    --path 'leaderboard/current.json' `
    --query 'exists' --output tsv 2>$null

if ($currentExists -eq 'true') {
    Write-Host "  leaderboard/current.json exists — will reset to empty."
    if ($Confirm) {
        $emptyJson = '{}' | Out-File -FilePath "$env:TEMP\empty-leaderboard.json" -Encoding utf8 -Force
        az storage file upload @storageArgs `
            --source "$env:TEMP\empty-leaderboard.json" `
            --path 'leaderboard/current.json' | Out-Null
        Remove-Item "$env:TEMP\empty-leaderboard.json" -Force
        Write-Host "  Reset leaderboard/current.json to {}" -ForegroundColor Green
    }
} else {
    Write-Host "  No current.json found — nothing to reset."
}

# ---------------------------------------------------------------------------
# 4. Delete legacy puzzle files (old naming conventions)
#    - puzzle-{date}.json        (legacy 17x17)
#    - puzzle-{date}-small.json  (legacy 10x10)
# ---------------------------------------------------------------------------
Write-Host "`n=== Legacy puzzle file cleanup ===" -ForegroundColor Cyan

$puzzleFiles = az storage file list @storageArgs `
    --path 'puzzles' `
    --query '[].name' --output tsv 2>$null

if ($puzzleFiles) {
    # Legacy files before cutoff: puzzle-YYYY-MM-DD.json (no size suffix) and puzzle-YYYY-MM-DD-small.json
    $legacyFiles = $puzzleFiles | Where-Object {
        ($_ -match '^puzzle-(\d{4}-\d{2}-\d{2})\.json$' -or
         $_ -match '^puzzle-(\d{4}-\d{2}-\d{2})-small\.json$') -and $Matches[1] -lt $CutoffDate
    }

    # Also remove any new-format puzzles from before the cutoff date
    $oldSizedFiles = $puzzleFiles | Where-Object {
        $_ -match '^puzzle-(\d{4}-\d{2}-\d{2})-\d+x\d+\.json$' -and $Matches[1] -lt $CutoffDate
    }

    $allLegacy = @($legacyFiles) + @($oldSizedFiles) | Sort-Object -Unique

    if ($allLegacy) {
        Write-Host "  Found $($allLegacy.Count) legacy/old puzzle file(s) to remove:"
        foreach ($file in $allLegacy) {
            Write-Host "    - puzzles/$file" -ForegroundColor Yellow
            if ($Confirm) {
                az storage file delete @storageArgs --path "puzzles/$file" | Out-Null
                Write-Verbose "    Deleted puzzles/$file"
            }
        }
    } else {
        Write-Host "  No legacy puzzle files found."
    }
} else {
    Write-Host "  No puzzles directory found (or empty)."
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
if (-not $Confirm) {
    Write-Host "DRY RUN — no files were deleted. Re-run with -Confirm to apply." -ForegroundColor Magenta
} else {
    Write-Host "Cleanup complete!" -ForegroundColor Green
    Write-Host "The Container App will regenerate fresh puzzles on next startup." -ForegroundColor Green
}
Write-Host ""
