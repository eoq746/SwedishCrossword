#!/usr/bin/env pwsh
# ---------------------------------------------------------------------------
# reset-data.ps1 — Clear pre-cutoff leaderboard scores and history from the
#                  Azure SQL database, and old puzzle files from the Azure Files
#                  share used by the Container App.
#
# Prerequisites:
#   - Azure CLI installed and logged in (az login)
#   - Access to the resource group rg-svensktkorsord
#   - SqlServer PowerShell module (Install-Module SqlServer)
#   - Your Entra identity must be a contained database user with db_owner.
#     Run scripts/setup-sql-user.ps1 once from Azure Cloud Shell to set this up.
#
# Usage:
#   ./scripts/reset-data.ps1                                          # Dry-run (shows row counts)
#   ./scripts/reset-data.ps1 -Confirm                                 # Actually delete rows
#   ./scripts/reset-data.ps1 -StartDate 2025-01-01 -EndDate 2025-06-01 # Custom range
#   ./scripts/reset-data.ps1 -Confirm -Verbose                        # Delete with detailed output
#
# Date range:
#   -StartDate  Inclusive start of the deletion window (rows on this date ARE deleted).
#   -EndDate    Inclusive end   of the deletion window (rows on this date ARE deleted).
#   Both dates use the format YYYY-MM-DD.
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [switch]$Confirm,

    [string]$ResourceGroup = 'rg-svensktkorsord',
    [string]$AppName       = 'svensktkorsord',

    # Inclusive start of the deletion window (rows on this date ARE deleted).
    [string]$StartDate     = '2000-01-01',

    # Inclusive end of the deletion window (rows on this date ARE deleted).
    [string]$EndDate       = '2026-05-14'
)

$ErrorActionPreference = 'Stop'

# Ensure the SqlServer module is available
if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    Write-Error "The 'SqlServer' PowerShell module is required. Install it with: Install-Module SqlServer -Scope CurrentUser"
    exit 1
}
Import-Module SqlServer -ErrorAction Stop

# ---------------------------------------------------------------------------
# 1. Resolve the SQL Server and database name
# ---------------------------------------------------------------------------
Write-Host "`n=== Resolving Azure SQL server ===" -ForegroundColor Cyan

$sqlServer = az sql server list `
    --resource-group $ResourceGroup `
    --query "[?contains(name, '$AppName')].name | [0]" `
    --output tsv

if (-not $sqlServer) {
    Write-Error "Could not find a SQL server in resource group '$ResourceGroup'."
    exit 1
}
Write-Host "  SQL server: $sqlServer"

$sqlDb = az sql db list `
    --resource-group $ResourceGroup `
    --server $sqlServer `
    --query "[?name != 'master'].name | [0]" `
    --output tsv

if (-not $sqlDb) {
    Write-Error "Could not find a user database on server '$sqlServer'."
    exit 1
}
Write-Host "  Database:   $sqlDb"

$serverFqdn = "$sqlServer.database.windows.net"

# ---------------------------------------------------------------------------
# 2. Add a temporary firewall rule for the caller's public IP
# ---------------------------------------------------------------------------
Write-Host "`n=== Adding temporary firewall rule ===" -ForegroundColor Cyan
$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org')
$firewallRuleName = "reset-data-script-$([guid]::NewGuid().ToString('N').Substring(0,8))"
az sql server firewall-rule create `
    --resource-group $ResourceGroup `
    --server $sqlServer `
    --name $firewallRuleName `
    --start-ip-address $myIp `
    --end-ip-address $myIp `
    --output none
Write-Host "  Added rule '$firewallRuleName' for IP $myIp"
Write-Host "  Waiting for firewall rule to propagate..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Get an Entra access token for Azure SQL
Write-Host "`n=== Acquiring Entra access token ===" -ForegroundColor Cyan
$tokenJson = az account get-access-token --resource https://database.windows.net/ --output json | ConvertFrom-Json
$accessToken = $tokenJson.accessToken
Write-Host "  Token acquired"

# Helper: run a SQL query using Invoke-Sqlcmd with Entra token auth
function Invoke-Sql {
    param([string]$Query)
    Invoke-Sqlcmd -ServerInstance $serverFqdn -Database $sqlDb -AccessToken $accessToken -Query $Query
}

# Wrap all work in try/finally so the firewall rule is always cleaned up
try {
    # Convert date range to Unix timestamps in milliseconds.
    # StartDate: beginning of that day (inclusive). EndDate: end of that day (inclusive).
    $startUnix = ([long]([DateTimeOffset]::Parse("${StartDate}T00:00:00Z")).ToUnixTimeSeconds()) * 1000
    $endUnix   = ([long]([DateTimeOffset]::Parse("${EndDate}T00:00:00Z")).ToUnixTimeSeconds() + 86400) * 1000

    # ---------------------------------------------------------------------------
    # 3. Delete history rows before the cutoff date
    # ---------------------------------------------------------------------------
    Write-Host "`n=== History cleanup (inclusive range: $StartDate to $EndDate) ===" -ForegroundColor Cyan

    $historyResult = Invoke-Sql "SELECT COUNT(*) AS cnt FROM history WHERE date >= '$StartDate' AND date <= '$EndDate'"
    $historyRows = [int]$historyResult.cnt
    Write-Host "  Rows to delete from [history]: $historyRows"

    if ($historyRows -gt 0 -and $Confirm) {
        Invoke-Sql "DELETE FROM history WHERE date >= '$StartDate' AND date <= '$EndDate'" | Out-Null
        Write-Host "  Deleted $historyRows row(s) from [history]" -ForegroundColor Green
    }

    # ---------------------------------------------------------------------------
    # 4. Delete scores with timestamps before the cutoff date
    # ---------------------------------------------------------------------------
    Write-Host "`n=== Scores cleanup (inclusive range: $StartDate to $EndDate, timestamps: $startUnix to $endUnix) ===" -ForegroundColor Cyan

    $scoresResult = Invoke-Sql "SELECT COUNT(*) AS cnt FROM scores WHERE timestamp >= $startUnix AND timestamp < $endUnix"
    $scoresRows = [int]$scoresResult.cnt
    Write-Host "  Rows to delete from [scores]: $scoresRows"

    if ($scoresRows -gt 0 -and $Confirm) {
        Invoke-Sql "DELETE FROM scores WHERE timestamp >= $startUnix AND timestamp < $endUnix" | Out-Null
        Write-Host "  Deleted $scoresRows row(s) from [scores]" -ForegroundColor Green
    }

    # ---------------------------------------------------------------------------
    # 5. Summary of remaining data
    # ---------------------------------------------------------------------------
    Write-Host "`n=== Current row counts ===" -ForegroundColor Cyan

    $tables = @('scores', 'history', 'user_aliases', 'friend_requests')
    foreach ($table in $tables) {
        $row = Invoke-Sql "SELECT COUNT(*) AS cnt FROM $table"
        Write-Host "  [$table]: $($row.cnt) row(s)"
    }
}
finally {
    # ---------------------------------------------------------------------------
    # Always remove the temporary firewall rule
    # ---------------------------------------------------------------------------
    Write-Host "`n=== Removing temporary firewall rule ===" -ForegroundColor Cyan
    az sql server firewall-rule delete `
        --resource-group $ResourceGroup `
        --server $sqlServer `
        --name $firewallRuleName `
        --output none 2>$null
    Write-Host "  Removed rule '$firewallRuleName'"
}

# ---------------------------------------------------------------------------
# 6. Delete old puzzle files from the Azure Files share
# ---------------------------------------------------------------------------
Write-Host "`n=== Resolving storage account ===" -ForegroundColor Cyan

$storageAccountName = az storage account list `
    --resource-group $ResourceGroup `
    --query "[?starts_with(name, '$($AppName.Replace('-',''))') || starts_with(name, 'svensktkorsord')].name | [0]" `
    --output tsv

if (-not $storageAccountName) {
    Write-Warning "Could not find a storage account in resource group '$ResourceGroup'. Skipping puzzle cleanup."
} else {
    Write-Host "  Storage account: $storageAccountName"

    $shareName = 'crossword-data'
    $accountKey = az storage account keys list `
        --resource-group $ResourceGroup `
        --account-name $storageAccountName `
        --query '[0].value' --output tsv

    $storageArgs = @(
        '--account-name', $storageAccountName
        '--account-key',  $accountKey
        '--share-name',   $shareName
    )

    Write-Host "`n=== Puzzle file cleanup (inclusive range: $StartDate to $EndDate) ===" -ForegroundColor Cyan

    $puzzleFiles = az storage file list @storageArgs `
        --path 'puzzles' `
        --query '[].name' --output tsv 2>$null

    if ($puzzleFiles) {
        # Legacy files: puzzle-YYYY-MM-DD.json and puzzle-YYYY-MM-DD-small.json
        # Legacy files: puzzle-YYYY-MM-DD.json and puzzle-YYYY-MM-DD-small.json
        $legacyFiles = $puzzleFiles | Where-Object {
            ($_ -match '^puzzle-(\d{4}-\d{2}-\d{2})\.json$' -or
             $_ -match '^puzzle-(\d{4}-\d{2}-\d{2})-small\.json$') -and $Matches[1] -ge $StartDate -and $Matches[1] -le $EndDate
        }

        # New-format puzzles in range: puzzle-YYYY-MM-DD-NxN.json
        $oldSizedFiles = $puzzleFiles | Where-Object {
            $_ -match '^puzzle-(\d{4}-\d{2}-\d{2})-\d+x\d+\.json$' -and $Matches[1] -ge $StartDate -and $Matches[1] -le $EndDate
        }

        $allOld = @($legacyFiles) + @($oldSizedFiles) | Sort-Object -Unique

        if ($allOld) {
            Write-Host "  Found $($allOld.Count) old puzzle file(s) to remove:"
            foreach ($file in $allOld) {
                Write-Host "    - puzzles/$file" -ForegroundColor Yellow
                if ($Confirm) {
                    az storage file delete @storageArgs --path "puzzles/$file" | Out-Null
                    Write-Verbose "    Deleted puzzles/$file"
                }
            }
        } else {
            Write-Host "  No old puzzle files found."
        }
    } else {
        Write-Host "  No puzzles directory found (or empty)."
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
if (-not $Confirm) {
    Write-Host "DRY RUN — no data was deleted. Re-run with -Confirm to apply." -ForegroundColor Magenta
} else {
    Write-Host "Cleanup complete!" -ForegroundColor Green
}
Write-Host ""
